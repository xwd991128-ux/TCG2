---
name: unity-compile-before-play
description: TCG2（Unity 2022.3 + MCP 工具链）里"对局/Play 起不来"的头号原因是**存在编译错误**，而不是时序、标记文件或 GameLogic 找不到。本 skill 给出可靠的编译判据（先用 clear_unity_logs 清日志再 compile_scripts + 全量日志 grep `error CS`，条数必须为 0），并列出 4 个反复踩到的坑：只加引用没加方法体、replace_in_file 改 switch/case 留下残留块、被"全量日志交叉验证 PASS"（其实是上一次的日志）骗过、`Compilation Status: Errors: 0` 与日志里的 `error CS` 不一致。当出现"Play 起不来 / play_mode_start 被排队 / 探针不跑 / autobattle 没反应 / 没找到 GameLogic / mcp.ps1 compile 报 PASS 但没动静"时使用。
---

# Play 起不来？先证明编译干净（TCG2 项目）

## 一句话规则

> **只要项目里有 1 条编译错误（`error CS…`），Unity 就拒绝进入 Play** —— 表现是：
> `play_mode_start` 返回"**操作 '启动播放模式' 已加入异步执行队列，请10秒后重试**"、`get_play_mode_status` 一直 `Stopped`、
> 探针日志一条都没有、`autobattle.json` 写了也没人消费。
> **此时不要怀疑时序、标记文件、GameLogic、场景** —— 先查编译错误。这几样只在"编译干净"时才值得查。

## ⛔ 不可信的判据（别再被它们骗）

| 看起来可信 | 为什么不能信 |
|---|---|
| `tools/mcp.ps1 compile` 输出 `✅ No compilation errors or warnings found` | 它读的是**滚动日志/缓存状态**，会把上一次编译的结果报出来 |
| `get_script_errors` → `Compilation Status: Errors: 0` | 同上；实测出现过"状态 0 错误"但日志里有 `error CS0103` |
| 自己写的"全量日志交叉验证 PASS" | **上一次**编译的日志也会被一起读到 → 假 PASS |
| `git status` 干净 / 没动过文件 | 上一次会话留下的编译错误一样会让本次 Play 失败 |

## ✅ 可靠判据（照抄这段）

```powershell
Set-Location "e:\Program Files\Unity\unity projects\TCG2"

# 1) 清掉旧日志（关键：不清就会把上一次的错误/清一次 PASS 混在一起）
powershell -NoProfile -File tools/mcp.ps1 call clear_unity_logs '{}'

# 2) 强制刷新 + 触发真实编译
powershell -NoProfile -File tools/mcp.ps1 call refresh_editor '{}'
Start-Sleep -Seconds 3
powershell -NoProfile -File tools/mcp.ps1 call compile_scripts '{}'
Start-Sleep -Seconds 12      # 必须等，否则读到编译前的日志

# 3) 全量日志正则数 error CS —— 必须为 0
powershell -NoProfile -File tools/mcp.ps1 call get_unity_logs '{\"maxCount\":600,\"logLevel\":\"all\",\"includeStackTrace\":false,\"searchText\":\"\"}' > "$env:TEMP\cv.txt" 2>&1
$t = [System.IO.File]::ReadAllText("$env:TEMP\cv.txt",[System.Text.Encoding]::UTF8)
$m = [regex]::Matches($t, 'error CS\d+[^\\"]{0,180}')
"error CS 条数=" + $m.Count
$m | Select-Object -First 10 | ForEach-Object { $_.Value.Trim() }
if ($m.Count -gt 0) { throw "有编译错误，先修完再跑对局" }
```

**看到错误就直接改**（信息里通常带文件名/行号，例如
`error CS0103: The name 'IsHeroCard' does not exist in the current context`）：
去 `Assets/TcgEngine/Scripts/...` 对应行号修掉 → **重跑上面 1~3 步**，直到 0 条 → 再提测。

## 反复踩到的 4 个坑

1. **只加了引用，没加方法体**：写了 `if (IsHeroCard(logic, c))` 却没定义 `IsHeroCard` → `CS0103`。
   改结构前先确认 helper 是否存在（`search_content` 一下名字）。
2. **`replace_in_file` 改 switch/case 大块代码**：旧分支没删干净 / 重复 `case` / `case "__xxx_old"` 残留 → 编译错或语义错。
   改完**回读现场**（`read_file` 该区域）确认括号与分支完整。
3. **被"交叉验证 PASS"骗**：日志是滚动的，**上次**的 `error CS` 会一起读出来 → 假报警；
   而"没清日志就编译"又会假 PASS。**唯一解：先 `clear_unity_logs` 再编译再读**。
4. **`Compilation Status: Errors: 0` 与日志不一致**时，**以日志里的 `error CS` 为准**。

## 编译干净之后才查的（第二梯队）

- Play 起来了但探针不跑 → 看日志有没有 `[节点批量测试] 探针启动` / `❌ 没找到 GameLogic`
  （后者是"起 Play 的时机太早/上一局没退干净"→ 重试即可）。
- 自动对战没反应 → `{persistentDataPath}\Workshop\autobattle.json` 标记要在**起 Play 之前**写好；
  报告写在同目录 `autobattle_report.txt`、探针报告在 `tools/node_batch_result.txt`。

## MCP 掉线了？自己起，别让用户去点

**规则（用户 2026-09-22 明确要求）：等 5 秒没响应就自己启动服务器，不要每次都让用户去 Unity 里手动点「启动服务器」。**

现象与原因：每次 `compile_scripts` / 进出 Play 都会触发**域重载**，插件在重载时停掉 HttpListener，表现为
`fetch failed`、`tools/mcp.ps1 probe` 显示「9123 未监听」。插件自身的自动启动**不可靠**：

- `Packages/com.unity.ai-mcp-trae/Editor/McpServerWindow.cs:401-410` 用 `EditorApplication.delayCall` + `if (this != null)` 守卫；
  域重载会**重建 EditorWindow 实例**，该守卫会让 `StartServerWithRetry()` 静默不执行（日志只留「准备启动」，没有「启动成功」）。
- 同文件 `:427` 保存的 `McpServer.WasRunning` 恢复标记，会被 `StopServer()` 内的 `:1080` 立刻擦成 `false`。

已落地的兜底（项目侧，不动第三方包）：`Assets/TcgEngine/Scripts/Editor/McpKeepAlive.cs`
用 `[InitializeOnLoad]` 在每次域重载后检查端口，未监听且 `EditorPrefs["McpServer.AutoStart"]` 为 true 时调用插件公开的
`Unity.MCP.Editor.McpServerWindow.StartServerStatic()`（插件 asmdef 是 `autoReferenced: true`，项目侧可直接调用）。

处理顺序（不要再让用户点）：

```powershell
# 1) 先看端口在不在听（比任何"等待"都快）
netstat -ano | Select-String ':9123'
# 2) 不在听 → 自己等/起（McpKeepAlive 会在域重载后自动拉起；必要时轮询）
powershell -NoProfile -File tools/mcp.ps1 wait 60
# 3) 还不行才读 Editor.log 里最后一处 [McpServer] 启动记录判断原因
```

⛔ **不要再犯的错**：`wait 150` 超时就下结论说"服务挂了、请你去点一下"。实测域重载窗口可以超过 150 秒，
服务端往往随后自己恢复（Editor.log 出现 `[McpServer] MCP服务器启动成功，端口: 9123 (尝试 1/3)`）。
**probe 报「未监听」≠ 服务挂了**，继续轮询/查端口即可。

## 本项目的 MCP 快捷方式

```powershell
# 常用：停止 / 编译 / 状态 / 读日志
powershell -NoProfile -File tools/mcp.ps1 stop
powershell -NoProfile -File tools/mcp.ps1 call refresh_editor '{}'
powershell -NoProfile -File tools/mcp.ps1 call compile_scripts '{}'
powershell -NoProfile -File tools/mcp.ps1 call get_editor_status '{}'
powershell -NoProfile -File tools/mcp.ps1 call play_mode_start '{}'
```
