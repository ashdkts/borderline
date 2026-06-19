using System.Runtime.InteropServices;

namespace Borderline;

internal static class NativeDisplay
{
    private const int ENUM_CURRENT_SETTINGS = -1;
    private const int DISP_CHANGE_SUCCESSFUL = 0;
    private const int DISP_CHANGE_RESTART = 1;
    private const int DISP_CHANGE_BADMODE = -2;

    private const int DM_PELSWIDTH = 0x80000;
    private const int DM_PELSHEIGHT = 0x100000;
    private const int DM_DISPLAYFREQUENCY = 0x400000;

    [Flags]
    private enum ChangeDisplaySettingsFlags : uint
    {
        CDS_UPDATEREGISTRY = 0x01,
        CDS_TEST = 0x02,
        CDS_ENABLE_UNSAFE_MODES = 0x100,
        CDS_RESET = 0x40000000,
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DEVMODE
    {
        private const int CCHDEVICENAME = 32;
        private const int CCHFORMNAME = 32;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = CCHDEVICENAME)]
        public string dmDeviceName;
        public short dmSpecVersion;
        public short dmDriverVersion;
        public short dmSize;
        public short dmDriverExtra;
        public int dmFields;
        public int dmPositionX;
        public int dmPositionY;
        public int dmDisplayOrientation;
        public int dmDisplayFixedOutput;
        public short dmColor;
        public short dmDuplex;
        public short dmYResolution;
        public short dmTTCOption;
        public short dmCollate;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = CCHFORMNAME)]
        public string dmFormName;
        public short dmLogPixels;
        public int dmBitsPerPel;
        public int dmPelsWidth;
        public int dmPelsHeight;
        public int dmDisplayFlags;
        public int dmDisplayFrequency;
        public int dmICMMethod;
        public int dmICMIntent;
        public int dmMediaType;
        public int dmDitherType;
        public int dmReserved1;
        public int dmReserved2;
        public int dmPanningWidth;
        public int dmPanningHeight;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool EnumDisplaySettings(string? lpszDeviceName, int iModeNum, ref DEVMODE lpDevMode);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int ChangeDisplaySettingsEx(
        string? lpszDeviceName,
        ref DEVMODE lpDevMode,
        IntPtr hwnd,
        ChangeDisplaySettingsFlags dwflags,
        IntPtr lParam);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int ChangeDisplaySettingsEx(
        string? lpszDeviceName,
        IntPtr lpDevMode,
        IntPtr hwnd,
        ChangeDisplaySettingsFlags dwflags,
        IntPtr lParam);

    private static DEVMODE? _backup;
    private static string? _deviceName;

    public static string ApplyExactMode(
        int top, int bottom, int left, int right,
        bool enableUnsafeModes,
        out int appliedW,
        out int appliedH)
    {
        appliedW = 0;
        appliedH = 0;
        if (!TryGetCurrentMode(out var current, out var device))
        {
            throw new InvalidOperationException("Could not read current display mode.");
        }

        if (_backup is null || _deviceName != device)
        {
            _backup = current;
            _deviceName = device;
        }

        var newW = current.dmPelsWidth - left - right;
        var newH = current.dmPelsHeight - top - bottom;
        if (newW < 320 || newH < 240)
        {
            throw new InvalidOperationException(
                $"Margins too large for {current.dmPelsWidth}x{current.dmPelsHeight}.");
        }

        ApplyModeInternal(device, current, newW, newH, enableUnsafeModes, out appliedW, out appliedH);
        return $"Custom mode {appliedW}x{appliedH} @ {current.dmDisplayFrequency}Hz (was {current.dmPelsWidth}x{current.dmPelsHeight}).";
    }

    public static string ApplyCustomMode(int top, int bottom, int left, int right, bool enableUnsafeModes)
    {
        try
        {
            return ApplyExactMode(top, bottom, left, right, enableUnsafeModes, out _, out _);
        }
        catch (InvalidOperationException) when (top != bottom || left != right)
        {
            if (!TryGetCurrentMode(out var current, out var device))
            {
                throw;
            }

            var uniform = Math.Max(Math.Max(top, bottom), Math.Max(left, right));
            var newW = current.dmPelsWidth - uniform * 2;
            var newH = current.dmPelsHeight - uniform * 2;
            ApplyModeInternal(device, current, newW, newH, enableUnsafeModes, out var aw, out var ah);
            return $"Custom mode {aw}x{ah} (uniform {uniform}px margins; asymmetric rejected by driver).";
        }
    }

    private static void ApplyModeInternal(
        string device, DEVMODE current, int newW, int newH, bool enableUnsafeModes,
        out int appliedW, out int appliedH)
    {
        appliedW = newW;
        appliedH = newH;

        if (enableUnsafeModes)
        {
            ChangeDisplaySettingsEx(NormalizeDevice(device), IntPtr.Zero, IntPtr.Zero,
                ChangeDisplaySettingsFlags.CDS_ENABLE_UNSAFE_MODES, IntPtr.Zero);
        }

        var mode = current;
        mode.dmFields = current.dmFields | DM_PELSWIDTH | DM_PELSHEIGHT | DM_DISPLAYFREQUENCY;
        mode.dmPelsWidth = newW;
        mode.dmPelsHeight = newH;

        TestOrApply(device, ref mode, testOnly: true);
        var code = TestOrApply(device, ref mode, testOnly: false);
        if (code == DISP_CHANGE_RESTART)
        {
            // Caller may append restart note if needed.
        }
    }

    public static string Restore()
    {
        if (_backup is not null && _deviceName is not null)
        {
            var mode = _backup.Value;
            TestOrApply(_deviceName, ref mode, testOnly: false);
            _backup = null;
            return $"Restored to {mode.dmPelsWidth}x{mode.dmPelsHeight}.";
        }

        ChangeDisplaySettingsEx(null, IntPtr.Zero, IntPtr.Zero, ChangeDisplaySettingsFlags.CDS_RESET, IntPtr.Zero);
        return "Display reset to system default.";
    }

    private static string? NormalizeDevice(string device) =>
        string.IsNullOrEmpty(device) ? null : device;

    private static int TestOrApply(string device, ref DEVMODE mode, bool testOnly)
    {
        mode.dmSize = (short)Marshal.SizeOf<DEVMODE>();
        var flags = testOnly
            ? ChangeDisplaySettingsFlags.CDS_TEST
            : ChangeDisplaySettingsFlags.CDS_UPDATEREGISTRY;

        var code = ChangeDisplaySettingsEx(NormalizeDevice(device), ref mode, IntPtr.Zero, flags, IntPtr.Zero);
        if (code != DISP_CHANGE_SUCCESSFUL && code != DISP_CHANGE_RESTART)
        {
            throw new InvalidOperationException(DescribeError(code));
        }

        return code;
    }

    private static string DescribeError(int code) => code switch
    {
        DISP_CHANGE_BADMODE => "display mode rejected by driver (code -2). Enable custom resolutions in AMD Software if available.",
        -1 => "display change failed (code -1)",
        _ => $"ChangeDisplaySettingsEx failed (code {code})",
    };

    private static bool TryGetCurrentMode(out DEVMODE mode, out string device)
    {
        device = string.Empty;
        mode = default;
        mode.dmSize = (short)Marshal.SizeOf<DEVMODE>();

        for (var i = 0; EnumDisplayDevices(i, out var name, out _); i++)
        {
            var dm = new DEVMODE { dmSize = (short)Marshal.SizeOf<DEVMODE>() };
            if (EnumDisplaySettings(name, ENUM_CURRENT_SETTINGS, ref dm))
            {
                device = name;
                mode = dm;
                return true;
            }
        }

        var primary = new DEVMODE { dmSize = (short)Marshal.SizeOf<DEVMODE>() };
        if (EnumDisplaySettings(null, ENUM_CURRENT_SETTINGS, ref primary))
        {
            device = "";
            mode = primary;
            return true;
        }

        return false;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "EnumDisplayDevicesW")]
    private static extern bool NativeEnumDisplayDevices(string? lpDevice, uint iDevNum, ref DISPLAY_DEVICE lpDisplayDevice, uint dwFlags);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DISPLAY_DEVICE
    {
        public int cb;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string DeviceName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string DeviceString;
        public int StateFlags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string DeviceID;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string DeviceKey;
    }

    private const int DISPLAY_DEVICE_ATTACHED_TO_DESKTOP = 0x1;
    private const int DISPLAY_DEVICE_PRIMARY_DEVICE = 0x4;

    private static bool EnumDisplayDevices(int index, out string deviceName, out string deviceString)
    {
        var dd = new DISPLAY_DEVICE { cb = Marshal.SizeOf<DISPLAY_DEVICE>() };
        if (!NativeEnumDisplayDevices(null, (uint)index, ref dd, 0))
        {
            deviceName = string.Empty;
            deviceString = string.Empty;
            return false;
        }

        deviceName = dd.DeviceName;
        deviceString = dd.DeviceString;
        return (dd.StateFlags & DISPLAY_DEVICE_ATTACHED_TO_DESKTOP) != 0;
    }

    public static GpuVendor DetectVendor()
    {
        for (var i = 0; EnumDisplayDevices(i, out _, out var desc); i++)
        {
            var u = desc.ToUpperInvariant();
            if (u.Contains("AMD") || u.Contains("RADEON") || u.Contains("ATI"))
            {
                return GpuVendor.Amd;
            }

            if (u.Contains("NVIDIA"))
            {
                return GpuVendor.Nvidia;
            }

            if (u.Contains("INTEL"))
            {
                return GpuVendor.Intel;
            }
        }

        return GpuVendor.Generic;
    }

    public static bool TryGetResolution(out int width, out int height)
    {
        width = 0;
        height = 0;
        if (!TryGetCurrentMode(out var mode, out _))
        {
            return false;
        }

        width = mode.dmPelsWidth;
        height = mode.dmPelsHeight;
        return true;
    }

    public static bool TryGetRefreshRate(out int hz)
    {
        hz = 60;
        if (!TryGetCurrentMode(out var mode, out _))
        {
            return false;
        }

        hz = mode.dmDisplayFrequency > 0 ? mode.dmDisplayFrequency : 60;
        return true;
    }

    public static string GetPrimaryDeviceName()
    {
        for (var i = 0; ; i++)
        {
            var dd = new DISPLAY_DEVICE { cb = Marshal.SizeOf<DISPLAY_DEVICE>() };
            if (!NativeEnumDisplayDevices(null, (uint)i, ref dd, 0))
            {
                break;
            }

            if ((dd.StateFlags & DISPLAY_DEVICE_PRIMARY_DEVICE) != 0)
            {
                return dd.DeviceName;
            }
        }

        for (var i = 0; EnumDisplayDevices(i, out var name, out _); i++)
        {
            return name;
        }

        return string.Empty;
    }

    public static void RefreshDisplay()
    {
        if (!TryGetCurrentMode(out var mode, out var device))
        {
            return;
        }

        TestOrApply(device, ref mode, testOnly: false);
    }
}

internal enum GpuVendor
{
    Generic,
    Amd,
    Nvidia,
    Intel,
}
