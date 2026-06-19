# Borderline

Borderline adjusts **display underscan/overscan at the driver level** on Windows. Instead of drawing black overlay bars, it registers a **custom resolution** with the graphics driver so the desktop uses fewer pixels and the GPU leaves the trimmed panel area unused (blank — nothing is drawn there by Borderline or the OS desktop).

macOS support is planned; v1 targets Windows only.

## Download on Windows

**https://github.com/ashdkts/borderline/releases/latest/download/borderline.exe**

Double-click to run — no installer.

### Using Borderline

1. Launch **Borderline**.
2. Set **Top / Bottom / Left / Right** margins in pixels.
3. Click **Apply** or **Enable margins**.
4. The driver switches to a smaller mode (e.g. 1880×1040 instead of 1920×1080). Unused edges are blank on the physical panel.
5. Click **Disable margins** or **Reset** to restore your previous mode.

Settings persist in `%APPDATA%\Borderline\settings.json`.

## How it works (driver level)

Borderline calls the Windows **`ChangeDisplaySettingsEx`** API to apply a custom display mode:

- Reads your monitor’s current resolution and refresh rate.
- Subtracts your margin values to compute a new width/height.
- Enables custom resolutions on NVIDIA GPUs (`CDS_ENABLE_UNSAFE_MODES`).
- Saves the mode in the display driver registry so it survives reboots.

This is the same class of change as **NVIDIA Control Panel → Adjust desktop size** (which creates resolutions like 1842×1030), but Borderline exposes **per-edge pixel control** in one simple window.

### GPU scaling note

If margins look stretched instead of letterboxed, set your GPU scaling to **center / no scaling / 1:1** in NVIDIA Control Panel, Intel Graphics Command Center, or AMD Software. Borderline sets the mode; the GPU decides how unused panel area is handled (typically blank).

### Limitations (v1)

| Feature | Status |
|---------|--------|
| Per-edge pixel margins | Supported via custom resolution |
| Blank unused panel area | Yes — driver/GPU, not an overlay |
| Rounded corners | Not at driver level (rectangular crop only) |
| AMD ADL / Intel IGCL direct APIs | Planned — v1 uses Windows display API |
| Multi-monitor per-display pick | Primary desktop display only |

## Publish a release

```bash
chmod +x scripts/*.sh
./scripts/build.sh
./scripts/publish-github.sh
```

Or push a tag to trigger CI:

```bash
git tag v1.0.1
git push origin v1.0.1
```

## Local build

```bash
./scripts/build.sh
# Output: dist/borderline.exe
```

## Project layout

| Path | Description |
|------|-------------|
| `borderline-app/` | Go Win32 GUI + `ChangeDisplaySettingsEx` driver logic |
| `scripts/build.sh` | Cross-compile Windows exe |
| `.github/workflows/release.yml` | GitHub Actions release |

## License

MIT
