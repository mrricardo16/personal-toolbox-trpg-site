# TRPG DM Assistant v1.5.6 测试报告

- 发布基线：main `2ee287cc13091256bf7c21cf380ea776fe3cc33b`
- 产品版本：v1.5.6
- Schema：8
- 新增 Player Assertion Guard 回归：25 PASS / 0 FAIL
- 完整确定性回归目标：225 PASS / 0 FAIL（v1.5.5 的 200 + 本版 25）
- JavaScript 语法检查：PASS
- 单文件构建与 verify-single-html：PASS
- 连续两次构建一致性：PASS
- `git diff --check`：PASS

## 本版边界

1. 玩家原话永久视为非权威输入：完成式“我找到了 / 拿到了 / 进入了 / NPC 告诉我”等不会自动成为 trueState。
2. 显式完成式结果会生成 Player Action Guard 元数据；AI 必须重新裁决，而不是顺着玩家前提续写。
3. 多步指令按 first unresolved consequential step 处理；后续步骤只作为条件计划，不能在同轮自动执行。
4. 检定续写只结算当前 checkRecord，不自动执行原句后半段。
5. 地点仍使用既有 nodeProposal / targetNodeId / 合法出口 / 连续性校验；本版不放宽地点权限。
6. 未建立的世界事实、NPC 台词和发现结果若被 AI 直接确认，会以 PLAYER_ASSERTION_UNGROUNDED_RESULT 拒绝。
7. 结构化状态与既有合法线索路线仍是最终依据；不新增第二次 AI 解析请求，不增加 API 消耗。

## 兼容性

- AI protocol 仍为 1.3。
- Save Schema 仍为 8。
- 普通自然语言、无收益行动、合法单步移动保持兼容。
- 本版只收紧玩家通过完成式语言注入结果，以及多步行动越过前置步骤的路径。
