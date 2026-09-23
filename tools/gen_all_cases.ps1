# Build the full 301-node case list from the inventory, optionally overlay curated cases, then split into chunks.
# ASCII-only on purpose (Windows PowerShell reads .ps1 as ANSI); the produced TSV is UTF8.
#   in : tools/node_inventory.tsv  (id \t name \t category \t inputs \t outputs)
#        tools/curated.tsv         (optional; same columns as batch_overrides.tsv)
#   out: tools/all_cases.tsv  +  tools/chunk_<i>.tsv
param(
    [int]$Chunks = 1,
    [string]$Inventory = "tools/node_inventory.tsv",
    [string]$Curated = "tools/curated.tsv",
    [string]$OutAll = "tools/all_cases.tsv",
    [string]$OutPrefix = "tools/chunk_"
)
$ErrorActionPreference = "Stop"
$root = (Get-Location).Path

# action-vs-value: a node whose id appears as `case "id":` in NodeDocRunner is an ActionNode
$runnerSrc = [System.IO.File]::ReadAllText((Join-Path $root "Assets/TcgEngine/Scripts/Workshop/Graph/NodeDocRunner.cs"), [System.Text.Encoding]::UTF8)
# 动作分派区间 = `private static void ExecuteAction(` 到"下一个类成员（8 空格缩进）"之间。
# 取值器里也写 `case "id":`，全文匹配会把取值节点误判成动作类（实测 102018 → 走动作通道 → 假通过）。
$actSpan = ""
$actStart = $runnerSrc.IndexOf("private static void ExecuteAction")
if ($actStart -ge 0) {
    $rest = $runnerSrc.Substring($actStart + 1)
    $mNext = [regex]::Match($rest, '(?m)^        (private|public|internal|protected) ')
    $actSpan = if ($mNext.Success) { $rest.Substring(0, $mNext.Index) } else { $rest }
}

$rows = @()
$count = 0
foreach ($line in (Get-Content (Join-Path $root $Inventory) -Encoding UTF8 | Select-Object -Skip 1)) {
    if (-not $line) { continue }
    $f = $line -split "`t"
    if ($f.Count -lt 5) { continue }
    $id = $f[0].Trim()
    $outs = @(($f[4] -split ',') | Where-Object { $_ })
    $dataOuts = @($outs | Where-Object { $_ -notmatch ':ActionNode$' -and $_ -notmatch ':Flow$' })
    $isCase = [regex]::IsMatch($actSpan, 'case\s+"' + [regex]::Escape($id) + '"\s*:')

    # default assert: value-class -> strict not-null (empty string does NOT count); action-class -> exec
    $assert = if ($isCase) { "exec" } else { "value_notnull" }
    $rows += ($id + "|" + $assert + "|||||auto")
    $count++
}
Write-Output ("auto rows = " + $count)

# ---- curated overlay: drop auto row for the same id, append curated rows (order preserved) ----
if ($Curated -and (Test-Path (Join-Path $root $Curated))) {
    $cur = @(Get-Content (Join-Path $root $Curated) -Encoding UTF8 | Where-Object { $_ -and -not $_.StartsWith("#") })
    $curIds = @{}
    foreach ($c in $cur) { $cid = ($c -split '\|')[0].Trim(); if ($cid) { $curIds[$cid] = $true } }
    $kept = @($rows | Where-Object { $cid = ($_ -split '\|')[0].Trim(); -not $curIds.ContainsKey($cid) })
    $rows = $kept + $cur
    Write-Output ("curated overlay = " + $cur.Count + " rows over " + $curIds.Count + " ids -> total " + $rows.Count)
}

$head = @()
$head += "# TITLE: full 301-node pass (auto defaults + curated overlay)"
$head += "# cols: id|assert|expect|fields|src1|dst1|note|src2|src2_const|child_action|child_field|child_value"
[System.IO.File]::WriteAllText((Join-Path $root $OutAll), (($head + $rows) -join "`r`n") + "`r`n", (New-Object System.Text.UTF8Encoding($false)))
Write-Output ("OK cases=" + $rows.Count + " -> " + $OutAll)

# ---- split into chunks ----
if ($Chunks -gt 1) {
    $per = [int][Math]::Ceiling($rows.Count / [double]$Chunks)
    for ($i = 0; $i -lt $Chunks; $i++) {
        $slice = @($rows | Select-Object -Skip ($i * $per) -First $per)
        if ($slice.Count -eq 0) { continue }
        $chunk = @()
        $chunk += ("# TITLE: full pass chunk " + ($i + 1) + " (" + $slice.Count + " cases)")
        $chunk += "# cols: id|assert|expect|fields|src1|dst1|note|src2|src2_const|child_action|child_field|child_value"
        $chunk += $slice
        $p = Join-Path $root ($OutPrefix + ($i + 1) + ".tsv")
        [System.IO.File]::WriteAllText($p, ($chunk -join "`r`n") + "`r`n", (New-Object System.Text.UTF8Encoding($false)))
        Write-Output ("  chunk " + ($i + 1) + ": " + $slice.Count + " -> " + $p)
    }
}
