# DECISION_LOG

> `Accepted` = 当前有效；`Superseded` = 被后续决策替代；`Open` = 尚未决定。

# ADR-001 — 保留 Single Player，不做全量重写
**Decision:** 现有 Single Player MUST 独立保留，Multiplayer 不通过重写旧产品启动。  
**Reason:** 当前产品已有稳定发布、规则链和 deterministic regression；Multiplayer 新问题是 authority/realtime。  
**Rejected alternative:** 整个项目直接改成 Vue + Server。  
**Status:** Accepted.

# ADR-002 — 保留 Single HTML 正式发布方式
**Decision:** Single Player 继续由多文件源码构建成唯一正式 single HTML。  
**Reason:** 当前已验证、易分发，且不妨碍源码模块化。  
**Rejected alternative:** 因 Multiplayer 引入 Vue 后取消 Single HTML。  
**Status:** Accepted.

# ADR-003 — Multiplayer 采用 Progressive Migration
**Decision:** Stable Single Player + new Multiplayer Client + new Multiplayer Server 并行。  
**Reason:** 降低 regression，rules 可逐步 parity migration。  
**Rejected alternative:** 一次性大重构。  
**Status:** Accepted.

# ADR-004 — Multiplayer Client 使用 Vue 3 + Vite + TypeScript
**Decision:** 新 Multiplayer UI 使用 Vue 3 + Vite + TypeScript。  
**Reason:** Room/member/connection/reconnect UI state 更复杂，独立 Client 避免污染旧 `ui.js`。  
**Rejected alternative:** 直接在旧 UI 叠完整 network runtime。  
**Status:** Accepted.

# ADR-005 — 不把 Single Player 全量迁移到 Vue
**Decision:** Vue 只用于 Multiplayer Client。  
**Reason:** 全量 rewrite 无助于 Server Authority，还会迫使旧 Save/UI/rules 重适配。  
**Rejected alternative:** 全项目 Vue rewrite。  
**Status:** Accepted.

# ADR-006 — Multiplayer Server 使用 ASP.NET Core
**Decision:** authoritative backend 使用 ASP.NET Core。  
**Reason:** 适合 REST + DI + realtime service，与 SignalR 原生集成。  
**Rejected alternative:** 继续纯 Browser 作为共享 authority。  
**Status:** Accepted.

# ADR-007 — Realtime 使用 SignalR
**Decision:** Room realtime synchronization 使用 SignalR。  
**Reason:** 适合 Room events/groups/connection lifecycle，并可保持 thin Hub。  
**Rejected alternative:** 第一版手写完整 WebSocket protocol。  
**Status:** Accepted.

# ADR-008 — Multiplayer Server Authoritative
**Decision:** Server 是 canonical Room/Game state、RNG、mechanics authority。  
**Reason:** 多 Client 不能同时成为同一共享世界的最终裁决者。  
**Rejected alternative:** Browser 继续作为 Multiplayer canonical owner。  
**Status:** Accepted.

# ADR-009 — AI 保持 Narrative + Structured Proposal
**Decision:** AI 不直接写 canonical state，保留 Proposal → Validation → Commit。  
**Reason:** 防止 stale/invalid/provider-error state。  
**Rejected alternative:** AI 输出即状态。  
**Status:** Accepted.

# ADR-010 — 玩家输入视为 Intent，而不是 World Fact
**Decision:** 玩家可自由描述，但输入不会自动写入世界。  
**Reason:** 自由 Roleplay 与可信状态必须同时存在。  
**Rejected alternative:** 禁止完成式措辞；或直接接受玩家自报结果。  
**Status:** Accepted.

# ADR-011 — 保持 BLOCK UNSAFE STATE, NOT PLAYER ACTION
**Decision:** Authority guard 位于 state validation/commit boundary。  
**Reason:** 不牺牲玩家自由，同时阻止越权结果。  
**Status:** Accepted.

# ADR-012 — Initial Multiplayer 使用 Host Provided API / BYOK
**Decision:** Host 提供 provider/model/endpoint/key；其他玩家不需要 key。  
**Reason:** 保持一个 Room 一个统一 AI KP context，第一版无需平台代付费系统。  
**Rejected alternative:** 每个玩家各用自己的 AI key 推进同一世界。  
**Status:** Accepted.

# ADR-013 — Credential 与 Room/Game State 分离
**Decision:** API key 放 Server-private credential store，不进入 Room/Game DTO。  
**Reason:** Room/Game 会被序列化、广播、保存、调试。  
**Status:** Accepted.

# ADR-014 — Phase 1 Persistence 为 Memory Only
**Decision:** Phase 1 不引入 database / Redis。  
**Reason:** 当前只验证 Room/realtime/credential boundary，restart 清房可接受。  
**Rejected alternative:** Phase 1 立即 PostgreSQL/Redis。  
**Status:** Accepted.

# ADR-015 — Disconnect 不等于 Leave
**Decision:** transient connection loss 不直接结束 Room 或删除 credential。  
**Reason:** 网络短断是正常现象。  
**Status:** Accepted.  
**Open detail:** grace period / idle timeout。

# ADR-016 — Canonical State 与 Player Projection 分离
**Decision:** Server 不广播完整 canonical state。  
**Reason:** hidden clue、secret check、director state、raw AI response 必须隔离。  
**Status:** Accepted.

# ADR-017 — Existing JS Rules 作为行为参考，不一次性重写
**Decision:** Rule migration 采用 behavior preservation + conformance/golden tests。  
**Reason:** 当前 rules 已被大量 regression 保护。  
**Rejected alternative:** 按新 C# 架构重新发明所有规则。  
**Status:** Accepted.

# ADR-018 — Existing JS Regression 不被 .NET Tests 替代
**Decision:** Multiplayer CI 是新增层，不删除 Single Player release gates。  
**Reason:** 两者验证不同产品面，旧 tests 是 migration safety net。  
**Status:** Accepted.

# ADR-019 — Sites 不作为 Authoritative Multiplayer Server
**Decision:** Sites 只考虑 optional presentation/prototype/frontend，canonical runtime 仍为 ASP.NET Core + SignalR。  
**Reason:** 不把核心 backend 建在未验证 hosting pattern 上。  
**Rejected alternative:** Sites 直接承担完整 authoritative multiplayer backend。  
**Status:** Accepted.

# ADR-020 — Repository 长期独立
**Decision:** TRPG 使用 dedicated repository。  
**Reason:** 项目将拥有独立 frontend/backend/realtime/CI/docs/deployment，已超出普通 toolbox utility。  
**Status:** Accepted.

# ADR-021 — Repository 迁移时点改为 Phase 1 前
**Decision:** 开始 Multiplayer code 前先创建 `mrricardo16/personal-toolbox-trpg-site` private repo，并导入 verified stable Single Player baseline。  
**Reason:** 将 repository isolation 单独作为前置任务，防止 Codex Multiplayer commits 误影响 old repo。  
**Supersedes:** Architecture Audit 早期“Phase 1 暂留 personal-toolbox，Phase 1 stable 后再拆 repo”。  
**Status:** Accepted and executed by the Dedicated Repository Bootstrap.

# ADR-022 — ChatGPT Project / Git Docs / Codex 分工
**Decision:** `ChatGPT Project = full design history`；`Git/Docs = current confirmed decisions`；`Codex = constrained engineering execution`。  
**Reason:** 聊天过长且包含 superseded ideas，工程 Agent 需要 concise authoritative context。  
**Status:** Accepted.

# ADR-023 — Codex 以小提交推进 Phase 1
**Decision:** 当前 first Phase 1 task 只允许两提交：server skeleton；InMemoryRoomStore。然后 STOP。  
**Reason:** 控制 scope，保护 stable baseline，便于 review/handoff。  
**Status:** Accepted.

# ADR-024 — Matchmaking / Accounts / Billing 不属于当前 Phase
**Decision:** 明确后置。
**Reason:** 当前先证明 Room/realtime/authority。
**Status:** Accepted.

# ADR-025 — Dedicated Repository Bootstrap supersedes the delayed split
**Decision:** The former “Phase 1 stable 后再拆 repo” decision is superseded. `mrricardo16/personal-toolbox-trpg-site` is the primary development repository; `mrricardo16/personal-toolbox` is the read-only legacy/stable source for the imported Single Player baseline.
**Reason:** Isolating the stable baseline before any Multiplayer work prevents future work from contaminating the legacy toolbox repository and establishes an authoritative development location.
**Supersedes:** The earlier “Phase 1 stable 后再拆 repo” decision.
**Status:** Accepted.
