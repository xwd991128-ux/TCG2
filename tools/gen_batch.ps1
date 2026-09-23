# Batch test-case generator (ASCII-only on purpose: Windows PowerShell reads .ps1 as ANSI).
# Reads:
#   tools/node_inventory.tsv   (id \t name \t category \t inputs \t outputs)  -- UTF8
#   tools/batch_overrides.tsv  (id|assert|expect|fields|src1|dst1|note)       -- UTF8, order = test order
#     optional first line: "# TITLE: <title>"
# Writes: tools/node_batch.json  (consumed by NodeBatchProbe)
param(
    [string]$Overrides = "tools/batch_overrides.tsv",
    [string]$Inventory = "tools/node_inventory.tsv",
    [string]$Out = "tools/node_batch.json"
)

$ErrorActionPreference = "Stop"
$root = (Get-Location).Path

# ---- inventory ----
$inv = @{}
foreach ($line in (Get-Content (Join-Path $root $Inventory) -Encoding UTF8 | Select-Object -Skip 1)) {
    if (-not $line) { continue }
    $f = $line -split "`t"
    if ($f.Count -lt 5) { continue }
    $inv[$f[0]] = [pscustomobject]@{
        id = $f[0]; name = $f[1]; cat = $f[2]
        ins = @(($f[3] -split ',') | Where-Object { $_ })
        outs = @(($f[4] -split ',') | Where-Object { $_ })
    }
}

# ---- overrides (order matters) ----
$title = "Batch"
$rows = @()
foreach ($line in (Get-Content (Join-Path $root $Overrides) -Encoding UTF8)) {
    if (-not $line) { continue }
    if ($line.StartsWith("# TITLE:")) { $title = $line.Substring(8).Trim(); continue }
    if ($line.StartsWith("#")) { continue }
    $p = $line -split '\|'
    if ($p.Count -lt 2) { continue }
    $rows += ,$p
}
if ($rows.Count -eq 0) { throw "no cases in $Overrides" }

# ---- build specs ----
# 动作/取值 判定：节点若出现在 NodeDocRunner 的动作分派 `case "id":` 里 → 动作类（走 Flow 执行链）；
# 否则是取值类（被消费者"拉取"求值）。比"有没有输出口"准得多（很多动作节点也带 outEvent/return 输出口）。
$runnerSrc = [System.IO.File]::ReadAllText((Join-Path $root "Assets/TcgEngine/Scripts/Workshop/Graph/NodeDocRunner.cs"), [System.Text.Encoding]::UTF8)
# 动作分派区间 = `private static void ExecuteAction(` 到"下一个类成员（8 空格缩进）"之间
$actSpan = ""
$actStart = $runnerSrc.IndexOf("private static void ExecuteAction")
if ($actStart -ge 0) {
    $rest = $runnerSrc.Substring($actStart + 1)
    $mNext = [regex]::Match($rest, '(?m)^        (private|public|internal|protected) ')
    $actSpan = if ($mNext.Success) { $rest.Substring(0, $mNext.Index) } else { $rest }
}
$nodes = @()
foreach ($p in $rows) {
    $id = $p[0].Trim()
    if (-not $inv.ContainsKey($id)) { Write-Output ("WARN unknown id: " + $id); continue }
    $d = $inv[$id]

    #★ 只有**真正的动作分派**（ExecuteAction 方法体）里的 case 才算动作节点。
    #  取值器（ResolveValueXxx / GetObjectInput / ResolveCollectionNode…）里同样写 `case "id":`
    #  —— 直接全文匹配会把取值节点误判成动作类（实测 102018 因此走动作通道 → "未支持动作"→ 假通过）。
    $isCase = [regex]::IsMatch($actSpan, 'case\s+"' + [regex]::Escape($id) + '"\s*:')
    $dataOuts = @($d.outs | Where-Object { $_ -notmatch ':ActionNode$' -and $_ -notmatch ':Flow$' })
    #★ 类型以「用例断言」为准（断言本身就是测法声明）：value_*/dyn → 取值类；exec/damage/heal/log/state_* → 动作类。
    #  仅当断言缺省时才回退到"在动作分派表里 / 无输出口"的启发式。
    $kindHint = if ($p.Count -gt 1) { $p[1].Trim() } else { "" }
    if ($kindHint -match '^(value_|dyn|value_any|list_|run_)') { $kind = "value" }
    elseif ($kindHint -match '^(exec|damage|heal|log|state_|nobreak|mana_|fatigue_|hp_down|armor_|board_|hand_|deck_|equip_|secret_|any_|buff_)') { $kind = "action" }
    else { $kind = if ($isCase -or $dataOuts.Count -eq 0) { "action" } else { "value" } }
    $outPin = ""; $outType = ""
    if ($kind -eq "value" -and $dataOuts.Count -gt 0) {
        #★ 优先取 `return`（规范结果口）：不少集合/循环节点额外带 `element`（循环元素）输出口，
        #  取 element 会拿到"当前元素"而不是结果集合（实测 111022/111023 因此失败）。
        $ret = @($dataOuts | Where-Object { $_ -match '^return:' })
        $pick = if ($ret.Count -gt 0) { $ret[0] } else { $dataOuts[0] }
        $sp = $pick -split ':'
        $outPin = $sp[0]; $outType = $sp[1]
    }

    $insArr = @()
    foreach ($i in $d.ins) {
        $ip = $i -split ':'
        if ($ip.Count -ge 2) { $insArr += [pscustomobject]@{ name = $ip[0]; type = $ip[1]; display = $ip[0] } }
    }

    # defaults
    $assert = if ($kind -eq "value") { "value_notnull" } else { "exec" }
    $expect = ""; $f1 = ""; $v1 = ""; $f2 = ""; $v2 = ""; $f3 = ""; $v3 = ""
    $src1 = ""; $dst1 = ""; $note = ""; $src2 = ""; $src2c = ""
    $cardIn = @($insArr | Where-Object { $_.type -eq "Card" })
    if ($kind -eq "value") {
        $defIn = @($insArr | Where-Object { $_.type -eq "CardDefine" })
        if ($cardIn.Count -gt 0) { $src1 = "EV_CARD"; $dst1 = $cardIn[0].name }
        elseif ($defIn.Count -gt 0) { $src1 = "DEFINE_OF_CASTER"; $dst1 = $defIn[0].name }
    }
    elseif ($cardIn.Count -gt 0) { $src1 = "EV_CARD"; $dst1 = $cardIn[0].name }

    # overrides
    if ($p.Count -gt 1 -and $p[1]) { $assert = $p[1].Trim() }
    if ($p.Count -gt 2 -and $p[2]) { $expect = $p[2].Trim() }
    if ($p.Count -gt 3 -and $p[3]) {
        $kv = $p[3] -split ';'
        if ($kv.Count -gt 0 -and $kv[0]) { $t = $kv[0] -split '='; $f1 = $t[0]; $v1 = if ($t.Count -gt 1) { $t[1] } else { "" } }
        if ($kv.Count -gt 1 -and $kv[1]) { $t = $kv[1] -split '='; $f2 = $t[0]; $v2 = if ($t.Count -gt 1) { $t[1] } else { "" } }
        if ($kv.Count -gt 2 -and $kv[2]) { $t = $kv[2] -split '='; $f3 = $t[0]; $v3 = if ($t.Count -gt 2 -or $t.Count -eq 2) { if ($t.Count -gt 1) { $t[1] } else { "" } } else { "" } }
    }
    if ($p.Count -gt 4 -and $p[4]) { $src1 = $p[4].Trim() }
    if ($p.Count -gt 5 -and $p[5]) { $dst1 = $p[5].Trim() }
    if ($p.Count -gt 6 -and $p[6]) { $note = $p[6].Trim() }
    if ($p.Count -gt 7 -and $p[7]) { $src2 = $p[7].Trim() }
    if ($p.Count -gt 8 -and $p[8]) { $src2c = $p[8].Trim() }
    #可选：循环体子动作（遍历/筛选等节点的 action 口）——列9=动作id、列10=字段名、列11=字段值
    $childAction = ""; $childField = ""; $childValue = ""
    if ($p.Count -gt 9 -and $p[9]) { $childAction = $p[9].Trim() }
    if ($p.Count -gt 10 -and $p[10]) { $childField = $p[10].Trim() }
    if ($p.Count -gt 11 -and $p[11]) { $childValue = $p[11].Trim() }
    if ($src1 -eq "NONE") { $src1 = ""; $dst1 = "" }
    if ($src2 -eq "NONE") { $src2 = "" }

    $nodes += [pscustomobject]@{
        id = $d.id; name = $d.name; category = $d.cat; kind = $kind
        out_type = $outType; out_pin = $outPin; ins = $insArr
        field1 = $f1; value1 = $v1; field2 = $f2; value2 = $v2; field3 = $f3; value3 = $v3
        src1 = $src1; src1_pin = "card"; dst1 = $dst1; src1_const = ""
        src2 = $src2; src2_const = $src2c
        child_action = $childAction; child_field = $childField; child_value = $childValue
        assert = $assert; expect = $expect; note = $note
    }
}

$spec = [pscustomobject]@{ title = $title; nodes = $nodes }
$json = $spec | ConvertTo-Json -Depth 6
[System.IO.File]::WriteAllText((Join-Path $root $Out), $json, (New-Object System.Text.UTF8Encoding($false)))
Write-Output ("OK cases=" + $nodes.Count + " -> " + $Out)
foreach ($n in $nodes) { Write-Output ("  " + $n.id + " " + $n.name + " kind=" + $n.kind + " assert=" + $n.assert + " src=" + $n.src1) }
