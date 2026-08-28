# Multiplayer Phase 2F Combat Opposed Design

**Status:** Approved design. Implementation plan reviewed; feature implementation pending authorization.

**Date:** 2026-08-25

**Baseline:** `main` / `4e38d326c7f8b65e06c4e5add02df360ba76431a`

## Goal

Define the Multiplayer canonical foundation for Combat Opposed while preserving the verified Single Player behavior as a conformance reference.

Phase 2F establishes a server-authoritative `CombatSession` aggregate with stable gameplay identities, turn/round authority, response ownership, pending opposed exchanges, exact exchange identity, deterministic server dice, per-character dying timing, safe projection, and reconnect recovery.

The chosen product boundary is **internal-only**. Phase 2F does not expose player Combat Intent, a public combat endpoint, a public defender-response flow, pending-exchange UI, or disconnect/timeout UX.

## Scope

Phase 2F covers:

- `CombatSession` as a canonical Multiplayer aggregate;
- stable participant identity, with investigator identity based on `CharacterId`;
- explicit participant selection rather than automatic inclusion of every room character;
- snapshotting the investigator's canonical combat values at Combat start;
- server-controlled start/end, current actor, turn, round, and round wrap;
- `BeginOpposedExchange` and `ResolvePendingExchange` as two internal canonical stages;
- stable `ExchangeId` across pending exchange, resolved exchange, and future damage consumption;
- server-authoritative dice through `IDiceRoller`;
- Single Player-conformant Dodge, Fight Back, success-level, and outnumbered semantics;
- per-character dying observation and round-check scheduling;
- deferred `DamageDisposition` without HP or weapon damage resolution;
- viewer-specific combat projection, commit-before-broadcast, and AttachSession recovery;
- per-room serialization, expected-revision validation, stale-intent rejection, and duplicate-exchange fail-closed behavior;
- reference conformance fixtures and separate Multiplayer generalization tests.

## Non-goals

Phase 2F does not implement:

- public Attack, Dodge, Fight Back, Pass, Start Combat, Resolve Exchange, or End Combat APIs;
- Player Combat Intent Protocol;
- real-player defender response transport, response UI, timeout, disconnect forfeit, or automatic response policy for player-owned defenders;
- Combat Damage, weapon rolls, damage bonus, Armor, HP mutation, Major Wound, defeat repair, or damage result;
- Firearms, Impaling, maneuvers, reloads, range bands, or automatic fire;
- full NPC sheets, NPC HP, NPC defeat, AI action selection, AI personality, or AI gameplay;
- Scenario encounter termination, persistence, database, Redis, matchmaking, accounts, or billing;
- Team Status Visibility or PlayerKnowledge runtime;
- a full product workflow allowing one player to own multiple characters. The identity model remains future-safe, but the existing roster initializer is not broadened by this phase;
- changes to `src/`, `build/`, `outputs/`, the formal Single Player HTML artifact, or the existing Single Player regression behavior.

## Existing Baseline

The current repository is a dedicated repository with `main` as the writable branch and `origin/main` synchronized at the baseline SHA above. The current Multiplayer runtime already provides:

- immutable-style `MultiplayerGameState` replacement;
- `CharacterState` with `CharacterId`, `OwnerPlayerId`, `Name`, canonical `CheckValues`, and structured Phase 2E health state;
- `GameStateStore` replacement guarded by a per-room `SemaphoreSlim` in `GameCoordinator`;
- monotonic `Revision` on successful canonical mutations;
- `GameProjection.Build(state, viewerPlayerId)` for viewer-specific snapshots;
- commit-before-broadcast through the existing realtime notifier;
- AttachSession/latest-snapshot recovery for reconnect;
- owner-only detailed health projection, with non-owner health details omitted;
- no public Phase 2E stabilization action route.

The current `CharacterState.CheckValues` is a provisional but canonical rule-value container. Phase 2F uses it as the source of required investigator combat keys instead of adding a second editable copy of the same values. Combat start must reject missing or invalid required values rather than silently inventing defaults.

The current repository has completed Phase 2E Health Stabilization. The current formal validation baseline includes the previously recorded Server 161/161, Client 21/21, Single Player 37/37 regression scripts, 69 JavaScript syntax checks, deterministic stabilization fixture export, and unchanged formal HTML artifact. Phase 2F design does not change those artifacts.

## Single Player Reference

The behavior reference is the actual execution of:

- `src/combat-opposed.js`;
- `src/check-engine.js`;
- `src/coc-resolution-engine.js`;
- `src/health-stabilization.js` and `src/hp-damage-state.js` only where Combat Opposed invokes dying timing;
- `build/test-v168-combat-opposed.js`.

`src/combat-damage.js` is read only to define the future `DamageDisposition` boundary. `src/firearms-impaling.js` is read only to confirm that Firearms/Impaling is a later dependency and is not part of Phase 2F.

The Single Player module currently identifies the investigator as `"player"`, stores combat under `state.campaign.combat`, uses browser authority, directly owns the combat UI, and invokes health-stabilization timing from the combat round wrapper. Those are Single Player implementation details, not Multiplayer domain identities or transport contracts.

The Single Player reference currently uses a maximum history of 120 and a participant limit of 24 total participants. Any adopted Multiplayer limit must preserve the observable reference limit where conformance requires it, while its validation error must be expressed through the Multiplayer contract rather than a browser message.

## Portable Reference Semantics

Only behavior that can be isolated from Single Player global state, UI, AI request state, browser persistence, and browser authority is portable into the Phase 2F pure engine.

### Initiative

At Combat start, participants are ordered by descending DEX. Equal DEX preserves stable input order. The order is snapshotted for the session; it is not re-read from live CharacterState during a turn.

Investigator DEX is read from the canonical `CheckValues` key selected by the design contract. The reference uses the normalized value range 1..100. Opponent DEX is supplied only through the trusted server-internal opponent definition and is normalized to the same range.

### Dodge

The attacker and defender each make a regular CoC percentile resolution. A successful attacker hits only when the attacker's success level is strictly higher than the defender's. Otherwise, when either side succeeds, the defender dodges. If neither side succeeds, the result is `both_fail_no_damage`.

The comparison uses the existing `cocRank` and `cocDifficultyPass` ordering: fumble/failure 0, regular 1, hard 2, extreme 3, critical 4. Equal successful levels therefore favor Dodge for the defender.

### Fight Back

If the defender succeeds at a strictly higher success level than the attacker, the defender fights back. Otherwise, a successful attacker hits. Equal successful levels therefore favor the attacker. If neither side succeeds, the result is `both_fail_no_damage`.

A defender win uses the reference's regular-cap damage mode, even if the defender's success level was extreme or critical. An attacker win preserves the attacker's regular versus extreme/critical damage eligibility. Phase 2F records this semantic mode but does not resolve damage.

### Outnumbered

The server derives outnumbered bonus from the defender's response count for the current round. When `responseCountBefore >= responseAllowance`, the attacker receives exactly one bonus die. The client and caller cannot submit or override this value.

The response count is incremented only when a pending exchange resolves successfully. Beginning an exchange does not consume a response allowance.

### Turn / Round

Only the current active actor can begin an opposed exchange or perform an internal Pass. A completed exchange increments the actor's action count, increments the defender's response count, and advances turn order. Pass consumes the current actor's action and advances turn without creating a damage disposition.

When the active order wraps, the completed round is recorded, action and response counts reset for the next round, and per-character dying scheduling is evaluated. A pending exchange blocks Pass, another exchange, and round progression.

Disconnect, reconnect, chat, SignalR delivery, refresh, and AI narrative processing never advance turn or round.

### Damage Disposition

An opposed result with a winner may produce a pending `DamageDisposition` bound to the exact `ExchangeId`. A no-hit, Dodge, or both-fail result produces `DamageDisposition = null`.

The disposition contains only semantic ownership and deferred mode:

```text
{
  pending: true,
  exchangeId,
  ownerId,
  targetId,
  mode,
  authority: "deferred_weapon_damage_engine",
  hpCommitted: false
}
```

Phase 2F does not add weapon, Armor, HP, damage roll, Major Wound, defeat, or damage-result fields.

### Dying Integration

The reference observes a single player dying state at Combat start and around wrap, does not check it in the same round, and checks it at most once per later completed round. A successful check retains dying; failure kills the player and ends Single Player Combat.

Multiplayer ports the timing semantics, not the single-player storage shape. Observation and last-check scheduling are keyed by `CharacterId`; health check ordinal, target, check history, stabilization, and death condition remain owned by the canonical health state and Health Stabilization engine.

Multiplayer does not end every combat participant when one investigator dies. A dead participant becomes inactive and the session continues unless a future explicit encounter termination condition ends it.

## Single-player-specific assumptions

The following reference assumptions must not become Multiplayer contracts:

- the literal participant ID `"player"`;
- one global `state.character` as the only investigator;
- `state.campaign.combat` as a browser-global mutable object;
- browser-owned RNG and browser UI controls;
- `playerDyingObservedRound` and `lastAutoDyingCheckRound` as scalar fields;
- browser AI busy guards and browser request IDs as server identity;
- direct browser HP adjustment or Combat Damage wrappers;
- opponent defaults that are entered through a browser form;
- browser prompt and diagnostics fields as canonical domain data;
- automatic browser rendering after every mutation.

If a Single Player behavior cannot be generalized without changing the verified reference, it is recorded as a Potential Reference Issue or Multiplayer generalization test. It is not silently rewritten in Single Player and it is not copied as an unsafe Multiplayer schema.

## Multiplayer Canonical Model

### Combat Identity

The canonical investigator identity is `CharacterId`. `OwnerPlayerId` is a separate controller/authorization relation. `PlayerId`, connection ID, session ID, token, nickname, and display label are never combat identity.

The participant identity model is future-safe for multiple characters per player:

```text
CombatParticipantId
├─ CharacterParticipant(CharacterId)       // investigator
└─ OpponentParticipant(server-generated id) // scoped opponent
```

The current roster initializer may still reject duplicate ownership because that is an existing roster boundary. Phase 2F must not use that current limitation to collapse participant identity back to PlayerId.

### Investigator Participant

An investigator participant references a `CharacterId` and the authoritative `OwnerPlayerId` relation. At Combat start the server validates and snapshots the required canonical values from `CharacterState.CheckValues`, including the selected DEX, `fighting_brawl`, and `dodge` keys. Values must be present and within the reference range.

The participant snapshot may contain the normalized DEX, Fighting, Dodge, side, response allowance, and active state required by opposed resolution. It must not become a second editable character sheet. Mid-combat live lookup is prohibited; a future explicit character/combat transition would be required to refresh a snapshot.

### Opponent Participant

Opponents are created only by a trusted server-internal Combat start flow. The definition is a narrow opposed participant, not an arbitrary public NPC payload. It may contain normalized DEX, Fighting, Dodge, response policy, response allowance, stable label, and active state.

It does not contain HP, Armor, weapon, damage bonus, SAN, personality, AI state, scenario data, or a full NPC sheet. An opponent has no Phase 2F Health Stabilization boundary, so dying scheduling applies only to investigator participants backed by `CharacterId`.

### Stat Sources

Investigator DEX, Fighting, and Dodge come from canonical `CharacterState.CheckValues` keys at Combat start. No duplicate `Dex`, `Fighting`, or `Dodge` values are added to `CharacterState` merely for Combat. Missing or invalid keys cause Combat start to fail closed.

Opponent opposed stats come from the trusted internal opponent definition and are normalized into the session snapshot. The session never live-looks-up an opponent sheet because there is no opponent sheet in this phase.

### Combat Session State

`CombatSession` is a canonical aggregate mounted on `MultiplayerGameState`. Its conceptual shape is:

```text
CombatSession
├─ CombatId
├─ Status (active / ended)
├─ Round
├─ CurrentActorParticipantId
├─ Order[]
├─ Participants[]
├─ ActionCounts[ParticipantId]
├─ ResponseCounts[ParticipantId]
├─ PendingExchange?
├─ CompletedHistory[]          // bounded to 120
├─ PendingDamageDispositions   // ExchangeId -> DamageDisposition; independent of history
├─ DyingSchedule[CharacterId]
├─ StartedAt / EndedAt
└─ EndReason
```

`CombatParticipantState` and `CombatExchange` are separate domain records. `PendingCombatExchange` is separate from completed history. `CombatSession` is not a network DTO and is never directly serialized.

`PendingCombatExchange` must include the exact `ExchangeId`, round/turn identity, attacker and defender participant IDs, defender response authority metadata, non-empty `AvailableResponses`, `ResponseCountBefore`, and the creating game revision. `ResolvePendingExchange` must reject any selected response that is not a member of that pending record's `AvailableResponses`; checking only the global set `{dodge, fight_back}` is insufficient.

The session stores only the canonical data required to reproduce or validate future operations. Labels and safe display summaries are projection concerns; internal exchange records may retain semantic labels for logs and tests but do not become public data automatically.

### Exchange Identity

`ExchangeId` is generated when `BeginOpposedExchange` commits. The same ID is retained in the pending record, the completed `CombatExchange`, and its future `DamageDisposition`.

An exchange ID is not a client-chosen fact. It is not reused, and a duplicate or stale ID fails closed without a second roll, revision, history item, or damage disposition.

### Damage Disposition

`DamageDisposition` belongs to a specific completed `CombatExchange`. It is null for results without damage eligibility. When present, it remains pending with `hpCommitted = false` until a future Combat Damage phase consumes it exactly once under the same room serialization boundary.

The canonical `CombatSession` also owns an independent pending-disposition registry with the exact shape `PendingDamageDispositions: ExchangeId -> DamageDisposition` (implemented as an equivalent strongly typed keyed collection if required by the language). `ResolvePendingExchange` appends the completed exchange to bounded `History` and, when its disposition is non-null, registers that same disposition under its exact `ExchangeId`. History trimming must never remove an unconsumed registry entry. `LastExchange` is only a recent-history convenience and is never the authority for damage consumption. Phase 2F does not consume or remove registry entries; a future Combat Damage transition consumes the exact registered `ExchangeId` exactly once and then removes or marks that entry consumed under the same room serialization boundary. The registry is internal canonical state and is not exposed by projection unless a future approved safe DTO explicitly requires a derived view.

Phase 2F does not mutate `CharacterHealthState` as a result of an opposed hit. The only health transitions in this phase are the already-approved per-character dying timing linkage to the Health Stabilization engine at canonical round wrap.

### History Bounds

Completed exchange history is bounded to the reference limit of 120 records. Trimming removes the oldest completed records only; it never removes the current `PendingCombatExchange` or changes the exact identity of a pending or recently resolved disposition.

The pending-disposition registry is not bounded by the resolved-history limit. After more than 120 damage-eligible exchanges, an oldest completed exchange may be trimmed from `History` while its unconsumed disposition remains addressable by `ExchangeId`. A duplicate or stale Resolve cannot create a second registry entry for the same `ExchangeId`.

The design does not introduce persistent event sourcing. The existing canonical state and snapshot recovery model remain authoritative.

## Authority Model

### Start Combat

Combat start is a trusted server-internal canonical transition. It validates room/game existence, authorized character ownership, explicit participant selection, no duplicate participant identities, required investigator stat keys, active/non-dead investigator state, at least one opponent, and the adopted participant limit.

No public Host endpoint accepts arbitrary opponent definitions in Phase 2F. A future human-GM mode may define a separate authority contract; it is not implied by this design.

### End Combat

End Combat is an internal canonical command. A trusted explicit End Combat is allowed to cancel a pending exchange as part of the same canonical transaction. It clears `PendingCombatExchange`, records the end reason `combat_ended_before_resolution`, marks the session ended, and commits through the normal room lock and revision path. The cancelled pending exchange is not appended as a resolved `CombatExchange` and does not roll, increment response/action counts, advance turn, or create a `DamageDisposition`.

The canonical invariant is: an ended CombatSession never has a non-null `PendingCombatExchange`. A disconnect, refresh, chat message, or single participant death does not implicitly end the session.

### Current Actor

The server derives the current actor from the canonical order and turn index. A mutation is valid only when the requested attacker is the current active actor and no pending exchange exists. A client cannot force a different actor or select a turn index.

### Player Intent

Phase 2F has no public Player Combat Intent API. Internal tests and trusted server code may exercise typed canonical commands, but those commands are not HTTP or client contracts.

The future public protocol must submit intent, not a completed world fact. It must validate `CharacterId` ownership, expected revision, current actor, target eligibility, and pending exchange identity before any rule engine call.

### NPC Actions

NPC response choice is a trusted server-side canonical policy limited to the supported response modes. It is not selected by AI at runtime and does not include AI narrative or personality. NPC actions occur only when an explicit trusted server command invokes the policy; no autonomous turn loop is introduced.

### Defender Response

When the defender is player-owned, only that CharacterId's owner may provide the future Dodge/Fight Back response intent. The attacker cannot select the defender's response. Phase 2F does not expose the response transport, so a player-owned pending exchange remains pending until a future approved internal/public response flow exists.

When the defender is an opponent, a trusted server-side response policy may resolve the pending exchange. The policy is not a player-submitted field and is not an AI decision. The policy must select only from the pending exchange's `AvailableResponses`; an empty `AvailableResponses` set makes the pending flow invalid and fails closed.

### Dice

Production resolution uses the existing server `IDiceRoller` abstraction. Client-provided rolls, random sequences, targets, difficulty levels, outnumbered flags, and damage amounts are not accepted as canonical facts.

Forced rolls exist only in pure-engine tests and conformance fixture seams. The pure engine itself remains deterministic for explicit inputs and has no wall-clock or random dependency.

### Turn Advancement

Only a completed internal canonical action advances the turn. `BeginOpposedExchange` commits a pending exchange and increments revision, but does not roll, consume an action, increment a response count, or advance turn.

`ResolvePendingExchange` rolls, resolves, records the exchange, increments response/action counts, and advances turn. Pass is an internal action with no exchange. Pending exchange blocks Pass, another exchange, and round wrap.

## Dying Round Integration

### Per-character observation

`CombatSession.DyingSchedule` is keyed by investigator `CharacterId` and contains only scheduling metadata such as `observedRound` and `lastCheckCompletedRound`. It does not duplicate `DyingEpisode.Checks`, CON, ordinal, target, stabilization, or death records.

At Combat start, each active, unstabilized, non-dead dying investigator is observed at the current round. When a trusted future damage transition enters dying during an active Combat, the transition records a new observation at the current round. Neither event checks dying immediately.

### First eligible round

At a round wrap, the completed round is `finishedRound`. A character is eligible only when `finishedRound > observedRound` and `lastCheckCompletedRound != finishedRound`. This preserves the reference behavior: a character observed in round 1 is not checked at the end of round 1, and is first checked at the end of round 2.

### Multiple dying characters

All eligible investigator characters are evaluated in deterministic combat order during the same room-locked round-wrap transition. The transition invokes the existing Health Stabilization engine with a server-provided percentile roll and a deterministic internal source context. The replacement `MultiplayerGameState` commits once for the complete round-wrap transition, then emits one latest projection.

If one character succeeds, it remains dying and becomes eligible in a later completed round. Each character has an independent check schedule and health episode.

### Death handling

A dying check failure transitions that investigator's canonical health state to dead and marks its participant inactive. The active order must no longer treat that participant as a future actor. This does not automatically end the whole CombatSession. A future encounter policy may end Combat explicitly when its own condition is met.

The design does not invent NPC dying or NPC Health. Only participants backed by Multiplayer `CharacterState` and `CharacterId` participate in this integration.

### Stabilized handling

If a trusted Health Stabilization transition clears active dying and creates a stabilized condition, that character has no eligible dying check. The combat schedule is cleared or marked inactive without inventing a Combat-specific stabilized record.

If fresh trusted damage later creates a new dying transition, stale stabilization is cleared by the health domain and a new current-round observation is established. A duplicate damage event does not create a second observation.

## Projection Policy

The canonical aggregate, viewer projection, and realtime delivery remain separate.

| Viewer | Allowed Phase 2F projection | Forbidden data |
|---|---|---|
| Owner of a combat investigator | Safe Combat active/status, round, current actor, own `CharacterId` participant, own snapshotted stats, safe participant summaries, and safe pending role/status | Other investigator stats, opponent DEX/Fighting/Dodge, response policy/allowance, raw rolls, targets, internal history, source IDs, provenance, dying schedule |
| Another combat participant | Their own private participant details plus safe session/participant summaries | Other player stats, opponent raw stats, response policy/allowance, raw rolls, targets, internal history, source IDs, provenance, dying schedule |
| Room nonparticipant | No CombatSession detail by default; existing room/game projection only | Round, turn, pending exchange, participant raw data, exchange history, NPC internals |

No Phase 2F network DTO directly contains `CombatSession`, `CombatParticipantState`, `CombatExchange`, `PendingCombatExchange`, or `DamageDisposition`. Any future safe pending indicator must expose only viewer-safe role/status and must not enable a client to resolve the exchange.

Raw rolls, success targets, response policy, response allowance, internal source IDs, health check records, and full combat history are internal/test data. No full omniscience is granted merely because a player is in the same room or same CombatSession.

## Realtime / Reconnect

Successful canonical operations follow:

```text
validate under room lock
    -> pure rule / transition engine
    -> canonical state replacement
    -> GameRevision increment
    -> viewer-specific GameProjection
    -> existing GameSnapshot / SignalR delivery
```

Commit occurs before broadcast. Delivery failure does not roll back the committed combat state. Phase 2F does not introduce a Combat-specific event stream.

AttachSession remains the authoritative recovery path. A disconnected participant reconnects to the latest viewer-safe snapshot. A pending exchange remains pending across disconnect, refresh, and SignalR failure; no automatic Dodge, Fight Back, forfeit, Pass, timeout, or turn advancement occurs.

## Concurrency / Stale Intent

All Combat mutations run inside the existing per-room mutation lock and replace the latest `MultiplayerGameState` through the existing revision-aware state store.

Every canonical mutation carries an expected game revision at the internal boundary. A mismatch fails closed before dice or state mutation. `BeginOpposedExchange` additionally requires the current actor and no existing pending exchange. `ResolvePendingExchange` additionally requires:

- the exact current pending `ExchangeId`;
- the expected current revision;
- the expected defender identity;
- owner authorization for a player defender, or a trusted server policy for an opponent defender.

An old, missing, already-resolved, or duplicate `ExchangeId` is a failed request. It does not re-roll, increment revision, append history, or create another disposition. A stale caller must read the latest safe projection and create a new valid internal request; no client can force an overwrite.

The same invariant applies to termination: an End Combat transaction either clears the pending exchange and ends the session with `combat_ended_before_resolution`, or is rejected before mutation according to a future explicitly approved policy. Phase 2F selects the trusted-cancellation policy above; it never permits `Status = ended` with a non-null pending exchange.

## Proposed Phase 2F Implementation Boundary

The design deliberately keeps the future implementation to two feature commits after this documentation-only design commit.

### Commit 1 — Pure Combat Opposed migration

Create the server-side pure opposed rule layer and conformance fixture without modifying Vue or public HTTP routes. It includes the portable participant normalization, order, success-level comparison, outnumbered, turn/round semantics, exchange result, and deferred disposition shape.

The fixture exporter must execute the actual Single Player reference in the established Node VM pattern. It must not contain a second implementation of the rules or derive expected values by copying the new C# code.

### Commit 2 — Canonical Multiplayer integration

Add `CombatSession` to canonical Multiplayer state, internal Begin/Resolve/Pass/End flows, revision and room-lock handling, safe projection, commit-before-broadcast, reconnect recovery, per-character dying scheduling, and the corresponding server/realtime/generalization tests.

No public Combat route or player action API is part of this commit. A client contract may be extended only for read-only projected status if required by the existing snapshot shape; no action handler, local dice, local turn logic, or battle button is allowed.

The implementation plan must derive its file map from the current repository at implementation time and must not mechanically copy the Phase 2E file map.

## Conformance Fixture Strategy

Reference fixtures must be generated from actual `src/combat-opposed.js` behavior and stable explicit inputs. They should cover at least:

### Pure reference conformance

- module version/authority and portable schema metadata;
- opponent normalization and invalid range rejection;
- stable DEX order and input-order tie break;
- duplicate participant and invalid start rejection;
- Dodge: equal regular, attacker higher, defender success, both fail, fumble/failure boundaries;
- Fight Back: equal regular attacker win, defender strictly higher, attacker failure/defender success, both fail;
- extreme/critical attacker disposition mode and Fight Back regular cap;
- response allowance and outnumbered bonus;
- action/pass turn consumption, round wrap, and counter reset;
- invalid same-side, self-target, inactive, and invalid-response rejection;
- no direct HP mutation and disposition-only hit result;
- active-combat health-stabilization suppression and reference round observation timing.

### Portable versus non-portable labeling

Each fixture case must identify whether it is:

- `reference_conformance`: directly derived from observable Single Player rule behavior;
- `multiplayer_generalization`: a new server contract required by CharacterId identity, pending exchanges, ownership, revision, projection, or multiple characters;
- `deferred`: intentionally outside Phase 2F.

The reference fixture does not claim that the Single Player scalar player dying fields or browser UI are Multiplayer-compatible.

Fixture tests should compare semantic error kinds and structured outcomes, not localized browser error strings. Production RNG and fixture forced rolls remain separate.

## Multiplayer Generalization Tests

The separate Multiplayer test layer must cover:

- CharacterId identity distinct from PlayerId, including future-safe multiple-character ownership relations;
- explicit participant subset and no automatic full-room inclusion;
- trusted opponent creation boundary and rejection of arbitrary public input;
- CheckValues canonical source, required combat keys, snapshot-at-start, and no live stat drift;
- Begin creates one pending exchange, increments revision once, and does not roll or advance turn;
- pending exchange blocks Pass and another Begin;
- trusted End Combat cancels pending with `combat_ended_before_resolution` and does not create a resolved exchange;
- an ended session never retains a pending exchange;
- `AvailableResponses` is non-empty at Begin and every selected response is validated against the pending set;
- attacker cannot choose a player-owned defender response;
- trusted NPC policy can resolve an opponent response without AI invocation;
- Resolve requires exact pending ExchangeId and expected revision;
- duplicate/stale Resolve is fail closed and exactly-once for dice/history/disposition;
- response/action counts and turn progression occur only after Resolve;
- round wrap and per-character dying observation/check timing;
- multiple dying investigators resolve independently;
- a damage disposition survives turn advancement;
- a damage disposition remains addressable after `LastExchange` is replaced;
- after more than 120 resolved damage-eligible exchanges, the oldest exchange may leave bounded history while its pending disposition remains addressable by `ExchangeId`;
- duplicate Resolve does not create a duplicate pending-disposition registry entry;
- one investigator death does not end the complete CombatSession;
- stabilized state suppresses dying scheduling and fresh dying creates a new observation;
- `CombatSession` is not directly serialized;
- owner-only private participant details and hidden opponent raw stats;
- no combat details for room nonparticipants;
- commit-before-broadcast, cross-room isolation, and AttachSession recovery;
- disconnect/reconnect does not auto-resolve pending exchange or advance turn;
- concurrent stale requests cannot lose updates.

## UI Boundary

Phase 2F does not add Attack, Dodge, Fight Back, Pass, Start Combat, End Combat, timeout, or pending-response controls. It does not add local combat dice, local turn state, or a client-side Combat rule engine.

If the existing client must understand a new snapshot field for recovery, it may render only a read-only server-projected status. The client must not infer combat state from HP, choose a defender response, generate an ExchangeId, submit a roll, or treat a narrative message as a canonical combat transition.

Pending exchange UI, player intent transport, defender response UX, reconnect messaging, and timeout policy are future design stages.

## Future Combat Damage Contract

Combat Damage may consume only a completed exchange's pending disposition under the same room lock and canonical state revision discipline. The future consumer must verify:

- exact `ExchangeId` exists;
- the disposition is pending and not already committed;
- owner and target participants still exist;
- target is still eligible under the future damage rules;
- the consumer has not already committed HP for this exchange.

The consumer then performs weapon/damage/Armor/HP resolution through the future approved Combat Damage and HP Damage State engines, records the result, marks the disposition committed, and prevents duplicate damage. Phase 2F neither defines nor implements those fields or transitions.

## Deferred

The following remain explicit follow-up design scopes:

- Player Combat Intent Protocol;
- public action routes and authorization contracts;
- real-player defender response flow;
- pending exchange UI, reconnect messaging, timeout, and disconnect policy;
- Combat Damage and HP integration for opposed hits;
- Firearms, Impaling, weapon schema, Armor, maneuvers, and range;
- NPC HP/defeat and encounter termination conditions;
- AI gameplay or AI selection of NPC actions;
- Scenario lifecycle and encounter ownership;
- Team Status Visibility, communication, and PlayerKnowledge;
- persistence, DB, Redis, matchmaking, accounts, and billing;
- broad roster/product support for one Player controlling multiple Characters.

## Risks

1. **Single-player identity leakage.** Copying the literal `player` participant or `state.character` into Multiplayer would break multiple-character safety. The domain must use CharacterId and owner authorization separately.
2. **Duplicate stat sources.** Adding mutable combat values beside `CheckValues` would allow drift. Combat start must snapshot canonical keys and reject missing values.
3. **Pending exchange deadlock by policy.** A player-owned pending defender cannot progress in Phase 2F because public response is intentionally deferred. This is an approved boundary, not a timeout bug.
4. **Projection leakage.** Directly serializing CombatSession would expose opponent stats, rolls, policy, history, or health provenance. Projection must remain viewer-specific.
5. **Dying timing drift.** A scalar Multiplayer field or global combat death rule would mishandle multiple characters. Scheduling must be keyed by CharacterId and health records must remain in HealthState.
6. **Exactly-once damage drift.** A future consumer that uses only `LastExchange` or labels instead of ExchangeId could double-apply HP. The disposition contract requires exact exchange identity.
7. **Reference mismatch.** Single Player Combat wrappers and browser side effects are not pure semantics. Conformance and generalization tests must be separately labeled.
8. **Roster mismatch.** Current roster initialization may still restrict duplicate PlayerId ownership. Phase 2F must preserve the identity model without silently expanding roster behavior.

## Open Questions

These are intentionally deferred and do not block the approved internal-only foundation:

- What public Player Intent payload and authorization protocol should create a pending exchange?
- Should a future defender response be synchronous over one request or a first-class realtime response command? The canonical model already chooses pending exchange, but transport is deferred.
- What future timeout policy, if any, is acceptable for a player-owned pending exchange?
- Which encounter-specific condition, if any, ends Combat when all investigators or all scoped opponents are inactive?
- Which safe combat summaries should be visible to nonparticipants under the future Team Status Visibility policy?
- Should future Combat Damage consume the disposition in a separate canonical command or as part of a fully atomic resolved exchange transaction?
- When a future damage transition creates dying during Combat, which canonical damage-to-combat hook owns the new `observedRound` write?

None of these questions authorizes a public API, AI gameplay, or Combat Damage implementation in Phase 2F.

## Recommendation

Proceed with the approved internal-only foundation using:

- `CombatSession` as the canonical aggregate;
- CharacterId-based investigator identity and separate OwnerPlayerId authorization;
- trusted, minimal opponent participants;
- snapshot-at-start canonical stats;
- `BeginOpposedExchange → ResolvePendingExchange` with revisioned `ExchangeId`;
- server-authoritative IDiceRoller and portable Single Player success semantics;
- per-character dying scheduling without copying Single Player scalar fields;
- deferred, exchange-scoped DamageDisposition;
- safe viewer-specific projection and snapshot-based reconnect recovery;
- no public Combat Intent, response, timeout, or UI action surface.

The next authorized artifact should be a separate implementation plan derived from this design and the current repository. It must be reviewed before any Phase 2F production code is written.
