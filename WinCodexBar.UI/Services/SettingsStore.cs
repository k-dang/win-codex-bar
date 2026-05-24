using System;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using WinCodexBar.Core.Models;

namespace WinCodexBar.UI.Services;

public interface IAppSettingsStore
{
    Task<AppSettings> LoadAsync();

    Task<AppSettings> SaveAsync(AppSettings settings);
}

public sealed class FileAppSettingsStore : IAppSettingsStore
{
    private const string SettingsFileName = "settings.json";

    private static string SettingsDirectoryPath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WinCodexBar");

    private static string SettingsPath =>
        Path.Combine(SettingsDirectoryPath, SettingsFileName);

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    private readonly string _settingsPath;

    public FileAppSettingsStore(string settingsPath)
    {
        if (string.IsNullOrWhiteSpace(settingsPath))
        {
            throw new ArgumentException("Settings path is required.", nameof(settingsPath));
        }

        _settingsPath = settingsPath;
    }

    public static FileAppSettingsStore CreateDefault()
    {
        return new FileAppSettingsStore(SettingsPath);
    }

    public async Task<AppSettings> LoadAsync()
    {
        AppSettings defaults = AppSettings.CreateDefault();

        try
        {
            if (!File.Exists(_settingsPath))
            {
                return defaults;
            }

            var json = await File.ReadAllTextAsync(_settingsPath);
            if (string.IsNullOrWhiteSpace(json))
            {
                return defaults;
            }

            AppSettings? settings = JsonSerializer.Deserialize<AppSettings>(json, SerializerOptions);
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

    public async Task<AppSettings> SaveAsync(AppSettings settings)
    {
        if (settings == null)
        {
            throw new ArgumentNullException(nameof(settings));
        }

        settings.NormalizeProviders();

        var json = JsonSerializer.Serialize(settings, SerializerOptions);
        var directory = Path.GetDirectoryName(_settingsPath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        await File.WriteAllTextAsync(_settingsPath, json);
        return settings;
    }
}
