//go:build windows

package main

func applyDriverMargins(s Settings) (string, error) {
	if !s.Enabled || (s.Top == 0 && s.Bottom == 0 && s.Left == 0 && s.Right == 0) {
		return restoreDriverMargins()
	}

	vendor := detectGPUVendor()
	if vendor == "amd" {
		if msg, err := applyAMDMargins(s); err == nil {
			return msg, nil
		}
	}

	return applyWin32Margins(s)
}

func restoreDriverMargins() (string, error) {
	if amdRestore() {
		return "AMD display restored.", nil
	}
	return restoreWin32Margins()
}

func gpuStatusLabel() string {
	v := detectGPUVendor()
	switch v {
	case "amd":
		return "GPU: AMD (ADL driver)"
	case "nvidia":
		return "GPU: NVIDIA (custom mode)"
	case "intel":
		return "GPU: Intel (custom mode)"
	default:
		return "GPU: generic (custom mode)"
	}
}
