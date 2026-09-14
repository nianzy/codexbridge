const CHATGPT_ORIGIN = "https://chatgpt.com";

chrome.runtime.onInstalled.addListener(() => {
  chrome.sidePanel.setPanelBehavior({ openPanelOnActionClick: true });
});

chrome.runtime.onMessage.addListener((message, _sender, sendResponse) => {
  if (message?.type === "CODEX_BRIDGE_CAPTURE_CURRENT") {
    captureCurrentTab().then(sendResponse);
    return true;
  }
  if (message?.type === "CODEX_BRIDGE_SEND_NATIVE") {
    sendToNativeHost(message.payload).then(sendResponse);
    return true;
  }
  return false;
});

async function captureCurrentTab() {
  const [tab] = await chrome.tabs.query({ active: true, currentWindow: true });
  if (!tab?.id || !tab.url?.startsWith(CHATGPT_ORIGIN)) {
    return { ok: false, error: "请先打开一个 chatgpt.com 会话。" };
  }
  try {
    const payload = await chrome.tabs.sendMessage(tab.id, { type: "CODEX_BRIDGE_READ_RENDERED_TURNS" });
    return { ok: true, payload };
  } catch (_error) {
    return { ok: false, error: "当前页面尚未准备好，请刷新 ChatGPT 后重试。" };
  }
}

async function sendToNativeHost(payload) {
  try {
    const response = await chrome.runtime.sendNativeMessage("app.codexbridge.nativehost", {
      type: "capture.import",
      payload
    });
    return response?.ok ? response : { ok: false, error: response?.error || "Codex Bridge 没有保存这段对话。" };
  } catch (_error) {
    return { ok: false, error: "无法连接 Codex Bridge。请打开 Codex Bridge，在“来源与权限”中重新检查浏览器连接。" };
  }
}
