namespace Borderline;

internal sealed class MainForm : Form
{
    private readonly Label _gpuLabel = new() { AutoSize = true, MaximumSize = new Size(420, 0) };
    private readonly Label _statusLabel = new()
    {
        AutoSize = true,
        MaximumSize = new Size(420, 0),
        UseMnemonic = false,
    };
    private readonly NumericUpDown _top = CreateMarginInput();
    private readonly NumericUpDown _bottom = CreateMarginInput();
    private readonly NumericUpDown _left = CreateMarginInput();
    private readonly NumericUpDown _right = CreateMarginInput();
    private readonly Button _applyBtn = new() { Text = "Apply", Width = 96, Height = 32 };
    private readonly Button _resetBtn = new() { Text = "Reset", Width = 96, Height = 32 };
    private readonly Button _enableBtn = new() { Text = "Enable margins", Width = 130, Height = 32 };

    private AppSettings _settings = AppSettings.Load();
    private bool _busy;

    public MainForm()
    {
        var version = typeof(MainForm).Assembly.GetName().Version?.ToString(3) ?? "?";
        Text = $"Borderline v{version}";
        AutoScaleMode = AutoScaleMode.Font;
        Font = new Font("Segoe UI", 9.75f);
        MinimumSize = new Size(480, 360);
        ClientSize = new Size(480, 360);
        FormBorderStyle = FormBorderStyle.FixedSingle;
        MaximizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        Padding = new Padding(16);

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 130));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        AddRow(layout, 0, _gpuLabel, columnSpan: 2);
        AddRow(layout, 1, new Label { Text = "Top (px)", AutoSize = true, Anchor = AnchorStyles.Left }, _top);
        AddRow(layout, 2, new Label { Text = "Bottom (px)", AutoSize = true, Anchor = AnchorStyles.Left }, _bottom);
        AddRow(layout, 3, new Label { Text = "Left (px)", AutoSize = true, Anchor = AnchorStyles.Left }, _left);
        AddRow(layout, 4, new Label { Text = "Right (px)", AutoSize = true, Anchor = AnchorStyles.Left }, _right);

        var buttons = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.LeftToRight,
            AutoSize = true,
            WrapContents = false,
            Margin = new Padding(0, 8, 0, 8),
        };
        buttons.Controls.AddRange([_applyBtn, _resetBtn, _enableBtn]);
        AddRow(layout, 5, buttons, columnSpan: 2);

        AddRow(layout, 6, _statusLabel, columnSpan: 2);

        Controls.Add(layout);

        _applyBtn.Click += (_, _) => { _settings.Enabled = true; _ = ApplyAsync(); };
        _resetBtn.Click += (_, _) => ResetUi();
        _enableBtn.Click += (_, _) => { _settings.Enabled = !_settings.Enabled; _ = ApplyAsync(); };

        LoadSettingsToUi();
        UpdateEnableCaption();
        _statusLabel.Text = _settings.Enabled
            ? "Saved margins loaded. Click Apply to re-enable."
            : "Enter pixel margins, then click Apply.";

        Shown += OnShown;
        FormClosing += (_, _) =>
        {
            if (_settings.Enabled && !_busy)
            {
                try
                {
                    DisplayService.Restore();
                }
                catch
                {
                    // Best effort on exit.
                }
            }
        };
    }

    private static void AddRow(TableLayoutPanel layout, int row, Control a, Control? b = null, int columnSpan = 1)
    {
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.Controls.Add(a, 0, row);
        if (columnSpan > 1)
        {
            layout.SetColumnSpan(a, columnSpan);
        }
        else if (b is not null)
        {
            layout.Controls.Add(b, 1, row);
        }
    }

    private static NumericUpDown CreateMarginInput() =>
        new() { Minimum = 0, Maximum = 500, Width = 110, Height = 28 };

    private void LoadSettingsToUi()
    {
        _top.Value = Clamp(_settings.Top);
        _bottom.Value = Clamp(_settings.Bottom);
        _left.Value = Clamp(_settings.Left);
        _right.Value = Clamp(_settings.Right);
    }

    private AppSettings ReadFromUi() =>
        new()
        {
            Top = (int)_top.Value,
            Bottom = (int)_bottom.Value,
            Left = (int)_left.Value,
            Right = (int)_right.Value,
            Enabled = _settings.Enabled,
        };

    private async Task ApplyAsync()
    {
        if (_busy)
        {
            return;
        }

        _busy = true;
        SetUiEnabled(false);
        SetStatus("Applying driver settings…");

        _settings = ReadFromUi();

        try
        {
            var message = await Task.Run(() => DisplayService.Apply(_settings)).ConfigureAwait(true);
            _settings.Save();
            SetStatus($"{message}  [{(_settings.Enabled ? "ON" : "OFF")}]");
        }
        catch (Exception ex)
        {
            SetStatus("Error: " + ex.Message);
        }
        finally
        {
            _busy = false;
            SetUiEnabled(true);
            UpdateEnableCaption();
        }
    }

    private void ResetUi()
    {
        if (_settings.Enabled)
        {
            _settings.Enabled = false;
            _ = ApplyAsync();
        }

        _settings = AppSettings.Default;
        LoadSettingsToUi();
        _settings.Save();
        UpdateEnableCaption();
        SetStatus("Reset to defaults.");
    }

    private void UpdateEnableCaption() =>
        _enableBtn.Text = _settings.Enabled ? "Disable margins" : "Enable margins";

    private void SetUiEnabled(bool enabled)
    {
        _top.Enabled = enabled;
        _bottom.Enabled = enabled;
        _left.Enabled = enabled;
        _right.Enabled = enabled;
        _applyBtn.Enabled = enabled;
        _resetBtn.Enabled = enabled;
        _enableBtn.Enabled = enabled;
    }

    private void SetStatus(string text) => _statusLabel.Text = text;

    private async void OnShown(object? sender, EventArgs e)
    {
        try
        {
            _gpuLabel.Text = await Task.Run(DisplayService.GpuLabel).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            _gpuLabel.Text = "GPU: unknown (" + ex.Message + ")";
        }

        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(5000).ConfigureAwait(false);
                var progress = new Progress<string>(msg => BeginInvoke(() => SetStatus(msg)));
                await UpdateService.CheckAndInstallAsync(progress, CancellationToken.None).ConfigureAwait(false);
            }
            catch
            {
                // Updates are optional.
            }
        });
    }

    private static decimal Clamp(int value) => Math.Clamp(value, 0, 500);
}
