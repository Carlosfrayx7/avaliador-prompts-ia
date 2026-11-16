using Anthropic;
using PromptEval.Core.Data;
using PromptEval.Core.Grading;
using PromptEval.Core.Models;
using PromptEval.Core.Runs;

var root = RepoRoot();
var paths = new
{
    Prompts = Path.Combine(root, "prompts"),
    Cases = Path.Combine(root, "datasets", "chamados.jsonl"),
    Calibration = Path.Combine(root, "datasets", "juiz-calibracao.jsonl"),
    Policy = Path.Combine(root, "datasets", "politica-loja.md"),
    Runs = Path.Combine(root, "runs"),
    Site = Path.Combine(root, "site"),
};
var command = args.FirstOrDefault();

try
{
    return command switch
    {
        "run" => await Run(),
        "judge-check" => await JudgeCheck(),
        "compare" => Compare(),
        "gate" => Gate(),
        "site" => Site(),
        _ => Usage(),
    };
}
catch (Exception ex) when (ex is FileNotFoundException or InvalidDataException or ArgumentException or FormatException)
{
    Console.Error.WriteLine(ex.Message);
    return 2;
}

async Task<int> Run()
{
    var prompts = (Arg("--prompt") ?? "v1-basico,v2-regras,v3-exemplos").Split(',', StringSplitOptions.TrimEntries)
        .Select(id => PromptVersion.Load(paths.Prompts, id, Policy())).ToList();
    var models = (Arg("--model") ?? "haiku,opus").Split(',', StringSplitOptions.TrimEntries)
        .Select(ClaudeModels.Resolve).Distinct().ToList();
    var cases = JsonLines.LoadCases(paths.Cases);
    if (Arg("--limit") is { } limit) cases = cases.Take(int.Parse(limit)).ToList();

    // Estimativa conservadora: 1.500 tokens de entrada e 1.200 de saída por resposta; o juiz (Opus) com 3.000 de entrada.
    var calls = prompts.Count * models.Count * cases.Count;
    var estimate = prompts.Count * models.Sum(m =>
                       ModelPricing.EstimateUsd(m, new TokenUsage(1_500L * cases.Count, 1_200L * cases.Count)))
                   + calls * ModelPricing.EstimateUsd(ClaudeModels.Opus, new TokenUsage(3_000, 1_200));
    Console.WriteLine($"{prompts.Count} prompt(s) × {models.Count} modelo(s) × {cases.Count} casos = {calls} respostas + {calls} julgamentos.");
    Console.WriteLine($"Custo estimado: até US$ {estimate:0.00}.");
    if (!Confirm()) return 0;

    var gateway = Gateway();
    var runner = new EvalRunner(gateway, Judge(gateway), TimeProvider.System);
    Directory.CreateDirectory(paths.Runs);

    foreach (var prompt in prompts)
    foreach (var model in models)
    {
        Console.Write($"{prompt.Id} × {ClaudeModels.ShortName(model)}: ");
        var progress = new Progress<int>(n => { if (n % 10 == 0) Console.Write($"{n} "); });
        var run = await runner.RunAsync(prompt, model, cases, progress, CancellationToken.None);
        await File.WriteAllTextAsync(Path.Combine(paths.Runs, run.Key + ".json"), run.ToJson());

        var s = RunSummary.Of(run);
        Console.WriteLine();
        Console.WriteLine($"  nota {s.Score:P1} | categoria {s.ByGrader[GraderNames.Category]:P0} | urgência {s.ByGrader[GraderNames.Urgency]:P0} | juiz {s.ByGrader[GraderNames.Judge]:P0} | erros {s.Errors}");
        Console.WriteLine($"  custo US$ {s.CostUsd:0.0000} (+ juiz US$ {s.JudgeCostUsd:0.0000}) | p95 {s.P95LatencyMs} ms | cache lido {s.CacheReadTokens} tokens");
    }
    return 0;
}

async Task<int> JudgeCheck()
{
    var items = JsonLines.Parse<JudgeCalibrationItem>(File.ReadAllText(paths.Calibration), "juiz-calibracao.jsonl");
    var cases = JsonLines.LoadCases(paths.Cases);
    var estimate = items.Count * ModelPricing.EstimateUsd(ClaudeModels.Opus, new TokenUsage(3_000, 1_200));
    Console.WriteLine($"{items.Count} respostas com veredito conhecido. Custo estimado: até US$ {estimate:0.00}.");
    if (!Confirm()) return 0;

    var gateway = Gateway();
    var result = await JudgeCalibrationResult.RunAsync(Judge(gateway), items, cases, TimeProvider.System, CancellationToken.None);
    Directory.CreateDirectory(paths.Runs);
    await File.WriteAllTextAsync(Path.Combine(paths.Runs, "judge-check.json"),
        System.Text.Json.JsonSerializer.Serialize(result, RunResult.JsonOptions));

    Console.WriteLine($"Concordância {result.Agreement:P0} | ruins reprovadas {result.BadCaught:P0} | boas aprovadas {result.GoodKept:P0} | US$ {result.CostUsd:0.0000}");
    foreach (var miss in result.Items.Where(i => !i.Agrees))
        Console.WriteLine($"  divergiu {miss.Id} ({miss.Reason}): esperado {miss.Expected}, juiz {miss.Actual?.ToString() ?? miss.Error}");
    return 0;
}

int Compare()
{
    var (baseline, candidate) = (LoadRun(Positional(1)), LoadRun(Positional(2)));
    var c = RunComparison.Of(baseline, candidate);
    Console.WriteLine($"{baseline.Key} → {candidate.Key}: nota {c.ScoreDelta:+0.0%;-0.0%;0.0%}");
    foreach (var x in c.Improved) Console.WriteLine($"  melhorou {x.Id}: {x.Before:P0} → {x.After:P0} (+{string.Join(", ", x.Gained)})");
    foreach (var x in c.Regressed) Console.WriteLine($"  piorou   {x.Id}: {x.Before:P0} → {x.After:P0} (-{string.Join(", ", x.Lost)})");
    return 0;
}

int Gate()
{
    var baseline = LoadRun(Arg("--baseline") ?? throw new ArgumentException("Informe --baseline."));
    var candidate = LoadRun(Arg("--candidate") ?? throw new ArgumentException("Informe --candidate."));
    var maxDrop = double.Parse(Arg("--max-drop") ?? "0.02", System.Globalization.CultureInfo.InvariantCulture);
    var result = RegressionGate.Check(baseline, candidate, maxDrop);
    Console.WriteLine(result.Passed
        ? $"gate ok: {candidate.Key} não regrediu em relação a {baseline.Key}"
        : $"gate falhou: {candidate.Key}\n  " + string.Join("\n  ", result.Reasons));
    return result.Passed ? 0 : 1;
}

int Site()
{
    var runs = RunResult.LoadAll(paths.Runs);
    var cases = JsonLines.LoadCases(paths.Cases);
    var prompts = Directory.GetFiles(paths.Prompts, "*.md").Order(StringComparer.Ordinal)
        .Select(f => PromptVersion.Load(paths.Prompts, Path.GetFileNameWithoutExtension(f), Policy())).ToList();
    var calibrationFile = Path.Combine(paths.Runs, "judge-check.json");
    var calibration = File.Exists(calibrationFile)
        ? System.Text.Json.JsonSerializer.Deserialize<JudgeCalibrationResult>(File.ReadAllText(calibrationFile), RunResult.JsonOptions)
        : null;
    var latest = runs.Count == 0 ? DateTimeOffset.Now : runs.Max(r => r.RanAt);
    File.WriteAllText(Path.Combine(paths.Site, "data.json"), SiteData.Build(runs, cases, prompts, calibration, latest));
    Console.WriteLine($"site/data.json: {runs.Count} execuções, {cases.Count} casos.");
    return 0;
}

int Usage()
{
    Console.WriteLine("""
        Uso: dotnet run --project src/PromptEval.Cli -- <comando>

          run [--prompt v1-basico,v2-regras] [--model haiku,opus] [--limit N] [--yes]
          judge-check [--yes]
          compare <execucao-base> <execucao-nova>        ex.: v2-regras__haiku v3-exemplos__haiku
          gate --baseline <execucao> --candidate <execucao> [--max-drop 0.02]
          site
        """);
    return command is null or "help" or "--help" ? 0 : 2;
}

RunResult LoadRun(string keyOrPath)
{
    var path = File.Exists(keyOrPath) ? keyOrPath : Path.Combine(paths.Runs, keyOrPath + ".json");
    if (!File.Exists(path)) throw new FileNotFoundException($"Execução não encontrada: {keyOrPath}");
    return RunResult.Load(path);
}

IMessagesGateway Gateway()
{
    if (!AnthropicCredentials.Available())
        throw new ArgumentException("Sem credenciais: rode `ant auth login` ou defina ANTHROPIC_API_KEY.");
    return new AnthropicMessagesGateway(new AnthropicClient());
}

string Policy() => File.ReadAllText(paths.Policy).Trim();

JudgeGrader Judge(IMessagesGateway gateway) => new(gateway, Policy());

bool Confirm()
{
    if (args.Contains("--yes")) return true;
    Console.Write("Continuar? [s/N] ");
    return string.Equals(Console.ReadLine()?.Trim(), "s", StringComparison.OrdinalIgnoreCase);
}

string? Arg(string flag)
{
    var i = Array.IndexOf(args, flag);
    return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
}

string Positional(int index) =>
    args.Length > index && !args[index].StartsWith("--")
        ? args[index]
        : throw new ArgumentException("Uso: compare <execucao-base> <execucao-nova>");

static string RepoRoot()
{
    var dir = new DirectoryInfo(Directory.GetCurrentDirectory());
    while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "AvaliadorPrompts.slnx"))) dir = dir.Parent;
    return dir?.FullName ?? Directory.GetCurrentDirectory();
}
