namespace Borderline;

internal static class DisplayService
{
    public static string GpuLabel()
    {
        return NativeDisplay.DetectVendor() switch
        {
            GpuVendor.Amd => "GPU: AMD (ADL driver)",
            GpuVendor.Nvidia => "GPU: NVIDIA (custom mode)",
            GpuVendor.Intel => "GPU: Intel (custom mode)",
            _ => "GPU: generic (custom mode)",
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
            try
            {
                return AmdDisplay.Apply(settings.Top, settings.Bottom, settings.Left, settings.Right);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"AMD path failed: {ex.Message}. Try again or use AMD Software.", ex);
            }
        }

        var nvidia = NativeDisplay.DetectVendor() == GpuVendor.Nvidia;
        return NativeDisplay.ApplyCustomMode(settings.Top, settings.Bottom, settings.Left, settings.Right, nvidia);
    }

    public static string Restore()
    {
        if (AmdDisplay.Restore())
        {
            return "AMD display restored.";
        }

        return NativeDisplay.Restore();
    }
}
