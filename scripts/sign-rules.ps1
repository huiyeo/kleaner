# Kleaner 规则清单签名脚本（工单 12）。
# 项目所有者在本机运行：读取私钥对当前规则库签名，产出 rules-manifest.json。
# 私钥只留在所有者机器（默认 %USERPROFILE%\.kleaner-signing\rules-private.pem），
# 绝不入仓库或客户端；丢失后须换钥并发新应用版本。
# 用法：pwsh ./scripts/sign-rules.ps1 [-Version 1.0.0] [-MinAppVersion 0.2.4]
param(
    [string]$Version = "1.0.0",
    [string]$MinAppVersion = "0.2.4",
    [string]$KeyPath = "$env:USERPROFILE\.kleaner-signing\rules-private.pem",
    [string]$RulesPath = "rules/rules.v1.json",
    [string]$OutDir = "releases"
)
$ErrorActionPreference = "Stop"
Set-Location (Join-Path $PSScriptRoot "..")

if (-not (Test-Path $KeyPath)) { throw "私钥不存在：$KeyPath" }
$published = (Get-Date).ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss'Z'")

# 摘要与发布副本一律使用 LF 形式：仓库 blob 与 raw 下载都是 LF，CRLF 工作区直接签名会导致
# 线上摘要永远对不上（2026-09-12 v1.2.0 首发踩坑重签）。先归一化到临时 LF 文件再参与签名。
$rulesText = [Text.Encoding]::UTF8.GetString([IO.File]::ReadAllBytes($RulesPath)).Replace("`r`n", "`n")
$lfFile = Join-Path ([IO.Path]::GetTempPath()) ("kleaner-rules-lf-" + [guid]::NewGuid().ToString("N") + ".json")
[IO.File]::WriteAllText($lfFile, $rulesText, [Text.UTF8Encoding]::new($false))
$sha = (Get-FileHash -Algorithm SHA512 $lfFile).Hash.ToLower()

$canonical = "kleaner-rules-manifest v1`n$Version`n$published`n$MinAppVersion`n$sha"
$tmp = New-Item -ItemType Directory -Force -Path (Join-Path $env:TEMP ("kleaner-sign-" + [guid]::NewGuid().ToString("N")))
$canonicalFile = Join-Path $tmp "canonical.bin"
$sigFile = Join-Path $tmp "signature.bin"
[IO.File]::WriteAllBytes($canonicalFile, [Text.Encoding]::UTF8.GetBytes($canonical))

& openssl pkeyutl -sign -inkey $KeyPath -rawin -in $canonicalFile -out $sigFile
if ($LASTEXITCODE -ne 0) { throw "openssl 签名失败" }
$signature = [Convert]::ToBase64String([IO.File]::ReadAllBytes($sigFile))

$manifest = [ordered]@{
    version       = $Version
    publishedUtc  = $published
    minAppVersion = $MinAppVersion
    rulesSha512   = $sha
    signature     = $signature
}
New-Item -ItemType Directory -Force -Path $OutDir | Out-Null
$outFile = Join-Path $OutDir "rules-manifest.json"
[IO.File]::WriteAllText($outFile, ($manifest | ConvertTo-Json), [Text.UTF8Encoding]::new($false))

Write-Host "清单已签名并写入：$outFile"
Write-Host "  版本 $Version / 最低应用 $MinAppVersion"
Write-Host "  规则 SHA512：$sha"
Copy-Item $lfFile (Join-Path $OutDir "rules.v1.json") -Force
Remove-Item $lfFile -Force
Write-Host "发布：将 $outFile 与 $(Join-Path $OutDir 'rules.v1.json') 上传到 rules-channel 分支（见 docs/release-checklist.md）。"
Remove-Item $tmp.FullName -Recurse -Force
