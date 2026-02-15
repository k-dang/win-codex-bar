namespace WinCodexBar.Core.Models;

public class AppSettings
{
    public int RefreshMinutes { get; set; } = 5;
    public Dictionary<ProviderKind, ProviderSettings> Providers { get; set; } = new();
    public ProviderSettings Codex { get; set; } = ProviderSettings.CreateDefault(ProviderKind.Codex);
    public ProviderSettings Claude { get; set; } = ProviderSettings.CreateDefault(ProviderKind.Claude);

    public static AppSettings CreateDefault()
    {
        var settings = new AppSettings
        {
            Codex = ProviderSettings.CreateDefault(ProviderKind.Codex),
            Claude = ProviderSettings.CreateDefault(ProviderKind.Claude)
        };
        settings.Providers = new Dictionary<ProviderKind, ProviderSettings>
        {
            [ProviderKind.Codex] = settings.Codex,
            [ProviderKind.Claude] = settings.Claude
        };
        return settings;
    }

    public void NormalizeProviders()
    {
        if (Providers.TryGetValue(ProviderKind.Codex, out ProviderSettings? codexSettings))
        {
            Codex = codexSettings;
        }
        else
        {
            Providers[ProviderKind.Codex] = Codex;
        }

        if (Providers.TryGetValue(ProviderKind.Claude, out ProviderSettings? claudeSettings))
        {
            Claude = claudeSettings;
        }
        else
        {
            Providers[ProviderKind.Claude] = Claude;
        }

        foreach (var provider in ProviderCatalog.SupportedProviders)
        {
            if (!Providers.TryGetValue(provider, out ProviderSettings _))
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

        if (Providers.TryGetValue(provider, out ProviderSettings? settings))
        {
            return settings;
        }

        settings = ProviderSettings.CreateDefault(provider);
        Providers[provider] = settings;
        return settings;
    }
}
