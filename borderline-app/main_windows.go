//go:build windows

package main

import (
	"fmt"
	"sync/atomic"
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

	idStartupTimer = 1
)

const (
	marginMax  = 500
	labelWidth = 120
	sliderW    = 220
	rowHeight  = 36

	wmAppStatus = wmApp + 1
)

var (
	appInstance     syscall.Handle
	mainWnd         syscall.Handle
	enableBtnHandle syscall.Handle
	current         Settings

	suspendLiveApply int32
	applyInProgress  int32
	pendingStatus    string

	controls struct {
		top, bottom, left, right syscall.Handle
		status, gpuLabel         syscall.Handle
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
	updateEnableButtonCaption()

	procShowWindow.Call(uintptr(mainWnd), swShow)
	procUpdateWindow.Call(uintptr(mainWnd))
	procSetTimer.Call(uintptr(mainWnd), idStartupTimer, 400, 0)

	checkForUpdatesAsync(func(msg string) {
		pendingStatus = msg
		procPostMessage.Call(uintptr(mainWnd), wmAppStatus, 0, 0)
	})

	runMessageLoop()
}

func runMessageLoop() {
	var message msg
	for {
		ret, _, _ := procGetMessage.Call(uintptr(unsafe.Pointer(&message)), 0, 0, 0)
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
		uintptr(unsafe.Pointer(utf16(fmt.Sprintf("Borderline v%s", appVersion)))),
		wsOverlappedWindow,
		cwUseDefault,
		cwUseDefault,
		440,
		360,
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
	y := 12
	controls.gpuLabel = createStatic(parent, gpuStatusLabel(), 16, y, 400, 18)
	y += 24

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
		"Ready. Driver changes run in the background so the window stays responsive.",
		16, y, 400, 48)
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
	atomic.StoreInt32(&suspendLiveApply, 1)
	setSliderPos(controls.top, current.Top)
	setSliderPos(controls.bottom, current.Bottom)
	setSliderPos(controls.left, current.Left)
	setSliderPos(controls.right, current.Right)
	atomic.StoreInt32(&suspendLiveApply, 0)
}

func setSliderPos(hwnd syscall.Handle, value int) {
	procSendMessage.Call(uintptr(hwnd), tbmSetpos, 0, uintptr(value))
}

func sliderPos(hwnd syscall.Handle) int {
	ret, _, _ := procSendMessage.Call(uintptr(hwnd), tbmGetpos, 0, 0)
	return int(ret)
}

func readSettingsFromUI() Settings {
	return Settings{
		Top:     clampMargin(sliderPos(controls.top)),
		Bottom:  clampMargin(sliderPos(controls.bottom)),
		Left:    clampMargin(sliderPos(controls.left)),
		Right:   clampMargin(sliderPos(controls.right)),
		Enabled: current.Enabled,
	}
}

func applyFromUIAsync() {
	if !atomic.CompareAndSwapInt32(&applyInProgress, 0, 1) {
		return
	}

	current = readSettingsFromUI()
	setControlsEnabled(false)
	setStatus("Applying driver settings…")

	settings := current
	go func() {
		defer atomic.StoreInt32(&applyInProgress, 0)

		var status string
		if settings.Enabled {
			msg, err := applyDriverMargins(settings)
			if err != nil {
				status = "Error: " + err.Error()
			} else {
				status = msg
			}
		} else {
			msg, err := restoreDriverMargins()
			if err != nil {
				status = msg + " (" + err.Error() + ")"
			} else {
				status = msg
			}
		}

		_ = saveSettings(settings)
		pendingStatus = fmt.Sprintf("%s  [%s]", status, enabledLabel())
		procPostMessage.Call(uintptr(mainWnd), wmAppStatus, 1, 0)
	}()
}

func setControlsEnabled(enabled bool) {
	flag := uintptr(0)
	if enabled {
		flag = 1
	}
	for _, hwnd := range []syscall.Handle{
		controls.top, controls.bottom, controls.left, controls.right,
		enableBtnHandle,
	} {
		if hwnd != 0 {
			procEnableWindow.Call(uintptr(hwnd), flag)
		}
	}
}

func resetSettings() {
	if current.Enabled {
		current.Enabled = false
		applyFromUIAsync()
	}
	current = defaultSettings()
	syncControlsFromSettings()
	setStatus("Reset to defaults.")
	_ = saveSettings(current)
	updateEnableButtonCaption()
}

func toggleEnabled() {
	current.Enabled = !current.Enabled
	applyFromUIAsync()
}

func setStatus(text string) {
	if controls.status != 0 {
		procSetWindowText.Call(uintptr(controls.status), uintptr(unsafe.Pointer(utf16(text))))
	}
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
	case wmCreate:
		return 0
	case wmTimer:
		if wParam == idStartupTimer {
			procKillTimer.Call(uintptr(hwnd), idStartupTimer)
			if current.Enabled {
				applyFromUIAsync()
			}
		}
		return 0
	case wmAppStatus:
		setStatus(pendingStatus)
		if wParam != 0 {
			setControlsEnabled(true)
			updateEnableButtonCaption()
		}
		return 0
	case wmHScroll:
		onScroll(lParam)
		return 0
	case wmCommand:
		switch loword(wParam) {
		case idApplyBtn:
			current.Enabled = true
			applyFromUIAsync()
		case idResetBtn:
			resetSettings()
		case idEnableBtn:
			toggleEnabled()
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
	if atomic.LoadInt32(&suspendLiveApply) != 0 {
		return
	}
	hwnd := syscall.Handle(lParam)
	switch hwnd {
	case controls.top, controls.bottom, controls.left, controls.right:
		if current.Enabled {
			applyFromUIAsync()
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
