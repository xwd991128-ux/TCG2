# =============================================================================
# 能力运行层基线比对（迁移验收判据）
#   用法：powershell -NoProfile -File tools/diff_runtime_baseline.ps1
#         （默认比对 tools/card_runtime_baseline.tsv(迁移前) vs tools/card_runtime_baseline_new.tsv(迁移后)）
#   判据：以 (卡, 能力, 触发, 目标模式) 为键，逐项比对"执行结果 + 状态差分"。
#        差异为 0 → 该批能力行为未变；有差异 → 逐条列出 前/后 观测值，人工定位。
# =============================================================================
param(
    [string]$Old = "tools/card_runtime_baseline.tsv",
    [string]$New = "tools/card_runtime_baseline_new.tsv",
    [string]$Out = "tools/baseline_diff.txt"
)
$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
Set-Location $root

function Load-Baseline($path) {
    $map = @{}
    if (-not (Test-Path $path)) { return $map }
    foreach ($ln in [System.IO.File]::ReadAllLines($path, [System.Text.Encoding]::UTF8)) {
        if ([string]::IsNullOrEmpty($ln) -or $ln.StartsWith("#")) { continue }
        $f = $ln -split "`t"
        if ($f.Count -lt 6) { continue }
        $key = $f[1] + "|" + $f[2] + "|" + $f[3] + "|" + $f[4]
        $map[$key] = [pscustomobject]@{ Result = $f[0]; Card = $f[1]; Abil = $f[2]; Trigger = $f[3]; Target = $f[4]; Delta = $f[5]; Note = $(if ($f.Count -ge 7) { $f[6] } else { "" }) }
    }
    return $map
}

$o = Load-Baseline $Old
$n = Load-Baseline $New
if ($o.Count -eq 0) { Write-Host ("[错误] 旧基线为空或不存在：" + $Old); exit 1 }
if ($n.Count -eq 0) { Write-Host ("[错误] 新基线为空或不存在：" + $New); exit 1 }

$sb = New-Object System.Text.StringBuilder
[void]$sb.AppendLine("# 能力运行层基线比对  " + (Get-Date -Format "yyyy-MM-dd HH:mm:ss"))
[void]$sb.AppendLine("# 旧：" + $Old + "（" + $o.Count + " 例）")
[void]$sb.AppendLine("# 新：" + $New + "（" + $n.Count + " 例）")

$missing = @(); $added = @(); $resultChanged = @(); $deltaChanged = @()
foreach ($k in $o.Keys) {
    if (-not $n.ContainsKey($k)) { $missing += $k; continue }
    $a = $o[$k]; $b = $n[$k]
    if ($a.Result -ne $b.Result) { $resultChanged += $k }
    elseif ($a.Delta -ne $b.Delta) { $deltaChanged += $k }
}
foreach ($k in $n.Keys) { if (-not $o.ContainsKey($k)) { $added += $k } }

$same = $o.Count - $missing.Count - $resultChanged.Count - $deltaChanged.Count
Write-Host ("旧基线 " + $o.Count + " 例；行为一致 " + $same + " 例")
Write-Host ("差异：结果变化 " + $resultChanged.Count + "｜状态差分变化 " + $deltaChanged.Count + "｜旧有新无 " + $missing.Count + "｜新增 " + $added.Count)
Write-Host ""

if ($deltaChanged.Count -gt 0) {
    Write-Host "=== 状态差分变化（迁移引起的行为差异，需逐条定位）==="
    [void]$sb.AppendLine("`n=== 状态差分变化 ===")
    foreach ($k in ($deltaChanged | Sort-Object)) {
        $a = $o[$k]; $b = $n[$k]
        Write-Host ("  " + $a.Card + " / " + $a.Abil + " (" + $a.Trigger + "/" + $a.Target + ")")
        Write-Host ("      旧：" + $a.Delta)
        Write-Host ("      新：" + $b.Delta)
        [void]$sb.AppendLine($a.Card + "`t" + $a.Abil + "`t" + $a.Trigger + "/" + $a.Target + "`n  旧: " + $a.Delta + "`n  新: " + $b.Delta)
    }
}
if ($resultChanged.Count -gt 0) {
    Write-Host "=== 执行结果变化（ok/skip/err）==="
    [void]$sb.AppendLine("`n=== 执行结果变化 ===")
    foreach ($k in ($resultChanged | Sort-Object)) {
        Write-Host ("  " + $o[$k].Card + " / " + $o[$k].Abil + "：" + $o[$k].Result + " → " + $n[$k].Result)
        [void]$sb.AppendLine($o[$k].Card + "`t" + $o[$k].Abil + "`t" + $o[$k].Result + " → " + $n[$k].Result)
    }
}
if ($missing.Count -gt 0) {
    Write-Host "=== 旧有新无（用例消失，通常是能力 id 变了）==="
    [void]$sb.AppendLine("`n=== 旧有新无 ===")
    foreach ($k in ($missing | Sort-Object)) { Write-Host ("  " + $k); [void]$sb.AppendLine($k) }
}
if ($added.Count -gt 0) {
    Write-Host "=== 新增用例（迁移后多出来的，确认是否预期）==="
    [void]$sb.AppendLine("`n=== 新增用例 ===")
    foreach ($k in ($added | Sort-Object)) { Write-Host ("  " + $k); [void]$sb.AppendLine($k) }
}

[System.IO.File]::WriteAllText((Join-Path $root $Out), $sb.ToString(), (New-Object System.Text.UTF8Encoding($false)))
Write-Host ""
if ($resultChanged.Count -eq 0 -and $deltaChanged.Count -eq 0 -and $missing.Count -eq 0) {
    Write-Host "★ 判据通过：所有用例行为一致（明细见 $Out）"
    exit 0
} else {
    Write-Host "✗ 判据未通过：有差异（明细见 $Out）"
    exit 2
}
