using Microsoft.Win32;

namespace Borderline;

/// <summary>
/// AMD DAL2 registry underscan — works on many iGPUs when ADL APIs accept but don't apply.
/// </summary>
internal static class AmdDalRegistry
{
    private const string VideoRoot = @"SYSTEM\CurrentControlSet\Control\Video";
    private static int? _backupUnderscan;
    private static string? _backupKeyPath;

    public static string? TryApply(int top, int bottom, int left, int right, out string? message)
    {
        message = null;
        try
        {
            if (!NativeDisplay.TryGetResolution(out var w, out var h))
            {
                return "could not read resolution";
            }

            NativeDisplay.TryGetRefreshRate(out var hz);

            var margin = Math.Max(Math.Max(top, bottom), Math.Max(left, right));
            var dim = Math.Min(w, h);
            if (margin <= 0 || dim <= 0)
            {
                return "no margins requested";
            }

            // ADL underscan percent: ~100 * border_pixels * 2 / smaller_dimension
            var targetPercent = Math.Clamp(margin * 100 * 2 / dim, 1, 30);

            if (!TryFindUnderscanKey(w, h, hz, out var keyPath, out var current))
            {
                return "no DAL underscan registry key for current mode";
            }

            if (_backupUnderscan is null)
            {
                _backupUnderscan = current;
                _backupKeyPath = keyPath;
            }

            using var key = Registry.LocalMachine.OpenSubKey(keyPath, writable: true);
            if (key is null)
            {
                return "registry key not writable (run as admin?)";
            }

            key.SetValue("Underscan", targetPercent, RegistryValueKind.DWord);
            AmdAdlCore.FlushAdapterDriver();

            var readBack = (int)(key.GetValue("Underscan") ?? -1);
            if (readBack != targetPercent)
            {
                key.SetValue("Underscan", current, RegistryValueKind.DWord);
                return $"registry write failed (wanted {targetPercent}, got {readBack})";
            }

            message =
                $"DAL registry underscan {targetPercent}% at {keyPath} (was {current}%, {w}x{h}@{hz}Hz).";
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return "registry access denied — try running as administrator";
        }
        catch (Exception ex)
        {
            return ex.Message;
        }
    }

    public static bool Restore()
    {
        if (_backupUnderscan is null || _backupKeyPath is null)
        {
            return false;
        }

        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(_backupKeyPath, writable: true);
            key?.SetValue("Underscan", _backupUnderscan.Value, RegistryValueKind.DWord);
            AmdAdlCore.FlushAdapterDriver();
            _backupUnderscan = null;
            _backupKeyPath = null;
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static bool TryFindUnderscanKey(int w, int h, int hz, out string keyPath, out int current)
    {
        keyPath = string.Empty;
        current = 0;

        using var videoRoot = Registry.LocalMachine.OpenSubKey(VideoRoot);
        if (videoRoot is null)
        {
            return false;
        }

        foreach (var guidName in videoRoot.GetSubKeyNames())
        {
            if (!guidName.StartsWith('{'))
            {
                continue;
            }

            using var guidKey = videoRoot.OpenSubKey(guidName);
            if (guidKey is null)
            {
                continue;
            }

            foreach (var instance in new[] { "0000", "0001", "0002" })
            {
                using var instKey = guidKey.OpenSubKey(instance);
                if (instKey is null)
                {
                    continue;
                }

                using var dal = instKey.OpenSubKey("DAL2_DATA_2.0") ?? instKey.OpenSubKey("DAL2_DATA__2_0");
                if (dal is null)
                {
                    continue;
                }

                var dalName = instKey.OpenSubKey("DAL2_DATA_2.0") is not null
                    ? "DAL2_DATA_2.0"
                    : "DAL2_DATA__2_0";

                foreach (var pathName in dal.GetSubKeyNames())
                {
                    if (!pathName.StartsWith("DisplayPath_", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    using var pathKey = dal.OpenSubKey(pathName);
                    if (pathKey is null)
                    {
                        continue;
                    }

                    foreach (var modeName in pathKey.GetSubKeyNames())
                    {
                        if (!modeName.StartsWith("MODE_", StringComparison.OrdinalIgnoreCase))
                        {
                            continue;
                        }

                        if (!ModeMatches(modeName, w, h, hz))
                        {
                            continue;
                        }

                        using var modeKey = pathKey.OpenSubKey(modeName);
                        using var adjustment = modeKey?.OpenSubKey("Adjustment");
                        if (adjustment?.GetValue("Underscan") is int value)
                        {
                            keyPath =
                                $@"{VideoRoot}\{guidName}\{instance}\{dalName}\{pathName}\{modeName}\Adjustment";
                            current = value;
                            return true;
                        }
                    }
                }
            }
        }

        return false;
    }

    private static bool ModeMatches(string modeName, int w, int h, int hz)
    {
        // MODE_1920x1080x60 or MODE1920x1080x0x60 variants
        var body = modeName.StartsWith("MODE_", StringComparison.OrdinalIgnoreCase)
            ? modeName[5..]
            : modeName;

        return body.Contains($"{w}x{h}", StringComparison.Ordinal) &&
               (body.Contains($"x{hz}", StringComparison.Ordinal) ||
                body.Contains($"x0x{hz}", StringComparison.Ordinal));
    }
}
