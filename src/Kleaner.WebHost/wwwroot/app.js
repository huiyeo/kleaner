const TOKEN_KEY = "kleaner.api-token";
const LOCALE_URL = "/locales/zh-CN.json";
let strings = {};

const fallback = {
  appName: "Kleaner", dashboard: "清理概览", quarantine: "隔离区", history: "操作历史",
  toolbox: "工具箱", startup: "启动项", settings: "设置", ready: "已连接到本地服务",
  reconnecting: "正在等待本地服务恢复…", offline: "本地服务不可用", install: "安装应用",
  updateReady: "发现新版本，点击更新", shellTitle: "安全、可预览、可还原的清理体验",
  shellBody: "选择规则、扫描、预览和确认将由后续页面逐步接入。此壳已建立本地服务连接并可在离线时打开。",
  whitelist: "严格白名单", preview: "强制预览", restore: "隔离区可还原",
};

function receiveLaunchToken() {
  const url = new URL(window.location.href);
  const token = url.searchParams.get("token");
  if (token) {
    sessionStorage.setItem(TOKEN_KEY, token);
    url.searchParams.delete("token");
    history.replaceState({}, "", url);
  }
  return sessionStorage.getItem(TOKEN_KEY);
}

function t(key) { return strings[key] ?? fallback[key] ?? key; }

async function loadStrings() {
  try {
    strings = await (await fetch(LOCALE_URL, { cache: "no-store" })).json();
  } catch {
    strings = fallback;
  }
}

function apiHeaders() {
  const token = sessionStorage.getItem(TOKEN_KEY);
  return token ? { "X-Kleaner-Token": token } : {};
}

class KleanerShell extends HTMLElement {
  connectedCallback() { this.render(); }

  render() {
    this.innerHTML = `<main class="shell">
      <aside class="sidebar">
        <div class="brand"><img src="/icons/kleaner.svg" alt=""><span>${t("appName")}</span></div>
        <section class="summary"><div class="ring">0 B</div><b>${t("dashboard")}</b><br><small>扫描后显示可释放空间</small></section>
        <nav class="nav" aria-label="主导航">
          ${["dashboard", "quarantine", "history", "toolbox", "startup", "settings"].map((key) =>
            `<button type="button" data-view="${key}" ${key === "dashboard" ? 'aria-current="page"' : ""}>${t(key)}</button>`).join("")}
        </nav>
        <div class="safety">${t("whitelist")} · ${t("preview")} · ${t("restore")}</div>
      </aside>
      <section class="main">
        <header class="topbar"><h1 id="page-title">${t("dashboard")}</h1><span class="connection" data-state="offline" id="connection">${t("offline")}</span><button class="button" hidden id="install">${t("install")}</button></header>
        <section class="content" id="content">${this.dashboard()}</section>
        <footer class="statusbar" data-state="offline" id="status"><span class="dot"></span><span>${t("offline")}</span></footer>
      </section>
    </main><aside class="notice" hidden id="update-notice"><span>${t("updateReady")}</span> <button class="button primary" id="update">更新</button></aside>`;
    this.querySelectorAll("[data-view]").forEach((button) => button.addEventListener("click", () => this.showView(button)));
  }

  dashboard() {
    return `<article class="hero"><h2>${t("shellTitle")}</h2><p>${t("shellBody")}</p><div class="cards">
      <div class="card"><b>${t("whitelist")}</b><span>只显示规则库中有明确安全理由的条目。</span></div>
      <div class="card"><b>${t("preview")}</b><span>执行前先生成计划；确认凭据一次性使用。</span></div>
      <div class="card"><b>${t("restore")}</b><span>清理文件移入隔离区，保留审计记录。</span></div>
    </div></article>`;
  }

  showView(button) {
    this.querySelectorAll("[data-view]").forEach((item) => item.removeAttribute("aria-current"));
    button.setAttribute("aria-current", "page");
    this.querySelector("#page-title").textContent = button.textContent;
    this.querySelector("#content").innerHTML = `<article class="hero"><h2>${button.textContent}</h2><p>该页面将在后续工单中接入真实数据与操作流程。</p></article>`;
  }
}

customElements.define("kleaner-shell", KleanerShell);

function setConnection(state, text) {
  document.querySelector("#connection")?.setAttribute("data-state", state);
  document.querySelector("#connection")?.replaceChildren(text);
  const status = document.querySelector("#status");
  status?.setAttribute("data-state", state);
  status?.querySelector("span:last-child")?.replaceChildren(text);
}

function handleEvent(eventName, payload) {
  if (eventName === "connected") setConnection("connected", t("ready"));
  if (eventName === "job.started") setConnection("connected", `正在执行 ${payload.kind ?? "任务"}`);
  if (eventName === "job.completed" || eventName === "job.cancelled") setConnection("connected", t("ready"));
}

async function connectEvents() {
  let attempt = 0;
  while (true) {
    try {
      const response = await fetch("/api/events", { headers: apiHeaders(), cache: "no-store" });
      if (!response.ok || !response.body) throw new Error(`SSE ${response.status}`);
      attempt = 0;
      const reader = response.body.pipeThrough(new TextDecoderStream()).getReader();
      let buffer = "";
      while (true) {
        const { value, done } = await reader.read();
        if (done) break;
        buffer += value;
        const frames = buffer.split("\n\n");
        buffer = frames.pop();
        for (const frame of frames) {
          const eventName = frame.match(/^event: (.+)$/m)?.[1];
          const data = frame.match(/^data: (.+)$/m)?.[1];
          if (eventName && data) handleEvent(eventName, JSON.parse(data));
        }
      }
    } catch {
      // 同端口同 token 的重启（提权 / Velopack）会在这里进入指数退避，成功后重新取得服务端快照。
    }
    const delay = Math.min(1000 * 2 ** attempt, 15000);
    attempt += 1;
    setConnection("retrying", `${t("reconnecting")}（${Math.ceil(delay / 1000)} 秒）`);
    await new Promise((resolve) => setTimeout(resolve, delay));
  }
}

function setupPwa() {
  let deferredInstall;
  const install = document.querySelector("#install");
  window.addEventListener("beforeinstallprompt", (event) => {
    event.preventDefault();
    deferredInstall = event;
    install.hidden = false;
  });
  install?.addEventListener("click", async () => {
    await deferredInstall?.prompt();
    deferredInstall = undefined;
    install.hidden = true;
  });
  if (!("serviceWorker" in navigator)) return;
  navigator.serviceWorker.register("/sw.js").then((registration) => {
    const showUpdate = () => {
      if (registration.waiting && navigator.serviceWorker.controller)
        document.querySelector("#update-notice").hidden = false;
    };
    registration.addEventListener("updatefound", () => registration.installing?.addEventListener("statechange", showUpdate));
    showUpdate();
    document.querySelector("#update")?.addEventListener("click", () => registration.waiting?.postMessage("SKIP_WAITING"));
  });
  navigator.serviceWorker.addEventListener("controllerchange", () => window.location.reload());
}

receiveLaunchToken();
await loadStrings();
document.querySelector("kleaner-shell")?.render();
setupPwa();
connectEvents();
