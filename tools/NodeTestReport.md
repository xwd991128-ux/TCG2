# 规则图节点实测报告（全量定版）

> 对象：NodeDoc 规则图节点 × 运行期实现（`NodeDocRunner`）
> 结果：**309 用例（301 节点 + 回归族）→ 309/309 通过** ✓（F13/F14 修复后复验）
> 证据：`tools/report_all.txt`（逐条测法 + 观测值）｜用例：`tools/all_cases.tsv` + `tools/curated.tsv`

## 一、怎么跑（可复现）

```powershell
# 1) 探针（临时测试脚本，不在 Assets 里，需先拷回 Assets 才能跑）
Copy-Item tools\probe\NodeBatchProbe.cs Assets\TcgEngine\Scripts\__TempProbe\NodeBatchProbe.cs
# 2) 生成用例：301 节点自动生成 + curated.tsv 人工覆盖
powershell -NoProfile -File tools\gen_all_cases.ps1 -Chunks 1
powershell -NoProfile -File tools\gen_batch.ps1 -Overrides tools\all_cases.tsv -Out tools\node_batch.json
# 3) 编译校验（必须先过：Play 起不来的头号原因是编译错误）
powershell -NoProfile -File tools\mcp.ps1 call clear_unity_logs '{}'
powershell -NoProfile -File tools\mcp.ps1 call refresh_editor '{}'; Start-Sleep 3
powershell -NoProfile -File tools\mcp.ps1 call compile_scripts '{}'; Start-Sleep 14
powershell -NoProfile -File tools\mcp.ps1 call get_script_errors '{}'   # 必须 "No compilation errors"
# 4) 进 Play（探针自启动，跑完自动写 tools\node_batch_result.txt）
powershell -NoProfile -File tools\mcp.ps1 call play_mode_start '{}'
```

> 报告判据：`tools\node_batch_result.txt` 首行含**批次名**才算本次结果（时间戳不可靠，实测会被上一次报告骗过）。

## 二、覆盖口径

| 维度 | 数量 | 说明 |
|---|---|---|
| 节点总数 | **301** | 清单 `tools/node_inventory.tsv`（NodeDoc 全量） |
| 用例总数 | **306** | 301 节点 + 5 条同节点不同路径（F4/F11/区域名归一/负向对照） |
| 按分类 | 卡牌76 / 玩家36 / 集合运算35 / 卡牌定义32 / 事件26 / 其他18 / 卡牌快照16 / 效果11 / 增益10 / 行动8 / 回合8 / 映射7 / 事件记录7 / 游戏5 / 牌堆5 / Buff1 | 全分类无遗漏 |

**断言类型**：`value_*`（严格非空，空串不算有值）/ `list_*`（集合元素数）/ `dyn`（与运行期真实属性比对）/ `run_bool·run_int`（在真实 Run 内用副作用观测）/ `damage·heal·state_*`（写入类副作用）/ `log` / `nobreak`。

## 三、重点验证（本轮新增/复验）

| 主题 | 测法 | 实测 |
|---|---|---|
| **F11 增益端到端** | 106002 按下拉 id 取定义 / 206001 字段路径 / 206001 buffDefine←106002 接线路径 / 106003 复读 | 均 ✓：`BuffData` 取到；两条路径都真的施加（`HasBuff=true`）✓ |
| **F4 牌堆族** | 105002（英雄区恰 1 张）、105003（牌堆名=牌库）、105004（所属玩家） | ✓ 全部命中 |
| **区域名归一** | 101017 的 pileName = 装备区/奥秘区/暂存区/英雄 | `装备区→装备`、`奥秘区→奥秘` ✓ |
| **集合运算** | 求和/最值/均值/属性映射/前X个/之外/随机/子集/内容相同/遍历循环体 | 与手牌实值一致（求和 19、最大 9、最小 0、平均 3、属性映射 [9,4,2,4,0]）✓ |
| **控制流** | 212002 重复2次+循环体伤害1 → 伤害 2；212005 条件恒假+上限2 → 伤害 2 | ✓ |
| **回归族** | F1/F2/F3/F5/F6/F7/F8/F9/F10/F12 代表用例 | ✓（F1 见下方待查） |

## 四、本轮修掉的"测试台自身"问题（影响结论可信度）

1. **动作/取值分类**：原判据全文搜 `case "id":`，把取值器里的 case 也当动作 → `102018` 等被误判、走缺省断言**假通过** ✗。改为只认 `ExecuteAction` 方法体内（L2142~L3904）✓
2. **`exec` 护栏**：日志出现「未支持的 NodeDoc 动作」即失败，避免"动作数≥1 只是入口推进"的假通过 ✓
3. **类型匹配全等**（仅 `NodeValueRef<...>` 按包含）：修掉 `CardSnapshot` 口被 `Card` 来源误接（104006~104009 全空）✓
4. **补通道**：`BuffDefine` / `Pile` / 非卡牌集合的 `ConvertTo{Defines,Buffs,Events}` 兜底 ✓

> 教训（与 skill `unity-compile-before-play` 一致）：**报"某族全挂"之前，先证明来源线真的接上、通道真的是这一类**。本轮 4 次"失败"最终都定位为测试台问题。

## 五、产品侧改动（本轮）

| 文件 | 改动 |
|---|---|
| `Assets/TcgEngine/Scripts/Workshop/ZoneNames.cs` | **新增**：区域（牌堆）名唯一口径（显示名/内部名/别名 + IsListZone/IsDataZone/ToDisplay） |
| `NodeDocRunner.cs` | `PileNormalize` 委托 `ZoneNames.Normalize`；`PileList`/`ZoneCards`/`GetCardPileName`/`PileMoveCard` 改用常量与 `IsDataZone`（未知区域现在会明确拒绝并告警，不再静默返回空）|
| `NodeDocRunner.cs`（F13） | `102018 是否濒死`：英雄识别改用 `CardData.type == CardType.Hero`（同 `GameLogic.IsHeroCard`），英雄读所属玩家 `Player.hp` → 布尔口/条件口不再恒真 |
| `NodeDocRunner.cs`（F14 加固） | `GetObjectInput` 补 `106006` 分支（卡上所有增益走 `ResolveBuffList`）→ 该节点接到 Object 口也能取到，不再返回空串 |
| `GraphEditorPanel.cs` | 4 处区域下拉（区域选项 / 生效区域多选 / 增益属性牌堆）统一取 `ZoneNames.Display*` |

## 六、F13 / F14（已闭环）

| # | 结论 | 处理 |
|---|---|---|
| **F13** | **真缺陷，已修**：`102018 是否濒死` 的英雄识别原用"与当前英雄实例同一对象"（`owner.hero.uid == card.uid`）→ 来源卡一换实例就漏判、退回 `GetHP()<=0`（英雄卡 hp 字段恒 0）→ **布尔口/条件口恒为真**（条件分支永远走"真"分支，实测伤害 7）| 改用引擎自己的判据 **`CardData.type == CardType.Hero`**（与 `GameLogic.IsHeroCard` 同口径），英雄一律读**所属玩家 HP**；用例成对验证：存活态（双方 HP=30）→ false ✓、濒死态（双方 HP=0）→ true ✓ |
| **F14** | **非缺陷**（测试台两处原因）：① 探针没有 `Buff` 通道（`106006` 的实现是 `ResolveBuffList`，此前落到 Object 通道得空串）；② 长会话里前面的「沉默(202015)/消灭(202016)」用例会**清掉卡上增益**（顺序依赖）→ 全量里返回空、单独跑却正常 | 探针补 `Buff` 通道；用例改为**自带前提**（本用例内重新施加一次，池被清空时先 `LoadAll` 重载）→ 实测 `caster.buffs=1` / `ResolveBuffList 结果=1` ✓；顺带给产品 `GetObjectInput` 补了 `106006` 分支，Object 口也能取到卡上增益 |

> 这两条也再次印证那条教训：**报"某节点坏了"之前，先证明来源线真的接上、通道真的是这一类、前提（状态）真的成立**。
