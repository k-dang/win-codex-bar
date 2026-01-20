using System.Collections.Generic;

namespace tray_ui.Models;

public class AppSettings
{
    public int RefreshMinutes { get; set; } = 5;
    public Dictionary<ProviderKind, ProviderSettings> Providers { get; set; } = new();
    public ProviderSettings Codex { get; set; } = ProviderSettings.CreateDefault(ProviderKind.Codex);

    public static AppSettings CreateDefault()
    {
        var settings = new AppSettings
        {
            Codex = ProviderSettings.CreateDefault(ProviderKind.Codex)
        };
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

        foreach (var provider in ProviderCatalog.SupportedProviders)
        {
            if (!Providers.TryGetValue(provider, out var settings) || settings == null)
            {
                Providers[provider] = ProviderSettings.CreateDefault(provider);
            }
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

}
