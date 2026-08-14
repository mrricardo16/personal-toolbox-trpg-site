# DESIGN_PRINCIPLES

## 1. 文档定位

本文件记录项目中不应被普通重构轻易破坏的设计原则。

规范词：

- `MUST`：必须。
- `MUST NOT`：禁止。
- `SHOULD`：强烈建议，偏离需有明确理由。
- `MAY`：可选。

---

## Principle 1 — AI 负责叙事，程序负责规则

**Principle**

AI SHOULD 负责自然语言理解、NPC、场景和叙事。  
Deterministic mechanics MUST 由程序拥有最终裁决。

**Reason**

LLM 擅长开放语言和叙事，不适合作为不可审计的骰子、HP、SAN 或 combat state authority。

**Implication**

Check、骰点、SAN、HP、Combat、Damage、Firearms 等规则结果必须由可信程序确认后，AI 再叙述。

**Typical violation**

让 AI 输出“你受到 7 点伤害”，然后客户端直接 `hp -= 7`，没有规则验证。

---

## Principle 2 — AI 不拥有 Canonical Game State

**Principle**

AI response MUST 被视为 proposal，不是最终状态。

**Reason**

AI 会出错、漏字段、产生旧响应、误解玩家断言或 provider failure。

**Implication**

AI result 必须经过 protocol normalization、validation、transaction preparation 和 commit。

**Typical violation**

把 LLM JSON 直接 merge 到 `state` / Server GameState。

---

## Principle 3 — 玩家自由描述，但玩家描述不是世界事实

**Principle**

玩家 MAY 自由说“我找到了钥匙”“我击败了怪物”。  
这些语句 MUST NOT 自动成为 world fact。

**Reason**

真实 TRPG 中玩家声明行动，规则与主持决定结果。限制措辞会破坏体验，直接接受结果又破坏 authority。

**Implication**

Player Action Guard / Server action pipeline 应把输入理解为 intent。

**Typical violation**

因输入包含“已经”就拒绝整句话；或相反，直接把“我已经拿到枪”写入 items。

---

## Principle 4 — BLOCK UNSAFE STATE, NOT PLAYER ACTION

**Principle**

系统 MUST 阻止未经授权或未经规则结算的 state commit，而不是限制玩家尝试。

**Reason**

安全边界应位于 authority/commit layer，而不是自然语言表面。

**Implication**

不安全的 AI stateChanges、premature HP、protected clue、错误 location transition 可以被剥离或拒绝，同时保留合法叙事与玩家行动。

**Typical violation**

为防止作弊，创建一大套关键词黑名单禁止玩家描述动作。

---

## Principle 5 — Deterministic Rules 优先

**Principle**

可以用确定规则表达的机械 SHOULD 尽量由 deterministic code 执行。

**Reason**

可测试、可复现、可比较、可迁移。

**Implication**

RNG 也需要明确 authority。Single Player 是 Browser RNG；Multiplayer 最终是 Server RNG。

**Typical violation**

让 AI 自己判断 COC success level、Damage Bonus 或 Armor。

---

## Principle 6 — Structured Proposal → Validation → Commit

**Principle**

AI integration MUST 维持结构化提议、验证、提交的事务边界。

**Reason**

防止 partial state corruption 和 stale response。

**Implication**

Multiplayer 不应因为改为 Server 就退回“AI response text parsing + direct mutation”。

**Typical violation**

SignalR 收到 AI 文本后直接广播并修改 Room state。

---

## Principle 7 — 已验证规则行为优先保护

**Principle**

已有 regression 证明的行为 MUST 视为迁移 specification。

**Reason**

当前 Single Player 已积累大量规则和边界修复；重写会重新引入旧 bug。

**Implication**

JS→Server 迁移 SHOULD 使用 golden/conformance tests，比较行为而不是仅比较代码结构。

**Typical violation**

“C# 更干净，所以重新按规则书写一版”，但没有 parity 测试。

---

## Principle 8 — 不为了换框架重写成熟规则

**Principle**

Vue、ASP.NET Core、SignalR 是解决 Multiplayer 新问题的工具，不是重写 Single Player 的理由。

**Reason**

UI framework migration 与 authority migration 是两个独立风险。

**Implication**

Single Player 保持现有 build 和 regression；Multiplayer Client 独立使用 Vue。

**Typical violation**

先把 `ui.js`、rules、save、scenario 全部改成 Vue composition API，再开始 Multiplayer。

---

## Principle 9 — Multiplayer Server Authoritative

**Principle**

Multiplayer canonical Room/Game state、可信 RNG 和 mechanics MUST 由 Server 持有。

**Reason**

多个 Client 不能同时成为同一共享世界的最终 authority。

**Implication**

Client 发送 intent/command；Server 验证、结算、commit、projection、broadcast。

**Typical violation**

玩家 A Browser 掷骰后告诉 Server “我成功了”，Server 不复核直接接受。

---

## Principle 10 — Canonical State 与 Player-visible Projection 分离

**Principle**

Server MUST NOT 默认广播整个 canonical state。

**Reason**

内部状态包含 hidden clue、secret result、director info、raw provider response 等不应被玩家读取的数据。

**Implication**

Multiplayer 需要 projection/snapshot boundary。

**Typical violation**

`Clients.Group(roomId).SendAsync("State", room.GameState)`。

---

## Principle 11 — API Credential 不属于 Game State

**Principle**

API Key MUST 与 Room/Game state 物理和语义分离。

**Reason**

Game State 会被序列化、广播、日志化、保存或调试。

**Implication**

Credential 使用 Server-private store；Room 只保存 provider/model/endpoint/credential-present 等非 secret metadata。

**Typical violation**

`RoomSettings.ApiKey` 被包含在 RoomSnapshot DTO。

---

## Principle 12 — Secret Data 不得广播

**Principle**

Credential、hidden scenario data、secret checks、raw provider response、internal diagnostic MUST NOT 进入普通 Client snapshot。

**Reason**

一旦发送到 Browser，就不能认为它仍是 secret。

**Implication**

使用 projection DTO；增加“secret absence”测试。

**Typical violation**

前端只是“不显示” hidden clue，但 payload 中仍存在该字段。

---

## Principle 13 — Disconnect ≠ Leave

**Principle**

Transient SignalR disconnect MUST NOT 自动被解释为 explicit leave / room end。

**Reason**

网络波动、浏览器切换、移动网络都会导致短断线。

**Implication**

使用 PlayerSessionToken + reconnect recovery；Room/Credential 清理依据明确 lifecycle 或 timeout。

**Typical violation**

Host `OnDisconnectedAsync` 立即删除 Room 和 API key。

---

## Principle 14 — 小步迁移

**Principle**

Multiplayer MUST 以垂直、小范围、可验证的阶段推进。

**Reason**

当前系统已有稳定行为；一次性跨 UI、Server、rules、AI、persistence 的变更不可审计。

**Implication**

Phase 1 只做基础设施；Phase 2 才迁 shared game state/rules；Phase 3 才接 Multiplayer AI KP。

**Typical violation**

一个 PR 同时创建 Vue、ASP.NET Core、数据库、AI、Combat Multiplayer。

---

## Principle 15 — 小 Commit

**Principle**

工程提交 SHOULD 一个 commit 对应一个可解释能力。

**Reason**

方便回滚、review、bisect 和 Codex handoff。

**Implication**

例如 server skeleton、room store、create room、join、ready、reconnect 分开提交。

**Typical violation**

`feat: multiplayer` 修改几十个模块。

---

## Principle 16 — 测试保护迁移

**Principle**

现有 Single Player tests MUST 保留；新 Multiplayer tests 在其旁边增加。

**Reason**

旧 tests 是稳定产品的 safety net，不是“旧技术债”。

**Implication**

CI 未来同时运行 JS regression 与 .NET/Vue tests。

**Typical violation**

为了让新架构通过，删除旧 JS test 或将旧 test 全部替换成新的 C# test。

---

## Principle 17 — Provider Failure Fail Closed

**Principle**

AI provider empty/timeout/network/invalid response MUST NOT 自动产生游戏事实。

**Reason**

Transport failure 和 game resolution 是不同事件。

**Implication**

保留 retry/resilience 的语义；retry exhaustion 后恢复可用状态，不伪造成功剧情。

**Typical violation**

AI 超时后为了“继续体验”自动生成一个默认成功结果。

---

## Principle 18 — SignalR Group 不是 Source of Truth

**Principle**

SignalR group SHOULD 只负责消息路由。Room membership 和 state MUST 存在 Room service/store。

**Reason**

Connection group 是 transport concept，不是 domain persistence。

**Implication**

Hub 保持 thin；RoomCoordinator/RoomStore 决定实际 membership。

**Typical violation**

只通过 `Groups.AddToGroupAsync` 判断某人是否是 Room member。

---

## Principle 19 — Repository Facts 以 Current Main 为准

**Principle**

Version、Schema、Protocol、test count、file tree 等 implementation facts MUST 在执行前从 repository 验证。

**Reason**

正式文档会过期，聊天更会过期。

**Implication**

Codex 进入任何迁移任务时先 inspect current main。

**Typical violation**

因为 Architecture Audit 写了 v1.6.10，就在未来仍硬编码 v1.6.10。

---

## Principle 20 — Future ≠ Current Task

**Principle**

Matchmaking、Accounts、PlatformProvided AI、Cloud Save、Billing、Redis、scale-out 等未来能力 MUST NOT 被默认拉入当前 Phase。

**Reason**

避免过度设计和 scope creep。

**Implication**

除非当前阶段明确需要，不创建为未来功能准备的大型抽象。

**Typical violation**

Phase 1 Room Store 先引入 PostgreSQL + Redis + distributed event bus。
