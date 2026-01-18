using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using Windows.Storage;

namespace tray_ui.Services;

public sealed class FileCacheEntry
{
    public string Path { get; set; } = string.Empty;
    public long Size { get; set; }
    public long Offset { get; set; }
    public long LastWriteUtcTicks { get; set; }
    public string? IncompleteLineBase64 { get; set; }
}

public sealed class ScanCache
{
    public Dictionary<string, FileCacheEntry> Files { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

public sealed class CacheStore
{
    private const string CacheFileName = "scan-cache.json";
    private static string CachePath =>
        Path.Combine(ApplicationData.Current.LocalFolder.Path, CacheFileName);

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    public async Task<ScanCache> LoadAsync()
    {
        try
        {
            if (!File.Exists(CachePath))
            {
                return new ScanCache();
            }

            var json = await File.ReadAllTextAsync(CachePath).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(json))
            {
                return new ScanCache();
            }

            var cache = JsonSerializer.Deserialize<ScanCache>(json, SerializerOptions);
            return cache ?? new ScanCache();
        }
        catch
        {
            return new ScanCache();
        }
    }

    public async Task SaveAsync(ScanCache cache)
    {
        var json = JsonSerializer.Serialize(cache, SerializerOptions);
        Directory.CreateDirectory(ApplicationData.Current.LocalFolder.Path);
        await File.WriteAllTextAsync(CachePath, json).ConfigureAwait(false);
    }

    public Task ClearAsync()
    {
        if (File.Exists(CachePath))
        {
            File.Delete(CachePath);
        }

        return Task.CompletedTask;
    }
}
