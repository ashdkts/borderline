using System.Runtime.InteropServices;

namespace Borderline;

internal static class AmdDisplay
{
    private const int ADL_OK = 0;

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate IntPtr ADLMainMemoryAlloc(int size);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
    private struct ADLDisplayInfo
    {
        public int iDisplayIndex;
        public int iDisplayLogicalAdapterIndex;
        public int iDisplayLogicalIndex;
        public int iDisplayControllerIndex;
        public int displayType;
        public int iDisplayOutputIndex;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
        public string szDisplayName;
        public int iDisplayConnector;
    }

    [DllImport("atiadlxx.dll", CallingConvention = CallingConvention.StdCall)]
    private static extern int ADL_Main_Control_Create(ADLMainMemoryAlloc callback, int enumConnectedAdapters);

    [DllImport("atiadlxx.dll", CallingConvention = CallingConvention.StdCall)]
    private static extern int ADL_Main_Control_Destroy();

    [DllImport("atiadlxx.dll", CallingConvention = CallingConvention.StdCall)]
    private static extern int ADL_Adapter_NumberOfAdapters_Get(ref int numAdapters);

    [DllImport("atiadlxx.dll", CallingConvention = CallingConvention.StdCall)]
    private static extern int ADL_Adapter_Active_Get(int adapterIndex, ref int active);

    [DllImport("atiadlxx.dll", CallingConvention = CallingConvention.StdCall)]
    private static extern int ADL_Display_DisplayInfo_Get(int adapterIndex, ref int numDisplays, IntPtr info, int forceRefresh);

    [DllImport("atiadlxx.dll", CallingConvention = CallingConvention.StdCall)]
    private static extern int ADL_Display_UnderscanState_Set(int adapterIndex, int displayIndex, int enabled);

    [DllImport("atiadlxx.dll", CallingConvention = CallingConvention.StdCall)]
    private static extern int ADL_Display_Underscan_Get(
        int adapterIndex, int displayIndex,
        ref int current, ref int defaultVal, ref int min, ref int max, ref int step);

    [DllImport("atiadlxx.dll", CallingConvention = CallingConvention.StdCall)]
    private static extern int ADL_Display_Underscan_Set(int adapterIndex, int displayIndex, int current);

    [DllImport("atiadlxx.dll", CallingConvention = CallingConvention.StdCall)]
    private static extern int ADL_Flush_Driver_Data();

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr VirtualAlloc(IntPtr lpAddress, UIntPtr dwSize, uint flAllocationType, uint flProtect);

    private static readonly ADLMainMemoryAlloc AllocCallback = size =>
    {
        if (size <= 0)
        {
            return IntPtr.Zero;
        }

        return VirtualAlloc(IntPtr.Zero, (UIntPtr)(uint)size, 0x3000, 0x04);
    };

    private static bool _initialized;
    private static int _adapter = -1;
    private static int _display = -1;
    private static int _originalUnderscan;

    private static void EnsureInit()
    {
        if (_initialized)
        {
            return;
        }

        if (ADL_Main_Control_Create(AllocCallback, 1) != ADL_OK)
        {
            throw new InvalidOperationException("AMD ADL initialization failed.");
        }

        _initialized = true;
    }

    /// <summary>Returns null on success (message in out param), or error reason string.</summary>
    public static string? TryApply(int top, int bottom, int left, int right, out string? message)
    {
        message = null;
        try
        {
            EnsureInit();
        }
        catch (Exception ex)
        {
            return ex.Message;
        }

        if (!FindDisplay(out var adapter, out var display, out var findError))
        {
            return findError;
        }

        var current = 0;
        var defaultVal = 0;
        var min = 0;
        var max = 0;
        var step = 0;
        if (ADL_Display_Underscan_Get(adapter, display, ref current, ref defaultVal, ref min, ref max, ref step) != ADL_OK)
        {
            return "underscan not supported on this display";
        }

        if (_adapter < 0)
        {
            _adapter = adapter;
            _display = display;
            _originalUnderscan = current;
        }

        if (!NativeDisplay.TryGetResolution(out var w, out var h))
        {
            return "could not read display resolution";
        }

        var margin = Math.Max(Math.Max(top, bottom), Math.Max(left, right));
        var dim = Math.Min(w, h);
        var target = dim <= 0 ? min : Math.Clamp(margin * 100 * 2 / dim, min, max);

        if (ADL_Display_UnderscanState_Set(adapter, display, 1) != ADL_OK)
        {
            return "could not enable underscan";
        }

        if (ADL_Display_Underscan_Set(adapter, display, target) != ADL_OK)
        {
            return "could not set underscan value";
        }

        ADL_Flush_Driver_Data();

        var note = (top != bottom || left != right)
            ? " Largest margin used (AMD driver limit)."
            : "";

        message = $"AMD underscan {target}% via driver.{note}";
        return null;
    }

    public static bool Restore()
    {
        if (_adapter < 0)
        {
            return false;
        }

        try
        {
            EnsureInit();
        }
        catch
        {
            return false;
        }

        ADL_Display_Underscan_Set(_adapter, _display, _originalUnderscan);
        ADL_Display_UnderscanState_Set(_adapter, _display, 0);
        ADL_Flush_Driver_Data();
        _adapter = -1;
        return true;
    }

    private static bool FindDisplay(out int adapter, out int display, out string error)
    {
        adapter = 0;
        display = 0;
        error = "no AMD adapter found";

        var numAdapters = 0;
        if (ADL_Adapter_NumberOfAdapters_Get(ref numAdapters) != ADL_OK || numAdapters <= 0)
        {
            return false;
        }

        var infoSize = Marshal.SizeOf<ADLDisplayInfo>();

        for (adapter = 0; adapter < numAdapters; adapter++)
        {
            var active = 0;
            if (ADL_Adapter_Active_Get(adapter, ref active) != ADL_OK || active == 0)
            {
                continue;
            }

            var numDisplays = 0;
            if (ADL_Display_DisplayInfo_Get(adapter, ref numDisplays, IntPtr.Zero, 0) != ADL_OK || numDisplays <= 0)
            {
                continue;
            }

            var buffer = Marshal.AllocHGlobal(numDisplays * infoSize);
            try
            {
                if (ADL_Display_DisplayInfo_Get(adapter, ref numDisplays, buffer, 1) != ADL_OK)
                {
                    continue;
                }

                for (var i = 0; i < numDisplays; i++)
                {
                    var ptr = buffer + (i * infoSize);
                    var info = Marshal.PtrToStructure<ADLDisplayInfo>(ptr);
                    display = info.iDisplayIndex;

                    var current = 0;
                    var defaultVal = 0;
                    var min = 0;
                    var max = 0;
                    var step = 0;
                    if (ADL_Display_Underscan_Get(adapter, display, ref current, ref defaultVal, ref min, ref max, ref step) == ADL_OK)
                    {
                        error = string.Empty;
                        return true;
                    }
                }
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }

        error = "underscan not supported on connected displays";
        return false;
    }
}
