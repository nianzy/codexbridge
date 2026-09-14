const MAX_TEXT_LENGTH = 250_000;

chrome.runtime.onMessage.addListener((message, _sender, sendResponse) => {
  if (message?.type !== "CODEX_BRIDGE_READ_RENDERED_TURNS") return false;
  sendResponse(readRenderedConversation());
  return true;
});

function readRenderedConversation() {
  const nodes = [...document.querySelectorAll("[data-message-author-role]")];
  const messages = nodes.map((node, index) => {
    const role = node.getAttribute("data-message-author-role");
    const owner = node.closest("[data-message-id], [data-testid^='conversation-turn']");
    const domID = owner?.getAttribute("data-message-id") || owner?.id;
    const text = normalizeText(node.innerText || node.textContent || "").slice(0, MAX_TEXT_LENGTH);
    return {
      role,
      value: {
        id: domID || `message-${role || "unknown"}-${index}`,
        idSource: domID ? "dom" : "dom-order",
        text
      }
    };
  }).filter((message) => ["user", "assistant"].includes(message.role) && message.value.text);

  const turns = [];
  for (const message of messages) {
    if (message.role === "user") {
      turns.push({
        id: `turn-${turns.length}-${message.value.id}`,
        index: turns.length,
        user: message.value,
        assistant: null,
        complete: false
      });
    } else if (turns.length && !turns.at(-1).assistant) {
      turns.at(-1).assistant = message.value;
      turns.at(-1).complete = true;
    }
  }

  const url = new URL(window.location.href);
  const pathParts = url.pathname.split("/").filter(Boolean);
  const conversationId = pathParts[0] === "c" ? pathParts[1] || null : null;
  const attachmentCount = document.querySelectorAll("a[href*='/backend-api/files/'], [data-testid*='attachment']").length;
  const warnings = [];
  if (messages.some((message) => message.value.idSource === "dom-order")) {
    warnings.push("部分消息没有稳定 DOM ID，刷新页面后可能需要重新选择。")
  }
  if (turns.some((turn) => !turn.complete)) {
    warnings.push("最后一轮可能仍在生成，只会按当前已渲染内容捕获。")
  }

  return {
    schemaVersion: 1,
    source: {
      kind: "chatgpt-web",
      url: url.href,
      conversationId,
      title: normalizeTitle(document.title)
    },
    capture: {
      capturedAt: new Date().toISOString(),
      scope: "rendered-current-page",
      attachmentCount,
      complete: turns.every((turn) => turn.complete)
    },
    selection: { selectedTurnIds: turns.map((turn) => turn.id) },
    turns,
    warnings
  };
}

function normalizeText(value) {
  return value.replace(/\u00a0/g, " ").replace(/[ \t]+\n/g, "\n").replace(/\n{3,}/g, "\n\n").trim();
}

function normalizeTitle(value) {
  return value.replace(/\s*[|·-]\s*ChatGPT\s*$/i, "").trim() || "ChatGPT 对话";
}
