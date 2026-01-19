using System;
using System.Globalization;
using System.Text.Json;
using tray_ui.Models;

namespace tray_ui.Services;

public static class LogParser
{
    public static bool TryParseLine(
        string line,
        ProviderKind providerHint,
        string sourceFile,
        out UsageRecord record)
    {
        record = new UsageRecord();

        if (string.IsNullOrWhiteSpace(line))
        {
            return false;
        }

        try
        {
            using var document = JsonDocument.Parse(line);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                return false;
            }

            var root = document.RootElement;
            if (!TryGetTimestamp(root, out var timestamp))
            {
                return false;
            }

            if (!TryGetUsage(root, out var inputTokens, out var outputTokens, out var totalTokens) &&
                !TryGetEventTokenUsage(root, out inputTokens, out outputTokens, out totalTokens))
            {
                return false;
            }

            var provider = TryGetProvider(root, providerHint);
            var sessionId = TryGetString(root, "session_id")
                ?? TryGetString(root, "sessionId")
                ?? TryGetString(root, "conversation_id")
                ?? TryGetString(root, "conversationId")
                ?? TryGetString(root, "trace_id")
                ?? TryGetString(root, "traceId");

            record = new UsageRecord
            {
                Provider = provider,
                SessionId = sessionId,
                Timestamp = timestamp,
                InputTokens = inputTokens,
                OutputTokens = outputTokens,
                TotalTokens = totalTokens,
                SourceFile = sourceFile
            };
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static ProviderKind TryGetProvider(JsonElement root, ProviderKind hint)
    {
        var provider = TryGetString(root, "provider");
        if (string.Equals(provider, "codex", StringComparison.OrdinalIgnoreCase))
        {
            return ProviderKind.Codex;
        }

        var model = TryGetString(root, "model") ?? TryGetString(root, "model_name");
        if (!string.IsNullOrWhiteSpace(model))
        {
            if (model.IndexOf("codex", StringComparison.OrdinalIgnoreCase) >= 0 ||
                model.IndexOf("gpt", StringComparison.OrdinalIgnoreCase) >= 0 ||
                model.IndexOf("openai", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return ProviderKind.Codex;
            }
        }

        return hint;
    }

    private static bool TryGetUsage(JsonElement root, out int inputTokens, out int outputTokens, out int totalTokens)
    {
        inputTokens = 0;
        outputTokens = 0;
        totalTokens = 0;

        if (TryGetObject(root, "usage", out var usage))
        {
            inputTokens = GetInt(usage, "input_tokens")
                ?? GetInt(usage, "prompt_tokens")
                ?? GetInt(usage, "inputTokens")
                ?? GetInt(usage, "promptTokens")
                ?? 0;

            outputTokens = GetInt(usage, "output_tokens")
                ?? GetInt(usage, "completion_tokens")
                ?? GetInt(usage, "outputTokens")
                ?? GetInt(usage, "completionTokens")
                ?? 0;

            totalTokens = GetInt(usage, "total_tokens")
                ?? GetInt(usage, "totalTokens")
                ?? 0;
        }

        if (inputTokens == 0 && outputTokens == 0 && totalTokens == 0)
        {
            inputTokens = GetInt(root, "input_tokens")
                ?? GetInt(root, "prompt_tokens")
                ?? GetInt(root, "inputTokens")
                ?? GetInt(root, "promptTokens")
                ?? 0;

            outputTokens = GetInt(root, "output_tokens")
                ?? GetInt(root, "completion_tokens")
                ?? GetInt(root, "outputTokens")
                ?? GetInt(root, "completionTokens")
                ?? 0;

            totalTokens = GetInt(root, "total_tokens")
                ?? GetInt(root, "totalTokens")
                ?? 0;
        }

        if (totalTokens == 0 && (inputTokens > 0 || outputTokens > 0))
        {
            totalTokens = inputTokens + outputTokens;
        }

        return totalTokens > 0;
    }

    private static bool TryGetEventTokenUsage(JsonElement root, out int inputTokens, out int outputTokens, out int totalTokens)
    {
        inputTokens = 0;
        outputTokens = 0;
        totalTokens = 0;

        var type = TryGetString(root, "type");
        if (!string.Equals(type, "event_msg", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (!TryGetObject(root, "payload", out var payload))
        {
            return false;
        }

        var payloadType = TryGetString(payload, "type");
        if (!string.Equals(payloadType, "token_count", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (!TryGetObject(payload, "info", out var info))
        {
            return false;
        }

        if (!TryGetObject(info, "last_token_usage", out var lastUsage) &&
            !TryGetObject(info, "total_token_usage", out lastUsage))
        {
            return false;
        }

        inputTokens = GetInt(lastUsage, "input_tokens") ?? 0;
        outputTokens = GetInt(lastUsage, "output_tokens") ?? 0;
        totalTokens = GetInt(lastUsage, "total_tokens") ?? 0;

        if (totalTokens == 0 && (inputTokens > 0 || outputTokens > 0))
        {
            totalTokens = inputTokens + outputTokens;
        }

        return totalTokens > 0;
    }

    private static bool TryGetTimestamp(JsonElement root, out DateTimeOffset timestamp)
    {
        timestamp = default;

        if (TryGetDateTimeOffset(root, "timestamp", out timestamp) ||
            TryGetDateTimeOffset(root, "time", out timestamp) ||
            TryGetDateTimeOffset(root, "created_at", out timestamp) ||
            TryGetDateTimeOffset(root, "createdAt", out timestamp) ||
            TryGetDateTimeOffset(root, "created", out timestamp) ||
            TryGetDateTimeOffset(root, "ts", out timestamp))
        {
            return true;
        }

        return false;
    }

    private static bool TryGetDateTimeOffset(JsonElement root, string property, out DateTimeOffset value)
    {
        value = default;
        if (!root.TryGetProperty(property, out var element))
        {
            return false;
        }

        switch (element.ValueKind)
        {
            case JsonValueKind.String:
                var text = element.GetString();
                if (DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out value))
                {
                    return true;
                }
                return false;
            case JsonValueKind.Number:
                if (element.TryGetInt64(out var numeric))
                {
                    value = numeric > 10_000_000_000L
                        ? DateTimeOffset.FromUnixTimeMilliseconds(numeric)
                        : DateTimeOffset.FromUnixTimeSeconds(numeric);
                    return true;
                }
                return false;
            default:
                return false;
        }
    }

    private static bool TryGetObject(JsonElement root, string property, out JsonElement value)
    {
        value = default;
        if (!root.TryGetProperty(property, out var element))
        {
            return false;
        }

        if (element.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        value = element;
        return true;
    }

    private static int? GetInt(JsonElement root, string property)
    {
        if (!root.TryGetProperty(property, out var element))
        {
            return null;
        }

        if (element.ValueKind == JsonValueKind.Number && element.TryGetInt32(out var value))
        {
            return value;
        }

        return null;
    }

    private static string? TryGetString(JsonElement root, string property)
    {
        if (!root.TryGetProperty(property, out var element))
        {
            return null;
        }

        return element.ValueKind == JsonValueKind.String ? element.GetString() : null;
    }
}
