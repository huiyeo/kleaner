const TOKEN_KEY = "kleaner.api-token";
const LOCALE_URL = "/locales/zh-CN.json";
let strings = {};
let jobsSnapshot = [];

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

class TokenExpiredError extends Error {}

async function refreshJobsSnapshot() {
  const response = await fetch("/api/jobs", { headers: apiHeaders(), cache: "no-store" });
  if (response.status === 401 || response.status === 403) throw new TokenExpiredError();
  if (!response.ok) throw new Error(`jobs ${response.status}`);
  jobsSnapshot = await response.json();
  window.dispatchEvent(new CustomEvent("kleaner.jobs-snapshot", { detail: jobsSnapshot }));
  return jobsSnapshot;
}

const mainScreen = { jobId: null, scan: null, selected: new Set(), plan: null, drawer: false, progress: { ruleIds: new Set(), files: 0, bytes: 0 }, message: "扫描会严格按规则库预览，不会删除文件。" };
const categoryNames = { temp: "临时文件", "browser-cache": "浏览器缓存", "dev-cache": "开发缓存", updater: "更新残留", system: "系统缓存", application: "应用缓存" };
const escapeHtml = (value) => String(value ?? "").replace(/[&<>'"]/g, (char) => ({ "&": "&amp;", "<": "&lt;", ">": "&gt;", "'": "&#39;", '"': "&quot;" })[char]);
const formatBytes = (bytes) => bytes >= 1024 ** 3 ? `${(bytes / 1024 ** 3).toFixed(2)} GB` : bytes >= 1024 ** 2 ? `${(bytes / 1024 ** 2).toFixed(1)} MB` : bytes >= 1024 ? `${(bytes / 1024).toFixed(0)} KB` : `${bytes} B`;

async function apiJson(path, method = "GET", body) {
  const headers = { ...apiHeaders() };
  if (body !== undefined) headers["Content-Type"] = "application/json";
  const response = await fetch(path, { method, headers, body: body === undefined ? undefined : JSON.stringify(body), cache: "no-store" });
  if (response.status === 401 || response.status === 403) throw new TokenExpiredError();
  const payload = response.status === 204 ? null : await response.json().catch(() => ({}));
  if (!response.ok) throw new Error(payload.error ?? `请求失败（${response.status}）`);
  return payload;
}

function updateFromJobs(jobs) {
  const scan = [...jobs].reverse().find((job) => job.kind === "scan");
  if (!scan) return;
  mainScreen.jobId = scan.jobId;
  if (scan.status === "Completed" && scan.result?.rules) {
    mainScreen.scan = scan.result;
    if (mainScreen.selected.size === 0) scan.result.rules.filter((rule) => rule.machineVerified).forEach((rule) => mainScreen.selected.add(rule.ruleId));
    mainScreen.message = "扫描完成。请核对规则和安全说明后再预览清理计划。";
  } else if (scan.status === "Running" || scan.status === "Cancelling") mainScreen.message = `正在只读扫描白名单规则… 已完成 ${mainScreen.progress.ruleIds.size} 条规则`;
  else if (scan.status === "Cancelled") { mainScreen.jobId = null; mainScreen.progress = { ruleIds: new Set(), files: 0, bytes: 0 }; mainScreen.message = "扫描已取消，未生成清理计划。"; }
  renderMainScreen();
}

window.addEventListener("kleaner.jobs-snapshot", (event) => updateFromJobs(event.detail));

function renderMainScreen() {
  const content = document.querySelector("#content");
  if (!content || document.querySelector("[data-view][aria-current='page']")?.dataset.view !== "dashboard") return;
  const rules = mainScreen.scan?.rules ?? [];
  const selected = rules.filter((rule) => mainScreen.selected.has(rule.ruleId));
  const bytes = selected.reduce((sum, rule) => sum + rule.totalBytes, 0);
  const files = selected.reduce((sum, rule) => sum + rule.fileCount, 0);
  const groups = Object.values(Object.groupBy(rules, (rule) => rule.category));
  const scanning = mainScreen.jobId && !mainScreen.scan;
  document.querySelector(".summary .ring")?.replaceChildren(rules.length ? formatBytes(bytes) : "0 B");
  content.innerHTML = `<section class="main-dashboard"><div class="main-actions"><p>${escapeHtml(mainScreen.message)}</p><button class="button primary" data-main="scan" ${scanning ? "disabled" : ""}>${scanning ? "扫描中…" : "开始扫描"}</button></div>${rules.length ? `<div class="clean-summary"><b>已勾选 ${selected.length} 条规则</b><span>${files} 个文件 · ${formatBytes(bytes)}</span><button class="button primary" data-main="preview" ${selected.length ? "" : "disabled"}>预览并清理</button></div><div class="rule-groups">${groups.map((group) => `<details open class="rule-group"><summary>${escapeHtml(categoryNames[group[0].category] ?? group[0].category)} · ${group.length} 条规则</summary>${group.map((rule) => `<label class="rule-row"><input type="checkbox" data-rule="${escapeHtml(rule.ruleId)}" ${mainScreen.selected.has(rule.ruleId) ? "checked" : ""}><span class="rule-copy"><b>${escapeHtml(rule.ruleName)}</b><small>${escapeHtml(rule.safetyNotes)}</small><em>${rule.risk === "medium" ? "中风险" : "低风险"} · ${rule.machineVerified ? "已验证" : "未验证·默认不勾选"}${rule.requiresElevation ? " · 需要管理员权限" : ""}</em></span><span>${rule.fileCount} 个 · ${formatBytes(rule.totalBytes)}</span></label>`).join("")}</details>`).join("")}</div>` : `<article class="hero"><h2>准备扫描</h2><p>只会读取已启用的白名单规则。扫描不会删除或移动任何文件；完成后才可生成一次性确认的清理计划。</p></article>`}${scanning ? `<footer class="scan-strip"><span>只读预览中，已完成 ${mainScreen.progress.ruleIds.size} 条规则 · ${mainScreen.progress.files} 个文件 · ${formatBytes(mainScreen.progress.bytes)}。</span><div><i></i></div><button class="button" data-main="cancel">取消扫描</button></footer>` : ""}${mainScreen.drawer && mainScreen.plan ? `<aside class="clean-drawer"><h2>确认清理计划</h2><p>以下文件将移入隔离区，可还原，不会直接永久删除。</p>${mainScreen.plan.items.map((item) => `<div>${escapeHtml(item.ruleName)} <span>${item.fileCount} 个 · ${formatBytes(item.totalBytes)}</span></div>`).join("")}<strong>合计 ${mainScreen.plan.totalFiles} 个文件 · ${formatBytes(mainScreen.plan.totalBytes)}</strong><div class="drawer-actions"><button class="button" data-main="close">返回调整</button><button class="button primary" data-main="confirm">确认移入隔离区</button></div></aside>` : ""}</section>`;
  content.querySelectorAll("[data-rule]").forEach((input) => input.addEventListener("change", () => { input.checked ? mainScreen.selected.add(input.dataset.rule) : mainScreen.selected.delete(input.dataset.rule); renderMainScreen(); }));
  content.querySelectorAll("[data-main]").forEach((button) => button.addEventListener("click", () => runMainAction(button.dataset.main)));
}

async function runMainAction(action) {
  try {
    if (action === "scan") { const job = await apiJson("/api/scan", "POST", {}); mainScreen.jobId = job.jobId; mainScreen.scan = null; mainScreen.plan = null; mainScreen.drawer = false; mainScreen.progress = { ruleIds: new Set(), files: 0, bytes: 0 }; mainScreen.selected.clear(); mainScreen.message = "正在只读扫描白名单规则…"; }
    else if (action === "cancel") { await apiJson(`/api/jobs/${mainScreen.jobId}/cancel`, "POST", {}); mainScreen.message = "已请求取消扫描，等待当前规则安全结束。"; }
    else if (action === "preview") { mainScreen.plan = await apiJson("/api/plans", "POST", { jobId: mainScreen.jobId, ruleIds: [...mainScreen.selected] }); if (mainScreen.plan.needsElevation) { mainScreen.message = "所选规则需要管理员权限，正在请求重启并等待重连；恢复后请重新扫描以生成新的预览计划。"; await apiJson("/api/elevate", "POST", {}); mainScreen.plan = null; mainScreen.scan = null; mainScreen.jobId = null; mainScreen.selected.clear(); } else mainScreen.drawer = true; }
    else if (action === "confirm") { const report = await apiJson(`/api/plans/${mainScreen.plan.planId}/confirm`, "POST", { confirmToken: mainScreen.plan.confirmToken }); mainScreen.drawer = false; mainScreen.message = `已移入隔离区：${report.movedCount} 个文件，跳过 ${report.skipped.length} 个；批次 ${report.batchId}。`; }
    else if (action === "close") mainScreen.drawer = false;
  } catch (error) { mainScreen.message = error instanceof TokenExpiredError ? "连接令牌已失效，请重新打开 Kleaner" : error.message; }
  renderMainScreen();
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
    renderMainScreen();
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
    if (button.dataset.view === "dashboard") renderMainScreen();
    else this.querySelector("#content").innerHTML = `<article class="hero"><h2>${button.textContent}</h2><p>该页面将在后续工单中接入真实数据与操作流程。</p></article>`;
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
  if (eventName === "scan.progress") {
    if (!mainScreen.progress.ruleIds.has(payload.ruleId)) {
      mainScreen.progress.ruleIds.add(payload.ruleId);
      mainScreen.progress.files += payload.fileCount ?? 0;
      mainScreen.progress.bytes += payload.totalBytes ?? 0;
    }
    mainScreen.message = `正在只读扫描白名单规则… 已完成 ${mainScreen.progress.ruleIds.size} 条规则`;
    renderMainScreen();
  }
  if (eventName === "job.completed" || eventName === "job.cancelled") refreshJobsSnapshot().catch(() => {});
}

async function connectEvents() {
  let attempt = 0;
  while (true) {
    try {
      // SSE 没有回放；每次连接前先恢复 REST 快照，避免断线期间漏掉任务终态。
      await refreshJobsSnapshot();
      const response = await fetch("/api/events", { headers: apiHeaders(), cache: "no-store" });
      if (response.status === 401 || response.status === 403) throw new TokenExpiredError();
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
    } catch (error) {
      if (error instanceof TokenExpiredError) {
        setConnection("error", "连接令牌已失效，请重新打开 Kleaner");
        return;
      }
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
