# 卡池接线审计：检查规则图里"接到不存在的端口/字段/节点"这类**静默失败**
# （引擎对未知端口/字段是忽略，不报错 —— 表现就是"图看着对，运行没反应"）
# 用法： powershell -NoProfile -File tools/audit_pool.ps1 [卡池json路径]
param(
    [string]$PoolPath = "$env:USERPROFILE\AppData\LocalLow\DefaultCompany\TCG2\Workshop\sample_pool.json"
)

$ErrorActionPreference = "Stop"
try { [Console]::OutputEncoding = [System.Text.Encoding]::UTF8 } catch { }
$root = Split-Path $PSScriptRoot -Parent
$docPath = Join-Path $root "Assets/TcgEngine/Resources/NodeDoc.xml"
if (-not (Test-Path $PoolPath)) { Write-Host "找不到卡池文件: $PoolPath"; exit 1 }

# ---- NodeDoc：编号 → 端口/字段名集合 ----
$doc = [System.IO.File]::ReadAllText($docPath, [System.Text.Encoding]::UTF8)
$ports = @{}
$blocks = [regex]::Matches($doc, '<defineId>(\d+)</defineId>[\s\S]*?(?=<defineId>|</root>|\z)')
foreach ($b in $blocks) {
    $id = $b.Groups[1].Value
    $set = New-Object System.Collections.Generic.HashSet[string]
    foreach ($pm in [regex]::Matches($b.Value, '<name>([^<]+)</name>')) { [void]$set.Add($pm.Groups[1].Value) }
    $ports[$id] = $set
}
Write-Host ("NodeDoc 节点数 = " + $ports.Count)

# ---- 卡池 ----
$json = Get-Content $PoolPath -Raw -Encoding UTF8 | ConvertFrom-Json
$cards = if ($json.cards) { $json.cards } else { $json }
Write-Host ("卡池文件 = " + (Split-Path $PoolPath -Leaf) + "，卡数 = " + $cards.Count)

$rows = New-Object System.Collections.Generic.List[string]
$cat = @{}
function Add-Finding([string]$key, [string]$text) {
    if (-not $cat.ContainsKey($key)) { $cat[$key] = 0 }
    $cat[$key] = $cat[$key] + 1
    $rows.Add("[$key] $text")
}

# 动态编号输入槽：**不在 NodeDoc 静态声明里**，由运行时按名字解析（见 NodeDocRunner.ParamSlotBase/ParamSlotName）
#   112004 整数运算 = arg / arg1 / arg2 …    112005 逻辑运算 = value / value1 / value2 …
# 不排除它们 → 会误报"端口/字段不在NodeDoc"（实测：4 张卡的 8 条全是这种误报，白查一轮）
function Test-DynamicParamSlot([string]$act, [string]$name) {
    if ($act -eq '112004') { return $name -match '^arg\d*$' }
    if ($act -eq '112005') { return $name -match '^value\d*$' }
    return $false
}

function Check-Link($c, $map, [string]$nodeId, [string]$side, [string]$pin) {
    $nd = $map[$nodeId]
    if (-not $nd) { Add-Finding "断线-节点不存在" "$($c.title) → $side=$pin"; return }
    $pinName = $pin -replace ('^' + [regex]::Escape($nodeId) + '_'), ''
    foreach ($p in $nd.pins) { if ([string]$p.name -eq $pinName) { return } }
    Add-Finding "连线指向不存在的口" "$($c.title) → $($nd.title)($($nd.action)) 口「$pinName」"
}

foreach ($c in $cards) {
    if (-not $c.graph -or -not $c.graph.nodes) { continue }
    $map = @{}
    foreach ($n in $c.graph.nodes) { $map[[string]$n.id] = $n }

    foreach ($n in $c.graph.nodes) {
        $act = [string]$n.action
        # 入口节点/内部动作用的是英文名（OnPlay / ActivateEffect …），只审数字编号的动作节点
        if ($act -notmatch '^\d+$') { continue }
        if (-not $ports.ContainsKey($act)) {
            Add-Finding "未知节点编号" "$($c.title) → $act ($($n.title))"
            continue
        }
        $decl = $ports[$act]
        foreach ($p in $n.pins) {
            $pn = [string]$p.name
            if ($pn -eq 'in' -or $pn -eq 'out') { continue }   # 引擎自动生成的流口
            if (Test-DynamicParamSlot $act $pn) { continue }   # 动态编号槽（运行时解析，不在静态文档里）
            if (-not $decl.Contains($pn)) {
                Add-Finding "端口不在NodeDoc" "$($c.title) → $act($($n.title)) 端口「$pn」"
            }
        }
        foreach ($f in $n.fields) {
            $fn = [string]$f.name
            if (Test-DynamicParamSlot $act $fn) { continue }   # 动态编号槽
            if (-not $decl.Contains($fn)) {
                Add-Finding "字段不在NodeDoc" "$($c.title) → $act($($n.title)) 字段「$fn」=$($f.value)"
            }
        }
    }

    foreach ($l in $c.graph.links) {
        Check-Link $c $map ([string]$l.from_node) 'from' ([string]$l.from_pin)
        Check-Link $c $map ([string]$l.to_node) 'to' ([string]$l.to_pin)
    }
}

Write-Host ""
Write-Host "== 分类统计 =="
$cat.GetEnumerator() | Sort-Object -Property Value -Descending | ForEach-Object { "{0,4}  {1}" -f $_.Value, $_.Key }
Write-Host ""
Write-Host "== 明细（前 40 条）=="
$rows | Select-Object -First 40
Write-Host ""
Write-Host ("问题合计 = " + $rows.Count)

# 另存 UTF8 报告：控制台可能按 GBK 解码 → 中文会乱码，读文件才准
$report = New-Object System.Collections.Generic.List[string]
$report.Add("卡池文件 = " + (Split-Path $PoolPath -Leaf) + "，卡数 = " + $cards.Count + "，问题合计 = " + $rows.Count)
$report.Add("")
$report.Add("== 分类统计 ==")
foreach ($kv in ($cat.GetEnumerator() | Sort-Object -Property Value -Descending)) {
    $report.Add(("{0,4}  {1}" -f $kv.Value, $kv.Key))
}
$report.Add("")
$report.Add("== 明细 ==")
foreach ($r in $rows) { $report.Add($r) }
$out = Join-Path $root "tools/audit_pool_report.txt"
[System.IO.File]::WriteAllLines($out, $report.ToArray(), (New-Object System.Text.UTF8Encoding($false)))
Write-Host ("报告已写入: " + $out)
