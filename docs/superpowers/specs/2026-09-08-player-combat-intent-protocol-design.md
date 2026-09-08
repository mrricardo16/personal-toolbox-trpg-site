# Multiplayer Phase 2H — Player Combat Intent Protocol Design

**Status:** Approved after application-state-access review. Implementation plan pending.

**Date:** 2026-09-08

**Scope:** Multiplayer player-facing Combat intent protocol only

**Depends on:** Phase 2F Combat Opposed and Phase 2G Combat Damage

## 1. Goal

Define the smallest safe player-facing protocol that lets an authenticated Multiplayer participant:

- initiate a melee opposed exchange on their own current turn;
- answer a pending exchange when their own investigator is the defender; and
- pass their own current turn.

The server remains the sole authority for identity, ownership, turn order, targets, response availability, dice, opposed outcomes, damage, HP/vitality, round advancement, and realtime snapshots. The client submits intent and renders the resulting authoritative `GameSnapshot`; it does not run a second Combat state machine.

This document is a design audit, not an implementation plan. No Phase 2H code is authorized by this document.

## 2. Scope

The target Phase 2H slice is limited to:

- three authenticated HTTP intent routes: melee attack, respond, and pass;
- a dedicated player-intent application coordinator over the existing internal canonical Combat transitions;
- safe viewer-specific Combat action affordances in `CombatSnapshot`;
- automatic server selection of an NPC defender's snapshotted response policy;
- automatic server consumption of any exact Pending Combat Damage disposition created by an opposed resolution;
- existing viewer-specific SignalR `GameSnapshot` delivery after each committed canonical transition;
- reconnect recovery from the latest canonical snapshot without mutation; and
- thin client controls that appear only from server-projected affordances.

## 3. Non-goals

Phase 2H does not add or expose:

- public Start Combat, End Combat, or Resolve Combat Damage routes;
- public NPC actor control, NPC pass, or an NPC-attacker driver;
- player-versus-player Combat or same-side attacks;
- timeout, disconnect forfeit, automatic player response, or turn advancement on disconnect;
- client dice, initiative, opposed-resolution, outnumbered, damage, HP, turn, or round authority;
- client-authored actor ownership, response availability, NPC policy, target eligibility, damage mode, weapon profile, or armor;
- firearms, Impaling, maneuver, movement, range, ammunition, healing, or Combat lifecycle UI;
- Scenario, AI gameplay decisions, persistence, multi-process coordination, or distributed exactly-once processing; or
- any change to the Single Player rules engine or formal release artifact.

## 4. Current-State Audit

### 4.1 Public HTTP surface

`GameApi.MapGameEndpoints` currently maps exactly:

1. `POST /api/rooms/{roomId}/game/initialize`
2. `GET /api/rooms/{roomId}/game`
3. `POST /api/rooms/{roomId}/game/check`

The current public Combat route count is **zero**. Phase 2F/2G tests also assert that Start, Attack, Respond, Dodge, Fight Back, Pass, End, and Damage paths return 404. Phase 2H must add only the three player-intent routes specified in this document; Start, End, and Damage remain internal.

### 4.2 Canonical internal Combat operations

The server already owns internal transitions for:

- `StartCombatAsync`
- `BeginOpposedExchangeAsync`
- `ResolvePendingExchangeAsync`
- `ResolveCombatDamageAsync`
- `PassCombatTurnAsync`
- `EndCombatAsync`

Start snapshots investigator/opponent Combat facts and establishes stable order. Begin records one pending exchange and does not roll. Resolve validates the pending exchange and response authority, rolls on the server, records the opposed result, creates a damage disposition when required, and advances the turn. Damage consumes the exact retained disposition, applies HP/vitality and order repair atomically, and retains an immutable replay result. Pass advances the current turn. End remains a trusted host-only internal lifecycle operation.

Each successful changed internal transition performs one canonical replacement and increments the Game revision once. A hit therefore intentionally spans a resolved-exchange revision and a later damage-consumption revision.

### 4.3 Locks, commit, gate, and notifier

`GameCoordinator` serializes each canonical mutation with a per-room `SemaphoreSlim` and expected-object `TryReplace`. Its Combat wrappers publish viewer-specific snapshots only after successful changed replacement. `RoomMutationDeliveryGate` separately serializes public room/game mutation delivery and hub attach/disconnect lifecycle per room.

Phase 2H must not take the coordinator's private room lock around calls to existing internal transitions: doing so would create a nested-lock deadlock. The HTTP handler instead holds one outer `RoomMutationDeliveryGate` lease for the entire player intent while the application coordinator calls the existing internal transitions sequentially. The application coordinator does not publish separately; the internal changed transitions already publish after their commits.

The outer gate prevents other public mutations and hub lifecycle changes from interleaving with the request-level sequence. It does not aggregate or suppress the existing intermediate canonical broadcasts.

### 4.4 Session identity and reconnect

The bearer session token resolves through `IPlayerSessionStore` to server-owned `PlayerId`, `RoomId`, and host status. `PlayerId` supplied in a request body is neither needed nor accepted.

SignalR `AttachSession` uses the same token, validates current room membership, registers the connection, and sends the latest viewer-specific `GameSnapshot`. Attach and reconnect do not mutate Game state or Game revision. Disconnect changes room presence only; it does not resolve a pending exchange, choose a response, pass a turn, advance turn/round, or consume Combat Damage.

### 4.5 Projection and privacy

Current `GameProjection(viewer)` behavior is the security boundary:

- a room member who owns no Combat participant receives `Combat = null`;
- a Combat participant receives safe labels/order/current actor/status/last exchange/last damage;
- only the viewer's own participant exposes snapshotted DEX/Fighting/Dodge;
- other players' and opponents' raw stats remain null; and
- pending damage registry, dying schedule, raw rolls, target authority, response policy/allowance, full history, weapon/profile, source, and provenance remain internal.

The current `CombatPendingSnapshot` exposes only a safe role and status. It does **not** expose an `ExchangeId` or allowed response choices, so the client cannot yet submit a safe defender response. The current projection also lacks authoritative eligible targets and pass/attack affordances.

### 4.6 Client and Single Player audit

The Multiplayer client currently renders Combat read-only. It has no Combat request contract, API call, handler, or action button. Its snapshot acceptance rule rejects a lower revision for the current room and accepts an equal or higher revision. REST and realtime both feed the same authoritative `GameSnapshot` state.

Single Player provides useful UX vocabulary—Attack, Dodge, Fight Back, Pass, Start, and End—but its browser owns dice and rule progression. Its attack currently targets the first active opponent rather than offering a general target picker. Phase 2H may borrow only the visible player-intent concepts. It must not migrate Single Player's browser-side authority or implicitly copy its first-target behavior.

### 4.7 Audit correction: NPC response policy is not canonical today

The current `OpponentDefinition.ResponsePolicy` is checked only for non-blank text during Start validation. It is not copied into `CombatParticipantState` and is not used by Begin or Resolve. Therefore the previously suggested assumption that an NPC's response policy is already snapshotted and executable is false.

Before NPC auto-response can be exposed, Phase 2H implementation must add a narrow, strongly typed internal `NpcResponsePolicy` to the canonical opponent participant snapshot (or an equivalently canonical opponent policy map scoped to the Combat session). Start must validate that the policy is a defined `CombatResponse` and is present in that opponent's snapshotted `AvailableResponses`. It must never be projected to clients.

Free-form policy strings are not acceptable authority. The supported initial policies are exactly `Dodge` and `FightBack`.

### 4.8 Audit correction: internal host-as-NPC control is not public authority

Current internal Begin and Pass authorization permits the room host to control an unowned NPC participant. That behavior exists for trusted integration/testing and must not be inherited by public player-intent endpoints.

Every Phase 2H public actor must resolve to an investigator participant whose `OwnerPlayerId` equals the authenticated session's `PlayerId`. A host receives no public NPC superuser privilege. NPC attack/pass automation remains deferred to a separate driver design.

## 5. Authority Model

The authoritative chain is:

```text
bearer session token
→ server PlayerId and RoomId
→ canonical Game and active Combat
→ stable participant identity
→ canonical ownership/current-turn/pending checks
→ internal transition(s)
→ commit(s)
→ viewer-specific GameProjection
→ SignalR and final HTTP GameSnapshot
```

The request expresses intent only. The server derives or validates every consequential fact.

| Fact | Authority |
| --- | --- |
| Player identity and room | session token / session store |
| Actor ownership | canonical participant `OwnerPlayerId` |
| Current actor | canonical Combat order and turn index |
| Eligible target | canonical active enemy participant set |
| Exchange identity | server-generated pending exchange |
| Allowed response | canonical pending `AvailableResponses` |
| NPC response | canonical internal snapshotted NPC policy |
| Dice and opposed result | server rules engines and `IDiceRoller` |
| Damage identity and result | exact retained disposition registry |
| Turn/round and dying schedule | canonical Combat transition |
| Visible actions | viewer-specific server projection |

## 6. Transport Alternatives

### Option A — Three explicit intent routes (recommended)

- clear authorization and validation per action;
- narrow request DTOs without discriminated-union parsing;
- straightforward status/error and future test matrices; and
- no implication that Combat is a client-managed resource.

### Option B — One discriminated-union `/combat/intents` route

This reduces route count but makes parsing, error reporting, authorization, and versioning more coupled. It also makes it easier to accidentally accept fields irrelevant to an intent.

### Option C — REST-shaped mutable Combat resources

Creating/updating exchanges or turns as client-owned resources misrepresents the domain. Exchange identity, results, damage, and turn progression are server outcomes, not client CRUD.

**Decision:** Use three explicit command routes.

## 7. Target Public Protocol

All routes require `Authorization: Bearer {PlayerSessionToken}` and a route `roomId` equal to the session's room.

### 7.1 Melee attack

```http
POST /api/rooms/{roomId}/game/combat/melee-attack
```

```json
{
  "expectedGameRevision": 12,
  "actorCharacterId": "00000000-0000-0000-0000-000000000001",
  "targetParticipantId": "opponent:0"
}
```

The server resolves `actorCharacterId` to the unique active investigator participant owned by the session player, verifies it is the current actor, and validates `targetParticipantId` against the server-projected eligible enemy set. The client does not send attacker stats, target stats, response, rolls, bonuses, weapon, damage, or next-turn data.

### 7.2 Respond to pending exchange

```http
POST /api/rooms/{roomId}/game/combat/respond
```

```json
{
  "expectedGameRevision": 13,
  "exchangeId": "server-generated-exchange-id",
  "response": "dodge"
}
```

The exchange must be the exact current pending exchange. Its snapshotted `DefenderOwnerPlayerId` must equal the authenticated player, and `response` must be in the pending exchange's snapshotted `AvailableResponses`. The initial wire values are exactly `dodge` and `fight_back`; unknown values are rejected, not defaulted.

### 7.3 Pass current turn

```http
POST /api/rooms/{roomId}/game/combat/pass
```

```json
{
  "expectedGameRevision": 12,
  "actorCharacterId": "00000000-0000-0000-0000-000000000001"
}
```

The actor must be the authenticated player's own active investigator and the canonical current actor. Pass is rejected while an exchange or damage disposition blocks progression. NPC pass is never authorized through this route.

### 7.4 Success response

Every successful route returns the latest viewer-specific `GameSnapshot` after the full request-level orchestration has completed. The implementation must re-read/project the latest canonical state rather than return an earlier transition's state.

No raw internal transition result is serialized.

## 8. Application Orchestration Alternatives

### Option A — Put orchestration in `GameApi`

This is expedient but couples authentication/HTTP mapping to multi-transition domain sequencing and makes failure recovery difficult to test without transport.

### Option B — Dedicated player-intent coordinator (recommended)

Introduce an application seam such as `IPlayerCombatIntentCoordinator`. It validates the public intent against canonical state and invokes the existing internal Combat interfaces. It owns request-level orchestration but no Combat rules, dice logic, state storage, projection, or publishing.

`GameApi` remains responsible for route mapping, bearer authentication, one outer `RoomMutationDeliveryGate` scope, DTO binding, error mapping, and returning the final projection.

### Option C — Add public intent methods directly to `GameCoordinator`

This mixes player/session policy with trusted internal transitions and risks making internal host/NPC authority publicly reachable.

**Decision:** Use a dedicated application coordinator, called once inside the existing public delivery gate.

### 8.1 Canonical-state access seam

`IPlayerCombatIntentCoordinator` is an application orchestration boundary, not a second canonical state reader. It must not inject or directly depend on `IGameStateStore`.

Before the first mutation, it obtains the authenticated viewer's authoritative `GameSnapshot` through:

```csharp
IGameCoordinator.GetProjectionAsync(roomId, authenticatedPlayerId)
```

The future `CombatViewerActionsSnapshot` is the safe prevalidation source for `ActorCharacterId`, `CanMeleeAttack`, `CanPass`, `EligibleTargetParticipantIds`, `PendingResponse.ExchangeId`, `PendingResponse.AvailableResponses`, and the current Game revision. These affordances may reject an obviously invalid public request, but they are not final mutation authority. The invoked internal Combat transition must still revalidate canonical revision, current actor, ownership, target, exact pending exchange, response membership, and the causal damage gate before any mutation or dice.

After Begin, the application coordinator must use `BeginOpposedExchangeResult.State` rather than re-reading storage. That returned canonical committed state supplies the exact `PendingExchange`, exact defender participant, human-or-NPC ownership, private snapshotted `NpcResponsePolicy`, and latest revision. NPC defender orchestration is therefore:

```text
BeginOpposedExchangeResult.State
→ exact PendingExchange
→ exact defender
→ private canonical NpcResponsePolicy
→ ResolvePendingExchange
```

After Resolve, the application coordinator must use `ResolvePendingExchangeResult.State`. It inspects only `DamageDispositions[ExchangeId]` for the exact resolved exchange. Only when that exact disposition is `Pending` does it call `ResolveCombatDamageAsync` with the exact `ExchangeId` and `ResolvePendingExchangeResult.State.Revision`. It must not infer damage from outcome text or `LastExchange`, use the original HTTP expected revision, or expose damage resolution publicly.

After Pass or the complete Begin/Resolve/Damage sequence, the HTTP/application boundary obtains a fresh viewer-specific projection through `IGameCoordinator.GetProjectionAsync(roomId, authenticatedPlayerId)` and returns that `GameSnapshot`. It never serializes `MultiplayerGameState`, `CombatSession`, `PendingCombatExchange`, `DamageDispositionState`, or an internal transition result.

The preferred application dependencies remain narrow: `IGameCoordinator` for viewer projection and `IInternalCombatResolutionCoordinator` (or the approved narrow internal Combat seam) for transitions, plus only mechanically required application dependencies. It must not directly depend on `IGameStateStore`, `IDiceRoller`, `ICombatDamageEngine`, `IHpDamageEngine`, `IGameRealtimeNotifier`, a SignalR hub context, persistence, or AI.

If implementation discovers that a required canonical fact is unavailable from the viewer projection before the first mutation or from the internal transition result `State` after a mutation, stop and identify that missing fact. Prefer a narrow internal query/result contract if truly necessary; never add a general `IGameStateStore` dependency to the player-intent application layer as a shortcut.

## 9. Identity and Target Validation

Validation order must avoid information leaks and side effects:

1. authenticate the bearer token;
2. require session `RoomId == route roomId`;
3. confirm room membership and active Game;
4. reject malformed IDs, response names, or revisions;
5. compare `ExpectedGameRevision` before any transition, dice, or server ID generation;
6. require active Combat and no causal damage blocker where applicable;
7. resolve the actor/defender from canonical stable identities;
8. enforce authenticated ownership and current-turn/pending authority;
9. validate target side, activity, and exact pending/response availability; and
10. invoke canonical transitions.

`ActorCharacterId` is used for player intent because it is already a stable player-owned public identity. Internally it resolves to the Combat participant ID. `TargetParticipantId` is used for an opponent because opponents have no CharacterId. It is valid only within the current active Combat and must not be accepted from stale projection state.

Current Combat models distinguish investigator and opponent sides. Phase 2H rejects same-side targets and does not define PvP semantics.

## 10. Revision, Retry, and Idempotency

### Compared options

1. **Expected revision only:** cheap, consistent with existing Game commands, prevents duplicate first transition after a successful commit.
2. **Client IntentId only:** can deduplicate ambiguous network retries but requires a durable per-intent registry and retained response semantics.
3. **Both:** strongest retry ergonomics but adds storage/lifecycle complexity not justified for the initial in-memory slice.

### Decision

Every Phase 2H mutation requires `ExpectedGameRevision`. Do not add a public `IntentId` registry in the initial slice.

Rules:

- validate the expected revision before the first canonical transition and before any dice;
- a stale request returns a conflict and the current safe revision, then the client resynchronizes;
- the client never silently rewrites the expected revision and retries an action;
- if the connection fails after a commit, retrying the same body is stale and cannot duplicate the initial transition;
- if a multi-transition intent partially commits, the committed intermediate state remains authoritative and is recovered by snapshot/reconnect;
- never re-run the whole intent automatically after a partial commit; and
- `CombatDamageCommitInvariantException` after RNG is non-retryable and must never be translated into an ordinary stale/retry response.

Expected revision alone does not provide replay of the original HTTP success payload. This is an accepted initial limitation. A future distributed deployment or offline queue may justify adding a durable IntentId registry.

## 11. NPC Defender Policy

### Compared options

1. **Snapshot policy at Combat Start and auto-resolve (recommended):** deterministic for the Combat lifetime and independent of later configuration changes.
2. **Wait for a host/driver response:** exposes trusted NPC control and can stall on disconnect.
3. **Read mutable configuration at response time:** makes replay and audit dependent on external mutable state.

### Decision

At Start, persist one internal typed response policy per opponent participant. Validate it against that participant's snapshotted response list. When a player attacks an NPC:

1. Begin creates and commits the pending exchange.
2. The application coordinator reads the committed pending exchange and canonical opponent policy.
3. It calls trusted Resolve with `RequestingPlayerId = null` and that policy.
4. No client or host chooses the NPC response.

The policy remains private. A viewer may see that no human response is required, but never the NPC's policy before resolution.

NPC attacker turns remain deferred. The public application coordinator must reject any request whose actor participant is unowned, even from the room host.

## 12. Player Defender Response

A player response is accepted only when:

- the bearer session owns the exact pending defender;
- the supplied `ExchangeId` equals the current pending exchange;
- the supplied response is a defined wire value and is in the pending snapshot's internal allowed list; and
- `ExpectedGameRevision` equals the current Game revision.

The response resolves the canonical exchange, then conditionally consumes Combat Damage as described below. Attackers, other Combat participants, room nonparticipants, disconnected former members, and hosts who do not own the defender cannot answer it.

The current product has no public NPC-attacker driver and no PvP targeting, so player-defender pending exchanges are reachable only through trusted internal/future flows at first. Keeping the route and authorization contract now is still useful because it defines the safe human-response boundary without granting new actor authority.

## 13. Combat Damage Orchestration

### Compared options

1. **Automatic synchronous server follow-up (recommended):** preserves current causal gate and provides a complete authoritative result without a public damage action.
2. **Background worker:** introduces a durable job/idempotency boundary not present in the in-memory architecture and increases visible pending time.
3. **Collapse opposed resolution and damage into one aggregate transition:** erases the approved Phase 2F/2G transition and revision boundaries and rewrites tested internals.

### Decision

After any successful Resolve, the application coordinator inspects the newly committed canonical Combat state. If the exact exchange now has a Pending damage disposition, it invokes internal `ResolveCombatDamageAsync` with:

- the exact server-generated `ExchangeId`; and
- the latest committed Game revision, not the request's original revision.

It does not infer damage eligibility from outcome text, client data, or `LastExchange`. No-hit resolutions do not call damage. Consumed replay stays internal and read-only.

The existing canonical boundaries remain separate:

| Intent path | Canonical commits |
| --- | --- |
| Pass | Pass: +1 |
| Attack human defender | Begin: +1, then waits |
| Attack NPC, no damage | Begin: +1; Resolve: +1 |
| Attack NPC, damage | Begin: +1; Resolve: +1; Damage: +1 |
| Human response, no damage | Resolve: +1 |
| Human response, damage | Resolve: +1; Damage: +1 |

The client must not assume that one intent means one revision increment.

## 14. Transition Sequencing and Failure Boundaries

### Melee attack

```text
authenticate + outer room delivery gate
→ validate request revision/owned current investigator/eligible target
→ BeginOpposedExchange
→ committed pending snapshot is published
→ if defender is human: return latest viewer snapshot
→ if defender is NPC: select canonical private policy
→ ResolvePendingExchange
→ committed opposed snapshot is published
→ if exact damage disposition Pending: ResolveCombatDamage
→ committed damage/repaired snapshot is published
→ return latest viewer snapshot
```

### Respond

```text
authenticate + outer room delivery gate
→ validate exact pending exchange/defender ownership/response/revision
→ ResolvePendingExchange
→ committed opposed snapshot is published
→ conditionally ResolveCombatDamage using latest revision
→ committed damage/repaired snapshot is published
→ return latest viewer snapshot
```

### Pass

```text
authenticate + outer room delivery gate
→ validate owned current investigator/revision/no blocker
→ PassCombatTurn
→ committed snapshot is published
→ return latest viewer snapshot
```

There is no rollback across already committed transitions. If a later transition fails before RNG, the server returns a structured conflict/internal failure and leaves the committed intermediate state available for reconnect and diagnosis. If replacement fails after any damage/CON/Dying RNG, the non-retryable invariant exception remains an operator-recovery event.

## 15. Realtime Delivery Decision

### Compared options

1. **Publish every canonical committed revision (recommended).** This matches current notifier behavior and preserves causal observability.
2. **Suppress intermediate revisions and publish only an aggregate final snapshot.** This would require a new transaction/broadcast buffer, obscure committed intermediate truth, and complicate failure recovery.

### Decision

Preserve one viewer-specific `GameSnapshot` publication after every successful changed internal transition. Do not add a Combat-specific SignalR event and do not publish from the application coordinator.

Clients must tolerate consecutive snapshots from one intent and revision gaps caused by transport timing. Existing monotonic acceptance remains the rule: reject current-room snapshots with lower revisions; accept equal or higher revisions. A final HTTP response that arrives after a newer realtime snapshot is therefore harmless.

## 16. Viewer-Specific Action Projection

### Compared options

1. **Server-projected affordances (recommended):** one authorization source and no client reconstruction of canonical rules.
2. **Client-derived buttons from participants/pending fields:** duplicates turn, ownership, blocker, response, and target logic and is prone to stale-state errors.

### Target safe shape

Add a nullable viewer-specific action object inside `CombatSnapshot`, conceptually:

```text
CombatViewerActionsSnapshot
  ActorCharacterId: Guid?
  CanMeleeAttack: bool
  CanPass: bool
  EligibleTargetParticipantIds: IReadOnlyList<string>
  PendingResponse: CombatPendingResponseSnapshot?

CombatPendingResponseSnapshot
  ExchangeId: string
  AvailableResponses: IReadOnlyList<string>
```

Projection rules:

- `Combat` remains null for room nonparticipants.
- `ActorCharacterId`, attack, pass, and eligible targets are populated only when the viewer owns the active current investigator and no pending exchange or damage blocker prevents progression.
- eligible targets contain only stable IDs of active opposing participants; no stats, policy, or target calculation inputs are added.
- `PendingResponse` appears only to the exact owning human defender for the exact current pending exchange.
- `AvailableResponses` contains only safe wire values actually snapshotted for that exchange.
- attackers and observing Combat participants may retain a generic waiting status but do not receive `ExchangeId` or response choices.
- NPC policy, response allowance, raw counters, damage disposition, dying schedule, rolls, and profiles remain absent.

The client renders buttons only from these fields. It may select among projected eligible target IDs and pending response values, but it must not create additional options or infer authorization.

## 17. Client State and UX Boundary

The Multiplayer client adds only:

- TypeScript request/response/action-projection contracts matching the server;
- three API methods that send bearer-authenticated minimal intents;
- Attack/Pass controls only when projected as available;
- a target selector sourced only from projected eligible target IDs and safe participant labels;
- Dodge/Fight Back controls only from the projected exact pending response; and
- busy/error/resync handling using the authoritative snapshot.

The client does not optimistically advance Combat. It waits for HTTP or SignalR snapshots. It does not predict NPC response, opposed outcome, damage, next actor, or round wrap. Stale conflicts trigger snapshot refresh/reattach and ask the player to decide again; they do not auto-replay the intent.

Start, End, and Damage controls remain absent.

## 18. Error Contract

### Compared options

1. **Status-only errors:** consistent with the current small API but too coarse for stale resync versus invalid turn/response UX.
2. **Narrow structured errors (recommended):** sufficient client recovery without serializing domain internals.

### Target shape

```json
{
  "code": "stale_game_revision",
  "currentGameRevision": 14
}
```

`currentGameRevision` is optional and is returned only after authenticated room membership has been established.

| HTTP | Public code examples | Meaning |
| --- | --- | --- |
| 400 | `invalid_intent`, `invalid_response` | malformed ID/value/body; response is not supported |
| 401 | `invalid_session` | missing, invalid, or expired bearer token |
| 403 | `not_member`, `actor_not_owned`, `defender_not_owned` | authenticated identity lacks the required authority |
| 404 | `room_not_found`, `game_not_found` | containing public resource does not exist |
| 409 | `stale_game_revision`, `combat_inactive`, `not_current_actor`, `target_not_eligible`, `exchange_not_pending`, `progression_blocked` | canonical state no longer accepts the intent |
| 500 | `combat_consistency_failure` | unexpected invariant failure; post-RNG cases are explicitly non-retryable |

Do not expose `GameErrorCode` names blindly, exception messages, raw rolls, policy, participant statistics, or whether a guessed private target/exchange existed outside the authenticated viewer's safe state.

## 19. Reconnect Semantics

Reconnect is read-only with respect to Game state:

- `AttachSession` reuses the same server session identity;
- it sends the latest viewer-specific `GameSnapshot`;
- the Game revision, round, turn, current actor, pending exchange, counters, damage registry, and dying schedule are unchanged by attach;
- a human defender regains their projected exact pending response affordance after reconnect;
- other participants regain only their own safe projection;
- room nonparticipants still receive `Combat = null`; and
- disconnect never selects Dodge/Fight Back, resolves, passes, advances, or consumes damage.

If an HTTP response is lost after a successful intent, reconnect supplies the latest authoritative revision. The player must not resubmit automatically.

## 20. Security and Privacy Requirements

### 20.1 Authorization matrix

| Intent | Required actor | Explicitly forbidden |
| --- | --- | --- |
| Melee attack | authenticated owner of current active investigator | other player, nonparticipant, host-as-NPC, inactive/not-current actor |
| Respond | authenticated owner of exact pending human defender | attacker, observer, other owner, host override, NPC response from public client |
| Pass | authenticated owner of current active investigator | other player, host-as-NPC, any request during pending exchange/damage blocker |

### 20.2 Never accept from the client

- `PlayerId`, `OwnerPlayerId`, host privilege, or defender owner;
- attacker/defender stats, response allowance, or NPC response policy;
- round, turn index, next actor, action/response counters;
- dice, success level, outnumbered bonus, outcome, winner, or damage mode;
- STR, SIZ, DB, weapon expression, armor, HP/vitality, Dying schedule; or
- revision result, exchange history, source, provenance, timestamps, or server IDs other than the projected exact exchange/participant identities needed for intent.

### 20.3 Never project

Keep the existing privacy matrix and additionally ensure action projection does not reveal:

- an NPC policy before resolution;
- another player's response choices or exact pending ExchangeId;
- private or raw opponent/player statistics;
- raw rolls, targets used internally, counters, allowance, damage registry/result internals, history, source, or provenance.

## 21. Future Test Matrix

Phase 2H implementation must start with failing tests and cover at least:

### 21.1 Route and authentication

- exactly three new Combat routes; public Start/End/Damage count remains zero;
- missing/invalid token, token-room mismatch, closed room, missing Game;
- request body cannot spoof PlayerId or authority; and
- narrow structured errors contain no internal exception/data leakage.

### 21.2 Melee attack

- own current investigator can attack a projected eligible opponent;
- other owner's, inactive, non-current, unowned NPC, and same-side actors are rejected;
- invalid/inactive/same-side/stale target is rejected before dice;
- stale revision rejects before server exchange ID, dice, mutation, or publish;
- human defender leaves one pending exchange and one Begin revision;
- NPC defender uses only canonical snapshotted typed policy;
- policy must be in snapshotted available responses;
- current free-form/non-persisted policy defect is eliminated; and
- host receives no public NPC control.

### 21.3 Respond

- only exact human defender owner can respond;
- wrong/stale exchange and unavailable response reject before dice;
- case/wire enum parsing is exact and unknown values do not default;
- successful response resolves once and advances according to canonical rules; and
- attacker/observer/host override cannot respond.

### 21.4 Pass

- only own current active investigator can pass;
- NPC pass and host-as-NPC pass are rejected;
- pending exchange and Pending damage disposition block pass;
- stale revision and rejected pass do not change counters/turn/round/revision; and
- successful wrap preserves existing atomic Dying scheduling behavior.

### 21.5 Damage orchestration

- hit invokes damage for the exact disposition using the post-Resolve revision;
- no-hit invokes no damage;
- Begin/Resolve/Damage remain separate commits and revisions;
- final HTTP snapshot is the latest post-damage projection;
- consumed damage is not exposed as a public action;
- partial pre-RNG failure does not retry the whole intent;
- consumed replay produces no duplicate mutation/broadcast; and
- post-RNG replacement failure remains non-retryable with no re-roll.

### 21.6 Realtime and reconnect

- each changed canonical transition publishes only after commit;
- multi-transition intents publish ordered viewer-specific revisions;
- no duplicate application-level publish is added;
- nonparticipant `Combat = null` across all intermediate/final snapshots;
- pending human response survives disconnect/reconnect with same revision/state;
- disconnect performs no auto-response/pass/advance/damage;
- reconnect is revision-neutral and restores current affordances; and
- stale lower client snapshots remain rejected.

### 21.7 Projection/privacy/client

- exact DTO property sets for viewer actions and pending response;
- action projection differs correctly by actor, defender, observer, and nonparticipant;
- other players never receive exact response affordance;
- internal policy/allowance/rolls/stats/registry/schedule/history/source/provenance absent from JSON;
- client sends only minimal intent fields and bearer token;
- controls appear only from projected affordances;
- client performs no dice, initiative, target eligibility, opposed, damage, next actor, or round calculations; and
- Start/End/Damage/NPC control buttons remain absent.

## 22. Product Limitations After Phase 2H

Even after this slice, Multiplayer Combat remains deliberately narrow:

- Combat creation and termination still require trusted internal integration;
- an NPC current actor has no player-facing driver and can stall progression;
- PvP and same-side attacks are unsupported;
- only melee opposed intent is public;
- NPC policy is a fixed Combat-start snapshot, not AI;
- no timeouts or disconnect forfeits exist;
- in-memory locks and state are not a distributed exactly-once guarantee; and
- no Scenario, persistence, firearms, Impaling, maneuver, movement, or inventory UX is included.

These limitations must remain visible in Current State/Handoff only after implementation is separately approved and completed; this pending design does not change current implementation status.

## 23. Deferred Work

- public Combat lifecycle orchestration (Start/End);
- NPC-attacker/GM driver and turn automation;
- PvP rules and consent/visibility policy;
- timeout/disconnect policy;
- durable IntentId registry and distributed state/roll reservation;
- background orchestration if persistence later justifies it;
- firearms, Impaling, maneuvers, movement, range, ammunition, and richer targets;
- AI/Scenario decisions; and
- persistence, reconnect across server restart, and horizontal scaling.

## 24. Risks and Mitigations

| Risk | Mitigation |
| --- | --- |
| Public path accidentally inherits host NPC authority | application coordinator requires owned investigator for every public actor/pass; explicit tests |
| NPC policy appears implemented but is currently discarded | add typed canonical Start snapshot before enabling auto-response; exact persistence/use tests |
| Client derives stale eligibility | project viewer actions and exact target/response IDs; server revalidates all input |
| Multi-transition request is assumed atomic | preserve explicit revisions/broadcasts and document partial-commit recovery |
| Duplicate retry after lost response | required expected revision, resync on 409, no automatic replay |
| Damage is rolled twice after failure | exact disposition lifecycle and non-retryable post-RNG invariant remain authoritative |
| Nested locks deadlock | outer delivery gate only; never acquire coordinator room lock externally |
| Duplicate realtime broadcasts | internal changed transition is the only publisher; application layer never publishes |
| Response/target data leaks to observers | viewer-specific projection and exact JSON property-set tests |
| Action surface expands into lifecycle/damage | route/button enumeration tests keep Start/End/Damage at zero |

## 25. Review Questions

No unresolved product-level question blocks this design if the following boundaries are accepted as explicit deferrals:

- PvP/same-side attacks are not introduced by Phase 2H.
- NPC attacker/pass automation is not introduced by Phase 2H.
- Expected revision without a durable IntentId is the accepted initial retry contract.

If any of those boundaries must change, it requires a separate product decision before implementation planning because it changes authority, reachable flows, and test scope.

## 26. Recommendation

Approve Phase 2H around three explicit authenticated intent routes and a dedicated player-intent application coordinator. Keep canonical rules in the existing internal transitions, preserve their separate revisions and commit-before-viewer-specific broadcasts, and return the latest projection after orchestration.

Before exposing NPC auto-response, correct the audited model gap by snapshotting a typed private NPC response policy at Combat Start. Public actors must always be investigator participants owned by the bearer session; never expose the current trusted host-as-NPC shortcut. Project exact viewer actions rather than reconstructing them on the client, require `ExpectedGameRevision` on every mutation, auto-consume exact Combat Damage dispositions on the server, and make reconnect purely authoritative recovery.

Do not create an implementation plan or begin Phase 2H code until this design is reviewed and approved.
