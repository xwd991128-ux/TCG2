# =============================================================================
# Phase 0-A 结构基线：把每张内置卡的每个能力**完整解析**成可 diff 的一行
#   产出：tools/card_baseline.tsv        （每张卡的每个能力一行；含所有效果/条件/过滤字段）
#         tools/card_baseline_meta.txt   （统计摘要）
#   用途：① 迁移前"这张卡现在是什么样"的存档（改完必须一致）
#         ② Phase 2 转换器的输入规格（照它生成规则图）
#   只读：不修改任何资产。
# =============================================================================
$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
Set-Location $root

function Read-Text($p) { [System.IO.File]::ReadAllText($p, [System.Text.Encoding]::UTF8) }
function Get-Guid($p) { $m = [regex]::Match((Read-Text $p), "guid:\s*([0-9a-fA-F]+)"); if ($m.Success) { $m.Groups[1].Value } else { "" } }

# ---------- 解析器 ----------
function Parse-Asset($path) {
    $keys = @{}; $scalars = @{}; $cur = ""
    foreach ($line in [System.IO.File]::ReadAllLines($path, [System.Text.Encoding]::UTF8)) {
        if ($line -match '^\s{2}([A-Za-z_][A-Za-z0-9_]*):\s*(.*)$') {
            $cur = $Matches[1]; $rest = $Matches[2].Trim()
            if ($cur -like 'm_*') { continue }
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
function EnumName($map, $raw) {
    if ($null -eq $raw) { return "" }
    $n = ($raw -replace '\D', '')
    if ($n -eq '') { return "" }
    if ($map.ContainsKey([int]$n)) { return $map[[int]$n] }
    return "?$n"
}

# ---------- 索引 ----------
$guid2class = @{}
Get-ChildItem "Assets\TcgEngine\Scripts" -Recurse -Filter *.cs.meta | ForEach-Object {
    $g = Get-Guid $_.FullName; if ($g) { $guid2class[$g] = ($_.Name -replace '\.cs\.meta$', '') }
}
$assetInfo = @{}
Get-ChildItem "Assets\TcgEngine\Resources" -Recurse -Filter *.asset | ForEach-Object {
    $meta = $_.FullName + ".meta"; if (-not (Test-Path -LiteralPath $meta)) { return }
    $g = Get-Guid $meta; if (-not $g) { return }
    $m = [regex]::Match((Read-Text $_.FullName), "m_Script:\s*\{fileID:\s*11500000,\s*guid:\s*([0-9a-fA-F]+)")
    $cls = if ($m.Success -and $guid2class.ContainsKey($m.Groups[1].Value)) { $guid2class[$m.Groups[1].Value] } else { "" }
    $assetInfo[$g] = [pscustomobject]@{ Path = $_.FullName.Substring($root.Length + 1); Class = $cls; Name = $_.BaseName }
}
$trigNames = Parse-Enum "Assets\TcgEngine\Scripts\Data\AbilityData.cs" "AbilityTrigger"
$targNames = Parse-Enum "Assets\TcgEngine\Scripts\Data\AbilityData.cs" "AbilityTarget"
$typeNames = Parse-Enum "Assets\TcgEngine\Scripts\Data\CardData.cs" "CardType"

# 引用解析缓存：guid → 展示名（优先被引资产的 id 字段，其次资产名）
$refCache = @{}
function Ref-Name($guid) {
    if (-not $guid) { return "" }
    if ($refCache.ContainsKey($guid)) { return $refCache[$guid] }
    $name = ""
    if ($assetInfo.ContainsKey($guid)) {
        $info = $assetInfo[$guid]
        $p = Parse-Asset (Join-Path $root $info.Path)
        if ($p.Scalars.ContainsKey("id") -and $p.Scalars["id"] -ne "") { $name = $p.Scalars["id"] }
        elseif ($p.Scalars.ContainsKey("effect")) { $name = $info.Name + "(" + $info.Class + ".effect=" + $p.Scalars["effect"] + ")" }
        else { $name = $info.Name }
    }
    elseif ($guid.Length -ge 32 -and $guid -match '^([0-9a-f])\1{31}$') { $name = "(伪造guid)" }
    else { $name = "(缺失)" }
    $refCache[$guid] = $name
    return $name
}

# ---------- 组件序列化：Class{f=v;...} ----------
function Serialize-Component($guid) {
    if (-not $assetInfo.ContainsKey($guid)) { return "$(Ref-Name $guid)" }
    $info = $assetInfo[$guid]
    $p = Parse-Asset (Join-Path $root $info.Path)
    $cls = if ($info.Class) { $info.Class } else { "?" + $info.Name }
    $parts = New-Object System.Collections.ArrayList
    foreach ($k in ($p.Scalars.Keys | Sort-Object)) {
        $v = $p.Scalars[$k]
        if ($k -eq "id") { continue }
        if ($k -eq "trigger" -or $k -eq "target") { continue }
        [void]$parts.Add($k + "=" + $v)
    }
    foreach ($k in ($p.Keys.Keys | Sort-Object)) {
        if ($k -eq "id") { continue }
        $names = @($p.Keys[$k] | ForEach-Object { Ref-Name $_ })
        if ($names.Count -gt 0) { [void]$parts.Add($k + "→[" + ($names -join ",") + "]") }
    }
    return $cls + "{" + ($parts -join ";") + "}"
}

# ---------- 扫能力 ----------
$abil = @{}
Get-ChildItem "Assets\TcgEngine\Resources\Abilities" -Recurse -Filter *.asset | ForEach-Object {
    $g = Get-Guid ($_.FullName + ".meta"); if (-not $g) { return }
    $abil[$g] = [pscustomobject]@{ Guid = $g; Id = $_.BaseName; Path = $_.FullName.Substring($root.Length + 1); Data = (Parse-Asset $_.FullName) }
}

# ---------- 扫卡并输出 ----------
$sb = New-Object System.Text.StringBuilder
[void]$sb.AppendLine("card_id`tcard_type`tmana`tatk`thp`tdeckbuilding`tabil_seq`tabil_id`ttrigger`ttarget`tvalue`tduration`tmana_cost`texhaust`tstatus`teffects`tconditions_trigger`tconditions_target`tfilters_target`tfx")
$cardCount = 0; $abilRows = 0; $cardNoAbil = 0
$cards = Get-ChildItem "Assets\TcgEngine\Resources\Cards" -Recurse -Filter *.asset | Sort-Object FullName
foreach ($cf in $cards) {
    $cardCount++
    $cp = Parse-Asset $cf.FullName
    $cid = if ($cp.Scalars.ContainsKey("id")) { $cp.Scalars["id"] } else { $cf.BaseName }
    $ctype = EnumName $typeNames $cp.Scalars["type"]
    $mana = $cp.Scalars["mana"]; $atk = $cp.Scalars["attack"]; $hp = $cp.Scalars["hp"]; $db = $cp.Scalars["deckbuilding"]
    $abGuids = if ($cp.Keys.ContainsKey("abilities")) { @($cp.Keys["abilities"]) } else { @() }
    if ($abGuids.Count -eq 0) {
        $cardNoAbil++
        [void]$sb.AppendLine(($cid, $ctype, $mana, $atk, $hp, $db, "0", "(无能力)", "", "", "", "", "", "", "", "", "", "", "", "") -join "`t")
        continue
    }
    $seq = 0
    foreach ($ag in $abGuids) {
        $seq++; $abilRows++
        if (-not $abil.ContainsKey($ag)) {
            [void]$sb.AppendLine(($cid, $ctype, $mana, $atk, $hp, $db, $seq, "(缺失能力)", "", "", "", "", "", "", "", "", "", "", "", "") -join "`t")
            continue
        }
        $a = $abil[$ag]; $d = $a.Data
        #★ 运行期按**能力资产的 id 字段**查找（AbilityData.ability_dict 以 id 为键），
        #  而文件名与 id 并不总是一致（实测 play/play_damage1.asset 的 id=play_deal_damage1、
        #  spells/spell_paralyse3.asset 的 id=spell_paralyse）→ 这里必须写 id 字段，否则运行层取不到。
        $abilKey = if ($d.Scalars.ContainsKey("id") -and $d.Scalars["id"] -ne "") { $d.Scalars["id"] } else { $a.Id }
        $trig = EnumName $trigNames $d.Scalars["trigger"]
        $targ = EnumName $targNames $d.Scalars["target"]
        $sts = if ($d.Keys.ContainsKey("status")) { @($d.Keys["status"] | ForEach-Object { Ref-Name $_ }) -join "," } else { "" }
        $eff = if ($d.Keys.ContainsKey("effects")) { @($d.Keys["effects"] | ForEach-Object { Serialize-Component $_ }) -join " + " } else { "" }
        $ct = if ($d.Keys.ContainsKey("conditions_trigger")) { @($d.Keys["conditions_trigger"] | ForEach-Object { Serialize-Component $_ }) -join " + " } else { "" }
        $ctg = if ($d.Keys.ContainsKey("conditions_target")) { @($d.Keys["conditions_target"] | ForEach-Object { Serialize-Component $_ }) -join " + " } else { "" }
        $ft = if ($d.Keys.ContainsKey("filters_target")) { @($d.Keys["filters_target"] | ForEach-Object { Serialize-Component $_ }) -join " + " } else { "" }
        $fxParts = New-Object System.Collections.ArrayList
        foreach ($fk in @("board_fx", "caster_fx", "target_fx", "projectile_fx")) {
            if ($d.Keys.ContainsKey($fk) -and $d.Keys[$fk].Count -gt 0) { [void]$fxParts.Add($fk + "=" + (($d.Keys[$fk] | ForEach-Object { [System.IO.Path]::GetFileName((Ref-Name $_)) }) -join ",")) }
        }
        $fx = $fxParts -join ";"
        [void]$sb.AppendLine(($cid, $ctype, $mana, $atk, $hp, $db, $seq, $abilKey, $trig, $targ, $d.Scalars["value"], $d.Scalars["duration"], $d.Scalars["mana_cost"], $d.Scalars["exhaust"], $sts, $eff, $ct, $ctg, $ft, $fx) -join "`t")
    }
}

[System.IO.File]::WriteAllText((Join-Path $root "tools\card_baseline.tsv"), $sb.ToString(), (New-Object System.Text.UTF8Encoding($false)))

$meta = @()
$meta += "卡数=" + $cardCount
$meta += "无能力卡数=" + $cardNoAbil
$meta += "能力行数=" + $abilRows
$meta += "能力资产数=" + $abil.Count
$meta += "tsv 行数=" + (@($sb.ToString() -split "`n").Count - 1)
[System.IO.File]::WriteAllText((Join-Path $root "tools\card_baseline_meta.txt"), ($meta -join "`r`n"), (New-Object System.Text.UTF8Encoding($false)))

Write-Host "=== Phase 0-A 结构基线已生成 ==="
$meta | ForEach-Object { Write-Host ("  " + $_) }
Write-Host ""
Write-Host "=== 抽样（前 3 行）==="
Get-Content "tools\card_baseline.tsv" -Encoding UTF8 | Select-Object -First 3 | ForEach-Object { $t=$_; if($t.Length -gt 260){$t=$t.Substring(0,260)+"…"}; Write-Host ("  " + $t) }
Write-Host "产出：tools/card_baseline.tsv / tools/card_baseline_meta.txt"
