"use strict";
const $ = (selector, root = document) => root.querySelector(selector);
const state = {
  user: null,
  csrf: "",
  categories: [],
  items: [],
  filter: "all",
  authMode: "login",
  returnTo: null,
};
const labels = {
  lost: "Perdido",
  found: "Encontrado",
  open: "Aberto",
  reserved: "Devolução combinada",
  returned: "Devolvido",
  cancelled: "Encerrado",
  pending: "Aguardando análise",
  approved: "Aprovada",
  rejected: "Recusada",
  delivered: "Entregue",
};
const modal = $("#modal");
const content = $("#modal-content");
const escapeHtml = (value) =>
  String(value ?? "").replace(
    /[&<>"']/g,
    (c) =>
      ({ "&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;", "'": "&#39;" })[
        c
      ],
  );
const dateLabel = (date) =>
  new Intl.DateTimeFormat("pt-BR", { day: "2-digit", month: "short" }).format(
    new Date(date + "T12:00:00"),
  );
const today = () => {
  const d = new Date();
  return `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, "0")}-${String(d.getDate()).padStart(2, "0")}`;
};
let toastTimer;
function toast(message) {
  const node = $("#toast");
  node.textContent = message;
  node.hidden = false;
  clearTimeout(toastTimer);
  toastTimer = setTimeout(() => {
    node.hidden = true;
  }, 5500);
}
async function api(path, options = {}) {
  const response = await fetch("/api" + path, {
    credentials: "same-origin",
    ...options,
    headers: {
      "Content-Type": "application/json",
      "X-CSRF-TOKEN": state.csrf,
      ...options.headers,
    },
  });
  const body = await response.json().catch(() => null);
  if (!response.ok)
    throw new Error(
      body?.error ||
        {
          401: "Entre na sua conta para continuar.",
          403: "Você não tem acesso a esta ação.",
          429: "Muitas tentativas. Aguarde um minuto e tente novamente.",
        }[response.status] ||
        "Não foi possível concluir. Tente novamente.",
    );
  return body;
}
async function session() {
  const data = await api("/session");
  state.user = data.user;
  state.csrf = data.csrfToken;
  state.categories = data.categories;
  $("#account").textContent = state.user
    ? `Olá, ${state.user.name.split(" ")[0]}`
    : "Entrar / cadastrar";
}
async function reload() {
  state.items = await api("/items");
  renderCards();
}
function show(html) {
  content.innerHTML = html;
  if (!modal.open) modal.showModal();
  else modal.scrollTop = 0;
}
$("#close-modal").addEventListener("click", () => modal.close());
modal.addEventListener("close", () => {
  state.returnTo = null;
});
function wireForm(handler) {
  const form = $("form", content);
  form.addEventListener("submit", async (event) => {
    event.preventDefault();
    const button = $('button[type="submit"]', form);
    const error = $(".form-error", form);
    button.disabled = true;
    error.textContent = "";
    try {
      await handler(Object.fromEntries(new FormData(form)));
    } catch (ex) {
      error.textContent = ex.message;
    } finally {
      button.disabled = false;
    }
  });
}
function requireUser(callback) {
  if (state.user) callback();
  else {
    state.returnTo = callback;
    auth();
  }
}
function auth(mode = "login") {
  state.authMode = mode;
  const register = mode === "register";
  show(
    `<h2 id="modal-title">${register ? "Uma nova história por aqui." : "Bem-vindo de volta."}</h2><p class="subtle">Seu arquivo de objetos, encontros e boas notícias.</p><form>${register ? '<label>Como podemos chamar você?<input name="name" autocomplete="name" required minlength="2" maxlength="60"></label>' : ""}<label>E-mail<input name="email" type="email" autocomplete="email" required maxlength="150"></label><label>Senha<input name="password" type="password" autocomplete="${register ? "new-password" : "current-password"}" required minlength="${register ? 10 : 1}" maxlength="128"></label>${register ? '<p class="field-note">Use pelo menos 10 caracteres. Seu e-mail não aparece no mural.</p>' : ""}<p class="form-error" role="alert"></p><button class="button" type="submit">${register ? "Criar meu arquivo" : "Entrar no meu arquivo"} <span aria-hidden="true">→</span></button></form><button class="text-button form-switch" id="switch-auth">${register ? "Já tenho uma conta" : "Ainda não tenho conta. Quero me cadastrar."}</button>`,
  );
  $("#switch-auth").addEventListener("click", () =>
    auth(register ? "login" : "register"),
  );
  wireForm(async (data) => {
    await api(register ? "/register" : "/login", {
      method: "POST",
      body: JSON.stringify(data),
    });
    await session();
    const next = state.returnTo;
    state.returnTo = null;
    modal.close();
    toast(
      register
        ? "Seu arquivo está aberto. Seja bem-vindo!"
        : "Que bom ter você de volta.",
    );
    if (next) next();
  });
}
$("#account").addEventListener("click", () =>
  state.user ? personal() : auth(),
);
$("#personal").addEventListener("click", () => requireUser(personal));
document
  .querySelectorAll("[data-create]")
  .forEach((button) =>
    button.addEventListener("click", () =>
      requireUser(() => create(button.dataset.create)),
    ),
  );
function create(kind) {
  const found = kind === "found";
  show(
    `<h2 id="modal-title">${found ? "Encontrou uma história?" : "Vamos procurar juntos."}</h2><p class="subtle">${found ? "Registre o objeto e guarde um detalhe para reconhecer o dono." : "Conte o que você perdeu. O arquivo pode ajudar a reencontrar."}</p><form><label>Um título para este registro<input name="title" required minlength="3" maxlength="90" placeholder="Ex.: Livro de capa verde"></label><div class="form-row"><label>Categoria<select name="category" required>${state.categories.map((c) => `<option>${escapeHtml(c)}</option>`).join("")}</select></label><label>Quando aconteceu?<input type="date" name="date" required max="${today()}" value="${today()}"></label></div><label>Onde?<input name="location" required minlength="3" maxlength="100" placeholder="Ex.: Biblioteca central"></label><label>Descrição pública<textarea name="description" required minlength="10" maxlength="1200" placeholder="Cor, formato e contexto. Evite dados pessoais e detalhes secretos."></textarea></label>${found ? '<label>Característica oculta<textarea name="secret" required minlength="5" maxlength="300" placeholder="Ex.: O que está escrito na dedicatória?"></textarea><span class="field-note">Só você pode consultar este detalhe. Compare-o com a descrição enviada por quem solicitar o objeto.</span></label>' : '<p class="notice">Guarde alguns detalhes para você: eles podem ajudar a comprovar que o objeto é seu.</p>'}<p class="form-error" role="alert"></p><button type="submit" class="button">Guardar no mural <span aria-hidden="true">↗</span></button></form>`,
  );
  wireForm(async (data) => {
    const item = await api("/items", {
      method: "POST",
      body: JSON.stringify({ ...data, kind, secret: data.secret || "" }),
    });
    await reload();
    toast("Registro guardado no mural.");
    await matches(item.id);
  });
}
const icons = {
  Livros:
    '<path d="m13 24 39 10 35-14v48L52 83 13 72Z"/><path d="M52 34v49M20 30l27 8v36l-27-7ZM59 39l21-9M59 46l21-9M59 53l21-9M59 60l21-9M59 67l15-6"/><path d="m21 24 31 9 28-11-30-9Z"/>',
  Eletrônicos:
    '<rect x="29" y="8" width="44" height="76" rx="6"/><path d="M34 20h34v49H34zM45 14h13"/><circle cx="51" cy="77" r="2"/>',
  Documentos:
    '<rect x="17" y="20" width="70" height="48" rx="4"/><circle cx="36" cy="39" r="7"/><path d="M24 57c1-13 24-13 25 0M58 33h20M58 41h20M58 49h13M25 76h53"/>',
  Acessórios:
    '<circle cx="37" cy="28" r="16"/><circle cx="37" cy="28" r="7"/><path d="m45 42 26 31 9-8-4-5-6 5-4-5 6-5-5-6-6 5-8-13M25 14C10 5 13 40 24 36"/>',
  Roupas:
    '<path d="m35 14-23 16 12 20 10-6v39h38V44l10 6 12-20-23-16c-6 15-29 15-36 0Z"/><path d="M34 70h38M41 17c1 9 21 9 24 0"/>',
  Outros:
    '<path d="m17 28 34-15 35 15v42L51 86 17 70ZM17 28l34 16 35-16M51 44v42M34 21l35 15v17"/>',
};
function renderCards() {
  const search = $("#search").value.trim().toLocaleLowerCase("pt-BR");
  const category = $("#category").value;
  const includeClosed = $("#closed").checked;
  const items = state.items.filter(
    (item) =>
      (state.filter === "all" || item.kind === state.filter) &&
      (!category || item.category === category) &&
      (includeClosed || item.status === "open") &&
      `${item.title} ${item.description} ${item.location}`
        .toLocaleLowerCase("pt-BR")
        .includes(search),
  );
  $("#record-count").textContent =
    `${items.length} ${items.length === 1 ? "registro no arquivo" : "registros no arquivo"}`;
  $("#cards").innerHTML = items.length
    ? items
        .map(
          (item) =>
            `<article class="item-card"><div class="card-visual"><span class="badge ${item.kind}">${labels[item.kind].toUpperCase()}</span><svg viewBox="0 0 104 96" aria-hidden="true">${icons[item.category] || icons.Outros}</svg><span class="catalog-number">REG. ${String(item.id).padStart(4, "0")}</span></div><div class="card-body"><span class="card-category">${escapeHtml(item.category)}${item.status !== "open" ? ` · ${labels[item.status]}` : ""}</span><h3>${escapeHtml(item.title)}</h3><p>${escapeHtml(item.description.length > 115 ? item.description.slice(0, 115) + "…" : item.description)}</p><div class="card-meta"><span>${escapeHtml(item.location)}</span><span>${dateLabel(item.date)}</span></div><button class="card-link" data-item="${item.id}" aria-label="Ver detalhes: ${escapeHtml(item.title)}">Ler esta história <span aria-hidden="true">↗</span></button></div></article>`,
        )
        .join("")
    : '<p class="empty">Nenhuma ficha por aqui ainda.<br><small>Tente outra busca ou registre uma nova história.</small></p>';
  document
    .querySelectorAll("[data-item]")
    .forEach((button) =>
      button.addEventListener("click", () =>
        detail(Number(button.dataset.item)),
      ),
    );
}
document.querySelectorAll("[data-filter]").forEach((button) =>
  button.addEventListener("click", () => {
    state.filter = button.dataset.filter;
    document.querySelectorAll("[data-filter]").forEach((b) => {
      const active = b === button;
      b.classList.toggle("active", active);
      b.setAttribute("aria-pressed", active);
    });
    renderCards();
  }),
);
["search", "category", "closed"].forEach((id) =>
  $("#" + id).addEventListener(
    id === "search" ? "input" : "change",
    renderCards,
  ),
);
function detail(id) {
  const item = state.items.find((i) => i.id === id);
  if (!item) return;
  const own = state.user?.id === item.ownerId;
  show(
    `<span class="badge ${item.kind}">${labels[item.kind]}</span><h2 id="modal-title">${escapeHtml(item.title)}</h2><div class="detail-grid"><div><small>Local</small>${escapeHtml(item.location)}</div><div><small>Data</small>${dateLabel(item.date)}</div><div><small>Categoria</small>${escapeHtml(item.category)}</div><div><small>Situação</small>${labels[item.status]}</div></div><p class="detail-description">${escapeHtml(item.description)}</p><div id="detail-extra"></div><div class="dialog-actions">${own ? `<button class="button" id="find-matches">Buscar correspondências</button>${item.kind === "found" ? '<button class="button outline" id="see-secret">Meu detalhe oculto</button>' : ""}${item.status === "open" ? '<button class="text-button" id="cancel-item">Encerrar registro</button>' : ""}` : item.kind === "found" && item.status === "open" ? '<button class="button" id="claim-item">Acho que é meu <span aria-hidden="true">→</span></button>' : '<p class="subtle">Encontrou algo parecido? Registre o objeto pelo início da página.</p>'}</div><p class="form-error" id="detail-error" role="alert"></p>`,
  );
  $("#claim-item")?.addEventListener("click", () =>
    requireUser(() => claimForm(item)),
  );
  $("#find-matches")?.addEventListener("click", () => matches(id));
  $("#see-secret")?.addEventListener("click", async () => {
    try {
      const data = await api(`/items/${id}/secret`);
      $("#detail-extra").innerHTML =
        `<p class="notice">${escapeHtml(data.secret)}</p>`;
    } catch (ex) {
      $("#detail-error").textContent = ex.message;
    }
  });
  $("#cancel-item")?.addEventListener("click", () =>
    confirmAction(
      "Encerrar este registro?",
      "As solicitações pendentes serão recusadas. O registro continuará no histórico.",
      async () => {
        await api(`/items/${id}/cancel`, { method: "POST" });
        await reload();
        modal.close();
        toast("Registro encerrado.");
      },
    ),
  );
}
function claimForm(item) {
  show(
    `<h2 id="modal-title">Esta história é sua?</h2><p>${escapeHtml(item.title)}</p><p class="notice">Descreva um detalhe que não aparece no mural: uma marca, dedicatória ou característica única. Quem encontrou vai conferir sua descrição. Não envie documentos ou outros dados sensíveis.</p><form><label>Sua comprovação<textarea name="proof" required minlength="10" maxlength="600" placeholder="Um detalhe que só você saberia…"></textarea></label><p class="form-error" role="alert"></p><button class="button" type="submit">Enviar para conferência</button></form>`,
  );
  wireForm(async (data) => {
    await api(`/items/${item.id}/claims`, {
      method: "POST",
      body: JSON.stringify(data),
    });
    toast("Comprovação enviada. Acompanhe no seu arquivo.");
    await personal();
  });
}
async function matches(id) {
  try {
    const rows = await api(`/items/${id}/matches`);
    show(
      `<h2 id="modal-title">Pistas para um reencontro.</h2><p class="subtle">Sugestões por categoria, data, local e descrição. A pontuação indica semelhança, não comprova propriedade.</p>${rows.length ? rows.map((m) => `<div class="archive-entry"><span class="match-score">${m.score}/100 pontos de semelhança</span><p><strong>${escapeHtml(m.item.title)}</strong></p><p class="match-reasons">${m.reasons.map(escapeHtml).join(" · ")}</p><button class="text-button" data-match="${m.item.id}">Ver registro →</button></div>`).join("") : '<p class="empty">Ainda não apareceu uma boa pista.<br><small>Volte quando novas fichas chegarem ao mural.</small></p>'}<button class="button outline" id="matches-back">Meu arquivo</button>`,
    );
    document
      .querySelectorAll("[data-match]")
      .forEach((b) =>
        b.addEventListener("click", () => detail(Number(b.dataset.match))),
      );
    $("#matches-back").addEventListener("click", personal);
  } catch (ex) {
    toast(ex.message);
  }
}
async function personal() {
  try {
    await reload();
    const claims = await api("/claims");
    const own = state.items.filter((i) => i.ownerId === state.user.id);
    const received = claims.filter((c) => c.claimantId !== state.user.id);
    const sent = claims.filter((c) => c.claimantId === state.user.id);
    const claimRow = (c, incoming) =>
      `<div class="archive-entry"><strong>${escapeHtml(c.title)}</strong><span class="status">${labels[c.status]}</span><p>${incoming ? `De ${escapeHtml(c.claimantName)} · ` : `Com ${escapeHtml(c.ownerName)} · `}${escapeHtml(c.proof)}</p>${incoming && ["pending", "approved"].includes(c.status) ? `<button class="button outline" data-claim-item="${c.itemId}">Conferir detalhe oculto</button><button class="button" data-action="${c.status === "pending" ? "approve" : "deliver"}" data-claim="${c.id}">${c.status === "pending" ? "Aprovar comprovação" : "Confirmar entrega"}</button><button class="text-button" data-action="reject" data-claim="${c.id}">Recusar</button>` : ""}${c.status === "approved" ? '<p class="notice">Combine a retirada pessoalmente em um local público do campus. Quem encontrou deve confirmar a entrega depois da devolução.</p>' : ""}</div>`;
    show(
      `<h2 id="modal-title">O arquivo de ${escapeHtml(state.user.name)}.</h2><p class="subtle">Suas fichas, suas pistas, seus reencontros.</p><section class="archive-section"><h3>Meus registros <small>(${own.length})</small></h3>${own.length ? own.map((i) => `<div class="archive-entry"><strong>${escapeHtml(i.title)}</strong><span class="status">${labels[i.status]}</span><br><button class="text-button" data-own="${i.id}">Abrir ficha →</button></div>`).join("") : '<p class="subtle">Seu arquivo ainda está em branco.</p>'}</section><section class="archive-section"><h3>Solicitações recebidas</h3>${received.length ? received.map((c) => claimRow(c, true)).join("") : '<p class="subtle">Nenhuma solicitação recebida.</p>'}</section><section class="archive-section"><h3>Minhas solicitações</h3>${sent.length ? sent.map((c) => claimRow(c, false)).join("") : '<p class="subtle">Nenhuma solicitação enviada.</p>'}</section><button class="text-button" id="logout">Sair da minha conta</button>`,
    );
    document
      .querySelectorAll("[data-own]")
      .forEach((b) =>
        b.addEventListener("click", () => detail(Number(b.dataset.own))),
      );
    document
      .querySelectorAll("[data-claim-item]")
      .forEach((b) =>
        b.addEventListener("click", () => detail(Number(b.dataset.claimItem))),
      );
    document.querySelectorAll("[data-action]").forEach((b) =>
      b.addEventListener("click", () => {
        const action = b.dataset.action;
        const messages = {
          approve: [
            "Aprovar esta comprovação?",
            "Confira primeiro o detalhe oculto. As outras solicitações pendentes serão recusadas e o objeto ficará reservado.",
          ],
          deliver: [
            "O objeto já foi entregue?",
            "Confirme apenas depois da devolução presencial. Esta etapa encerra a história.",
          ],
          reject: [
            "Recusar esta solicitação?",
            "Se ela já estava aprovada, o objeto voltará a ficar disponível no mural.",
          ],
        };
        confirmAction(...messages[action], async () => {
          await api(`/claims/${b.dataset.claim}/${action}`, { method: "POST" });
          await reload();
          await personal();
          toast("Solicitação atualizada.");
        });
      }),
    );
    $("#logout").addEventListener("click", async () => {
      try {
        await api("/logout", { method: "POST" });
        await session();
        modal.close();
        toast("Até o próximo reencontro.");
      } catch (ex) {
        toast(ex.message);
      }
    });
  } catch (ex) {
    toast(ex.message);
  }
}
function confirmAction(title, message, action) {
  show(
    `<h2 id="modal-title">${escapeHtml(title)}</h2><p>${escapeHtml(message)}</p><form><p class="form-error" role="alert"></p><div class="dialog-actions"><button class="button" type="submit">Confirmar</button><button class="button outline" type="button" id="back-personal">Voltar ao meu arquivo</button></div></form>`,
  );
  $("#back-personal").addEventListener("click", personal);
  wireForm(action);
}
(async () => {
  try {
    await session();
    state.categories.forEach((c) => {
      const option = document.createElement("option");
      option.value = c;
      option.textContent = c;
      $("#category").append(option);
    });
    await reload();
  } catch (ex) {
    $("#cards").innerHTML =
      '<p class="empty">O arquivo não abriu desta vez.<br><small>Confira sua conexão e recarregue a página.</small></p>';
    toast(ex.message);
  }
})();
