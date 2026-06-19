//go:build windows

package main

import (
	"syscall"
	"unsafe"
)

type overlayState struct {
	hwnd syscall.Handle
}

var overlay overlayState

func destroyOverlay() {
	if overlay.hwnd != 0 {
		procDestroyWindow.Call(uintptr(overlay.hwnd))
		overlay.hwnd = 0
	}
}

func applyOverlay(s Settings) {
	if !s.Enabled || (s.Top == 0 && s.Bottom == 0 && s.Left == 0 && s.Right == 0 && s.CornerRadius == 0) {
		destroyOverlay()
		return
	}

	screen := virtualScreenRect()
	width := int(screen.Right - screen.Left)
	height := int(screen.Bottom - screen.Top)
	if width <= 0 || height <= 0 {
		return
	}

	pixels := buildOverlayPixels(width, height, s)

	if overlay.hwnd == 0 {
		instance, _, _ := procGetModuleHandle.Call(0)
		hwnd, _, _ := procCreateWindowEx.Call(
			wsExLayered|wsExTopmost|wsExToolWindow|wsExNoActivate,
			uintptr(unsafe.Pointer(utf16("BorderlineOverlayClass"))),
			0,
			wsPopup,
			uintptr(screen.Left),
			uintptr(screen.Top),
			uintptr(width),
			uintptr(height),
			0,
			0,
			instance,
			0,
		)
		if hwnd == 0 {
			return
		}
		overlay.hwnd = syscall.Handle(hwnd)
	} else {
		procSetWindowPos.Call(
			uintptr(overlay.hwnd),
			hwndTopmost,
			uintptr(screen.Left),
			uintptr(screen.Top),
			uintptr(width),
			uintptr(height),
			swpNoActivate|swpShowWindow,
		)
	}

	updateLayeredBitmap(overlay.hwnd, width, height, pixels)
	procShowWindow.Call(uintptr(overlay.hwnd), swShow)
}

func buildOverlayPixels(width, height int, s Settings) []byte {
	pixels := make([]byte, width*height*4)

	innerLeft := s.Left
	innerTop := s.Top
	innerRight := width - s.Right
	innerBottom := height - s.Bottom
	radius := s.CornerRadius

	for y := 0; y < height; y++ {
		for x := 0; x < width; x++ {
			if isBorderPixel(x, y, innerLeft, innerTop, innerRight, innerBottom, radius) {
				idx := (y*width + x) * 4
				pixels[idx+0] = 0   // B
				pixels[idx+1] = 0   // G
				pixels[idx+2] = 0   // R
				pixels[idx+3] = 255 // A
			}
		}
	}

	return pixels
}

func isBorderPixel(x, y, innerLeft, innerTop, innerRight, innerBottom, radius int) bool {
	if x < innerLeft || x >= innerRight || y < innerTop || y >= innerBottom {
		return true
	}

	if radius <= 0 {
		return false
	}

	type corner struct {
		cx, cy int
	}
	corners := []corner{
		{innerLeft + radius, innerTop + radius},
		{innerRight - radius, innerTop + radius},
		{innerLeft + radius, innerBottom - radius},
		{innerRight - radius, innerBottom - radius},
	}

	for i, c := range corners {
		inCornerZone := false
		switch i {
		case 0:
			inCornerZone = x < innerLeft+radius && y < innerTop+radius
		case 1:
			inCornerZone = x >= innerRight-radius && y < innerTop+radius
		case 2:
			inCornerZone = x < innerLeft+radius && y >= innerBottom-radius
		case 3:
			inCornerZone = x >= innerRight-radius && y >= innerBottom-radius
		}
		if !inCornerZone {
			continue
		}

		dx := float64(x - c.cx)
		dy := float64(y - c.cy)
		if dx*dx+dy*dy > float64(radius*radius) {
			return true
		}
	}

	return false
}

func updateLayeredBitmap(hwnd syscall.Handle, width, height int, pixels []byte) {
	screenDC, _, _ := procGetDC.Call(0)
	memDC, _, _ := procCreateCompatibleDC.Call(screenDC)
	defer procReleaseDC.Call(0, screenDC)

	var bmi bitmapInfo
	bmi.Header = bitmapInfoHeader{
		Size:        uint32(unsafe.Sizeof(bitmapInfoHeader{})),
		Width:       int32(width),
		Height:      -int32(height),
		Planes:      1,
		BitCount:    32,
		Compression: biRGB,
	}

	var bits unsafe.Pointer
	hbmp, _, _ := procCreateDIBSection.Call(
		memDC,
		uintptr(unsafe.Pointer(&bmi)),
		dibRGBColors,
		uintptr(unsafe.Pointer(&bits)),
		0,
		0,
	)
	if hbmp == 0 {
		procDeleteDC.Call(memDC)
		return
	}

	copy((*[1 << 30]byte)(bits)[:len(pixels)], pixels)

	old, _, _ := procSelectObject.Call(memDC, hbmp)
	defer func() {
		procSelectObject.Call(memDC, old)
		procDeleteObject.Call(hbmp)
		procDeleteDC.Call(memDC)
	}()

	blend := blendFunction{AlphaFormat: 0x01} // AC_SRC_ALPHA
	size := size{CX: int32(width), CY: int32(height)}
	ptSrc := point{}
	ptDst := point{}

	procUpdateLayeredWindow.Call(
		uintptr(hwnd),
		0,
		uintptr(unsafe.Pointer(&ptDst)),
		uintptr(unsafe.Pointer(&size)),
		memDC,
		uintptr(unsafe.Pointer(&ptSrc)),
		0,
		uintptr(unsafe.Pointer(&blend)),
		ulwAlpha,
	)
}

func registerOverlayClass(instance syscall.Handle) {
	className := utf16("BorderlineOverlayClass")
	wcx := wndClassEx{
		Size:      uint32(unsafe.Sizeof(wndClassEx{})),
		Instance:  instance,
		ClassName: className,
	}
	procRegisterClassEx.Call(uintptr(unsafe.Pointer(&wcx)))
}

func clampMargin(v int) int {
	if v < 0 {
		return 0
	}
	if v > 500 {
		return 500
	}
	return v
}

func clampRadius(v int) int {
	if v < 0 {
		return 0
	}
	maxR := 200
	if v > maxR {
		return maxR
	}
	return v
}
