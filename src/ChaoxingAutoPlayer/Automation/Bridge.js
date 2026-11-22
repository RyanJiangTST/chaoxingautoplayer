(() => {
  "use strict";

  if (window.__chaoxingAutoPlayerInstalled) {
    return;
  }

  window.__chaoxingAutoPlayerInstalled = true;
  const frameId = `frame-${Date.now()}-${Math.random().toString(36).slice(2)}`;
  const state = {
    running: false,
    muted: true,
    resumeAt: 0,
    taskMap: new Map(),
    lastMouseAt: 0,
    playRetryAt: 0,
    quizActive: false,
    quizSubmitting: false,
    quizSkipping: false,
    quizSubmittedAt: 0,
    quizSignature: "",
    quizOutcomeSignature: ""
  };

  const post = (type, payload = {}) => {
    try {
      window.chrome?.webview?.postMessage({
        source: "chaoxing-auto-player",
        type,
        frameId,
        ...payload
      });
    } catch {
      // The frame may be navigating away.
    }
  };

  const normalize = value => String(value ?? "").replace(/\s+/g, " ").trim();

  const visible = element => {
    if (!element || !(element instanceof Element)) {
      return false;
    }
    const style = getComputedStyle(element);
    return style.display !== "none"
      && style.visibility !== "hidden"
      && Number(style.opacity || 1) > 0
      && element.getClientRects().length > 0;
  };

  const hash = value => {
    let result = 2166136261;
    for (let index = 0; index < value.length; index += 1) {
      result ^= value.charCodeAt(index);
      result = Math.imul(result, 16777619);
    }
    return (result >>> 0).toString(16);
  };

  const attributeText = element => {
    if (!element) {
      return "";
    }
    return [
      element.id,
      element.className,
      element.getAttribute?.("title"),
      element.getAttribute?.("aria-label"),
      element.getAttribute?.("data-type"),
      element.getAttribute?.("data-type-name"),
      element.getAttribute?.("data-objectid"),
      element.getAttribute?.("data-taskid"),
      element.getAttribute?.("href"),
      element.getAttribute?.("onclick"),
      getComputedStyle(element).backgroundImage
    ].filter(Boolean).join(" ");
  };

  const kindFor = root => {
    if (/^cur\d+$/i.test(root.id || "") && !root.querySelector(".ans-job-icon")) {
      return "chapter";
    }
    const samples = [root, ...root.querySelectorAll("*")].slice(0, 30);
    const marker = samples.map(attributeText).join(" ");
    const text = `${marker} ${normalize(root.textContent)}`;
    if (root.tagName === "IFRAME") {
      const frameMarker = `${root.className} ${root.getAttribute("src") || root.src || ""}`;
      if (/insertvideo|ans-insertvideo|modules\/video/i.test(frameMarker)) {
        return "video";
      }
      if (/insertdoc|ans-insertdoc|modules\/pdf|modules\/doc/i.test(frameMarker)) {
        return "document";
      }
      return "other";
    }
    if (/video|视频|多媒体|mp4|m3u8/i.test(text)) {
      return "video";
    }
    if (/测验|考试|作业|答题|quiz|exam/i.test(text)) {
      return "quiz";
    }
    if (/文档|阅读|资料|ppt|pdf|document|book/i.test(text)) {
      return "document";
    }
    return "other";
  };

  const completedFor = root => {
    const marker = `${root.className} ${attributeText(root)} ${normalize(root.textContent)}`;
    return /finish|finished|complete|completed|done|已完成|已学完|job-finish/i.test(marker);
  };

  const keyFor = (root, index) => {
    const query = new URLSearchParams(location.search);
    const source = [
      root.getAttribute?.("data-objectid"),
      root.getAttribute?.("data-taskid"),
      root.getAttribute?.("data-id"),
      root.getAttribute?.("data-url"),
      root.getAttribute?.("href"),
      root.getAttribute?.("onclick"),
      root.id,
      query.get("knowledgeid"),
      query.get("chapterId"),
      query.get("chapterid"),
      query.get("objectid"),
      normalize(root.textContent),
      index
    ].filter(Boolean).join("|");
    return `task-${hash(source || String(index))}`;
  };

  const clickableRoot = element => {
    const attachCard = element.closest(
      ".ans-attach-ct, [class*='attach-ct'], [class*='attach-card'], [class*='attachCard']"
    );
    if (attachCard) {
      return attachCard;
    }

    const direct = element.closest(
      "li, a, [data-objectid], [data-taskid], [data-url], .chapter_item, .chapter_unit, .posCatalog_select, .task-point, [class*='job']"
    );
    if (direct) {
      return direct;
    }
    const parent = element.parentElement?.closest("li, a, div");
    return parent || element;
  };

  const titleFor = (root, kind) => {
    const iframe = root.tagName === "IFRAME" ? root : root.querySelector?.("iframe");
    if (iframe) {
      const dataText = iframe.getAttribute("data");
      if (dataText) {
        try {
          const metadata = JSON.parse(dataText);
          const metadataTitle = normalize(metadata?.name || metadata?.title);
          if (metadataTitle) {
            return metadataTitle.slice(0, 240);
          }
        } catch {
          // Ignore malformed optional metadata and use the type fallback.
        }
      }

      const directTitle = normalize(iframe.getAttribute("title") || iframe.getAttribute("aria-label"));
      if (directTitle) {
        return directTitle.slice(0, 240);
      }
      return kind === "video" ? "章节视频" : "章节文档";
    }

    const containingCard = root.closest(
      "[class*='attach'], [class*='card'], [class*='module'], [class*='item']"
    ) || root.parentElement;
    return normalize(containingCard?.textContent).slice(0, 240)
      || normalize(root.getAttribute("title"))
      || normalize(root.textContent).slice(0, 240)
      || `任务`;
  };

  const collectTasks = () => {
    state.taskMap.clear();
    const selector = [
      ".ans-job-icon",
      "[class*='job-icon']",
      "[class*='task-point']",
      "[data-objectid]",
      "[data-taskid]",
      ".chapter_item",
      ".posCatalog_select",
      "iframe.ans-attach-online",
      "iframe[class*='insertvideo']",
      "iframe[class*='insertdoc']"
    ].join(",");

    const roots = [];
    const seen = new Set();
    for (const element of document.querySelectorAll(selector)) {
      const root = clickableRoot(element);
      if (!root || seen.has(root) || !visible(root)) {
        continue;
      }
      seen.add(root);
      roots.push(root);
      if (roots.length >= 400) {
        break;
      }
    }

    const tasks = roots.map((root, index) => {
      const key = keyFor(root, index);
      const kind = kindFor(root);
      state.taskMap.set(key, root);
      return {
        key,
        title: titleFor(root, kind) || `任务 ${index + 1}`,
        kind,
        completed: completedFor(root),
        locked: /unlock|previous mission|解锁|完成前置|不可学习/i.test(normalize(root.textContent)),
        current: /posCatalog_active/i.test(root.className || ""),
        order: index
      };
    }).filter(task => task.kind !== "other" || /job|task|chapter/i.test(state.taskMap.get(task.key)?.className || ""));

    for (const [key, root] of state.taskMap) {
      if (!root.isConnected) {
        state.taskMap.delete(key);
      }
    }

    post("tasks", { tasks, title: document.title, url: location.href });
  };

  const visibleVideos = () => Array.from(document.querySelectorAll("video"))
    .filter(video => video.isConnected && (visible(video) || video.readyState > 0));

  const currentVideo = () => visibleVideos()[0] || document.querySelector("video");

  const enforceMute = () => {
    if (!state.muted) {
      return;
    }
    for (const media of document.querySelectorAll("video, audio")) {
      if (!media.muted) {
        media.muted = true;
      }
    }
  };

  const videoKey = video => {
    const query = new URLSearchParams(location.search);
    const marker = query.get("objectid")
      || query.get("chapterId")
      || query.get("chapterid")
      || video.getAttribute("data-objectid")
      || video.getAttribute("data-taskid")
      || video.currentSrc
      || video.src
      || location.href;
    return `video-${hash(String(marker))}`;
  };

  const snapshotVideo = eventName => {
    const video = currentVideo();
    if (!video) {
      return;
    }
    post(eventName, {
      video: {
        taskKey: videoKey(video),
        title: normalize(video.closest("div")?.textContent).slice(0, 240) || document.title || "当前视频",
        currentTime: Number.isFinite(video.currentTime) ? video.currentTime : 0,
        duration: Number.isFinite(video.duration) ? video.duration : 0,
        paused: video.paused,
        ended: video.ended,
        sourceUrl: video.currentSrc || video.src || location.href
      }
    });
  };

  const applyResume = video => {
    if (state.resumeAt <= 1 || video.currentTime >= state.resumeAt - 2 || !Number.isFinite(video.duration)) {
      return;
    }
    video.currentTime = Math.min(state.resumeAt, Math.max(0, video.duration - 1));
  };

  const bindVideo = video => {
    if (video.__chaoxingAutoPlayerBound) {
      return;
    }

    video.__chaoxingAutoPlayerBound = true;
    video.addEventListener("loadedmetadata", () => {
      applyResume(video);
      snapshotVideo("video");
    });
    video.addEventListener("play", () => snapshotVideo("video"));
    video.addEventListener("volumechange", enforceMute);
    video.addEventListener("timeupdate", () => snapshotVideo("video"));
    video.addEventListener("pause", () => {
      snapshotVideo("video");
      if (state.running && !video.ended && !state.quizActive) {
        setTimeout(() => {
          if (!state.running || video.ended) {
            return;
          }
          if (video.paused) {
            post("blocked", { message: "播放器自行暂停。若页面出现必答题、验证码或确认框，请手动完成后点击继续。" });
          }
          playCurrent();
        }, 1800);
      }
    });
    video.addEventListener("ended", () => {
      snapshotVideo("video");
      post("ended", {});
    });
    video.addEventListener("error", () => {
      post("blocked", { message: "视频加载失败，等待重试。" });
    });
  };

  const playCurrent = () => {
    if (!state.running || state.quizActive) {
      return;
    }

    const video = currentVideo();
    if (!video) {
      return;
    }

    bindVideo(video);
    applyResume(video);
    video.playbackRate = 1;
    video.muted = state.muted;
    enforceMute();
    const promise = video.play();
    if (promise?.catch) {
      promise.catch(error => {
        post("blocked", { message: `无法自动播放：${error?.message || "浏览器未允许播放"}` });
      });
    }
    snapshotVideo("video");
  };

  const simulateMouse = () => {
    if (!state.running || state.quizActive || Date.now() - state.lastMouseAt < 9000) {
      return;
    }

    state.lastMouseAt = Date.now();
    const video = currentVideo();
    const target = video || document.body;
    if (!target) {
      return;
    }

    const rect = target.getBoundingClientRect();
    const x = Math.max(1, Math.min(window.innerWidth - 2, rect.left + Math.max(1, rect.width / 2)));
    const y = Math.max(1, Math.min(window.innerHeight - 2, rect.top + Math.max(1, rect.height / 2)));
    const options = {
      bubbles: true,
      cancelable: true,
      clientX: x,
      clientY: y,
      screenX: x,
      screenY: y,
      view: window
    };
    target.dispatchEvent(new MouseEvent("mousemove", options));
    target.dispatchEvent(new MouseEvent("mouseover", options));
    document.dispatchEvent(new MouseEvent("mousemove", options));
  };

  const clickTask = key => {
    const target = state.taskMap.get(key);
    if (!target || !target.isConnected) {
      post("blocked", { message: "目标任务点已不在页面上，准备重新扫描。" });
      return false;
    }

    target.scrollIntoView({ block: "center", inline: "nearest", behavior: "instant" });
    target.click();
    post("clickedTask", { key, title: normalize(target.textContent).slice(0, 240) });
    setTimeout(collectTasks, 800);
    return true;
  };

  const clickNextFallback = () => {
    const candidates = Array.from(document.querySelectorAll("a, button, [role='button'], span, div"))
      .filter(visible)
      .filter(element => /^(下一节|下一个|下一章|next)$/i.test(normalize(element.textContent)));
    const target = candidates[0];
    if (target) {
      target.scrollIntoView({ block: "center", behavior: "instant" });
      target.click();
      post("clickedNext", { title: normalize(target.textContent) });
    } else {
      post("blocked", { message: "没有找到后续视频任务点。" });
    }
  };

  const visibleQuizRoot = () => {
    const candidates = Array.from(document.querySelectorAll(
      ".ans-videoquiz, [id*='videoquiz'], [class*='videoquiz']"
    )).filter(candidate =>
      visible(candidate)
      && !candidate.matches(".ans-videoquiz-opt, a, button, input")
      && candidate.querySelectorAll(".ans-videoquiz-opt").length > 0
    );
    return candidates
      .sort((left, right) => left.querySelectorAll(".ans-videoquiz-opt").length
        - right.querySelectorAll(".ans-videoquiz-opt").length)[0] || null;
  };

  const readQuiz = () => {
    const root = visibleQuizRoot();
    if (!root) {
      return null;
    }

    const optionElements = Array.from(root.querySelectorAll("li.ans-videoquiz-opt, .ans-videoquiz-opt"));
    if (optionElements.length === 0) {
      return null;
    }

    const options = optionElements.map((element, index) => {
      const input = element.querySelector("input[name='ans-videoquiz-opt'], input[type='radio'], input[type='checkbox']");
      const marker = [
        input?.value,
        input?.getAttribute("data-right"),
        input?.getAttribute("data-correct"),
        element.getAttribute("data-right"),
        element.getAttribute("data-correct"),
        element.className
      ].filter(Boolean).join(" ").toLowerCase();
      return {
        index,
        element,
        text: normalize(element.textContent),
        markedCorrect: /(^|\s)(true|1|right|correct)(\s|$)/i.test(marker),
        input
      };
    });

    const question = normalize(
      root.querySelector(".tkTopic_title, .question-title, .ans-videoquiz-title, [class*='topic_title']")?.textContent
        || root.textContent
    ).slice(0, 1000);
    const multiple = options.some(option => option.input?.type === "checkbox")
      || /多选|multiple/i.test(root.textContent);
    const questionType = multiple
      ? "多选题"
      : /判断|true|false|正确|错误/i.test(root.textContent) ? "判断题" : "单选题";
    return { root, options, question, multiple, questionType };
  };

  const quizSignatureFor = quiz =>
    `${quiz.questionType}|${quiz.question}|${quiz.options.map(option => option.text).join("|")}`;

  const resetQuizState = () => {
    state.quizActive = false;
    state.quizSubmitting = false;
    state.quizSkipping = false;
    state.quizSubmittedAt = 0;
    state.quizSignature = "";
    state.quizOutcomeSignature = "";
  };

  const findQuizSkipControl = root => {
    const controls = Array.from(root.querySelectorAll(
      "a, button, span, i, [role='button']"
    )).filter(element =>
      visible(element)
      && !element.closest(".ans-videoquiz-opt")
      && !element.matches(
        ".ans-videoquiz-submit, .ans-videoquiz-back, .ans-videoquiz-continue, "
        + "#videoquiz-submit, #knowledgeBack, #videoquiz-continue, .continueLearn"
      )
    );

    return controls.find(element => {
      const text = normalize(element.textContent);
      const marker = normalize([
        element.id,
        element.className,
        element.getAttribute("title"),
        element.getAttribute("aria-label"),
        text
      ].filter(Boolean).join(" "));
      return /^(关闭|关闭本题|跳过|跳过本题|×|✕|x)$/i.test(text)
        || /(?:^|[\s_-])(close|skip)(?:$|[\s_-])/i.test(marker)
        || /关闭|跳过/i.test(marker);
    }) || null;
  };

  const detectQuiz = () => {
    if (!state.running) {
      return;
    }

    if (state.quizSkipping) {
      return;
    }

    if (state.quizSubmitting && Date.now() - state.quizSubmittedAt > 250) {
      const continueButton = Array.from(document.querySelectorAll(
        ".ans-videoquiz-continue, #videoquiz-continue, a.continueLearn"
      )).find(visible);
      if (continueButton) {
        const signature = `resolved:${normalize(continueButton.textContent)}`;
        if (state.quizOutcomeSignature !== signature) {
          state.quizOutcomeSignature = signature;
          state.quizSubmitting = false;
          state.quizActive = false;
          continueButton.click();
          post("quizResolved", { message: "互动题已通过，继续播放。" });
          playCurrent();
        }
        return;
      }

      const quiz = readQuiz();
      const wrongText = normalize(quiz?.root?.textContent);
      const wrongControl = Array.from(document.querySelectorAll(
        ".ans-videoquiz-back, #knowledgeBack, .spanNotBack"
      )).find(visible);
      if (wrongControl || /回答错误|答错|answerErr|incorrect/i.test(wrongText)) {
        const signature = `wrong:${wrongText.slice(0, 200)}`;
        if (state.quizOutcomeSignature !== signature) {
          state.quizOutcomeSignature = signature;
          state.quizSubmitting = false;
          post("quizWrong", { message: "互动题答案错误，准备尝试其他选项。" });
        }
        return;
      }

      if (Date.now() - state.quizSubmittedAt > 10000) {
        state.quizSubmitting = false;
        post("quizNeedsAttention", { message: "互动题提交后没有返回明确结果，请手动检查。" });
      }
      return;
    }

    const quiz = readQuiz();
    if (!quiz) {
      return;
    }

    const signature = quizSignatureFor(quiz);
    if (state.quizActive && state.quizSignature === signature) {
      return;
    }

    state.quizActive = true;
    state.quizSubmitting = false;
    state.quizSkipping = false;
    state.quizSignature = signature;
    state.quizOutcomeSignature = "";
    const video = currentVideo();
    try {
      video?.pause();
    } catch {
      // The platform normally pauses the video before showing the quiz.
    }
    post("quizDetected", {
      quiz: {
        question: quiz.question,
        questionType: quiz.questionType,
        multiple: quiz.multiple,
        options: quiz.options.map(option => ({
          index: option.index,
          text: option.text,
          markedCorrect: option.markedCorrect
        }))
      }
    });
  };

  const resolveQuiz = optionIndexes => {
    const quiz = readQuiz();
    if (!quiz || !Array.isArray(optionIndexes)) {
      post("quizNeedsAttention", { message: "互动题面板已变化，请重新扫描。" });
      return;
    }

    const selected = new Set(optionIndexes.map(Number));
    for (const option of quiz.options) {
      if (!option.input) {
        option.element.click();
        continue;
      }
      if (option.input.checked !== selected.has(option.index)) {
        option.input.click();
      }
    }

    const submit = Array.from(quiz.root.querySelectorAll(
      "a.ans-videoquiz-submit, #videoquiz-submit, #videoquiz-submitting"
    )).find(visible) || Array.from(document.querySelectorAll(
      "a.ans-videoquiz-submit, #videoquiz-submit"
    )).find(visible);
    if (!submit) {
      post("quizNeedsAttention", { message: "没有找到互动题提交按钮。" });
      return;
    }

    state.quizSubmitting = true;
    state.quizSkipping = false;
    state.quizSubmittedAt = Date.now();
    state.quizOutcomeSignature = "";
    submit.click();
  };

  const skipQuiz = () => {
    const quiz = readQuiz();
    if (!quiz) {
      resetQuizState();
      post("quizSkipped", { message: "学习测验已关闭，继续播放。" });
      playCurrent();
      return;
    }

    const control = findQuizSkipControl(quiz.root);
    if (!control) {
      post("quizNeedsAttention", {
        message: "当前学习测验没有可识别的“关闭/跳过”入口，请手动处理或切换为自动作答。"
      });
      return;
    }

    const signature = quizSignatureFor(quiz);
    state.quizSkipping = true;
    try {
      control.click();
    } catch {
      state.quizSkipping = false;
      post("quizNeedsAttention", { message: "点击学习测验的跳过入口失败，请手动处理。" });
      return;
    }

    setTimeout(() => {
      const remaining = readQuiz();
      const sameQuiz = Boolean(remaining && quizSignatureFor(remaining) === signature);
      state.quizSkipping = false;
      if (!sameQuiz) {
        resetQuizState();
        post("quizSkipped", { message: "已自动跳过学习测验，继续播放。" });
        playCurrent();
        return;
      }

      post("quizNeedsAttention", {
        message: "学习测验未能自动关闭，请手动处理或切换为自动作答。"
      });
    }, 1200);
  };

  window.__cxapProbe = () => ({
    running: state.running,
    quizActive: state.quizActive,
    quizSubmitting: state.quizSubmitting,
    quizSkipping: state.quizSkipping,
    quizSignature: state.quizSignature
  });
  window.__cxapQuizProbe = () => {
    const candidates = Array.from(document.querySelectorAll(
      ".ans-videoquiz, [id*='videoquiz'], [class*='videoquiz']"
    ));
    const quiz = readQuiz();
    return {
      candidates: candidates.length,
      candidateIds: candidates.map(candidate => candidate.id || candidate.className),
      candidateVisible: candidates.map(candidate => visible(candidate)),
      candidateOptions: candidates.map(candidate => candidate.querySelectorAll(".ans-videoquiz-opt").length),
      rootId: quiz?.root?.id || "",
      rootVisible: Boolean(quiz?.root && visible(quiz.root)),
      options: quiz?.options?.length || 0,
      question: quiz?.question || "",
      questionType: quiz?.questionType || ""
    };
  };
  window.__cxapDetectQuiz = detectQuiz;

  const start = message => {
    state.running = true;
    state.muted = message?.muted !== false;
    state.resumeAt = Number(message?.resumeAt) || 0;
    enforceMute();
    playCurrent();
    simulateMouse();
    collectTasks();
  };

  const pause = () => {
    state.running = false;
    for (const video of document.querySelectorAll("video")) {
      try {
        video.pause();
      } catch {
        // Ignore a video that is already detaching.
      }
    }
    snapshotVideo("video");
  };

  window.chrome?.webview?.addEventListener("message", event => {
    const message = event.data;
    if (!message || message.source !== "chaoxing-auto-player") {
      return;
    }

    switch (message.command) {
      case "start":
        start(message);
        break;
      case "pause":
        pause();
        break;
      case "scan":
        collectTasks();
        snapshotVideo("video");
        break;
      case "clickTask":
        clickTask(message.key);
        break;
      case "nextVideo":
        clickNextFallback();
        break;
      case "resolveQuiz":
        resolveQuiz(message.optionIndexes);
        break;
      case "skipQuiz":
        skipQuiz();
        break;
      case "setMuted":
        state.muted = Boolean(message.muted);
        enforceMute();
        playCurrent();
        break;
      default:
        break;
    }
  });

  setInterval(() => {
    if (state.running) {
      playCurrent();
      simulateMouse();
      enforceMute();
    }
  }, 5000);

  setInterval(() => {
    if (state.running) {
      detectQuiz();
    }
  }, 1000);

  setInterval(() => {
    if (state.running) {
      collectTasks();
      snapshotVideo("video");
    }
  }, 12000);

  document.addEventListener("DOMContentLoaded", () => {
    collectTasks();
    snapshotVideo("video");
  }, { once: true });

  setTimeout(() => {
    post("ready", { title: document.title, url: location.href });
    collectTasks();
    snapshotVideo("video");
  }, 500);
})();
