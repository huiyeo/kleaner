"""MangoDisk Windows 规则清单 → Kleaner rules.v1.json 迁移工具。

合规边界：MangoDisk 为 GPL-3.0，本工具只提取其规则所指向的**目录事实**
（路径模式、官方文档链接），安全说明文本全部为本项目原创表述，
不复制其 evidence 原文。safety-notes.md 与 README 中已透明标注来源。

用法：
  python tools/import_mangodisk_rules.py convert <toml目录> [输出json]
  python tools/import_mangodisk_rules.py verify <rules.json> <scan输出.json> [日期]
输出 JSON：{"schemaVersion":1, "rules":[新规则], "extensions":{现有id:{paths,exclude}}}
"""
from __future__ import annotations

import glob
import json
import sys
import tomllib
from pathlib import Path

TPL = {
    "home": "%USERPROFILE%",
    "local_app_data": "%LOCALAPPDATA%",
    "roaming_app_data": "%APPDATA%",
    "program_data": "%ProgramData%",
    "temp": "%TEMP%",
}
CAT_MAP = {
    "ai": "application",
    "application": "application",
    "browser": "browser-cache",
    "container": "application",
    "development": "dev-cache",
    "system": "system",
}
RISK_MAP = {"safe": "low", "recoverable": "medium"}

CAT_PURPOSE = {
    "application": "应用运行产生的网页缓存、着色器缓存、日志或崩溃报告，均为可再生数据，不涉及聊天记录、账号与用户配置。",
    "browser-cache": "浏览器的网页缓存与着色器缓存，删除后按需重新下载，不涉及书签、密码、历史记录等用户数据。",
    "dev-cache": "开发工具链的下载/编译缓存，删除后由工具按需重建，不影响已安装的工具本身与项目源码。",
    "updater": "应用内置更新器的安装包残留，按「保留最新 1 份」策略清理。",
    "system": "Windows 系统组件缓存，由系统按需重建。",
}

SKIP = {
    "dev.maven-cache": "与现有 maven-repository 同根",
    "dev.uv-cache": "与现有 uv-cache 同根",
    "dev.go-cache": "与现有 go-build-cache 同根",
    "dev.pip-cache": "与现有 pip-cache 同根",
    "system.user-temp": "与现有 user-temp 同根（保留本机已验证的 14 天阈值）",
    "system.crash-dumps": "与现有 crash-dumps 同根",
}

EXTEND = {
    "browser.chrome-cache": "chrome-http-cache",
    "browser.edge-cache": "edge-http-cache",
    "browser.chrome-offline-cache": "chrome-http-cache",
    "browser.edge-offline-cache": "edge-http-cache",
    "dev.cargo-cache": "cargo-registry",
    "dev.gradle-cache": "gradle-caches",
    "dev.browser-automation-cache": "playwright-browsers",
    "dev.npm-cache": "npm-cache",
    "system.error-reports": "wer-reports",
}

NAME_ZH = {
    "electron-updater": "Electron 系更新器", "electron": "Electron 应用",
    "gecko-family": "Gecko 系浏览器", "stale-partial-downloads": "下载目录残留分块",
    "directx-shader": "GPU 着色器（DirectX/NVIDIA/AMD/Intel）",
    "browser-automation": "自动化测试工具（Cypress/Selenium）",
    "tencent-meeting": "腾讯会议", "netease-cloud-music": "网易云音乐",
    "game-launcher": "游戏启动器（Epic/育碧）", "build-accelerator": "编译加速器（sccache/Terraform）",
    "package-manager": "包管理器（Composer/deno/vcpkg）", "python-tooling": "Python 工具链（Poetry/pyenv）",
    "user-tool": "用户级开发工具（bun/ruff/mypy 等）", "jvm-tooling": "JVM 工具链（sbt/Ivy）",
    "sogou-input": "搜狗输入法", "douyin-live": "抖音直播伴侣", "visual-studio": "Visual Studio",
    "duckduckgo": "DuckDuckGo", "go-module": "Go 模块下载缓存", "copilot-cli": "Copilot CLI",
    "huggingface": "HuggingFace", "battlenet": "暴雪战网", "chatgpt": "ChatGPT 桌面版",
    "dropbox": "Dropbox", "flashvoice": "FlashVoice", "gitmind": "GitMind", "jetbrains": "JetBrains IDE",
    "thumbnail": "Windows 缩略图", "zenaion": "Zenaion", "360-safe": "360 安全浏览器",
    "360-speed": "360 极速浏览器", "android": "Android SDK", "ccache": "ccache", "docker": "Docker Desktop",
    "notion": "Notion", "postman": "Postman", "sccache": "sccache", "telegram": "Telegram",
    "adobe": "Adobe", "arc": "Arc 浏览器", "brave": "Brave", "chrome": "Chrome", "chromium": "Chromium",
    "discord": "Discord", "editor": "VS Code 系编辑器", "edge": "Edge", "firefox": "Firefox",
    "hex": "Hex 包管理器", "nuget": "NuGet", "opera": "Opera", "pnpm": "pnpm", "qq": "QQ",
    "signal": "Signal", "sogou": "搜狗浏览器", "spotify": "Spotify", "steam": "Steam", "teams": "Teams",
    "uc": "UC 浏览器", "vivaldi": "Vivaldi", "wecom": "企业微信", "wechat": "微信", "whatsapp": "WhatsApp",
    "wps": "WPS", "yarn": "Yarn", "dart": "Dart 分析服务", "obs": "OBS Studio", "ea": "EA App",
    "2345": "2345 浏览器", "vlc": "VLC", "zoom": "Zoom", "node-tooling": "Node 工具链（corepack/node-gyp/electron）",
    "slack": "Slack", "figma": "Figma", "obsidian": "Obsidian", "insomnia": "Insomnia",
    "claude": "Claude 桌面版", "github-desktop": "GitHub Desktop", "microsoft-teams": "Microsoft Teams",
    "code": "VS Code", "cursor": "Cursor", "windsurf": "Windsurf", "vscodium": "VSCodium",
}

# 按 mango id 覆盖显示名（消除同名歧义）
NAME_OVERRIDE = {
    "app.wechat-diagnostic-cache": "微信诊断日志",
    "app.wechat-rendering-cache": "微信渲染/小程序缓存",
    "app.wps-diagnostic-cache": "WPS 日志与转储",
    "app.wps-rendering-cache": "WPS 渲染缓存",
    "app.zoom-diagnostic-cache": "Zoom 日志",
    "dev.android-user-cache": "Android 用户目录缓存（~/.android）",
    "app.douyin-live-updater-cache": "抖音直播伴侣更新器缓存",
    "dev.go-module-cache": "Go 模块下载缓存",
}

# 按子目录拆分为多条规则：mango id -> (子目录列表, 规则名模板)
SPLIT_RULES = {
    "app.electron-cache": (
        ["Slack", "Microsoft Teams", "Figma", "obsidian", "Insomnia", "Claude", "GitHub Desktop"],
        None),  # 名称取自 NAME_ZH 的子目录键
    "dev.editor-cache": (
        ["Code", "Cursor", "Windsurf", "VSCodium"],
        None),
}

NOTES_EXTRA = {
    "dev.ccache-cache": "已排除配置文件 ccache.conf，保留用户缓存配置。",
    "system.stale-partial-downloads": "仅命中浏览器下载产生的 .crdownload/.part 等分块临时文件，不触碰正常命名的下载文件，深度限制在下载目录 3 层以内。",
    "app.telegram-temporary-cache": "仅限 tdata 下的 temp 与 dumps 目录，不触碰会话数据。",
    "dev.nuget-cache": "包含全局包目录 ~/.nuget/packages，删除后首次还原构建会重新下载。",
    "system.directx-shader-cache": "删除后首次启动游戏/3D 应用会重新编译着色器，可能短暂变慢。",
    "app.wechat-rendering-cache": "仅命中小程序编译缓存与网页缓存，不触碰聊天记录与用户数据。",
    "app.zoom-diagnostic-cache": "仅限 Zoom 日志目录，14 天阈值。",
    "app.obs-diagnostic-cache": "仅命中 OBS 的日志/性能分析/崩溃目录。",
}


def app_key(mid: str) -> str:
    tail = mid.split(".", 1)[1] if "." in mid else mid
    for suffix in ("-cache", "-diagnostic", "-rendering", "-msix", "-offline", "-user", "-tool"):
        if tail.endswith(suffix):
            tail = tail[: -len(suffix)]
            break
    return tail


def name_zh(mid: str) -> str:
    if mid in NAME_OVERRIDE:
        return NAME_OVERRIDE[mid]
    key = app_key(mid)
    for k in sorted(NAME_ZH, key=len, reverse=True):
        if key == k or key.startswith(k + "-") or k in key:
            base = NAME_ZH[k]
            break
    else:
        base = key
    if base.endswith("缓存"):
        return base
    sep = " " if base.isascii() else ""
    return f"{base}{sep}缓存"


def exp_root(r: dict) -> list[str]:
    """展开一个 root 定义为 Kleaner 路径前缀（目录，不含尾部通配）。"""
    tpl = r["template"]
    for k, v in TPL.items():
        tpl = tpl.replace("${" + k + "}", v)
    tpl = tpl.replace("/", "\\")
    if r.get("kind") != "childDirectories":
        return [tpl]
    if r.get("include_all_children") or r.get("child_prefixes"):
        # profile 目录族（Default/Guest/Profile N…）：段用 * 通配，后缀是安全边界
        segs = ["*"]
    elif r.get("child_names"):
        segs = list(r["child_names"])
    else:
        segs = [""]
    suffixes = r.get("suffixes") or [""]
    out = []
    for seg in segs:
        for suf in suffixes:
            parts = [tpl]
            if seg:
                parts.append(seg)
            if suf:
                parts.append(suf.replace("/", "\\"))
            out.append("\\".join(parts))
    return out


def _matchers(d: dict) -> list[dict]:
    mk = d.get("matcher", {})
    return mk.get("items", []) if mk.get("kind") == "allOf" else [mk]


def paths_of(d: dict) -> list[str]:
    mkind = d.get("matcher", {}).get("kind", "all")
    out: list[str] = []
    if mkind == "allOf" and any(i.get("kind") == "pathSegmentIn" for i in _matchers(d)):
        mkind = "pathSegmentIn"  # not(pathSegmentIn) 由显式枚举天然排除
        segs = next(i["values"] for i in _matchers(d) if i.get("kind") == "pathSegmentIn")
    if mkind == "pathSegmentIn":
        segs = locals().get("segs", d["matcher"].get("values"))
        for r in d.get("roots", []):
            out.extend(b + "\\" + s + "\\**" for b in exp_root(r) for s in segs)
    elif mkind == "extensionIn":
        for r in d.get("roots", []):
            out.extend(b + "\\*." + e for b in exp_root(r) for e in d["matcher"]["values"])
    elif mkind == "allOf" and any(i.get("kind") == "extensionIn" for i in _matchers(d)):
        # extensionIn + maxDepth：按 0..3 层深度展开
        exts = next(i["values"] for i in _matchers(d) if i.get("kind") == "extensionIn")
        depth = next((i.get("depth", 1) for i in _matchers(d) if i.get("kind") == "maxDepth"), 1)
        for r in d.get("roots", []):
            for b in exp_root(r):
                for lv in range(depth + 1):
                    mid = "\\*" * lv
                    out.extend(f"{b}{mid}\\*.{e}" for e in exts)
    else:  # all / olderThan / not / nameGlob
        for r in d.get("roots", []):
            out.extend(b + "\\**" for b in exp_root(r))
        if mkind == "nameGlob":
            out = []
            for r in d.get("roots", []):
                out.extend(b + "\\" + g for b in exp_root(r) for g in d["matcher"]["values"])
    return dedup(out)


def exclude_of(d: dict) -> list[str]:
    out: list[str] = []
    for item in _matchers(d):
        if item.get("kind") == "not" and item.get("item", {}).get("kind") == "nameEquals":
            for r in d.get("roots", []):
                for b in exp_root(r):
                    out.extend(b + "\\" + n for n in item["item"].get("values", []))
    return dedup(out)


def dedup(xs: list[str]) -> list[str]:
    seen, out = set(), []
    for x in xs:
        if x.lower() not in seen:
            seen.add(x.lower())
            out.append(x)
    return out


def age_of(d: dict) -> int | None:
    for item in _matchers(d):
        if item.get("kind") == "olderThan":
            return int(item["days"])
    return None


def note_of(d: dict, cat: str) -> str:
    parts = [CAT_PURPOSE[cat]]
    if d["id"] in NOTES_EXTRA:
        parts.append(NOTES_EXTRA[d["id"]])
    refs = d.get("verification", {}).get("references") or []
    if refs:
        parts.append("参考：" + "、".join(refs[:3]))
    parts.append("仅清理限定目录内容，被占用文件自动跳过并提示。")
    return "".join(parts)


UNVERIFIED = "官方文档来源，本机未验证，默认不勾选"


def convert_rule(d: dict) -> dict | None:
    cat = CAT_MAP[d["category"]]
    paths = paths_of(d)
    if len(paths) > 32:
        print(f"  !! {d['id']}: paths={len(paths)} 超上限 32，跳过", file=sys.stderr)
        return None
    rule = {
        "id": d["id"].replace(".", "-"),
        "name": d.get("name") or name_zh(d["id"]),
        "category": cat,
        "risk": RISK_MAP[d["risk"]],
        "paths": paths,
        "requiresElevation": False,
        "enabled": True,
        "safetyNotes": note_of(d, cat),
        "safetyDoc": f"docs/safety-notes.md#{d['id'].replace('.', '-')}",
        "verified": UNVERIFIED,
    }
    exclude = exclude_of(d)
    if exclude:
        rule["exclude"] = exclude
    age = age_of(d)
    if age is not None:
        rule["ageDays"] = age
    return rule


UPDATER_IDS = {"app.douyin-live-updater-cache", "app.electron-updater-cache"}


def convert_updater(d: dict) -> dict | None:
    paths = []
    for r in d.get("roots", []):
        for b in exp_root(r):
            paths.extend([b + "\\**\\*.exe", b + "\\**\\*.nupkg"])
    paths = dedup(paths)
    if len(paths) > 32:
        print(f"  !! {d['id']}: updater paths 超上限", file=sys.stderr)
        return None
    return {
        "id": d["id"].replace(".", "-"),
        "name": name_zh(d["id"]),
        "category": "updater",
        "risk": "low",
        "paths": paths,
        "requiresElevation": False,
        "enabled": True,
        "keepNewest": 1,
        "safetyNotes": CAT_PURPOSE["updater"] + "被占用文件自动跳过并提示。",
        "safetyDoc": f"docs/safety-notes.md#{d['id'].replace('.', '-')}",
        "verified": UNVERIFIED,
    }


def convert_split(d: dict, child: str) -> dict | None:
    """把按应用名枚举子目录的规则拆成每应用一条（避免 paths 超限）。"""
    import copy

    d2 = copy.deepcopy(d)
    for r in d2.get("roots", []):
        if r.get("kind") == "childDirectories":
            r["child_names"] = [child]
            r.pop("child_prefixes", None)
            r.pop("include_all_children", None)
    slug = child.lower().replace(" ", "-").replace(".", "")
    d2["id"] = d["id"] + "-" + slug
    base = NAME_ZH.get(slug, child)
    sep = " " if base.isascii() else ""
    d2["name"] = f"{base}{sep}缓存"
    return convert_rule(d2)


def cmd_convert(toml_dir: str, out_path: str) -> None:
    rules, extensions, skipped = [], {}, []
    for f in sorted(glob.glob(str(Path(toml_dir) / "**" / "*.toml"), recursive=True)):
        d = tomllib.load(open(f, "rb"))
        mid = d["id"]
        if mid in SKIP:
            skipped.append((mid, SKIP[mid]))
            continue
        if mid in EXTEND:
            target = EXTEND[mid]
            ext = extensions.setdefault(target, {"paths": [], "exclude": []})
            ext["paths"] = dedup(ext["paths"] + paths_of(d))
            ext["exclude"] = dedup(ext["exclude"] + exclude_of(d))
            skipped.append((mid, f"附加路径并入现有规则 {target}"))
            continue
        if mid in SPLIT_RULES:
            for child in SPLIT_RULES[mid][0]:
                r = convert_split(d, child)
                if r:
                    rules.append(r)
            continue
        r = convert_updater(d) if mid in UPDATER_IDS else convert_rule(d)
        if r:
            rules.append(r)
    Path(out_path).write_text(json.dumps(
        {"schemaVersion": 1, "rules": rules, "extensions": extensions},
        ensure_ascii=False, indent=1), encoding="utf-8")
    print(f"转换 {len(rules)} 条新规则，{len(extensions)} 条现有规则待扩展 -> {out_path}")
    for mid, why in skipped:
        print(f"  跳过 {mid}: {why}")


def _covered(candidate: str, existing: list[str]) -> bool:
    """candidate 是否已被 existing 中某条模式覆盖（前缀 + ** 或完全一致）。"""
    cand = candidate.lower().rstrip("\\*")
    for ex in existing:
        exn = ex.lower()
        if exn.endswith("\\**") and cand.startswith(exn[:-2]):
            return True
    return False


def cmd_merge(candidates_path: str, rules_path: str) -> None:
    cand = json.load(open(candidates_path, encoding="utf-8"))
    doc = json.loads(Path(rules_path).read_text(encoding="utf-8"))
    existing = {r["id"]: r for r in doc["rules"]}
    ids = set(existing)

    # 1) 扩展现有规则（精确去重 + 前缀覆盖去重）
    for tid, ext in cand.get("extensions", {}).items():
        rule = existing[tid]
        added = 0
        for p in ext["paths"]:
            if len(rule["paths"]) >= 32:
                break
            if p.lower() in {x.lower() for x in rule["paths"]} or _covered(p, rule["paths"]):
                continue
            rule["paths"].append(p)
            added += 1
        for p in ext.get("exclude", []):
            rule.setdefault("exclude", [])
            if p.lower() not in {x.lower() for x in rule["exclude"]}:
                rule["exclude"].append(p)
        print(f"扩展 {tid}: +{added} paths")

    # 2) 追加新规则（id 冲突防御）
    added = 0
    for r in cand["rules"]:
        if r["id"] in ids:
            print(f"  !! id 冲突，跳过 {r['id']}")
            continue
        ids.add(r["id"])
        doc["rules"].append(r)
        added += 1
    doc["channel"] = "2026.09"

    Path(rules_path).write_text(json.dumps(doc, ensure_ascii=False, indent=1), encoding="utf-8")
    print(f"合并完成：新增 {added} 条，总计 {len(doc['rules'])} 条 -> {rules_path}")

    # 3) 同步 safety-notes.md（新规则条目）
    md = Path(rules_path).parent / "docs" / "safety-notes.md"
    if md.exists():
        lines = [md.read_text(encoding="utf-8").rstrip("\n")]
        for r in cand["rules"]:
            if r["id"] in existing:
                continue
            lines.append(f"\n## {r['id']}\n")
            lines.append(f"- **{r['name']}**（{r['category']}，风险 {r['risk']}）{r['safetyNotes']}")
            lines.append(f"- 验证状态：{r['verified']}。")
        md.write_text("\n".join(lines) + "\n", encoding="utf-8")
        print(f"safety-notes.md 追加 {added} 条")


def cmd_verify(rules_path: str, scan_path: str, date: str) -> None:
    hits = {}
    report = json.load(open(scan_path, encoding="utf-8"))
    results = report.get("Results", report.get("results", report if isinstance(report, list) else []))
    for res in results:
        rid = res.get("RuleId") or res.get("ruleId")
        if rid:
            hits[rid] = res.get("FileCount", res.get("fileCount", 0))
    doc = json.loads(Path(rules_path).read_text(encoding="utf-8"))
    n_hit = 0
    for r in doc["rules"]:
        cnt = hits.get(r["id"], 0)
        if cnt > 0:
            r["verified"] = f"本机实测 {date}（扫描命中 {cnt} 项）"
            n_hit += 1
        else:
            r["verified"] = UNVERIFIED
    Path(rules_path).write_text(json.dumps(doc, ensure_ascii=False, indent=1), encoding="utf-8")
    print(f"verify 完成：{n_hit} 条本机命中，{len(doc['rules']) - n_hit} 条未验证")


if __name__ == "__main__":
    if len(sys.argv) >= 4 and sys.argv[1] == "convert":
        cmd_convert(sys.argv[2], sys.argv[3])
    elif len(sys.argv) >= 4 and sys.argv[1] == "merge":
        cmd_merge(sys.argv[2], sys.argv[3])
    elif len(sys.argv) >= 5 and sys.argv[1] == "verify":
        cmd_verify(sys.argv[2], sys.argv[3], sys.argv[4])
    else:
        print(__doc__)
