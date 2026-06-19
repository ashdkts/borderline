//go:build windows

package main

import (
	"strings"
	"unsafe"
)

func detectGPUVendor() string {
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
		text := strings.ToUpper(utf16ToString(dd.DeviceString[:]))
		switch {
		case strings.Contains(text, "AMD"), strings.Contains(text, "RADEON"), strings.Contains(text, "ATI"):
			return "amd"
		case strings.Contains(text, "NVIDIA"):
			return "nvidia"
		case strings.Contains(text, "INTEL"):
			return "intel"
		}
	}
	return "generic"
}
