# TRPG DM Assistant v1.5.7 Canonical Assertion Hotfix

## 问题来源

v1.5.7 重复真实 DeepSeek E2E 在第 5 轮发现：玩家输入 `管家告诉我凶手是医生。` 时，模型有机会不在 narrative 中直接确认该说法，却通过 canonical state operation 写入相同事实。

原 Player Assertion Guard 主要检查 narrative 是否确认未验证断言；因此 `updateNpc`、`addPinnedFact`、`addRevealedTruth` 等状态写入存在绕过路径。

## 修复

- 在 Interaction Availability 层新增 canonical assertion boundary。
- 对玩家未验证的 `npc_claim` / `world_fact`，同时检查 narrative 与 canonical state proposal。
- 匹配到玩家未验证断言时，使用既有 `PLAYER_ASSERTION_UNGROUNDED_RESULT` graceful recovery：剥离非法状态，但继续玩家回合。
- 覆盖 `updateNpc` / `addNpc` 与 NPC continuity 文本，以及 `addPinnedFact` / `addRevealedTruth`。
- 与玩家断言不一致的真实 NPC 回应继续允许；正常询问不受影响。
- 继续遵守 `BLOCK UNSAFE STATE, NOT PLAYER ACTION`。

## 确定性验证

- v1.5.6 Player Assertion Guard：25 PASS / 0 FAIL
- v1.5.7 Case Integrity / Interaction Availability：34 PASS / 0 FAIL
- 新增 Canonical Assertion State：7 PASS / 0 FAIL
- JavaScript syntax：PASS
- single HTML build / verify：PASS
- 连续两次构建一致：PASS
- 构建产物大小：422363 bytes

新增 7 条专项回归覆盖：

1. `updateNpc.claim` 不能写入玩家自报 NPC 事实。
2. `updateNpc.description` 不能绕过。
3. `addPinnedFact` 不能绕过。
4. `addRevealedTruth` 不能绕过。
5. world fact 不能经 pinned fact 注入。
6. 与玩家断言不匹配的 NPC 回应不被误杀。
7. 普通询问仍允许 NPC 状态更新。

## 真实 DeepSeek E2E

Focused canonical guard acceptance：5 个有效模型响应全部通过。

- Attempt 1：PASS，recovery=none
- Attempt 2：PASS，recovery=PLAYER_ASSERTION_UNGROUNDED_RESULT
- Attempt 3：PASS，recovery=none；应用内部发生一次空响应自动重试
- Attempt 4：PASS，recovery=PLAYER_ASSERTION_UNGROUNDED_RESULT
- Attempt 5：PASS，recovery=none

最终：`passes=5 attempts=5 providerFlakes=0`。

其中 2 / 5 次真实模型输出实际触发 Guard recovery，证明 canonical assertion 防线在真实模型越权输出上执行，而不是仅靠模型主动守规矩。

## 独立发现：Provider 空 final content

更广的 5×3 重复 E2E 还观察到 DeepSeek 偶发连续两次返回空 final content，应用最终产生 `AI_RESPONSE_JSON_PARSE_FAILED`。该现象与 canonical assertion hotfix 独立：

- 当前已有一次自动空响应重试；
- 连续两次空内容仍会进入现有错误恢复流程；
- 本 hotfix 不把 provider 空响应伪装成 Guard 成功或失败。

建议后续单独处理 API reliability / empty-final-content resilience。

## 兼容性

- APP_VERSION：1.5.7（不变）
- Save Schema：8（不变）
- AI protocol：1.3（不变）
- 不新增正常跑团 AI 请求。
