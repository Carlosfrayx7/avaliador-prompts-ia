using Anthropic;
using Anthropic.Models.Messages;

namespace PromptEval.Core.Models;

public sealed class AnthropicMessagesGateway(AnthropicClient client) : IMessagesGateway
{
    public async Task<GatewayResponse> SendAsync(GatewayRequest request, CancellationToken ct)
    {
        var schema = request.Schema.EnumerateObject().ToDictionary(p => p.Name, p => p.Value.Clone());

        // O prompt de sistema é o prefixo comum a todos os casos de uma execução; com cache, os casos
        // seguintes pagam só a leitura do cache. Prefixos abaixo do mínimo do modelo simplesmente não são guardados.
        var system = new List<TextBlockParam>
        {
            request.CacheSystemPrompt
                ? new TextBlockParam { Text = request.SystemPrompt, CacheControl = new CacheControlEphemeral() }
                : new TextBlockParam { Text = request.SystemPrompt },
        };

        var response = await client.Messages.Create(new MessageCreateParams
        {
            Model = request.Model,
            MaxTokens = request.MaxTokens,
            System = system,
            OutputConfig = new OutputConfig
            {
                Effort = Effort.Medium,
                Format = new JsonOutputFormat { Schema = schema },
            },
            Messages = [new UserMessageParam { Content = request.UserText }],
        }, ct);

        var text = string.Concat(response.Content.Select(b => b.Value).OfType<TextBlock>().Select(t => t.Text));
        var usage = new TokenUsage(
            response.Usage.InputTokens,
            response.Usage.OutputTokens,
            response.Usage.CacheReadInputTokens ?? 0,
            response.Usage.CacheCreationInputTokens ?? 0);
        return new GatewayResponse(
            WireValue.Of(response.StopReason) ?? "unknown",
            string.IsNullOrWhiteSpace(text) ? null : text,
            usage,
            WireValue.Of(response.StopDetails?.Category));
    }
}
