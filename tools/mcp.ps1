# ============================================================
#  mcp.ps1 —— 直连 Unity MCP（ch-unity-mcp）的命令行工具包
#
#  为什么需要它：
#   1) IDE 侧的 MCP 工具注册偶尔会失效（报 tool does not exist or is not registered），
#      但服务端（http://localhost:9123/mcp）通常还活着 —— 本脚本走 HTTP 直连，等价可用；
#   2) get_script_errors 会**漏报**真实 CS 错误（报 0 但 Console 有 CS1061），
#      所以 compile 命令做的是「触发编译 + 拉全量日志自己筛 error CS」双查；
#   3) 响应是 UTF-8，PowerShell 默认按 GBK 解 → 中文全乱码、按中文检索必然失败。
#      本脚本统一 -OutFile 落盘 + ReadAllText(UTF8)，避免这个坑。
#
#  ★★ 2026-09 排查结论：**"连不上"绝大多数不是服务挂了**（Unity 侧 `McpServer` 会随每次域重载重建监听，
#      IDE 侧 `[MCP:HealthPatrol] Healthy: 1` 一直正常）。三类现象必须分清楚，别再一律说"服务不可达"：
#      ① fetch failed / 无法连接   → 域重载的十几秒窗口（compile/play 之后必有）。**等**：用 wait 轮询到恢复。
#      ② HTTP 400 错误的请求        → Host 头不匹配：Mono 的 HttpListener 只认 `localhost:9123`；
#                                    用 127.0.0.1 请求必须显式带 Host 头（本脚本已默认带上）。
#      ③ tool does not exist/…     → 是 **IDE 侧工具表陈旧**（跟服务无关）→ 立刻改走本脚本 HTTP 直连，
#                                    并**不要**让人去重启 MCP 服务（它本来就开着）。
#      诊断入口：`powershell -File tools/mcp.ps1 probe`
#
#  ★ 2026-09 起：域重载后的自愈改由 Assets/TcgEngine/Scripts/Editor/McpKeepAlive.cs 负责 ——
#     按 10 秒间隔轮询端口，发现没监听就调 McpServerWindow.StartServerStatic() 重连
#     （菜单「TcgEngine/工具/MCP 连接守护」可开关，EditorPrefs: TcgEngine.McpKeepAlive.Enabled）。
#     实测：触发编译后监听消失，约 6 秒自动恢复，无需人工点面板。
#     所以"端口未监听"先 wait 一轮再判定，别急着让人重启服务。
#
#  用法（在项目根目录）：
#    powershell -File tools/mcp.ps1 probe        # ★ 连接诊断：进程/端口/三种请求方式/tools 数
#    powershell -File tools/mcp.ps1 wait [秒]    # ★ 轮询等待恢复（默认最多 180 秒）
#    powershell -File tools/mcp.ps1 status
#    powershell -File tools/mcp.ps1 compile      # 触发编译 → 等恢复 → 双查（官方 + 全量日志）
#    powershell -File tools/mcp.ps1 logs [关键字]
#    powershell -File tools/mcp.ps1 play | stop
#    powershell -File tools/mcp.ps1 autobattle [局数]     # 全自动：AI vs AI 回归 + 打印报告
#    powershell -File tools/mcp.ps1 report
#    powershell -File tools/mcp.ps1 call <tool> '<json参数>'
# ============================================================

param(
    [Parameter(Position = 0)][string]$Cmd = "status",
    [Parameter(Position = 1)][string]$Arg1,
    [Parameter(Position = 2)][string]$Arg2
)

$ErrorActionPreference = "Continue"
$McpUrl = "http://localhost:9123/mcp"
#★ Host 必须显式写成 localhost:9123：服务端是 Mono HttpListener，只注册了这个前缀，
#  用 127.0.0.1 请求且不带 Host 会直接 400（实测复现）
$Headers = @{ "Accept" = "application/json, text/event-stream"; "Content-Type" = "application/json"; "Host" = "localhost:9123" }
$TmpFile = Join-Path $env:TEMP "mcp_ps_result.bin"
$WorkshopDir = Join-Path $env:USERPROFILE "AppData\LocalLow\DefaultCompany\TCG2\Workshop"
$FlagFile = Join-Path $WorkshopDir "autobattle.json"
$ReportFile = Join-Path $WorkshopDir "autobattle_report.txt"

function Invoke-Mcp {
    param([string]$Tool, [string]$ArgsJson = "{}", [int]$Retry = 12)
    $body = '{"jsonrpc":"2.0","id":2,"method":"tools/call","params":{"name":"' + $Tool + '","arguments":' + $ArgsJson + '}}'
    for ($i = 1; $i -le $Retry; $i++) {
        try {
            Invoke-WebRequest -Uri $McpUrl -Method POST -Headers $Headers -Body $body `
                -TimeoutSec 120 -UseBasicParsing -OutFile $TmpFile | Out-Null
            #★ 必须按 UTF-8 解码，否则中文/引号会被 GBK 吃掉
            return [System.IO.File]::ReadAllText($TmpFile, [System.Text.Encoding]::UTF8)
        }
        catch {
            Start-Sleep -Seconds 4     #域重载期间会断连，等一会重试
        }
    }
    return "FAILED"
}

#★ 轮询等待 MCP 恢复：域重载（compile / play / stop）之后必然有一段不可达窗口。
#  绝不要再"等 15 秒试一次就判失败" —— 那是之前反复误报"服务连不上"的根源。
function Wait-Mcp {
    param([int]$TimeoutSec = 180, [int]$IntervalSec = 3)
    $sw = [Diagnostics.Stopwatch]::StartNew()
    while ($sw.Elapsed.TotalSeconds -lt $TimeoutSec) {
        $r = Invoke-Mcp "get_editor_status" "{}" 1        # 单次尝试，交给外层轮询
        if ($r -ne "FAILED") {
            #★ 用 Write-Host：进度信息走控制台，不混进管道（否则会被外层 Show-Text/正则吃掉）
            Write-Host ("MCP 可用（等待 " + [int]$sw.Elapsed.TotalSeconds + " 秒后恢复）")
            return $r
        }
        Start-Sleep -Seconds $IntervalSec
    }
    #★ 注意：这条必须写成**单行**。写成两行用 + 续行时，PowerShell 会把第 2 行当新语句，报"缺少右)"（实测踩过）
    Write-Host ("[!] MCP 在 " + $TimeoutSec + " 秒内仍未响应 → 先跑 probe 判定（进程/端口/Host/IDE注册）；不要直接下结论说服务挂了")
    return "FAILED"
}

function Get-Text {
    param([string]$Raw)

    if ($Raw -eq "FAILED") { return "MCP 调用失败（服务未响应）" }
    # 从 {"result":{"content":[{"type":"text","text":"..."}]}} 里取出 text 并还原转义
    $m = [regex]::Match($Raw, '"text":\s*"((?:[^"\\]|\\.)*)"')
    if (-not $m.Success) { return $Raw }
    return ($m.Groups[1].Value -replace '\\r\\n', "`n" -replace '\\n', "`n" -replace '\\r', "" -replace '\\"', '"' -replace '\\\\', '\')
}

function Show-Text {
    param([string]$Raw)
    Write-Output (Get-Text $Raw)
}

function Get-LogsRaw {
    param([string]$MaxCount = "80")
    return (Invoke-Mcp "get_unity_logs" ('{"maxCount":' + $MaxCount + ',"logLevel":"all","includeStackTrace":false,"searchText":""}'))
}

switch ($Cmd.ToLower()) {

    "status" {
        Show-Text (Invoke-Mcp "get_editor_status")
        Show-Text (Invoke-Mcp "get_play_mode_status")
    }

    "call" {
        if (-not $Arg1) { Write-Output "用法: call <tool> '<json>'"; break }
        if (-not $Arg2) { $Arg2 = "{}" }
        Show-Text (Invoke-Mcp $Arg1 $Arg2)
    }

    "probe" {
        Write-Output "== 1) Unity 进程（是否被重启过）=="
        (Get-Process -Name Unity -ErrorAction SilentlyContinue |
            Select-Object Id, StartTime | Format-Table -AutoSize | Out-String).Trim()
        Write-Output "== 2) 9123 监听状态 =="
        $ns = @(netstat -ano | Select-String ":9123")
        if ($ns.Count -eq 0) { Write-Output "未监听 → 项目守护 McpKeepAlive 会在 10 秒内自动拉起；先 wait 一轮再判定，不要让人去点面板" }
        else { $ns | Select-Object -First 4 | ForEach-Object { $_.Line.Trim() } }
        Write-Output "== 3) 请求方式对照（区分 Host 问题 vs 真断连）=="
        $body = '{"jsonrpc":"2.0","id":1,"method":"tools/list","params":{}}'
        $cases = @(
            @{ label = "localhost + Host头"; url = $McpUrl; hdr = $Headers; expect = "ok" },
            @{ label = "127.0.0.1 + Host头"; url = "http://127.0.0.1:9123/mcp"; hdr = $Headers; expect = "ok" },
            @{ label = "127.0.0.1 无Host（预期400）"; url = "http://127.0.0.1:9123/mcp";
               hdr = @{ "Accept" = "application/json, text/event-stream"; "Content-Type" = "application/json" }; expect = "400" }
        )
        foreach ($c in $cases) {
            try {
                $sw = [Diagnostics.Stopwatch]::StartNew()
                $r = Invoke-WebRequest -Uri $c.url -Method POST -Headers $c.hdr -Body $body -TimeoutSec 15 -UseBasicParsing
                $sw.Stop()
                $n = ([regex]::Matches($r.Content, '"name":')).Count
                Write-Output ("  " + $c.label + " => HTTP " + $r.StatusCode + " | " + $sw.ElapsedMilliseconds + "ms | tools≈" + $n)
            }
            catch {
                $msg = $_.Exception.Message
                $hint = ""
                if ($msg -match "400") { $hint = "  ← Host 头不匹配（不是服务挂了）" }
                elseif ($msg -match "拒绝|refused|无法连接|连接") { $hint = "  ← 端口不可达：多为域重载窗口，用 wait 轮询" }
                Write-Output ("  " + $c.label + " => " + $msg + $hint)
            }
        }
        Write-Output "== 4) IDE 侧健康检查（判断是否只是工具表陈旧）=="
        $lg = Get-ChildItem -Recurse -File "$env:LOCALAPPDATA\CodeBuddyExtension\Logs" -ErrorAction SilentlyContinue |
            Sort-Object LastWriteTime -Descending | Select-Object -First 1
        if ($lg -ne $null) {
            Select-String -Path $lg.FullName -Pattern "HealthPatrol|Rescan completed" -ErrorAction SilentlyContinue |
                Select-Object -Last 3 | ForEach-Object { "  " + $_.Line.Trim() }
        }
        Write-Output "== 结论规则：Healthy 正常 + 端口在听 + localhost 返回 200 → 服务没问题，"
        Write-Output "   报 tool does not exist 就是 IDE 工具表陈旧，直接用本脚本继续干活即可 =="
    }

    "wait" {
        $t = 180
        if ($Arg1) { $t = [int]$Arg1 }
        $r = Wait-Mcp $t
        if ($r -ne "FAILED") { Show-Text $r }
    }

    "compile" {
        Write-Output "== 触发编译 =="
        Show-Text (Invoke-Mcp "compile_scripts")
        Write-Output "== 等待域重载（轮询到恢复为止，不猜时间）=="
        Wait-Mcp 200 | Out-Null
        #★ 固定等待（必补）：编译**失败**时不会触发域重载 → Wait-Mcp 立刻返回，
        #  日志检查就会在 Unity 真正开始编译前执行；若此刻日志刚被清空，就会误报 "PASS：error CS = 0"，
        #  从而把"程序集编译不过 → Play 无法启动"误判成"没问题"（2026-09 实测踩过：探针缺方法，导致一整轮排查跑偏）。
        Start-Sleep -Seconds 8
        #★ 再轮询官方 Is Compiling，直到编译**真正结束**（编译失败时不会触发域重载，仅靠 Wait-Mcp 会读早）
        for ($i = 0; $i -lt 20; $i++) {
            $st = Invoke-Mcp "get_script_errors"
            if ($st -eq "FAILED" -or $st -match "Is Compiling: False") { break }
            Start-Sleep -Seconds 3
        }
        Write-Output "== 官方状态（不可全信：实测会漏报 CS 错误）=="
        $off = Invoke-Mcp "get_script_errors"
        if ($off -eq "FAILED") { Write-Output "（官方状态没读到：服务仍在重载，下面用全量日志为准）" }
        else { Show-Text $off }
        Write-Output "== 全量日志交叉验证 =="
        $logs = Get-LogsRaw
        if ($logs -eq "FAILED") {
            #★ 关键守卫：拿不到日志时 error CS 必然是 0，绝不能据此判 PASS（假的"验证通过"）
            Write-Output "日志未读到（重载窗口）→ 再等一轮后重跑本命令的日志部分：powershell -File tools/mcp.ps1 logs"
            break
        }
        $errs = [regex]::Matches($logs, "error CS\d+")
        if ($errs.Count -eq 0) {
            Write-Output "PASS：全量日志中 error CS = 0"
        }
        else {
            Write-Output ("FAIL：发现 " + $errs.Count + " 条编译错误")
            ([regex]::Matches($logs, "error CS\d+[^\\]{0,100}") | Select-Object -First 10 | ForEach-Object { $_.Value })
        }
    }

    "logs" {
        $txt = Get-Text (Get-LogsRaw)          #★ 先还原转义（否则整段 JSON 算一行，关键字过滤无效）
        if (-not $Arg1) {
            Write-Output $txt
        }
        else {
            $hits = @($txt -split "`n" | Where-Object { $_ -match [regex]::Escape($Arg1) })
            Write-Output ("匹配 " + $hits.Count + " 行：")
            $hits | ForEach-Object { $_.Trim() }
        }
    }

    "play" {
        Show-Text (Invoke-Mcp "play_mode_start")
        #★ 这里是历史上最耗时的一段：固定 sleep 后单次查询 → 状态没变就以为"服务没响应/操作没生效"。
        #  实际是「异步队列 + 域重载」两个窗口叠加。改为：等待服务恢复 → 轮询到真的 Is Playing: True。
        Wait-Mcp 200 | Out-Null
        $ok = $false
        for ($i = 0; $i -lt 12; $i++) {
            Start-Sleep -Seconds 5
            $st = Invoke-Mcp "get_play_mode_status" "{}" 3
            if ($st -eq "FAILED") { Wait-Mcp 60 | Out-Null; continue }
            if ((Get-Text $st) -match "Is Playing: True") {
                $ok = $true
                Write-Output ("已进入 Play（约 " + (5 * ($i + 1)) + " 秒）")
                break
            }
        }
        if (-not $ok) {
            Write-Output "[!] 60 秒内没进 Play → 先查编译错误（有 CS 错误时 Unity 会拒绝进入 Play，别怀疑 MCP）"
        }
        Show-Text (Invoke-Mcp "get_play_mode_status")
    }

    "stop" {
        Show-Text (Invoke-Mcp "play_mode_stop")
        Wait-Mcp 200 | Out-Null
        $ok = $false
        for ($i = 0; $i -lt 12; $i++) {
            Start-Sleep -Seconds 5
            $st = Invoke-Mcp "get_play_mode_status" "{}" 3
            if ($st -eq "FAILED") { Wait-Mcp 60 | Out-Null; continue }
            if ((Get-Text $st) -match "Is Playing: False") {
                $ok = $true
                Write-Output ("已退出 Play（约 " + (5 * ($i + 1)) + " 秒）")
                break
            }
        }
        if (-not $ok) { Write-Output "[!] 60 秒内没退出 Play（可再跑一次 stop）" }
        Show-Text (Invoke-Mcp "get_play_mode_status")
    }

    "report" {
        if (Test-Path $ReportFile) {
            [System.IO.File]::ReadAllText($ReportFile, [System.Text.Encoding]::UTF8)
        }
        else {
            Write-Output "还没有报告：" $ReportFile
        }
    }

    "autobattle" {
        $battles = 1
        if ($Arg1) { $battles = [int]$Arg1 }

        #★ 先退出 Play：标记文件是在 Play 启动时（RuntimeInitializeOnLoadMethod）读的，
        #  如果编辑器已经在 Play 里，写标记不会生效。
        Write-Output "== 先退出 Play（保证标记文件能被读到）=="
        Show-Text (Invoke-Mcp "play_mode_stop")
        Start-Sleep -Seconds 15

        New-Item -ItemType Directory -Force -Path $WorkshopDir | Out-Null
        #★ 必须清掉旧报告：等待循环是靠 done=1 判断结束的，留着旧报告会立刻"成功"返回上一次的结果
        if (Test-Path $ReportFile) { Remove-Item -Force $ReportFile }
        $cfg = '{"battles":' + $battles + ',"maxSecondsPerBattle":180,"stuckSeconds":40}'
        [System.IO.File]::WriteAllText($FlagFile, $cfg, (New-Object System.Text.UTF8Encoding($false)))
        Write-Output ("已写触发标记: " + $FlagFile + "  " + $cfg)

        Show-Text (Invoke-Mcp "play_mode_start")
        $limit = 20 + 190 * $battles
        Write-Output "== 等待自动对战结束：最多 $limit 秒 =="
        $wait = 0
        while ($wait -lt $limit) {
            Start-Sleep -Seconds 10
            $wait += 10
            if (Test-Path $ReportFile) {
                $txt = [System.IO.File]::ReadAllText($ReportFile, [System.Text.Encoding]::UTF8)
                if ($txt -match "done=1") { break }
            }
        }
        Write-Output "== 报告 =="
        if (Test-Path $ReportFile) {
            [System.IO.File]::ReadAllText($ReportFile, [System.Text.Encoding]::UTF8)
        }
        else {
            Write-Output "（等不到报告：可能标记文件没被读到，或对局卡住）"
            Write-Output "标记文件还在 = " (Test-Path $FlagFile)
        }
    }

    default {
        Write-Output "命令: status | compile | logs [关键字] | play | stop | autobattle [局数] | report | call <tool> '<json>'"
    }
}
