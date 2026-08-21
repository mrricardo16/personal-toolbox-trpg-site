# ARCHITECTURE

## 1. 文档定位

本文件只记录当前已经确定或明确倾向的 Architecture Direction，不重新设计系统。

Implementation Fact 与 Design Direction 必须分开。

---

# 2. Existing Single Player

## 2.1 Current Implementation Facts

截至 2026-08-14，Repository `mrricardo16/personal-toolbox/main` 的 `trpg-dm-assistant/`：

- APP_VERSION：`1.6.10`
- Save Schema：`8`
- AI Protocol：`1.3`
- Deterministic regression baseline：`892 PASS / 0 FAIL`
- Formal release artifact：`outputs/trpg-dm-assistant.html`
- Formal HTML size：`662681 bytes`

源码已按功能拆分，包括 `state.js`、`check-engine.js`、`scenario-engine.js`、`case-integrity.js`、`memory.js`、`ai-protocol.js`、`player-action-guard.js`、`saves.js`、`ui.js`、SAN/HP/Combat/Damage/Firearms 等模块。

### Critical clarification

```text
Single HTML release
≠
Single HTML source
```

正式 HTML 是 build artifact。现有 source 已经是多文件维护。

## 2.2 Current Source Coupling

现有 Single Player 主要依赖：

```text
fixed load order
+
shared global state
+
shared global functions/constants
+
late-file function wrapping / decoration
```

因此：

- rules 在逻辑上可复用；
- 当前 JS files 不能直接视为 clean domain packages；
- Multiplayer migration 应保护行为，不直接复制现有 runtime coupling。

## 2.3 Current Single Player Authority

Single Player 中 Browser 同时承担 UI、Game State、deterministic rules、AI orchestration 与 Save runtime owner。

这是当前稳定 Single Player 的合法实现。

Multiplayer 不继续沿用 Browser canonical authority。

---

# 3. Existing Engine Boundaries

## 3.1 AI Protocol

当前 AI Protocol 1.3 已经实现：

```text
Player Action
  ↓
AI request
  ↓
Structured response
  ↓
Protocol validation
  ↓
Prepare transaction
  ↓
Validate draft
  ↓
Commit
  ↓
revision++
```

关键语义：`protocolVersion`、`requestId`、`baseRevision`、`decision`、`check`、`stateChanges`、`campaignChanges`、`locationEffect`、`nodeProposal`、`endingProposal`、`actionSuggestions`。

Multiplayer MUST 保留：

> AI proposal → validation → transaction commit

Multiplayer MAY 未来升级 payload 支持 multi-actor/shared room，但 Phase 1 不改 AI Protocol。

## 3.2 Save Schema

Save Schema 8 是 Single Player Browser Snapshot Schema。

Multiplayer MUST NOT 直接向 Schema 8 加入：

- Room membership；
- connection state；
- PlayerSessionToken；
- credential；
- server runtime。

未来多人 snapshot SHOULD 独立版本化，例如 `MultiplayerGameSnapshot v1`。具体命名尚未实现。

## 3.3 Existing Rule Assets

高价值稳定资产：

- Scenario Engine；
- Scenario Integrity / Case Integrity；
- Check Engine；
- CoC Resolution；
- Consequence Contract；
- Failure Forward；
- SAN；
- HP / dying / recovery；
- Combat Opposed；
- Damage / Armor；
- Firearms / Impaling；
- Player Action Guard；
- API Response Resilience。

Multiplayer SHOULD 逐步迁移这些规则的语义到 Server，MUST NOT 一次性重写整套 rules。

---

# 4. Multiplayer Target Architecture

`FINAL DECISION`

```text
┌────────────────────────────────┐
│ Multiplayer Client             │
│ Vue 3 + Vite + TypeScript      │
│                                │
│ UI / local UI state            │
│ player input draft             │
│ REST client                    │
│ SignalR client                 │
│ server snapshot rendering      │
└───────────────┬────────────────┘
                │ HTTPS / SignalR
                ▼
┌────────────────────────────────┐
│ ASP.NET Core Server            │
│ REST API                       │
│ RoomHub                        │
│ Room Coordinator               │
│ Room Store                     │
│ Credential Store               │
│ Player Projection              │
│ Future Game Coordinator        │
│ Future Deterministic Rules     │
│ Future AI Gateway              │
└───────────────┬────────────────┘
                │
                ▼
          AI Provider(s)
```

---

# 5. Authority Boundary

## Client owns

Client MAY own：页面/view、tab、modal、animation、form draft、unsent text、connection indicator、session token storage。

Client MUST NOT own canonical：HP、SAN、Dice、Check result、Combat order、Clues、Scenario progress、Room membership authority、AI committed result。

## Server owns

Server MUST own：RoomId、Invite validation、Host identity、PlayerId、membership、ready、Room status/revision、Game canonical state、Character mapping、Scene/Location、Clues/Items、NPC state、Check target/result、RNG、HP/SAN、Combat/Turn/Damage、Scenario progress、validated AI result、projection policy、authorization。

部分 Game fields Phase 1 尚未实现，但 authority target 已确定。

## AI owns

AI owns language-level responsibilities：narrative、NPC dialogue、scene description、natural-language interpretation、structured proposal generation。

AI MUST NOT own canonical state or deterministic mechanics。

## Player owns

Player owns intent、roleplay statement、action choice、character decisions；不拥有最终 result/world truth。

---

# 6. State Layers

## Canonical Game State

Server-owned 可信共享世界：characters、campaign、scenario progression、clues、NPC state、HP/SAN、combat、rule outcomes。

## Room State

RoomId、InviteCode、HostPlayerId、members、ready、max players、status、provider metadata、revision。

## Server Runtime State

active request、action queue、disconnect lease、retry state、raw provider result、hidden diagnostics、pending secret result。

## Credential State

Server-private：`RoomId -> API Credential`。不属于 Room/Game snapshot。

## Player Projection

Server 根据 player identity、revealed information、permissions 生成可发送 snapshot。

## Client UI State

仅影响 presentation，不影响 canonical logic。

---

# 7. Room / SignalR Architecture

推荐：

```text
RoomHub
  ↓
IRoomCoordinator
  ↓
IRoomStore
```

Hub SHOULD thin。SignalR Group 仅做 delivery routing，不作为 Room membership source of truth。

Phase 1 realtime semantics 包括 AttachSession、JoinRoom、SetReady、LeaveRoom、RequestRoomSnapshot，以及 RoomSnapshot、MemberJoined/Left、ConnectionChanged、ReadyChanged、RoomClosed、CommandRejected。

具体 method/event 名称 MAY 调整，语义不变。

Reconnect 必须以 PlayerSessionToken 恢复身份，并重新获取 latest RoomSnapshot；不能只依赖遗漏事件重放。

---

# 8. Credential Architecture

Initial Multiplayer：Host Provided API / BYOK。

Credential MUST：

- 通过 Server request 提交；
- 存在 Server-private memory；
- 与 Room model 分离；
- 不进入 Client response/broadcast/save/log/Git；
- Room 真正结束时删除；
- process restart 清空可接受。

Transient disconnect MUST NOT 立即删除 credential。

---

# 9. AI Provider Boundary

当前方向保持轻量：

```text
AI Gateway / IAiProvider
  ↓
Provider settings
  ↓
Credential reference
```

初始目标支持 DeepSeek + configurable OpenAI-compatible endpoint/model。

MUST NOT 预先建立复杂 provider plugin marketplace。

Server 接收自定义 outbound endpoint 时必须处理 SSRF/security boundary。具体 allowlist policy 是 implementation/security decision，当前未最终定稿。

---

# 10. Persistence

## Phase 1

`FINAL DECISION`

- Room：Memory Only；
- Credential：Memory Only；
- Database：不引入；
- Redis：不引入；
- Server restart 清空 Room/Credential 可接受。

何时引入 persistent DB / Redis / scale-out：`OPEN QUESTION`，必须由真实需求驱动。

---

# 11. Deployment

## Single Player

继续：`src + build -> outputs/trpg-dm-assistant.html`。

## Multiplayer MVP

`CURRENT DIRECTION`

```text
Internet
  ↓
HTTPS
  ↓
ASP.NET Core single process / instance
  ├─ Vue dist
  ├─ /api/*
  └─ /hubs/room
```

## Sites

`FINAL DECISION`：ChatGPT Sites 不作为 authoritative Multiplayer Server。

`OPEN QUESTION`：未来是否用于 landing/demo/frontend hosting/prototype。

---

# 12. Repository Architecture

后续最新 bootstrap 决策：

1. 当前稳定 source 仍在 `mrricardo16/personal-toolbox/trpg-dm-assistant/`；
2. Phase 1 code 前先隔离到新 private repo：`mrricardo16/personal-toolbox-trpg-site`；
3. 新 repo 初始 root 保留 Single Player 的 `README/src/build/outputs/reports`；
4. 后续新增 `docs/`、`multiplayer/`、dedicated CI；
5. old `personal-toolbox` MUST 保持 untouched。

这条后续决定 SUPERSEDES Architecture Audit 中“Phase 1 stable 后再迁独立 repo”的旧时点。

截至本次 consolidation，新 remote 尚未创建/不可见。

---

# 13. Rule Migration Direction

```text
Existing JS behavior
  ↓
Golden / Conformance Fixture
  ↓
Explicit input/output rule
  ↓
Server implementation
  ↓
Parity verification
```

目标不是复刻 global/monkey-patch 结构，而是保留规则语义。

---

# 14. Explicit Non-Goals

当前 MUST NOT 默认引入：PostgreSQL、Redis、Azure SignalR、Kubernetes、Accounts、OAuth、Matchmaking、Billing、Cloud Save、full gameplay migration、full AI KP Multiplayer、full Vue rewrite of Single Player。

## HP / Damage Migration Boundary

`CocHpDamageEngine` is a pure deterministic C# rule component fed by canonical positive damage and a server-side CON roll. `GameCoordinator.ApplyDamageAsync` is server-internal only, serializes per room, commits canonical state and revision before calling realtime delivery. `GameProjection` exposes health only to the character owner; browser clients render projection fields and contain no HP rule or mutation command.
