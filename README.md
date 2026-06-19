# Borderline

Borderline is a Windows tool for customizing display **underscan** and **overscan** with pixel-by-pixel control and optional rounded corners. It draws a topmost transparent overlay: black borders on the edges you choose, with a fully transparent (and clickable) center so your desktop stays usable.

macOS support is planned; v1 targets Windows only.

## Download on Windows

After the first GitHub Release is published:

**https://github.com/YOUR_USERNAME/borderline/releases/latest/download/borderline.exe**

Double-click `borderline.exe` — no installer required.

### Using Borderline

1. Launch **Borderline**.
2. Drag the **Top / Bottom / Left / Right** sliders to set margin size in pixels.
3. Adjust **Corner radius** for rounded inner corners (0 = square).
4. Click **Apply** (or **Enable borders**) to show the overlay.
5. Click **Disable borders** to hide it without losing your slider values.
6. Settings are saved to `%APPDATA%\\Borderline\\settings.json` and restored on next launch.

The overlay covers all monitors (virtual desktop). Fully transparent pixels pass clicks through to windows underneath.

## Publish to GitHub Releases

### 1. Log in to GitHub CLI (once)

If you use the bundled CLI from the littlescreens repo:

```bash
../littlescreens/.tools/gh_2.95.0_macOS_arm64/bin/gh auth login
```

### 2. Build and publish

```bash
chmod +x scripts/*.sh
./scripts/build.sh
./scripts/publish-github.sh
```

This creates the `borderline` repository on your GitHub account (if needed), pushes `main`, tags `v1.0.0`, and triggers the release workflow.

### 3. Download on your Windows PC

Open the release URL from the publish script output, or:

```powershell
# PowerShell — download latest release
Invoke-WebRequest -Uri "https://github.com/YOUR_USERNAME/borderline/releases/latest/download/borderline.exe" -OutFile borderline.exe
```

## Local development (Mac/Linux)

Cross-compile the Windows executable:

```bash
./scripts/build.sh
```

Serve the build locally (optional, for LAN testing):

```bash
./scripts/serve-releases.sh
# On Windows: download http://YOUR_MAC_IP:8080/borderline.exe
```

## How it works

Borderline does **not** modify GPU drivers or EDID data. Instead it places a layered, click-through-safe overlay over the virtual screen:

- **Underscan-style margins**: increase sliders to add black bars on any edge.
- **Rounded corners**: masks the inner corner arcs so the visible area has smooth corners.
- **Multi-monitor**: uses the Windows virtual screen bounds so all displays are covered.

This approach works on any GPU without admin rights. For hardware-level timing changes (true EDID overscan), use your graphics control panel or tools like CRU; Borderline focuses on a simple, reversible visual margin tool.

## Project layout

| Path | Description |
|------|-------------|
| `borderline-app/` | Go + Win32 GUI and overlay |
| `scripts/build.sh` | Cross-compile `borderline.exe` |
| `scripts/publish-github.sh` | Create repo, push, tag, release |
| `.github/workflows/release.yml` | CI build and GitHub Release |

## License

MIT
