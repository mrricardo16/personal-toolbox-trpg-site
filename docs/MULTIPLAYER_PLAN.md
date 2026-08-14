# MULTIPLAYER_PLAN

## 1. 总原则

Multiplayer 采用 Progressive Migration。

```text
Stable Single Player
  ├─ continues independently
  └─ provides proven behavior/reference

Dedicated Multiplayer
  ├─ Vue Client
  └─ ASP.NET Core + SignalR Server
```

Multiplayer MUST NOT 通过全量重写 Single Player 来开始。

---

# 2. Pre-Phase — Repository Bootstrap

这是后续最新工作流决定，位于 Phase 1 之前。

目标：`mrricardo16/personal-toolbox-trpg-site`

任务只做：

- isolated workspace；
- clone/verify legacy source；
- import stable Single Player to new repo root；
- preserve build/tests/output；
- create authoritative docs；
- adapt CI；
- create private remote；
- verify clean baseline。

MUST NOT：

- 修改 old `personal-toolbox`；
- 开始 broad Vue/.NET rewrite；
- 实现 Room/gameplay；
- copy secret values。

---

# 3. Phase 1 — Multiplayer Foundation

## 3.1 Goal

证明：

> Room lifecycle + player identity + realtime synchronization + Host credential boundary 可以稳定工作。

Phase 1 不是 TRPG gameplay migration。

## 3.2 Server Foundation

目标能力：

- ASP.NET Core server skeleton；
- DI；
- test project；
- `IRoomStore`；
- `InMemoryRoomStore`；
- minimal `RoomSession`；
- `RoomPlayer`；
- `RoomSnapshot`；
- Room coordinator/service；
- SignalR RoomHub；
- server-side authorization boundary。

## 3.3 Room Lifecycle

包括：

- Create Room；
- Join Room；
- Leave Room；
- Host Close/End Room；
- max players；
- Ready；
- member connected/disconnected state；
- room status；
- room revision。

第一版保持简单，不建立复杂多状态 state machine。

## 3.4 Player Identity

必须有：

```text
PlayerId
PlayerSessionToken
```

InviteCode 只表示进入 Room 的许可，不是 identity token。

Session token 用于玩家身份与 reconnect，可存在 Client `sessionStorage`，与 API credential 完全不同。

## 3.5 SignalR

Phase 1 使用 SignalR 做 realtime Room updates。

```text
RoomHub
  ↓
Room Coordinator
  ↓
Room Store
```

Group 只用于 delivery routing；Snapshot 是恢复 authority source。

## 3.6 Reconnect

必须验证：

```text
Player B joins
  ↓
disconnect
  ↓
reconnect
  ↓
same PlayerId
  ↓
same Room
  ↓
latest RoomSnapshot
```

`Disconnect != Leave`

Host transient disconnect MUST NOT 立即销毁 Room/Credential。

Exact timeout/grace period：`OPEN QUESTION`。

## 3.7 Host Provided Credential

Phase 1 包括：

- Host provider config；
- model；
- endpoint；
- API key input；
- server-side API connection test；
- InMemoryCredentialStore；
- RoomId→credential lifecycle；
- Room close cleanup；
- secret leak tests。

Credential MUST NOT 出现在 Room DTO、Game state、broadcast、save、logs、client-visible exception、Git。

## 3.8 Minimal Multiplayer Client

技术：Vue 3 + Vite + TypeScript + SignalR Client。

最小 UI：

### Home
- Create Room
- Join Room

### Create Room
- nickname
- scenario/rules metadata as needed
- max players
- provider/model/endpoint
- API key

### Lobby
- room code
- members
- host marker
- ready state
- leave
- host end

Phase 1 MUST NOT 重建完整 Single Player gameplay UI。

## 3.9 First Codex Implementation Boundary

已生成的 Phase 1 start prompt 更严格：

### Commit 1
只创建 minimal ASP.NET Core server skeleton + test project。

### Commit 2
只创建 `IRoomStore`、`InMemoryRoomStore`、minimal Room model 和 store tests。

之后 STOP。

## 3.10 Phase 1 Definition of Done

最终至少满足：

```text
Host creates
Player B joins
Both see same room/member snapshot
B Ready -> A sees new revision
B disconnects/reconnects -> same PlayerId / Room
Room capacity enforced
Unauthorized host command rejected
Cross-room state isolated
Host closes Room -> Room removed
Host closes Room -> Credential removed
Credential absent from DTO/log/error
```

同时：existing Single Player CI green；.NET build/test green；Vue build green；SignalR integration test 覆盖真实 HubConnection path。

---

# 4. Phase 2 — Shared Game State

## 4.1 Goal

把 Multiplayer 从同步 Lobby 推进到 Server-owned canonical TRPG Game State。

## 4.2 New Multiplayer State

不要修改 Single Player Schema 8 承载 multiplayer runtime。

建立独立 Multiplayer model，例如：

```text
MultiplayerGameState v1
MultiplayerGameSnapshot v1
```

具体命名可调整。

## 4.3 Multiple Characters

从当前 `state.character` 演化为类似：

```text
Characters[PlayerId] -> Character
```

只属于 Multiplayer model。

## 4.4 Rule Migration Order

当前建议按垂直能力逐步迁：

1. core state/contracts/scenario activation；
2. Check + Dice；
3. HP/SAN consequences；
4. Scenario/Clue；
5. Combat；
6. Damage/Firearms。

顺序 MAY 根据实现依赖调整，但 MUST 保持 small step + parity。

## 4.5 Golden / Conformance

建立 test vector：

```json
{
  "initialState": {},
  "command": {},
  "randomSequence": [],
  "expectedState": {},
  "expectedEvents": []
}
```

同一 fixture 在 Existing JS reference 与 Server rule implementation 中比较。

不要求把 892 条 tests 逐条翻译成 C#。

## 4.6 Player Projection

Phase 2 必须正式实现：

```text
Canonical Game State
  ↓
Projection Service
  ↓
Player-visible Snapshot
```

隔离 hidden clues、secret rolls、hidden NPC info、director data、internal AI runtime。

---

# 5. Phase 3 — AI KP Multiplayer

## 5.1 Goal

```text
Players
  ↓
Room Action Coordinator
  ↓
Rules / pending checks
  ↓
AI Gateway
  ↓
Structured Proposal
  ↓
Server Validation
  ↓
Canonical Commit
  ↓
Player Projections
  ↓
Broadcast
```

## 5.2 AI Protocol Evolution

保留当前 AI Protocol 核心：requestId、baseRevision、structured changes、validation、transaction commit、stale response protection。

未来 payload 需要支持 multiple actors、active player(s)、shared world、room action context。

是否命名为 `AI Protocol 2.0`：`OPEN QUESTION`。

## 5.3 Action Scheduling

### Investigation

Server MAY queue individual actions、resolve mechanical-only actions、batch group declarations、select one world advance。

不应：`one player message = one full AI world advance`。

### Group Resolution

可以 collect actions 后只做 one AI world advance。具体 wait window / batching policy：`OPEN QUESTION`。

### Combat

由 Server Combat State → Current Actor → Command → Deterministic Resolution → AI Narrative → Next Turn。

---

# 6. Future — Not Current Task

- Matchmaking；
- Accounts；
- Public Rooms；
- Friends；
- persistent profiles；
- Platform Provided AI；
- Billing；
- Human Keeper mode；
- Cloud Save；
- persistent DB；
- Redis/backplane；
- multi-instance scale-out；
- ranking；
- scenario marketplace。

`Future ≠ Current Task`

---

# 7. Open Questions

1. reconnect grace period；
2. duplicate nickname policy；
3. initial endpoint allowlist / custom endpoint security policy；
4. Room expiration / idle timeout；
5. Multiplayer snapshot formal schema/version naming；
6. Action batching wait/priority；
7. Phase 3 AI Protocol 是否升级到 2.0；
8. 何时需要 persistent database；
9. 何时需要 Redis / scale-out；
10. Sites 是否未来用于 frontend/demo；
11. public room/matchmaking 的实际产品优先级。
