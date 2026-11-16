namespace PromptEval.Core.Data;

/// <summary>
/// Uma versão de prompt guardada em <c>prompts/&lt;id&gt;.md</c>. O cabeçalho entre linhas <c>---</c>
/// traz <c>nome</c> e <c>descricao</c>; o resto do arquivo é o prompt de sistema enviado ao modelo.
/// </summary>
public sealed record PromptVersion(string Id, string Name, string Description, string SystemPrompt)
{
    public static PromptVersion Parse(string id, string content)
    {
        var text = content.Replace("\r\n", "\n");
        if (!text.StartsWith("---\n")) throw new InvalidDataException($"{id}: falta o cabeçalho '---'.");
        var end = text.IndexOf("\n---\n", 4, StringComparison.Ordinal);
        if (end < 0) throw new InvalidDataException($"{id}: cabeçalho sem fechamento '---'.");

        var header = text[4..end].Split('\n')
            .Select(l => l.Split(':', 2))
            .Where(p => p.Length == 2)
            .ToDictionary(p => p[0].Trim(), p => p[1].Trim());
        var body = text[(end + 5)..].Trim();
        if (body.Length == 0) throw new InvalidDataException($"{id}: prompt vazio.");

        return new PromptVersion(
            id,
            header.GetValueOrDefault("nome") ?? id,
            header.GetValueOrDefault("descricao") ?? "",
            body);
    }

    public const string PolicyPlaceholder = "{{politica}}";

    /// <summary>A política da loja fica num arquivo só; prompts que a usam trazem o marcador <c>{{politica}}</c>.</summary>
    public PromptVersion WithPolicy(string policy) =>
        this with { SystemPrompt = SystemPrompt.Replace(PolicyPlaceholder, policy.Trim()) };

    public static PromptVersion Load(string promptsDir, string id, string policy) =>
        Load(promptsDir, id).WithPolicy(policy);

    public static PromptVersion Load(string promptsDir, string id)
    {
        var path = Path.Combine(promptsDir, id + ".md");
        if (!File.Exists(path))
        {
            var available = Directory.Exists(promptsDir)
                ? string.Join(", ", Directory.GetFiles(promptsDir, "*.md").Select(Path.GetFileNameWithoutExtension))
                : "nenhum";
            throw new FileNotFoundException($"Prompt '{id}' não encontrado. Disponíveis: {available}.");
        }
        return Parse(id, File.ReadAllText(path));
    }
}
