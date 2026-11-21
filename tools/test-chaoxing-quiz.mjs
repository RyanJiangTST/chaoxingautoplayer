const port = Number(process.env.CXAP_DEBUG_PORT || 9223);
const quizCase = process.env.CXAP_QUIZ_CASE || "correct";
const targets = await fetch(`http://127.0.0.1:${port}/json/list`).then(response => response.json());
const page = targets.find(target => target.type === "page");
if (!page?.webSocketDebuggerUrl) {
  throw new Error("No debuggable WebView2 page target found.");
}

const socket = new WebSocket(page.webSocketDebuggerUrl);
await new Promise((resolve, reject) => {
  socket.addEventListener("open", resolve, { once: true });
  socket.addEventListener("error", reject, { once: true });
});

let nextId = 1;
const pending = new Map();
const contexts = [];
socket.addEventListener("message", event => {
  const message = JSON.parse(event.data);
  if (message.method === "Runtime.executionContextCreated") {
    contexts.push(message.params.context);
    return;
  }
  if (!message.id || !pending.has(message.id)) return;
  const entry = pending.get(message.id);
  pending.delete(message.id);
  message.error ? entry.reject(new Error(message.error.message)) : entry.resolve(message.result);
});

const call = (method, params = {}) => new Promise((resolve, reject) => {
  const id = nextId++;
  pending.set(id, { resolve, reject });
  socket.send(JSON.stringify({ id, method, params }));
});

await call("Page.enable");
await call("Runtime.enable");
await new Promise(resolve => setTimeout(resolve, 250));
const tree = await call("Page.getFrameTree");
const frames = [];
const walk = node => {
  if (!node?.frame) return;
  frames.push(node.frame);
  for (const child of node.childFrames || []) walk(child);
};
walk(tree.frameTree);
const frame = frames.find(item => item.url.includes("/ananas/modules/video/index.html"));
if (!frame) {
  throw new Error("Video module frame was not found. Open and start a course video first.");
}

const defaultContext = contexts.find(context =>
  context.auxData?.frameId === frame.id && context.auxData?.isDefault === true);
const executionContextId = defaultContext?.id
  ?? (await call("Page.createIsolatedWorld", {
    frameId: frame.id,
    worldName: "chaoxing-quiz-test",
    grantUniveralAccess: true
  })).executionContextId;

const inject = `(() => {
  document.querySelector("#cxap-fake-quiz")?.remove();
  window.__cxapFakeQuizSubmitted = false;
  window.__cxapFakeQuizSelected = [];
  window.__cxapFakeQuizSubmissions = [];
  window.__cxapFakeQuizContinued = false;
  const panel = document.createElement("div");
  panel.id = "cxap-fake-quiz";
  panel.className = "ans-videoquiz";
  panel.style.cssText = "position:fixed;left:30%;top:20%;width:520px;height:330px;background:white;color:#111;z-index:2147483647;border:4px solid #2563eb;padding:20px";
  panel.innerHTML = [
    "<h3 class='tkTopic_title'>测试单选互动题</h3>",
    "<ul>",
    "<li class='ans-videoquiz-opt'><label><input name='ans-videoquiz-opt' type='radio' value='${quizCase === "correct" ? "true" : "false"}'> A. 首选选项</label></li>",
    "<li class='ans-videoquiz-opt'><label><input name='ans-videoquiz-opt' type='radio' value='false'> B. 错误选项</label></li>",
    "</ul>",
    "<a class='ans-videoquiz-submit' id='videoquiz-submit' href='javascript:;'>提交</a> ",
    "<a class='ans-videoquiz-back' id='knowledgeBack' href='javascript:;' style='display:none'>回答错误</a> ",
    "<a class='ans-videoquiz-continue' id='videoquiz-continue' href='javascript:;' style='display:none'>继续学习</a>"
  ].join("");
  document.body.appendChild(panel);
  const submit = panel.querySelector("#videoquiz-submit");
  const back = panel.querySelector("#knowledgeBack");
  const continueButton = panel.querySelector("#videoquiz-continue");
  submit.addEventListener("click", () => {
    const selected = Array.from(panel.querySelectorAll("input")).map(input => ({
      value: input.value,
      checked: input.checked
    }));
    window.__cxapFakeQuizSubmissions.push(selected);
    window.__cxapFakeQuizSelected = selected;
    window.__cxapFakeQuizSubmitted = true;
    if ("${quizCase}" === "fallback" && window.__cxapFakeQuizSubmissions.length === 1) {
      back.style.display = "inline";
      return;
    }
    back.style.display = "none";
    continueButton.style.display = "inline";
  });
  continueButton.addEventListener("click", () => {
    window.__cxapFakeQuizContinued = true;
    panel.remove();
  });
  return true;
})()`;

await call("Runtime.evaluate", {
  expression: inject,
  contextId: executionContextId,
  returnByValue: true
});
await call("Runtime.evaluate", {
  expression: "window.__cxapDetectQuiz?.()",
  contextId: executionContextId,
  returnByValue: true
});

await new Promise(resolve => setTimeout(resolve, 8000));
const result = await call("Runtime.evaluate", {
  expression: `(() => {
    const panel = document.querySelector("#cxap-fake-quiz");
    const style = panel ? getComputedStyle(panel) : null;
    return {
      submitted: window.__cxapFakeQuizSubmitted,
      selected: window.__cxapFakeQuizSelected,
      submissions: window.__cxapFakeQuizSubmissions,
      continued: window.__cxapFakeQuizContinued,
      panelExists: Boolean(panel),
      panelRects: panel?.getClientRects().length ?? 0,
      panelDisplay: style?.display ?? "",
      panelVisibility: style?.visibility ?? "",
      panelOpacity: style?.opacity ?? "",
      panelInstanceofElement: panel instanceof Element,
      quizRoots: document.querySelectorAll(".ans-videoquiz").length,
      quizOptions: document.querySelectorAll("li.ans-videoquiz-opt").length,
      bridge: window.__cxapProbe?.(),
      quizProbe: window.__cxapQuizProbe?.()
    };
  })()`,
  contextId: executionContextId,
  returnByValue: true
});

console.log(JSON.stringify(result.result.value, null, 2));
socket.close();
