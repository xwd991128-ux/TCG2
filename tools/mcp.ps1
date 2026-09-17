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
#  用法（在项目根目录）：
#    powershell -File tools/mcp.ps1 status
#    powershell -File tools/mcp.ps1 compile
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
$Headers = @{ "Accept" = "application/json, text/event-stream"; "Content-Type" = "application/json" }
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

    "compile" {
        Write-Output "== 触发编译 =="
        Show-Text (Invoke-Mcp "compile_scripts")
        Write-Output "== 等待域重载 =="
        Start-Sleep -Seconds 30
        Write-Output "== 官方状态（不可全信：实测会漏报 CS 错误）=="
        $off = Invoke-Mcp "get_script_errors"
        if ($off -eq "FAILED") { Write-Output "（官方状态读取失败：服务正在域重载，稍后自动以全量日志为准）" }
        else { Show-Text $off }
        Write-Output "== 全量日志交叉验证 =="
        $logs = Get-LogsRaw
        if ($logs -eq "FAILED") {
            #★ 关键守卫：拿不到日志时 error CS 必然是 0，绝不能据此判 PASS（假的"验证通过"）
            Write-Output "无法读取 Console 日志（MCP 未响应）→ 本次结论不可信，请重跑 compile"
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
        Start-Sleep -Seconds 20
        Show-Text (Invoke-Mcp "get_play_mode_status")
    }

    "stop" {
        Show-Text (Invoke-Mcp "play_mode_stop")
        Start-Sleep -Seconds 15
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
