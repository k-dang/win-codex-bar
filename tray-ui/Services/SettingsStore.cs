using System;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using Windows.Storage;
using tray_ui.Models;

namespace tray_ui.Services;

public class SettingsStore
{
    private const string SettingsFileName = "settings.json";

    private static string SettingsPath =>
        Path.Combine(ApplicationData.Current.LocalFolder.Path, SettingsFileName);

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    public async Task<AppSettings> LoadAsync()
    {
        var defaults = AppSettings.CreateDefault();

        try
        {
            if (!File.Exists(SettingsPath))
            {
                return defaults;
            }

            var json = await File.ReadAllTextAsync(SettingsPath);
            if (string.IsNullOrWhiteSpace(json))
            {
                return defaults;
            }

            var settings = JsonSerializer.Deserialize<AppSettings>(json, SerializerOptions);
            if (settings == null)
            {
                return defaults;
            }

            if (settings.RefreshMinutes <= 0)
            {
                settings.RefreshMinutes = defaults.RefreshMinutes;
            }

            settings.NormalizeProviders();

            return settings;
        }
        catch
        {
            return defaults;
        }
    }

    public async Task SaveAsync(AppSettings settings)
    {
        if (settings == null)
        {
            throw new ArgumentNullException(nameof(settings));
        }

        settings.NormalizeProviders();

        var json = JsonSerializer.Serialize(settings, SerializerOptions);
        Directory.CreateDirectory(ApplicationData.Current.LocalFolder.Path);
        await File.WriteAllTextAsync(SettingsPath, json);
    }
}
