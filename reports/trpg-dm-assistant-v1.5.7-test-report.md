# TRPG DM Assistant v1.5.7 测试报告

- 发布基线：main `c6058478deaa81acf013c7c4513d4576eec526a4`
- 产品版本：v1.5.7
- Save Schema：8
- AI protocol：1.3
- 新增 Case Integrity / Interaction Availability 回归：34 PASS / 0 FAIL
- 完整确定性回归：259 PASS / 0 FAIL（v1.5.6 的 225 + 本版 34）
- JavaScript 语法检查：PASS
- 单文件构建与 verify-single-html：PASS
- 连续两次构建一致性：PASS
- 唯一正式 HTML 检查：PASS
- `git diff --check`：PASS
- 真实 API 消耗：0（本版未使用 DS_KEY）

## Case Integrity Validator

1. 在剧本正式写入运行态前执行静态完整性检查。
2. ERROR 只用于可证明的结构损坏，例如不存在的出口目标、缺失的 route 必填引用、重复关键 ID；ERROR 阻止坏剧本替换当前运行案件。
3. WARN/INFO 用于不可达节点、动态 NPC/线索/flag 来源无法静态证明、结局条件脆弱、线索依赖环等风险；这些结果不会阻止玩家开始游戏。
4. 检查关键线索单次检定且没有 failure_forward / automatic / clue / npc / flag 备选路线的 soft-lock 风险。
5. 检查节点拓扑可达性、线索获取路线、依赖环和静态 Ending 可满足性。
6. 完整报告写入 `state.runtime.caseIntegrity`，WARN 同步进入 `scenario.warnings`。

## Interaction Availability Invariant

核心规则：`BLOCK UNSAFE STATE, NOT PLAYER ACTION`。

1. v1.5.6 的 `PLAYER_ASSERTION_UNGROUNDED_RESULT` 与 `PLAYER_ACTION_CHAIN_OVERRESOLVED` 不再自动把正常玩家回合变成技术失败。
2. 基础 AI protocol 校验仍先执行；只有这两个已知 Guard 越权错误可进入 graceful recovery，未知 operation 等真正协议错误仍严格拒绝。
3. 恢复时剥离未经授权的后续移动、物品、线索或结局状态，只保留第一个合法步骤允许的状态。
4. 恢复叙事不会复述玩家未验证的世界事实/NPC 台词，避免把伪造断言重新写进聊天上下文。
5. 检定前抢跑结果会被中和，但合法 check 本身保留，玩家仍可继续掷骰。
6. 合法单步移动、普通调查、等待和无收益行动不会触发 recovery。

## 回归结论

- 所有内置剧本通过完整性检查且无 blocking error。
- 潜在 soft-lock 与动态来源无法静态证明时默认告警而非封禁，避免过度防御。
- Guard 恢复后仍可进入安全事务准备，不需要额外 AI 请求。
- v1.5.6 原始 Guard 专项仍保持 25 PASS / 0 FAIL；新层是在其外部增加可玩性恢复，不放宽 canonical state 权限。
