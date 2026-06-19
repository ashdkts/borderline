namespace Borderline;

internal static class DisplayService
{
    private static ApplyMethod _lastMethod;

    public static string GpuLabel()
    {
        if (NativeDisplay.DetectVendor() != GpuVendor.Amd)
        {
            return NativeDisplay.DetectVendor() switch
            {
                GpuVendor.Nvidia => "GPU: NVIDIA (custom resolution)",
                GpuVendor.Intel => "GPU: Intel (custom resolution)",
                _ => "GPU: generic (custom resolution)",
            };
        }

        try
        {
            return AmdAdlCore.GetCapabilitySummary();
        }
        catch (Exception ex)
        {
            return $"GPU: AMD Radeon ({ex.Message})";
        }
    }

    public static string Apply(AppSettings settings)
    {
        if (!settings.Enabled || (settings.Top == 0 && settings.Bottom == 0 && settings.Left == 0 && settings.Right == 0))
        {
            return Restore();
        }

        if (NativeDisplay.DetectVendor() == GpuVendor.Amd)
        {
            return AmdApply.Apply(settings, out _lastMethod);
        }

        _lastMethod = ApplyMethod.Win32Custom;
        return NativeDisplay.ApplyCustomMode(
            settings.Top, settings.Bottom, settings.Left, settings.Right,
            NativeDisplay.DetectVendor() == GpuVendor.Nvidia);
    }

    public static string Restore()
    {
        if (_lastMethod is ApplyMethod.Win32Custom or ApplyMethod.None)
        {
            _lastMethod = ApplyMethod.None;
            return NativeDisplay.Restore();
        }

        if (AmdApply.Restore(_lastMethod))
        {
            _lastMethod = ApplyMethod.None;
            return "AMD display settings restored.";
        }

        _lastMethod = ApplyMethod.None;
        return NativeDisplay.Restore();
    }
}
