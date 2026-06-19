//go:build windows

package main

import (
	"syscall"
	"unsafe"
)

var (
	modKernel32 = syscall.NewLazyDLL("kernel32.dll")
	modUser32   = syscall.NewLazyDLL("user32.dll")
	modGdi32    = syscall.NewLazyDLL("gdi32.dll")
	modComctl32 = syscall.NewLazyDLL("comctl32.dll")

	procGetModuleHandle         = modKernel32.NewProc("GetModuleHandleW")
	procLoadCursor              = modUser32.NewProc("LoadCursorW")
	procRegisterClassEx         = modUser32.NewProc("RegisterClassExW")
	procCreateWindowEx          = modUser32.NewProc("CreateWindowExW")
	procDestroyWindow           = modUser32.NewProc("DestroyWindow")
	procShowWindow              = modUser32.NewProc("ShowWindow")
	procUpdateWindow            = modUser32.NewProc("UpdateWindow")
	procGetMessage              = modUser32.NewProc("GetMessageW")
	procTranslateMessage        = modUser32.NewProc("TranslateMessage")
	procDispatchMessage         = modUser32.NewProc("DispatchMessageW")
	procDefWindowProc           = modUser32.NewProc("DefWindowProcW")
	procPostQuitMessage         = modUser32.NewProc("PostQuitMessage")
	procSendMessage             = modUser32.NewProc("SendMessageW")
	procSetWindowText           = modUser32.NewProc("SetWindowTextW")
	procGetSystemMetrics        = modUser32.NewProc("GetSystemMetrics")
	procUpdateLayeredWindow     = modUser32.NewProc("UpdateLayeredWindow")
	procGetDC                   = modUser32.NewProc("GetDC")
	procReleaseDC               = modUser32.NewProc("ReleaseDC")
	procSetWindowPos            = modUser32.NewProc("SetWindowPos")
	procGetClientRect           = modUser32.NewProc("GetClientRect")
	procMoveWindow              = modUser32.NewProc("MoveWindow")
	procInitCommonControlsEx    = modComctl32.NewProc("InitCommonControlsEx")

	procCreateCompatibleDC = modGdi32.NewProc("CreateCompatibleDC")
	procDeleteDC           = modGdi32.NewProc("DeleteDC")
	procCreateDIBSection   = modGdi32.NewProc("CreateDIBSection")
	procSelectObject       = modGdi32.NewProc("SelectObject")
	procDeleteObject       = modGdi32.NewProc("DeleteObject")
)

const (
	wsOverlappedWindow = 0x00CF0000
	wsVisible          = 0x10000000
	wsChild            = 0x40000000
	wsPopup            = 0x80000000
	wsVisibleChild     = wsChild | wsVisible

	wsExLayered    = 0x00080000
	wsExTopmost    = 0x00000008
	wsExToolWindow = 0x00000080
	wsExNoActivate = 0x08000000

	bsPushButton = 0x00000000

	ssLeft = 0x00000000

	tbsHorz      = 0x0000
	tbsAutoticks = 0x0001

	tbmSetrange = 0x0405
	tbmGetpos   = 0x0400
	tbmSetpos   = 0x0409

	cwUseDefault = ^uintptr(0) - 0x8000

	wmDestroy     = 0x0002
	wmClose       = 0x0010
	wmCommand     = 0x0111
	wmHScroll     = 0x0114
	wmDisplayChange = 0x007E

	swShow   = 5
	swHide   = 0
	idcArrow = 32512

	smXVIRTUALSCREEN = 76
	smYVIRTUALSCREEN = 77
	smCXVIRTUALSCREEN = 78
	smCYVIRTUALSCREEN = 79

	ulwAlpha = 0x00000002

	hwndTopmost   = ^uintptr(0) // HWND_TOPMOST = -1
	swpNoActivate = 0x0010
	swpShowWindow = 0x0040
	swpNomove     = 0x0002
	swpNosize     = 0x0001

	biRGB       = 0
	dibRGBColors = 0
)

type wndClassEx struct {
	Size       uint32
	Style      uint32
	WndProc    uintptr
	ClsExtra   int32
	WndExtra   int32
	Instance   syscall.Handle
	Icon       syscall.Handle
	Cursor     syscall.Handle
	Background syscall.Handle
	MenuName   *uint16
	ClassName  *uint16
	IconSm     syscall.Handle
}

type msg struct {
	HWnd    syscall.Handle
	Message uint32
	WParam  uintptr
	LParam  uintptr
	Time    uint32
	Pt      struct {
		X, Y int32
	}
}

type rect struct {
	Left   int32
	Top    int32
	Right  int32
	Bottom int32
}

type size struct {
	CX int32
	CY int32
}

type point struct {
	X int32
	Y int32
}

type blendFunction struct {
	BlendOp             byte
	BlendFlags          byte
	SourceConstantAlpha byte
	AlphaFormat         byte
}

type bitmapInfoHeader struct {
	Size          uint32
	Width         int32
	Height        int32
	Planes        uint16
	BitCount      uint16
	Compression   uint32
	SizeImage     uint32
	XPelsPerMeter int32
	YPelsPerMeter int32
	ClrUsed       uint32
	ClrImportant  uint32
}

type bitmapInfo struct {
	Header bitmapInfoHeader
}

type initCommonControlsEx struct {
	Size uint32
	ICC  uint32
}

func utf16(s string) *uint16 {
	p, _ := syscall.UTF16PtrFromString(s)
	return p
}

func makelong(low, high uint16) uintptr {
	return uintptr(uint32(low) | (uint32(high) << 16))
}

func getSystemMetrics(index int) int32 {
	ret, _, _ := procGetSystemMetrics.Call(uintptr(index))
	return int32(ret)
}

func virtualScreenRect() rect {
	return rect{
		Left:   getSystemMetrics(smXVIRTUALSCREEN),
		Top:    getSystemMetrics(smYVIRTUALSCREEN),
		Right:  getSystemMetrics(smXVIRTUALSCREEN) + getSystemMetrics(smCXVIRTUALSCREEN),
		Bottom: getSystemMetrics(smYVIRTUALSCREEN) + getSystemMetrics(smCYVIRTUALSCREEN),
	}
}

func initCommonControls() {
	ice := initCommonControlsEx{
		Size: uint32(unsafe.Sizeof(initCommonControlsEx{})),
		ICC:  0x00000004, // ICC_BAR_CLASSES
	}
	procInitCommonControlsEx.Call(uintptr(unsafe.Pointer(&ice)))
}
