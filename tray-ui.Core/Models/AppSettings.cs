using System;
using System.Collections.Generic;
using System.IO;

namespace tray_ui.Models;

public class AppSettings
{
    public List<string> LogRoots { get; set; } = new();
    public int RefreshMinutes { get; set; } = 5;
    public bool WatchFileChanges { get; set; } = false;
    public ProviderSettings Codex { get; set; } = ProviderSettings.CreateDefault(ProviderKind.Codex);
    public ProviderSettings Claude { get; set; } = ProviderSettings.CreateDefault(ProviderKind.Claude);

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

        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        if (!string.IsNullOrWhiteSpace(appData))
        {
            roots.Add(Path.Combine(appData, "Claude", "logs"));
        }

        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (!string.IsNullOrWhiteSpace(localAppData))
        {
            roots.Add(Path.Combine(localAppData, "Claude", "logs"));
        }

        settings.LogRoots = NormalizeRoots(roots);
        settings.Codex = ProviderSettings.CreateDefault(ProviderKind.Codex);
        settings.Claude = ProviderSettings.CreateDefault(ProviderKind.Claude);
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
