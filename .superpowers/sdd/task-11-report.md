# Task 11 report: read-only combat delivery and status

## Scope

- Added only the approved safe TypeScript contract, read-only lobby display, focused delivery test, client test, and this report.
- No combat API, action handler, dice, initiative, turn progression, response selection, damage, or server production code was changed.

## TDD evidence

### RED

Before client implementation, the focused client command ran with the new read-only combat rendering test. It produced **1 failed, 10 passed**: the lobby did not contain `COMBAT ACTIVE` because it did not yet consume `gameSnapshot.combat`.

### GREEN

```powershell
npm test -- --run src/components/HomeView.test.ts src/state/gameSnapshot.test.ts src/realtime/roomConnection.test.ts
```

Result: **11 passed, 0 failed**.

```powershell
npm run build
```

Result: `vue-tsc --noEmit` and Vite build succeeded.

The dedicated server delivery regression also passed:

```powershell
dotnet test multiplayer/server/Trpg.Multiplayer.slnx --no-restore --filter "FullyQualifiedName~InternalCombat_PublishesSafeSnapshotsAndAttachRecoversPendingWithoutMutation" --nologo -v:minimal
```

Result: **1 passed, 0 failed, 0 skipped**.

## Evidence

- The SignalR regression starts and begins internal combat through the existing canonical coordinator path, observes the committed revision-3 host snapshot with safe pending data, verifies a room nonparticipant receives `Combat = null`, checks internal registry/allowance names are absent, then reconnects and receives the unchanged revision-3 pending snapshot.
- The lobby displays only server-projected active/inactive state, round, current label, ordered labels, last outcome/winner/disposition state, and pending role/status. It creates no combat control.
- The client contract contains only safe projection interfaces; it adds no request or API interface.

## Validation and concern

- `git diff --check` passed; strict UTF-8 decoding passed for all authorized targets.
- The completed approved aggregate server filter passed after stale runner cleanup:

```powershell
dotnet test multiplayer/server/Trpg.Multiplayer.slnx --no-restore --filter "FullyQualifiedName~SignalRGameDeliveryTests|FullyQualifiedName~DisconnectReconnectTests" --nologo -v:minimal
```

Result: **13 passed, 0 failed, 0 skipped** (duration 3 seconds).

## Review-correction coverage

- Expanded the SignalR combat regression to assert that the revision-3 delivered snapshot is observed only after the canonical store is already at revision 3.
- It records pending exchange/current actor/action-response state before host disconnect, then asserts AttachSession recovery is revision-neutral and retains round 1, turn/current actor, the same pending exchange, null `LastExchange`, and unchanged counters.
- Recovery JSON is compared with a fresh safe `GameProjection` for the same canonical state; the snapshot additionally rejects internal registry/schedule, raw check/roll/target, policy/allowance/history/source/provenance names, and asserts opponent `Stats` is null.
- The reattached connection is explicitly stopped after assertions.

The exact aggregate server filter then completed after stale test-runner cleanup: **13 passed, 0 failed, 0 skipped** (duration 3 seconds).

## Refreshed exact client validation

```powershell
npm ci
npm test -- --run src/components/HomeView.test.ts src/state/gameSnapshot.test.ts src/realtime/roomConnection.test.ts
npm run build
```

`npm ci` installed 182 packages and audited 183 with 0 vulnerabilities. The focused client run passed **3 files / 11 tests**. `vue-tsc --noEmit` and Vite production build passed (50 modules transformed).
