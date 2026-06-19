# Borderline

Borderline adjusts **display underscan/overscan at the driver level** on Windows. Unused panel area stays blank — Borderline does not draw overlay bars.

**Download:** https://github.com/ashdkts/borderline/releases/latest/download/borderline.exe

## v1.0.2 changes

- **Fixes frozen UI** — driver changes run on a background thread; the window stays responsive
- **AMD support** — uses AMD ADL (`atiadlxx.dll`) for native driver underscan on Radeon GPUs
- **Auto-update** — checks GitHub Releases on launch and installs newer versions automatically

## Using Borderline

1. Launch **Borderline** (shows detected GPU at the top).
2. Set **Top / Bottom / Left / Right** margins in pixels.
3. Click **Apply** or **Enable margins**.
4. Click **Disable margins** or **Reset** to restore your previous mode.

Settings persist in `%APPDATA%\Borderline\settings.json`.

## GPU support

| GPU | Method |
|-----|--------|
| **AMD** | ADL driver underscan (native). Per-edge sliders use the **largest** margin as uniform underscan — AMD’s driver API does not expose independent edge values. |
| **NVIDIA** | Custom resolution via Windows display API (+ unsafe modes for custom timings) |
| **Intel / other** | Custom resolution via Windows display API |

If edges look stretched, set GPU scaling to **center / 1:1** in your graphics control panel.

## Auto-update

On launch, Borderline checks:

`https://github.com/ashdkts/borderline/releases/latest/download/latest.json`

If a newer version exists, it downloads the exe (SHA-256 verified), replaces itself, and restarts. Status appears in the window footer.

## Publish a release

```bash
./scripts/build.sh 1.0.2
git tag v1.0.2
git push origin main --tags
```

## License

MIT
