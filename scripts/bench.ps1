# Kleaner 性能基线一键验收脚本（工单 15）。
# 口径见 docs/performance-baseline.md；结果 JSON 落在 docs/performance/ 下。
# 用法：pwsh ./scripts/bench.ps1 [-Files 2000] [-Iterations 5] [-ColdStarts 5]
param(
    [int]$Files = 2000,
    [int]$Iterations = 5,
    [int]$ColdStarts = 5,
    [string]$OutDir = "docs/performance"
)
$ErrorActionPreference = "Stop"
Set-Location (Join-Path $PSScriptRoot "..")

Write-Host "== 构建 Release =="
dotnet build Kleaner.slnx -c Release | Out-Null
if ($LASTEXITCODE -ne 0) { throw "构建失败" }

$cli = Join-Path "tools/Kleaner.ScanCli/bin/Release/net10.0-windows" "Kleaner.ScanCli.exe"
$app = Resolve-Path "src/Kleaner.App/bin/Release/net10.0-windows/Kleaner.App.exe"
$root = Join-Path $env:TEMP ("kleaner-bench-" + [guid]::NewGuid().ToString("N"))

Write-Host "== 生成合成数据集（$Files 个文件） =="
& $cli gen-dataset --root $root --files $Files
if ($LASTEXITCODE -ne 0) { throw "数据集生成失败" }

Write-Host "== 运行基准（每场景 $Iterations 轮） =="
$benchResult = Join-Path $root "bench-result.json"
$benchRules = Join-Path $root "bench-rules.json"
# --rules 把 rules-scan / cancel 锚定在合成数据集上；不传则回退真实规则库扫描真实目录（跨日不可比）
& $cli bench --root $root --rules $benchRules --iterations $Iterations --out $benchResult | Out-Null
if ($LASTEXITCODE -ne 0) { throw "基准运行失败" }

Write-Host "== 冷启动测量（$ColdStarts 次，口径：进程启动到主窗口句柄出现） =="
$coldStartMs = @()
$appPath = $app.ToString()
1..$ColdStarts | ForEach-Object {
    $sw = [System.Diagnostics.Stopwatch]::StartNew()
    $proc = Start-Process -FilePath $appPath -PassThru
    try
    {
        while ($sw.ElapsedMilliseconds -lt 60000)
        {
            $proc.Refresh()
            if ($proc.MainWindowHandle -ne 0) { break }
            Start-Sleep -Milliseconds 25
        }
        $sw.Stop()
        if ($proc.MainWindowHandle -eq 0) { throw "60 秒内未出现主窗口" }
        $coldStartMs += $sw.ElapsedMilliseconds
    }
    finally
    {
        if (-not $proc.HasExited) { Stop-Process -Id $proc.Id -Force }
    }
    Start-Sleep -Milliseconds 800
}

$os = Get-CimInstance Win32_OperatingSystem
$cpu = Get-CimInstance Win32_Processor | Select-Object -First 1
$bench = Get-Content $benchResult -Raw | ConvertFrom-Json
$coldSorted = $coldStartMs | Sort-Object
$coldP95 = $coldSorted[[Math]::Min($coldSorted.Count - 1, [Math]::Ceiling(0.95 * $coldSorted.Count) - 1)]

$environment = [ordered]@{
    machine       = $env:COMPUTERNAME
    os            = $os.Caption
    osBuild       = $os.BuildNumber
    cpu           = $cpu.Name.Trim()
    cores         = [int]$cpu.NumberOfCores
    memoryGb      = [math]::Round($os.TotalVisibleMemorySize / 1MB, 1)
    dotnetSdk     = (dotnet --version)
    timestampUtc  = (Get-Date).ToUniversalTime().ToString("o")
}
$summary = [ordered]@{
    environment  = $environment
    dataset      = $bench.dataset
    rulesPath    = $bench.rulesPath
    scenarios    = $bench.scenarios
    coldStartMs  = $coldStartMs
    coldStartP95 = $coldP95
}

New-Item -ItemType Directory -Force -Path $OutDir | Out-Null
$stamp = (Get-Date).ToUniversalTime().ToString("yyyyMMdd-HHmmss")
$outFile = Join-Path $OutDir "performance-run-$stamp.json"
# UTF-8 无 BOM：PS 5.1 的 Set-Content -Encoding UTF8 会写 BOM，干扰部分 JSON 消费方
[IO.File]::WriteAllText($outFile, ($summary | ConvertTo-Json -Depth 6), (New-Object System.Text.UTF8Encoding($false)))

Write-Host ""
Write-Host "== 摘要 =="
Write-Host "数据集：$($bench.dataset.FileCount) 个文件 / $([math]::Round($bench.dataset.TotalBytes / 1MB, 1)) MB / 种子 $($bench.dataset.Seed)"
foreach ($s in $bench.scenarios)
{
    Write-Host ("{0,-12} p50={1,10:F1} ms   p95={2,10:F1} ms   峰值内存={3,8:F1} MB" -f `
        $s.name, $s.p50Ms, $s.p95Ms, ($s.peakWorkingSetBytes / 1MB))
}
Write-Host ("{0,-12} p95={1,10:F0} ms（{2} 次样本：{3}）" -f "冷启动", $coldP95, $coldStarts, ($coldStartMs -join ", "))
Write-Host "结果已写入：$outFile"

Remove-Item $root -Recurse -Force -ErrorAction SilentlyContinue
