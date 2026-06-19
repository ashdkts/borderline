//go:build windows

package main

import (
	"fmt"
	"sync"
	"syscall"
	"unsafe"
)

const adlOK = 0

var (
	modADL = syscall.NewLazyDLL("atiadlxx.dll")
	procVirtualAlloc              = modKernel32.NewProc("VirtualAlloc")

	procADLMainControlCreate      = modADL.NewProc("ADL_Main_Control_Create")
	procADLMainControlDestroy     = modADL.NewProc("ADL_Main_Control_Destroy")
	procADLAdapterNumberOfAdapters = modADL.NewProc("ADL_Adapter_NumberOfAdapters_Get")
	procADLAdapterActiveGet       = modADL.NewProc("ADL_Adapter_Active_Get")
	procADLDisplayDisplayInfoGet  = modADL.NewProc("ADL_Display_DisplayInfo_Get")
	procADLDisplayUnderscanSupport = modADL.NewProc("ADL_Display_UnderscanSupport_Get")
	procADLDisplayUnderscanStateSet = modADL.NewProc("ADL_Display_UnderscanState_Set")
	procADLDisplayUnderscanGet    = modADL.NewProc("ADL_Display_Underscan_Get")
	procADLDisplayUnderscanSet    = modADL.NewProc("ADL_Display_Underscan_Set")
	procADLFlushDriverData        = modADL.NewProc("ADL_Flush_Driver_Data")
)

type amdState struct {
	mu            sync.Mutex
	initialized   bool
	adapterIndex  int
	displayIndex  int
	defaultValue  int
	originalValue int
	hasBackup     bool
}

var amd amdState

func adlMalloc(size int) uintptr {
	if size <= 0 {
		return 0
	}
	mem, _, _ := procVirtualAlloc.Call(0, uintptr(size), 0x3000, 0x04)
	return mem
}

func amdInit() error {
	amd.mu.Lock()
	defer amd.mu.Unlock()
	if amd.initialized {
		return nil
	}
	if err := modADL.Load(); err != nil {
		return err
	}

	callback := syscall.NewCallback(adlMalloc)
	ret, _, _ := procADLMainControlCreate.Call(callback, 1)
	if int32(ret) != adlOK {
		return fmt.Errorf("ADL_Main_Control_Create failed (%d)", ret)
	}
	amd.initialized = true
	return nil
}

func amdShutdown() {
	amd.mu.Lock()
	defer amd.mu.Unlock()
	if !amd.initialized {
		return
	}
	_, _, _ = procADLMainControlDestroy.Call()
	amd.initialized = false
}

func amdFindPrimaryDisplay() (int, int, error) {
	var numAdapters int32
	ret, _, _ := procADLAdapterNumberOfAdapters.Call(uintptr(unsafe.Pointer(&numAdapters)))
	if int32(ret) != adlOK {
		return 0, 0, fmt.Errorf("ADL_Adapter_NumberOfAdapters_Get failed")
	}

	for adapter := int32(0); adapter < numAdapters; adapter++ {
		var active int32
		ret, _, _ = procADLAdapterActiveGet.Call(uintptr(adapter), uintptr(unsafe.Pointer(&active)))
		if int32(ret) != adlOK || active == 0 {
			continue
		}

		var numDisplays int32
		ret, _, _ = procADLDisplayDisplayInfoGet.Call(uintptr(adapter), uintptr(unsafe.Pointer(&numDisplays)), 0)
		if int32(ret) != adlOK || numDisplays <= 0 {
			continue
		}

		// ADLDisplayInfo is ~296 bytes in current ADL headers.
		const displayInfoSize = 296
		buf := make([]byte, int(numDisplays)*displayInfoSize)
		ret, _, _ = procADLDisplayDisplayInfoGet.Call(
			uintptr(adapter),
			uintptr(unsafe.Pointer(&numDisplays)),
			uintptr(unsafe.Pointer(&buf[0])),
		)
		if int32(ret) != adlOK {
			continue
		}

		for i := int32(0); i < numDisplays; i++ {
			off := int(i) * displayInfoSize
			displayIndex := *(*int32)(unsafe.Pointer(&buf[off]))
			var supported int32
			ret, _, _ = procADLDisplayUnderscanSupport.Call(
				uintptr(adapter),
				uintptr(displayIndex),
				uintptr(unsafe.Pointer(&supported)),
			)
			if int32(ret) == adlOK && supported != 0 {
				return int(adapter), int(displayIndex), nil
			}
		}
	}

	return 0, 0, fmt.Errorf("no AMD display with underscan support")
}

func amdMapMargins(s Settings, width, height uint32, min, max int) int {
	margin := maxInt(s.Top, s.Bottom, s.Left, s.Right)
	if margin == 0 {
		return min
	}
	dim := int(width)
	if int(height) < dim {
		dim = int(height)
	}
	if dim <= 0 {
		return min
	}
	value := margin * 100 * 2 / dim
	if value < min {
		return min
	}
	if value > max {
		return max
	}
	return value
}

func applyAMDMargins(s Settings) (string, error) {
	if err := amdInit(); err != nil {
		return "", err
	}
	defer amdShutdown()

	adapter, display, err := amdFindPrimaryDisplay()
	if err != nil {
		return "", err
	}

	var current, defaultVal, min, max, step int32
	ret, _, _ := procADLDisplayUnderscanGet.Call(
		uintptr(adapter),
		uintptr(display),
		uintptr(unsafe.Pointer(&current)),
		uintptr(unsafe.Pointer(&defaultVal)),
		uintptr(unsafe.Pointer(&min)),
		uintptr(unsafe.Pointer(&max)),
		uintptr(unsafe.Pointer(&step)),
	)
	if int32(ret) != adlOK {
		return "", fmt.Errorf("ADL_Display_Underscan_Get failed (%d)", ret)
	}

	device, err := primaryDisplayDevice()
	if err != nil {
		return "", err
	}
	cur, err := currentDevMode(device)
	if err != nil {
		return "", err
	}

	if !amd.hasBackup {
		amd.adapterIndex = adapter
		amd.displayIndex = display
		amd.originalValue = int(current)
		amd.defaultValue = int(defaultVal)
		amd.hasBackup = true
	}

	target := amdMapMargins(s, cur.PelsWidth, cur.PelsHeight, int(min), int(max))

	ret, _, _ = procADLDisplayUnderscanStateSet.Call(uintptr(adapter), uintptr(display), 1)
	if int32(ret) != adlOK {
		return "", fmt.Errorf("ADL_Display_UnderscanState_Set failed (%d)", ret)
	}

	ret, _, _ = procADLDisplayUnderscanSet.Call(uintptr(adapter), uintptr(display), uintptr(target))
	if int32(ret) != adlOK {
		return "", fmt.Errorf("ADL_Display_Underscan_Set failed (%d)", ret)
	}
	_, _, _ = procADLFlushDriverData.Call()

	note := ""
	if s.Top != s.Bottom || s.Left != s.Right {
		note = " (AMD uses uniform underscan; per-edge values use the largest margin)"
	}

	return fmt.Sprintf("AMD underscan set to %d%% via driver%s.", target, note), nil
}

func amdRestore() bool {
	if !amd.hasBackup {
		return false
	}
	if err := amdInit(); err != nil {
		return false
	}
	defer amdShutdown()

	adapter := amd.adapterIndex
	display := amd.displayIndex

	_, _, _ = procADLDisplayUnderscanSet.Call(uintptr(adapter), uintptr(display), uintptr(amd.originalValue))
	_, _, _ = procADLDisplayUnderscanStateSet.Call(uintptr(adapter), uintptr(display), 0)
	_, _, _ = procADLFlushDriverData.Call()
	amd.hasBackup = false
	return true
}
