# CURRENT_STATE

## 1. 文档定位

这是动态状态文档。任何 Codex 开始工作前 MUST 重新从 current Repository 验证动态事实。

Current verification date：**2026-08-17**

---

# 2. Current Verified Implementation Facts

Source repository：

```text
mrricardo16/personal-toolbox
└─ trpg-dm-assistant/
```

Imported into dedicated repository：

```text
mrricardo16/personal-toolbox-trpg-site
└─ repository root
```

Verified branch：`main`

- APP_VERSION：`1.6.10`
- Save Schema：`8`
- AI Protocol：`1.3`
- Regression baseline：`892 PASS / 0 FAIL`
- v1.6.10 focused suite：`45 PASS / 0 FAIL`
- Current stable product：`outputs/trpg-dm-assistant.html`
- Formal HTML size：`662681 bytes`
- Latest verified TRPG commit：`10188be886e6609feca8a2a426dfdf8426a49a0e`
- Commit title：`Release TRPG DM Assistant v1.6.10 Firearms / Impaling`

---

# 3. Current Source Shape

```text
README.md
build/
outputs/
reports/
src/
```

`src/` 已经按功能拆成多 JS files。正式 single HTML 是 build artifact，不是唯一 source file。

---

# 4. Current CI

Current `trpg-ci.yml`：

- path scoped to TRPG project；
- Node 24；
- 运行现有 security/save/AI/scenario/CoC/SAN/HP/combat/firearms regressions；
- JavaScript syntax check；
- single HTML build/verify；
- deterministic double build/hash；
- generated output diff check；
- 验证正式 HTML artifact 唯一。

Current `trpg-real-api.yml`：

- manual workflow；
- deterministic preflight；
- 通过 `DS_KEY` repository secret 传入 runtime；
- real DeepSeek full-case E2E。

---

# 5. Current Development Phase

```text
Phase 0 Architecture Audit       ✅ completed
Project Context Consolidation    ✅ completed
Dedicated Repository Bootstrap   ✅ completed
Multiplayer Phase 1              ✅ Host Provided Credential Foundation complete
```

---

# 6. Completed

## Stable Single Player

已完成并进入当前 baseline 的主要能力：

- structured state；
- save/import/export；
- Scenario Engine；
- Scenario Integrity；
- AI Protocol；
- state transaction；
- Player Action Guard；
- interaction availability；
- API response resilience；
- progress semantics；
- threat clock；
- NPC knowledge boundary；
- ending resolution；
- CoC resolution；
- consequence contract；
- failure forward；
- SAN loss / insanity tracking；
- HP damage state；
- dying / stabilization；
- healing recovery；
- combat opposed；
- combat damage / armor；
- Firearms / Impaling；
- deterministic regression；
- real provider acceptance workflow。

## Multiplayer Design

已完成：

- Multiplayer Architecture Audit；
- Server Authoritative decision；
- Vue 3 + Vite + TypeScript client decision；
- ASP.NET Core + SignalR server decision；
- BYOK/Host Provided API decision；
- Memory Only Phase 1 decision；
- Room/Credential boundary；
- PlayerSessionToken requirement；
- Reconnect principle；
- Player Projection requirement；
- phased migration plan；
- repository bootstrap prompt；
- Phase 1 first-two-commits prompt。

---

# 7. Current Phase Status

Current verified phase (2026-08-17): Multiplayer Phase 2B Realtime GameState Synchronization + Minimal Check Gameplay Client completed.

Completed in this phase:

- ASP.NET Core server skeleton
- `/health` integration test
- `RoomSession`, `RoomPlayer`, `IRoomStore`, and `InMemoryRoomStore`
- In-memory Room Store behavior and concurrency tests
- `RoomCoordinator` Create / Join / Leave / Ready lifecycle semantics
- Per-room mutation serialization and revision/error-result contracts
- Ephemeral CSPRNG `PlayerSessionToken` session store
- Server-generated `InviteCode` registry with collision retry and close invalidation
- Minimal HTTP Create / Join / Leave / Ready bootstrap API
- Public room/player snapshot DTOs without session credentials
- SignalR `RoomHub` at `/hubs/room` with explicit `AttachSession(playerSessionToken)`
- Identity-only in-memory Player Connection Registry with first/last connection accounting
- Shared RoomSnapshot projection and token-free realtime event DTOs
- HTTP Join/Ready/Leave committed-state realtime delivery with room isolation
- `MemberConnectionChanged` delivery for first attach and last disconnect
- `IsConnected` first-attach/last-disconnect lifecycle with same-session reattach
- Disconnect preserves Room membership/readiness; multiple active connections are safe
- Explicit Leave and host close invalidate session tokens and clean group/registry state
- Shared per-room mutation delivery gate covering HTTP and Hub lifecycle ordering

### Host Provided Credential Foundation

- Host-only public AI configuration (`Provider`, `Endpoint`, `Model`, `CredentialPresent`)
- Process-memory `IRoomCredentialStore` with set, replace, read, exists, and remove lifecycle
- Credential cleanup on explicit room close; disconnect does not delete credentials
- Server-side host-only connection test at `POST /api/rooms/{roomId}/ai-config/test`
- HTTPS-only outbound endpoint policy with userinfo, localhost, loopback, private, link-local, unique-local, multicast, and metadata-range rejection
- DNS resolution and resolved-address validation before outbound request
- No automatic redirects, bounded 64 KiB response body, 10-second timeout, and finite sanitized failure codes
- Connection tests use `IHttpClientFactory`, do not mutate canonical room state, and enforce one active test per room

### Multiplayer Phase 1 Lobby MVP

- Vue 3 + Vite + TypeScript client bootstrap under `multiplayer/client`
- Home/Create/Join/Lobby/Ready/Leave/host-close flow using the actual server contracts
- Authenticated room snapshot recovery after page reload through a narrow member-only GET
- `sessionStorage` session identity recovery; no API key is written to browser storage
- SignalR `RoomSnapshot` replacement model with `AttachSession` reattach after reconnect
- Reconnecting/connected/disconnected status UX and `RoomClosed` cleanup back to Home
- Host-only AI provider/model/endpoint configuration and safe connection-test feedback
- API key is held in memory only and cleared after the configuration/test submission

Phase 1 Lobby MVP deliberately does not include gameplay, shared GameState, Scenario, Character,
AI gameplay protocol, database, Redis, matchmaking, or Azure SignalR.

### Multiplayer Phase 2A

- Independent in-memory `MultiplayerGameState` store and `GameCoordinator` separate from `RoomSession`
- Independent Game revision starting at 1 and incrementing only on successful Check mutation
- Server-generated CharacterId with Player → Character ownership validation
- Host-only game initialization with member roster validation and deterministic conflict policy
- Player-safe `GameSnapshot` projection boundary with viewer context and no session credentials or AI credential
- Room-close GameState cleanup; disconnect preserves GameState; active-game join/member-leave mutation is rejected
- JS reference export script and committed 19-case deterministic Check/Dice conformance fixture
- Pure C# CoC Check resolution engine with server-side secure percentile dice generation
- Minimal authenticated `/api/rooms/{roomId}/game/check` API using canonical Character check values
- Minimal LastCheck record, independent game revision mutation, ownership/error/concurrency coverage

Phase 2A deliberately does not include gameplay Vue UI, realtime GameState sync, Scenario progression,
SAN, HP, healing, combat, firearms, AI KP gameplay, persistence, database, Redis, or Phase 3 work.

### Multiplayer Phase 2B

- Independent `IGameRealtimeNotifier` / SignalR game delivery boundary
- Canonical GameSnapshot broadcast after committed initialize/check mutations
- Viewer-safe per-player projections with other players' check values hidden
- Optional GameSnapshot recovery on `AttachSession` after page refresh/reconnect
- `CheckResolved` semantic event without roll/target/result payload duplication
- Game revision stale-snapshot rejection in the Vue client
- Minimal host game initialization and per-owner Check UI using the actual Phase 2A APIs
- Server LastCheck snapshot rendering, safe HTTP error handling, and RoomClosed game cleanup
- Real WebApplicationFactory + SignalR client integration coverage and two-tab localhost acceptance

Phase 2B deliberately does not include new deterministic rules, SAN, HP, healing, combat, firearms,
Scenario progression, AI KP gameplay, persistence, database, Redis, matchmaking, or Phase 3 work.

Phase 2B's two feature commits were validated and pushed to the dedicated repository `main`.

当前 Bootstrap 已完成：稳定 Single Player 已导入 standalone repository，CI 路径已适配，Context Docs 已固化。

本轮已完成 Multiplayer Phase 2B 的两次 feature commit，且均已通过测试并推送到 dedicated repository `main`。

---

# 8. Phase 2C Deterministic Rule Audit Complete (Phase 3 Deferred)

Phase 2B Realtime GameState + Minimal Check gameplay vertical slice 已完成。后续如继续开发，应先审计并选择下一个 deterministic gameplay rule slice；本轮不启动 Phase 3。

当前明确未实现且留待后续任务：Scenario progression、SAN、HP、healing、combat、firearms、AI KP gameplay、持久化、DB、Redis、matchmaking 与 Azure SignalR。

2026-08-17 Phase 2C audit 已完成：下一条 deterministic vertical slice 选择为 `HP / Damage State`。本轮只新增审计文档，没有修改 production code、Single Player rule code、Multiplayer behavior 或 formal HTML。下一步必须使用单独 implementation prompt，且继续保持 HP slice 不包含 Stabilization、Healing、Combat、Firearms、SAN、Scenario progression 或 Phase 3。

---

# 9. Known Risks

## High

- shared global state；
- late module wrapping / monkey-patch dependency；
- canonical vs secret projection；
- concurrent Room mutations；
- single-process in-memory connection registry；
- per-room mutation gate retention and delivery failure semantics；
- configurable outbound AI endpoint SSRF；
- JS→Server semantic drift。

## Medium / High

- Single character → multiple player characters；
- current Combat assumes one main player character；
- AI Protocol multi-actor evolution；
- reconnect/host lifecycle；
- credential cleanup timing。

## Medium

- Save Schema 8 与 Multiplayer snapshot boundary；
- action batching policy；
- Room command scheduling。

---

# 10. Known Technical Debt

当前 Single Player 中已有但暂不要求立即清理：

- fixed script load order；
- global `state`；
- global helper dependency；
- function wrapping / decoration chain；
- `memory.js` 混合 runtime + UI；
- `check-engine.js` 混合 pure-ish rules 与 DOM/form logic；
- `scenarios/library.js` 同时承载 constants/default config/scenario data；
- `ui.js` 强 DOM coupling；
- Single Player API key persistence 属于 browser-side BYOK model。

这些 technical debt MUST NOT 在 Phase 1 被顺便全量重构。

---

# 11. Dynamic Facts Requiring Re-verification Before Coding

- APP_VERSION；
- Save Schema；
- AI Protocol；
- deterministic test count；
- current `main` commit；
- file tree；
- CI commands；
- dedicated repo 是否已存在；
- installed .NET SDK；
- installed Node/npm；
- required GitHub secrets 是否存在。

---

# 12. Historical Evidence Gaps

本次可检索历史没有足够直接证据说明：

- “之前 Codex 开发结果不满意”的具体失败案例；
- “为什么后来继续由网页版 ChatGPT 开发”的明确单一原因；
- 该切换发生的确切日期/对话标题。

已能确认的是：

- 项目长期在 ChatGPT Project 中积累设计和开发上下文；
- 2026-08-14 已生成专门的 Codex Repository Bootstrap 与 Phase 1 task prompts；
- 当前工作方式明确准备把工程执行逐步交给 Codex，同时用 Project/Docs 固化设计上下文。

若未来找到原对话，应补入 `CONTEXT_MAP.md`，不要反向编造原因。

---

# Phase 2D — HP / Damage State

已完成 JS `src/hp-damage-state.js` → C# conformance fixture、pure `CocHpDamageEngine`、canonical character health state 与 server-internal `GameCoordinator.ApplyDamageAsync`。HP mutation 在 per-room serialization 内 commit 后才生成 viewer-specific SignalR snapshots；没有新增玩家或 Host 的 arbitrary damage HTTP API。

当前 projection policy：角色 owner 可见 `currentHp/maxHp` 与 active condition booleans；非 owner 不接收 HP details、damage history、event key 或 CON roll。reconnect 继续通过现有 GameSnapshot 恢复最新 owner-visible HP。

仍 deferred：Stabilization、Healing、SAN、Combat Opposed/Damage、Firearms、Scenario、AI gameplay、DB、Redis。
