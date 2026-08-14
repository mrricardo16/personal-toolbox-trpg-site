# Multiplayer Phase 1 SignalR Realtime Foundation Design

## Goal

Add a thin ASP.NET Core SignalR delivery layer for the existing authoritative room lifecycle, then add connection-aware disconnect and reattach semantics without introducing credentials, Vue, gameplay, persistence, or a second room mutation authority.

## Scope and commit boundary

This work is limited to exactly two feature commits:

1. `SignalR Room Realtime Delivery`
2. `Disconnect / Reconnect Foundation`

The design document is included in Commit 1 rather than committed separately. After Commit 2 is validated and pushed, work stops.

Out of scope: Credential/API key, AI provider, Vue, gameplay/GameState, Scenario migration, database, Redis/backplane, Azure SignalR, matchmaking, accounts, billing, automatic client reconnect, and reconnect expiry/grace timers.

## Authority and data flow

The existing `RoomCoordinator` remains the only authority for canonical Room mutation. `IRoomStore` remains storage-only. SignalR groups are delivery routing state and are never treated as membership truth.

```text
HTTP mutation
  -> RoomCoordinator
  -> IRoomStore canonical commit
  -> IRoomRealtimeNotifier
  -> SignalR group clients

Hub.AttachSession(token)
  -> IPlayerSessionStore token resolution
  -> RoomCoordinator membership validation
  -> IPlayerConnectionRegistry connection association
  -> SignalR group membership
  -> current RoomSnapshot
```

The token is passed explicitly to `AttachSession`; it is never accepted from a hub URL query string. The connection registry stores only resolved `RoomId`, `PlayerId`, and `ConnectionId`, never the raw session token.

## Commit 1: SignalR Room Realtime Delivery

### Components

- `RoomHub` mapped at `/hubs/room`.
- `IRoomClient` strongly typed server-to-client event contract.
- `RoomGroupNames` single helper for `room:{RoomId}` names.
- `IPlayerConnectionRegistry` and in-memory implementation for connection-to-identity and identity-to-connections lookup.
- `IRoomRealtimeNotifier` and `SignalRRoomRealtimeNotifier` for HTTP-to-group delivery.
- `RoomSnapshotMapper` shared by HTTP and Hub responses so DTO projection remains consistent.

### Hub contract

The only client-to-server method in Commit 1 is:

```text
Task<RoomSnapshot> AttachSession(string playerSessionToken)
```

Attach validates a non-empty token, resolves it through `IPlayerSessionStore`, confirms the Room still exists and the Player is still a canonical member, registers the connection identity, joins the SignalR group, and returns the latest authoritative snapshot. Any failure is rejected with a generic `HubException` message that contains no token or internal storage detail.

Commit 1 does not mutate `RoomPlayer.IsConnected`; connection state mutation belongs to Commit 2.

### Server-to-client events

Events carry semantic metadata and, where a room remains active, the latest authoritative `RoomSnapshot`:

- `RoomSnapshot`
- `MemberJoined`
- `MemberLeft`
- `ReadyChanged`
- `RoomClosed`

No event carries `PlayerSessionToken`, raw `RoomSession`, internal locks, store objects, or future credentials. `RoomClosed` does not invent a snapshot for a removed Room.

HTTP lifecycle success paths call the notifier after the Coordinator commit:

- Join: `MemberJoined` plus current snapshot.
- Ready change: `ReadyChanged` plus current snapshot.
- Ready idempotent no-op: no semantic event; the unchanged snapshot may still be returned by HTTP.
- Normal leave: remove the leaving player's connections from the group, then broadcast `MemberLeft` plus current snapshot to the remaining group.
- Host leave: `RoomClosed`, then all Room connections and sessions are cleaned up.

Create has no existing Room group to broadcast to.

### Commit 1 tests

Use `WebApplicationFactory<Program>` and `Microsoft.AspNetCore.SignalR.Client` with TestServer LongPolling. Tests must exercise the real Hub protocol and cover:

- valid AttachSession returns the correct RoomSnapshot and identity;
- invalid/empty token is rejected and does not join a group;
- a token from Room A cannot attach to Room B;
- HTTP Join reaches an attached host as `MemberJoined` and a snapshot containing the new member;
- HTTP Ready reaches attached members as `ReadyChanged` and an updated revision/snapshot;
- normal HTTP Leave reaches the remaining members as `MemberLeft`, and the leaving connection is removed from delivery;
- host HTTP Leave reaches connected members as `RoomClosed`;
- snapshots and event payloads do not contain session tokens.

## Commit 2: Disconnect / Reconnect Foundation

### Connection semantics

`Disconnect != Leave`. HTTP Create/Join produces a canonical member with `IsConnected=false`. The first successful AttachSession changes it to true; transient disconnect preserves membership, readiness, host role, PlayerId, and session validity.

Add a Coordinator operation equivalent to:

```text
SetConnected(RoomId, PlayerId, bool connected)
```

The operation is serialized by the existing per-room lock. A real state change increments Revision; setting the current value is an idempotent no-op. If the Room or Player no longer exists, it returns the existing business result/error contract.

### Multiple connections

The registry maps a Player identity to zero or more active ConnectionIds. The first connection for a Player causes `SetConnected(true)`. Additional connections do not increment Revision. A disconnect only causes `SetConnected(false)` when the last connection for that Player disappears.

### Hub lifecycle

- AttachSession registers the connection and adds it to the Room group; if it is the first connection, it invokes `SetConnected(true)` and returns the post-mutation snapshot.
- Repeated AttachSession calls for the same active `ConnectionId` are idempotent and do not duplicate registry entries or group membership.
- `OnDisconnectedAsync` unregisters the connection. If other connections remain, no canonical state changes. If it was the last connection, it invokes `SetConnected(false)` and broadcasts `MemberConnectionChanged` plus the latest snapshot.
- If the Room was explicitly closed before disconnect cleanup, cleanup is best effort, does not recreate the Room, and does not throw an unhandled 500.
- Reattach with the same valid token resolves to the same PlayerId and RoomId, does not create a new Player, and returns the latest snapshot.
- Explicit Leave removes the session, so the old token cannot reattach. Host close invalidates all Room sessions.

### Commit 2 tests

Add real HubConnection integration coverage for:

- disconnect preserves membership and readiness while setting `IsConnected=false`;
- reattach with the same token restores `IsConnected=true`, same PlayerId, same RoomId, and latest Revision/snapshot;
- reattach never creates a duplicate Player;
- host disconnect preserves the Room and host role;
- two connections for one Player keep `IsConnected=true` after the first closes and become false only after the last closes;
- explicit Leave invalidates reattach;
- host close invalidates every Room session;
- connection and explicit-leave races do not recreate a Player/Room or escape an expected not-found result.

## DI and compatibility

All runtime registries/notifiers are Singleton services. Existing `/health` and HTTP room endpoints remain available. Existing RoomCoordinator, RoomApi, and Single Player tests remain green. No Single Player source, generated HTML, Save Schema, or AI Protocol is changed.

## Validation and documentation

For both commits, run `dotnet restore`, `dotnet build`, focused/full `dotnet test`, all existing 37 Node regression commands, JavaScript syntax checks, single-HTML build/verify, deterministic double-build, HTML count, generated-output diff, `git diff --check`, UTF-8 checks, and forbidden-scope scans.

After Commit 2 only, update `docs/CURRENT_STATE.md` and `docs/HANDOFF.md` to record the completed Hub, AttachSession, delivery events, connection registry, IsConnected lifecycle, disconnect, reattach, and multi-connection semantics. Record reconnect expiry, Credential, Host API, Vue, gameplay, and GameState as not implemented.
