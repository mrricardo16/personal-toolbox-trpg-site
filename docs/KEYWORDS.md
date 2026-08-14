# Codex Context Retrieval Index

> 目的：让未来 Codex / GPT / Agent 通过稳定关键词快速定位 Project 历史、正式设计文档与 Repository。
>
> 状态：`FINAL DECISION` / `CURRENT DIRECTION` / `REJECTED / SUPERSEDED` / `OPEN QUESTION` / `IMPLEMENTATION FACT`。

## Product

### Keyword: TRPG AI DM
**Aliases / Related Terms:** TRPG AI 主持助手 / AI DM / AI Game Master  
**Meaning:** 项目总体名称；AI 主持叙事，程序负责可信规则和状态。  
**Why it matters:** 总体检索入口。  
**Current status:** FINAL DECISION  
**Recommended search phrase:** `"TRPG AI 主持助手" / "TRPG AI DM"`

### Keyword: AI KP
**Aliases / Related Terms:** AI Keeper / KP / Keeper  
**Meaning:** COC 语境下的 AI 主持角色，不拥有 canonical state authority。  
**Why it matters:** 防止把“AI 主持”理解成“AI 直接改规则/状态”。  
**Current status:** FINAL DECISION  
**Recommended search phrase:** `"AI KP" / "AI Keeper" / "AI负责叙事"`

### Keyword: Single Player
**Aliases / Related Terms:** 单人版 / 单机版 / stable product  
**Meaning:** 当前稳定产品；多 JS source 构建为 single HTML。  
**Why it matters:** Multiplayer MUST NOT 破坏它。  
**Current status:** FINAL DECISION + IMPLEMENTATION FACT  
**Recommended search phrase:** `"Single Player" / "单人版本" / "唯一正式产品入口"`

### Keyword: Multiplayer
**Aliases / Related Terms:** 联机 / 多人跑团 / Room Multiplayer  
**Meaning:** 在稳定 Single Player 旁路建立的新产品方向。  
**Why it matters:** 当前下一阶段工程方向。  
**Current status:** FINAL DECISION  
**Recommended search phrase:** `"Multiplayer" / "联机房间" / "一个房间一个 AI KP"`

### Keyword: Scenario
**Aliases / Related Terms:** 剧本 / 模组 / Scenario Engine  
**Meaning:** chapter/scene/node/clue/NPC/check 等结构化内容。  
**Why it matters:** 连接 AI 叙事、调查进度与程序连续性。  
**Current status:** IMPLEMENTATION FACT + FINAL DESIGN INTENT  
**Recommended search phrase:** `"Scenario Engine" / "clue route"`

### Keyword: Campaign
**Aliases / Related Terms:** Campaign State / Director State  
**Meaning:** 运行中的 shared investigation/world progress。  
**Why it matters:** Multiplayer 最终属于 Server canonical state。  
**Current status:** IMPLEMENTATION FACT; Multiplayer ownership FINAL DECISION  
**Recommended search phrase:** `"campaign" / "directorState" / "campaignChanges"`

## Authority

### Keyword: Server Authoritative
**Aliases / Related Terms:** 服务器权威 / Server Authority  
**Meaning:** Multiplayer Server 拥有 canonical Room/Game state 与可信规则结算权。  
**Why it matters:** Multiplayer 最核心的 authority boundary。  
**Current status:** FINAL DECISION  
**Recommended search phrase:** `"Server Authoritative" / "服务器权威" / "canonical state"`

### Keyword: Browser Authority
**Aliases / Related Terms:** browser-owned rules / 浏览器裁决  
**Meaning:** 当前 Single Player 中 Browser 拥有 Check/SAN/HP/Combat 等规则。  
**Why it matters:** 是现有稳定行为参考，但不是 Multiplayer 最终 authority。  
**Current status:** IMPLEMENTATION FACT for SP; SUPERSEDED for Multiplayer target  
**Recommended search phrase:** `"browser-owned" / "browser_coc" / "浏览器权威"`

### Keyword: Canonical State
**Aliases / Related Terms:** Canonical Game State / authoritative state  
**Meaning:** 唯一被程序认可的真实世界状态。  
**Why it matters:** AI output、玩家描述、Client cache 都不能直接替代它。  
**Current status:** FINAL DECISION  
**Recommended search phrase:** `"canonical state" / "可信状态"`

### Keyword: Player Intent
**Aliases / Related Terms:** 玩家行动意图 / non-authoritative statement  
**Meaning:** 玩家自然语言先被视为意图，而非已完成世界事实。  
**Why it matters:** 兼顾自由表达和 authority。  
**Current status:** FINAL DECISION  
**Recommended search phrase:** `"Player Intent" / "玩家输入不是世界事实"`

### Keyword: AI Proposal
**Aliases / Related Terms:** structured proposal / AI 提议  
**Meaning:** AI 可提出叙事和状态变化，但需程序验证。  
**Why it matters:** AI 不是 canonical database。  
**Current status:** FINAL DECISION  
**Recommended search phrase:** `"structured proposal" / "stateChanges"`

### Keyword: Transaction Commit
**Aliases / Related Terms:** prepare / validate / commit / revision  
**Meaning:** AI result 验证后以 transaction 写入 canonical state。  
**Why it matters:** 防止 partial corruption 与 stale response。  
**Current status:** FINAL DECISION; SP already implemented  
**Recommended search phrase:** `"commitAiTransaction" / "baseRevision"`

### Keyword: Player Projection
**Aliases / Related Terms:** Player-visible Snapshot / ProjectionService  
**Meaning:** Server 从 canonical state 生成特定玩家可见 snapshot。  
**Why it matters:** 防止 hidden clue/secret check/director data 泄漏。  
**Current status:** FINAL DECISION; not implemented  
**Recommended search phrase:** `"Player Projection" / "Player-visible Snapshot"`

## Core Principles

### Keyword: BLOCK UNSAFE STATE, NOT PLAYER ACTION
**Aliases / Related Terms:** 阻止不安全状态，不阻止玩家行动  
**Meaning:** 玩家可以自由尝试；系统阻止未经合法结算的 state mutation。  
**Why it matters:** 项目体验和安全的核心口号。  
**Current status:** FINAL DECISION  
**Recommended search phrase:** `"BLOCK UNSAFE STATE, NOT PLAYER ACTION"`

### Keyword: AI负责叙事，程序负责规则
**Aliases / Related Terms:** AI narrates, program adjudicates  
**Meaning:** AI 负责语言，程序负责 Check/RNG/HP/SAN/Combat 等可信机械。  
**Why it matters:** 决定 Rule Engine 与 AI integration。  
**Current status:** FINAL DECISION  
**Recommended search phrase:** `"AI负责叙事，程序负责规则"`

### Keyword: Deterministic Rules
**Aliases / Related Terms:** deterministic engine / rule engine  
**Meaning:** 机械优先由可测试、可复现程序逻辑决定。  
**Why it matters:** 是规则可靠性与迁移 parity 的基础。  
**Current status:** FINAL DECISION  
**Recommended search phrase:** `"deterministic" / "规则引擎"`

### Keyword: Preserve Verified Behavior
**Aliases / Related Terms:** behavior preservation / parity  
**Meaning:** 迁移时保护 regression 已证明的规则行为。  
**Why it matters:** 不允许因框架变化重新发明业务规则。  
**Current status:** FINAL DECISION  
**Recommended search phrase:** `"Behavior Preservation" / "conformance"`

## Multiplayer

### Keyword: Room
**Aliases / Related Terms:** Game Room / RoomSession  
**Meaning:** Multiplayer 隔离单元，承载 Host、members、settings、revision。  
**Why it matters:** 一个 Room 对应一个 authoritative shared context。  
**Current status:** FINAL DECISION; not implemented  
**Recommended search phrase:** `"RoomSession" / "RoomSnapshot"`

### Keyword: Host
**Aliases / Related Terms:** 房主 / HostPlayerId  
**Meaning:** 创建 Room，并提供初始设置和 BYOK credential 的玩家。  
**Why it matters:** Host privilege 仍由 Server 授权。  
**Current status:** FINAL DECISION  
**Recommended search phrase:** `"HostPlayerId" / "房主"`

### Keyword: Join
**Aliases / Related Terms:** Join Room / InviteCode  
**Meaning:** 玩家通过 invite flow 进入 Room。  
**Why it matters:** InviteCode 不是玩家身份。  
**Current status:** FINAL DECISION for Phase 1  
**Recommended search phrase:** `"JoinRoom" / "InviteCode"`

### Keyword: Ready
**Aliases / Related Terms:** SetReady / Lobby Ready  
**Meaning:** 由 Server 保存和同步的准备状态。  
**Why it matters:** Phase 1 最小 realtime synchronization 验证点。  
**Current status:** FINAL DECISION for Phase 1  
**Recommended search phrase:** `"SetReady" / "ReadyChanged"`

### Keyword: Reconnect
**Aliases / Related Terms:** 重连 / session restore  
**Meaning:** 断线后恢复同一 PlayerId 和 latest snapshot。  
**Why it matters:** Disconnect ≠ Leave。  
**Current status:** FINAL DECISION; exact timeout OPEN QUESTION  
**Recommended search phrase:** `"Reconnect" / "PlayerSessionToken"`

### Keyword: Room Revision
**Aliases / Related Terms:** revision / snapshot revision  
**Meaning:** Room mutation 后递增版本。  
**Why it matters:** 支持 snapshot consistency 和冲突判断。  
**Current status:** CURRENT DIRECTION  
**Recommended search phrase:** `"Room Revision" / "revision++"`

### Keyword: Player Session
**Aliases / Related Terms:** PlayerSessionToken / PlayerId  
**Meaning:** 无账号 MVP 中识别玩家的 Server-issued identity。  
**Why it matters:** Nickname + InviteCode 不足以做授权。  
**Current status:** FINAL DECISION  
**Recommended search phrase:** `"PlayerSessionToken" / "PlayerId"`

### Keyword: BYOK
**Aliases / Related Terms:** Host Provided API / Bring Your Own Key  
**Meaning:** 初始 Multiplayer 由 Host 提供 AI API key。  
**Why it matters:** 保持一个 Room 一个 AI KP context。  
**Current status:** FINAL DECISION  
**Recommended search phrase:** `"BYOK" / "Host Provided API"`

### Keyword: Credential Lifecycle
**Aliases / Related Terms:** API key lifecycle / CredentialStore  
**Meaning:** Key 仅在 Server-private runtime，Room 真正结束时清理。  
**Why it matters:** Key MUST NOT 进入 Game State/save/log/broadcast。  
**Current status:** FINAL DECISION  
**Recommended search phrase:** `"Credential Lifecycle" / "IRoomCredentialStore"`

### Keyword: One Canonical World State
**Aliases / Related Terms:** shared world / 一个 canonical world state  
**Meaning:** 一个 Room 只有一份 Server 认可世界真状态。  
**Why it matters:** 防止各玩家剧情分叉。  
**Current status:** FINAL DECISION  
**Recommended search phrase:** `"One Canonical World State"`

### Keyword: One Canonical AI Context
**Aliases / Related Terms:** One AI KP / unified keeper context  
**Meaning:** 一个 Room 只有一个共享 AI KP context。  
**Why it matters:** 避免一条 player message 就独立 world advance。  
**Current status:** FINAL DECISION  
**Recommended search phrase:** `"一个房间一个 AI KP" / "batch player actions"`

## Backend

### Keyword: ASP.NET Core
**Aliases / Related Terms:** Multiplayer Server / .NET backend  
**Meaning:** authoritative multiplayer backend 技术选型。  
**Why it matters:** REST、SignalR、Room、credential、future rules/AI gateway 的宿主。  
**Current status:** FINAL DECISION  
**Recommended search phrase:** `"ASP.NET Core" / "Multiplayer Server"`

### Keyword: SignalR
**Aliases / Related Terms:** realtime / RoomHub / HubConnection  
**Meaning:** Multiplayer realtime communication 方案。  
**Why it matters:** 支持 Room events、connection lifecycle 和 future game events。  
**Current status:** FINAL DECISION  
**Recommended search phrase:** `"SignalR" / "RoomHub"`

### Keyword: RoomStore
**Aliases / Related Terms:** IRoomStore / InMemoryRoomStore  
**Meaning:** Phase 1 server-side Room storage abstraction。  
**Why it matters:** SignalR group 不是 source of truth。  
**Current status:** FINAL DECISION for Phase 1  
**Recommended search phrase:** `"IRoomStore" / "InMemoryRoomStore"`

### Keyword: RoomCoordinator
**Aliases / Related Terms:** IRoomCoordinator / room command service  
**Meaning:** 处理 Room mutation、authorization、revision、broadcast orchestration。  
**Why it matters:** 避免业务散落在 Hub。  
**Current status:** CURRENT DIRECTION  
**Recommended search phrase:** `"RoomCoordinator" / "thin hub"`

### Keyword: CredentialStore
**Aliases / Related Terms:** IRoomCredentialStore / InMemoryCredentialStore  
**Meaning:** 与 Room model 分离的 Server-private key store。  
**Why it matters:** 防止 key 被 DTO 序列化。  
**Current status:** FINAL DECISION  
**Recommended search phrase:** `"CredentialStore" / "IRoomCredentialStore"`

### Keyword: AI Gateway
**Aliases / Related Terms:** IAiProvider / OpenAI-compatible / DeepSeek  
**Meaning:** Server-side AI call boundary。  
**Why it matters:** 集中 provider、retry、安全与 protocol integration。  
**Current status:** CURRENT DIRECTION  
**Recommended search phrase:** `"AI Gateway" / "IAiProvider"`

## Frontend

### Keyword: Vue 3
**Aliases / Related Terms:** Multiplayer Client  
**Meaning:** 新 Multiplayer UI 技术选型。  
**Why it matters:** 与 stable Single Player 解耦。  
**Current status:** FINAL DECISION  
**Recommended search phrase:** `"Vue 3" / "Multiplayer Client"`

### Keyword: Vite
**Aliases / Related Terms:** Vue build tool  
**Meaning:** Multiplayer Client build tool。  
**Why it matters:** 配合独立 client + TypeScript。  
**Current status:** FINAL DECISION  
**Recommended search phrase:** `"Vite" / "multiplayer/client"`

### Keyword: TypeScript
**Aliases / Related Terms:** TS / typed contracts  
**Meaning:** Multiplayer Client language direction。  
**Why it matters:** 使 API/SignalR contracts 显式。  
**Current status:** FINAL DECISION  
**Recommended search phrase:** `"TypeScript" / "contracts"`

### Keyword: SignalR Client
**Aliases / Related Terms:** HubConnection  
**Meaning:** Vue 与 RoomHub 的 realtime connection layer。  
**Why it matters:** Client 只消费 Server snapshot/event。  
**Current status:** FINAL DECISION  
**Recommended search phrase:** `"SignalR Client" / "HubConnection"`

## Existing Engine

### Keyword: AI Protocol
**Aliases / Related Terms:** protocol 1.3 / requestId / baseRevision  
**Meaning:** Single Player structured AI validation/transaction protocol。  
**Why it matters:** Multiplayer 保留核心语义。  
**Current status:** IMPLEMENTATION FACT: 1.3; semantics FINAL DECISION  
**Recommended search phrase:** `"AI_PROTOCOL_VERSION" / "baseRevision"`

### Keyword: Save Schema
**Aliases / Related Terms:** Schema 8 / save snapshot  
**Meaning:** 当前 Single Player Browser Snapshot 格式。  
**Why it matters:** Multiplayer 不直接塞 Room/runtime/credential。  
**Current status:** IMPLEMENTATION FACT: 8  
**Recommended search phrase:** `"SCHEMA_VERSION" / "Schema 8"`

### Keyword: Scenario Engine
**Aliases / Related Terms:** scenario-engine.js / node navigation  
**Meaning:** Scenario parsing/node/clue/transition 逻辑。  
**Why it matters:** 高价值复用语义。  
**Current status:** IMPLEMENTATION FACT + CURRENT DIRECTION  
**Recommended search phrase:** `"scenario-engine.js" / "clue routes"`

### Keyword: Check Engine
**Aliases / Related Terms:** check-engine.js / COC percentile  
**Meaning:** Dice/COC check 和部分 character rules。  
**Why it matters:** pure-ish rule 可迁，DOM/global 部分不可直接当 shared package。  
**Current status:** IMPLEMENTATION FACT  
**Recommended search phrase:** `"check-engine.js" / "rollCocPercentile"`

### Keyword: SAN
**Aliases / Related Terms:** sanity / SAN loss / insanity  
**Meaning:** Browser-owned SAN mechanics。  
**Why it matters:** Multiplayer 最终迁 Server。  
**Current status:** IMPLEMENTATION FACT for SP; CURRENT DIRECTION for migration  
**Recommended search phrase:** `"san-loss-resolution" / "SAN"`

### Keyword: HP
**Aliases / Related Terms:** HP Damage State / Major Wound / dying  
**Meaning:** HP、重伤、dying、stabilization、healing mechanics。  
**Why it matters:** authoritative combat 核心。  
**Current status:** IMPLEMENTATION FACT for SP  
**Recommended search phrase:** `"hp-damage-state" / "Major Wound"`

### Keyword: Combat
**Aliases / Related Terms:** Combat Mode / combat-opposed.js  
**Meaning:** Browser-owned combat round/DEX/Dodge/Fight Back。  
**Why it matters:** Multiplayer 需要 Server participants 与 multi-character mapping。  
**Current status:** IMPLEMENTATION FACT; migration deferred  
**Recommended search phrase:** `"combat-opposed.js" / "Combat Mode"`

### Keyword: Damage
**Aliases / Related Terms:** combat-damage.js / Damage Bonus / Armor  
**Meaning:** Weapon damage、DB、Armor、HP application。  
**Why it matters:** 必须保持 deterministic parity。  
**Current status:** IMPLEMENTATION FACT  
**Recommended search phrase:** `"combat-damage.js" / "Damage Bonus"`

### Keyword: Firearms
**Aliases / Related Terms:** Firearms / Impaling / Dive for Cover  
**Meaning:** v1.6.10 单发枪械与 Impale mechanics。  
**Why it matters:** 当前最新稳定 rule layer。  
**Current status:** IMPLEMENTATION FACT  
**Recommended search phrase:** `"firearms-impaling.js" / "Impale"`

### Keyword: Memory
**Aliases / Related Terms:** memory.js / rollback / diagnostics  
**Meaning:** 当前混合 turn snapshot、context、diagnostics、UI。  
**Why it matters:** Multiplayer 要拆 Server runtime 与 Client UI。  
**Current status:** IMPLEMENTATION FACT + CURRENT DIRECTION  
**Recommended search phrase:** `"memory.js" / "turn snapshot"`

### Keyword: Scenario Integrity
**Aliases / Related Terms:** case-integrity.js  
**Meaning:** Node/clue/ending/dependency/reachability validation。  
**Why it matters:** 可作为 future server-side validation 语义。  
**Current status:** IMPLEMENTATION FACT + CURRENT DIRECTION  
**Recommended search phrase:** `"case-integrity.js" / "critical clue"`

### Keyword: Player Action Guard
**Aliases / Related Terms:** assertion guard  
**Meaning:** 阻止玩家完成式描述或 AI 回声直接成为 world fact。  
**Why it matters:** 对应核心 authority principle。  
**Current status:** IMPLEMENTATION FACT + FINAL DECISION  
**Recommended search phrase:** `"player-action-guard.js" / "assertion"`

### Keyword: AI Resilience
**Aliases / Related Terms:** api-response-resilience.js / retry / graceful fallback  
**Meaning:** provider empty/network/http/invalid response 的有限重试与 fail-closed recovery。  
**Why it matters:** Provider failure 不应生成虚假 game result。  
**Current status:** IMPLEMENTATION FACT + FINAL DECISION  
**Recommended search phrase:** `"api-response-resilience.js" / "retry exhaustion"`

## Testing

### Keyword: Deterministic Regression
**Aliases / Related Terms:** 892 PASS / regression baseline  
**Meaning:** 当前 stable Single Player behavior baseline。  
**Why it matters:** Multiplayer 不能删除或用 .NET tests 替代。  
**Current status:** IMPLEMENTATION FACT: 892 PASS / 0 FAIL  
**Recommended search phrase:** `"892 PASS" / "trpg-ci.yml"`

### Keyword: Golden Test
**Aliases / Related Terms:** Golden Fixture / test vector  
**Meaning:** 固定 initialState/command/randomSequence/expectedState/events。  
**Why it matters:** 用于 JS vs Server parity。  
**Current status:** CURRENT DIRECTION  
**Recommended search phrase:** `"Golden Test" / "test-vectors"`

### Keyword: Conformance Test
**Aliases / Related Terms:** parity test / behavior compatibility  
**Meaning:** 同 fixture 在旧 JS 与新 Server 中产生等价结果。  
**Why it matters:** 防止跨语言 semantic drift。  
**Current status:** CURRENT DIRECTION  
**Recommended search phrase:** `"Conformance Test" / "JS reference"`

### Keyword: Real API Acceptance
**Aliases / Related Terms:** trpg-real-api.yml / DeepSeek E2E  
**Meaning:** GitHub Secret 驱动的真实 provider full-case test。  
**Why it matters:** deterministic test 无法覆盖真实 provider transport。  
**Current status:** IMPLEMENTATION FACT  
**Recommended search phrase:** `"trpg-real-api.yml" / "TRPG_TEST_API_KEY"`

## Deployment

### Keyword: Single HTML
**Aliases / Related Terms:** portable HTML / outputs/trpg-dm-assistant.html  
**Meaning:** 当前 Single Player formal release artifact。  
**Why it matters:** release format 不等于 source architecture。  
**Current status:** FINAL DECISION + IMPLEMENTATION FACT  
**Recommended search phrase:** `"build-single-html.js" / "outputs/trpg-dm-assistant.html"`

### Keyword: Sites
**Aliases / Related Terms:** ChatGPT Sites / hosted app  
**Meaning:** 曾讨论作为托管/原型/frontend；不作为 authoritative multiplayer server。  
**Why it matters:** 防止托管便利改变核心 backend boundary。  
**Current status:** FINAL DECISION for not-authoritative; future use OPEN QUESTION  
**Recommended search phrase:** `"Sites" / "authoritative multiplayer server"`

### Keyword: ASP.NET Core Hosting
**Aliases / Related Terms:** same-origin hosting / Vue dist  
**Meaning:** MVP 倾向一个 ASP.NET Core runtime 提供 Vue、REST、SignalR。  
**Why it matters:** 减少 CORS/WSS 和多部署单元。  
**Current status:** CURRENT DIRECTION  
**Recommended search phrase:** `"same origin" / "/hubs/room"`

### Keyword: Single Instance
**Aliases / Related Terms:** one process / in-memory deployment  
**Meaning:** Phase 1/MVP 与 InMemory state 匹配的单实例方向。  
**Why it matters:** 当前无需 Redis/backplane。  
**Current status:** CURRENT DIRECTION  
**Recommended search phrase:** `"Single Instance" / "Memory Only"`

## Workflow

### Keyword: ChatGPT Project
**Aliases / Related Terms:** Project history / 设计历史  
**Meaning:** 保存完整讨论、演进与否决方案。  
**Why it matters:** 历史源，不是施工时唯一规范。  
**Current status:** FINAL WORKFLOW DECISION  
**Recommended search phrase:** `"ChatGPT Project" / "Project Context Consolidation"`

### Keyword: Codex
**Aliases / Related Terms:** engineering executor / Codex Desktop  
**Meaning:** 未来主要执行 repository 中的受约束工程任务。  
**Why it matters:** Project/Docs 与工程执行分工明确。  
**Current status:** CURRENT DIRECTION  
**Recommended search phrase:** `"Codex" / "Phase1 Start Prompt"`

### Keyword: GitHub
**Aliases / Related Terms:** Repository / CI / commit  
**Meaning:** 保存 current source、authoritative docs、CI 和可审计变更。  
**Why it matters:** Git/Docs 表示当前确认结论。  
**Current status:** FINAL WORKFLOW DECISION  
**Recommended search phrase:** `"GitHub" / "authoritative project docs"`

### Keyword: Architecture Audit
**Aliases / Related Terms:** Phase 0 / Multiplayer Architecture Audit  
**Meaning:** 2026-08-14 对 v1.6.10 code 与 Multiplayer 方向的系统审计。  
**Why it matters:** 重要历史快照；repository timing 已被后续决策替代。  
**Current status:** HISTORICAL SNAPSHOT; partially SUPERSEDED  
**Recommended search phrase:** `"Multiplayer Architecture Audit" / "Phase 0"`

### Keyword: Handoff
**Aliases / Related Terms:** HANDOFF.md / takeover context  
**Meaning:** 新 Agent 5 分钟恢复当前状态的短文档。  
**Why it matters:** 防止每次重新设计。  
**Current status:** FINAL WORKFLOW DECISION  
**Recommended search phrase:** `"HANDOFF" / "Do Not Do"`

### Keyword: ADR
**Aliases / Related Terms:** Decision Log / Architecture Decision Record  
**Meaning:** 记录重要决定、原因、替代方案和 supersession。  
**Why it matters:** 防止旧聊天方案复活。  
**Current status:** FINAL WORKFLOW DECISION  
**Recommended search phrase:** `"ADR" / "Rejected alternative"`

### Keyword: Dedicated Repository
**Aliases / Related Terms:** mrricardo16/personal-toolbox-trpg-site / bootstrap  
**Meaning:** 最新决定是在 Phase 1 前先隔离 stable baseline 到新 private repo。  
**Why it matters:** supersede 早期“Phase 1 后再拆 repo”。  
**Current status:** FINAL DECISION, IMPLEMENTED  
**Recommended search phrase:** `"Dedicated TRPG Repository Bootstrap" / "personal-toolbox-trpg-site"`
