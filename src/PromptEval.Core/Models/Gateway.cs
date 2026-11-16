using System.Text.Json;

namespace PromptEval.Core.Models;

/// <summary>Uma chamada à API com saída estruturada. O gateway fica atrás de interface para os testes rodarem sem rede.</summary>
public sealed record GatewayRequest(
    string Model, string SystemPrompt, string UserText, JsonElement Schema, int MaxTokens, bool CacheSystemPrompt);

public sealed record GatewayResponse(
    string StopReason, string? Text, TokenUsage Usage, string? RefusalCategory);

public sealed record TokenUsage(long Input, long Output, long CacheRead = 0, long CacheWrite = 0)
{
    public static readonly TokenUsage Zero = new(0, 0);
    public TokenUsage Add(TokenUsage other) =>
        new(Input + other.Input, Output + other.Output, CacheRead + other.CacheRead, CacheWrite + other.CacheWrite);
}

public interface IMessagesGateway
{
    Task<GatewayResponse> SendAsync(GatewayRequest request, CancellationToken ct);
}
