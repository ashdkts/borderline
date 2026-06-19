using System.Diagnostics;
using System.Net.Http;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;

namespace Borderline;

internal static class UpdateService
{
    private const string Repo = "ashdkts/borderline";

    private sealed class ReleaseManifest
    {
        public string Version { get; set; } = "";
        public string Url { get; set; } = "";
        public string Sha256 { get; set; } = "";
    }

    public static async Task<string?> CheckAndInstallAsync(IProgress<string> progress, CancellationToken token)
    {
        var current = Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "0.0.0";
        var url = $"https://github.com/{Repo}/releases/latest/download/latest.json";

        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        var json = await client.GetStringAsync(url, token).ConfigureAwait(false);
        var manifest = JsonSerializer.Deserialize<ReleaseManifest>(json);
        if (manifest is null || string.IsNullOrWhiteSpace(manifest.Version) || string.IsNullOrWhiteSpace(manifest.Url))
        {
            return null;
        }

        if (!IsNewer(manifest.Version, current))
        {
            return null;
        }

        progress.Report($"Downloading v{manifest.Version}…");
        var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Borderline", "updates");
        Directory.CreateDirectory(dir);
        var target = Path.Combine(dir, $"borderline-{manifest.Version}.exe");

        if (!File.Exists(target) || !HashMatches(target, manifest.Sha256))
        {
            var bytes = await client.GetByteArrayAsync(manifest.Url, token).ConfigureAwait(false);
            if (!string.IsNullOrWhiteSpace(manifest.Sha256))
            {
                var hash = Convert.ToHexString(SHA256.HashData(bytes));
                if (!hash.Equals(manifest.Sha256, StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException("Update checksum mismatch.");
                }
            }

            await File.WriteAllBytesAsync(target, bytes, token).ConfigureAwait(false);
        }

        var self = Environment.ProcessPath ?? Process.GetCurrentProcess().MainModule?.FileName;
        if (string.IsNullOrWhiteSpace(self))
        {
            return null;
        }

        self = Path.GetFullPath(self);
        if (string.Equals(self, Path.GetFullPath(target), StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var script = Path.Combine(Path.GetDirectoryName(self)!, "borderline-update.cmd");
        var cmd = $"""
            @echo off
            timeout /t 2 /nobreak >nul
            copy /Y "{target}" "{self}" >nul
            start "" "{self}"
            del "%~f0"
            """;
        await File.WriteAllTextAsync(script, cmd, token).ConfigureAwait(false);

        Process.Start(new ProcessStartInfo("cmd.exe", $"/C \"{script}\"") { UseShellExecute = true });
        Environment.Exit(0);
        return manifest.Version;
    }

    private static bool IsNewer(string latest, string current)
    {
        static int Parse(string v)
        {
            v = v.TrimStart('v');
            var parts = v.Split('.');
            var n = 0;
            for (var i = 0; i < Math.Min(3, parts.Length); i++)
            {
                _ = int.TryParse(parts[i], out var x);
                n = n * 100 + x;
            }

            return n;
        }

        return Parse(latest) > Parse(current);
    }

    private static bool HashMatches(string path, string expected)
    {
        if (string.IsNullOrWhiteSpace(expected))
        {
            return true;
        }

        using var stream = File.OpenRead(path);
        var hash = Convert.ToHexString(SHA256.HashData(stream));
        return hash.Equals(expected, StringComparison.OrdinalIgnoreCase);
    }
}
