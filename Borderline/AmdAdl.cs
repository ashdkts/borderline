using System.Runtime.InteropServices;

namespace Borderline;

/// <summary>
/// AMD ADL2 ModeTimingOverride — driver-level overscan margins on Radeon GPUs.
/// </summary>
internal static class AmdAdl
{
    private const int AdlOk = 0;
    private const int AdlDlModetimingStandardCustom = 0x08;
    private const int AdlDlModetimingStandardDriverDefault = 0x10;
    private const int AdlDlTimingFlagReducedBlanking = 0x0010;

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int Adl2DisplayModeTimingOverrideGet(
        IntPtr context, int adapter, int display, ref AdlDisplayMode modeIn, ref AdlDisplayModeInfo modeOut);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int Adl2DisplayModeTimingOverrideSet(
        IntPtr context, int adapter, int display, ref AdlDisplayModeInfo mode, int forceUpdate);

    [StructLayout(LayoutKind.Sequential)]
    private struct AdlDisplayMode
    {
        public int iPelsHeight;
        public int iPelsWidth;
        public int iBitsPerPel;
        public int iDisplayFrequency;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct AdlDetailedTiming
    {
        public int iSize;
        public short sTimingFlags;
        public short sHTotal;
        public short sHDisplay;
        public short sHSyncStart;
        public short sHSyncWidth;
        public short sVTotal;
        public short sVDisplay;
        public short sVSyncStart;
        public short sVSyncWidth;
        public ushort sPixelClock;
        public short sHOverscanRight;
        public short sHOverscanLeft;
        public short sVOverscanBottom;
        public short sVOverscanTop;
        public short sOverscan8B;
        public short sOverscanGR;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct AdlDisplayModeInfo
    {
        public int iTimingStandard;
        public int iPossibleStandard;
        public int iRefreshRate;
        public int iPelsWidth;
        public int iPelsHeight;
        public AdlDetailedTiming sDetailedTiming;
    }

    private static Adl2DisplayModeTimingOverrideGet? _getOverride;
    private static Adl2DisplayModeTimingOverrideSet? _setOverride;
    private static AdlDisplayModeInfo? _backup;
    private static bool _hasBackup;

    public static string? TryApplyMargins(int top, int bottom, int left, int right, out string? message)
    {
        message = null;
        try
        {
            _ = AmdAdlCore.Context;
            var target = AmdAdlCore.Target;
            TryEnableGpuScaling(target.Adapter);

            _getOverride ??= AmdAdlCore.GetDelegate<Adl2DisplayModeTimingOverrideGet>("ADL2_Display_ModeTimingOverride_Get");
            _setOverride ??= AmdAdlCore.GetDelegate<Adl2DisplayModeTimingOverrideSet>("ADL2_Display_ModeTimingOverride_Set");

            if (!NativeDisplay.TryGetResolution(out var w, out var h))
            {
                return "could not read resolution";
            }

            NativeDisplay.TryGetRefreshRate(out var hz);

            var modeIn = new AdlDisplayMode
            {
                iPelsWidth = w,
                iPelsHeight = h,
                iBitsPerPel = 32,
                iDisplayFrequency = hz,
            };

            var modeOut = new AdlDisplayModeInfo();
            var getResult = _getOverride(AmdAdlCore.Context, target.Adapter, target.Display, ref modeIn, ref modeOut);
            if (getResult != AdlOk || !HasValidTiming(modeOut.sDetailedTiming))
            {
                modeOut = BuildCvtModeInfo(w, h, hz);
            }

            if (!_hasBackup)
            {
                _backup = modeOut;
                _hasBackup = true;
            }

            if (left + right >= w || top + bottom >= h)
            {
                return "margins too large";
            }

            modeOut.iTimingStandard = AdlDlModetimingStandardCustom;
            modeOut.iPelsWidth = w;
            modeOut.iPelsHeight = h;
            modeOut.iRefreshRate = hz;

            var dt = modeOut.sDetailedTiming;
            if (!HasValidTiming(dt))
            {
                dt = BuildCvtModeInfo(w, h, hz).sDetailedTiming;
            }

            dt.iSize = Marshal.SizeOf<AdlDetailedTiming>();
            dt.sHDisplay = (short)w;
            dt.sVDisplay = (short)h;
            dt.sHOverscanLeft = (short)left;
            dt.sHOverscanRight = (short)right;
            dt.sVOverscanTop = (short)top;
            dt.sVOverscanBottom = (short)bottom;
            modeOut.sDetailedTiming = dt;

            var setResult = _setOverride(AmdAdlCore.Context, target.Adapter, target.Display, ref modeOut, 1);
            if (setResult != AdlOk)
            {
                return $"timing override rejected ({AmdAdlCore.FormatAdlError(setResult)}, adapter {target.Adapter}, display {target.Display})";
            }

            AmdAdlCore.Flush(AmdAdlCore.Context, target.Adapter);
            NativeDisplay.RefreshDisplay();

            message =
                $"AMD timing overscan {left}/{right}/{top}/{bottom}px on {w}x{h} (adapter {target.Adapter}, display {target.Display}).";
            return null;
        }
        catch (Exception ex)
        {
            return ex.Message;
        }
    }

    public static bool Restore()
    {
        if (!_hasBackup)
        {
            return false;
        }

        try
        {
            var target = AmdAdlCore.Target;
            _setOverride ??= AmdAdlCore.GetDelegate<Adl2DisplayModeTimingOverrideSet>("ADL2_Display_ModeTimingOverride_Set");
            var restore = _backup!.Value;
            restore.iTimingStandard = AdlDlModetimingStandardDriverDefault;
            _setOverride(AmdAdlCore.Context, target.Adapter, target.Display, ref restore, 1);
            AmdAdlCore.Flush(AmdAdlCore.Context, target.Adapter);
            NativeDisplay.RefreshDisplay();
            _hasBackup = false;
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static void TryEnableGpuScaling(int adapter)
    {
        var gpuScaling = AmdAdlCore.TryGetDelegate<Adl2DfpGpuScalingEnableSet>("ADL2_DFP_GPUScalingEnable_Set");
        gpuScaling?.Invoke(AmdAdlCore.Context, adapter, 1);
    }

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int Adl2DfpGpuScalingEnableSet(IntPtr context, int adapter, int enabled);

    private static bool HasValidTiming(AdlDetailedTiming dt) =>
        dt.sHTotal > 0 && dt.sVTotal > 0 && dt.sPixelClock > 0;

    private static AdlDisplayModeInfo BuildCvtModeInfo(int width, int height, int refreshHz)
    {
        return new AdlDisplayModeInfo
        {
            iTimingStandard = AdlDlModetimingStandardCustom,
            iRefreshRate = refreshHz,
            iPelsWidth = width,
            iPelsHeight = height,
            sDetailedTiming = BuildCvtReducedBlanking(width, height, refreshHz),
        };
    }

    /// <summary>CVT reduced-blanking timings (same approach as switchres / CRU).</summary>
    private static AdlDetailedTiming BuildCvtReducedBlanking(int width, int height, int refreshHz)
    {
        var hBlank = 160;
        var hTotal = width + hBlank;
        var vSync = height <= 1024 ? 3 : height <= 2048 ? 6 : 9;
        var vBlank = Math.Max(460 / hTotal + 1, 3 + vSync);
        var vTotal = height + vBlank;
        var pixelClockKhz = (long)hTotal * vTotal * refreshHz / 1000;
        var pixelClock10Khz = (ushort)Math.Clamp(pixelClockKhz / 10, 1, ushort.MaxValue);

        return new AdlDetailedTiming
        {
            iSize = Marshal.SizeOf<AdlDetailedTiming>(),
            sTimingFlags = (short)AdlDlTimingFlagReducedBlanking,
            sHTotal = (short)hTotal,
            sHDisplay = (short)width,
            sHSyncStart = (short)(width + 48),
            sHSyncWidth = 32,
            sVTotal = (short)vTotal,
            sVDisplay = (short)height,
            sVSyncStart = (short)(height + vBlank - 6),
            sVSyncWidth = (short)vSync,
            sPixelClock = pixelClock10Khz,
        };
    }
}
