using System.Runtime.InteropServices;

namespace Borderline;

/// <summary>
/// AMD ADL2 — ModeTimingOverride for driver-level display margins on Radeon GPUs.
/// </summary>
internal static class AmdAdl
{
    private const int AdlOk = 0;
    private const int AdlMaxPath = 256;
    private const int AdlDlModetimingStandardCustom = 0x08;
    private const int AdlDlModetimingStandardDriverDefault = 0x10;

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
    private delegate int Adl2DisplayModeTimingOverrideGet(
        IntPtr context, int adapter, int display, ref AdlDisplayMode modeIn, ref AdlDisplayModeInfo modeOut);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int Adl2DisplayModeTimingOverrideSet(
        IntPtr context, int adapter, int display, ref AdlDisplayModeInfo mode, int forceUpdate);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int Adl2FlushDriverData(IntPtr context, int adapter);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
    private struct AdapterInfo
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
    private struct AdlDisplayId
    {
        public int iDisplayLogicalIndex;
        public int iDisplayPhysicalIndex;
        public int iDisplayLogicalAdapterIndex;
        public int iDisplayPhysicalAdapterIndex;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
    private struct AdlDisplayInfo
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
    private static Adl2MainControlDestroy? _destroy;
    private static Adl2DisplayModeTimingOverrideGet? _getOverride;
    private static Adl2DisplayModeTimingOverrideSet? _setOverride;
    private static Adl2FlushDriverData? _flush;

    private static int _adapter = -1;
    private static int _display = -1;
    private static AdlDisplayModeInfo? _backup;
    private static bool _hasBackup;

    public static string? TryApplyMargins(int top, int bottom, int left, int right, out string? message)
    {
        message = null;
        try
        {
            EnsureLoaded();
            FindPrimaryDisplay();

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
            var getResult = _getOverride!(_context, _adapter, _display, ref modeIn, ref modeOut);
            if (getResult != AdlOk)
            {
                // Build from current resolution if driver has no override yet.
                modeOut = BuildDefaultModeInfo(w, h, hz);
            }

            if (!_hasBackup)
            {
                _backup = modeOut;
                _hasBackup = true;
            }

            var newW = w - left - right;
            var newH = h - top - bottom;
            if (newW < 320 || newH < 240)
            {
                return "margins too large";
            }

            modeOut.iTimingStandard = AdlDlModetimingStandardCustom;
            modeOut.iPelsWidth = newW;
            modeOut.iPelsHeight = newH;
            modeOut.iRefreshRate = hz;

            var dt = modeOut.sDetailedTiming;
            if (dt.sHTotal <= 0)
            {
                dt = BuildDefaultModeInfo(w, h, hz).sDetailedTiming;
            }

            dt.iSize = Marshal.SizeOf<AdlDetailedTiming>();
            dt.sHDisplay = (short)Math.Max(1, dt.sHDisplay - left - right);
            dt.sVDisplay = (short)Math.Max(1, dt.sVDisplay - top - bottom);
            dt.sHOverscanLeft = (short)left;
            dt.sHOverscanRight = (short)right;
            dt.sVOverscanTop = (short)top;
            dt.sVOverscanBottom = (short)bottom;
            modeOut.sDetailedTiming = dt;

            if (_setOverride!(_context, _adapter, _display, ref modeOut, 1) != AdlOk)
            {
                return "AMD rejected timing override";
            }

            _flush!(_context, _adapter);
            NativeDisplay.RefreshDisplay();

            message = $"AMD timing override {newW}x{newH} with {left}/{right}/{top}/{bottom}px margins.";
            return null;
        }
        catch (Exception ex)
        {
            return ex.Message;
        }
    }

    public static bool Restore()
    {
        if (!_hasBackup || _adapter < 0)
        {
            return false;
        }

        try
        {
            EnsureLoaded();
            var restore = _backup!.Value;
            restore.iTimingStandard = AdlDlModetimingStandardDriverDefault;
            _setOverride!(_context, _adapter, _display, ref restore, 1);
            _flush!(_context, _adapter);
            NativeDisplay.RefreshDisplay();
            _hasBackup = false;
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static AdlDisplayModeInfo BuildDefaultModeInfo(int w, int h, int hz)
    {
        return new AdlDisplayModeInfo
        {
            iTimingStandard = AdlDlModetimingStandardCustom,
            iRefreshRate = hz,
            iPelsWidth = w,
            iPelsHeight = h,
            sDetailedTiming = new AdlDetailedTiming
            {
                iSize = Marshal.SizeOf<AdlDetailedTiming>(),
                sHTotal = (short)w,
                sHDisplay = (short)w,
                sHSyncStart = (short)(w + 8),
                sHSyncWidth = 32,
                sVTotal = (short)h,
                sVDisplay = (short)h,
                sVSyncStart = (short)(h + 3),
                sVSyncWidth = 5,
                sPixelClock = 0,
            },
        };
    }

    private static void EnsureLoaded()
    {
        if (_context != IntPtr.Zero)
        {
            return;
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
        _destroy = GetDelegate<Adl2MainControlDestroy>("ADL2_Main_Control_Destroy");
        _getOverride = GetDelegate<Adl2DisplayModeTimingOverrideGet>("ADL2_Display_ModeTimingOverride_Get");
        _setOverride = GetDelegate<Adl2DisplayModeTimingOverrideSet>("ADL2_Display_ModeTimingOverride_Set");
        _flush = GetDelegate<Adl2FlushDriverData>("ADL2_Flush_Driver_Data");

        if (create(MallocCallback, 1, out _context) != AdlOk)
        {
            throw new InvalidOperationException("ADL2 init failed");
        }
    }

    private static T GetDelegate<T>(string name) where T : Delegate
    {
        if (!NativeLibrary.TryGetExport(_dll, name, out var proc))
        {
            throw new InvalidOperationException($"ADL export missing: {name}");
        }

        return Marshal.GetDelegateForFunctionPointer<T>(proc);
    }

    private static void FindPrimaryDisplay()
    {
        if (_adapter >= 0)
        {
            return;
        }

        var numAdaptersGet = GetDelegate<Adl2AdapterNumberOfAdaptersGet>("ADL2_Adapter_NumberOfAdapters_Get");
        var adapterInfoGet = GetDelegate<Adl2AdapterAdapterInfoGet>("ADL2_Adapter_AdapterInfo_Get");
        var displayInfoGet = GetDelegate<Adl2DisplayDisplayInfoGet>("ADL2_Display_DisplayInfo_Get");

        var numAdapters = 0;
        if (numAdaptersGet(_context, ref numAdapters) != AdlOk || numAdapters <= 0)
        {
            throw new InvalidOperationException("No AMD adapters");
        }

        var infoSize = Marshal.SizeOf<AdapterInfo>();
        var adapterBuffer = Marshal.AllocHGlobal(infoSize * numAdapters);
        try
        {
            for (var i = 0; i < numAdapters; i++)
            {
                var ptr = adapterBuffer + (i * infoSize);
                Marshal.WriteInt32(ptr, infoSize);
            }

            if (adapterInfoGet(_context, adapterBuffer, infoSize * numAdapters) != AdlOk)
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
                if (displayInfoGet(_context, adapter.iAdapterIndex, ref numDisplays, ref displayList, 1) != AdlOk ||
                    numDisplays <= 0 || displayList == IntPtr.Zero)
                {
                    continue;
                }

                var displaySize = Marshal.SizeOf<AdlDisplayInfo>();
                for (var d = 0; d < numDisplays; d++)
                {
                    var info = Marshal.PtrToStructure<AdlDisplayInfo>(displayList + (d * displaySize));
                    _adapter = adapter.iAdapterIndex;
                    _display = info.displayID.iDisplayLogicalIndex;
                    return;
                }
            }
        }
        finally
        {
            Marshal.FreeHGlobal(adapterBuffer);
        }

        throw new InvalidOperationException("No AMD display found");
    }
}
