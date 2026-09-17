let payload = null;
let selected = new Set();
let capturedTabId = null;
let reloadTimer = null;
let loadSequence = 0;

const elements = Object.fromEntries([...document.querySelectorAll("[id]")].map((element) => [element.id, element]));
elements.retry.addEventListener("click", () => load({ reason: "manual" }));
elements.refresh.addEventListener("click", () => load({ reason: "manual" }));
elements["toggle-all"].addEventListener("click", toggleAll);
elements.send.addEventListener("click", send);

chrome.runtime.onMessage.addListener((message) => {
  if (message?.type !== "CODEX_BRIDGE_PAGE_CHANGED") return;
  scheduleReload({ tabId: message.tabId, url: message.url, reason: "navigation" });
});

chrome.tabs.onActivated.addListener(({ tabId }) => {
  scheduleReload({ tabId, reason: "tab-activated" });
});

chrome.tabs.onUpdated.addListener((tabId, changeInfo, tab) => {
  if (!tab.active || (!changeInfo.url && changeInfo.status !== "complete")) return;
  scheduleReload({ tabId, url: changeInfo.url || tab.url, reason: "tab-updated" });
});

load({ reason: "initial" });

async function load({ reason = "manual", expectedTabId = null, expectedUrl = null } = {}) {
  const sequence = ++loadSequence;
  const previousPayload = payload;
  const previousSelection = new Set(selected);
  show("loading");
  elements.refresh.disabled = true;

  let response = null;
  let captureIsFresh = false;
  const attempts = reason === "initial" ? 1 : 5;
  for (let attempt = 0; attempt < attempts; attempt += 1) {
    response = await chrome.runtime.sendMessage({ type: "CODEX_BRIDGE_CAPTURE_CURRENT" });
    if (sequence !== loadSequence) return;
    captureIsFresh = isFreshCapture(response, previousPayload, expectedTabId, expectedUrl);
    if (captureIsFresh) break;
    if (response?.error?.startsWith("请先打开")) break;
    if (attempt < attempts - 1) await wait(350 + attempt * 150);
  }

  if (sequence !== loadSequence) return;
  elements.refresh.disabled = false;
  if (!captureIsFresh) {
    elements["error-text"].textContent = response?.error || "当前页面没有可捕获的完整文字轮次。";
    show("error");
    return;
  }

  const nextPayload = response.payload;
  selected = selectionForReload(previousPayload, nextPayload, previousSelection);
  payload = nextPayload;
  capturedTabId = response.tabId ?? expectedTabId;
  elements.title.textContent = payload.source.title;
  elements.summary.textContent = `${payload.turns.length} 轮已渲染讨论 · ${payload.capture.attachmentCount} 个附件不传输`;
  const warnings = payload.warnings || [];
  elements.warning.textContent = warnings.join(" ");
  elements.warning.classList.toggle("hidden", !warnings.length);
  renderTurns();
  show("capture");
  elements.footer.classList.remove("hidden");
}

function scheduleReload({ tabId, url = null, reason }) {
  clearTimeout(reloadTimer);
  reloadTimer = setTimeout(async () => {
    const [activeTab] = await chrome.tabs.query({ active: true, currentWindow: true });
    if (!activeTab?.id || activeTab.id !== tabId) return;
    if (tabId === capturedTabId && url && payload?.source?.url === url && reason !== "tab-updated") return;
    load({ reason, expectedTabId: tabId, expectedUrl: url || activeTab.url });
  }, reason === "navigation" ? 500 : 180);
}

function isFreshCapture(response, previousPayload, expectedTabId, expectedUrl) {
  if (!response?.ok || !response.payload?.turns?.length) return false;
  if (expectedTabId != null && response.tabId !== expectedTabId) return false;
  if (expectedUrl && conversationID(expectedUrl) !== conversationID(response.payload.source?.url)) return false;

  const changedConversation = previousPayload
    && conversationID(previousPayload.source?.url) !== conversationID(response.payload.source?.url);
  if (changedConversation && turnSignature(previousPayload) === turnSignature(response.payload)) return false;
  return true;
}

function selectionForReload(previousPayload, nextPayload, previousSelection) {
  if (!previousPayload || conversationID(previousPayload.source?.url) !== conversationID(nextPayload.source?.url)) {
    return new Set(nextPayload.turns.map((turn) => turn.id));
  }
  const previousTurnIDs = new Set(previousPayload.turns.map((turn) => turn.id));
  return new Set(nextPayload.turns
    .filter((turn) => previousSelection.has(turn.id) || !previousTurnIDs.has(turn.id))
    .map((turn) => turn.id));
}

function conversationID(value) {
  try {
    const parts = new URL(value).pathname.split("/").filter(Boolean);
    const conversationIndex = parts.lastIndexOf("c");
    return conversationIndex >= 0 ? parts[conversationIndex + 1] || "" : "";
  } catch (_error) {
    return "";
  }
}

function turnSignature(value) {
  return value?.turns?.map((turn) => `${turn.id}:${turn.user?.text || ""}:${turn.assistant?.text || ""}`).join("|") || "";
}

function wait(milliseconds) {
  return new Promise((resolve) => setTimeout(resolve, milliseconds));
}

function renderTurns() {
  elements.turns.replaceChildren(...payload.turns.map((turn) => {
    const button = document.createElement("button");
    button.className = `turn ${selected.has(turn.id) ? "selected" : ""}`;
    const head = document.createElement("div");
    head.className = "turn-head";
    head.innerHTML = `<span>第 ${turn.index + 1} 轮</span><span class="check">${selected.has(turn.id) ? "✓ 已选择" : "未选择"}</span>`;
    const question = document.createElement("p");
    question.textContent = turn.user.text;
    button.append(head, question);
    if (turn.assistant?.text) {
      const answer = document.createElement("p");
      answer.className = "answer";
      answer.textContent = turn.assistant.text;
      button.append(answer);
    }
    button.addEventListener("click", () => {
      selected.has(turn.id) ? selected.delete(turn.id) : selected.add(turn.id);
      renderTurns();
    });
    return button;
  }));
  elements["selected-count"].textContent = `${selected.size} 轮`;
  elements.send.disabled = selected.size === 0;
  elements["toggle-all"].textContent = selected.size === payload.turns.length ? "取消全选" : "全选";
}

function toggleAll() {
  selected = selected.size === payload.turns.length ? new Set() : new Set(payload.turns.map((turn) => turn.id));
  renderTurns();
}

async function send() {
  elements.send.disabled = true;
  elements.send.textContent = "正在保存…";
  const turns = payload.turns.filter((turn) => selected.has(turn.id));
  const outgoing = { ...payload, turns, selection: { selectedTurnIds: turns.map((turn) => turn.id) } };
  const response = await chrome.runtime.sendMessage({ type: "CODEX_BRIDGE_SEND_NATIVE", payload: outgoing });
  elements.send.textContent = response?.ok ? "已保存到 Codex Bridge ✓" : "重试";
  if (!response?.ok) {
    elements.warning.textContent = response?.error || "保存失败。请确认 Codex Bridge 已打开后重试。";
    elements.warning.classList.remove("hidden");
    elements.send.disabled = false;
  }
}

function show(id) {
  ["loading", "error", "capture"].forEach((key) => elements[key].classList.toggle("hidden", key !== id));
  if (id !== "capture") elements.footer.classList.add("hidden");
}
