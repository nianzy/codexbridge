let payload = null;
let selected = new Set();

const elements = Object.fromEntries([...document.querySelectorAll("[id]")].map((element) => [element.id, element]));
elements.retry.addEventListener("click", load);
elements["toggle-all"].addEventListener("click", toggleAll);
elements.send.addEventListener("click", send);

load();

async function load() {
  show("loading");
  const response = await chrome.runtime.sendMessage({ type: "CODEX_BRIDGE_CAPTURE_CURRENT" });
  if (!response?.ok || !response.payload?.turns?.length) {
    elements["error-text"].textContent = response?.error || "当前页面没有可捕获的完整文字轮次。";
    show("error");
    return;
  }
  payload = response.payload;
  selected = new Set(payload.turns.map((turn) => turn.id));
  elements.title.textContent = payload.source.title;
  elements.summary.textContent = `${payload.turns.length} 轮已渲染讨论 · ${payload.capture.attachmentCount} 个附件不传输`;
  const warnings = payload.warnings || [];
  elements.warning.textContent = warnings.join(" ");
  elements.warning.classList.toggle("hidden", !warnings.length);
  renderTurns();
  show("capture");
  elements.footer.classList.remove("hidden");
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
