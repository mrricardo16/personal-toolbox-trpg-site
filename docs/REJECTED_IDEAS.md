# REJECTED_IDEAS

> 只记录历史明确讨论并被否决或后续替代的方案，防止 Agent 搜到旧聊天后再次带回旧路线。

## 1. 全部重写为 Vue

**Idea**：Single Player 与 Multiplayer 一起全量迁移 Vue 3。  
**Why it was considered**：Multiplayer UI state 更复杂，Vue 更适合现代 frontend。  
**Why it was rejected**：会重写 stable UI/Save/rules wiring；不能解决 Server Authority；破坏 regression 风险高。  
**What replaced it**：Single Player 保持现状；仅 Multiplayer Client 用 Vue 3 + Vite + TypeScript。  
**Can it be reconsidered later?** MAY，但不能作为 Multiplayer 前置条件。

## 2. 在现有 Vanilla UI 上直接叠完整 Multiplayer

**Idea**：继续扩展 `ui.js + global state` 增加 Room、SignalR、members、reconnect。  
**Why it was considered**：少引入新技术，可复用现有 UI。  
**Why it was rejected**：会把 Single Player runtime、network runtime、Room/Game state 混合，破坏稳定产品。  
**What replaced it**：独立 Vue Multiplayer Client。  
**Can it be reconsidered later?** 不建议；只复用视觉/interaction ideas。

## 3. Browser 继续作为 Multiplayer Canonical State Owner

**Idea**：由 Host Browser 维护最终 game state，再同步给别人。  
**Why it was considered**：现有 Browser rules 已成熟。  
**Why it was rejected**：多 Client authority、断线、作弊、状态分叉和 secrets 无法可靠解决。  
**What replaced it**：Server Authoritative。  
**Can it be reconsidered later?** 不应作为当前 Multiplayer 模式重新采用。

## 4. 每个玩家分别使用自己的 AI

**Idea**：每个玩家携带自己的 API key 独立调用 AI。  
**Why it was considered**：费用分摊，看似简单。  
**Why it was rejected**：产生多个 AI contexts 和多份世界推进。  
**What replaced it**：Host Provided API / BYOK；一个 Room 一个 AI KP context。  
**Can it be reconsidered later?** 仅可作为完全不同产品模式，不得破坏 canonical shared world。

## 5. Phase 1 立即加入 Database

**Idea**：Room/Game/Credential 首日进入 PostgreSQL 等持久化。  
**Why it was considered**：更像生产系统，restart 不丢数据。  
**Why it was rejected**：Phase 1 验证目标不需要；增加 schema/migration/deploy complexity。  
**What replaced it**：InMemory Room/Credential。  
**Can it be reconsidered later?** YES，真实需要 persistent rooms/cloud save 时。

## 6. Phase 1 立即加入 Redis / Azure SignalR / Distributed Architecture

**Idea**：第一版直接按多实例高并发设计。  
**Why it was considered**：为未来 scale 准备。  
**Why it was rejected**：没有真实 scale requirement；InMemory canonical state 本身对应 single-instance MVP。  
**What replaced it**：single process / single instance。  
**Can it be reconsidered later?** YES，出现 HA/scale-out 需求后。

## 7. Sites 直接承担 Authoritative Multiplayer Runtime

**Idea**：ChatGPT Sites 承载完整联机 backend。  
**Why it was considered**：发布方便、快速呈现。  
**Why it was rejected**：当前 canonical architecture 明确需要 ASP.NET Core + SignalR；不能建立在未验证 runtime 假设上。  
**What replaced it**：ASP.NET Core authoritative runtime；Sites future optional frontend/demo。  
**Can it be reconsidered later?** presentation role 可验证，authoritative backend 当前不采用。

## 8. 一次性迁移所有 Rules 到 Server

**Idea**：Scenario/Check/SAN/HP/Combat/Damage/Firearms 一次重写。  
**Why it was considered**：更快达到完整 Multiplayer。  
**Why it was rejected**：892 regression + global/decorator coupling 使跨语言大重写不可控。  
**What replaced it**：Phase 2 vertical slices + golden/conformance。  
**Can it be reconsidered later?** 不建议。

## 9. 用 .NET Tests 替换 Existing JS Tests

**Idea**：迁 Server 后删除旧 JS regression。  
**Why it was considered**：统一技术栈。  
**Why it was rejected**：旧 tests 是稳定 Single Player specification。  
**What replaced it**：Existing JS release gate + new Multiplayer tests。  
**Can it be reconsidered later?** 只有 Single Player 正式退役且另行决策时。

## 10. Multiplayer 数据直接扩展 Save Schema 8

**Idea**：Schema 8 直接加入 Room/members/connection。  
**Why it was considered**：复用 save code。  
**Why it was rejected**：Schema 8 是 Single Player Browser Snapshot；Multiplayer 有 Server runtime/multiple actors。  
**What replaced it**：future standalone Multiplayer snapshot schema。  
**Can it be reconsidered later?** 可做 adapter，不应污染 Schema 8。

## 11. Phase 1 同时做 Matchmaking / Accounts / Billing

**Idea**：第一版做完整联机平台。  
**Why it was considered**：长期可能需要。  
**Why it was rejected**：当前先验证 Room 与 Server Authority。  
**What replaced it**：Future roadmap。  
**Can it be reconsidered later?** YES，到对应产品阶段。

## 12. Phase 1 继续留在 personal-toolbox，稳定后再拆 Repo

**Idea**：Architecture Audit 最初决定 Phase 1 暂留 monorepo。  
**Why it was considered**：避免 repo move + Vue + .NET + SignalR 同时发生，且现有 CI 已 path scoped。  
**Why it was superseded**：后续 Bootstrap 方案把 repository isolation 本身拆成独立前置任务，不与 Multiplayer feature 同时发生。  
**What replaced it**：Phase 1 前先创建 `mrricardo16/personal-toolbox-trpg-site` private repo，old `personal-toolbox` untouched。  
**Can it be reconsidered later?** 当前不应回退，除非 bootstrap 实际出现重大阻塞并重新决策。
