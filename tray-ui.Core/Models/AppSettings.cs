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
