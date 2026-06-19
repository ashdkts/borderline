using System.Text.Json;

namespace Borderline;

public sealed class AppSettings
{
    public int Top { get; set; }
    public int Bottom { get; set; }
    public int Left { get; set; }
    public int Right { get; set; }
    public bool Enabled { get; set; }

    public static AppSettings Default => new();

    private static string Path =>
        System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Borderline",
            "settings.json");

    public static AppSettings Load()
    {
        try
        {
            var path = Path;
            if (!File.Exists(path))
            {
                return Default;
            }

            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<AppSettings>(json) ?? Default;
        }
        catch
        {
            return Default;
        }
    }

    public void Save()
    {
        var path = Path;
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
        var json = JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(path, json);
    }
}
