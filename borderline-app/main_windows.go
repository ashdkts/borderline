//go:build windows

package main

import (
	"fmt"
	"strconv"
	"strings"
	"sync/atomic"
	"syscall"
	"unsafe"
)

const (
	idTopEdit    = 1001
	idBottomEdit = 1002
	idLeftEdit   = 1003
	idRightEdit  = 1004
	idApplyBtn   = 1010
	idResetBtn   = 1011
	idEnableBtn  = 1012

	idGPUTimer    = 1
	idUpdateTimer = 2

	wmAppStatus = wmApp + 1
)

const (
	marginMax  = 500
	labelWidth = 120
	editW      = 80
	rowHeight  = 32
)

var (
	appInstance     syscall.Handle
	mainWnd         syscall.Handle
	enableBtnHandle syscall.Handle
	mainWndProcPtr  uintptr
	current         Settings

	applyInProgress int32
	pendingStatus   string
	controlsReady   bool

	controls struct {
		top, bottom, left, right syscall.Handle
		status, gpuLabel         syscall.Handle
	}
)

func main() {
	initCommonControls()

	instance, _, _ := procGetModuleHandle.Call(0)
	appInstance = syscall.Handle(instance)

	mainWndProcPtr = syscall.NewCallback(mainWindowProc)
	mustRegisterMainClass(appInstance)

	current = loadSettings()

	mainWnd = createMainWindow(appInstance)
	if mainWnd == 0 {
		showError("Borderline could not create its window.")
		return
	}

	procShowWindow.Call(uintptr(mainWnd), swShow)
	procUpdateWindow.Call(uintptr(mainWnd))

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
}

func mustRegisterMainClass(instance syscall.Handle) {
	className := utf16("BorderlineMainWindow")
	wcx := wndClassEx{
		Size:      uint32(unsafe.Sizeof(wndClassEx{})),
		WndProc:   mainWndProcPtr,
		Instance:  instance,
		ClassName: className,
	}
	cursor, _, _ := procLoadCursor.Call(0, idcArrow)
	wcx.Cursor = syscall.Handle(cursor)
	if ret, _, _ := procRegisterClassEx.Call(uintptr(unsafe.Pointer(&wcx))); ret == 0 {
		showError("RegisterClassEx failed.")
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
		420,
		340,
		0,
		0,
		uintptr(instance),
		0,
	)
	return syscall.Handle(hwnd)
}

func layoutControls(parent syscall.Handle) {
	y := 12
	controls.gpuLabel = createStatic(parent, "GPU: detecting…", 16, y, 380, 18)
	y += 24

	controls.top = createEditRow(parent, "Top (px)", idTopEdit, 16, y)
	y += rowHeight
	controls.bottom = createEditRow(parent, "Bottom (px)", idBottomEdit, 16, y)
	y += rowHeight
	controls.left = createEditRow(parent, "Left (px)", idLeftEdit, 16, y)
	y += rowHeight
	controls.right = createEditRow(parent, "Right (px)", idRightEdit, 16, y)
	y += rowHeight + 8

	createButton(parent, "Apply", idApplyBtn, 16, y, 90, 28)
	createButton(parent, "Reset", idResetBtn, 116, y, 90, 28)
	createButton(parent, "Enable margins", idEnableBtn, 216, y, 120, 28)
	y += 40

	controls.status = createStatic(parent, "Ready.", 16, y, 380, 48)
	controlsReady = true
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

func createEditRow(parent syscall.Handle, label string, id, x, y int) syscall.Handle {
	createStatic(parent, label, x, y+4, labelWidth, 20)
	hwnd, _, _ := procCreateWindowEx.Call(
		wsExClientEdge,
		uintptr(unsafe.Pointer(utf16("EDIT"))),
		uintptr(unsafe.Pointer(utf16("0"))),
		wsVisibleChild|esNumber,
		uintptr(x+labelWidth), uintptr(y), uintptr(editW), uintptr(24),
		uintptr(parent), uintptr(id), uintptr(appInstance), 0,
	)
	return syscall.Handle(hwnd)
}

func syncControlsFromSettings() {
	setEditValue(controls.top, current.Top)
	setEditValue(controls.bottom, current.Bottom)
	setEditValue(controls.left, current.Left)
	setEditValue(controls.right, current.Right)
}

func setEditValue(hwnd syscall.Handle, value int) {
	if hwnd == 0 {
		return
	}
	procSetWindowText.Call(uintptr(hwnd), uintptr(unsafe.Pointer(utf16(strconv.Itoa(value)))))
}

func editValue(hwnd syscall.Handle) int {
	if hwnd == 0 {
		return 0
	}
	buf := make([]uint16, 32)
	procGetWindowText.Call(uintptr(hwnd), uintptr(unsafe.Pointer(&buf[0])), 32)
	text := syscall.UTF16ToString(buf)
	v, err := strconv.Atoi(strings.TrimSpace(text))
	if err != nil {
		return 0
	}
	return clampMargin(v)
}

func readSettingsFromUI() Settings {
	return Settings{
		Top:     editValue(controls.top),
		Bottom:  editValue(controls.bottom),
		Left:    editValue(controls.left),
		Right:   editValue(controls.right),
		Enabled: current.Enabled,
	}
}

func applyFromUIAsync() {
	if !controlsReady {
		return
	}
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
		pendingStatus = fmt.Sprintf("%s  [%s]", status, enabledLabelFor(settings))
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

func enabledLabelFor(s Settings) string {
	if s.Enabled {
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

func detectGPUAsync() {
	go func() {
		label := gpuStatusLabel()
		pendingStatus = label
		procPostMessage.Call(uintptr(mainWnd), wmAppStatus, 0, 0)
	}()
}

func mainWindowProc(hwnd syscall.Handle, uMsg uint32, wParam, lParam uintptr) uintptr {
	switch uMsg {
	case wmCreate:
		layoutControls(hwnd)
		syncControlsFromSettings()
		updateEnableButtonCaption()
		if current.Enabled {
			setStatus("Saved margins loaded. Click Apply to re-enable.")
		} else {
			setStatus("Ready. Enter pixel margins, then click Apply.")
		}
		procSetTimer.Call(uintptr(hwnd), idGPUTimer, 200, 0)
		procSetTimer.Call(uintptr(hwnd), idUpdateTimer, 8000, 0)
		return 0
	case wmTimer:
		switch wParam {
		case idGPUTimer:
			procKillTimer.Call(uintptr(hwnd), idGPUTimer)
			detectGPUAsync()
		case idUpdateTimer:
			procKillTimer.Call(uintptr(hwnd), idUpdateTimer)
			checkForUpdatesAsync(func(msg string) {
				pendingStatus = msg
				procPostMessage.Call(uintptr(mainWnd), wmAppStatus, 0, 0)
			})
		}
		return 0
	case wmAppStatus:
		if controls.gpuLabel != 0 && strings.HasPrefix(pendingStatus, "GPU:") {
			procSetWindowText.Call(uintptr(controls.gpuLabel), uintptr(unsafe.Pointer(utf16(pendingStatus))))
		} else {
			setStatus(pendingStatus)
		}
		if wParam != 0 {
			setControlsEnabled(true)
			updateEnableButtonCaption()
		}
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
	case wmClose:
		if current.Enabled {
			go func() { _, _ = restoreDriverMargins() }()
		}
		procDestroyWindow.Call(uintptr(hwnd))
		return 0
	case wmDestroy:
		procPostQuitMessage.Call(0)
		return 0
	}

	ret, _, _ := procDefWindowProc.Call(uintptr(hwnd), uintptr(uMsg), wParam, lParam)
	return ret
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

func showError(msg string) {
	procMessageBox.Call(
		0,
		uintptr(unsafe.Pointer(utf16(msg))),
		uintptr(unsafe.Pointer(utf16("Borderline"))),
		mbOK|mbIconError,
	)
}
