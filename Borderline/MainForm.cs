namespace Borderline;

internal sealed class MainForm : Form
{
    private readonly Label _gpuLabel = new() { AutoSize = false, Height = 20, Width = 380 };
    private readonly Label _statusLabel = new() { AutoSize = false, Height = 48, Width = 380 };
    private readonly NumericUpDown _top = CreateMarginInput();
    private readonly NumericUpDown _bottom = CreateMarginInput();
    private readonly NumericUpDown _left = CreateMarginInput();
    private readonly NumericUpDown _right = CreateMarginInput();
    private readonly Button _applyBtn = new() { Text = "Apply", Width = 90 };
    private readonly Button _resetBtn = new() { Text = "Reset", Width = 90 };
    private readonly Button _enableBtn = new() { Text = "Enable margins", Width = 120 };

    private AppSettings _settings = AppSettings.Load();
    private bool _busy;

    public MainForm()
    {
        var version = typeof(MainForm).Assembly.GetName().Version?.ToString(3) ?? "?";
        Text = $"Borderline v{version}";
        ClientSize = new Size(420, 320);
        FormBorderStyle = FormBorderStyle.FixedSingle;
        MaximizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(16),
            ColumnCount = 2,
            RowCount = 8,
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 120));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        layout.Controls.Add(_gpuLabel, 0, 0);
        layout.SetColumnSpan(_gpuLabel, 2);

        AddRow(layout, 1, "Top (px)", _top);
        AddRow(layout, 2, "Bottom (px)", _bottom);
        AddRow(layout, 3, "Left (px)", _left);
        AddRow(layout, 4, "Right (px)", _right);

        var buttons = new FlowLayoutPanel { FlowDirection = FlowDirection.LeftToRight, AutoSize = true, WrapContents = false };
        buttons.Controls.AddRange([_applyBtn, _resetBtn, _enableBtn]);
        layout.Controls.Add(buttons, 0, 5);
        layout.SetColumnSpan(buttons, 2);

        layout.Controls.Add(_statusLabel, 0, 6);
        layout.SetColumnSpan(_statusLabel, 2);

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

    private static NumericUpDown CreateMarginInput() =>
        new() { Minimum = 0, Maximum = 500, Width = 100 };

    private static void AddRow(TableLayoutPanel layout, int row, string label, Control input)
    {
        layout.Controls.Add(new Label { Text = label, AutoSize = true, Anchor = AnchorStyles.Left }, 0, row);
        layout.Controls.Add(input, 1, row);
    }

    private void LoadSettingsToUi()
    {
        _top.Value = Clamp(_settings.Top);
        _bottom.Value = Clamp(_settings.Bottom);
        _left.Value = Clamp(_settings.Left);
        _right.Value = Clamp(_settings.Right);
    }

    private AppSettings ReadFromUi()
    {
        return new AppSettings
        {
            Top = (int)_top.Value,
            Bottom = (int)_bottom.Value,
            Left = (int)_left.Value,
            Right = (int)_right.Value,
            Enabled = _settings.Enabled,
        };
    }

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
