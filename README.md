# Borderline

Borderline adjusts **display underscan/overscan at the driver level** on Windows. Unused panel area stays blank — no overlay bars.

**Download:** https://github.com/ashdkts/borderline/releases/latest/download/borderline.exe

## v1.1.1 — rewritten in C# / WinForms

Earlier Go builds could hang or crash on startup due to low-level Win32 issues. **v1.1.0 is a full rewrite** using .NET WinForms — a standard Windows UI stack that should open reliably.

### Features

- Pixel margin fields (Top / Bottom / Left / Right)
- **AMD** — native ADL driver underscan
- **NVIDIA / Intel / other** — custom resolution via Windows display API
- **Auto-update** — checks GitHub Releases ~5 seconds after launch
- Settings saved to `%APPDATA%\Borderline\settings.json`

### Usage

1. Download and run `borderline.exe` (self-contained, no .NET install required).
2. Enter margins in pixels.
3. Click **Apply** or **Enable margins**.
4. Click **Disable margins** or **Reset** to restore.

### AMD note

AMD’s driver API applies **uniform** underscan. If edges differ, Borderline uses the **largest** margin value.

### Build (requires .NET 8 SDK)

```bash
./scripts/build.sh 1.1.0
```

### Publish release

```bash
git tag v1.1.0
git push origin v1.1.0
```

## License

MIT
