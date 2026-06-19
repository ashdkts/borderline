namespace Borderline;

internal enum ApplyMethod
{
    None,
    AmdGpuScaling,
    AmdViewPort,
    AmdUnderscan,
    AmdOverscan,
    AmdDalRegistry,
    AmdTiming,
    Win32Custom,
}

internal static class AmdApply
{
    public static string Apply(AppSettings settings, out ApplyMethod method)
    {
        method = ApplyMethod.None;
        var log = new List<string> { $"Borderline apply {DateTime.Now:u}" };

        AmdAdlCore.PrepareForMargins();
        log.Add(AmdAdlCore.GetCapabilitySummary());

        var (ok, msg) = Try("gpu-scaling", () =>
        {
            var err = AmdGpuScaling.TryApply(
                settings.Top, settings.Bottom, settings.Left, settings.Right, out var m);
            return err is null ? (true, m!) : (false, err);
        }, log);
        if (ok)
        {
            method = ApplyMethod.AmdGpuScaling;
            ApplyLog.Write(log);
            return msg;
        }

        (ok, msg) = Try("viewport", () =>
        {
            var err = AmdAdlCore.TryApplyViewPort(settings.Top, settings.Bottom, settings.Left, settings.Right, out var m);
            return err is null ? (true, m!) : (false, err);
        }, log);
        if (ok)
        {
            method = ApplyMethod.AmdViewPort;
            ApplyLog.Write(log);
            return msg;
        }

        (ok, msg) = Try("underscan", () =>
        {
            var err = AmdAdlCore.TryApplyUnderscan(settings.Top, settings.Bottom, settings.Left, settings.Right, out var m);
            return err is null ? (true, m!) : (false, err);
        }, log);
        if (ok)
        {
            method = ApplyMethod.AmdUnderscan;
            ApplyLog.Write(log);
            return msg;
        }

        (ok, msg) = Try("overscan", () =>
        {
            var err = AmdAdlCore.TryApplyOverscan(settings.Top, settings.Bottom, settings.Left, settings.Right, out var m);
            return err is null ? (true, m!) : (false, err);
        }, log);
        if (ok)
        {
            method = ApplyMethod.AmdOverscan;
            ApplyLog.Write(log);
            return msg;
        }

        (ok, msg) = Try("registry", () =>
        {
            var err = AmdDalRegistry.TryApply(settings.Top, settings.Bottom, settings.Left, settings.Right, out var m);
            return err is null ? (true, m!) : (false, err);
        }, log);
        if (ok)
        {
            method = ApplyMethod.AmdDalRegistry;
            ApplyLog.Write(log);
            return msg;
        }

        (ok, msg) = Try("timing", () =>
        {
            var err = AmdAdl.TryApplyMargins(settings.Top, settings.Bottom, settings.Left, settings.Right, out var m);
            return err is null ? (true, m!) : (false, err);
        }, log);
        if (ok)
        {
            method = ApplyMethod.AmdTiming;
            ApplyLog.Write(log);
            return msg;
        }

        try
        {
            log.Add("[windows] trying custom resolution…");
            var custom = NativeDisplay.ApplyCustomMode(
                settings.Top, settings.Bottom, settings.Left, settings.Right, enableUnsafeModes: true);
            log.Add($"[windows] ok: {custom}");
            method = ApplyMethod.Win32Custom;
            ApplyLog.Write(log);
            return custom;
        }
        catch (Exception ex)
        {
            log.Add($"[windows] {ex.Message}");
            ApplyLog.Write(log);
            throw new InvalidOperationException(BuildFailureMessage(), ex);
        }
    }

    public static bool Restore(ApplyMethod method) => method switch
    {
        ApplyMethod.AmdGpuScaling => AmdGpuScaling.Restore(),
        ApplyMethod.AmdViewPort => AmdAdlCore.RestoreViewPort(),
        ApplyMethod.AmdUnderscan => AmdAdlCore.RestoreUnderscan(),
        ApplyMethod.AmdOverscan => AmdAdlCore.RestoreOverscan(),
        ApplyMethod.AmdDalRegistry => AmdDalRegistry.Restore(),
        ApplyMethod.AmdTiming => AmdAdl.Restore(),
        _ => false,
    };

    private static (bool ok, string message) Try(
        string name, Func<(bool ok, string message)> attempt, List<string> log)
    {
        log.Add($"[{name}] trying…");
        try
        {
            var result = attempt();
            log.Add(result.ok ? $"[{name}] ok: {result.message}" : $"[{name}] {result.message}");
            return result;
        }
        catch (Exception ex)
        {
            log.Add($"[{name}] {ex.Message}");
            return (false, ex.Message);
        }
    }

    private static string BuildFailureMessage() =>
        "Your Radeon iGPU rejected every margin path Borderline tried (viewport, underscan, GPU scaling + custom resolution, registry, timing).\r\n\r\n" +
        "This hardware/driver combo may not expose per-edge blanking. What often works on SER-class iGPUs:\r\n" +
        "1. AMD Software → Gaming → Display → GPU Scaling ON, Scaling Mode Centered\r\n" +
        "2. Windows Settings → Display → set a lower resolution (e.g. 1840×1000 on 1920×1080)\r\n" +
        "   — centered scaling leaves blank bars at the native panel size\r\n" +
        "3. Use equal margins on all sides in Borderline (asymmetric per-edge may not be supported)\r\n\r\n" +
        $"Send this file if you want help: {ApplyLog.LastPath}";
}

internal static class ApplyLog
{
    public static string LastPath { get; private set; } = "";

    public static void Write(IEnumerable<string> lines)
    {
        try
        {
            var dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "Borderline");
            Directory.CreateDirectory(dir);
            LastPath = Path.Combine(dir, "last-apply.log");
            File.WriteAllLines(LastPath, lines);
        }
        catch
        {
            // Non-fatal.
        }
    }
}
