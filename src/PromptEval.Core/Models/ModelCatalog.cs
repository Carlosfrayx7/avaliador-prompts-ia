using Anthropic.Core;

namespace PromptEval.Core.Models;

public static class ClaudeModels
{
    public const string Opus = "claude-opus-5-5";
    public const string Haiku = "claude-haiku-5-5";

    public static string Resolve(string? nameOrId) => nameOrId?.Trim().ToLowerInvariant() switch
    {
        "opus" or Opus => Opus,
        "haiku" or Haiku => Haiku,
        _ => throw new ArgumentException($"Modelo não suportado: '{nameOrId}'. Use opus ou haiku."),
    };

    public static string ShortName(string model) => model switch
    {
        Opus => "opus",
        Haiku => "haiku",
        _ => model,
    };
}

/// <summary>Preços por milhão de tokens (API da Anthropic, novembro de 2026).</summary>
public static class ModelPricing
{
    private sealed record Price(decimal Input, decimal Output, decimal CacheRead);

    private static readonly Dictionary<string, Price> PerMillion = new()
    {
        [ClaudeModels.Opus] = new(4.00m, 20.00m, 0.20m),
        [ClaudeModels.Haiku] = new(0.10m, 0.50m, 0.01m),
    };

    /// <summary>Escrita no cache (TTL de 5 minutos) custa 1,25× a entrada.</summary>
    public static decimal EstimateUsd(string model, TokenUsage usage)
    {
        if (!PerMillion.TryGetValue(model, out var p)) throw new ArgumentException($"Sem preço para {model}.");
        var usd = usage.Input * p.Input
                  + usage.CacheWrite * p.Input * 1.25m
                  + usage.CacheRead * p.CacheRead
                  + usage.Output * p.Output;
        return Math.Round(usd / 1_000_000m, 6);
    }
}

/// <summary>O SDK expõe stop_reason e category como ApiEnum; <c>Raw()</c> devolve o valor exato do JSON.</summary>
public static class WireValue
{
    public static string? Of<TEnum>(ApiEnum<string, TEnum>? value) where TEnum : struct, Enum => value?.Raw();
}

public static class AnthropicCredentials
{
    public static bool Available()
    {
        if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY")) ||
            !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("ANTHROPIC_AUTH_TOKEN")))
            return true;

        var configDirs = new[]
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Anthropic"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config", "anthropic"),
        };
        return configDirs.Select(d => Path.Combine(d, "credentials"))
            .Any(d => Directory.Exists(d) && Directory.EnumerateFiles(d, "*.json").Any());
    }
}
