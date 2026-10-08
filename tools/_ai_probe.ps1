Set-Location 'e:\Program Files\Unity\unity projects\TCG2'

$all = Select-String -Path 'tools\_logs_dump.txt' -Pattern '"message":' | Select-Object -ExpandProperty Line
$game = $all | Where-Object { $_ -notmatch 'McpServer|Unity\.MCP|MainThreadDispatcher' }

'game lines        = ' + $game.Count
'AI: Time          = ' + ($game | Select-String -Pattern 'AI: Time').Count
'Execute AI Action = ' + ($game | Select-String -Pattern 'Execute AI Action').Count
'Nodes 102         = ' + ($game | Select-String -Pattern 'Nodes 102').Count
''
'== any error/exception lines in dump =='
$all | Select-String -Pattern 'Exception|error|Error|Assert' | Select-Object -First 15

''
'== ordered tail: AI + Execute + turn/state lines =='
$game | Select-String -Pattern 'AI: Time|Execute AI Action|EndTurn|Turn|回合|Winner|End Game' | Select-Object -Last 45
