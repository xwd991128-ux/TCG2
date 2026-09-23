# =============================================================================
# Phase 1 审计：内置能力系统 → 节点库 覆盖对照
#   产出（tools/ 下）：
#     audit_class_freq.tsv        Effect/Condition/Filter/Status 类的使用频次（含涉及能力数/卡数）
#     audit_trigger_target_freq.tsv  AbilityTrigger / AbilityTarget 使用频次
#     audit_field_usage.tsv        AbilityData 字段使用面（FX / multi_target / target_slots / status / chain…）
#     audit_orphan_abilities.tsv   没有被任何卡引用的能力（可安全忽略/清理）
#   只读：不修改任何资产。
# =============================================================================
$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
Set-Location $root

function Read-Text($p) { [System.IO.File]::ReadAllText($p, [System.Text.Encoding]::UTF8) }
function Get-Guid($p) { $m = [regex]::Match((Read-Text $p), "guid:\s*([0-9a-fA-F]+)"); if ($m.Success) { $m.Groups[1].Value } else { "" } }

# ---------- 1. guid → 脚本类名（.cs.meta） ----------
$guid2class = @{}
Get-ChildItem "Assets\TcgEngine\Scripts" -Recurse -Filter *.cs.meta | ForEach-Object {
    $g = Get-Guid $_.FullName
    if ($g) { $guid2class[$g] = ($_.Name -replace '\.cs\.meta$', '') }
}
Write-Host ("[1] 脚本类名索引：" + $guid2class.Count + " 条")

# ---------- 2. 资产 guid → 路径 / 类名 / 目录 ----------
$assetInfo = @{}
Get-ChildItem "Assets\TcgEngine\Resources" -Recurse -Filter *.asset | ForEach-Object {
    $meta = $_.FullName + ".meta"
    if (-not (Test-Path $meta)) { return }
    $g = Get-Guid $meta
    if (-not $g) { return }
    $txt = Read-Text $_.FullName
    $m = [regex]::Match($txt, "m_Script:\s*\{fileID:\s*11500000,\s*guid:\s*([0-9a-fA-F]+)")
    $cls = if ($m.Success -and $guid2class.ContainsKey($m.Groups[1].Value)) { $guid2class[$m.Groups[1].Value] } else { "" }
    $rel = $_.FullName.Substring($root.Length + 1)
    $parts = $rel -split '\\'
    $folder = if ($parts.Count -ge 5) { $parts[3] } else { "" }
    $assetInfo[$g] = [pscustomobject]@{ Guid = $g; Path = $rel; Class = $cls; Folder = $folder; Name = $_.BaseName }
}
Write-Host ("[2] 资产索引：" + $assetInfo.Count + " 条")

# ---------- 3. YAML 解析：取每个 key 下的 guid 列表 + 标量 ----------
function Parse-Asset($path) {
    $keys = @{}; $scalars = @{}
    $cur = ""
    foreach ($line in [System.IO.File]::ReadAllLines($path, [System.Text.Encoding]::UTF8)) {
        if ($line -match '^\s{2}([A-Za-z_][A-Za-z0-9_]*):\s*(.*)$') {
            $cur = $Matches[1]; $rest = $Matches[2].Trim()
            if (-not $keys.ContainsKey($cur)) { $keys[$cur] = New-Object System.Collections.ArrayList }
            if ($rest -match 'guid:\s*([0-9a-fA-F]+)') { [void]$keys[$cur].Add($Matches[1]) }
            elseif ($rest -ne '' -and $rest -ne '[]' -and $rest -ne '{}') { $scalars[$cur] = $rest }
        }
        elseif ($line -match '^\s{2}-\s*(.*)$' -and $cur -ne '') {
            if ($Matches[1] -match 'guid:\s*([0-9a-fA-F]+)') { [void]$keys[$cur].Add($Matches[1]) }
        }
    }
    return [pscustomobject]@{ Keys = $keys; Scalars = $scalars }
}

# ---------- 4. 枚举名（AbilityTrigger / AbilityTarget） ----------
function Parse-Enum($file, $enumName) {
    $lines = [System.IO.File]::ReadAllLines($file, [System.Text.Encoding]::UTF8)
    $map = @{}; $started = $false; $idx = -1
    foreach ($l in $lines) {
        if (-not $started) { if ($l -match ("enum\s+" + [regex]::Escape($enumName) + "\b")) { $started = $true }; continue }
        if ($l -match '^\s{4}\}') { break }
        $m = [regex]::Match($l, '^\s*([A-Za-z_][A-Za-z0-9_]*)\s*(=\s*(\d+))?')
        if ($m.Success) {
            if ($m.Groups[3].Success) { $idx = [int]$m.Groups[3].Value } else { $idx++ }
            $map[$idx] = $m.Groups[1].Value
        }
    }
    return $map
}
$abilityCs = "Assets\TcgEngine\Scripts\Data\AbilityData.cs"
$trigNames = Parse-Enum $abilityCs "AbilityTrigger"
$targNames = Parse-Enum $abilityCs "AbilityTarget"
Write-Host ("[3] 枚举：AbilityTrigger " + $trigNames.Count + " 项，AbilityTarget " + $targNames.Count + " 项")

# ---------- 5. 扫全部 AbilityData ----------
$abil = @{}   # guid -> object
Get-ChildItem "Assets\TcgEngine\Resources\Abilities" -Recurse -Filter *.asset | ForEach-Object {
    $g = Get-Guid ($_.FullName + ".meta"); if (-not $g) { return }
    $p = Parse-Asset $_.FullName
    $abil[$g] = [pscustomobject]@{
        Guid = $g; Path = $_.FullName.Substring($root.Length + 1); Id = $_.BaseName
        Keys = $p.Keys; Scalars = $p.Scalars
        Cards = New-Object System.Collections.ArrayList
    }
}
Write-Host ("[4] AbilityData：" + $abil.Count + " 个")

# ---------- 6. 扫全部 CardData：卡 → 能力 ----------
$cardCount = 0
Get-ChildItem "Assets\TcgEngine\Resources\Cards" -Recurse -Filter *.asset | ForEach-Object {
    $cardCount++
    $p = Parse-Asset $_.FullName
    if ($p.Keys.ContainsKey("abilities")) {
        foreach ($ag in $p.Keys["abilities"]) { if ($abil.ContainsKey($ag)) { [void]$abil[$ag].Cards.Add($_.BaseName) } }
    }
}
Write-Host ("[5] CardData：" + $cardCount + " 张")

# ---------- 7. 聚合：类 → 引用次数 / 能力数 / 卡数 ----------
$agg = @{}
function Add-Ref($kind, $cls, $abilId, $cards) {
    if (-not $cls) { $cls = "(未解析)" }
    $k = "$kind|$cls"
    if (-not $agg.ContainsKey($k)) {
        $agg[$k] = [pscustomobject]@{ Kind = $kind; Class = $cls; Refs = 0; Abils = (New-Object System.Collections.Generic.HashSet[string]); Cards = (New-Object System.Collections.Generic.HashSet[string]) }
    }
    $agg[$k].Refs++
    [void]$agg[$k].Abils.Add($abilId)
    foreach ($c in $cards) { [void]$agg[$k].Cards.Add($c) }
}
$kindOfFolder = @{ "Effects" = "Effect"; "Conditions" = "Condition"; "Status" = "Status" }

# 字段使用统计
$fu = [ordered]@{ abil_total = 0; has_fx = 0; has_audio = 0; multi_target = 0; target_slots = 0; has_status = 0; has_chain = 0; cond_trigger = 0; cond_target = 0; filters = 0; no_effect = 0; value_nonzero = 0; mana_cost = 0; exhaust = 0 }
$trigFreq = @{}; $targFreq = @{}

foreach ($a in $abil.Values) {
    $fu.abil_total++
    foreach ($fx in @("board_fx", "caster_fx", "target_fx", "projectile_fx")) {
        if ($a.Keys.ContainsKey($fx) -and $a.Keys[$fx].Count -gt 0) { $fu.has_fx++; break }
    }
    foreach ($au in @("cast_audio", "target_audio")) {
        if ($a.Keys.ContainsKey($au) -and $a.Keys[$au].Count -gt 0) { $fu.has_audio++; break }
    }
    if ($a.Scalars.ContainsKey("multi_target") -and $a.Scalars["multi_target"] -match '[1-9]') { $fu.multi_target++ }
    if ($a.Keys.ContainsKey("target_slots") -and $a.Keys["target_slots"].Count -gt 0) { $fu.target_slots++ }
    if ($a.Keys.ContainsKey("status") -and $a.Keys["status"].Count -gt 0) { $fu.has_status++ }
    if ($a.Keys.ContainsKey("chain_abilities") -and $a.Keys["chain_abilities"].Count -gt 0) { $fu.has_chain++ }
    if ($a.Keys.ContainsKey("conditions_trigger") -and $a.Keys["conditions_trigger"].Count -gt 0) { $fu.cond_trigger++ }
    if ($a.Keys.ContainsKey("conditions_target") -and $a.Keys["conditions_target"].Count -gt 0) { $fu.cond_target++ }
    if ($a.Keys.ContainsKey("filters_target") -and $a.Keys["filters_target"].Count -gt 0) { $fu.filters++ }
    if (-not ($a.Keys.ContainsKey("effects") -and $a.Keys["effects"].Count -gt 0)) { $fu.no_effect++ }
    if ($a.Scalars.ContainsKey("effects")) { }
    if ($a.Scalars.ContainsKey("value") -and $a.Scalars["value"] -match '[1-9]') { $fu.value_nonzero++ }
    if ($a.Scalars.ContainsKey("mana_cost") -and $a.Scalars["mana_cost"] -match '[1-9]') { $fu.mana_cost++ }
    if ($a.Scalars.ContainsKey("exhaust") -and $a.Scalars["exhaust"] -match '[1-9]') { $fu.exhaust++ }

    # trigger / target
    $t = if ($a.Scalars.ContainsKey("trigger")) { ($a.Scalars["trigger"] -replace '\D', '') } else { "0" }
    $tn = if ($trigNames.ContainsKey([int]$t)) { $trigNames[[int]$t] } else { "?" }
    if (-not $trigFreq.ContainsKey($tn)) { $trigFreq[$tn] = 0 }; $trigFreq[$tn]++
    $tg = if ($a.Scalars.ContainsKey("target")) { ($a.Scalars["target"] -replace '\D', '') } else { "0" }
    $tgn = if ($targNames.ContainsKey([int]$tg)) { $targNames[[int]$tg] } else { "?" }
    if (-not $targFreq.ContainsKey($tgn)) { $targFreq[$tgn] = 0 }; $targFreq[$tgn]++

    # 类引用
    foreach ($k in @("effects", "conditions_trigger", "conditions_target", "filters_target", "status")) {
        if (-not $a.Keys.ContainsKey($k)) { continue }
        foreach ($refGuid in $a.Keys[$k]) {
            if (-not $assetInfo.ContainsKey($refGuid)) { Add-Ref "Unknown" "?" $a.Id $a.Cards; continue }
            $info = $assetInfo[$refGuid]
            $kind = if ($kindOfFolder.ContainsKey($info.Folder)) { $kindOfFolder[$info.Folder] } else { $info.Folder }
            if ($info.Folder -eq "Conditions") {
                if ($info.Class -like "Filter*") { $kind = "Filter" } else { $kind = "Condition" }
            }
            $cls = if ($info.Class) { $info.Class } else { "(无脚本)" + $info.Name }
            Add-Ref $kind $cls $a.Id $a.Cards
        }
    }
}

# ---------- 8. 输出 ----------
$out = New-Object System.Collections.ArrayList
foreach ($v in $agg.Values) {
    [void]$out.Add([pscustomobject]@{
        Kind = $v.Kind; Class = $v.Class; Refs = $v.Refs; AbilCount = $v.Abils.Count; CardCount = $v.Cards.Count
        SampleCards = (($v.Cards | Select-Object -First 5) -join ",")
    })
}
$out = $out | Sort-Object -Property @{ Expression = "Kind"; Descending = $false }, @{ Expression = "Refs"; Descending = $true }
$sb = New-Object System.Text.StringBuilder
[void]$sb.AppendLine("kind`tclass`trefs`tabilities`tcards`tsample_cards")
foreach ($r in $out) { [void]$sb.AppendLine(($r.Kind + "`t" + $r.Class + "`t" + $r.Refs + "`t" + $r.AbilCount + "`t" + $r.CardCount + "`t" + $r.SampleCards)) }
[System.IO.File]::WriteAllText((Join-Path $root "tools\audit_class_freq.tsv"), $sb.ToString(), (New-Object System.Text.UTF8Encoding($false)))

$sb2 = New-Object System.Text.StringBuilder
[void]$sb2.AppendLine("trigger`tcount")
foreach ($k in ($trigFreq.Keys | Sort-Object { -$trigFreq[$_] })) { [void]$sb2.AppendLine($k + "`t" + $trigFreq[$k]) }
[void]$sb2.AppendLine("target`tcount")
foreach ($k in ($targFreq.Keys | Sort-Object { -$targFreq[$_] })) { [void]$sb2.AppendLine($k + "`t" + $targFreq[$k]) }
[System.IO.File]::WriteAllText((Join-Path $root "tools\audit_trigger_target_freq.tsv"), $sb2.ToString(), (New-Object System.Text.UTF8Encoding($false)))

$sb3 = New-Object System.Text.StringBuilder
[void]$sb3.AppendLine("field`tcount")
foreach ($k in $fu.Keys) { [void]$sb3.AppendLine($k + "`t" + $fu[$k]) }
[System.IO.File]::WriteAllText((Join-Path $root "tools\audit_field_usage.tsv"), $sb3.ToString(), (New-Object System.Text.UTF8Encoding($false)))

$sb4 = New-Object System.Text.StringBuilder
[void]$sb4.AppendLine("ability_id`tpath`treferenced_by_cards")
$orphan = 0
foreach ($a in ($abil.Values | Sort-Object Id)) {
    if ($a.Cards.Count -eq 0) { $orphan++; [void]$sb4.AppendLine($a.Id + "`t" + $a.Path + "`t0") }
}
[System.IO.File]::WriteAllText((Join-Path $root "tools\audit_orphan_abilities.tsv"), $sb4.ToString(), (New-Object System.Text.UTF8Encoding($false)))

# ---------- 9. 控制台摘要 ----------
Write-Host ""
Write-Host "=== 类使用频次（按 kind 汇总）==="
foreach ($g in ($out | Group-Object Kind)) {
    Write-Host ("  " + $g.Name + "：类数=" + $g.Count + "，引用总次数=" + (($g.Group | Measure-Object -Property Refs -Sum).Sum))
}
Write-Host ""
Write-Host "=== Top 20 高频类 ==="
$out | Sort-Object -Property Refs -Descending | Select-Object -First 20 | ForEach-Object { Write-Host ("  " + $_.Kind.PadRight(9) + $_.Class.PadRight(28) + " refs=" + $_.Refs + " abil=" + $_.AbilCount + " cards=" + $_.CardCount) }
Write-Host ""
Write-Host "=== 字段使用面 ==="
foreach ($k in $fu.Keys) { Write-Host ("  " + $k.PadRight(16) + " = " + $fu[$k]) }
Write-Host ""
Write-Host ("=== 未被任何卡引用的能力 = " + $orphan + " 个（见 audit_orphan_abilities.tsv）")
Write-Host "产出：tools/audit_class_freq.tsv / audit_trigger_target_freq.tsv / audit_field_usage.tsv / audit_orphan_abilities.tsv"
