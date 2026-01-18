using System;

namespace tray_ui.Models;

public enum ProviderSourceMode
{
    Auto = 0,
    OAuth = 1,
    Web = 2,
    Cli = 3
}

public enum CookieSourceMode
{
    Auto = 0,
    Manual = 1
}

public sealed class ProviderSettings
{
    public bool Enabled { get; set; } = true;
    public ProviderSourceMode SourceMode { get; set; } = ProviderSourceMode.Auto;
    public CookieSourceMode CookieSource { get; set; } = CookieSourceMode.Auto;
    public string? CookieHeader { get; set; }

    public static ProviderSettings CreateDefault(ProviderKind provider)
    {
        return provider switch
        {
            ProviderKind.Codex => new ProviderSettings
            {
                Enabled = true,
                SourceMode = ProviderSourceMode.Auto,
                CookieSource = CookieSourceMode.Auto
            },
            ProviderKind.Claude => new ProviderSettings
            {
                Enabled = true,
                SourceMode = ProviderSourceMode.Auto,
                CookieSource = CookieSourceMode.Auto
            },
            _ => new ProviderSettings()
        };
    }
}
