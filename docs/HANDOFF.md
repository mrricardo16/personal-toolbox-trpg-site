# HANDOFF

## Current Goal

在 dedicated TRPG repository 中维护稳定 Single Player，并以小提交推进 Multiplayer Foundation。

---

## Current Phase

Current verified phase (2026-08-17): Multiplayer Phase 1 Lobby MVP completed.

Completed:

- ASP.NET Core server skeleton and `/health` integration test
- `RoomSession`, `RoomPlayer`, `IRoomStore`, and `InMemoryRoomStore`
- Room Store create/get, unknown, remove, duplicate, isolation, existence, and concurrency tests
- `RoomCoordinator` Create / Join / Leave / Ready lifecycle semantics
- Per-room mutation serialization and revision/error-result contracts
- Ephemeral CSPRNG `PlayerSessionToken` session store
- Server-generated `InviteCode` registry with collision retry and close invalidation
- Minimal HTTP Create / Join / Leave / Ready bootstrap API
- Public room/player snapshot DTOs without session credentials
- SignalR `RoomHub` mapped at `/hubs/room` with explicit `AttachSession(playerSessionToken)`
- Identity-only in-memory Player Connection Registry with first/last active connection accounting
- Token-free RoomSnapshot/event projection and room-group delivery
- HTTP Join/Ready/Leave realtime delivery after canonical Coordinator commits
- First attach/last disconnect `IsConnected` lifecycle and `MemberConnectionChanged`
- Membership/readiness preservation across disconnect and same-session reattach
- Explicit Leave/host close token invalidation and group/registry cleanup
- Shared per-room mutation gate for HTTP and Hub lifecycle ordering
- Host-only public AI configuration with DeepSeek and OpenAI-compatible provider values
- In-memory server-private credential lifecycle: set, replace, read, exists, remove
- Credential cleanup on explicit host room close; ordinary leave and disconnect preserve it
- Server-side host-only API connection test without GameState or Room revision mutation
- HTTPS-only outbound endpoint policy with localhost, loopback, private, link-local, unique-local, multicast, and metadata-range rejection
- DNS resolution and resolved-address validation before outbound request
- Disabled automatic redirects, 64 KiB response bound, 10-second timeout, and sanitized result codes
- Per-room connection-test concurrency guard and fake resolver/fake handler coverage
- Vue 3 + Vite + TypeScript Lobby client with Home/Create/Join/Lobby/Ready/Leave flows
- Authenticated member-only room snapshot recovery after page reload
- SignalR snapshot replacement, reconnect reattach, connection status UX, and RoomClosed cleanup
- Host-only AI configuration and safe connection-test UI; API key remains memory-only and clears after submit

Phase 1 Lobby MVP is complete. Reconnect expiry/grace timers remain a future decision.

```text
Single Player v1.6.10        ✅ stable
Architecture Audit          ✅ complete
Context Consolidation       ✅ this package
Dedicated Repo Bootstrap    ✅ complete
Multiplayer Phase 1         ✅ Lobby MVP complete
```

---

## Current Verified Baseline

Source：

```text
mrricardo16/personal-toolbox/trpg-dm-assistant
main
```

Verified 2026-08-14：

- APP_VERSION `1.6.10`
- Save Schema `8`
- AI Protocol `1.3`
- deterministic regression `892 PASS / 0 FAIL`
- formal output `outputs/trpg-dm-assistant.html`
- output size `662681 bytes`

Dedicated target：

```text
mrricardo16/personal-toolbox-trpg-site
```

Dedicated Repository Bootstrap is complete. This repository is the primary development repository. `mrricardo16/personal-toolbox` remains a read-only legacy/stable source. The two Credential Foundation feature commits are now the current dedicated `main` history after remote verification.

## Real API Secret Configuration

**NEEDS MANUAL SECRET CONFIGURATION:** target repository secret `DS_KEY` is not configured. The manual Real API workflow remains protected by its existing secret reference and was not run locally.

---

## Current Architecture

### Single Player

```text
modular JS source
→ build-single-html
→ one standalone HTML
```

保持不重写。

### Multiplayer

```text
Vue 3 + Vite + TypeScript
          ↓
ASP.NET Core + SignalR
          ↓
Server Authoritative
```

Phase 1 Memory Only。

---

## Stable Assets

不要破坏：

- existing JS source/build；
- Scenario Engine；
- AI Protocol semantics；
- Save Schema 8 compatibility；
- Player Action Guard；
- Scenario Integrity；
- API resilience；
- Check/SAN/HP/Combat/Damage/Firearms rules；
- existing CI；
- 892 deterministic baseline。

---

## Non-negotiable Principles

1. AI 负责叙事，程序负责规则。
2. AI proposal → validation → commit。
3. Player input = intent, not world fact。
4. BLOCK UNSAFE STATE, NOT PLAYER ACTION。
5. Multiplayer Server Authoritative。
6. Client 不拥有 canonical game state。
7. Credential 不属于 Game State。
8. Secret data 不广播。
9. Disconnect ≠ Leave。
10. Preserve verified behavior。
11. Existing tests MUST remain。
12. Future ≠ Current Task。

---

## Do Not Do

不要：

- 改 old `personal-toolbox`；
- 全量 Vue rewrite；
- 一次迁所有 rules；
- Phase 1 加 DB/Redis；
- 每玩家单独 AI；
- Browser 做 Multiplayer canonical owner；
- 把 key 写入 Room DTO/save/log；
- 一次实现完整 Phase 1；
- 为了新 tests 删除旧 tests；
- 自动开始 matchmaking/accounts/billing。

---

## Next Task

Phase 1 Lobby MVP 已完成。下一阶段是单独的 Phase 2 Shared GameState design/implementation task。

AI gameplay、Multiplayer AI Protocol、Scenario/GameState migration、持久化 credential、reconnect expiry/grace、完整 rate limiting、DB、Redis、matchmaking 与 Azure SignalR 均明确留待后续任务。

---

## Files To Read

首次接手：

1. `docs/HANDOFF.md`
2. `docs/CURRENT_STATE.md`
3. `docs/DESIGN_PRINCIPLES.md`
4. `docs/ARCHITECTURE.md`
5. `docs/MULTIPLAYER_PLAN.md`
6. `docs/DECISION_LOG.md`
7. `docs/REJECTED_IDEAS.md`
8. `docs/PRODUCT_VISION.md`
9. `docs/CONTEXT_MAP.md`
10. `KEYWORDS.md`

再读取 current Repository README/code/CI。

---

## Tests To Run

在 legacy/current baseline 上，以 Repository CI 为准。

当前 CI 包含：

- JS regression scripts；
- JS syntax checks；
- build-single-html；
- verify-single-html；
- deterministic double build；
- output diff check。

新 repo 建立后：

- existing Single Player tests 继续；
- Phase 1 增加 `dotnet build` / `dotnet test`；
- Vue Client 出现后增加 client build/tests。

---

## Questions Requiring User Decision / Future Validation

- reconnect grace period；
- custom OpenAI-compatible endpoint policy；
- Room idle expiry；
- duplicate nickname policy；
- Multiplayer snapshot schema name/version；
- action batching timing；
- future Sites frontend role；
- DB/Redis 引入时点；
- matchmaking/accounts/platform billing priority。

---

## Historical Evidence Gap

目前没有足够可检索原始对话证据说明：

- 之前 Codex 具体哪一次实现为何不满意；
- 为什么明确转回网页版 ChatGPT 的具体原因。

不要自行编造。未来找到原对话后再补 `CONTEXT_MAP`。
