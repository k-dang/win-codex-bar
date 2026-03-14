namespace WinCodexBar.Core.Models;

public class AppSettings
{
    public int RefreshMinutes { get; set; } = 5;
    public Dictionary<ProviderKind, ProviderSettings> Providers { get; set; } = new();

    public static AppSettings CreateDefault()
    {
        return new AppSettings
        {
            Providers = ProviderCatalog.SupportedProviderKinds
                .ToDictionary(provider => provider, ProviderSettings.CreateDefault)
        };
    }

    public void NormalizeProviders()
    {
        Providers ??= new Dictionary<ProviderKind, ProviderSettings>();

        foreach (var provider in ProviderCatalog.SupportedProviderKinds)
        {
            if (!Providers.TryGetValue(provider, out var providerSettings) || providerSettings == null)
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
