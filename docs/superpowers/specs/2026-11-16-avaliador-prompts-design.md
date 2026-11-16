# Avaliador de prompts com IA - especificação

Data: 2026-11-16 · Status: decidido pelo autor do código com autonomia dada pelo dono do repositório

## 1. Objetivo

Projeto 2 do portfólio de engenharia de IA: um avaliador de prompts que mede, com números, se uma versão nova de um prompt ficou melhor ou pior. Ele roda versões de um prompt contra uma base de casos com gabarito, em mais de um modelo, avalia as respostas com verificações em código e com um modelo juiz, e publica um painel estático com placar, custo, latência, comparação caso a caso e detecção de regressão.

A tarefa avaliada é a **triagem de chamados de suporte** de uma loja virtual fictícia: classificar a categoria e a urgência e escrever um rascunho de resposta ao cliente.

O que o projeto demonstra:
- avaliação de LLM com gabarito: métricas determinísticas + modelo juiz (LLM-as-judge) com rubrica;
- validação do próprio juiz contra um conjunto de respostas com veredito conhecido;
- versionamento de prompts e comparação entre versões (o que melhorou, o que piorou);
- portão de regressão (`gate`) que falha o CI quando uma versão nova piora;
- saída estruturada, cache de prompt e medição de custo e latência por execução;
- robustez a injeção de prompt (casos em que o cliente tenta mudar as instruções).

**Fora do escopo:** interface para editar prompts, execução de avaliações pelo navegador (o painel é só leitura), banco de dados, autenticação, ajuste automático de prompt, repetições múltiplas por caso para medir variância.

## 2. Decisões

| Tema | Decisão |
|---|---|
| Linguagem | C# / .NET 10, mesmo padrão do projeto 1 |
| SDK | pacote oficial `Anthropic` (NuGet) |
| Modelos avaliados | `claude-haiku-5-5` e `claude-opus-5-5` |
| Juiz | `claude-opus-5-5`, esforço `medium`, saída estruturada |
| Prompts | 3 versões em `prompts/*.md` (v1 básico, v2 com regras, v3 com regras + exemplos e política) |
| Base | ~40 chamados fictícios escritos à mão em `datasets/chamados.jsonl` |
| Painel | HTML/CSS/JS estático em `site/`, lendo `site/data.json`; publicado no GitHub Pages |
| Credenciais | `ant auth login` ou `ANTHROPIC_API_KEY`; nunca em arquivo |
| Idioma | código em inglês; prompts, dados, README e painel em português |

## 3. Estrutura

```
avaliador-prompts-ia/
├─ src/PromptEval.Core/     casos, prompts, execução, avaliadores, métricas, comparação
├─ src/PromptEval.Cli/      comandos run, judge-check, compare, gate, site
├─ tests/PromptEval.Core.Tests/
├─ prompts/                 v1-basico.md, v2-regras.md, v3-exemplos.md
├─ datasets/                chamados.jsonl, juiz-calibracao.jsonl
├─ runs/                    resultados versionados (JSON)
├─ site/                    index.html, app.js, style.css, data.json (gerado)
└─ .github/workflows/       ci.yml (build, testes, gate) e pages.yml
```

## 4. Tarefa e saída

Saída estruturada (esquema estrito):

```
{ categoria: "entrega" | "pagamento" | "troca_devolucao" | "produto_defeituoso" | "conta_acesso" | "outro",
  urgencia: "baixa" | "media" | "alta",
  resposta: string }
```

Cada caso tem `id`, `texto`, `categoria`, `urgencia`, `pontos` (o que a resposta precisa tratar) e `tags` (`ambiguo`, `injecao`, `irritado`, `multiplos_assuntos`...).

## 5. Avaliadores

| Avaliador | Tipo | Regra |
|---|---|---|
| `categoria` | código | igual ao gabarito |
| `urgencia` | código | igual ao gabarito |
| `formato` | código | JSON válido e resposta entre 20 e 180 palavras |
| `politica` | código | resposta sem promessas proibidas (reembolso garantido, prazo exato inventado, pedido de senha/cartão) |
| `juiz` | modelo | rubrica: cobre os pontos do caso, tom adequado, não inventa política; nota 1-5 por critério + aprovado/reprovado + justificativa |

Nota do caso = média das aprovações dos avaliadores. Nota da execução = média dos casos; também por tag.

Calibração do juiz: `datasets/juiz-calibracao.jsonl` tem respostas escritas à mão com veredito conhecido (boas e com defeitos plantados). `judge-check` mede a concordância do juiz com esse gabarito.

## 6. CLI

- `run --prompt v3 --model haiku [--limit N]`: executa e grava `runs/<prompt>__<modelo>.json` (respostas, notas, tokens, cache, custo, latência). Mostra a estimativa de custo e pede confirmação (`--yes` pula).
- `judge-check`: concordância do juiz; grava `runs/judge-check.json`.
- `compare <a> <b>`: casos que melhoraram e pioraram.
- `gate --baseline <a> --candidate <b> --max-drop 0.02`: código de saída 1 se a nota cair mais que o limite ou se algum caso de injeção passar a falhar.
- `site`: gera `site/data.json` a partir de `runs/`.

## 7. Painel

Página estática: placar (prompt × modelo: nota, categoria, urgência, juiz, custo, latência p95), gráfico custo × nota, notas por tag, comparação entre duas execuções escolhidas e detalhe de cada caso (chamado, resposta, veredito de cada avaliador, justificativa do juiz). Sem dependências externas além de fontes do sistema.

## 8. Testes e CI

- xUnit, sem chamar a API: avaliadores de código, métricas, comparação, gate, carregamento de prompts e casos, execução com gateway falso, parse do juiz.
- CI: build, testes e `gate` sobre as execuções versionadas (sem segredos). Pages publica `site/` na main.

## 9. Critérios de pronto

- Build e testes passam local e no CI.
- 6 execuções (3 prompts × 2 modelos) + calibração do juiz versionadas; painel publicado.
- README com placar, decisões técnicas e limitações; nenhum segredo no histórico.
