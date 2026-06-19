//go:build windows

package main

import (
	"crypto/sha256"
	"encoding/hex"
	"encoding/json"
	"fmt"
	"io"
	"net/http"
	"os"
	"os/exec"
	"path/filepath"
	"strings"
	"time"
)

type releaseManifest struct {
	Version string `json:"version"`
	URL     string `json:"url"`
	SHA256  string `json:"sha256"`
}

func manifestURL() string {
	if releaseRepo == "" {
		return ""
	}
	return fmt.Sprintf("https://github.com/%s/releases/latest/download/latest.json", releaseRepo)
}

func checkForUpdatesAsync(onStatus func(string)) {
	if manifestURL() == "" || appVersion == "dev" {
		return
	}

	go func() {
		manifest, err := fetchReleaseManifest(manifestURL())
		if err != nil {
			return
		}
		if !versionNewer(manifest.Version, appVersion) {
			return
		}

		onStatus(fmt.Sprintf("Update %s available — downloading…", manifest.Version))
		if err := installUpdate(manifest); err != nil {
			onStatus("Update failed: " + err.Error())
			return
		}
		onStatus(fmt.Sprintf("Updated to %s — restarting…", manifest.Version))
		time.Sleep(800 * time.Millisecond)
		relaunchSelf()
	}()
}

func fetchReleaseManifest(url string) (*releaseManifest, error) {
	client := &http.Client{Timeout: 20 * time.Second}
	resp, err := client.Get(url)
	if err != nil {
		return nil, err
	}
	defer resp.Body.Close()
	if resp.StatusCode != http.StatusOK {
		return nil, fmt.Errorf("manifest HTTP %s", resp.Status)
	}
	var manifest releaseManifest
	if err := json.NewDecoder(resp.Body).Decode(&manifest); err != nil {
		return nil, err
	}
	if manifest.Version == "" || manifest.URL == "" {
		return nil, fmt.Errorf("invalid manifest")
	}
	return &manifest, nil
}

func versionNewer(latest, current string) bool {
	return parseVersion(latest) > parseVersion(current)
}

func parseVersion(v string) int {
	v = strings.TrimPrefix(strings.TrimSpace(v), "v")
	parts := strings.Split(v, ".")
	n := 0
	for i, p := range parts {
		var x int
		fmt.Sscanf(p, "%d", &x)
		switch i {
		case 0:
			n += x * 10000
		case 1:
			n += x * 100
		case 2:
			n += x
		}
	}
	return n
}

func installUpdate(m *releaseManifest) error {
	dir, err := updateDir()
	if err != nil {
		return err
	}
	target := filepath.Join(dir, "borderline-"+m.Version+".exe")
	if !needsDownload(target, m.SHA256) {
		return stageUpdateAndRestart(target)
	}
	return downloadUpdate(m.URL, target, m.SHA256)
}

func updateDir() (string, error) {
	base, err := os.UserConfigDir()
	if err != nil {
		return "", err
	}
	dir := filepath.Join(base, "Borderline", "updates")
	return dir, os.MkdirAll(dir, 0o755)
}

func needsDownload(path, expectedSHA256 string) bool {
	info, err := os.Stat(path)
	if err != nil || info.IsDir() {
		return true
	}
	if expectedSHA256 == "" {
		return false
	}
	actual, err := fileSHA256(path)
	return err != nil || !strings.EqualFold(actual, expectedSHA256)
}

func downloadUpdate(url, dest, expectedSHA256 string) error {
	client := &http.Client{Timeout: 3 * time.Minute}
	resp, err := client.Get(url)
	if err != nil {
		return err
	}
	defer resp.Body.Close()
	if resp.StatusCode != http.StatusOK {
		return fmt.Errorf("download HTTP %s", resp.Status)
	}

	tmp := dest + ".partial"
	out, err := os.Create(tmp)
	if err != nil {
		return err
	}
	hasher := sha256.New()
	if _, err := io.Copy(io.MultiWriter(out, hasher), resp.Body); err != nil {
		out.Close()
		os.Remove(tmp)
		return err
	}
	if err := out.Close(); err != nil {
		os.Remove(tmp)
		return err
	}
	actual := hex.EncodeToString(hasher.Sum(nil))
	if expectedSHA256 != "" && !strings.EqualFold(actual, expectedSHA256) {
		os.Remove(tmp)
		return fmt.Errorf("checksum mismatch")
	}
	if err := os.Rename(tmp, dest); err != nil {
		return err
	}
	return stageUpdateAndRestart(dest)
}

func stageUpdateAndRestart(newExe string) error {
	self, err := os.Executable()
	if err != nil {
		return err
	}
	self, err = filepath.Abs(self)
	if err != nil {
		return err
	}
	newExe, err = filepath.Abs(newExe)
	if err != nil {
		return err
	}
	if strings.EqualFold(self, newExe) {
		return nil
	}

	scriptPath := filepath.Join(filepath.Dir(self), "borderline-update.cmd")
	script := fmt.Sprintf(`@echo off
timeout /t 2 /nobreak >nul
copy /Y "%s" "%s" >nul
start "" "%s"
del "%%~f0"
`, newExe, self, self)
	if err := os.WriteFile(scriptPath, []byte(script), 0o644); err != nil {
		return err
	}
	updateState.pendingScript = scriptPath
	updateState.pendingExe = newExe
	return nil
}

func relaunchSelf() {
	if updateState.pendingScript == "" {
		return
	}
	cmd := exec.Command("cmd.exe", "/C", updateState.pendingScript)
	_ = cmd.Start()
	os.Exit(0)
}

func fileSHA256(path string) (string, error) {
	f, err := os.Open(path)
	if err != nil {
		return "", err
	}
	defer f.Close()
	h := sha256.New()
	if _, err := io.Copy(h, f); err != nil {
		return "", err
	}
	return hex.EncodeToString(h.Sum(nil)), nil
}

var updateState struct {
	pendingScript string
	pendingExe    string
}
