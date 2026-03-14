namespace WinCodexBar.Core.Models;

public sealed class ProviderDefinition
{
    public ProviderKind Kind { get; init; } = ProviderKind.Unknown;
    public string DisplayName { get; init; } = string.Empty;
    public string UsageTitle { get; init; } = string.Empty;
    public string SettingsTitle { get; init; } = string.Empty;
    public string EnabledLabel { get; init; } = string.Empty;
    public string SourceLabel { get; init; } = string.Empty;
    public IReadOnlyList<ProviderSourceMode> SupportedSourceModes { get; init; } = Array.Empty<ProviderSourceMode>();
    public string PrimaryUsageLabel { get; init; } = "Session";
    public string SecondaryUsageLabel { get; init; } = "Weekly";
    public bool SupportsCookieHeader { get; init; }
    public string CookieSourceLabel { get; init; } = string.Empty;
    public string CookieHeaderPlaceholder { get; init; } = string.Empty;
}

public static class ProviderCatalog
{
    private static readonly IReadOnlyList<ProviderSourceMode> DefaultSourceModes = new[]
    {
        ProviderSourceMode.Auto,
        ProviderSourceMode.OAuth,
        ProviderSourceMode.Web,
        ProviderSourceMode.Cli
    };

    public static readonly IReadOnlyList<ProviderDefinition> SupportedProviders = new[]
    {
        new ProviderDefinition
        {
            Kind = ProviderKind.Codex,
            DisplayName = "Codex",
            UsageTitle = "Codex usage",
            SettingsTitle = "Codex Provider",
            EnabledLabel = "Enable Codex usage",
            SourceLabel = "Codex source",
            SupportedSourceModes = DefaultSourceModes,
            SupportsCookieHeader = true,
            CookieSourceLabel = "Codex cookie source",
            CookieHeaderPlaceholder = "Codex cookie header (manual)",
            PrimaryUsageLabel = "Session",
            SecondaryUsageLabel = "Weekly"
        },
        new ProviderDefinition
        {
            Kind = ProviderKind.Claude,
            DisplayName = "Claude Code",
            UsageTitle = "Claude Code usage",
            SettingsTitle = "Claude Code Provider",
            EnabledLabel = "Enable Claude Code usage",
            SourceLabel = "Claude source",
            SupportedSourceModes = DefaultSourceModes,
            SupportsCookieHeader = true,
            CookieSourceLabel = "Claude cookie source",
            CookieHeaderPlaceholder = "Claude cookie header (manual)",
            PrimaryUsageLabel = "Session",
            SecondaryUsageLabel = "Weekly"
        }
    };

    private static readonly IReadOnlyDictionary<ProviderKind, ProviderDefinition> DefinitionsByKind =
        SupportedProviders.ToDictionary(provider => provider.Kind);

    public static IEnumerable<ProviderKind> SupportedProviderKinds => SupportedProviders.Select(provider => provider.Kind);

    public static ProviderDefinition GetDefinition(ProviderKind provider)
    {
        return DefinitionsByKind.TryGetValue(provider, out var definition)
            ? definition
            : CreateFallbackDefinition(provider);
    }

    public static string GetDisplayName(ProviderKind provider)
    {
        return GetDefinition(provider).DisplayName;
    }

    public static string GetSourceDisplayName(ProviderSourceMode mode)
    {
        return mode switch
        {
            ProviderSourceMode.OAuth => "OAuth",
            ProviderSourceMode.Web => "Web (Cookies)",
            ProviderSourceMode.Cli => "CLI",
            _ => "Auto"
        };
    }

    public static string GetCookieSourceDisplayName(CookieSourceMode mode)
    {
        return mode switch
        {
            CookieSourceMode.Manual => "Manual",
            _ => "Auto"
        };
    }

    private static ProviderDefinition CreateFallbackDefinition(ProviderKind provider)
    {
        var displayName = provider == ProviderKind.Unknown ? "Unknown" : provider.ToString();

        return new ProviderDefinition
        {
            Kind = provider,
            DisplayName = displayName,
            UsageTitle = $"{displayName} usage",
            SettingsTitle = $"{displayName} Provider",
            EnabledLabel = $"Enable {displayName} usage",
            SourceLabel = $"{displayName} source",
            SupportedSourceModes = DefaultSourceModes,
            SupportsCookieHeader = true,
            CookieSourceLabel = $"{displayName} cookie source",
            CookieHeaderPlaceholder = $"{displayName} cookie header (manual)",
            PrimaryUsageLabel = "Session",
            SecondaryUsageLabel = "Weekly"
        };
    }
}
