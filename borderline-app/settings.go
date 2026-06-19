//go:build windows

package main

import (
	"encoding/json"
	"os"
	"path/filepath"
)

type Settings struct {
	Top     int  `json:"top"`
	Bottom  int  `json:"bottom"`
	Left    int  `json:"left"`
	Right   int  `json:"right"`
	Enabled bool `json:"enabled"`
}

func defaultSettings() Settings {
	return Settings{
		Top:     0,
		Bottom:  0,
		Left:    0,
		Right:   0,
		Enabled: false,
	}
}

func settingsPath() (string, error) {
	dir, err := os.UserConfigDir()
	if err != nil {
		return "", err
	}
	return filepath.Join(dir, "Borderline", "settings.json"), nil
}

func loadSettings() Settings {
	path, err := settingsPath()
	if err != nil {
		return defaultSettings()
	}

	data, err := os.ReadFile(path)
	if err != nil {
		return defaultSettings()
	}

	var s Settings
	if err := json.Unmarshal(data, &s); err != nil {
		return defaultSettings()
	}
	return s
}

func saveSettings(s Settings) error {
	path, err := settingsPath()
	if err != nil {
		return err
	}

	if err := os.MkdirAll(filepath.Dir(path), 0o755); err != nil {
		return err
	}

	data, err := json.MarshalIndent(s, "", "  ")
	if err != nil {
		return err
	}

	return os.WriteFile(path, data, 0o644)
}
