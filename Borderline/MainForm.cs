namespace Borderline;

internal sealed class MainForm : Form
{
    private readonly Label _gpuLabel = new() { Left = 16, Top = 12, Width = 460, Height = 22, AutoEllipsis = true };
    private readonly Label _lblTop = new() { Text = "Top (px)", Left = 16, Top = 48, Width = 100, Height = 24 };
    private readonly Label _lblBottom = new() { Text = "Bottom (px)", Left = 16, Top = 84, Width = 100, Height = 24 };
    private readonly Label _lblLeft = new() { Text = "Left (px)", Left = 16, Top = 120, Width = 100, Height = 24 };
    private readonly Label _lblRight = new() { Text = "Right (px)", Left = 16, Top = 156, Width = 100, Height = 24 };
    private readonly NumericUpDown _top = CreateInput(130, 44);
    private readonly NumericUpDown _bottom = CreateInput(130, 80);
    private readonly NumericUpDown _left = CreateInput(130, 116);
    private readonly NumericUpDown _right = CreateInput(130, 152);
    private readonly Button _applyBtn = new() { Text = "Apply", Left = 16, Top = 200, Width = 100, Height = 34 };
    private readonly Button _resetBtn = new() { Text = "Reset", Left = 126, Top = 200, Width = 100, Height = 34 };
    private readonly Button _enableBtn = new() { Text = "Enable margins", Left = 236, Top = 200, Width = 130, Height = 34 };
    private readonly Label _statusLabel = new() { Left = 16, Top = 250, Width = 460, Height = 96 };

    private AppSettings _settings = AppSettings.Load();
    private bool _busy;

    public MainForm()
    {
        var version = typeof(MainForm).Assembly.GetName().Version?.ToString(3) ?? "?";
        Text = $"Borderline v{version}";
        Font = new Font("Segoe UI", 10f);
        ClientSize = new Size(500, 370);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;

        Controls.AddRange([
            _gpuLabel, _lblTop, _lblBottom, _lblLeft, _lblRight,
            _top, _bottom, _left, _right,
            _applyBtn, _resetBtn, _enableBtn, _statusLabel,
        ]);

        _applyBtn.Click += (_, _) => { _settings.Enabled = true; _ = ApplyAsync(); };
        _resetBtn.Click += (_, _) => ResetUi();
        _enableBtn.Click += (_, _) => { _settings.Enabled = !_settings.Enabled; _ = ApplyAsync(); };

        LoadSettingsToUi();
        UpdateEnableCaption();
        _statusLabel.Text = "Enter pixel margins, then click Apply.";

        Shown += OnShown;
        FormClosing += (_, _) =>
        {
            if (_settings.Enabled && !_busy)
            {
                try { DisplayService.Restore(); } catch { }
            }
        };
    }

    private static NumericUpDown CreateInput(int x, int y) =>
        new() { Left = x, Top = y, Width = 110, Height = 28, Minimum = 0, Maximum = 500 };

    private void LoadSettingsToUi()
    {
        _top.Value = Clamp(_settings.Top);
        _bottom.Value = Clamp(_settings.Bottom);
        _left.Value = Clamp(_settings.Left);
        _right.Value = Clamp(_settings.Right);
    }

    private AppSettings ReadFromUi() => new()
    {
        Top = (int)_top.Value,
        Bottom = (int)_bottom.Value,
        Left = (int)_left.Value,
        Right = (int)_right.Value,
        Enabled = _settings.Enabled,
    };

    private async Task ApplyAsync()
    {
        if (_busy) return;
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
        if (_settings.Enabled) { _settings.Enabled = false; _ = ApplyAsync(); }
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
        _top.Enabled = _bottom.Enabled = _left.Enabled = _right.Enabled = enabled;
        _applyBtn.Enabled = _resetBtn.Enabled = _enableBtn.Enabled = enabled;
    }

    private void SetStatus(string text) => _statusLabel.Text = text;

    private async void OnShown(object? sender, EventArgs e)
    {
        try { _gpuLabel.Text = await Task.Run(DisplayService.GpuLabel).ConfigureAwait(true); }
        catch (Exception ex) { _gpuLabel.Text = "GPU: unknown (" + ex.Message + ")"; }

        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(5000).ConfigureAwait(false);
                var progress = new Progress<string>(msg => BeginInvoke(() => SetStatus(msg)));
                await UpdateService.CheckAndInstallAsync(progress, CancellationToken.None).ConfigureAwait(false);
            }
            catch { }
        });
    }

    private static decimal Clamp(int v) => Math.Clamp(v, 0, 500);
}
