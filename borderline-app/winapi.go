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
	procShowWindow           = modUser32.NewProc("ShowWindow")
	procUpdateWindow         = modUser32.NewProc("UpdateWindow")
	procGetMessage           = modUser32.NewProc("GetMessageW")
	procTranslateMessage     = modUser32.NewProc("TranslateMessage")
	procDispatchMessage      = modUser32.NewProc("DispatchMessageW")
	procDefWindowProc        = modUser32.NewProc("DefWindowProcW")
	procPostQuitMessage      = modUser32.NewProc("PostQuitMessage")
	procSendMessage          = modUser32.NewProc("SendMessageW")
	procSetWindowText        = modUser32.NewProc("SetWindowTextW")
	procInitCommonControlsEx = modComctl32.NewProc("InitCommonControlsEx")
)

const (
	wsOverlappedWindow = 0x00CF0000
	wsVisible          = 0x10000000
	wsChild            = 0x40000000
	wsVisibleChild     = 0x40000000 | 0x10000000

	bsPushButton = 0x00000000
	ssLeft       = 0x00000000

	tbsHorz      = 0x0000
	tbsAutoticks = 0x0001

	tbmSetrange = 0x0405
	tbmGetpos   = 0x0400
	tbmSetpos   = 0x0409

	cwUseDefault = ^uintptr(0) - 0x8000

	wmClose         = 0x0010
	wmCommand       = 0x0111
	wmDestroy       = 0x0002
	wmHScroll       = 0x0114
	wmDisplayChange = 0x007E

	swShow   = 5
	idcArrow = 32512
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

func makelong(low, high uint16) uintptr {
	return uintptr(uint32(low) | (uint32(high) << 16))
}

func initCommonControls() {
	ice := initCommonControlsEx{
		Size: uint32(unsafe.Sizeof(initCommonControlsEx{})),
		ICC:  0x00000004,
	}
	procInitCommonControlsEx.Call(uintptr(unsafe.Pointer(&ice)))
}
