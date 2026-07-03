using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
using WinCodexBar.Core.Models;

namespace WinCodexBar.Core.Services;

internal sealed class CursorUsageSummary
{
    [JsonPropertyName("billingCycleStart")]
    public string? BillingCycleStart { get; set; }

    [JsonPropertyName("billingCycleEnd")]
    public string? BillingCycleEnd { get; set; }

    [JsonPropertyName("individualUsage")]
    public CursorIndividualUsage? IndividualUsage { get; set; }

    [JsonPropertyName("teamUsage")]
    public CursorTeamUsage? TeamUsage { get; set; }
}

internal sealed class CursorIndividualUsage
{
    [JsonPropertyName("plan")]
    public CursorPlanUsage? Plan { get; set; }

    [JsonPropertyName("overall")]
    public CursorMoneyUsage? Overall { get; set; }
}

internal sealed class CursorPlanUsage
{
    // Money values are in cents (e.g. 2000 = $20.00).
    [JsonPropertyName("used")]
    public long? Used { get; set; }

    [JsonPropertyName("limit")]
    public long? Limit { get; set; }

    // Percent fields are already percentage units; fractional values below 1.0 mean fractions of a percent.
    [JsonPropertyName("autoPercentUsed")]
    public double? AutoPercentUsed { get; set; }

    [JsonPropertyName("apiPercentUsed")]
    public double? ApiPercentUsed { get; set; }

    [JsonPropertyName("totalPercentUsed")]
    public double? TotalPercentUsed { get; set; }
}

internal sealed class CursorTeamUsage
{
    [JsonPropertyName("pooled")]
    public CursorMoneyUsage? Pooled { get; set; }
}

internal sealed class CursorMoneyUsage
{
    // Money values are in cents.
    [JsonPropertyName("used")]
    public long? Used { get; set; }

    [JsonPropertyName("limit")]
    public long? Limit { get; set; }
}

internal sealed record CursorRequestQuota(int Used, int Limit);

internal sealed record CursorWebUsageResult(CursorUsageSummary Summary, CursorRequestQuota? RequestQuota);

internal sealed record CursorMappedUsage(UsageWindow? Primary, UsageWindow? Secondary);

internal static class CursorUsageMapper
{
    public static CursorMappedUsage Map(CursorUsageSummary summary, CursorRequestQuota? requestQuota = null)
    {
        var billingCycleStart = IsoDate.Parse(summary.BillingCycleStart);
        var billingCycleEnd = IsoDate.Parse(summary.BillingCycleEnd);
        var windowMinutes = BillingCycleWindowMinutes(billingCycleStart, billingCycleEnd);

        // Legacy request-based plans meter by request count; the token-based Auto/API
        // percentages are meaningless against a request quota, so the Auto bar is hidden.
        if (requestQuota is { Limit: > 0 })
        {
            var requestPercent = Clamp(requestQuota.Used / (double)requestQuota.Limit * 100);
            return new CursorMappedUsage(
                CreateWindow("Total", requestPercent, windowMinutes, billingCycleEnd),
                Secondary: null);
        }

        var plan = summary.IndividualUsage?.Plan;
        var autoPercent = ClampNullable(plan?.AutoPercentUsed);

        return new CursorMappedUsage(
            CreateWindow("Total", ResolvePrimaryPercent(summary), windowMinutes, billingCycleEnd),
            autoPercent.HasValue
                ? CreateWindow("Auto", autoPercent.Value, windowMinutes, billingCycleEnd)
                : null);
    }

    public static ProviderUsageSnapshot ToSnapshot(CursorWebUsageResult usage, string sourceLabel)
    {
        var mapped = Map(usage.Summary, usage.RequestQuota);

        return new ProviderUsageSnapshot
        {
            Provider = ProviderKind.Cursor,
            SourceLabel = sourceLabel,
            Primary = mapped.Primary,
            Secondary = mapped.Secondary,
            UpdatedAt = DateTimeOffset.Now
        };
    }

    private static double ResolvePrimaryPercent(CursorUsageSummary summary)
    {
        var plan = summary.IndividualUsage?.Plan;
        if (plan?.TotalPercentUsed is { } totalPercent)
        {
            return Clamp(totalPercent);
        }

        var autoPercent = ClampNullable(plan?.AutoPercentUsed);
        var apiPercent = ClampNullable(plan?.ApiPercentUsed);
        if (autoPercent.HasValue && apiPercent.HasValue)
        {
            return Clamp((autoPercent.Value + apiPercent.Value) / 2);
        }

        if (apiPercent.HasValue)
        {
            return apiPercent.Value;
        }

        if (autoPercent.HasValue)
        {
            return autoPercent.Value;
        }

        return RatioPercent(plan?.Used, plan?.Limit)
            ?? RatioPercent(summary.IndividualUsage?.Overall?.Used, summary.IndividualUsage?.Overall?.Limit)
            ?? RatioPercent(summary.TeamUsage?.Pooled?.Used, summary.TeamUsage?.Pooled?.Limit)
            ?? 0;
    }

    private static double? RatioPercent(long? used, long? limit)
    {
        if (limit is not > 0)
        {
            return null;
        }

        return Clamp((used ?? 0) / (double)limit.Value * 100);
    }

    private static UsageWindow CreateWindow(string label, double usedPercent, int? windowMinutes, DateTimeOffset? resetsAt)
    {
        return new UsageWindow
        {
            Label = label,
            UsedPercent = usedPercent,
            WindowMinutes = windowMinutes,
            ResetsAt = resetsAt,
            ResetDescription = UsageWindowFormatter.FormatResetDescription(resetsAt)
        };
    }

    private static int? BillingCycleWindowMinutes(DateTimeOffset? start, DateTimeOffset? end)
    {
        if (start == null || end == null)
        {
            return null;
        }

        var minutes = (int)Math.Round((end.Value - start.Value).TotalMinutes);
        return minutes > 0 ? minutes : null;
    }

    private static double Clamp(double value)
    {
        return Math.Clamp(value, 0, 100);
    }

    private static double? ClampNullable(double? value)
    {
        return value.HasValue ? Clamp(value.Value) : null;
    }
}

internal static class CursorWebApiFetcher
{
    private const string BaseUrl = "https://cursor.com";

    public static async Task<CursorWebUsageResult> FetchUsageAsync(
        HttpClient httpClient,
        string cookieHeader,
        string? knownUserId,
        CancellationToken cancellationToken)
    {
        // /api/auth/me and /api/usage are best-effort extras; their failure never fails
        // the refresh, so the chain safely runs alongside the primary usage-summary request.
        var requestQuotaTask = TryFetchRequestQuotaChainAsync(httpClient, cookieHeader, knownUserId, cancellationToken);
        var summary = await FetchUsageSummaryAsync(httpClient, cookieHeader, cancellationToken);

        return new CursorWebUsageResult(summary, await requestQuotaTask);
    }

    private static async Task<CursorRequestQuota?> TryFetchRequestQuotaChainAsync(
        HttpClient httpClient,
        string cookieHeader,
        string? knownUserId,
        CancellationToken cancellationToken)
    {
        // The app-token source already knows the user id from the JWT; only the
        // manual-cookie path needs to resolve it via /api/auth/me.
        var userId = knownUserId ?? await TryFetchUserIdAsync(httpClient, cookieHeader, cancellationToken);
        return string.IsNullOrWhiteSpace(userId)
            ? null
            : await TryFetchRequestQuotaAsync(httpClient, cookieHeader, userId, cancellationToken);
    }

    private static async Task<CursorUsageSummary> FetchUsageSummaryAsync(
        HttpClient httpClient,
        string cookieHeader,
        CancellationToken cancellationToken)
    {
        using var request = CreateRequest($"{BaseUrl}/api/usage-summary", cookieHeader);
        using var response = await httpClient.SendAsync(request, cancellationToken);
        var json = await response.Content.ReadAsStringAsync(cancellationToken);

        if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
        {
            throw new InvalidOperationException("Not logged in to Cursor. Sign in to Cursor and update the cookie header.");
        }

        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"Cursor usage failed ({(int)response.StatusCode}).");
        }

        return JsonSerializer.Deserialize<CursorUsageSummary>(json) ?? new CursorUsageSummary();
    }

    private static async Task<string?> TryFetchUserIdAsync(
        HttpClient httpClient,
        string cookieHeader,
        CancellationToken cancellationToken)
    {
        try
        {
            using var request = CreateRequest($"{BaseUrl}/api/auth/me", cookieHeader);
            using var response = await httpClient.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            var json = await response.Content.ReadAsStringAsync(cancellationToken);
            using var doc = JsonDocument.Parse(json);
            // Normalize the sub the same way the app-token path does so both
            // paths query /api/usage with the same user-id shape.
            return doc.RootElement.TryGetProperty("sub", out var sub) && sub.ValueKind == JsonValueKind.String
                ? CursorAppSession.TryExtractUserId(sub.GetString())
                : null;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return null;
        }
    }

    private static async Task<CursorRequestQuota?> TryFetchRequestQuotaAsync(
        HttpClient httpClient,
        string cookieHeader,
        string userId,
        CancellationToken cancellationToken)
    {
        try
        {
            using var request = CreateRequest($"{BaseUrl}/api/usage?user={Uri.EscapeDataString(userId)}", cookieHeader);
            using var response = await httpClient.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            var json = await response.Content.ReadAsStringAsync(cancellationToken);
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("gpt-4", out var gpt4) || gpt4.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            var limit = GetInt(gpt4, "maxRequestUsage");
            if (limit is not > 0)
            {
                return null;
            }

            var used = GetInt(gpt4, "numRequestsTotal") ?? GetInt(gpt4, "numRequests") ?? 0;
            return new CursorRequestQuota(used, limit.Value);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return null;
        }
    }

    private static HttpRequestMessage CreateRequest(string url, string cookieHeader)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Add("Cookie", cookieHeader);
        request.Headers.Add("Accept", "application/json");
        request.Headers.Add("User-Agent", "WinCodexBar");
        return request;
    }

    private static int? GetInt(JsonElement element, string name)
    {
        return element.TryGetProperty(name, out var value) &&
               value.ValueKind == JsonValueKind.Number &&
               value.TryGetInt32(out var parsed)
            ? parsed
            : null;
    }
}
