namespace Borderline;

internal enum ApplyMethod
{
    None,
    AmdTiming,
    AmdUnderscan,
    Win32Custom,
}

internal static class DisplayService
{
    private static ApplyMethod _lastMethod;

    public static string GpuLabel()
    {
        return NativeDisplay.DetectVendor() switch
        {
            GpuVendor.Amd => "GPU: AMD Radeon (ADL timing override)",
            GpuVendor.Nvidia => "GPU: NVIDIA (custom resolution)",
            GpuVendor.Intel => "GPU: Intel (custom resolution)",
            _ => "GPU: generic (custom resolution)",
        };
    }

    public static string Apply(AppSettings settings)
    {
        if (!settings.Enabled || (settings.Top == 0 && settings.Bottom == 0 && settings.Left == 0 && settings.Right == 0))
        {
            return Restore();
        }

        if (NativeDisplay.DetectVendor() == GpuVendor.Amd)
        {
            var err = AmdAdl.TryApplyMargins(settings.Top, settings.Bottom, settings.Left, settings.Right, out var msg);
            if (err is null)
            {
                _lastMethod = ApplyMethod.AmdTiming;
                return msg!;
            }

            var underErr = AmdDisplay.TryApply(settings.Top, settings.Bottom, settings.Left, settings.Right, out var underMsg);
            if (underErr is null)
            {
                _lastMethod = ApplyMethod.AmdUnderscan;
                return underMsg!;
            }

            try
            {
                var custom = NativeDisplay.ApplyCustomMode(
                    settings.Top, settings.Bottom, settings.Left, settings.Right, enableUnsafeModes: true);
                _lastMethod = ApplyMethod.Win32Custom;
                return custom;
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException(
                    $"All AMD methods failed. Timing: {err}. Underscan: {underErr}. Windows: {ex.Message}", ex);
            }
        }

        var nvidia = NativeDisplay.DetectVendor() == GpuVendor.Nvidia;
        _lastMethod = ApplyMethod.Win32Custom;
        return NativeDisplay.ApplyCustomMode(settings.Top, settings.Bottom, settings.Left, settings.Right, nvidia);
    }

    public static string Restore()
    {
        switch (_lastMethod)
        {
            case ApplyMethod.AmdTiming:
                if (AmdAdl.Restore())
                {
                    _lastMethod = ApplyMethod.None;
                    return "AMD timing override restored.";
                }
                break;
            case ApplyMethod.AmdUnderscan:
                if (AmdDisplay.Restore())
                {
                    _lastMethod = ApplyMethod.None;
                    return "AMD underscan restored.";
                }
                break;
        }

        _lastMethod = ApplyMethod.None;
        return NativeDisplay.Restore();
    }
}
