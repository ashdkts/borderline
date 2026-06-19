//go:build windows

package main

import (
	"fmt"
	"syscall"
	"unsafe"
)

const (
	cchDeviceName = 32

	enumCurrentSettings = 0xFFFFFFFF

	dmPelsWidth        = 0x00080000
	dmPelsHeight       = 0x00100000
	dmDisplayFrequency = 0x00400000
	dmPosition         = 0x00000020

	cdsUpdateregistry     = 0x00000001
	cdsGlobal             = 0x00000008
	cdsEnableUnsafeModes  = 0x00000100
	cdsReset              = 0x40000000

	dispChangeSuccessful = 0
	dispChangeRestart    = 1
	dispChangeBadmode    = -2

	displayDeviceAttachedToDesktop = 0x00000001
)

var (
	procEnumDisplayDevices      = modUser32.NewProc("EnumDisplayDevicesW")
	procEnumDisplaySettingsEx   = modUser32.NewProc("EnumDisplaySettingsExW")
	procChangeDisplaySettingsEx = modUser32.NewProc("ChangeDisplaySettingsExW")
)

type displayDevice struct {
	Cb           uint32
	DeviceName   [cchDeviceName]uint16
	DeviceString [128]uint16
	StateFlags   uint32
	DeviceID     [128]uint16
	DeviceKey    [128]uint16
}

// devMode matches DEVMODEW layout for display settings on 64-bit Windows.
type devMode struct {
	DeviceName       [cchDeviceName]uint16
	SpecVersion      uint16
	DriverVersion    uint16
	Size             uint16
	DriverExtra      uint16
	Fields           uint32
	Orientation      int16
	PaperSize        int16
	PaperLength      int16
	PaperWidth       int16
	Scale            int16
	Copies           int16
	DefaultSource    int16
	PrintQuality     int16
	Color            int16
	Duplex           int16
	YResolution      int16
	TTCOption        int16
	Collate          int16
	FormName         [32]uint16
	LogPixels        uint16
	BitsPerPixel     uint32
	PelsWidth        uint32
	PelsHeight       uint32
	DisplayFlags     uint32
	DisplayFrequency uint32
	ICMMethod        uint32
	ICMIntent        uint32
	MediaType        uint32
	DitherType       uint32
	Reserved1        uint32
	Reserved2        uint32
	PanningWidth     uint32
	PanningHeight    uint32
}

type displayDriver struct {
	deviceName string
	original   devMode
	hasBackup  bool
}

var activeDriver displayDriver

func devModeSize() uint16 {
	return uint16(unsafe.Sizeof(devMode{}))
}

func primaryDisplayDevice() (string, error) {
	for i := uint32(0); ; i++ {
		var dd displayDevice
		dd.Cb = uint32(unsafe.Sizeof(dd))
		ok, _, _ := procEnumDisplayDevices.Call(0, uintptr(i), uintptr(unsafe.Pointer(&dd)), 0)
		if ok == 0 {
			break
		}
		if dd.StateFlags&displayDeviceAttachedToDesktop != 0 {
			return utf16ToString(dd.DeviceName[:]), nil
		}
	}
	return "", fmt.Errorf("no display attached to desktop")
}

func utf16ToString(s []uint16) string {
	for i, v := range s {
		if v == 0 {
			return syscall.UTF16ToString(s[:i])
		}
	}
	return syscall.UTF16ToString(s)
}

func currentDevMode(deviceName string) (devMode, error) {
	var dm devMode
	dm.Size = devModeSize()
	namePtr, err := syscall.UTF16PtrFromString(deviceName)
	if err != nil {
		return dm, err
	}
	ok, _, _ := procEnumDisplaySettingsEx.Call(
		uintptr(unsafe.Pointer(namePtr)),
		enumCurrentSettings,
		uintptr(unsafe.Pointer(&dm)),
		0,
	)
	if ok == 0 {
		return dm, fmt.Errorf("EnumDisplaySettingsEx failed for %s", deviceName)
	}
	return dm, nil
}

func enableUnsafeModesForAll() {
	for i := uint32(0); ; i++ {
		var dd displayDevice
		dd.Cb = uint32(unsafe.Sizeof(dd))
		ok, _, _ := procEnumDisplayDevices.Call(0, uintptr(i), uintptr(unsafe.Pointer(&dd)), 0)
		if ok == 0 {
			break
		}
		if dd.StateFlags&displayDeviceAttachedToDesktop == 0 {
			continue
		}
		var dm devMode
		dm.Size = devModeSize()
		_, _, _ = procChangeDisplaySettingsEx.Call(
			uintptr(unsafe.Pointer(&dd.DeviceName[0])),
			uintptr(unsafe.Pointer(&dm)),
			0,
			cdsEnableUnsafeModes,
			0,
		)
	}
}

func changeDisplayMode(deviceName string, dm *devMode) (int32, error) {
	namePtr, err := syscall.UTF16PtrFromString(deviceName)
	if err != nil {
		return 0, err
	}
	dm.Size = devModeSize()
	ret, _, _ := procChangeDisplaySettingsEx.Call(
		uintptr(unsafe.Pointer(namePtr)),
		uintptr(unsafe.Pointer(dm)),
		0,
		cdsUpdateregistry|cdsGlobal,
		0,
	)
	code := int32(ret)
	if code != dispChangeSuccessful && code != dispChangeRestart {
		return code, fmt.Errorf("ChangeDisplaySettingsEx returned %d", code)
	}
	return code, nil
}

func resetDisplayMode() error {
	_, _, _ = procChangeDisplaySettingsEx.Call(0, 0, 0, cdsReset, 0)
	return nil
}

func applyDriverMargins(s Settings) (string, error) {
	if !s.Enabled || (s.Top == 0 && s.Bottom == 0 && s.Left == 0 && s.Right == 0) {
		return restoreDriverMargins()
	}

	device, err := primaryDisplayDevice()
	if err != nil {
		return "", err
	}

	cur, err := currentDevMode(device)
	if err != nil {
		return "", err
	}

	if !activeDriver.hasBackup || activeDriver.deviceName != device {
		activeDriver.original = cur
		activeDriver.deviceName = device
		activeDriver.hasBackup = true
	}

	newW := int(cur.PelsWidth) - s.Left - s.Right
	newH := int(cur.PelsHeight) - s.Top - s.Bottom
	if newW < 320 || newH < 240 {
		return "", fmt.Errorf("margins too large for %dx%d display", cur.PelsWidth, cur.PelsHeight)
	}

	enableUnsafeModesForAll()

	adjusted := cur
	adjusted.Fields = cur.Fields | dmPelsWidth | dmPelsHeight | dmDisplayFrequency
	adjusted.PelsWidth = uint32(newW)
	adjusted.PelsHeight = uint32(newH)

	code, err := changeDisplayMode(device, &adjusted)
	if err != nil {
		// Some GPUs reject asymmetric custom modes; retry uniform underscan scale.
		if s.Top != s.Bottom || s.Left != s.Right {
			uniform := max(s.Top, s.Bottom, s.Left, s.Right)
			adjusted.PelsWidth = cur.PelsWidth - uint32(uniform*2)
			adjusted.PelsHeight = cur.PelsHeight - uint32(uniform*2)
			code, err = changeDisplayMode(device, &adjusted)
		}
		if err != nil {
			return "", err
		}
	}

	msg := fmt.Sprintf("Driver mode %dx%d @ %dHz (was %dx%d). Unused panel area is blank — not drawn by Borderline.",
		adjusted.PelsWidth, adjusted.PelsHeight, adjusted.DisplayFrequency,
		cur.PelsWidth, cur.PelsHeight)
	if code == dispChangeRestart {
		msg += " Restart may be required."
	}
	return msg, nil
}

func restoreDriverMargins() (string, error) {
	if !activeDriver.hasBackup {
		_ = resetDisplayMode()
		return "Display mode reset.", nil
	}

	_, err := changeDisplayMode(activeDriver.deviceName, &activeDriver.original)
	if err != nil {
		_ = resetDisplayMode()
		return "Restored via system reset.", err
	}
	return fmt.Sprintf("Restored %dx%d.", activeDriver.original.PelsWidth, activeDriver.original.PelsHeight), nil
}

func max(vals ...int) int {
	m := vals[0]
	for _, v := range vals[1:] {
		if v > m {
			m = v
		}
	}
	return m
}
