using System.Text.Json;
using System.Text.Json.Serialization;

namespace Loop.Core.Settings;

/// <summary>Loads and saves <see cref="LoopSettings"/> as JSON.</summary>
public static class SettingsStore
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        Converters = { new JsonStringEnumConverter() },
    };

    /// <summary>The default location: %APPDATA%\LoopForWindows\settings.json.</summary>
    public static string DefaultPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "LoopForWindows", "settings.json");

    /// <summary>
    /// Loads settings. A missing file gives the defaults. An unreadable or invalid file also gives
    /// the defaults, plus a message explaining why, so the app always starts.
    /// </summary>
    public static (LoopSettings Settings, string? Problem) Load(string path)
    {
        if (!File.Exists(path)) return (LoopSettings.CreateDefault(), null);

        try
        {
            var settings = Deserialize(File.ReadAllText(path));
            var errors = settings.Validate();
            return errors.Count == 0
                ? (settings, null)
                : (LoopSettings.CreateDefault(), $"{path} has invalid settings, so defaults are in use: {string.Join(" ", errors)}");
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException or NotSupportedException)
        {
            return (LoopSettings.CreateDefault(), $"Could not read {path}, so defaults are in use: {ex.Message}");
        }
    }

    /// <summary>Saves atomically (write a temp file, then replace), creating the folder if needed.</summary>
    public static void Save(string path, LoopSettings settings)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        var temp = path + ".tmp";
        File.WriteAllText(temp, Serialize(settings));
        File.Move(temp, path, overwrite: true);
    }

    public static string Serialize(LoopSettings settings) => JsonSerializer.Serialize(settings, Options);

    public static LoopSettings Deserialize(string json)
    {
        var settings = JsonSerializer.Deserialize<LoopSettings>(json, Options) ?? LoopSettings.CreateDefault();
        // "null" for a collection in the file means "use the default".
        var defaults = LoopSettings.CreateDefault();
        settings.CycleSizes ??= defaults.CycleSizes;
        settings.DirectionKeys ??= defaults.DirectionKeys;
        settings.ActionKeys ??= defaults.ActionKeys;
        settings.ExcludedApps ??= defaults.ExcludedApps;
        settings.PreviewColor ??= defaults.PreviewColor;
        return settings;
    }
}
