# Phase 2F Commit 2 Aggregate Report

## Working Tree

Commit 2 is based on synchronized Commit 1 `633e0477efe4903900844250316935548d3c1b9a`.

The cumulative tracked implementation changes are limited to:

- `multiplayer/server/src/Trpg.Multiplayer.Api/Gameplay/MultiplayerGameState.cs`
- `multiplayer/server/src/Trpg.Multiplayer.Api/Gameplay/GameContracts.cs`
- `multiplayer/server/src/Trpg.Multiplayer.Api/Gameplay/IGameCoordinator.cs`
- `multiplayer/server/src/Trpg.Multiplayer.Api/Gameplay/GameCoordinator.cs`
- `multiplayer/server/src/Trpg.Multiplayer.Api/Gameplay/GameProjection.cs`
- `multiplayer/server/tests/Trpg.Multiplayer.Api.Tests/Gameplay/GameStateTests.cs`
- `multiplayer/server/tests/Trpg.Multiplayer.Api.Tests/Gameplay/GameApiTests.cs`
- `multiplayer/server/tests/Trpg.Multiplayer.Api.Tests/Realtime/SignalRGameDeliveryTests.cs`
- `multiplayer/client/src/contracts/rooms.ts`
- `multiplayer/client/src/components/LobbyView.vue`
- `multiplayer/client/src/components/HomeView.test.ts`

The untracked `.superpowers/sdd` files are the intended Task 6-11 briefs, reports, reviews, and this aggregate evidence report. No `src/`, `build/`, `outputs/`, formal Single Player artifact, public Combat route, Combat Damage, weapon, Firearms, Healing, SAN, Scenario, AI gameplay, persistence, DB, Redis, timeout, disconnect-forfeit, or Player Combat Intent implementation is present in the Commit 2 diff.

Scope confirmation: **PASS**.

## Task Evidence

### Task 6

- RED: state tests failed to compile because `MultiplayerGameState.Combat` and its constructor parameter were absent.
- GREEN: 25 passed, 0 failed, 0 skipped.
- Added optional canonical `CombatSession`, internal command/result contracts, combat errors, and the internal coordinator seam without routes.

### Task 7

- Initial RED demonstrated absent Start/Begin behavior; review-correction RED was 23 passed / 1 failed for the missing truthful interface seam.
- Final GREEN: 24 passed, 0 failed, 0 skipped.
- Added trusted Start and Begin with CharacterId identity, canonical stat snapshots, stable DEX order, server ExchangeId, non-empty distinct read-only responses, revision discipline, and no Begin dice/counters/turn/history/HP mutation.

### Task 8

- RED: four new transition tests failed while Resolve/Pass/End methods were absent; the previous 24 tests passed.
- Final GREEN: 28 passed, 0 failed, 0 skipped.
- Added validated Resolve, Pass, and End; server dice; shared Check semantics; stable exchange identity; bounded history; independent pending-disposition registry; and duplicate/stale fail-closed behavior.

### Task 9

- Initial RED: 28 passed / 2 failed for missing round-wrap linkage.
- Review-correction RED: 31 passed / 1 failed for stale fresh-dying observation.
- Final GREEN: 32 passed, 0 failed, 0 skipped; combined health/combat GREEN: 43 passed, 0 failed, 0 skipped.
- Added per-CharacterId dying scheduling, same-round suppression, deterministic multi-character processing, fresh-episode re-observation, and one canonical round-wrap replacement.

### Task 10

- RED: test compilation produced seven missing-contract errors for the absent safe Combat projection.
- GREEN: 43 passed, 0 failed, 0 skipped.
- Added viewer-specific safe projection and route-absence coverage; domain Combat records and registries remain unprojected.

### Task 11

- Server delivery/reconnect regression: 13 passed, 0 failed, 0 skipped for the approved realtime filter.
- Client RED: 1 failed / 10 passed because read-only Combat rendering was absent.
- Client focused GREEN: 3 files / 11 tests passed; production build passed.
- Added only safe snapshot delivery/recovery coverage and read-only Vue status.

## TDD

Task 11 server RED exception:

> TDD RED not applicable for Task 11 server regression coverage. The tested server behaviors were intentionally implemented by Tasks 6–10 and Task 11 had no authorization to alter those production paths. Newly added regression tests passed against the existing implementation on first execution; no artificial production regression was introduced solely to manufacture a RED state.

Task 11 client RED evidence: **1 failed / 10 passed** due to missing read-only Combat rendering.

## Core Invariants

### Begin

**PASS**. Expected revision, current actor, active opposing defender, participant/controller authority, and absence of pending state are validated first. Begin generates the server ExchangeId, snapshots non-empty canonical `AvailableResponses` and `ResponseCountBefore`, commits pending state with exactly one revision increment, and publishes only after commit. It performs no dice, Check, HP, counter, turn, or resolved-history work.

### Resolve

**PASS**. Active state, expected revision, exact current ExchangeId, participant activity/identity, defender authority, and pending response membership are validated before dice. Rejections do not roll, mutate, revise, or write the registry. Success uses `IDiceRoller`, existing Check resolution, and the opposed engine; retains the same ExchangeId; increments response/action once; clears pending; appends bounded history; registers any non-null disposition under that ExchangeId; advances turn/wrap; and performs one canonical replacement/revision. Duplicate Resolve fails before a second roll or registry write.

### Pass

**PASS**. Pending state blocks Pass. Only the authorized current actor can Pass; success increments the action counter, advances turn/wrap, and increments revision once.

### End

**PASS**. End is trusted/host internal-only, may clear a pending exchange atomically, records exact reason `combat_ended_before_resolution`, and cannot commit inactive plus pending. It performs no roll, response/action increment, turn advancement, resolved history append, or disposition creation.

### Disposition registry

**PASS**. `CombatSession.PendingDamageDispositions` uses ExchangeId as the canonical key and is independent of `History` and `LastExchange`. The 121-resolution regression proves 120-entry history trimming while the first unconsumed disposition remains addressable; all 121 dispositions remain pending. Phase 2F neither consumes nor completes dispositions and performs no Combat Damage or exchange HP mutation.

### Round wrap

**PASS**. Scheduling is keyed by CharacterId. Already-dying and fresh-dying episodes are observed without same-round checks; eligible checks occur at most once per later completed round. Stabilization removes eligibility, fresh episodes reset observation, multiple characters process in combat order, and one death only inactivates that participant. The wrap computes all changes in memory, performs one state replacement/revision/commit, and the outer canonical command publishes one snapshot after commit without invoking single-character coordinator mutation methods in the loop.

### Projection

**PASS**. `CombatSession` is never sent directly. Participants receive safe status/round/current/order/labels/side/activity, their own approved stats, safe last-exchange summary, and safe pending role/status. Other-player and opponent raw stats are hidden. Raw rolls, targets, response policy/allowance, full history, registry, dying schedule, source, provenance, and stabilization internals are absent. Room nonparticipants receive `Combat = null`; projection preserves revision.

### Realtime

**PASS**. Canonical transitions run under the room lock, replace state first, then invoke the existing viewer-specific `GameSnapshot` publisher. No Combat-specific event or pre-commit broadcast was added.

### Reconnect

**PASS**. AttachSession returns the latest viewer-specific snapshot. The regression preserves revision 3, round 1, current actor, exact pending ExchangeId, null `LastExchange`, and unchanged counters across disconnect/reconnect. No auto-response, selection, resolution, turn, round, or revision mutation occurs; nonparticipants remain `Combat = null`.

## Validation

Server:

- restore: **PASS**
- build: **PASS**, 0 warnings / 0 errors
- test: **199 passed / 0 failed / 0 skipped**
- format verify-no-changes: **PASS**

Client:

- `npm ci`: **PASS**, 182 packages installed, 183 audited, 0 vulnerabilities
- test: **6 files / 22 tests passed**
- build: **PASS**, `vue-tsc --noEmit` and Vite, 50 modules transformed

Check fixture:

- **19 cases**
- SHA256: `2B318131B76B5FDDE16DE0DFDC5D3479F83E1212D2556F13582EBD90B7296C4F`

HP fixture:

- **10 cases**
- SHA256: `806BF2001D0065CCC323A6159EF736AE3FEB8611903058AB8E18D29110B86AD9`

Stabilization fixture:

- **21 cases**
- SHA256: `ADAC219C8A1F6F9491289808351D48918352FC69427FB30696D16A0765F67BBE`

Combat fixture:

- **19 cases**

Combat exporter SHA 1:

`FF0D8774C6ED56EFA416E73F0EE5F1ABC7E94EAFC081A44CFBBD749B37D7516C`

Combat exporter SHA 2:

`FF0D8774C6ED56EFA416E73F0EE5F1ABC7E94EAFC081A44CFBBD749B37D7516C`

Single Player:

- authoritative offline CI regression commands: **37 / 37 PASS**
- JavaScript syntax: **69 / 69 PASS**

Formal HTML:

- build: **PASS**
- `VERIFY_SINGLE_HTML:PASS`
- SHA before / after first / after second: `0A635D94CDD7284B35433092C834D92BCAD44961C14E30DBD565AB48B7E14D4D`
- deterministic double build: **PASS**
- HTML count: **1**
- formal artifact unchanged: **PASS**

## Scope

Public Combat API count: **0**

Vue Combat action button count: **0**

Client Combat API/request pattern count: **0**

Combat Damage: **NO**

Player Combat Intent: **NO**

Timeout/disconnect policy: **NO**

## Encoding

UTF-8: **PASS**

`git diff --check`: **PASS**

## Concerns

- The broad filesystem glob `build/test-*.js` currently finds 40 files, including two credentialed real-API acceptance scripts and one legacy helper. The established protected baseline is the 37 explicit offline regression commands in `.github/workflows/trpg-ci.yml`; those authoritative 37 commands all passed. No API credential was supplied and no real external API acceptance was claimed.
- `npm ci` emitted a deprecation warning for transitive `glob@10.5.0`; the audit reported 0 vulnerabilities and the client tests/build passed. Dependency cleanup is outside Commit 2 scope.
- Git reports prospective LF-to-CRLF conversion warnings for several working-copy files. Current bytes decode as strict UTF-8 and `git diff --check` passes; no encoding or content corruption was observed.

## Escalation Required

**NO**
