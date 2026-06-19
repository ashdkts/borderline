//go:build windows

package main

import (
	"fmt"
	"syscall"
	"unsafe"
)

const (
	idTopSlider    = 1001
	idBottomSlider = 1002
	idLeftSlider   = 1003
	idRightSlider  = 1004
	idApplyBtn     = 1010
	idResetBtn     = 1011
	idEnableBtn    = 1012
)

const (
	marginMax  = 500
	labelWidth = 120
	sliderW    = 220
	rowHeight  = 36
)

var (
	appInstance     syscall.Handle
	mainWnd         syscall.Handle
	enableBtnHandle syscall.Handle
	current         Settings
	controls        struct {
		top, bottom, left, right syscall.Handle
		status                   syscall.Handle
	}
)

func main() {
	initCommonControls()

	instance, _, _ := procGetModuleHandle.Call(0)
	appInstance = syscall.Handle(instance)
	mustRegisterMainClass(appInstance)

	current = loadSettings()

	mainWnd = createMainWindow(appInstance)
	layoutControls(mainWnd)
	syncControlsFromSettings()
	if current.Enabled {
		applyFromUI()
	}
	updateEnableButtonCaption()

	procShowWindow.Call(uintptr(mainWnd), swShow)
	procUpdateWindow.Call(uintptr(mainWnd))

	var message msg
	for {
		ret, _, _ := procGetMessage.Call(
			uintptr(unsafe.Pointer(&message)),
			0,
			0,
			0,
		)
		if ret == 0 {
			break
		}
		procTranslateMessage.Call(uintptr(unsafe.Pointer(&message)))
		procDispatchMessage.Call(uintptr(unsafe.Pointer(&message)))
	}

	if current.Enabled {
		_, _ = restoreDriverMargins()
	}
}

func mustRegisterMainClass(instance syscall.Handle) {
	className := utf16("BorderlineMainWindow")
	wcx := wndClassEx{
		Size:      uint32(unsafe.Sizeof(wndClassEx{})),
		WndProc:   syscall.NewCallback(mainWindowProc),
		Instance:  instance,
		ClassName: className,
	}
	cursor, _, _ := procLoadCursor.Call(0, idcArrow)
	wcx.Cursor = syscall.Handle(cursor)
	if ret, _, _ := procRegisterClassEx.Call(uintptr(unsafe.Pointer(&wcx))); ret == 0 {
		panic("RegisterClassExW failed for main window")
	}
}

func createMainWindow(instance syscall.Handle) syscall.Handle {
	hwnd, _, _ := procCreateWindowEx.Call(
		0,
		uintptr(unsafe.Pointer(utf16("BorderlineMainWindow"))),
		uintptr(unsafe.Pointer(utf16("Borderline — Display margins"))),
		wsOverlappedWindow|wsVisible,
		cwUseDefault,
		cwUseDefault,
		420,
		340,
		0,
		0,
		uintptr(instance),
		0,
	)
	if hwnd == 0 {
		panic("CreateWindowExW failed for main window")
	}
	return syscall.Handle(hwnd)
}

func layoutControls(parent syscall.Handle) {
	y := 16
	controls.top = createSliderRow(parent, "Top (px)", idTopSlider, 16, y)
	y += rowHeight
	controls.bottom = createSliderRow(parent, "Bottom (px)", idBottomSlider, 16, y)
	y += rowHeight
	controls.left = createSliderRow(parent, "Left (px)", idLeftSlider, 16, y)
	y += rowHeight
	controls.right = createSliderRow(parent, "Right (px)", idRightSlider, 16, y)
	y += rowHeight + 8

	createButton(parent, "Apply", idApplyBtn, 16, y, 90, 28)
	createButton(parent, "Reset", idResetBtn, 116, y, 90, 28)
	createButton(parent, "Enable margins", idEnableBtn, 216, y, 120, 28)
	y += 40

	controls.status = createStatic(parent,
		"Applies a driver-level custom resolution so the GPU leaves unused panel area (blank, not an overlay).",
		16, y, 380, 56)
}

func createStatic(parent syscall.Handle, text string, x, y, w, h int) syscall.Handle {
	hwnd, _, _ := procCreateWindowEx.Call(
		0,
		uintptr(unsafe.Pointer(utf16("STATIC"))),
		uintptr(unsafe.Pointer(utf16(text))),
		wsVisibleChild|ssLeft,
		uintptr(x), uintptr(y), uintptr(w), uintptr(h),
		uintptr(parent), 0, uintptr(appInstance), 0,
	)
	return syscall.Handle(hwnd)
}

func createButton(parent syscall.Handle, caption string, id, x, y, w, h int) syscall.Handle {
	hwnd, _, _ := procCreateWindowEx.Call(
		0,
		uintptr(unsafe.Pointer(utf16("BUTTON"))),
		uintptr(unsafe.Pointer(utf16(caption))),
		wsVisibleChild|bsPushButton,
		uintptr(x), uintptr(y), uintptr(w), uintptr(h),
		uintptr(parent), uintptr(id), uintptr(appInstance), 0,
	)
	if id == idEnableBtn {
		enableBtnHandle = syscall.Handle(hwnd)
	}
	return syscall.Handle(hwnd)
}

func createSliderRow(parent syscall.Handle, label string, id, x, y int) syscall.Handle {
	createStatic(parent, label, x, y+4, labelWidth, 20)
	hwnd, _, _ := procCreateWindowEx.Call(
		0,
		uintptr(unsafe.Pointer(utf16("msctls_trackbar32"))),
		0,
		wsVisibleChild|tbsHorz|tbsAutoticks,
		uintptr(x+labelWidth), uintptr(y), uintptr(sliderW), uintptr(24),
		uintptr(parent), uintptr(id), uintptr(appInstance), 0,
	)
	procSendMessage.Call(uintptr(hwnd), tbmSetrange, 1, makelong(0, marginMax))
	return syscall.Handle(hwnd)
}

func syncControlsFromSettings() {
	setSliderPos(controls.top, current.Top)
	setSliderPos(controls.bottom, current.Bottom)
	setSliderPos(controls.left, current.Left)
	setSliderPos(controls.right, current.Right)
}

func setSliderPos(hwnd syscall.Handle, value int) {
	procSendMessage.Call(uintptr(hwnd), tbmSetpos, 1, uintptr(value))
}

func sliderPos(hwnd syscall.Handle) int {
	ret, _, _ := procSendMessage.Call(uintptr(hwnd), tbmGetpos, 0, 0)
	return int(ret)
}

func applyFromUI() {
	current.Top = clampMargin(sliderPos(controls.top))
	current.Bottom = clampMargin(sliderPos(controls.bottom))
	current.Left = clampMargin(sliderPos(controls.left))
	current.Right = clampMargin(sliderPos(controls.right))

	var status string
	if current.Enabled {
		msg, err := applyDriverMargins(current)
		if err != nil {
			status = "Error: " + err.Error()
		} else {
			status = msg
		}
	} else {
		msg, _ := restoreDriverMargins()
		status = msg
	}

	_ = saveSettings(current)
	setStatus(fmt.Sprintf("%s  [%s]", status, enabledLabel()))
}

func resetSettings() {
	if current.Enabled {
		current.Enabled = false
		_, _ = restoreDriverMargins()
	}
	current = defaultSettings()
	syncControlsFromSettings()
	setStatus("Reset to defaults.")
	_ = saveSettings(current)
	updateEnableButtonCaption()
}

func toggleEnabled() {
	current.Enabled = !current.Enabled
	applyFromUI()
}

func setStatus(text string) {
	procSetWindowText.Call(uintptr(controls.status), uintptr(unsafe.Pointer(utf16(text))))
}

func enabledLabel() string {
	if current.Enabled {
		return "ON"
	}
	return "OFF"
}

func updateEnableButtonCaption() {
	label := "Enable margins"
	if current.Enabled {
		label = "Disable margins"
	}
	if enableBtnHandle != 0 {
		procSetWindowText.Call(uintptr(enableBtnHandle), uintptr(unsafe.Pointer(utf16(label))))
	}
}

func mainWindowProc(hwnd syscall.Handle, uMsg uint32, wParam, lParam uintptr) uintptr {
	switch uMsg {
	case wmHScroll:
		onScroll(lParam)
		return 0
	case wmCommand:
		switch loword(wParam) {
		case idApplyBtn:
			current.Enabled = true
			applyFromUI()
			updateEnableButtonCaption()
		case idResetBtn:
			resetSettings()
		case idEnableBtn:
			toggleEnabled()
			updateEnableButtonCaption()
		}
		return 0
	case wmClose, wmDestroy:
		if current.Enabled {
			_, _ = restoreDriverMargins()
		}
		procPostQuitMessage.Call(0)
		return 0
	}

	ret, _, _ := procDefWindowProc.Call(uintptr(hwnd), uintptr(uMsg), wParam, lParam)
	return ret
}

func onScroll(lParam uintptr) {
	hwnd := syscall.Handle(lParam)
	switch hwnd {
	case controls.top, controls.bottom, controls.left, controls.right:
		if current.Enabled {
			applyFromUI()
		}
	}
}

func loword(x uintptr) uint16 {
	return uint16(x & 0xffff)
}

func clampMargin(v int) int {
	if v < 0 {
		return 0
	}
	if v > marginMax {
		return marginMax
	}
	return v
}
