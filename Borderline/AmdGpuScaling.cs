using System.Runtime.InteropServices;

namespace Borderline;

/// <summary>
/// AMD GPU scaling + centered expansion + custom resolution.
/// On many Radeon iGPUs this is the only working path for driver-level blank margins.
/// </summary>
internal static class AmdGpuScaling
{
    private const int AdlOk = 0;
    private const int PropertyTypeExpansion = 1;
    private const int PropertyTypeUnderscanScaling = 2;
    private const int ExpansionCenter = 0;

    [StructLayout(LayoutKind.Sequential)]
    private struct AdlDisplayProperty
    {
        public int iSize;
        public int iPropertyType;
        public int iExpansionMode;
        public int iSupport;
        public int iCurrent;
        public int iDefault;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct AdlCustomMode
    {
        public int iFlags;
        public int iModeWidth;
        public int iModeHeight;
        public int iBaseModeWidth;
        public int iBaseModeHeight;
        public int iRefreshRate;
    }

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int Adl2DfpGpuScalingEnableGet(
        IntPtr context, int adapter, int display, ref int support, ref int current, ref int defaultVal);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int Adl2DfpGpuScalingEnableSet(IntPtr context, int adapter, int display, int enabled);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int Adl2DisplayPropertyGet(
        IntPtr context, int adapter, int display, ref AdlDisplayProperty property);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int Adl2DisplayPropertySet(
        IntPtr context, int adapter, int display, ref AdlDisplayProperty property);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int Adl2DisplayCustomizedModeAdd(
        IntPtr context, int adapter, int display, AdlCustomMode mode);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int Adl2AdapterModesReEnumerate(IntPtr context);

    private static int? _backupGpuScaling;
    private static int? _backupExpansion;
    private static bool _appliedCustomMode;

    public static string? TryApply(int top, int bottom, int left, int right, out string? message)
    {
        message = null;
        try
        {
            if (!NativeDisplay.TryGetResolution(out var nativeW, out var nativeH))
            {
                return "could not read resolution";
            }

            NativeDisplay.TryGetRefreshRate(out var hz);

            var newW = nativeW - left - right;
            var newH = nativeH - top - bottom;
            if (newW < 320 || newH < 240)
            {
                return "margins too large";
            }

            var uniform = top == bottom && left == right;
            if (!uniform)
            {
                // Centered GPU scaling only gives symmetric borders; use largest margin.
                var m = Math.Max(Math.Max(top, bottom), Math.Max(left, right));
                newW = nativeW - m * 2;
                newH = nativeH - m * 2;
            }

            string? lastErr = null;
            foreach (var target in AmdAdlCore.EnumerateTargetsPublic())
            {
                foreach (var displayIndex in AmdAdlCore.DisplayIndicesPublic(target))
                {
                    lastErr = TryOnDisplay(
                        target, displayIndex, nativeW, nativeH, newW, newH, hz,
                        left, right, top, bottom, uniform, out message);
                    if (lastErr is null)
                    {
                        return null;
                    }
                }
            }

            return lastErr ?? "GPU scaling path unavailable";
        }
        catch (Exception ex)
        {
            return ex.Message;
        }
    }

    public static bool Restore()
    {
        var restored = false;
        if (_appliedCustomMode)
        {
            NativeDisplay.Restore();
            _appliedCustomMode = false;
            restored = true;
        }

        try
        {
            var target = AmdAdlCore.Target;
            var gpuSet = AmdAdlCore.GetDelegate<Adl2DfpGpuScalingEnableSet>("ADL2_DFP_GPUScalingEnable_Set");
            var propSet = AmdAdlCore.GetDelegate<Adl2DisplayPropertySet>("ADL2_Display_Property_Set");

            if (_backupGpuScaling is int gs)
            {
                gpuSet(AmdAdlCore.Context, target.Adapter, target.Display, gs);
            }

            if (_backupExpansion is int exp)
            {
                var prop = new AdlDisplayProperty
                {
                    iSize = Marshal.SizeOf<AdlDisplayProperty>(),
                    iPropertyType = PropertyTypeExpansion,
                    iExpansionMode = exp,
                };
                propSet(AmdAdlCore.Context, target.Adapter, target.Display, ref prop);
            }

            AmdAdlCore.FlushAdapterDriver();
            _backupGpuScaling = null;
            _backupExpansion = null;
            restored = true;
        }
        catch
        {
            // Best effort.
        }

        return restored;
    }

    private static string? TryOnDisplay(
        AmdAdlCore.TargetDisplay target,
        int displayIndex,
        int nativeW,
        int nativeH,
        int newW,
        int newH,
        int hz,
        int left,
        int right,
        int top,
        int bottom,
        bool uniform,
        out string? message)
    {
        message = null;

        var gpuGet = AmdAdlCore.GetDelegate<Adl2DfpGpuScalingEnableGet>("ADL2_DFP_GPUScalingEnable_Get");
        var gpuSet = AmdAdlCore.GetDelegate<Adl2DfpGpuScalingEnableSet>("ADL2_DFP_GPUScalingEnable_Set");
        var propGet = AmdAdlCore.GetDelegate<Adl2DisplayPropertyGet>("ADL2_Display_Property_Get");
        var propSet = AmdAdlCore.GetDelegate<Adl2DisplayPropertySet>("ADL2_Display_Property_Set");

        var support = 0;
        var current = 0;
        var defaultVal = 0;
        if (gpuGet(AmdAdlCore.Context, target.Adapter, displayIndex, ref support, ref current, ref defaultVal) != AdlOk ||
            support == 0)
        {
            return $"GPU scaling not supported (display {displayIndex})";
        }

        if (_backupGpuScaling is null)
        {
            _backupGpuScaling = current;
        }

        if (gpuSet(AmdAdlCore.Context, target.Adapter, displayIndex, 1) != AdlOk)
        {
            return $"could not enable GPU scaling (display {displayIndex})";
        }

        var underProp = new AdlDisplayProperty { iSize = Marshal.SizeOf<AdlDisplayProperty>() };
        underProp.iPropertyType = PropertyTypeUnderscanScaling;
        if (propGet(AmdAdlCore.Context, target.Adapter, displayIndex, ref underProp) == AdlOk &&
            underProp.iSupport != 0)
        {
            underProp.iCurrent = 1;
            propSet(AmdAdlCore.Context, target.Adapter, displayIndex, ref underProp);
        }

        var expProp = new AdlDisplayProperty { iSize = Marshal.SizeOf<AdlDisplayProperty>() };
        expProp.iPropertyType = PropertyTypeExpansion;
        if (propGet(AmdAdlCore.Context, target.Adapter, displayIndex, ref expProp) != AdlOk)
        {
            return $"expansion mode get failed (display {displayIndex})";
        }

        if (_backupExpansion is null)
        {
            _backupExpansion = expProp.iExpansionMode;
        }

        expProp.iExpansionMode = ExpansionCenter;
        if (propSet(AmdAdlCore.Context, target.Adapter, displayIndex, ref expProp) != AdlOk)
        {
            return $"could not set centered scaling (display {displayIndex})";
        }

        TryAddCustomMode(target, displayIndex, nativeW, nativeH, newW, newH, hz);

        try
        {
            var marginNote = uniform
                ? ""
                : " (symmetric margins only on this GPU)";
            var t = uniform ? top : Math.Max(Math.Max(top, bottom), Math.Max(left, right));
            var b = uniform ? bottom : t;
            var l = uniform ? left : t;
            var r = uniform ? right : t;

            var result = NativeDisplay.ApplyCustomMode(t, b, l, r, enableUnsafeModes: true);
            _appliedCustomMode = true;

            if (!NativeDisplay.TryGetResolution(out var actualW, out var actualH) ||
                (actualW == nativeW && actualH == nativeH))
            {
                return $"custom mode {newW}x{newH} rejected after enabling GPU scaling (display {displayIndex})";
            }

            AmdAdlCore.FlushAdapterDriver();
            message =
                $"GPU scaling centered at {actualW}x{actualH} (panel {nativeW}x{nativeH}, display {displayIndex}){marginNote}. {result}";
            return null;
        }
        catch (Exception ex)
        {
            return ex.Message;
        }
    }

    private static void TryAddCustomMode(
        AmdAdlCore.TargetDisplay target,
        int displayIndex,
        int nativeW,
        int nativeH,
        int newW,
        int newH,
        int hz)
    {
        try
        {
            var add = AmdAdlCore.TryGetDelegate<Adl2DisplayCustomizedModeAdd>("ADL2_Display_CustomizedMode_Add");
            var reEnum = AmdAdlCore.TryGetDelegate<Adl2AdapterModesReEnumerate>("ADL2_Adapter_Modes_ReEnumerate");
            if (add is null)
            {
                return;
            }

            var mode = new AdlCustomMode
            {
                iFlags = 0,
                iModeWidth = newW,
                iModeHeight = newH,
                iBaseModeWidth = nativeW,
                iBaseModeHeight = nativeH,
                iRefreshRate = hz,
            };
            add(AmdAdlCore.Context, target.Adapter, displayIndex, mode);
            reEnum?.Invoke(AmdAdlCore.Context);
        }
        catch
        {
            // Optional; ChangeDisplaySettingsEx may still work.
        }
    }
}
