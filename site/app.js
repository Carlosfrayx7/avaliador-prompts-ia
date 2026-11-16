"use strict";

const GRADERS = ["categoria", "urgencia", "formato", "politica", "juiz"];
const GRADER_LABEL = { categoria: "Categoria", urgencia: "Urgência", formato: "Formato", politica: "Política", juiz: "Juiz" };
const MODEL_LABEL = { haiku: "Haiku 5.5", opus: "Opus 5.5" };
const TAG_LABEL = {
  simples: "Simples", ambiguo: "Ambíguo", irritado: "Cliente irritado", injecao: "Injeção de instruções",
  multiplos_assuntos: "Vários assuntos", informal: "Informal", seguranca: "Segurança",
};
const CAT_LABEL = {
  entrega: "Entrega", pagamento: "Pagamento", troca_devolucao: "Troca e devolução",
  produto_defeituoso: "Produto com defeito", conta_acesso: "Conta e acesso", outro: "Outro",
};
const URG_LABEL = { baixa: "Baixa", media: "Média", alta: "Alta" };
const LOW = 0.8;
const COST_WARNING_RATIO = 10;
const JUDGE_WARNING = 0.7;
const GATE_MAX_DROP = 0.02;

const pct = (v, digits = 0) => `${(v * 100).toLocaleString("pt-BR", { minimumFractionDigits: digits, maximumFractionDigits: digits })}%`;
const usd = (v) => `US$ ${Number(v).toLocaleString("pt-BR", { minimumFractionDigits: v < 0.1 ? 4 : 2, maximumFractionDigits: v < 0.1 ? 4 : 2 })}`;
const pp = (v) => `${(v * 100).toLocaleString("pt-BR", { minimumFractionDigits: 1, maximumFractionDigits: 1 })} p.p.`;
const secs = (ms) => `${(ms / 1000).toLocaleString("pt-BR", { minimumFractionDigits: 1, maximumFractionDigits: 1 })} s`;
const int = (v) => Number(v).toLocaleString("pt-BR");
const esc = (s) => String(s ?? "").replace(/[&<>"']/g, (c) => ({ "&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;", "'": "&#39;" }[c]));
const $ = (id) => document.getElementById(id);
const promptShort = (run) => run.promptNome.split("·")[0].trim();
const runName = (run) => `${run.promptNome} · ${MODEL_LABEL[run.modelo] ?? run.modelo}`;
const lens = '<svg aria-hidden="true" viewBox="0 0 20 20"><circle cx="8" cy="8" r="5.5"/><path d="M12 12l5 5"/></svg>';

let data;
let byKey = {};
let caseById = {};
let selectedCase = null;
let selectedRunKey = null;

init();

async function init() {
  wireTabs();
  try {
    const res = await fetch("data.json", { cache: "no-cache" });
    if (!res.ok) throw new Error(`HTTP ${res.status}`);
    data = await res.json();
  } catch (err) {
    $("verdict").innerHTML = `<p class="error-note">Não foi possível carregar data.json (${esc(err.message)}). Gere o arquivo com <code>dotnet run --project src/PromptEval.Cli -- site</code> e sirva a pasta site por HTTP.</p>`;
    return;
  }
  if (!data.execucoes?.length) {
    $("verdict").innerHTML = '<p>Nenhuma execução encontrada. Rode <code>dotnet run --project src/PromptEval.Cli -- run</code> e depois o comando <code>site</code>.</p>';
    return;
  }
  data.execucoes.sort((a, b) => a.prompt.localeCompare(b.prompt) || a.modelo.localeCompare(b.modelo));
  data.execucoes.forEach((r) => {
    byKey[r.chave] = r;
    r.casoPorId = Object.fromEntries(r.casos.map((c) => [c.id, c]));
  });
  caseById = Object.fromEntries(data.casos.map((c) => [c.id, c]));
  selectedRunKey = winner().chave;

  renderVerdict();
  renderLabels();
  renderPlot();
  let resizeTimer;
  window.addEventListener("resize", () => { clearTimeout(resizeTimer); resizeTimer = setTimeout(renderPlot, 150); });
  renderTags();
  renderCompareControls();
  renderCaseFilters();
  renderCases();
  renderJudge();
  $("generated").textContent = `Execuções de ${new Date(data.geradoEm).toLocaleDateString("pt-BR", { day: "2-digit", month: "long", year: "numeric" })}.`;
}

/* Tabs: hash routing, standard tab keyboard behaviour */
function wireTabs() {
  const tabs = [...document.querySelectorAll('[role="tab"]')];
  const show = () => {
    const id = (location.hash || "#placar").slice(1);
    const known = tabs.some((t) => t.getAttribute("aria-controls") === id);
    const target = known ? id : "placar";
    tabs.forEach((t) => {
      const on = t.getAttribute("aria-controls") === target;
      t.setAttribute("aria-selected", String(on));
      t.tabIndex = on ? 0 : -1;
      $(t.getAttribute("aria-controls")).hidden = !on;
    });
  };
  tabs.forEach((t, i) => t.addEventListener("keydown", (e) => {
    const step = e.key === "ArrowRight" ? 1 : e.key === "ArrowLeft" ? -1 : 0;
    if (!step) return;
    e.preventDefault();
    const next = tabs[(i + step + tabs.length) % tabs.length];
    next.focus();
    location.hash = next.getAttribute("href");
  }));
  window.addEventListener("hashchange", show);
  show();
}

function goToCase(id, runKey) {
  if (runKey) selectedRunKey = runKey;
  location.hash = "#casos";
  selectCase(id);
  document.querySelector(`#cases-table tr[data-id="${id}"] .row-btn`)?.focus({ preventScroll: true });
  $("case-detail").scrollIntoView({ block: "nearest" });
}

/* Placar */
function winner() {
  return [...data.execucoes].sort((a, b) => b.resumo.nota - a.resumo.nota || a.resumo.custoUsd - b.resumo.custoUsd)[0];
}

function cheapestCost() {
  return Math.min(...data.execucoes.map((r) => r.resumo.custoUsd).filter((v) => v > 0));
}

function injectionFailures(run) {
  return data.casos
    .filter((c) => c.tags.includes("injecao"))
    .filter((c) => {
      const res = run.casoPorId[c.id];
      return !res || res.avaliacoes.some((g) => (g.avaliador === "politica" || g.avaliador === "juiz") && !g.aprovado);
    })
    .map((c) => c.id);
}

function warnings(run) {
  const s = run.resumo;
  const out = [];
  if (s.custoUsd > cheapestCost() * COST_WARNING_RATIO) out.push("Alto em custo");
  if (s.porAvaliador.juiz < JUDGE_WARNING) out.push("Baixo em aprovação do juiz");
  const inj = injectionFailures(run);
  if (inj.length) out.push(`Falha em injeção (${inj.length})`);
  if (s.erros > 0) out.push(`Erro da API (${s.erros})`);
  return out;
}

function renderVerdict() {
  const w = winner();
  const sameModel = data.execucoes.filter((r) => r.prompt === w.prompt && r.chave !== w.chave)[0];
  const first = data.execucoes.filter((r) => r.modelo === w.modelo).sort((a, b) => a.prompt.localeCompare(b.prompt))[0];
  let sub = "";
  if (first && first.chave !== w.chave) {
    sub += `Sair da ${promptShort(first)} para a ${promptShort(w)} no mesmo modelo levou a nota de ${pct(first.resumo.nota)} para ${pct(w.resumo.nota)}. `;
  }
  if (sameModel) {
    const ratio = sameModel.resumo.custoUsd / w.resumo.custoUsd;
    const diff = sameModel.resumo.nota - w.resumo.nota;
    const diffText = Math.abs(diff) < 0.001 ? "a mesma nota" : diff > 0 ? `${pp(diff)} a mais de nota` : `${pp(-diff)} a menos de nota`;
    if (ratio >= 1) sub += `Com o mesmo prompt, o ${MODEL_LABEL[sameModel.modelo]} custou ${ratio.toLocaleString("pt-BR", { maximumFractionDigits: 0 })} vezes mais, com ${diffText}.`;
  }
  // A versão seguinte no mesmo modelo: o portão a aceitaria?
  const next = data.execucoes.filter((r) => r.modelo === w.modelo && r.prompt > w.prompt)[0];
  let gateLine = "";
  if (next) {
    const g = compare(w, next);
    gateLine = g.reasons.length
      ? `A ${esc(promptShort(next))} (${esc(next.promptNome.split("·").slice(1).join("·").trim())}) seria barrada pelo portão de regressão: ${esc(g.reasons.join("; "))}. <a href="#comparar" data-pair="${w.chave}|${next.chave}">Ver a comparação</a>.`
      : `A ${esc(promptShort(next))} passaria no portão de regressão, com variação de ${g.delta >= 0 ? "+" : "−"}${pp(Math.abs(g.delta))}. <a href="#comparar" data-pair="${w.chave}|${next.chave}">Ver a comparação</a>.`;
  }
  $("verdict").innerHTML = `
    <p>Melhor resultado: <strong>${esc(runName(w))}</strong>, com nota de ${pct(w.resumo.nota, 1)} e ${usd(w.resumo.custoUsd)} para responder os ${w.resumo.casos} chamados.</p>
    ${sub ? `<p class="verdict-sub">${esc(sub)}</p>` : ""}
    ${gateLine ? `<p class="verdict-sub">${gateLine}</p>` : ""}`;
  $("verdict").querySelectorAll("a[data-pair]").forEach((a) => a.addEventListener("click", () => {
    const [base, cand] = a.dataset.pair.split("|");
    $("cmp-base").value = base;
    $("cmp-cand").value = cand;
    renderCompare();
    requestAnimationFrame(() => $("compare-title").focus());
  }));
}

function renderLabels() {
  const w = winner();
  $("labels").innerHTML = data.execucoes.map((run) => {
    const s = run.resumo;
    const ws = warnings(run);
    const row = (key) => {
      const v = s.porAvaliador[key];
      return `<tr class="sub${v < LOW ? " low" : ""}"><th scope="row">${GRADER_LABEL[key]}</th><td>${pct(v)}</td></tr>`;
    };
    const seals = ws.length
      ? ws.map((t) => `<span class="seal">${lens}${esc(t)}</span>`).join("")
      : '<span class="seal seal-ok">Sem alertas</span>';
    return `
      <article class="run-card" aria-labelledby="lbl-${run.chave}">
        <div class="seals">${seals}</div>
        <div class="nlabel${run.chave === w.chave ? " winner" : ""}">
          <h3 id="lbl-${run.chave}">${esc(promptShort(run))} · ${esc(MODEL_LABEL[run.modelo] ?? run.modelo)}</h3>
          <p class="serving">Porção de <b>${s.casos} chamados</b><br>${esc(run.promptNome.split("·").slice(1).join("·").trim())}</p>
          <table>
            <thead><tr><th scope="col">Aprovação por avaliador</th><th scope="col">Média</th></tr></thead>
            <tbody>
              <tr class="major"><th scope="row">Nota geral</th><td>${pct(s.nota, 1)}</td></tr>
              ${GRADERS.map(row).join("")}
              <tr class="sep"><th scope="row">Casos sem falha</th><td>${pct(s.casosAprovados)}</td></tr>
              <tr><th scope="row">Custo</th><td>${usd(s.custoUsd)}</td></tr>
              <tr><th scope="row">Custo do juiz</th><td>${usd(s.custoJuizUsd)}</td></tr>
              <tr><th scope="row">Latência média</th><td>${secs(s.latenciaMediaMs)}</td></tr>
              <tr><th scope="row">Latência p95</th><td>${secs(s.latenciaP95Ms)}</td></tr>
              <tr><th scope="row">Tokens do cache</th><td>${int(s.tokensCacheLidos)}</td></tr>
            </tbody>
          </table>
          <p class="foot-note">Nota geral: fração de avaliadores aprovados em cada chamado, na média dos ${s.casos} chamados.</p>
          ${run.chave === w.chave ? '<span class="winner-flag">Melhor nota da avaliação</span>' : ""}
        </div>
      </article>`;
  }).join("");
}

function renderPlot() {
  const runs = data.execucoes;
  const W = Math.max(300, Math.round($("plot").clientWidth || 520)), H = W < 420 ? 300 : 340, L = 44, R = 12, T = 20, B = 44;
  const costs = runs.map((r) => r.resumo.custoUsd);
  const lo = Math.log10(Math.min(...costs) / 1.6), hi = Math.log10(Math.max(...costs) * 1.6);
  const scores = runs.map((r) => r.resumo.nota);
  const sLo = Math.max(0, Math.floor(Math.min(...scores) * 10) / 10 - 0.05), sHi = 1;
  const x = (c) => L + ((Math.log10(c) - lo) / (hi - lo)) * (W - L - R);
  const y = (s) => T + (1 - (s - sLo) / (sHi - sLo)) * (H - T - B);
  const lat = runs.map((r) => r.resumo.latenciaP95Ms);
  const rad = (ms) => 5 + 11 * ((ms - Math.min(...lat)) / Math.max(1, Math.max(...lat) - Math.min(...lat)));

  const xTicks = [];
  for (let e = Math.ceil(lo); e <= Math.floor(hi); e++) xTicks.push(10 ** e);
  const yTicks = [];
  for (let v = Math.ceil(sLo * 10) / 10; v <= sHi + 1e-9; v += 0.1) yTicks.push(Number(v.toFixed(1)));

  // Rótulos à direita de cada ponto; quando dois ficam próximos, desce o de baixo e liga com uma linha.
  // Rótulo de 12px ao lado do ponto; se cruzar outro ponto ou rótulo, troca de lado ou desce em passos de 15px.
  const labelW = (p) => (promptShort(p.r).length + MODEL_LABEL[p.r.modelo].length + 3) * 6.6;
  const placed = runs.map((r) => ({ r, cx: x(r.resumo.custoUsd), cy: y(r.resumo.nota), rr: rad(r.resumo.latenciaP95Ms) }))
    .sort((a, b) => a.cy - b.cy);
  const boxes = [];
  const hits = (bx) => boxes.some((o) => bx.x1 < o.x2 && bx.x2 > o.x1 && bx.y1 < o.y2 && bx.y2 > o.y1)
    || placed.some((q) => q.cx + q.rr > bx.x1 && q.cx - q.rr < bx.x2 && q.cy + q.rr > bx.y1 && q.cy - q.rr < bx.y2);
  placed.forEach((p) => {
    const w = labelW(p);
    p.right = p.cx + p.rr + 8 + w < W - R;
    p.ly = p.cy + 4;
    for (let dy = 0; dy < 150; dy += 15) {
      for (const right of [p.right, !p.right]) {
        const lx = right ? p.cx + p.rr + 8 : p.cx - p.rr - 8;
        const bx = { x1: right ? lx : lx - w, x2: right ? lx + w : lx, y1: p.cy - 8 + dy, y2: p.cy + 6 + dy };
        if (bx.x1 < L || bx.x2 > W) continue;
        if (!hits(bx)) { p.right = right; p.ly = p.cy + 4 + dy; boxes.push(bx); return; }
      }
    }
  });
  const pts = placed.map((p) => {
    const lx = p.right ? p.cx + p.rr + 8 : p.cx - p.rr - 8;
    const leader = Math.abs(p.ly - 4 - p.cy) > 3 ? `<line class="leader" x1="${p.right ? p.cx + p.rr : p.cx - p.rr}" y1="${p.cy}" x2="${lx + (p.right ? -2 : 2)}" y2="${p.ly - 4}"/>` : "";
    return `<g>
      <circle class="pt ${esc(p.r.prompt)}" cx="${p.cx}" cy="${p.cy}" r="${p.rr}"><title>${esc(runName(p.r))}: nota ${pct(p.r.resumo.nota, 1)}, ${usd(p.r.resumo.custoUsd)}, p95 ${secs(p.r.resumo.latenciaP95Ms)}</title></circle>
      ${leader}<text class="pt-label" x="${lx}" y="${p.ly}" text-anchor="${p.right ? "start" : "end"}">${esc(promptShort(p.r))} · ${esc(MODEL_LABEL[p.r.modelo])}</text>
    </g>`;
  }).join("");

  $("plot").innerHTML = `
    <svg viewBox="0 0 ${W} ${H}" role="img" aria-label="Gráfico de custo por nota das ${runs.length} execuções">
      ${yTicks.map((v) => `<line class="grid" x1="${L}" x2="${W - R}" y1="${y(v)}" y2="${y(v)}"/><text x="${L - 8}" y="${y(v) + 4}" text-anchor="end">${pct(v)}</text>`).join("")}
      ${xTicks.map((v) => `<line class="grid" x1="${x(v)}" x2="${x(v)}" y1="${T}" y2="${H - B}"/><text x="${x(v)}" y="${H - B + 18}" text-anchor="middle">${usd(v)}</text>`).join("")}
      <line class="axis" x1="${L}" x2="${W - R}" y1="${H - B}" y2="${H - B}"/>
      <line class="axis" x1="${L}" x2="${L}" y1="${T}" y2="${H - B}"/>
      <text x="${(L + W - R) / 2}" y="${H - 6}" text-anchor="middle">Custo das 40 respostas</text>
      ${pts}
    </svg>`;
}

function renderTags() {
  const runs = data.execucoes;
  const tags = [...new Set(runs.flatMap((r) => Object.keys(r.resumo.porTag)))];
  const count = (t) => data.casos.filter((c) => c.tags.includes(t)).length;
  $("tags").innerHTML = `
    <table class="data">
      <thead><tr><th scope="col">Tipo</th>${runs.map((r) => `<th scope="col">${esc(promptShort(r))}<br>${esc(MODEL_LABEL[r.modelo])}</th>`).join("")}</tr></thead>
      <tbody>${tags.map((t) => `<tr><th scope="row">${esc(TAG_LABEL[t] ?? t)} <span class="muted">(${count(t)})</span></th>${runs.map((r) => {
        const v = r.resumo.porTag[t];
        return `<td class="${v < LOW ? "low" : ""}">${v === undefined ? "–" : pct(v)}</td>`;
      }).join("")}</tr>`).join("")}</tbody>
    </table>`;
}

/* Comparar */
function renderCompareControls() {
  const opts = data.execucoes.map((r) => `<option value="${r.chave}">${esc(runName(r))}</option>`).join("");
  const base = $("cmp-base"), cand = $("cmp-cand");
  base.innerHTML = opts;
  cand.innerHTML = opts;
  const w = winner();
  const prev = data.execucoes.filter((r) => r.modelo === w.modelo && r.prompt < w.prompt).pop();
  base.value = (prev ?? data.execucoes[0]).chave;
  cand.value = w.chave === base.value ? data.execucoes[data.execucoes.length - 1].chave : w.chave;
  base.addEventListener("change", renderCompare);
  cand.addEventListener("change", renderCompare);
  renderCompare();
}

function compare(a, b) {
  const improved = [], regressed = [], mixed = [], missing = [];
  for (const before of a.casos) {
    const now = b.casoPorId[before.id];
    if (!now) { missing.push(before.id); continue; }
    const ok = (c, g) => c.avaliacoes.some((x) => x.avaliador === g && x.aprovado);
    const gained = now.avaliacoes.filter((g) => g.aprovado && !ok(before, g.avaliador)).map((g) => g.avaliador);
    const lost = before.avaliacoes.filter((g) => g.aprovado && !ok(now, g.avaliador)).map((g) => g.avaliador);
    if (!gained.length && !lost.length) continue;
    const change = { id: before.id, before: before.nota, after: now.nota, gained, lost };
    if (now.nota > before.nota) improved.push(change);
    else if (now.nota < before.nota) regressed.push(change);
    else mixed.push(change);
  }
  const delta = b.resumo.nota - a.resumo.nota;
  const reasons = [];
  if (missing.length) reasons.push(`casos ausentes na versão nova: ${missing.join(", ")}`);
  if (delta < -GATE_MAX_DROP) reasons.push(`a nota caiu ${pp(-delta)} (limite ${pp(GATE_MAX_DROP)})`);
  const safety = new Set(data.casos.filter((c) => c.tags.includes("injecao")).map((c) => c.id));
  [...regressed, ...mixed].filter((c) => safety.has(c.id)).forEach((c) => {
    const critical = c.lost.filter((g) => g === "politica" || g === "juiz");
    if (critical.length) reasons.push(`o caso de injeção ${c.id} deixou de passar em ${critical.map((g) => GRADER_LABEL[g].toLowerCase()).join(" e ")}`);
  });
  return { improved, regressed, mixed, delta, reasons };
}

function renderCompare() {
  const a = byKey[$("cmp-base").value], b = byKey[$("cmp-cand").value];
  if (a.chave === b.chave) {
    $("compare-out").innerHTML = '<p class="empty">Escolha duas execuções diferentes para comparar.</p>';
    return;
  }
  const c = compare(a, b);
  const item = (x, kind) => `<li class="${kind}"><button type="button" data-case="${x.id}" data-run="${b.chave}">
      <span class="cid">${x.id}</span>
      <span><span>${esc(truncate(caseById[x.id]?.texto, 90))}</span><br>
      <span class="what">${pct(x.before)} → ${pct(x.after)}${x.lost.length ? ` · deixou de passar em ${x.lost.map((g) => GRADER_LABEL[g].toLowerCase()).join(", ")}` : ""}${x.gained.length ? ` · passou a aprovar em ${x.gained.map((g) => GRADER_LABEL[g].toLowerCase()).join(", ")}` : ""}</span></span>
    </button></li>`;
  $("compare-out").innerHTML = `
    <div class="gate ${c.reasons.length ? "fail" : "pass"}">
      <span class="stamp">${c.reasons.length ? "Reprovada" : "Aprovada"}</span>
      <div>
        <p>${c.reasons.length ? "O portão de regressão barraria esta versão no CI:" : `O portão de regressão aceitaria esta versão: a nota não caiu mais de ${pp(GATE_MAX_DROP)} e nenhum caso de injeção piorou.`}</p>
        ${c.reasons.length ? `<ul>${c.reasons.map((r) => `<li>${esc(r)}</li>`).join("")}</ul>` : ""}
      </div>
    </div>
    <div class="mini-label">
      <h3>Resumo da troca</h3>
      <table>
        <tbody>
          <tr class="major"><th scope="row">Variação da nota geral</th><td>${c.delta >= 0 ? "+" : "−"}${pp(Math.abs(c.delta))}</td></tr>
          <tr><th scope="row">Chamados que melhoraram</th><td>${c.improved.length}</td></tr>
          <tr><th scope="row">Chamados que pioraram</th><td>${c.regressed.length}</td></tr>
          <tr><th scope="row">Chamados com troca (ganhou e perdeu)</th><td>${c.mixed.length}</td></tr>
          <tr class="sep"><th scope="row">Custo da base</th><td>${usd(a.resumo.custoUsd)}</td></tr>
          <tr><th scope="row">Custo da nova versão</th><td>${usd(b.resumo.custoUsd)}</td></tr>
        </tbody>
      </table>
    </div>
    <div class="changes">
      <section><h3>Pioraram</h3>${c.regressed.length ? `<ul>${c.regressed.map((x) => item(x, "lost")).join("")}</ul>` : '<p class="none">Nenhum chamado piorou.</p>'}</section>
      <section><h3>Melhoraram</h3>${c.improved.length ? `<ul>${c.improved.map((x) => item(x, "gain")).join("")}</ul>` : '<p class="none">Nenhum chamado melhorou.</p>'}</section>
      ${c.mixed.length ? `<section class="wide"><h3>Trocaram: mesma nota, avaliadores diferentes</h3><ul>${c.mixed.map((x) => item(x, "mixed")).join("")}</ul></section>` : ""}
    </div>`;
  $("compare-out").querySelectorAll("button[data-case]").forEach((btn) =>
    btn.addEventListener("click", () => goToCase(btn.dataset.case, btn.dataset.run)));
}

/* Casos */
function renderCaseFilters() {
  const tags = [...new Set(data.casos.flatMap((c) => c.tags))].sort();
  $("f-tag").insertAdjacentHTML("beforeend", tags.map((t) => `<option value="${t}">${esc(TAG_LABEL[t] ?? t)}</option>`).join(""));
  const cats = [...new Set(data.casos.map((c) => c.categoria))];
  $("f-cat").insertAdjacentHTML("beforeend", cats.map((c) => `<option value="${c}">${esc(CAT_LABEL[c] ?? c)}</option>`).join(""));
  ["f-tag", "f-cat", "f-fail"].forEach((id) => $(id).addEventListener("change", renderCases));
}

function squares(res) {
  if (!res) return '<span class="sq" aria-label="sem resultado">' + GRADERS.map(() => '<i class="na"></i>').join("") + "</span>";
  const passed = GRADERS.map((g) => res.avaliacoes.some((x) => x.avaliador === g && x.aprovado));
  const label = GRADERS.map((g, i) => `${GRADER_LABEL[g]} ${passed[i] ? "aprovado" : "reprovado"}`).join(", ");
  return `<span class="sq" role="img" aria-label="${label}">${passed.map((p) => `<i class="${p ? "ok" : "ko"}"></i>`).join("")}</span>`;
}

function renderCases() {
  const tag = $("f-tag").value, cat = $("f-cat").value, onlyFail = $("f-fail").checked;
  const runs = data.execucoes;
  const rows = data.casos.filter((c) =>
    (!tag || c.tags.includes(tag)) &&
    (!cat || c.categoria === cat) &&
    (!onlyFail || runs.some((r) => (r.casoPorId[c.id]?.nota ?? 0) < 1)));

  if (!rows.length) {
    $("cases-table").innerHTML = '<p class="empty">Nenhum chamado com esses filtros. Troque o tipo ou a categoria.</p>';
    return;
  }
  $("cases-table").innerHTML = `
    <table class="data cases">
      <thead><tr><th scope="col">Chamado</th><th scope="col" class="txt">Texto do cliente</th>${runs.map((r) => `<th scope="col">${esc(promptShort(r))}<br>${esc(MODEL_LABEL[r.modelo])}</th>`).join("")}</tr></thead>
      <tbody>${rows.map((c) => `<tr data-id="${c.id}" aria-selected="${c.id === selectedCase}">
        <th scope="row"><button type="button" class="row-btn">${c.id}</button></th>
        <td class="txt">${esc(truncate(c.texto, 72))}</td>
        ${runs.map((r) => `<td>${squares(r.casoPorId[c.id])}</td>`).join("")}
      </tr>`).join("")}</tbody>
    </table>`;
  $("cases-table").querySelectorAll("tbody tr").forEach((tr) =>
    tr.addEventListener("click", () => {
      selectCase(tr.dataset.id);
      // Em uma coluna, o detalhe fica abaixo da tabela: leva o leitor até ele.
      if (matchMedia("(max-width: 980px)").matches) $("case-detail").scrollIntoView({ behavior: "smooth", block: "start" });
    }));
}

function selectCase(id) {
  selectedCase = id;
  document.querySelectorAll("#cases-table tbody tr").forEach((tr) => tr.setAttribute("aria-selected", String(tr.dataset.id === id)));
  renderDetail();
}

function renderDetail() {
  const c = caseById[selectedCase];
  if (!c) return;
  const run = byKey[selectedRunKey] ?? winner();
  const res = run.casoPorId[c.id];
  const judge = res?.juizNotas;
  $("case-detail").innerHTML = `
    <h3>Chamado ${c.id}</h3>
    <p class="ticket">${esc(c.texto)}</p>
    <dl>
      <dt>Gabarito</dt><dd>${esc(CAT_LABEL[c.categoria])} · urgência ${esc(URG_LABEL[c.urgencia].toLowerCase())}</dd>
      <dt>Tipo</dt><dd>${c.tags.map((t) => esc(TAG_LABEL[t] ?? t)).join(", ")}</dd>
    </dl>
    <p class="block-note" style="margin-bottom:4px"><b>A resposta precisa:</b></p>
    <ul class="points">${c.pontos.map((p) => `<li>${esc(p)}</li>`).join("")}</ul>
    <div class="run-pick" role="group" aria-label="Execução">
      ${data.execucoes.map((r) => `<button type="button" data-run="${r.chave}" aria-pressed="${r.chave === run.chave}">${esc(promptShort(r))} · ${esc(MODEL_LABEL[r.modelo])}</button>`).join("")}
    </div>
    ${!res ? '<p class="empty">Esta execução não tem este chamado.</p>' : `
      ${res.erro ? `<p class="error-note">Erro: ${esc(res.erro)}</p>` : ""}
      ${res.saida ? `<dl><dt>Classificou</dt><dd>${esc(CAT_LABEL[res.saida.categoria] ?? res.saida.categoria)} · urgência ${esc((URG_LABEL[res.saida.urgencia] ?? res.saida.urgencia).toLowerCase())}</dd></dl>` : ""}
      <ul class="grades">${GRADERS.map((g) => {
        const grade = res.avaliacoes.find((x) => x.avaliador === g);
        const ok = grade?.aprovado;
        return `<li><span class="sq" role="img" aria-label="${ok ? "aprovado" : "reprovado"}"><i class="${ok ? "ok" : "ko"}"></i></span><span class="gname">${GRADER_LABEL[g]}</span><span>${esc(grade?.detalhe ?? "")}</span></li>`;
      }).join("")}</ul>
      ${res.saida ? `<p class="block-note" style="margin:12px 0 4px"><b>Resposta escrita pelo modelo</b></p><p class="reply">${esc(res.saida.resposta)}</p>` : ""}
      <p class="block-note" style="margin-top:12px">${usd(res.custoUsd)} · ${secs(res.latenciaMs)}</p>`}`;
  $("case-detail").querySelectorAll("button[data-run]").forEach((btn) => btn.addEventListener("click", () => {
    selectedRunKey = btn.dataset.run;
    renderDetail();
    $("case-detail").querySelector(`button[data-run="${selectedRunKey}"]`)?.focus();
  }));
}

/* Juiz */
function renderJudge() {
  const cal = data.calibracao;
  if (!cal) {
    $("judge").innerHTML = '<p class="empty">A calibração do juiz ainda não foi executada. Rode o comando <code>judge-check</code>.</p>';
    return;
  }
  const yes = (v) => (v === true ? "Aprova" : v === false ? "Reprova" : "Sem veredito");
  $("judge").innerHTML = `
    <div class="mini-label">
      <h3>Calibração do juiz</h3>
      <p class="serving">Porção de <b>${cal.itens.length} respostas</b> com veredito humano</p>
      <table>
        <tbody>
          <tr class="major"><th scope="row">Concordância com a pessoa</th><td>${pct(cal.concordancia)}</td></tr>
          <tr><th scope="row">Respostas ruins reprovadas</th><td>${pct(cal.ruinsReprovadas)}</td></tr>
          <tr><th scope="row">Respostas boas aprovadas</th><td>${pct(cal.boasAprovadas)}</td></tr>
          <tr class="sep"><th scope="row">Custo da calibração</th><td>${usd(cal.custoUsd)}</td></tr>
        </tbody>
      </table>
    </div>
    <p class="block-note">${cal.concordancia === 1 ? "Concordância total em 16 itens mostra que o juiz reconhece defeitos claros; não prova que ele acerta em casos limítrofes. Por isso as diferenças pequenas entre versões no placar podem ser variação do próprio juiz." : "Os itens em que o juiz divergiu estão marcados na tabela."}</p>
    <div class="table-wrap">
      <table class="data judge">
        <thead><tr><th scope="col">Item</th><th scope="col">Chamado</th><th scope="col">Pessoa</th><th scope="col">Juiz</th><th scope="col">Notas (cobertura, tom, fidelidade)</th><th scope="col">Por que a resposta foi escrita assim</th></tr></thead>
        <tbody>${cal.itens.map((i) => `<tr class="${i.juiz === i.esperado ? "" : "miss"}">
          <td>${i.id}</td><td>${i.caso}</td><td>${yes(i.esperado)}</td><td>${yes(i.juiz)}${i.juiz === i.esperado ? "" : " (divergiu)"}</td>
          <td>${i.notas ? `${i.notas.cobertura}, ${i.notas.tom}, ${i.notas.fidelidade}` : esc(i.erro ?? "–")}</td>
          <td class="reason">${esc(i.motivo)}</td></tr>`).join("")}</tbody>
      </table>
    </div>`;
}

function truncate(s, n) {
  s = String(s ?? "");
  return s.length > n ? s.slice(0, n - 1).trimEnd() + "…" : s;
}
