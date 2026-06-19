//go:build windows

package main

import (
	"syscall"
	"unsafe"
)

var (
	modKernel32 = syscall.NewLazyDLL("kernel32.dll")
	modUser32   = syscall.NewLazyDLL("user32.dll")
	modComctl32 = syscall.NewLazyDLL("comctl32.dll")

	procGetModuleHandle      = modKernel32.NewProc("GetModuleHandleW")
	procLoadCursor           = modUser32.NewProc("LoadCursorW")
	procRegisterClassEx      = modUser32.NewProc("RegisterClassExW")
	procCreateWindowEx       = modUser32.NewProc("CreateWindowExW")
	procDestroyWindow        = modUser32.NewProc("DestroyWindow")
	procShowWindow           = modUser32.NewProc("ShowWindow")
	procUpdateWindow         = modUser32.NewProc("UpdateWindow")
	procGetMessage           = modUser32.NewProc("GetMessageW")
	procTranslateMessage     = modUser32.NewProc("TranslateMessage")
	procDispatchMessage      = modUser32.NewProc("DispatchMessageW")
	procDefWindowProc        = modUser32.NewProc("DefWindowProcW")
	procPostQuitMessage      = modUser32.NewProc("PostQuitMessage")
	procPostMessage          = modUser32.NewProc("PostMessageW")
	procSendMessage          = modUser32.NewProc("SendMessageW")
	procSetWindowText        = modUser32.NewProc("SetWindowTextW")
	procGetWindowText        = modUser32.NewProc("GetWindowTextW")
	procEnableWindow         = modUser32.NewProc("EnableWindow")
	procSetTimer             = modUser32.NewProc("SetTimer")
	procKillTimer            = modUser32.NewProc("KillTimer")
	procMessageBox           = modUser32.NewProc("MessageBoxW")
	procInitCommonControlsEx = modComctl32.NewProc("InitCommonControlsEx")
)

const (
	wsOverlappedWindow = 0x00CF0000
	wsChild            = 0x40000000
	wsVisibleChild     = 0x40000000 | 0x10000000
	wsExClientEdge     = 0x00000200

	bsPushButton = 0x00000000
	ssLeft       = 0x00000000
	esNumber     = 0x2000

	cwUseDefault = ^uintptr(0) - 0x8000

	wmCreate  = 0x0001
	wmClose   = 0x0010
	wmCommand = 0x0111
	wmDestroy = 0x0002
	wmTimer   = 0x0113
	wmApp     = 0x8000

	swShow = 5
	idcArrow = 32512

	mbOK        = 0x00000000
	mbIconError = 0x00000010
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

type initCommonControlsEx struct {
	Size uint32
	ICC  uint32
}

func utf16(s string) *uint16 {
	p, _ := syscall.UTF16PtrFromString(s)
	return p
}

func initCommonControls() {
	ice := initCommonControlsEx{
		Size: uint32(unsafe.Sizeof(initCommonControlsEx{})),
		ICC:  0x00004004, // ICC_STANDARD_CLASSES | ICC_BAR_CLASSES
	}
	procInitCommonControlsEx.Call(uintptr(unsafe.Pointer(&ice)))
}
