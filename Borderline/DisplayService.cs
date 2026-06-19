namespace Borderline;

internal enum ApplyMethod
{
    None,
    AmdSize,
    AmdUnderscan,
    AmdTiming,
    Win32Custom,
}

internal static class DisplayService
{
    private static ApplyMethod _lastMethod;

    public static string GpuLabel()
    {
        return NativeDisplay.DetectVendor() switch
        {
            GpuVendor.Amd => "GPU: AMD Radeon (ADL display size / underscan)",
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
            var errors = new List<string>();

            var sizeErr = AmdAdlCore.TryApplySizeAndPosition(
                settings.Top, settings.Bottom, settings.Left, settings.Right, out var sizeMsg);
            if (sizeErr is null)
            {
                _lastMethod = ApplyMethod.AmdSize;
                return sizeMsg!;
            }

            errors.Add($"Size: {sizeErr}");

            var underErr = AmdAdlCore.TryApplyUnderscan(
                settings.Top, settings.Bottom, settings.Left, settings.Right, out var underMsg);
            if (underErr is null)
            {
                _lastMethod = ApplyMethod.AmdUnderscan;
                return underMsg!;
            }

            errors.Add($"Underscan: {underErr}");

            var timingErr = AmdAdl.TryApplyMargins(
                settings.Top, settings.Bottom, settings.Left, settings.Right, out var timingMsg);
            if (timingErr is null)
            {
                _lastMethod = ApplyMethod.AmdTiming;
                return timingMsg!;
            }

            errors.Add($"Timing: {timingErr}");

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
                    $"All AMD methods failed. {string.Join(" ", errors)} Windows: {ex.Message}", ex);
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
            case ApplyMethod.AmdSize:
                if (AmdAdlCore.RestoreSizeAndPosition())
                {
                    _lastMethod = ApplyMethod.None;
                    return "AMD display size restored.";
                }
                break;
            case ApplyMethod.AmdUnderscan:
                if (AmdAdlCore.RestoreUnderscan())
                {
                    _lastMethod = ApplyMethod.None;
                    return "AMD underscan restored.";
                }
                break;
            case ApplyMethod.AmdTiming:
                if (AmdAdl.Restore())
                {
                    _lastMethod = ApplyMethod.None;
                    return "AMD timing override restored.";
                }
                break;
        }

        _lastMethod = ApplyMethod.None;
        return NativeDisplay.Restore();
    }
}
