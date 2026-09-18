# 生成"NodeDoc 节点 × 运行时实现"覆盖清单（离线，不占 Unity）。用完可重复运行。
# 判据（v2，修正 v1 的漏判）：
#   1) 先剥掉行注释；
#   2) 剔除 IsSupportedAction 白名单方法体（它的 case 标签不是实现）；
#   3) 三种"实现"信号：
#      · case 标签   ：case "id":            → 已实现
#      · 条件式引用  ：*.action == "id"      → 已实现（条件式，需人工确认位置）
#      · 否则        ：未实现
$root = Split-Path -Parent $PSScriptRoot
$xmlPath = Join-Path $root 'Assets\TcgEngine\Resources\NodeDoc.xml'
$runnerPath = Join-Path $root 'Assets\TcgEngine\Scripts\Workshop\Graph\NodeDocRunner.cs'
$dbPath = Join-Path $root 'Assets\TcgEngine\Scripts\Workshop\Graph\NodeDocDb.cs'
$outPath = Join-Path $root 'tools\NodeDocCoverage.md'

[xml]$doc = [System.IO.File]::ReadAllText($xmlPath, [Text.Encoding]::UTF8)
$defs = @()
foreach ($n in $doc.ArrayOfActionComment.ActionComment) {
    $id = [string]$n.defineId
    if (-not $id) { continue }
    $cat = [string]$n.category
    if (-not $cat) { $cat = '其他' }
    $ob = $false
    if ([string]$n.obsoleteMsg -and ([string]$n.obsoleteMsg).Trim().Length -gt 0) { $ob = $true }
    $defs += [pscustomobject]@{ id = $id; en = [string]$n.editorName; cat = $cat; ob = $ob; msg = ([string]$n.obsoleteMsg) }
}
Write-Output ('NodeDoc 节点数 = ' + $defs.Count)

$dbSrc = [System.IO.File]::ReadAllText($dbPath, [Text.Encoding]::UTF8)
$cn = @{}
foreach ($m in [regex]::Matches($dbSrc, '\{\s*"(\d{4,6})"\s*,\s*"([^"]+)"\s*\}')) { $cn[$m.Groups[1].Value] = $m.Groups[2].Value }

$src = [System.IO.File]::ReadAllText($runnerPath, [Text.Encoding]::UTF8)
$clean = [regex]::Replace($src, '//[^\n\r]*', '')

# 剔除 IsSupportedAction 方法体（按大括号配平切片）
$marker = 'IsSupportedAction'
$mi = $clean.IndexOf($marker)
if ($mi -ge 0) {
    $brace = $clean.IndexOf('{', $mi)
    if ($brace -ge 0) {
        $depth = 0
        $end = -1
        for ($i = $brace; $i -lt $clean.Length; $i++) {
            if ($clean[$i] -eq '{') { $depth++ }
            elseif ($clean[$i] -eq '}') { $depth--; if ($depth -eq 0) { $end = $i; break } }
        }
        if ($end -gt $brace) {
            $clean = $clean.Substring(0, $brace) + $clean.Substring($end + 1)
            Write-Output '已剔除 IsSupportedAction 白名单方法体'
        }
    }
}

$byCase = @{}
foreach ($m in [regex]::Matches($clean, 'case\s+"([^"]+)"\s*:')) { $byCase[$m.Groups[1].Value] = $true }
$byCond = @{}
foreach ($m in [regex]::Matches($clean, '\.action\s*==\s*"([^"]+)"')) { $byCond[$m.Groups[1].Value] = $true }
foreach ($m in [regex]::Matches($clean, '"([^"]+)"\s*==\s*\w+\.action')) { $byCond[$m.Groups[1].Value] = $true }

$done = @($defs | Where-Object { $byCase.ContainsKey($_.id) })
$cond = @($defs | Where-Object { -not $byCase.ContainsKey($_.id) -and $byCond.ContainsKey($_.id) })
$miss = @($defs | Where-Object { -not $byCase.ContainsKey($_.id) -and -not $byCond.ContainsKey($_.id) })
#★ 判据 v3：取值通道里的 *.action == "id" 分支**同样算已实现**（2026-09 逐个核实：16 个"仅条件式"节点全部是真实现/部分实现，
#  没有一个是"残留引用" —— 它们都是取值/输出类节点，实现在 GetObjectInput/ResolveValueDefine/ResolveMapInput 等取值函数里）。
$impl = @($defs | Where-Object { $byCase.ContainsKey($_.id) -or $byCond.ContainsKey($_.id) })
Write-Output ('已实现 = ' + $impl.Count + '（动作 case ' + $done.Count + ' + 取值通道 ' + $cond.Count + '）｜未实现 = ' + $miss.Count + ' / 总 ' + $defs.Count)

function Rows($list, $tag) {
    $r = @()
    foreach ($grp in ($list | Group-Object cat | Sort-Object Count -Descending)) {
        $r += ''
        $r += '### ' + $grp.Name + '（' + $grp.Count + ' 个）'
        $r += ''
        $r += '| defineId | 节点名 | 过时 |'
        $r += '|---|---|---|'
        foreach ($x in ($grp.Group | Sort-Object id)) {
            $nm = $x.en
            if ($cn.ContainsKey($x.id)) { $nm = $cn[$x.id] }
            $ot = '否'
            if ($x.ob) { $ot = '是' }
            $r += '| ' + $x.id + ' | ' + $nm + ' | ' + $ot + ' |'
        }
    }
    return $r
}

$sb = New-Object System.Text.StringBuilder
[void]$sb.AppendLine('# 规则图节点覆盖清单（NodeDoc 全量 × 运行时实现）— 判据 v3')
[void]$sb.AppendLine('')
[void]$sb.AppendLine('判据：剥离行注释 → 剔除 `IsSupportedAction` 白名单 → 按 `case "id":`（动作分派）/ `*.action == "id"`（**取值通道**）两种信号判定，两者**都算已实现**。')
[void]$sb.AppendLine('')
[void]$sb.AppendLine('统计：共 **' + $defs.Count + '** 个节点｜**已实现 ' + $impl.Count + '**（动作 case ' + $done.Count + ' + 取值通道 ' + $cond.Count + '）｜**未实现 ' + $miss.Count + '**。')
[void]$sb.AppendLine('')
[void]$sb.AppendLine('> v1 判据要求 `case` 后紧跟 `{`，漏掉了内联单语句 case 与条件式分派；v2 修正后仍把"取值通道实现"单列成"待确认"。')
[void]$sb.AppendLine('> v3 依据：2026-09 对 16 个"仅条件式"节点**逐个核实**（读实现所在方法）→ 14 个真实现 + 2 个部分实现（111031 端口被编辑器移除、113001 `elements` 初始元素被忽略），**没有一个是残留引用**。')
[void]$sb.AppendLine('> 指标口径：对局报告的 `nodoc_logs` 统计 `[NodeDoc]` 前缀日志，已被 GameLog 开关接管（`gamelog_verbose=False` 时恒为 0），不能用于判断图是否执行。')
[void]$sb.AppendLine('')
[void]$sb.AppendLine('## 一、未实现（' + $miss.Count + ' 个，按分类）')
foreach ($l in (Rows $miss 'miss')) { [void]$sb.AppendLine($l) }
[void]$sb.AppendLine('')
[void]$sb.AppendLine('## 二、仅"条件式引用"命中（' + $cond.Count + ' 个，需人工确认是否真实现）')
foreach ($l in (Rows $cond 'cond')) { [void]$sb.AppendLine($l) }
[void]$sb.AppendLine('')
[void]$sb.AppendLine('## 三、已实现（case 标签，' + $done.Count + ' 个，按分类）')
foreach ($l in (Rows $done 'done')) { [void]$sb.AppendLine($l) }
[System.IO.File]::WriteAllText($outPath, $sb.ToString(), (New-Object System.Text.UTF8Encoding($true)))
Write-Output ('已写入 ' + $outPath + '（' + (Get-Item $outPath).Length + ' bytes）')
