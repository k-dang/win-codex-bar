using System;
using System.Collections.Generic;
using System.IO;

namespace tray_ui.Models;

public class AppSettings
{
    public List<string> LogRoots { get; set; } = new();
    public int RefreshMinutes { get; set; } = 5;
    public bool WatchFileChanges { get; set; } = false;
    public Dictionary<ProviderKind, ProviderSettings> Providers { get; set; } = new();
    public ProviderSettings Codex { get; set; } = ProviderSettings.CreateDefault(ProviderKind.Codex);

    public static AppSettings CreateDefault()
    {
        var settings = new AppSettings();
        var roots = new List<string>();

        var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (!string.IsNullOrWhiteSpace(userProfile))
        {
            roots.Add(Path.Combine(userProfile, ".codex", "logs"));
            roots.Add(Path.Combine(userProfile, ".codex", "sessions"));
        }

        settings.LogRoots = NormalizeRoots(roots);
        settings.Codex = ProviderSettings.CreateDefault(ProviderKind.Codex);
        settings.Providers = new Dictionary<ProviderKind, ProviderSettings>
        {
            [ProviderKind.Codex] = settings.Codex
        };
        return settings;
    }

    public void NormalizeProviders()
    {
        Providers ??= new Dictionary<ProviderKind, ProviderSettings>();
        Codex ??= ProviderSettings.CreateDefault(ProviderKind.Codex);

        if (Providers.TryGetValue(ProviderKind.Codex, out var codexSettings) && codexSettings != null)
        {
            Codex = codexSettings;
        }
        else
        {
            Providers[ProviderKind.Codex] = Codex;
        }
    }

    public IEnumerable<KeyValuePair<ProviderKind, ProviderSettings>> EnumerateProviders()
    {
        NormalizeProviders();
        return Providers;
    }

    public ProviderSettings GetProviderSettings(ProviderKind provider)
    {
        NormalizeProviders();

        if (Providers.TryGetValue(provider, out var settings) && settings != null)
        {
            return settings;
        }

        settings = ProviderSettings.CreateDefault(provider);
        Providers[provider] = settings;
        return settings;
    }

    public static List<string> NormalizeRoots(IEnumerable<string> roots)
    {
        var deduped = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var root in roots)
        {
            if (string.IsNullOrWhiteSpace(root))
            {
                continue;
            }

            var trimmed = root.Trim();
            if (trimmed.Length == 0)
            {
                continue;
            }

            deduped.Add(trimmed);
        }

        return new List<string>(deduped);
    }
}
