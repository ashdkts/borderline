namespace Borderline;

internal static class ApplyFailureHelp
{
    public static string ShortStatus =>
        "Apply failed — your Radeon iGPU does not support driver-level margins on this output.";

    public static string FullMessage =>
        "Borderline could not apply margins on this PC.\r\n\r\n" +
        "Your Beelink's Radeon iGPU does not expose the driver APIs Borderline needs for " +
        "per-edge blanking. That is a hardware/driver limitation on many SER-class machines, " +
        "not a bug you can fix by retrying.\r\n\r\n" +
        "What you CAN do manually:\r\n\r\n" +
        "Step 1 — AMD Software (Adrenalin)\r\n" +
        "  Gaming → Display\r\n" +
        "  • GPU Scaling: ON\r\n" +
        "  • Scaling Mode: Centered\r\n\r\n" +
        "Step 2 — Windows\r\n" +
        "  Settings → System → Display → Display resolution\r\n" +
        "  • Choose a resolution SMALLER than native (e.g. 1840×1000 on a 1920×1080 monitor)\r\n" +
        "  • With centered GPU scaling, unused panel area stays blank at the edges\r\n\r\n" +
        "Step 3 — Equal margins only\r\n" +
        "  Different top/bottom/left/right values are not supported on this GPU.\r\n\r\n" +
        $"Diagnostic log:\r\n{ApplyLog.LastPath}";
}
