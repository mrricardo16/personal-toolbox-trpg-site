# Multiplayer Phase 1 SignalR Realtime Foundation Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add real ASP.NET Core SignalR room delivery and then add disconnect/reattach semantics while preserving `RoomCoordinator` as the sole canonical Room mutation authority.

**Architecture:** A thin strongly typed `RoomHub` validates explicit session tokens, associates resolved identities with connections, and joins delivery groups. HTTP lifecycle mutations continue through `RoomCoordinator`; a singleton `IRoomRealtimeNotifier` broadcasts committed semantic events and authoritative snapshots. Commit 2 adds a singleton connection registry with per-player connection counts and routes first-attach/last-disconnect through a new Coordinator connection-state mutation.

**Tech Stack:** ASP.NET Core net8.0, built-in SignalR, `Microsoft.AspNetCore.SignalR.Client` 8.0.0 for integration tests, xUnit, `WebApplicationFactory<Program>`, TestServer LongPolling.

## Global Constraints

- Work only in `E:\personal-toolbox-trpg-site` on `main`; do not touch the legacy `personal-toolbox` repository.
- Produce exactly two feature commits: `feat: add signalr room realtime delivery` and `feat: add room disconnect and reconnect lifecycle`.
- Include the approved design and this plan in Commit 1; do not create a third documentation commit.
- Keep all edited text UTF-8; preserve existing Chinese text and logs.
- Room membership and canonical state remain in `IRoomStore`/`RoomCoordinator`; SignalR groups are delivery routing only.
- Hub must not implement Create, Join, Leave, or Ready business rules.
- Pass `PlayerSessionToken` explicitly to `AttachSession`; never use `/hubs/room?access_token=...` or another query string.
- Never place raw tokens, internal locks, store implementations, credentials, AI data, Vue code, gameplay, GameState, Scenario migration, DB, Redis, or Azure SignalR in this work.
- Existing `/health`, HTTP Room API, 36 Multiplayer tests, 37 Node regression commands, 892 assertions, 69 JS syntax checks, and deterministic single HTML output must remain green.
- For each new behavior: write a failing test, run it to confirm the expected missing-feature failure, implement the minimum code, then rerun focused and full tests.

---

## Task 1: Commit 1 test package and shared delivery contracts

**Files:**
- Modify: `multiplayer/server/tests/Trpg.Multiplayer.Api.Tests/Trpg.Multiplayer.Api.Tests.csproj`
- Create: `multiplayer/server/src/Trpg.Multiplayer.Api/Realtime/IRoomClient.cs`
- Create: `multiplayer/server/src/Trpg.Multiplayer.Api/Realtime/RoomRealtimeEvents.cs`
- Create: `multiplayer/server/src/Trpg.Multiplayer.Api/Realtime/RoomGroupNames.cs`
- Create: `multiplayer/server/src/Trpg.Multiplayer.Api/Realtime/RoomSnapshotMapper.cs`
- Create: `multiplayer/server/tests/Trpg.Multiplayer.Api.Tests/Realtime/SignalRRoomDeliveryTests.cs`

**Interfaces:**
- `IRoomClient` exposes only server-to-client methods: `RoomSnapshot`, `MemberJoined`, `MemberLeft`, `ReadyChanged`, and `RoomClosed`.
- Event DTOs contain room/player IDs, nickname/ready metadata, revision, and snapshots only; no session token.
- `RoomGroupNames.For(Guid roomId)` returns exactly `room:{roomId}`.
- `RoomSnapshotMapper.ToSnapshot(RoomSession room, IInviteCodeRegistry inviteCodes)` is the shared HTTP/Hub projection.

- [ ] **Step 1: Add the SignalR client test dependency.**

Add exactly:

```xml
<PackageReference Include="Microsoft.AspNetCore.SignalR.Client" Version="8.0.0" />
```

to the test project only.

- [ ] **Step 2: Write the failing real Hub tests.**

Create a `HubConnection` with `WithUrl(server.RootUri + "/hubs/room", options => { options.HttpMessageHandlerFactory = _ => server.CreateHandler(); options.Transports = HttpTransportType.LongPolling; })`. Add tests for valid attach, invalid token rejection, cross-room rejection, and token absence in serialized snapshot/event DTOs. Register event handlers with `TaskCompletionSource` rather than fixed `Task.Delay`.

- [ ] **Step 3: Run the focused tests and verify RED.**

Run:

```powershell
dotnet test multiplayer/server/tests/Trpg.Multiplayer.Api.Tests/Trpg.Multiplayer.Api.Tests.csproj --filter FullyQualifiedName~SignalRRoomDeliveryTests --no-restore
```

Expected: tests fail because `/hubs/room` and `AttachSession` are not implemented, not because of malformed test setup.

- [ ] **Step 4: Add the shared contract types only.**

Use typed methods with concrete DTOs, for example:

```csharp
public interface IRoomClient
{
    Task RoomSnapshot(RoomSnapshot snapshot);
    Task MemberJoined(MemberJoinedEvent message);
    Task MemberLeft(MemberLeftEvent message);
    Task ReadyChanged(ReadyChangedEvent message);
    Task RoomClosed(RoomClosedEvent message);
}
```

Keep `MemberConnectionChanged` for Task 5/Commit 2, not Commit 1.

- [ ] **Step 5: Compile the contract-only change.**

Run the focused test command again. It must still fail at the missing Hub/runtime boundary, proving the RED test is testing the intended feature.

---

## Task 2: Commit 1 connection registry and thin Hub

**Files:**
- Create: `multiplayer/server/src/Trpg.Multiplayer.Api/Realtime/IPlayerConnectionRegistry.cs`
- Create: `multiplayer/server/src/Trpg.Multiplayer.Api/Realtime/InMemoryPlayerConnectionRegistry.cs`
- Create: `multiplayer/server/src/Trpg.Multiplayer.Api/Realtime/RoomHub.cs`
- Modify: `multiplayer/server/src/Trpg.Multiplayer.Api/Program.cs`
- Create/modify: `multiplayer/server/tests/Trpg.Multiplayer.Api.Tests/Realtime/PlayerConnectionRegistryTests.cs`

**Interfaces:**
- `Register(connectionId, roomId, playerId)` returns whether this is the first active connection for the player and is idempotent for the same connection.
- `Unregister(connectionId)` returns the resolved identity and whether it was the last active connection.
- `GetConnections(roomId, playerId)`, `GetRoomConnections(roomId)`, and `RemoveRoom(roomId)` support group cleanup without exposing tokens.
- `RoomHub : Hub<IRoomClient>` exposes only `Task<RoomSnapshot> AttachSession(string playerSessionToken)` in Commit 1.

- [ ] **Step 1: Write registry tests for identity-only storage.**

Cover first registration, duplicate registration, two connections for one player, last-connection detection, room enumeration, unknown connection, and room cleanup. Assert the public registry API never returns a token.

- [ ] **Step 2: Run registry tests and verify RED.**

Run:

```powershell
dotnet test multiplayer/server/tests/Trpg.Multiplayer.Api.Tests/Trpg.Multiplayer.Api.Tests.csproj --filter FullyQualifiedName~PlayerConnectionRegistryTests --no-restore
```

Expected: compile failure because the registry types do not exist.

- [ ] **Step 3: Implement the singleton in-memory registry.**

Use `ConcurrentDictionary<string, ConnectionIdentity>` and a per-player connection set or equivalent atomic structure. Store only `ConnectionId`, `RoomId`, and `PlayerId`. Make duplicate registration idempotent and make cleanup safe when the Room has already been removed.

- [ ] **Step 4: Implement AttachSession.**

Resolve the explicit token with `IPlayerSessionStore`, load the Room from `IRoomStore`, confirm the resolved Player is present, register `Context.ConnectionId`, call `Groups.AddToGroupAsync`, and return `RoomSnapshotMapper.ToSnapshot`. Throw a generic `HubException("Session attach rejected.")` for all expected rejection paths. Do not change `IsConnected` in Commit 1.

- [ ] **Step 5: Register and map SignalR.**

In `Program.cs` add `builder.Services.AddSignalR()`, singleton connection registry, and:

```csharp
app.MapHub<RoomHub>("/hubs/room");
```

Do not alter the existing `/health` or HTTP endpoint mappings.

- [ ] **Step 6: Run Hub and registry tests GREEN.**

Run the two focused filters and then:

```powershell
dotnet test multiplayer/server/Trpg.Multiplayer.slnx --no-restore
```

Expected: all existing tests plus the new Attach/registry tests pass; Commit 1 snapshots still show `IsConnected=false` before Commit 2 attach mutation.

---

## Task 3: Commit 1 HTTP notifier and lifecycle delivery

**Files:**
- Create: `multiplayer/server/src/Trpg.Multiplayer.Api/Realtime/IRoomRealtimeNotifier.cs`
- Create: `multiplayer/server/src/Trpg.Multiplayer.Api/Realtime/SignalRRoomRealtimeNotifier.cs`
- Modify: `multiplayer/server/src/Trpg.Multiplayer.Api/RoomApi.cs`
- Modify: `multiplayer/server/src/Trpg.Multiplayer.Api/Program.cs`
- Extend: `multiplayer/server/tests/Trpg.Multiplayer.Api.Tests/Realtime/SignalRRoomDeliveryTests.cs`

**Interfaces:**
- `PublishMemberJoinedAsync(RoomSnapshot snapshot, PlayerSnapshot player)`.
- `PublishReadyChangedAsync(RoomSnapshot snapshot, Guid playerId, bool isReady)`.
- `PublishMemberLeftAsync(RoomSnapshot snapshot, Guid playerId)`.
- `PublishRoomClosedAsync(Guid roomId)`.

- [ ] **Step 1: Add failing broadcast tests.**

Attach a host connection, perform HTTP Join/Ready/normal Leave/host Leave, and await the corresponding typed client event plus snapshot. Assert revisions, member identities, remaining membership, RoomClosed delivery, and that a leaving connection receives no later room events. Add a cross-room listener to prove no event leaks between groups.

- [ ] **Step 2: Run broadcast tests and verify RED.**

Run the focused SignalR filter. Expected: Attach may work, but HTTP mutation tests fail because no notifier is registered/called.

- [ ] **Step 3: Implement the notifier.**

Inject `IHubContext<RoomHub, IRoomClient>` and the connection registry. Use `RoomGroupNames.For(roomId)`. For normal leave, remove the leaving player's connections from the group and registry before broadcasting `MemberLeft` to remaining connections. For host close, broadcast `RoomClosed`, then remove all room connections and registry entries. Never send a fake closed snapshot.

- [ ] **Step 4: Make the HTTP API notify after canonical commits.**

Inject the notifier into Create/Join/Ready/Leave handlers. Join and Ready call the notifier only after successful Coordinator results. Ready idempotent results (`RoomResult.Changed == false`) return HTTP success without a `ReadyChanged` event. Leave notifies only after successful removal/close. Create has no group to notify.

- [ ] **Step 5: Add the minimal changed-result metadata.**

Extend `RoomResult<T>` with `bool Changed`, defaulting to true for normal successful mutations and false for idempotent Ready. Preserve existing `IsSuccess`, `Value`, and `Error` callers. Add direct Coordinator assertions for both changed and no-op Ready paths.

- [ ] **Step 6: Run Commit 1 GREEN validation.**

Run:

```powershell
dotnet restore multiplayer/server/Trpg.Multiplayer.slnx
dotnet build multiplayer/server/Trpg.Multiplayer.slnx --no-restore
dotnet test multiplayer/server/Trpg.Multiplayer.slnx --no-restore
```

Expected: zero warnings/errors and all existing plus real SignalR delivery tests pass.

- [ ] **Step 7: Stage and create Commit 1.**

Stage only the approved design/plan docs and Commit 1 source/tests. Run `git diff --cached --check`, confirm no Commit 2 names or docs updates are staged, then commit:

```powershell
git commit -m "feat: add signalr room realtime delivery"
```

Push `main`, fetch `origin/main`, and verify local SHA equals remote SHA before starting Commit 2.

---

## Task 4: Commit 2 connection-state RED tests

**Files:**
- Modify: `multiplayer/server/src/Trpg.Multiplayer.Api/Rooms/RoomCommands.cs`
- Modify: `multiplayer/server/tests/Trpg.Multiplayer.Api.Tests/Rooms/RoomCoordinatorTests.cs`
- Extend: `multiplayer/server/tests/Trpg.Multiplayer.Api.Tests/Realtime/SignalRRoomDeliveryTests.cs`
- Create: `multiplayer/server/tests/Trpg.Multiplayer.Api.Tests/Realtime/DisconnectReconnectTests.cs`

**Interfaces:**
- Add `SetConnectedRoomCommand(Guid RoomId, Guid PlayerId, bool IsConnected)` or the repository-equivalent name.
- Add `RoomCoordinator.SetConnectedAsync(...)` returning the existing `RoomResult<RoomSession>` contract with `Changed` metadata.

- [ ] **Step 1: Change HTTP-created members to disconnected baseline.**

Update Create/Join expectations and implementation so a member has `IsConnected=false` until SignalR AttachSession. Preserve host/member identity, readiness, capacity, and all existing HTTP auth behavior.

- [ ] **Step 2: Write failing Coordinator tests.**

Cover connected false-to-true and true-to-false revision increments, same-value idempotence, missing Room, closed Room, non-member, and concurrent SetConnected/Leave behavior.

- [ ] **Step 3: Write failing real Hub lifecycle tests.**

Cover attach sets connected true, disconnect preserves member/ready state but sets false, same token reattach restores true without duplicate Player, host disconnect keeps the Room, two active connections keep true until the last closes, explicit Leave rejects reattach, and host close rejects every old token.

- [ ] **Step 4: Run the focused tests and verify RED.**

Run the Coordinator and disconnect filters. Expected: compile or assertion failures because `SetConnectedAsync`, `OnDisconnectedAsync`, and multi-connection behavior are not implemented.

---

## Task 5: Commit 2 connection lifecycle GREEN implementation

**Files:**
- Modify: `multiplayer/server/src/Trpg.Multiplayer.Api/Rooms/RoomCoordinator.cs`
- Modify: `multiplayer/server/src/Trpg.Multiplayer.Api/Realtime/RoomHub.cs`
- Modify: `multiplayer/server/src/Trpg.Multiplayer.Api/Realtime/IRoomClient.cs`
- Modify: `multiplayer/server/src/Trpg.Multiplayer.Api/Realtime/RoomRealtimeEvents.cs`
- Modify: `multiplayer/server/src/Trpg.Multiplayer.Api/Realtime/IPlayerConnectionRegistry.cs`
- Modify: `multiplayer/server/src/Trpg.Multiplayer.Api/Realtime/InMemoryPlayerConnectionRegistry.cs`
- Modify: `multiplayer/server/src/Trpg.Multiplayer.Api/Realtime/IRoomRealtimeNotifier.cs`
- Modify: `multiplayer/server/src/Trpg.Multiplayer.Api/Realtime/SignalRRoomRealtimeNotifier.cs`
- Modify: `multiplayer/server/src/Trpg.Multiplayer.Api/Program.cs`

**Interfaces:**
- `RoomCoordinator.SetConnectedAsync(SetConnectedRoomCommand)` serializes through the same room lock and returns `Changed=false` for no-op state.
- Registry `Unregister` identifies whether the removed connection was the player's last active connection.
- `IRoomClient.MemberConnectionChanged` carries `PlayerId`, `IsConnected`, and `Revision` or an equivalent non-secret event DTO plus the authoritative snapshot.

- [ ] **Step 1: Implement Coordinator connection mutation.**

Find the member under the existing per-room lock. Return `RoomNotFound`, `RoomClosed`, or `NotMember` through `RoomResult`; use `player with { IsConnected = command.IsConnected }`; replace the snapshot atomically; increment only on a real state change.

- [ ] **Step 2: Implement first-attach semantics.**

After token/membership validation and group registration, call `SetConnectedAsync(true)` only when registry registration reports the first connection. If the Coordinator mutation changes the snapshot, broadcast `MemberConnectionChanged` and return the post-mutation snapshot. If group add fails, unregister the connection and do not leave a stale registry entry.

- [ ] **Step 3: Implement `OnDisconnectedAsync`.**

Unregister `Context.ConnectionId`. If it was not the last connection, stop. If it was last, call `SetConnectedAsync(false)` and broadcast only a successful changed result. Swallow expected RoomNotFound/NotMember cleanup results; do not recreate a Room or throw token/internal details.

- [ ] **Step 4: Implement multi-connection and cleanup delivery.**

Ensure first attach/last disconnect are the only canonical state transitions. Explicit Leave and host close must remove connection registry entries and SignalR group membership consistently with the Commit 1 notifier.

- [ ] **Step 5: Run focused GREEN tests.**

Run Coordinator, registry, and disconnect/reconnect filters. Expected: all lifecycle tests pass, including same-token reattach and multi-connection revision assertions.

---

## Task 6: Commit 2 documentation and full validation

**Files:**
- Modify only after implementation is green: `docs/CURRENT_STATE.md`
- Modify only after implementation is green: `docs/HANDOFF.md`
- Tests/source from Tasks 4-5 as needed for final fixes.

- [ ] **Step 1: Update dynamic docs.**

Record completed `RoomHub`, `AttachSession`, SignalR RoomSnapshot/event delivery, Connection Registry, `IsConnected` lifecycle, disconnect preservation, same-session reattach, multi-connection safety, and explicit session invalidation. Record reconnect expiry/grace timers, Credential/API key, Host API, Vue, gameplay, and GameState as not implemented. Keep existing Chinese text UTF-8.

- [ ] **Step 2: Run .NET verification.**

Run `dotnet restore`, `dotnet build --no-restore`, and `dotnet test --no-restore`; require zero warnings/errors and zero failed tests.

- [ ] **Step 3: Run the unchanged Single Player gates.**

Execute all 37 `build/test-*.js` commands from `.github/workflows/trpg-ci.yml`, `node --check` for all 69 JS files, `build-single-html.js`, `verify-single-html.js`, a second build with equal SHA256, exactly one HTML file, unchanged generated artifact, and `git diff --check`.

- [ ] **Step 4: Run security and scope checks.**

Confirm no API key/credential values, raw token in snapshots/events/logs, URL token support, SignalR group membership used as Room truth, Hub business mutations, DB/Redis/Vue/gameplay code, or legacy repository changes. Strictly decode all changed text as UTF-8.

- [ ] **Step 5: Stage only Commit 2 files and amend/fix before commit.**

Run `git diff --cached --check`, inspect staged names, then commit:

```powershell
git commit -m "feat: add room disconnect and reconnect lifecycle"
```

Push `main`, fetch `origin/main`, verify local SHA equals `origin/main`, verify `git status -sb` is clean, and stop. Do not create a third commit or begin Credential/Vue work.

## Self-review checklist

- [x] Every design requirement has a corresponding task and test.
- [x] No task introduces a second Room mutation authority.
- [x] Commit 1 never mutates `IsConnected`; Commit 2 owns first/last connection transitions.
- [x] Event payloads and connection registry never expose raw session tokens.
- [x] The plan contains no unresolved placeholders or unspecified error behavior.
- [x] The two commit boundaries remain explicit and documentation is included without a third commit.
