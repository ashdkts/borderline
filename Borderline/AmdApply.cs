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
        var caps = AmdAdlCore.QueryCaps();
        log.Add($"capabilities: gpuScaling={(caps.GpuScaling ? "yes" : "no")} " +
                $"viewport={(caps.ViewPort ? "yes" : "no")} underscan={(caps.Underscan ? "yes" : "no")}");

        var (ok, msg) = caps.GpuScaling
            ? Try("gpu-scaling", () =>
            {
                var err = AmdGpuScaling.TryApply(
                    settings.Top, settings.Bottom, settings.Left, settings.Right, out var m);
                return err is null ? (true, m!) : (false, err);
            }, log)
            : Skip("gpu-scaling", "not supported on this display", log);
        if (ok)
        {
            method = ApplyMethod.AmdGpuScaling;
            ApplyLog.Write(log);
            return msg;
        }

        (ok, msg) = caps.ViewPort
            ? Try("viewport", () =>
            {
                var err = AmdAdlCore.TryApplyViewPort(
                    settings.Top, settings.Bottom, settings.Left, settings.Right, out var m);
                return err is null ? (true, m!) : (false, err);
            }, log)
            : Skip("viewport", "not supported on this adapter", log);
        if (ok)
        {
            method = ApplyMethod.AmdViewPort;
            ApplyLog.Write(log);
            return msg;
        }

        (ok, msg) = caps.Underscan
            ? Try("underscan", () =>
            {
                var err = AmdAdlCore.TryApplyUnderscan(
                    settings.Top, settings.Bottom, settings.Left, settings.Right, out var m);
                return err is null ? (true, m!) : (false, err);
            }, log)
            : Skip("underscan", "not supported on this display", log);
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

        if (!caps.GpuScaling)
        {
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
            }
        }
        else
        {
            log.Add("[windows] skipped (already attempted with GPU scaling path)");
        }

        ApplyLog.Write(log);
        CleanupAfterFailedApply();
        throw new InvalidOperationException(ApplyFailureHelp.FullMessage);
    }

    public static void CleanupAfterFailedApply()
    {
        try { AmdGpuScaling.Restore(); } catch { }
        try { AmdAdlCore.RestoreViewPort(); } catch { }
        try { AmdAdlCore.RestoreUnderscan(); } catch { }
        try { AmdAdlCore.RestoreOverscan(); } catch { }
        try { AmdDalRegistry.Restore(); } catch { }
        try { AmdAdl.Restore(); } catch { }
        try { NativeDisplay.Restore(); } catch { }
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

    private static (bool ok, string message) Skip(string name, string reason, List<string> log)
    {
        log.Add($"[{name}] skipped ({reason})");
        return (false, reason);
    }
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
