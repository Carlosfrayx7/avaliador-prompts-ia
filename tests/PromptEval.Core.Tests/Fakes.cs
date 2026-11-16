using System.Collections.Concurrent;
using PromptEval.Core.Data;
using PromptEval.Core.Grading;
using PromptEval.Core.Models;
using PromptEval.Core.Runs;
using PromptEval.Core.Triage;

namespace PromptEval.Core.Tests;

/// <summary>Gateway falso: responde pelo conteúdo do pedido, sem rede.</summary>
public sealed class FakeGateway(Func<GatewayRequest, GatewayResponse> respond) : IMessagesGateway
{
    public ConcurrentQueue<GatewayRequest> Requests { get; } = new();

    public Task<GatewayResponse> SendAsync(GatewayRequest request, CancellationToken ct)
    {
        Requests.Enqueue(request);
        return Task.FromResult(respond(request));
    }

    public static GatewayResponse Ok(string json, long input = 1000, long output = 200) =>
        new("end_turn", json, new TokenUsage(input, output), null);

    public const string GoodJudge = """{"justificativa":"ok","cobertura":5,"tom":5,"fidelidade":5}""";
    public const string BadJudge = """{"justificativa":"inventa prazo","cobertura":4,"tom":5,"fidelidade":2}""";

    public static bool IsJudge(GatewayRequest r) => r.SystemPrompt.Contains("Você avalia rascunhos");
}

public static class Build
{
    public static TicketCase Case(string id = "c01", string category = "entrega", string urgency = "media", params string[] tags) =>
        new(id, $"texto do chamado {id}", category, urgency, ["ponto"], tags.Length == 0 ? ["simples"] : tags);

    public static string Reply(int words = 30) => string.Join(' ', Enumerable.Repeat("palavra", words));

    public static TriageOutput Output(string category = "entrega", string urgency = "media", string? reply = null) =>
        new(category, urgency, reply ?? Reply());

    public static CaseResult Result(string id, bool[] passed, params string[] tags)
    {
        var grades = GraderNames.All.Select((g, i) => new Grade(g, passed[i], "")).ToList();
        return new CaseResult(id, tags, Output(), null, grades, new TokenUsage(100, 10), 0.001m, 1000, null, 0.002m);
    }

    public static bool[] All(bool value) => Enumerable.Repeat(value, GraderNames.All.Count).ToArray();

    public static RunResult Run(string prompt, params CaseResult[] cases) =>
        new(prompt, prompt, "", ClaudeModels.Haiku, ClaudeModels.Opus, DateTimeOffset.UnixEpoch, cases);
}
