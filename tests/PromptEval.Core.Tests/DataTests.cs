using PromptEval.Core.Data;
using PromptEval.Core.Triage;

namespace PromptEval.Core.Tests;

public class DataTests
{
    private static string RepoFile(params string[] parts)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "AvaliadorPrompts.slnx"))) dir = dir.Parent;
        return Path.Combine([dir!.FullName, .. parts]);
    }

    [Fact]
    public void Prompt_header_and_body_are_parsed_and_policy_is_injected()
    {
        var prompt = PromptVersion.Parse("v9", "---\nnome: v9 · teste\ndescricao: algo\n---\nRegras.\n{{politica}}\n")
            .WithPolicy("  POLITICA  ");
        Assert.Equal("v9 · teste", prompt.Name);
        Assert.Equal("algo", prompt.Description);
        Assert.Equal("Regras.\nPOLITICA", prompt.SystemPrompt);
    }

    [Theory]
    [InlineData("sem cabeçalho")]
    [InlineData("---\nnome: x\n")]
    [InlineData("---\nnome: x\n---\n   ")]
    public void Malformed_prompts_are_rejected(string content)
    {
        Assert.Throws<InvalidDataException>(() => PromptVersion.Parse("x", content));
    }

    [Fact]
    public void Versioned_dataset_is_valid_and_balanced()
    {
        var cases = JsonLines.LoadCases(RepoFile("datasets", "chamados.jsonl"));
        Assert.Equal(40, cases.Count);
        Assert.All(Taxonomy.Categories, c => Assert.InRange(cases.Count(x => x.Category == c), 5, 8));
        Assert.True(cases.Count(c => c.Tags.Contains("injecao")) >= 4);
    }

    [Fact]
    public void Calibration_set_references_existing_cases_and_is_balanced()
    {
        var cases = JsonLines.LoadCases(RepoFile("datasets", "chamados.jsonl")).Select(c => c.Id).ToHashSet();
        var items = JsonLines.Parse<JudgeCalibrationItem>(File.ReadAllText(RepoFile("datasets", "juiz-calibracao.jsonl")), "cal");
        Assert.All(items, i => Assert.Contains(i.CaseId, cases));
        Assert.Equal(items.Count / 2, items.Count(i => i.ExpectedApproved));
    }

    [Fact]
    public void Every_versioned_prompt_loads_and_has_no_placeholder_left()
    {
        var policy = File.ReadAllText(RepoFile("datasets", "politica-loja.md"));
        var files = Directory.GetFiles(RepoFile("prompts"), "*.md");
        Assert.Equal(3, files.Length);
        foreach (var file in files)
        {
            var prompt = PromptVersion.Load(RepoFile("prompts"), Path.GetFileNameWithoutExtension(file), policy);
            Assert.DoesNotContain(PromptVersion.PolicyPlaceholder, prompt.SystemPrompt);
        }
    }

    [Fact]
    public void Dataset_rejects_unknown_category_and_duplicate_ids()
    {
        TicketCase Make(string id, string category) => new(id, "t", category, "baixa", ["p"], []);
        Assert.Throws<InvalidDataException>(() => JsonLines.Validate([Make("a", "nao_existe")]));
        Assert.Throws<InvalidDataException>(() => JsonLines.Validate([Make("a", "outro"), Make("a", "outro")]));
    }

    [Fact]
    public void Invalid_json_line_reports_file_and_line()
    {
        var ex = Assert.Throws<InvalidDataException>(() => JsonLines.Parse<TicketCase>("\n{bad", "x.jsonl"));
        Assert.StartsWith("x.jsonl:2", ex.Message);
    }

    [Fact]
    public void Triage_schema_lists_every_category_and_parse_accepts_valid_output()
    {
        var schema = TriageTask.Schema.ToString();
        Assert.All(Taxonomy.Categories, c => Assert.Contains($"\"{c}\"", schema));
        Assert.NotNull(TriageTask.TryParse("""{"categoria":"outro","urgencia":"baixa","resposta":"oi"}"""));
        Assert.Null(TriageTask.TryParse("""{"categoria":"outro"}"""));
    }
}
