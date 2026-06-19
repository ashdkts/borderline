using System.Runtime.InteropServices;

namespace Borderline;

internal static class AmdDisplay
{
    private const int ADL_OK = 0;

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate IntPtr ADLMainMemoryAlloc(int size);

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
    private static extern int ADL_Display_UnderscanSupport_Get(int adapterIndex, int displayIndex, ref int supported);

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

    public static bool Available
    {
        get
        {
            try
            {
                return ADL_Main_Control_Create(AllocCallback, 1) == ADL_OK;
            }
            catch (DllNotFoundException)
            {
                return false;
            }
            finally
            {
                if (_initialized)
                {
                    ADL_Main_Control_Destroy();
                    _initialized = false;
                }
            }
        }
    }

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

    public static string Apply(int top, int bottom, int left, int right)
    {
        EnsureInit();
        FindDisplay(out var adapter, out var display);

        var current = 0;
        var defaultVal = 0;
        var min = 0;
        var max = 0;
        var step = 0;
        if (ADL_Display_Underscan_Get(adapter, display, ref current, ref defaultVal, ref min, ref max, ref step) != ADL_OK)
        {
            throw new InvalidOperationException("Could not read AMD underscan settings.");
        }

        if (_adapter < 0)
        {
            _adapter = adapter;
            _display = display;
            _originalUnderscan = current;
        }

        if (!NativeDisplay.TryGetResolution(out var w, out var h))
        {
            throw new InvalidOperationException("Could not read display resolution.");
        }

        var margin = Math.Max(Math.Max(top, bottom), Math.Max(left, right));
        var dim = Math.Min(w, h);
        var target = dim <= 0 ? min : Math.Clamp(margin * 100 * 2 / dim, min, max);

        if (ADL_Display_UnderscanState_Set(adapter, display, 1) != ADL_OK)
        {
            throw new InvalidOperationException("Could not enable AMD underscan.");
        }

        if (ADL_Display_Underscan_Set(adapter, display, target) != ADL_OK)
        {
            throw new InvalidOperationException("Could not set AMD underscan value.");
        }

        ADL_Flush_Driver_Data();

        var note = (top != bottom || left != right)
            ? " Per-edge values use the largest margin (AMD driver limit)."
            : "";

        return $"AMD underscan set to {target}% via driver.{note}";
    }

    public static bool Restore()
    {
        if (_adapter < 0 || !_initialized)
        {
            return false;
        }

        ADL_Display_Underscan_Set(_adapter, _display, _originalUnderscan);
        ADL_Display_UnderscanState_Set(_adapter, _display, 0);
        ADL_Flush_Driver_Data();
        _adapter = -1;
        return true;
    }

    public static void Shutdown()
    {
        if (_initialized)
        {
            ADL_Main_Control_Destroy();
            _initialized = false;
        }
    }

    private static void FindDisplay(out int adapter, out int display)
    {
        var numAdapters = 0;
        if (ADL_Adapter_NumberOfAdapters_Get(ref numAdapters) != ADL_OK)
        {
            throw new InvalidOperationException("Could not enumerate AMD adapters.");
        }

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

            const int infoSize = 296;
            var buffer = Marshal.AllocHGlobal(numDisplays * infoSize);
            try
            {
                if (ADL_Display_DisplayInfo_Get(adapter, ref numDisplays, buffer, 1) != ADL_OK)
                {
                    continue;
                }

                for (var i = 0; i < numDisplays; i++)
                {
                    var displayIndex = Marshal.ReadInt32(buffer, i * infoSize);
                    var supported = 0;
                    if (ADL_Display_UnderscanSupport_Get(adapter, displayIndex, ref supported) == ADL_OK && supported != 0)
                    {
                        display = displayIndex;
                        return;
                    }
                }
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }

        throw new InvalidOperationException("No AMD display with underscan support found.");
    }
}
