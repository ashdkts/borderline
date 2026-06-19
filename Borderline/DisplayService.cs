namespace Borderline;

internal enum ApplyMethod
{
    None,
    AmdAdl,
    Win32Custom,
}

internal static class DisplayService
{
    private static ApplyMethod _lastMethod;

    public static string GpuLabel()
    {
        return NativeDisplay.DetectVendor() switch
        {
            GpuVendor.Amd => "GPU: AMD (ADL if available, else custom resolution)",
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
            var amdError = AmdDisplay.TryApply(settings.Top, settings.Bottom, settings.Left, settings.Right, out var amdMsg);
            if (amdError is null)
            {
                _lastMethod = ApplyMethod.AmdAdl;
                return amdMsg!;
            }

            var customMsg = NativeDisplay.ApplyCustomMode(
                settings.Top, settings.Bottom, settings.Left, settings.Right,
                enableUnsafeModes: true);
            _lastMethod = ApplyMethod.Win32Custom;
            return $"{customMsg} (ADL underscan unavailable: {amdError})";
        }

        var nvidia = NativeDisplay.DetectVendor() == GpuVendor.Nvidia;
        _lastMethod = ApplyMethod.Win32Custom;
        return NativeDisplay.ApplyCustomMode(settings.Top, settings.Bottom, settings.Left, settings.Right, nvidia);
    }

    public static string Restore()
    {
        switch (_lastMethod)
        {
            case ApplyMethod.AmdAdl:
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
