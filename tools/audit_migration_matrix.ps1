# =============================================================================
# Phase 1 审计 v2：可达性闭包 + 逐卡迁移矩阵 + 未解析引用
#   产出（tools/ 下）：
#     card_migration_matrix.tsv  逐卡施工单：入口/效果类/条件/过滤/复杂度/建议批次
#     audit_reachability.tsv     能力可达性（直接引用 vs 链式可达 vs 真孤儿）
#     audit_unknown_refs.tsv     解析不到的 guid 引用（排查用）
#   只读：不修改任何资产。
# =============================================================================
$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
Set-Location $root

function Read-Text($p) { [System.IO.File]::ReadAllText($p, [System.Text.Encoding]::UTF8) }
function Get-Guid($p) { $m = [regex]::Match((Read-Text $p), "guid:\s*([0-9a-fA-F]+)"); if ($m.Success) { $m.Groups[1].Value } else { "" } }

# ---------- 索引 ----------
$guid2class = @{}
Get-ChildItem "Assets\TcgEngine\Scripts" -Recurse -Filter *.cs.meta | ForEach-Object {
    $g = Get-Guid $_.FullName; if ($g) { $guid2class[$g] = ($_.Name -replace '\.cs\.meta$', '') }
}
$assetInfo = @{}
Get-ChildItem "Assets\TcgEngine\Resources" -Recurse -Filter *.asset | ForEach-Object {
    $meta = $_.FullName + ".meta"; if (-not (Test-Path $meta)) { return }
    $g = Get-Guid $meta; if (-not $g) { return }
    $m = [regex]::Match((Read-Text $_.FullName), "m_Script:\s*\{fileID:\s*11500000,\s*guid:\s*([0-9a-fA-F]+)")
    $cls = if ($m.Success -and $guid2class.ContainsKey($m.Groups[1].Value)) { $guid2class[$m.Groups[1].Value] } else { "" }
    $rel = $_.FullName.Substring($root.Length + 1); $parts = $rel -split '\\'
    $folder = if ($parts.Count -ge 5) { $parts[3] } else { "" }
    $assetInfo[$g] = [pscustomobject]@{ Guid = $g; Path = $rel; Class = $cls; Folder = $folder; Name = $_.BaseName }
}

function Parse-Asset($path) {
    $keys = @{}; $scalars = @{}; $cur = ""
    foreach ($line in [System.IO.File]::ReadAllLines($path, [System.Text.Encoding]::UTF8)) {
        if ($line -match '^\s{2}([A-Za-z_][A-Za-z0-9_]*):\s*(.*)$') {
            $cur = $Matches[1]; $rest = $Matches[2].Trim()
            if ($cur -like 'm_*') { continue }   # m_Script / m_GameObject… 是 YAML 头，不是业务字段（否则污染未解析引用清单）
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
function Parse-Enum($file, $enumName) {
    $map = @{}; $started = $false; $idx = -1
    foreach ($l in [System.IO.File]::ReadAllLines($file, [System.Text.Encoding]::UTF8)) {
        if (-not $started) { if ($l -match ("enum\s+" + [regex]::Escape($enumName) + "\b")) { $started = $true }; continue }
        if ($l -match '^\s{4}\}') { break }
        $m = [regex]::Match($l, '^\s*([A-Za-z_][A-Za-z0-9_]*)\s*(=\s*(\d+))?')
        if ($m.Success) { if ($m.Groups[3].Success) { $idx = [int]$m.Groups[3].Value } else { $idx++ }; $map[$idx] = $m.Groups[1].Value }
    }
    return $map
}
$trigNames = Parse-Enum "Assets\TcgEngine\Scripts\Data\AbilityData.cs" "AbilityTrigger"
$typeNames = Parse-Enum "Assets\TcgEngine\Scripts\Data\CardData.cs" "CardType"

# ---------- 扫能力 / 卡 ----------
$abil = @{}
Get-ChildItem "Assets\TcgEngine\Resources\Abilities" -Recurse -Filter *.asset | ForEach-Object {
    $g = Get-Guid ($_.FullName + ".meta"); if (-not $g) { return }
    $p = Parse-Asset $_.FullName
    $abil[$g] = [pscustomobject]@{ Guid = $g; Id = $_.BaseName; Keys = $p.Keys; Scalars = $p.Scalars; Direct = $false; Reach = $false }
}
$cards = @{}          # ★ 以 guid 为键（基名会撞：155 个资产只有 154 个不同基名）
$cardNameSeen = @{}
Get-ChildItem "Assets\TcgEngine\Resources\Cards" -Recurse -Filter *.asset | ForEach-Object {
    $g = Get-Guid ($_.FullName + ".meta"); if (-not $g) { return }
    $p = Parse-Asset $_.FullName
    if ($cardNameSeen.ContainsKey($_.BaseName)) { Write-Host ("  [警告] 卡基名冲突：" + $_.BaseName + " ← " + $_.FullName.Substring($root.Length + 1)) }
    $cardNameSeen[$_.BaseName] = $true
    $cards[$g] = [pscustomobject]@{ Id = $_.BaseName; Guid = $g; Path = $_.FullName.Substring($root.Length + 1); Keys = $p.Keys; Scalars = $p.Scalars; AbilGuids = @() }
    if ($p.Keys.ContainsKey("abilities")) { $cards[$g].AbilGuids = @($p.Keys["abilities"]) }
}
Write-Host ("[1] 能力=" + $abil.Count + "，卡=" + $cards.Count + "（不同基名 " + $cardNameSeen.Count + "）")

# ---------- 全工程 guid → 路径（用于把"未解析引用"定位到 Resources 之外的真实资产） ----------
$allGuid2path = @{}
Get-ChildItem "Assets" -Recurse -Include *.meta -File -ErrorAction SilentlyContinue | ForEach-Object {
    $g = Get-Guid $_.FullName
    if ($g -and -not $allGuid2path.ContainsKey($g)) { $allGuid2path[$g] = $_.FullName.Substring($root.Length + 1) -replace '\.meta$', '' }
}
Write-Host ("[1b] 全工程资产索引：" + $allGuid2path.Count + " 条")

# ---------- 组件 → 它引用的能力（用于链式可达） ----------
$compToAbil = @{}   # 组件 guid -> 该组件引用的能力 guid 列表
foreach ($a in $abil.Values) {
    foreach ($k in $a.Keys.Keys) {
        foreach ($g in $a.Keys[$k]) {
            if ($abil.ContainsKey($g)) { continue }        # 能力→能力：单独处理
            if (-not $assetInfo.ContainsKey($g)) { continue }
            if (-not $compToAbil.ContainsKey($g)) {
                $pp = Parse-Asset (Join-Path $root $assetInfo[$g].Path)
                $compToAbil[$g] = @($pp.Keys.Keys | ForEach-Object { $pp.Keys[$_] } | Where-Object { $abil.ContainsKey($_) })
            }
        }
    }
}

# ---------- 边：能力 → 能力 ----------
$edges = @{}
foreach ($a in $abil.Values) {
    $set = New-Object System.Collections.Generic.HashSet[string]
    if ($a.Keys.ContainsKey("chain_abilities")) { foreach ($g in $a.Keys["chain_abilities"]) { if ($abil.ContainsKey($g)) { [void]$set.Add($g) } } }
    foreach ($k in $a.Keys.Keys) {
        foreach ($g in $a.Keys[$k]) {
            if ($abil.ContainsKey($g)) { continue }
            if ($compToAbil.ContainsKey($g)) { foreach ($b in $compToAbil[$g]) { [void]$set.Add($b) } }
        }
    }
    $edges[$a.Guid] = $set
}

# ---------- 可达闭包 ----------
$direct = New-Object System.Collections.Generic.HashSet[string]
foreach ($c in $cards.Values) { foreach ($g in $c.AbilGuids) { if ($abil.ContainsKey($g)) { [void]$direct.Add($g) } } }
$reach = New-Object System.Collections.Generic.HashSet[string]
$stack = New-Object System.Collections.Generic.Stack[string]
foreach ($g in $direct) { [void]$stack.Push($g) }
while ($stack.Count -gt 0) {
    $g = $stack.Pop()
    if ($reach.Contains($g)) { continue }
    [void]$reach.Add($g)
    if ($edges.ContainsKey($g)) { foreach ($b in $edges[$g]) { if (-not $reach.Contains($b)) { [void]$stack.Push($b) } } }
}
foreach ($g in $abil.Keys) { $abil[$g].Direct = $direct.Contains($g); $abil[$g].Reach = $reach.Contains($g) }
Write-Host ("[2] 可达性：直接引用=" + $direct.Count + "，含链式可达=" + $reach.Count + "，真孤儿=" + ($abil.Count - $reach.Count))

# ---------- 未解析引用（= 需要随池打包的 Resources 之外资产，或真缺失） ----------
$unknown = New-Object System.Collections.ArrayList
foreach ($a in $abil.Values) {
    foreach ($k in $a.Keys.Keys) {
        foreach ($g in $a.Keys[$k]) {
            if ($assetInfo.ContainsKey($g) -or $abil.ContainsKey($g)) { continue }
            $tgt = if ($allGuid2path.ContainsKey($g)) { $allGuid2path[$g] } else { "(缺失)" }
            $kind = if ($tgt -eq "(缺失)") { "缺失" } else { ($tgt -split '/')[3] }
            [void]$unknown.Add([pscustomobject]@{ Owner = $a.Id; Key = $k; Guid = $g; Target = $tgt; Kind = $kind })
        }
    }
}

# ---------- 逐卡矩阵 ----------
$genericEffect = @("EffectDamage", "EffectHeal", "EffectDraw", "EffectAddStat", "EffectMana", "EffectSetStat", "EffectResetStat", "EffectDestroy", "EffectExhaust", "EffectDiscard", "EffectSendPile", "EffectClearStatus", "EffectClearTemp", "EffectExile", "EffectShuffle", "EffectSetAtkEqualHp", "EffectSetAtkEqualHpReal", "EffectAddStatRoll")
$summonEffect = @("EffectSummon", "EffectSummonMultiple", "EffectCreate", "EffectTransform", "EffectPlay", "EffectRepeat", "EffectAttack", "EffectAttackRedirect")
$rows = New-Object System.Collections.ArrayList
foreach ($c in ($cards.Values | Sort-Object Id)) {
    $abList = New-Object System.Collections.ArrayList
    $seen = New-Object System.Collections.Generic.HashSet[string]
    foreach ($g in $c.AbilGuids) { if ($abil.ContainsKey($g) -and $seen.Add($g)) { [void]$abList.Add($abil[$g]) } }
    # 链式补充
    $stack2 = New-Object System.Collections.Generic.Stack[string]
    foreach ($g in $c.AbilGuids) { if ($abil.ContainsKey($g)) { [void]$stack2.Push($g) } }
    while ($stack2.Count -gt 0) {
        $g = $stack2.Pop()
        if ($edges.ContainsKey($g)) { foreach ($b in $edges[$g]) { if ($seen.Add($b)) { [void]$abList.Add($abil[$b]); [void]$stack2.Push($b) } } }
    }
    $eff = New-Object System.Collections.Generic.HashSet[string]
    $con = New-Object System.Collections.Generic.HashSet[string]
    $fil = New-Object System.Collections.Generic.HashSet[string]
    $trg = New-Object System.Collections.Generic.HashSet[string]
    $statusN = 0; $chainN = 0; $fxN = 0
    foreach ($a in $abList) {
        if ($a.Scalars.ContainsKey("trigger")) { $t = ($a.Scalars["trigger"] -replace '\D', ''); $tn = if ($trigNames.ContainsKey([int]$t)) { $trigNames[[int]$t] } else { "T$t" }; [void]$trg.Add($tn) }
        foreach ($k in @("effects", "conditions_trigger", "conditions_target", "filters_target")) {
            if (-not $a.Keys.ContainsKey($k)) { continue }
            foreach ($g in $a.Keys[$k]) {
                if (-not $assetInfo.ContainsKey($g)) { continue }
                $i = $assetInfo[$g]
                $cls = if ($i.Class) { $i.Class } else { "(无)" + $i.Name }
                if ($k -eq "effects") { [void]$eff.Add($cls) }
                elseif ($k -eq "filters_target") { [void]$fil.Add($cls) }
                elseif ($i.Class -like "Filter*") { [void]$fil.Add($cls) }
                else { [void]$con.Add($cls) }
            }
        }
        if ($a.Keys.ContainsKey("status") -and $a.Keys["status"].Count -gt 0) { $statusN++ }
        if ($a.Keys.ContainsKey("chain_abilities") -and $a.Keys["chain_abilities"].Count -gt 0) { $chainN++ }
        foreach ($fk in @("board_fx", "caster_fx", "target_fx", "projectile_fx")) { if ($a.Keys.ContainsKey($fk) -and $a.Keys[$fk].Count -gt 0) { $fxN++; break } }
    }
    $typeN = if ($c.Scalars.ContainsKey("type")) { ($c.Scalars["type"] -replace '\D', '') } else { "0" }
    $typeName = if ($typeNames.ContainsKey([int]$typeN)) { $typeNames[[int]$typeN] } else { "T$typeN" }
    $score = $abList.Count + $eff.Count + $con.Count * 2 + $fil.Count * 2 + $chainN + $(if ($fxN -gt 0) { 1 } else { 0 })
    # 建议批次
    $batch = ""
    if ($abList.Count -eq 0) { $batch = "1-白板" }
    elseif ($trg.Contains("Ongoing") -or $trg.Contains("OnDeath") -or $typeName -eq "Hero") { $batch = "5-英雄光环亡语" }
    elseif ($con.Count -gt 0 -or $fil.Count -gt 0) { $batch = "4-条件过滤" }
    elseif ((@($eff | Where-Object { $genericEffect -notcontains $_ })).Count -eq 0) { $batch = "2-数值直伤" }
    elseif ((@($eff | Where-Object { $genericEffect -notcontains $_ -and $summonEffect -notcontains $_ })).Count -eq 0) { $batch = "3-召唤衍生" }
    else { $batch = "6-专有逻辑" }
    [void]$rows.Add([pscustomobject]@{
        Card = $c.Id; Type = $typeName; Deckbuilding = $c.Scalars["deckbuilding"]; Mana = $c.Scalars["mana"]
        Abil = $abList.Count; Triggers = (($trg | Sort-Object) -join ","); Effects = (($eff | Sort-Object) -join ",")
        Conditions = (($con | Sort-Object) -join ","); Filters = (($fil | Sort-Object) -join ",")
        StatusN = $statusN; ChainN = $chainN; FxN = $fxN; Score = $score; Batch = $batch
        HasArt = $(if ($c.Keys.ContainsKey("art_board") -and $c.Keys["art_board"].Count -gt 0) { 1 } else { 0 })
        HasCardFx = $(if (($c.Keys.ContainsKey("spawn_fx") -and $c.Keys["spawn_fx"].Count -gt 0) -or ($c.Keys.ContainsKey("death_fx") -and $c.Keys["death_fx"].Count -gt 0)) { 1 } else { 0 })
    })
}

# ---------- 输出 ----------
$sb = New-Object System.Text.StringBuilder
[void]$sb.AppendLine("card`ttype`tdeckbuilding`tmana`tabilities`ttriggers`teffects`tconditions`tfilters`tstatus`tchain`tability_fx`tscore`tsuggested_batch`thas_art`thas_card_fx")
foreach ($r in $rows) {
    [void]$sb.AppendLine(($r.Card, $r.Type, $r.Deckbuilding, $r.Mana, $r.Abil, $r.Triggers, $r.Effects, $r.Conditions, $r.Filters, $r.StatusN, $r.ChainN, $r.FxN, $r.Score, $r.Batch, $r.HasArt, $r.HasCardFx) -join "`t")
}
[System.IO.File]::WriteAllText((Join-Path $root "tools\card_migration_matrix.tsv"), $sb.ToString(), (New-Object System.Text.UTF8Encoding($false)))

$sb2 = New-Object System.Text.StringBuilder
[void]$sb2.AppendLine("ability_id`tdirect`treachable`tchain_out")
foreach ($a in ($abil.Values | Sort-Object Id)) { [void]$sb2.AppendLine($a.Id + "`t" + $a.Direct + "`t" + $a.Reach + "`t" + $(if ($edges.ContainsKey($a.Guid)) { $edges[$a.Guid].Count } else { 0 })) }
[System.IO.File]::WriteAllText((Join-Path $root "tools\audit_reachability.tsv"), $sb2.ToString(), (New-Object System.Text.UTF8Encoding($false)))

$sb3 = New-Object System.Text.StringBuilder
[void]$sb3.AppendLine("owner_ability`tkey`tkind`ttarget_path`tguid")
foreach ($u in ($unknown | Sort-Object Owner)) { [void]$sb3.AppendLine($u.Owner + "`t" + $u.Key + "`t" + $u.Kind + "`t" + $u.Target + "`t" + $u.Guid) }
[System.IO.File]::WriteAllText((Join-Path $root "tools\audit_unknown_refs.tsv"), $sb3.ToString(), (New-Object System.Text.UTF8Encoding($false)))

Write-Host ""
Write-Host "=== 建议批次分布（逐卡）==="
$rows | Group-Object Batch | Sort-Object Name | ForEach-Object { Write-Host ("  " + $_.Name.PadRight(16) + " = " + $_.Count + " 张") }
Write-Host ""
Write-Host "=== 复杂度 Top 15 ==="
$rows | Sort-Object -Property Score -Descending | Select-Object -First 15 | ForEach-Object { Write-Host ("  " + $_.Card.PadRight(26) + " score=" + $_.Score.ToString().PadRight(4) + " abil=" + $_.Abil + " eff=" + $_.Effects) }
Write-Host ""
Write-Host ("=== 未解析引用 = " + $unknown.Count + " 条（= 必须随池打包的 Resources 之外资产 / 真缺失）===")
$unknown | Group-Object Kind | Sort-Object Count -Descending | ForEach-Object { Write-Host ("  " + $_.Name.PadRight(14) + " = " + $_.Count) }
Write-Host "  ── 按字段分组（Top 12）──"
$unknown | Group-Object Key | Sort-Object Count -Descending | Select-Object -First 12 | ForEach-Object { Write-Host ("  " + $_.Name.PadRight(20) + " = " + $_.Count) }
Write-Host "产出：tools/card_migration_matrix.tsv / audit_reachability.tsv / audit_unknown_refs.tsv"
