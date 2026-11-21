const port = Number(process.env.CXAP_DEBUG_PORT || 9223);
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
socket.addEventListener("message", event => {
  const message = JSON.parse(event.data);
  if (!message.id || !pending.has(message.id)) {
    return;
  }
  const { resolve, reject } = pending.get(message.id);
  pending.delete(message.id);
  if (message.error) {
    reject(new Error(message.error.message));
  } else {
    resolve(message.result);
  }
});

const call = (method, params = {}) => new Promise((resolve, reject) => {
  const id = nextId++;
  pending.set(id, { resolve, reject });
  socket.send(JSON.stringify({ id, method, params }));
});

const expression = `(() => {
  const clean = value => String(value ?? "").replace(/\\s+/g, " ").trim().slice(0, 220);
  const describe = element => ({
    tag: element.tagName,
    id: element.id || "",
    className: typeof element.className === "string" ? element.className : "",
    chapterId: element.getAttribute("chapterid") || element.getAttribute("data-chapterid") || "",
    knowledgeId: element.getAttribute("knowledgeid") || element.getAttribute("data-knowledgeid") || "",
    objectId: element.getAttribute("objectid") || element.getAttribute("data-objectid") || "",
    text: clean(element.textContent)
  });
  const unique = (items, limit) => {
    const seen = new Set();
    const result = [];
    for (const element of items) {
      const item = describe(element);
      const key = [item.chapterId, item.knowledgeId, item.objectId, item.className, item.text].join("|");
      if (seen.has(key)) continue;
      seen.add(key);
      result.push(item);
      if (result.length >= limit) break;
    }
    return result;
  };
  return {
    url: location.href,
    title: document.title,
    hasFocus: document.hasFocus(),
    visibilityState: document.visibilityState,
    bodyLength: document.body?.innerText?.length || 0,
    bodyText: clean(document.body?.innerText),
    bodyHtml: String(document.body?.innerHTML || "").replace(/\\s+/g, " ").slice(0, 5000),
    iframes: Array.from(document.querySelectorAll("iframe")).map(frame => ({
      id: frame.id || "",
      className: typeof frame.className === "string" ? frame.className : "",
      src: clean(frame.getAttribute("src") || frame.src),
      title: clean(frame.getAttribute("title")),
      parentClassName: typeof frame.parentElement?.className === "string" ? frame.parentElement.className : "",
      parentHtml: String(frame.parentElement?.outerHTML || "").replace(/\\s+/g, " ").slice(0, 1500)
    })),
    scripts: Array.from(document.scripts).map(script => clean(script.src || script.getAttribute("src"))).filter(Boolean),
    dialogs: Array.from(document.querySelectorAll(
      "[role='dialog'], [class*='dialog'], [class*='modal'], [class*='question'], [class*='popup']"
    )).filter(element => {
      const style = getComputedStyle(element);
      return style.display !== "none" && style.visibility !== "hidden" && element.getClientRects().length > 0;
    }).slice(0, 20).map(element => ({
      tag: element.tagName,
      className: typeof element.className === "string" ? element.className : "",
      text: clean(element.textContent)
    })),
    buttons: Array.from(document.querySelectorAll("button, [role='button'], input[type='button'], input[type='submit']"))
      .filter(element => {
        const style = getComputedStyle(element);
        return style.display !== "none" && style.visibility !== "hidden" && element.getClientRects().length > 0;
      }).slice(0, 30).map(element => ({
        tag: element.tagName,
        className: typeof element.className === "string" ? element.className : "",
        value: clean(element.value),
        text: clean(element.textContent)
      })),
    questionNodes: Array.from(document.querySelectorAll(
      "[class*='question'], [id*='question'], [class*='quiz'], [id*='quiz'], "
      + "[class*='answer'], [class*='option'], [class*='interactive'], [class*='exercise'], "
      + "[class*='pop-test'], [class*='poptest'], [class*='exam']"
    )).slice(0, 80).map(element => {
      const style = getComputedStyle(element);
      return {
        tag: element.tagName,
        id: element.id || "",
        className: typeof element.className === "string" ? element.className : "",
        visible: style.display !== "none" && style.visibility !== "hidden" && element.getClientRects().length > 0,
        text: clean(element.textContent),
        html: String(element.outerHTML || "").replace(/\\s+/g, " ").slice(0, 1200)
      };
    }),
    chapters: unique(document.querySelectorAll("[chapterid], [data-chapterid], [knowledgeid], [class*='chapter']"), 80),
    jobs: unique(document.querySelectorAll(".ans-job-icon, [class*='job-icon'], [data-objectid], [data-taskid]"), 80),
    catalog: unique(document.querySelectorAll(".posCatalog_select, .chapter_item, .chapter_unit, [class*='catalog']"), 80),
    videos: Array.from(document.querySelectorAll("video")).map(video => ({
      src: clean(video.currentSrc || video.src),
      currentTime: video.currentTime,
      duration: video.duration,
      paused: video.paused,
      muted: video.muted,
      volume: video.volume
    }))
  };
})()`;

await call("Page.enable");
const tree = await call("Page.getFrameTree");
const frames = [];
const walk = node => {
  if (!node?.frame) return;
  frames.push(node.frame);
  for (const child of node.childFrames || []) walk(child);
};
walk(tree.frameTree);

const results = [];
for (const frame of frames) {
  try {
    const world = await call("Page.createIsolatedWorld", {
      frameId: frame.id,
      worldName: "chaoxing-inspector",
      grantUniveralAccess: true
    });
    const evaluated = await call("Runtime.evaluate", {
      expression,
      contextId: world.executionContextId,
      returnByValue: true,
      awaitPromise: true
    });
    results.push({ frameId: frame.id, url: frame.url, value: evaluated.result?.value });
  } catch (error) {
    results.push({ frameId: frame.id, url: frame.url, error: error.message });
  }
}

console.log(JSON.stringify(results, null, 2));
socket.close();
