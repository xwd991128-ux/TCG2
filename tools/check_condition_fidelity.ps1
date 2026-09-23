# 条件对账（临时脚本）：编译冒烟里的条件 vs 迁移前基线 card_baseline.tsv
# 只比"类名多重集"（数值字段格式两边不同，见下方 NOTE），用于验证数据型条件是否逐条还原。
$ErrorActionPreference = "Continue"
$root = Split-Path -Parent $MyInvocation.MyCommand.Path

function Names($s) {
    if ([string]::IsNullOrWhiteSpace($s)) { return "" }
    $out = @()
    foreach ($p in ($s -split ' \+ ')) {
        $t = ($p -split '\{')[0].Trim()
        if ($t.Length -gt 0) { $out += $t }
    }
    return ($out -join '+')
}

$base = @{}
foreach ($ln in [IO.File]::ReadAllLines((Join-Path $root 'card_baseline.tsv'), [Text.Encoding]::UTF8)) {
    if ($ln -notmatch "`t") { continue }
    $f = $ln -split "`t"
    if ($f.Count -lt 19) { continue }
    if ($f[0] -eq 'card_id') { continue }
    $base[$f[7] + '|' + $f[0]] = @{ ct = $f[16]; ctg = $f[17] }
}

$okByCard = @{}
foreach ($ln in [IO.File]::ReadAllLines((Join-Path $root 'converter_report.tsv'), [Text.Encoding]::UTF8)) {
    if ($ln -like '#*' -or $ln -notmatch "`t") { continue }
    $f = $ln -split "`t"
    #ok=转成图；data=数据直通（含纯状态）——两者都要对账，否则数据直通卡（能力 id 与基线同名）永远没人比
    if ($f.Count -lt 6 -or ($f[4] -ne 'ok' -and $f[4] -ne 'data')) { continue }
    if (-not $okByCard.ContainsKey($f[0])) { $okByCard[$f[0]] = @() }
    $okByCard[$f[0]] += $f[1]
}

$smoke = [ordered]@{}
foreach ($ln in [IO.File]::ReadAllLines((Join-Path $root 'pool_compile_smoke.tsv'), [Text.Encoding]::UTF8)) {
    if ($ln -like '#*' -or $ln -notmatch "`t") { continue }
    $f = $ln -split "`t"
    if ($f.Count -lt 10) { continue }
    if ($f[2] -like '<*') { continue }   # 图摘要行(<效果图>)/异常行不是能力行
    if (-not $smoke.Contains($f[0])) { $smoke[$f[0]] = @() }
    $smoke[$f[0]] += , $f
}

$bad = 0
$checked = 0
$dataChecked = 0
foreach ($card in $smoke.Keys) {
    $ids = $okByCard[$card]
    $rows = $smoke[$card]
    if ($null -eq $ids) { Write-Output ("NO-OK-IDS " + $card); $bad++; continue }

    #① 先按**能力 id** 直接匹配：数据直通（Ongoing/装备/纯状态）编译后 id 与基线同名 → 可逐条精确对账
    $used = @{}
    $rest = @()
    foreach ($r in $rows) {
        $rid = $r[2]
        if ($ids -contains $rid) {
            $b = $base[$rid + '|' + $card]
            if ($null -eq $b) { Write-Output ("NO-BASELINE " + $card + " " + $rid); $bad++; continue }
            $checked++
            $dataChecked++
            $bct = Names $b.ct
            $sct = Names $r[5]
            $bctg = Names $b.ctg
            $sctg = Names $r[6]
            if ($bct -ne $sct) { Write-Output ("TRIG-DIFF " + $card + "/" + $rid + " baseline=[" + $bct + "] compiled=[" + $sct + "]"); $bad++ }
            if ($bctg -ne $sctg) { Write-Output ("TGT-DIFF  " + $card + "/" + $rid + " baseline=[" + $bctg + "] compiled=[" + $sctg + "]"); $bad++ }
            $used[$rid] = 1
        } else {
            $rest += , $r
        }
    }

    #② 剩下的是图编译能力（id = graph_*_node*，与基线不同名）→ 与"没用掉的 ok/data id"按顺序配对
    $free = @($ids | Where-Object { -not $used.ContainsKey($_) })
    if ($free.Count -ne $rest.Count) {
        Write-Output ("COUNT-DIFF " + $card + " ok=" + $ids.Count + " 按id匹配=" + $used.Count + " 剩余行=" + $rest.Count + " 剩余id=" + $free.Count)
        $bad++
        continue
    }
    for ($i = 0; $i -lt $rest.Count; $i++) {
        $b = $base[$free[$i] + '|' + $card]
        if ($null -eq $b) { Write-Output ("NO-BASELINE " + $card + " " + $free[$i]); $bad++; continue }
        $checked++
        $r = $rest[$i]
        $bct = Names $b.ct
        $sct = Names $r[5]
        $bctg = Names $b.ctg
        $sctg = Names $r[6]
        if ($bct -ne $sct) { Write-Output ("TRIG-DIFF " + $card + "/" + $free[$i] + " baseline=[" + $bct + "] compiled=[" + $sct + "]"); $bad++ }
        if ($bctg -ne $sctg) { Write-Output ("TGT-DIFF  " + $card + "/" + $free[$i] + " baseline=[" + $bctg + "] compiled=[" + $sctg + "]"); $bad++ }
    }
}
Write-Output ("checked=" + $checked + "（其中按 id 精确匹配=" + $dataChecked + "）mismatch=" + $bad)
