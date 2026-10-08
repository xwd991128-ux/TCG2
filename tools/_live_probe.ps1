Set-Location 'e:\Program Files\Unity\unity projects\TCG2'

powershell -NoProfile -File tools/mcp.ps1 call get_unity_logs '{\"maxCount\":4000,\"logLevel\":\"all\",\"includeStackTrace\":false,\"searchText\":\"\"}' > tools\_logs_dump.txt 2>&1

# 只留游戏侧日志（滤掉 McpServer / Unity.MCP 噪音）
$game = Select-String -Path 'tools\_logs_dump.txt' -Pattern '"message":' |
        Select-Object -ExpandProperty Line |
        Where-Object { $_ -notmatch 'McpServer|Unity\.MCP|MainThreadDispatcher' }

'game-side log lines = ' + $game.Count
''
'== counts by keyword =='
'AI: Time      = ' + ($game | Select-String -Pattern 'AI: Time' -AllMatches).Count
'AI(any)       = ' + ($game | Select-String -Pattern 'AI' -AllMatches).Count
'Draw/CardUI   = ' + ($game | Select-String -Pattern 'CardUI|cardui' -AllMatches).Count
''
'== last 40 game-side lines =='
$game | Select-Object -Last 40
