using System.Runtime.InteropServices;

namespace Borderline;

/// <summary>
/// Shared ADL2 context, display discovery, and AMD display adjustment APIs.
/// </summary>
internal static class AmdAdlCore
{
    private const int AdlOk = 0;
    private const int AdlMaxPath = 256;
    private const int AdlDisplayAdjustUnderscan = 1 << 6;
    private const int AdlDisplayAdjustHorSize = 1 << 4;
    private const int AdlDisplayAdjustVertSize = 1 << 3;
    private const int AdlDisplayAdjustHorPos = 1 << 2;
    private const int AdlDisplayAdjustVertPos = 1 << 1;

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate IntPtr AdlMalloc(int size);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int Adl2MainControlCreate(AdlMalloc callback, int enumerateConnected, out IntPtr context);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int Adl2MainControlDestroy(IntPtr context);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int Adl2AdapterNumberOfAdaptersGet(IntPtr context, ref int num);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int Adl2AdapterAdapterInfoGet(IntPtr context, IntPtr info, int size);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int Adl2DisplayDisplayInfoGet(IntPtr context, int adapter, ref int numDisplays, ref IntPtr displayInfo, int forceRefresh);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int Adl2DisplayAdjustCapsGet(IntPtr context, int adapter, int display, ref int caps);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int Adl2DisplaySizeGet(
        IntPtr context, int adapter, int display,
        ref int width, ref int height,
        ref int defaultWidth, ref int defaultHeight,
        ref int minWidth, ref int minHeight,
        ref int maxWidth, ref int maxHeight,
        ref int stepWidth, ref int stepHeight);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int Adl2DisplaySizeSet(IntPtr context, int adapter, int display, int width, int height);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int Adl2DisplayPositionGet(
        IntPtr context, int adapter, int display,
        ref int x, ref int y,
        ref int defaultX, ref int defaultY,
        ref int minX, ref int minY,
        ref int maxX, ref int maxY,
        ref int stepX, ref int stepY);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int Adl2DisplayPositionSet(IntPtr context, int adapter, int display, int x, int y);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int Adl2DisplayAdjustmentCoherentSet(IntPtr context, int adapter, int display, int coherent);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int Adl2DisplayUnderscanSupportGet(IntPtr context, int adapter, int display, ref int support);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int Adl2DisplayUnderscanStateSet(IntPtr context, int adapter, int display, int enabled);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int Adl2DisplayUnderscanGet(
        IntPtr context, int adapter, int display,
        ref int current, ref int defaultVal, ref int min, ref int max, ref int step);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int Adl2DisplayUnderscanSet(IntPtr context, int adapter, int display, int current);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int Adl2DfpGpuScalingEnableSet(IntPtr context, int adapter, int enabled);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    internal delegate int Adl2FlushDriverData(IntPtr context, int adapter);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
    internal struct AdapterInfo
    {
        public int iSize;
        public int iAdapterIndex;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = AdlMaxPath)]
        public string strUDID;
        public int iBusNumber;
        public int iDeviceNumber;
        public int iFunctionNumber;
        public int iVendorID;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = AdlMaxPath)]
        public string strAdapterName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = AdlMaxPath)]
        public string strDisplayName;
        public int iPresent;
        public int iExist;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = AdlMaxPath)]
        public string strDriverPath;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = AdlMaxPath)]
        public string strDriverPathExt;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = AdlMaxPath)]
        public string strPNPString;
        public int iOSDisplayIndex;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct AdlDisplayId
    {
        public int iDisplayLogicalIndex;
        public int iDisplayPhysicalIndex;
        public int iDisplayLogicalAdapterIndex;
        public int iDisplayPhysicalAdapterIndex;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
    internal struct AdlDisplayInfo
    {
        public AdlDisplayId displayID;
        public int iDisplayControllerIndex;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = AdlMaxPath)]
        public string strDisplayName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = AdlMaxPath)]
        public string strDisplayManufacturerName;
        public int iDisplayType;
        public int iDisplayOutputType;
        public int iDisplayConnector;
        public int iDisplayInfoMask;
        public int iDisplayInfoValue;
    }

    internal readonly record struct TargetDisplay(int Adapter, int Display, string AdapterDeviceName);

    private static readonly AdlMalloc MallocCallback = size =>
    {
        if (size <= 0)
        {
            return IntPtr.Zero;
        }

        return Marshal.AllocHGlobal(size);
    };

    private static IntPtr _context;
    private static IntPtr _dll;
    private static Adl2FlushDriverData? _flush;
    private static Adl2DfpGpuScalingEnableSet? _gpuScalingSet;
    private static TargetDisplay? _target;
    private static int _backupSizeW;
    private static int _backupSizeH;
    private static int _backupPosX;
    private static int _backupPosY;
    private static int _backupUnderscan;
    private static bool _hasSizeBackup;
    private static bool _hasUnderscanBackup;

    internal static IntPtr Context
    {
        get
        {
            EnsureLoaded();
            return _context;
        }
    }

    internal static IntPtr Dll
    {
        get
        {
            EnsureLoaded();
            return _dll;
        }
    }

    internal static Adl2FlushDriverData Flush => EnsureLoadedDelegates().flush;

    internal static TargetDisplay Target
    {
        get
        {
            if (_target is null)
            {
                _target = FindTargetDisplay();
            }

            return _target.Value;
        }
    }

    private static bool _gpuScalingPrepared;

    public static void PrepareForMargins()
    {
        if (_gpuScalingPrepared)
        {
            return;
        }

        try
        {
            EnsureLoadedDelegates();
            _gpuScalingSet?.Invoke(Context, Target.Adapter, 1);
            _gpuScalingPrepared = true;
        }
        catch
        {
            // GPU scaling is optional; size/underscan may still work.
        }
    }

    public static void FlushAdapterDriver()
    {
        try
        {
            Flush(Context, Target.Adapter);
        }
        catch
        {
            // Ignore flush failures on registry-only paths.
        }
    }

    public static string? TryApplySizeAndPosition(int top, int bottom, int left, int right, out string? message)
    {
        message = null;
        try
        {
            EnsureLoaded();
            string? lastErr = null;
            foreach (var candidate in EnumerateTargets())
            {
                lastErr = TryApplySizeOnTarget(candidate, top, bottom, left, right, out message);
                if (lastErr is null)
                {
                    _target = candidate;
                    return null;
                }
            }

            return lastErr ?? "no AMD displays found";
        }
        catch (Exception ex)
        {
            return ex.Message;
        }
    }

    private static string? TryApplySizeOnTarget(
        TargetDisplay target, int top, int bottom, int left, int right, out string? message)
    {
        message = null;
        try
        {
            EnsureLoaded();
            var sizeGet = GetDelegate<Adl2DisplaySizeGet>("ADL2_Display_Size_Get");
            var sizeSet = GetDelegate<Adl2DisplaySizeSet>("ADL2_Display_Size_Set");
            var posGet = GetDelegate<Adl2DisplayPositionGet>("ADL2_Display_Position_Get");
            var posSet = GetDelegate<Adl2DisplayPositionSet>("ADL2_Display_Position_Set");
            var capsGet = GetDelegate<Adl2DisplayAdjustCapsGet>("ADL2_Display_AdjustCaps_Get");
            var coherentSet = TryGetDelegate<Adl2DisplayAdjustmentCoherentSet>("ADL2_Display_AdjustmentCoherent_Set");

            var caps = 0;
            capsGet(Context, target.Adapter, target.Display, ref caps);
            if ((caps & (AdlDisplayAdjustHorSize | AdlDisplayAdjustVertSize)) == 0)
            {
                return "display size adjustment not supported";
            }

            var width = 0;
            var height = 0;
            var defaultWidth = 0;
            var defaultHeight = 0;
            var minWidth = 0;
            var minHeight = 0;
            var maxWidth = 0;
            var maxHeight = 0;
            var stepWidth = 1;
            var stepHeight = 1;
            var sizeResult = sizeGet(
                Context, target.Adapter, target.Display,
                ref width, ref height, ref defaultWidth, ref defaultHeight,
                ref minWidth, ref minHeight, ref maxWidth, ref maxHeight,
                ref stepWidth, ref stepHeight);
            if (sizeResult != AdlOk)
            {
                return $"size get failed ({FormatAdlError(sizeResult)})";
            }

            if (defaultWidth <= 0)
            {
                defaultWidth = maxWidth > 0 ? maxWidth : 100;
            }

            if (defaultHeight <= 0)
            {
                defaultHeight = maxHeight > 0 ? maxHeight : 100;
            }

            if (!_hasSizeBackup)
            {
                _backupSizeW = width;
                _backupSizeH = height;
                _hasSizeBackup = true;
            }

            var x = 0;
            var y = 0;
            var defaultX = 0;
            var defaultY = 0;
            var minX = 0;
            var minY = 0;
            var maxX = 0;
            var maxY = 0;
            var stepX = 1;
            var stepY = 1;
            var posSupported = (caps & (AdlDisplayAdjustHorPos | AdlDisplayAdjustVertPos)) != 0;
            if (posSupported &&
                posGet(
                    Context, target.Adapter, target.Display,
                    ref x, ref y, ref defaultX, ref defaultY,
                    ref minX, ref minY, ref maxX, ref maxY,
                    ref stepX, ref stepY) == AdlOk &&
                _backupPosX == 0 && _backupPosY == 0)
            {
                _backupPosX = x;
                _backupPosY = y;
            }

            if (!NativeDisplay.TryGetResolution(out var screenW, out var screenH))
            {
                return "could not read resolution";
            }

            var widthScale = (double)(screenW - left - right) / screenW;
            var heightScale = (double)(screenH - top - bottom) / screenH;
            var newWidth = ScaleSetting(defaultWidth, minWidth, maxWidth, stepWidth, widthScale);
            var newHeight = ScaleSetting(defaultHeight, minHeight, maxHeight, stepHeight, heightScale);

            if (newWidth == width && newHeight == height)
            {
                return
                    $"size range too narrow (cur {width}x{height}, min {minWidth}x{minHeight} max {maxWidth}x{maxHeight}, default {defaultWidth}x{defaultHeight})";
            }

            coherentSet?.Invoke(Context, target.Adapter, target.Display, 0);

            if (sizeSet(Context, target.Adapter, target.Display, newWidth, newHeight) != AdlOk)
            {
                return "AMD rejected display size change";
            }

            if (posSupported)
            {
                var shiftX = (left - right) / 2.0;
                var shiftY = (top - bottom) / 2.0;
                var rangeX = Math.Max(1, maxX - minX);
                var rangeY = Math.Max(1, maxY - minY);
                var newX = Snap(defaultX + (int)Math.Round(shiftX / screenW * rangeX), minX, maxX, stepX);
                var newY = Snap(defaultY + (int)Math.Round(shiftY / screenH * rangeY), minY, maxY, stepY);
                posSet(Context, target.Adapter, target.Display, newX, newY);
            }

            Flush(Context, target.Adapter);

            var verifyW = 0;
            var verifyH = 0;
            var verifyDefW = 0;
            var verifyDefH = 0;
            var verifyMinW = 0;
            var verifyMinH = 0;
            var verifyMaxW = 0;
            var verifyMaxH = 0;
            var verifyStepW = 0;
            var verifyStepH = 0;
            sizeGet(
                Context, target.Adapter, target.Display,
                ref verifyW, ref verifyH, ref verifyDefW, ref verifyDefH,
                ref verifyMinW, ref verifyMinH, ref verifyMaxW, ref verifyMaxH,
                ref verifyStepW, ref verifyStepH);

            if (verifyW == width && verifyH == height)
            {
                sizeSet(Context, target.Adapter, target.Display, width, height);
                return
                    $"driver ignored size change (still {width}x{height}, wanted {newWidth}x{newHeight}, caps 0x{caps:X})";
            }

            message =
                $"AMD display size {width}x{height} -> {verifyW}x{verifyH} (adapter {target.Adapter}, display {target.Display}, caps 0x{caps:X}).";
            return null;
        }
        catch (Exception ex)
        {
            return ex.Message;
        }
    }

    public static string? TryApplyUnderscan(int top, int bottom, int left, int right, out string? message)
    {
        message = null;
        try
        {
            EnsureLoaded();
            string? lastErr = null;
            foreach (var candidate in EnumerateTargets())
            {
                lastErr = TryApplyUnderscanOnTarget(candidate, top, bottom, left, right, out message);
                if (lastErr is null)
                {
                    _target = candidate;
                    return null;
                }
            }

            return lastErr ?? "no AMD displays found";
        }
        catch (Exception ex)
        {
            return ex.Message;
        }
    }

    private static string? TryApplyUnderscanOnTarget(
        TargetDisplay target, int top, int bottom, int left, int right, out string? message)
    {
        message = null;
        try
        {
            var supportGet = GetDelegate<Adl2DisplayUnderscanSupportGet>("ADL2_Display_UnderscanSupport_Get");
            var stateSet = GetDelegate<Adl2DisplayUnderscanStateSet>("ADL2_Display_UnderscanState_Set");
            var underGet = GetDelegate<Adl2DisplayUnderscanGet>("ADL2_Display_Underscan_Get");
            var underSet = GetDelegate<Adl2DisplayUnderscanSet>("ADL2_Display_Underscan_Set");

            var support = 0;
            if (supportGet(Context, target.Adapter, target.Display, ref support) != AdlOk || support == 0)
            {
                return "underscan not supported on this display";
            }

            var current = 0;
            var defaultVal = 0;
            var min = 0;
            var max = 0;
            var step = 1;
            if (underGet(Context, target.Adapter, target.Display, ref current, ref defaultVal, ref min, ref max, ref step) != AdlOk)
            {
                return "underscan get failed";
            }

            if (!_hasUnderscanBackup)
            {
                _backupUnderscan = current;
                _hasUnderscanBackup = true;
            }

            if (!NativeDisplay.TryGetResolution(out var w, out var h))
            {
                return "could not read resolution";
            }

            var margin = Math.Max(Math.Max(top, bottom), Math.Max(left, right));
            var dim = Math.Min(w, h);
            var targetValue = dim <= 0 ? min : margin * 100 * 2 / dim;
            targetValue = Snap(targetValue, min, max, step);

            if (targetValue == current && margin > 0)
            {
                return $"underscan already at {current}% (range {min}-{max}, step {step})";
            }

            if (stateSet(Context, target.Adapter, target.Display, 1) != AdlOk)
            {
                return "could not enable underscan";
            }

            var setResult = underSet(Context, target.Adapter, target.Display, targetValue);
            if (setResult != AdlOk)
            {
                return $"underscan set failed ({FormatAdlError(setResult)}, range {min}-{max}, step {step})";
            }

            Flush(Context, target.Adapter);

            var verify = 0;
            var verifyDef = 0;
            var verifyMin = 0;
            var verifyMax = 0;
            var verifyStep = 0;
            underGet(Context, target.Adapter, target.Display, ref verify, ref verifyDef, ref verifyMin, ref verifyMax, ref verifyStep);
            if (verify == current)
            {
                underSet(Context, target.Adapter, target.Display, current);
                return $"driver ignored underscan (still {current}%, wanted {targetValue}%, range {min}-{max})";
            }

            message =
                $"AMD underscan {current}% -> {verify}% (adapter {target.Adapter}, display {target.Display}, range {min}-{max}).";
            return null;
        }
        catch (Exception ex)
        {
            return ex.Message;
        }
    }

    public static bool RestoreSizeAndPosition()
    {
        if (!_hasSizeBackup || _target is null)
        {
            return false;
        }

        try
        {
            EnsureLoaded();
            var target = _target.Value;
            var sizeSet = GetDelegate<Adl2DisplaySizeSet>("ADL2_Display_Size_Set");
            var posSet = TryGetDelegate<Adl2DisplayPositionSet>("ADL2_Display_Position_Set");
            sizeSet(Context, target.Adapter, target.Display, _backupSizeW, _backupSizeH);
            posSet?.Invoke(Context, target.Adapter, target.Display, _backupPosX, _backupPosY);
            Flush(Context, target.Adapter);
            _hasSizeBackup = false;
            return true;
        }
        catch
        {
            return false;
        }
    }

    public static bool RestoreUnderscan()
    {
        if (!_hasUnderscanBackup || _target is null)
        {
            return false;
        }

        try
        {
            EnsureLoaded();
            var target = _target.Value;
            var underSet = GetDelegate<Adl2DisplayUnderscanSet>("ADL2_Display_Underscan_Set");
            var stateSet = GetDelegate<Adl2DisplayUnderscanStateSet>("ADL2_Display_UnderscanState_Set");
            underSet(Context, target.Adapter, target.Display, _backupUnderscan);
            stateSet(Context, target.Adapter, target.Display, 0);
            Flush(Context, target.Adapter);
            _hasUnderscanBackup = false;
            return true;
        }
        catch
        {
            return false;
        }
    }

    internal static T GetDelegate<T>(string name) where T : Delegate
    {
        if (!NativeLibrary.TryGetExport(_dll, name, out var proc))
        {
            throw new InvalidOperationException($"ADL export missing: {name}");
        }

        return Marshal.GetDelegateForFunctionPointer<T>(proc);
    }

    internal static T? TryGetDelegate<T>(string name) where T : Delegate
    {
        if (_dll == IntPtr.Zero || !NativeLibrary.TryGetExport(_dll, name, out var proc))
        {
            return null;
        }

        return Marshal.GetDelegateForFunctionPointer<T>(proc);
    }

    internal static string FormatAdlError(int code) => code switch
    {
        -1 => "ADL_ERR",
        -2 => "ADL_ERR_INVALID_PARAM",
        -3 => "ADL_ERR_INVALID_DISPLAY",
        -4 => "ADL_ERR_INVALID_CONTROLLER",
        -5 => "ADL_ERR_INVALID_ADAPTER",
        -6 => "ADL_ERR_NOT_SUPPORTED",
        -7 => "ADL_ERR_NULL_POINTER",
        _ => $"ADL code {code}",
    };

    private static void EnsureLoaded()
    {
        if (_context != IntPtr.Zero)
        {
            return;
        }

        EnsureLoadedDelegates();
    }

    private static (Adl2FlushDriverData flush, Adl2DfpGpuScalingEnableSet? gpuScaling) EnsureLoadedDelegates()
    {
        if (_context != IntPtr.Zero && _flush is not null)
        {
            return (_flush, _gpuScalingSet);
        }

        try
        {
            _dll = NativeLibrary.Load("atiadlxx.dll");
        }
        catch (DllNotFoundException)
        {
            _dll = NativeLibrary.Load("atiadlxy.dll");
        }

        var create = GetDelegate<Adl2MainControlCreate>("ADL2_Main_Control_Create");
        _flush = GetDelegate<Adl2FlushDriverData>("ADL2_Flush_Driver_Data");
        _gpuScalingSet = TryGetDelegate<Adl2DfpGpuScalingEnableSet>("ADL2_DFP_GPUScalingEnable_Set");

        if (create(MallocCallback, 1, out _context) != AdlOk)
        {
            throw new InvalidOperationException("ADL2 init failed");
        }

        return (_flush, _gpuScalingSet);
    }

    private static IEnumerable<TargetDisplay> EnumerateTargets()
    {
        EnsureLoaded();

        var numAdaptersGet = GetDelegate<Adl2AdapterNumberOfAdaptersGet>("ADL2_Adapter_NumberOfAdapters_Get");
        var adapterInfoGet = GetDelegate<Adl2AdapterAdapterInfoGet>("ADL2_Adapter_AdapterInfo_Get");
        var displayInfoGet = GetDelegate<Adl2DisplayDisplayInfoGet>("ADL2_Display_DisplayInfo_Get");
        var primaryDevice = NativeDisplay.GetPrimaryDeviceName();

        var numAdapters = 0;
        if (numAdaptersGet(Context, ref numAdapters) != AdlOk || numAdapters <= 0)
        {
            yield break;
        }

        var infoSize = Marshal.SizeOf<AdapterInfo>();
        var adapterBuffer = Marshal.AllocHGlobal(infoSize * numAdapters);
        try
        {
            for (var i = 0; i < numAdapters; i++)
            {
                Marshal.WriteInt32(adapterBuffer + (i * infoSize), infoSize);
            }

            if (adapterInfoGet(Context, adapterBuffer, infoSize * numAdapters) != AdlOk)
            {
                yield break;
            }

            var primaryMatches = new List<TargetDisplay>();
            var others = new List<TargetDisplay>();

            for (var i = 0; i < numAdapters; i++)
            {
                var adapter = Marshal.PtrToStructure<AdapterInfo>(adapterBuffer + (i * infoSize));
                if (adapter.iPresent == 0)
                {
                    continue;
                }

                var numDisplays = 0;
                IntPtr displayList = IntPtr.Zero;
                if (displayInfoGet(Context, adapter.iAdapterIndex, ref numDisplays, ref displayList, 1) != AdlOk ||
                    numDisplays <= 0 || displayList == IntPtr.Zero)
                {
                    continue;
                }

                var displaySize = Marshal.SizeOf<AdlDisplayInfo>();
                for (var d = 0; d < numDisplays; d++)
                {
                    var info = Marshal.PtrToStructure<AdlDisplayInfo>(displayList + (d * displaySize));
                    if (info.displayID.iDisplayLogicalAdapterIndex != adapter.iAdapterIndex)
                    {
                        continue;
                    }

                    var candidate = new TargetDisplay(
                        adapter.iAdapterIndex,
                        info.displayID.iDisplayLogicalIndex,
                        adapter.strDisplayName);

                    if (!string.IsNullOrEmpty(primaryDevice) &&
                        string.Equals(adapter.strDisplayName, primaryDevice, StringComparison.OrdinalIgnoreCase))
                    {
                        primaryMatches.Add(candidate);
                    }
                    else
                    {
                        others.Add(candidate);
                    }
                }
            }

            foreach (var t in primaryMatches)
            {
                yield return t;
            }

            foreach (var t in others)
            {
                yield return t;
            }
        }
        finally
        {
            Marshal.FreeHGlobal(adapterBuffer);
        }
    }

    private static TargetDisplay FindTargetDisplay()
    {
        EnsureLoaded();

        var numAdaptersGet = GetDelegate<Adl2AdapterNumberOfAdaptersGet>("ADL2_Adapter_NumberOfAdapters_Get");
        var adapterInfoGet = GetDelegate<Adl2AdapterAdapterInfoGet>("ADL2_Adapter_AdapterInfo_Get");
        var displayInfoGet = GetDelegate<Adl2DisplayDisplayInfoGet>("ADL2_Display_DisplayInfo_Get");

        var primaryDevice = NativeDisplay.GetPrimaryDeviceName();
        var numAdapters = 0;
        if (numAdaptersGet(Context, ref numAdapters) != AdlOk || numAdapters <= 0)
        {
            throw new InvalidOperationException("No AMD adapters");
        }

        var infoSize = Marshal.SizeOf<AdapterInfo>();
        var adapterBuffer = Marshal.AllocHGlobal(infoSize * numAdapters);
        TargetDisplay? fallback = null;
        try
        {
            for (var i = 0; i < numAdapters; i++)
            {
                Marshal.WriteInt32(adapterBuffer + (i * infoSize), infoSize);
            }

            if (adapterInfoGet(Context, adapterBuffer, infoSize * numAdapters) != AdlOk)
            {
                throw new InvalidOperationException("Adapter info failed");
            }

            for (var i = 0; i < numAdapters; i++)
            {
                var adapter = Marshal.PtrToStructure<AdapterInfo>(adapterBuffer + (i * infoSize));
                if (adapter.iPresent == 0)
                {
                    continue;
                }

                var numDisplays = 0;
                IntPtr displayList = IntPtr.Zero;
                if (displayInfoGet(Context, adapter.iAdapterIndex, ref numDisplays, ref displayList, 1) != AdlOk ||
                    numDisplays <= 0 || displayList == IntPtr.Zero)
                {
                    continue;
                }

                var displaySize = Marshal.SizeOf<AdlDisplayInfo>();
                for (var d = 0; d < numDisplays; d++)
                {
                    var info = Marshal.PtrToStructure<AdlDisplayInfo>(displayList + (d * displaySize));
                    if (info.displayID.iDisplayLogicalAdapterIndex != adapter.iAdapterIndex)
                    {
                        continue;
                    }

                    var candidate = new TargetDisplay(
                        adapter.iAdapterIndex,
                        info.displayID.iDisplayLogicalIndex,
                        adapter.strDisplayName);

                    fallback ??= candidate;

                    if (!string.IsNullOrEmpty(primaryDevice) &&
                        string.Equals(adapter.strDisplayName, primaryDevice, StringComparison.OrdinalIgnoreCase))
                    {
                        return candidate;
                    }
                }
            }
        }
        finally
        {
            Marshal.FreeHGlobal(adapterBuffer);
        }

        if (fallback is not null)
        {
            return fallback.Value;
        }

        throw new InvalidOperationException($"No AMD display found (Windows primary: {primaryDevice})");
    }

    private static int ScaleSetting(int baseline, int min, int max, int step, double scale)
    {
        var range = Math.Max(1, max - min);
        var scaled = baseline - (int)Math.Round((1.0 - scale) * range);
        return Snap(scaled, min, max, Math.Max(1, step));
    }

    private static int Snap(int value, int min, int max, int step)
    {
        value = Math.Clamp(value, min, max);
        if (step <= 1)
        {
            return value;
        }

        var offset = value - min;
        offset = (offset / step) * step;
        return min + offset;
    }
}
