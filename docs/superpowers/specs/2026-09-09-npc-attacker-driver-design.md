# Multiplayer Phase 2I NPC Attacker Driver Design

## Status

Approved after continuation/executor boundary review.

The implementation plan is documented separately. Implementation has not started.

**Date:** 2026-09-09

**Audited baseline:** `f041a59b373240fdf2fb8a01ab1dbe1e6232dbed`

**Scope:** Multiplayer server-side NPC attacker orchestration design only

## Goal

Define the smallest server-internal application boundary that advances an already-active Combat session when its canonical current actor is an NPC. The selected design is deterministic: the NPC attacks the first legal active opposing investigator in stable canonical Combat order, or uses the existing canonical Pass transition when no legal melee target exists.

The driver selects only a narrow action intent and target. Existing Phase 2F/2G transitions remain the sole authority for exchange identity, dice, opposed results, outnumbered, defender response, damage, HP/vitality, defeat, turn/round advancement, dying checks, and Combat termination.

The governing principles remain:

- AI narrates; program adjudicates.
- Intent is not world truth.
- Multiplayer Server is authoritative.
- NPC action selection is program-owned and server-internal.
- Existing Combat engines own canonical adjudication.

## Current Combat Gap

Phase 2H can advance player-owned investigator turns through three public intents. It can also resolve an NPC defender using the opponent's private snapshotted `NpcResponsePolicy`. It does not drive an NPC when an unowned opponent becomes the current actor.

The current stalled path is:

```text
canonical current actor is opponent
→ no trusted NPC attacker application driver
→ no player-owned public intent is legal
→ Combat waits indefinitely
```

The server has internal Begin and Pass transitions, but both commands require a non-null `RequestingPlayerId`. `GameCoordinator` verifies room membership and authorizes an unowned actor only when that requester is the room Host. This is an internal Host-control shortcut, not a valid NPC runtime identity. Phase 2I must not call it with a forged Host `PlayerId`.

## Scope

Phase 2I design covers:

- deterministic NPC Attack-versus-Pass selection for an already-active Combat session;
- stable legal investigator target selection from private canonical state;
- a dedicated application-layer continuation coordinator;
- narrow internal NPC Begin and Pass execution seams without player impersonation;
- synchronous bounded continuation after successful player Combat transitions;
- stop conditions at human input, pending state, Combat end, invariant failure, and a deterministic loop bound;
- preservation of transition-returned canonical state between application steps;
- independent revisions and realtime publication for every canonical NPC action;
- partial-commit, retry, concurrency, disconnect/reconnect, privacy, and error boundaries;
- future implementation security and regression matrices; and
- a narrow future AI proposal seam that remains unimplemented.

## Non-goals

Phase 2I does not design or authorize:

- production code, tests, fixtures, client changes, or Single Player changes in this review task;
- public NPC attack, pass, turn, Start, End, Damage, or ResolveDamage routes;
- Host or arbitrary-player control of NPC actions, targets, or responses;
- Vue NPC controls or a client-triggered continuation request;
- AI target selection, tactics, response selection, dice, result, or canonical command execution;
- an `NpcAttackerPolicy` snapshot, enemy archetypes, boss logic, aggro, threat, taunt, positioning, scripted phases, ranged behavior, or spellcasting;
- PvP or same-side targeting;
- timeout, forfeit, disconnected-player substitution, or automatic defender response;
- Firearms, Impaling, movement, range, ammunition, inventory, weapon selection, or loadout editing;
- spawning opponents, Scenario encounters, narrative advancement, rewards, next-scene logic, or forced Combat termination;
- persistence, crash recovery, database, Redis, queues, hosted workers, event sourcing, or multi-process exactly-once behavior; or
- implementation work.

## Existing Phase 2F/2G/2H Foundation

### Phase 2F: Combat Opposed

The canonical `CombatSession` contains a stable `Order`, `TurnIndex`, participant snapshots, action/response counts, an optional `PendingExchange`, bounded completed history, pending damage dispositions, and per-investigator dying schedules. Current actor identity is `Order[TurnIndex]`. Investigator participants use `CharacterId`, have `Kind = "investigator"`, `Side = "investigator"`, and a non-null `OwnerPlayerId`. Opponents use server-generated `CombatParticipantId` values such as `opponent:0`, have `Kind = "opponent"`, `Side = "opponent"`, null `CharacterId`, and null `OwnerPlayerId`.

Start orders participants by descending DEX with stable input order for ties. Begin creates a server-generated `ExchangeId` and pending exchange without rolling. Resolve validates the exact pending exchange and response authority, rolls through server dice, records the result, creates an exact damage disposition when needed, and advances through the canonical turn logic.

### Phase 2G: Combat Damage

The separate internal Damage transition consumes the exact retained disposition. It applies investigator damage through the HP engine or opponent damage through combat-scoped vitality, repairs inactive positions without deleting stable `Order`, performs canonical round wrap only when required, and terminates Combat only when an entire side is defeated. The NPC driver must not reproduce any of this behavior.

### Phase 2H: Player Combat Intent

The public Combat surface is exactly three authenticated routes:

```text
melee-attack
respond
pass
```

`PlayerCombatIntentCoordinator` prevalidates from viewer-safe projection, then calls canonical Begin/Resolve/Damage/Pass transitions. Human defenders stop after Begin. NPC defenders use only their private typed `NpcResponsePolicy`. Each changed transition commits and publishes independently. The final HTTP result is a fresh viewer-specific `GameSnapshot`.

Phase 2H deliberately rejects unowned NPC actors even for the Host. Its current application coordinator has only `IGameCoordinator` and `IInternalCombatResolutionCoordinator` dependencies. Phase 2I intentionally adds exactly one narrow delegation dependency, `ICombatContinuationCoordinator`, while leaving all NPC behavior and canonical adjudication outside `PlayerCombatIntentCoordinator`.

### Audited transition results

The current internal results already expose canonical committed state to the application layer:

| Result | Current payload |
| --- | --- |
| `BeginOpposedExchangeResult` | `MultiplayerGameState State` |
| `ResolvePendingExchangeResult` | `MultiplayerGameState State` |
| `PassCombatTurnResult` | `MultiplayerGameState State` |
| `ResolveCombatDamageResult` | `MultiplayerGameState State`, `CombatDamageResult Damage` |

This is sufficient to continue without reading `IGameStateStore`. The remaining gap is that Phase 2H currently ignores `PassCombatTurnResult.State`, and its damage helper returns only an optional application error, so some successful paths lose the newest internal state before final projection.

## Single Player Audit

The actual browser runtime has no autonomous NPC attacker policy or background continuation loop.

- `combatStart` creates one investigator plus browser-authored opponents and establishes descending-DEX stable order.
- On the investigator turn, the UI attacks the first active opposition participant and applies that opponent's preset Dodge/Fight Back preference.
- On an opponent turn, the browser user chooses the investigator's Dodge/Fight Back response and clicks a button that resolves the opponent attack.
- A common Pass button lets the browser user skip whichever participant is current.
- `combatResolveMelee` and `combatPassTurn` advance the browser-owned canonical turn; Combat Damage is consumed by browser wrappers.

Portable behavioral references are limited to stable Combat order, active opposing-side target filtering, and the human player's ownership of their defender response choice. The browser user's manual opponent button, browser RNG, global mutable state, automatic render wrappers, single literal `player` target, and browser authority are not Multiplayer authority and must not be ported.

## Authority Model

```text
committed private canonical State
→ dedicated Combat continuation coordinator
→ deterministic NPC action selector
→ narrow internal NPC Begin or Pass command
→ existing canonical transition validates and commits
→ transition publishes viewer-specific GameSnapshot
→ inspect returned State or stop at human boundary
```

The NPC driver owns only action selection. `GameCoordinator` and existing rule engines remain the canonical mutation/adjudication authority. The HTTP layer owns authentication, public error mapping, and the one existing outer delivery gate. The client only renders resulting snapshots.

## NPC Driver Responsibility

When the current actor is a canonical NPC opponent, the driver chooses exactly one of:

```text
MeleeAttack(legal target participant ID)
Pass
```

It may inspect private committed Combat state only to identify the current NPC, determine whether a legal investigator target exists, and choose the first such target in stable order. It does not roll, calculate, resolve, damage, mutate, publish, end Combat, select a weapon, update `PlayerKnowledgeState`, or generate narrative.

## Driver Type

### Options compared

| Driver | Benefit | Cost/risk | Decision |
| --- | --- | --- | --- |
| Deterministic internal program driver | reproducible, testable, no external dependency, no additional RNG or prompt surface | intentionally simple tactics | **Selected** |
| Random server driver | produces variation without AI | adds random authority, replay evidence, and post-RNG partial-failure concerns before gameplay value requires them | Rejected for Phase 2I |
| AI driver | may eventually express narrative tactics | nondeterministic, availability-dependent, privacy-sensitive, and vulnerable to untrusted prompt/context influence | Deferred |

The deterministic option is the only one aligned with Phase 2I's narrow goal of closing the missing loop while leaving all adjudication in existing program rules.

## NPC Actor Identity

The actor is the canonical `CombatParticipantId` from `Order[TurnIndex]`. A legal NPC attacker must be active, have `Kind = "opponent"`, `Side = "opponent"`, `CharacterId = null`, and `OwnerPlayerId = null`.

No `PlayerId` represents that actor. The driver must not discover or supply the room Host as a surrogate identity. The internal execution command must carry the exact observed NPC participant ID and expected revision, with no public/trusted boolean and no caller-selectable `RequestingPlayerId`.

## Legal Target Selection

A legal Phase 2I target must be derived from the newest private canonical Combat state and must satisfy all of:

- the participant ID occurs in the stable canonical `CombatSession.Order`;
- the participant record exists and is `Active`;
- its side opposes the NPC actor's side;
- `Kind == "investigator"`;
- `CharacterId` and `OwnerPlayerId` are non-null; and
- it remains eligible under the canonical Begin transition's revalidation.

The target is never supplied by Host, player, client, HTTP, display name, arbitrary `CharacterId`, or AI narration. Same-side participants, inactive/defeated participants, malformed hidden participants, and non-investigator kinds are excluded. Phase 2I introduces no PvP.

Connection state is deliberately absent from this legality rule. Disconnect is transport presence, not Leave or gameplay inactivation.

Selection is allowed only after the continuation coordinator validates the canonical structure. The order is fixed:

```text
committed State
→ ValidateContinuationState
→ derive legal action set
→ select deterministic action
```

The selected target must exist exactly once, occur in canonical `Order` where required by the canonical Begin rule, be active and on the opposing side, have `Kind = "investigator"`, and have non-null `CharacterId` and `OwnerPlayerId`. It must also have valid defender response capability and satisfy all existing pending/revision requirements. The selector considers only structurally valid targets.

Missing or duplicate participants, an invalid active `TurnIndex`, malformed current actor identity, opponent identity carrying a character or owner, side/kind contradictions, structurally invalid required investigator data, an active session with no active investigator after side-defeat termination should have occurred, or inconsistent target relations are canonical corruption. They produce `NpcCombatContinuationInvariantException` (or the finalized dedicated equivalent) before mutation. They are never reclassified as an empty legal action set.

## Attack vs Pass Policy

After structural validation succeeds, the initial policy is uniform and deterministic:

```text
if at least one legal melee target exists:
    MeleeAttack(first legal target)
else:
    canonical Pass
```

Pass does not end Combat or invent a target. It reuses `PassCombatTurn`, preserving action counts, stable active scanning, round wrap, and dying scheduling. It is available only for a genuinely valid empty legal-action condition under canonical rules, deterministic future policy extension, and direct coverage of the trusted NPC Pass seam. It is not a repair mechanism for malformed state. Under the current two-side non-impaling melee rules, an active NPC with an active opposing investigator normally attacks and stops at human `PendingExchange`. The driver never calls `EndCombat`; existing damage/turn rules remain responsible for canonical termination.

No `NpcAttackerPolicy` is added to the Combat snapshot in Phase 2I. `NpcResponsePolicy` remains a distinct existing field used only when an NPC is the defender. Aggressive/passive or tactical attacker policies are deferred until a demonstrated product need justifies new canonical state.

## Deterministic Target Policy

### Options compared

| Option | Benefit | Cost/risk | Decision |
| --- | --- | --- | --- |
| First legal target in stable canonical order | deterministic, reproducible, no new state | may focus the same investigator repeatedly | **Selected** |
| Rotating deterministic target | spreads attacks | requires a new canonical cursor/history rule and retry semantics | Deferred |
| Random server target | varies play | adds RNG, replay/debug, partial-commit, and post-RNG failure complexity | Rejected for Phase 2I |
| AI/tactical target | potentially richer behavior | nondeterministic authority, availability, privacy, and prompt-injection surface | Deferred |

The selected order is the Combat session's snapshotted `Order`, not current connection order, room join order, dictionary enumeration, participant label, or client projection order.

## Internal Begin Support Audit

Current Begin cannot represent a trusted NPC attacker without a real player identity:

- `BeginOpposedExchangeCommand.RequestingPlayerId` is a non-null `Guid`;
- `BeginOpposedExchangeCore` calls `TryGetMember` for that player;
- an unowned attacker is authorized only when `room.HostPlayerId == RequestingPlayerId`; and
- null therefore cannot compile as the command value or pass membership checks.

Current Pass has the same limitation: `PassCombatTurnCommand.RequestingPlayerId` is a non-null `Guid`, and `CanControlActor` delegates unowned actors to the Host.

Phase 2I therefore needs a narrow server-internal application execution seam implemented by the same canonical `GameCoordinator` singleton that already owns Combat mutation, conceptually:

```text
IInternalNpcCombatTurnExecutor
├─ BeginNpcOpposedExchangeAsync(BeginNpcOpposedExchangeCommand)
└─ PassNpcCombatTurnAsync(PassNpcCombatTurnCommand)
```

The command shapes are fixed to canonical execution facts only:

```text
BeginNpcOpposedExchangeCommand
├─ RoomId
├─ ExpectedGameRevision
├─ NpcParticipantId
└─ TargetParticipantId

PassNpcCombatTurnCommand
├─ RoomId
├─ ExpectedGameRevision
└─ NpcParticipantId
```

The exact names are implementation details for later review, but the authority shape is fixed:

- no `RequestingPlayerId`;
- no Host impersonation;
- no caller-provided `trusted = true` switch;
- internal assembly/application registration only;
- exact current NPC participant and expected revision required;
- canonical revalidation of active/current/unowned opponent identity, pending gates, target legality, and revision before mutation; and
- reuse of the existing Begin/Pass core transition behavior after that distinct authority validation.

The NPC executor independently validates room/game existence, active Combat, exact revision, absence of pending damage and `PendingExchange`, a valid `TurnIndex`, `NpcParticipantId == Order[TurnIndex]`, unique participant identity, and an active current participant with `Kind = "opponent"`, `Side = "opponent"`, null `CharacterId`, and null `OwnerPlayerId`. Begin additionally validates the exact legal opposing investigator target and its response capability.

Because current Begin/Pass core methods contain player/Host authorization inline, future implementation must separate authority validation from shared private canonical validation/mutation helpers. The existing player/Host entry point performs its current authorization before calling the helper; the new NPC entry point performs exact unowned-current-opponent authorization before calling the same helper. The full mechanics must not be duplicated and player authorization must not be weakened. The design forbids a Host `PlayerId`, `Guid.Empty` sentinel, nullable trusted player, `trusted = true`, reflection, or any public caller-controlled authority flag.

Both NPC methods use the same existing wrapper shape as player Begin/Pass:

```text
WithRoomLockAsync
→ NPC authority validation
→ shared canonical Begin/Pass mutation core
→ if IsSuccess && Changed: PublishGameSnapshotAsync
→ return committed MultiplayerGameState
```

They acquire the same private per-room lock, use the same state replacement rules, increment exactly one revision for each changed transition, and publish exactly one viewer-specific `GameSnapshot` after commit. There is no second stateful Combat engine or repository.

The existing Host-capable commands are not broadened or exposed. Removing their historical internal Host behavior may be considered separately; Phase 2I does not depend on it.

## Driver Trigger Model

### Options compared

| Trigger | Analysis | Decision |
| --- | --- | --- |
| Synchronous application continuation after a successful canonical player transition | bounded, in-process, deterministic, uses returned state, preserves current commit/publication model | **Selected** |
| Scheduled/internal worker invocation | requires operation persistence, ownership, retry, crash recovery, and dedupe | Deferred |
| Background polling loop | can race, duplicate work, and outlive request/room lifecycle | Rejected |
| Client/public trigger | grants transport influence over NPC timing and can stall or duplicate | Rejected |

The selected flow is iterative, not recursive. It begins from the latest committed state returned by Pass, Resolve, or Damage while the original request still holds the outer room delivery gate.

```text
player transition returns State
→ ContinueAsync(State)
→ stop condition check
→ if NPC, execute one canonical action with State.Revision
→ receive newly committed State
→ repeat within bound
```

Synchronous continuation adds latency proportional to the bounded consecutive NPC segment but performs no AI or network call. Current participant count is capped by the canonical Combat start limit, so this remains appropriate for the in-memory single-process phase.

The application-only return value should describe orchestration, not duplicate the Combat state machine. A narrow outcome may distinguish `NoChange`, `WaitingForPlayerResponse`, `CombatEnded`, and `Advanced`, always paired internally with the latest committed state. Consistency failures use the dedicated exception/error boundary rather than a second canonical status enum.

## Consecutive NPC Turns

The coordinator drives continuously until it reaches a human-input boundary, pending state, Combat end, or failure. One external player intent may therefore cause several independently committed NPC revisions.

For an order such as:

```text
Player A → NPC 1 → NPC 2 → Player B
```

the coordinator may Pass NPC 1 when it has no legal target, inspect the returned state, then drive NPC 2. If NPC 2 attacks Player B, Begin commits a pending human exchange and continuation stops immediately. It does not require a second external trigger between consecutive NPCs.

Under the initial uniform two-side melee policy, a healthy active investigator is legal to every active opponent, so the first normal NPC turn attacks and stops at pending response. Multi-NPC continuation mainly protects no-target, repaired, malformed, and future policy/eligibility states. The loop contract is still required now so it never relies on a client to wake the next unowned actor.

Driving only one NPC per external trigger is rejected because it can leave Combat stalled with no authorized client operation and encourages creation of a public/internal "continue" button.

## Human Defender Boundary

An NPC attack against an investigator always follows:

```text
NPC driver
→ canonical BeginOpposedExchange
→ PendingExchange for the human defender
→ STOP
```

The driver must not select Dodge or Fight Back, reuse the NPC defender policy, auto-resolve, impose a timeout, or let the Host substitute. Only the exact player owner may use the existing Phase 2H `respond` intent with the exact projected pending exchange and allowed response.

Pending exchange checks take precedence over current actor inspection. Begin does not advance the turn, so continuing after it would otherwise risk a duplicate attack.

## Continuation After Player Respond

After a human response, Phase 2H Resolve may create damage, and Damage may defeat the NPC attacker or repair the next actor. Continuation must begin only from the latest successful canonical state:

```text
Respond
→ Resolve returned State
→ optional Damage returned State
→ ContinueAsync(latest State)
```

Fight Back needs no special NPC-driver rule. Existing opposed and damage semantics may damage/defeat the NPC; existing repair chooses the canonical next active actor. The continuation coordinator merely inspects that result.

The same call belongs after these successful Phase 2H paths:

- Player Pass: from `PassCombatTurnResult.State`;
- Player Melee Attack against an NPC defender: from Damage state when damage occurs, otherwise Resolve state;
- Human Respond: from Damage state when damage occurs, otherwise Resolve state.

Player Melee Attack against a human defender stops at Begin because `PendingExchange` is the human boundary. Start Combat has no current public/runtime caller to extend in Phase 2I and remains deferred; any future Start orchestration must explicitly enter the same outer-gated continuation boundary.

## Application Layer Ownership

### Options compared

| Owner | Problem/benefit | Decision |
| --- | --- | --- |
| `PlayerCombatIntentCoordinator` | already sees player transitions, but would mix human intent policy with NPC behavior | Caller only; does not own NPC rules |
| Dedicated `ICombatContinuationCoordinator` with a deterministic `NpcCombatTurnDriver` decision component | isolates stop/loop policy and NPC selection from transport and canonical rules | **Selected** |
| `GameCoordinator` | would mix action policy with canonical state transition/adjudication | Rejected |

`ICombatContinuationCoordinator` owns request-level continuation and the safety loop. `NpcCombatTurnDriver` is a small deterministic selector over the committed private Combat state; it returns only Attack(target) or Pass. The continuation coordinator invokes the narrow internal NPC executor.

`PlayerCombatIntentCoordinator` remains the human-intent coordinator. Its constructor is intentionally extended to exactly three dependencies:

```text
PlayerCombatIntentCoordinator
├─ IGameCoordinator
├─ IInternalCombatResolutionCoordinator
└─ ICombatContinuationCoordinator
```

The third dependency is delegation-only. The coordinator passes the latest committed state into continuation after successful paths and obtains a final viewer projection afterward. It owns no NPC action selection, target policy, loop policy, NPC canonical authorization, dice, damage, or turn advancement. `GameCoordinator` remains authoritative for mutation and mechanics, not policy selection.

## Dependency Boundary

The dedicated continuation boundary may depend only on:

- the deterministic NPC selector;
- the narrow internal NPC Begin/Pass executor; and
- transition-returned `MultiplayerGameState` values.

`PlayerCombatIntentCoordinator` must have exactly the three dependencies listed above. It must not directly depend on `IGameStateStore`, `IDiceRoller`, `ICheckResolutionEngine`, `ICombatDamageEngine`, `IHpDamageEngine`, `IGameRealtimeNotifier`, SignalR `HubContext`, `IPlayerConnectionRegistry`, AI/provider services, persistence, or a queue/worker.

It must not depend directly on:

- `IGameStateStore`;
- `IGameCoordinator` projection/query access;
- `IDiceRoller`;
- `ICheckResolutionEngine`;
- `ICombatDamageEngine` or `IHpDamageEngine`;
- `IGameRealtimeNotifier` or SignalR `HubContext`;
- `IPlayerConnectionRegistry`;
- AI/provider services;
- persistence, queue, timer, or hosted service; or
- `RoomMutationDeliveryGate` acquisition.

The HTTP path already holds the single outer delivery gate. Existing internal transitions acquire their own private per-room `GameCoordinator` lock, commit, and publish. The continuation coordinator must never acquire that private lock or nest the outer gate.

Publication ownership is exact:

- `PlayerCombatIntentCoordinator`: zero publications;
- `ICombatContinuationCoordinator`: zero publications;
- `NpcCombatTurnDriver`: zero publications; and
- the `GameCoordinator` canonical transition wrapper implementing `IInternalNpcCombatTurnExecutor`: exactly one publication after each successful changed NPC transition.

## Canonical State Access

The primary state-access strategy is transition-returned canonical `State` only.

### Options compared

| State source | Analysis | Decision |
| --- | --- | --- |
| Latest transition result `State` | exact committed private state and revision; no extra race/read | **Selected** |
| Narrow internal query | acceptable only for a future trigger that genuinely has no transition result | Deferred fallback |
| Direct `IGameStateStore` | creates a second reader, bypasses application boundaries, and encourages race-prone shortcuts | Rejected |

Viewer-safe `GameSnapshot` is not suitable for the driver because it intentionally omits private ownership/policy/stats and is scoped to a player. The driver must not infer hidden canonical facts from projection.

## Transition Result Seam

No canonical transition result needs a wider public payload: Begin, Resolve, Pass, and Damage already return `State`. The narrow future change is inside the application layer, where Phase 2H must stop discarding that state.

Primary recommendation: use an internal sealed application-only execution outcome/helper that always carries the latest successful `MultiplayerGameState` alongside any approved application error. Conceptually:

```text
CombatIntentExecutionOutcome
├─ LastCommittedState
└─ Error?
```

The exact record is never returned by HTTP, projected, serialized, or registered as a general query service. `ResolveExactPendingDamageAsync` should return the Resolve state when there is no pending damage and the Damage result state when damage commits. Pass retains `PassCombatTurnResult.State`; Melee/Respond update `lastCommittedState` after every success; human-defender Begin retains `BeginOpposedExchangeResult.State` but continuation immediately stops because `PendingExchange` exists. No path reads `IGameStateStore` to recover a discarded state.

The player execution shape is:

```text
public player intent
→ existing safe projection prevalidation
→ canonical player transition(s)
→ retain latest committed State
→ ICombatContinuationCoordinator.ContinueAsync(latest State)
→ fresh viewer-specific GameProjection
→ HTTP response
```

The alternative of adding `IInternalGameStateQuery` or reading the store is unnecessary for current Phase 2H paths. The final public success remains a fresh viewer-specific projection, obtained only after continuation completes.

## Revision Semantics

Every successful changed NPC canonical action owns one normal Game revision. Revisions are not collapsed into the originating player intent.

Examples:

```text
Player Pass       N → N+1
NPC Begin         N+1 → N+2
human response pending at N+2
```

```text
Player Pass       +1
NPC 1 Pass        +1
NPC 2 Pass        +1
NPC 3 Begin       +1
```

One player request can therefore advance by more than one revision. The client must continue treating revision as monotonic canonical state, not "one request equals +1."

## Realtime Semantics

Each existing changed transition publishes exactly one viewer-specific `GameSnapshot` after its commit. `PlayerCombatIntentCoordinator`, the continuation coordinator, and the selector publish zero times; only the canonical `GameCoordinator` transition wrapper publishes, exactly once per successful changed transition.

Intermediate revisions remain observable and ordered under the existing outer `RoomMutationDeliveryGate`:

```text
player commit publication
→ NPC Pass/Begin commit publication
→ any later NPC commit publication
→ final HTTP viewer projection
```

Phase 2I does not aggregate, suppress, buffer, or publish only the final state. It adds no NPC action event authority and exposes no raw command, policy, target reasoning, or private state. A final HTTP snapshot arriving after a newer realtime snapshot remains harmless under the client's monotonic acceptance rule.

## Partial Commit Semantics

Each transition is independently canonical. There is no transaction spanning the player action and all NPC continuation.

- If Player Pass commits and NPC Begin fails, the Pass remains committed and reconnectable.
- If NPC Pass commits and a later NPC action fails, that Pass remains committed and reconnectable.
- If NPC Begin commits, the pending human exchange remains canonical even if the HTTP response or later application work fails.
- If Resolve commits and Damage fails before commit, the resolved state and exact pending damage disposition remain canonical.
- If a post-RNG Damage commit invariant fails, the existing non-retryable operator-recovery boundary remains in force.

No earlier commit is rolled back. The continuation coordinator reports an approved consistency failure and stops; it does not guess recovery from pending damage.

## Retry / Idempotency

Phase 2I adds no `NpcTurnId`, `DriverOperationId`, or dedupe registry initially.

Idempotency is derived from:

- exact committed `State.Revision` passed to the next internal command;
- canonical current actor identity;
- pending exchange and pending damage gates;
- the outer room delivery gate for current public flows;
- per-room canonical transition serialization and compare/replace; and
- server-generated ExchangeId only after canonical validation.

A stale or duplicate continuation starting from an old state cannot validly repeat the same Begin/Pass at the old revision. Once Begin commits, `PendingExchange` blocks another action. Once Pass commits, the old expected revision is stale and the old actor is no longer current.

If an HTTP response is lost, retrying the original player request with its old revision remains a stale conflict. The client must resynchronize and must not replay automatically. The whole player-plus-NPC continuation is never retried.

## Concurrency

Current public Combat requests hold one `RoomMutationDeliveryGate` lease around the full player coordinator call. Phase 2I continuation remains inside that same lease. This serializes same-room public mutations and hub lifecycle delivery ordering without nesting the gate.

Each canonical Begin/Pass call still acquires and releases the existing private `GameCoordinator` per-room lock. The continuation layer never holds or accesses that lock itself. It receives a committed state, invokes one transition, waits for its publication/return, then inspects the returned state.

The safety invariant is: two flows must not both commit an NPC action for the same actor at the same revision. Even if a future internal caller violates the outer-gate contract, expected revision plus canonical compare/replace permits at most one mutation; the loser must fail closed as a consistency/stale condition and must not retry against a refreshed revision automatically.

## Safety Loop Bound

### Options compared

| Bound | Analysis | Decision |
| --- | --- | --- |
| Bound derived from canonical `CombatSession.Order.Count` | one full traversal is enough to reach a human boundary in a valid active session; adapts to roster size | **Selected** |
| Fixed arbitrary count | either too small for valid rosters or too large to detect defects quickly | Rejected |
| No bound | permits infinite Pass/wrap loops on malformed state | Rejected |

At continuation entry, set the maximum successful automatic NPC actions to the snapshotted canonical `Order.Count`. Count every committed NPC Begin or Pass. Begin must immediately stop at pending human response. If another automatic action would exceed the bound, fail closed with a dedicated consistency invariant.

In a valid active two-sided session, at least one active investigator boundary must be encountered within one traversal. Exceeding the bound means the state/turn/eligibility assumptions are inconsistent; it is not a reason to keep passing. The driver scans only current participants/order, never the entire game history, and makes no AI/network call.

## Disconnect / Reconnect

Connection status does not affect target legality. An active investigator whose owner is temporarily disconnected remains a legal target because Disconnect is not Leave or defeat.

If the NPC attacks that investigator:

```text
Begin commits
→ PendingExchange remains
→ no automatic response or timeout
→ owner reconnects with the same session identity
→ latest snapshot restores the exact existing response affordance
```

Disconnect/reconnect does not change Game revision, choose a response, pass, advance, consume damage, or trigger a fresh NPC action. Current reconnect tests already establish exact pending-state preservation and owner-only response restoration; Phase 2I must preserve that contract.

## Projection / Privacy

Phase 2I does not add public projection fields. In particular it must not expose:

- `NpcNextTarget`, `NpcPolicy`, `NpcIntent`, or decision reason;
- internal action candidates or rejected targets;
- `NpcResponsePolicy` or any future attacker policy;
- hidden stats, rolls, response counters/allowance, damage registry, history, source, or provenance;
- the raw internal NPC command or trusted execution seam; or
- connection state as Combat legality.

Players see only the ordinary resulting Combat snapshots and, for the exact human defender, the existing owner-only `PendingResponse`. SignalR retains the same viewer-specific privacy boundary.

Server operational logging may later record room ID, NPC participant ID, and selected action category. It must not expose credentials, hidden narrative, secrets, raw private state, or target-decision traces to clients.

## Error Boundary

Normal invalid/stale public player intents retain existing structured errors. An impossible NPC continuation state, failed safety bound, unexpected internal NPC transition rejection while correctly outer-gated, malformed participant/order relation, or unresolved pending damage at continuation entry is a server consistency boundary. Structural validation occurs before action selection, so malformed state causes this boundary with zero new mutation rather than an ordinary Pass.

If implementation needs a dedicated exception such as `NpcCombatContinuationInvariantException`, `GameApi` may catch that exact approved type alongside existing Combat invariant types and map it to:

```text
500 combat_consistency_failure
```

The response contains no revision, exception detail, stack trace, retry hint, participant data, or private reason. The exception is logged server-side with safe room/action identifiers. Do not add `catch (Exception)` and do not suppress a failure to manufacture progression.

Pending Damage is fail-closed: continuation performs no Begin or Pass and does not auto-consume or repair a historical residual disposition. Recovery of a partial-failure pending damage state requires a separately designed recovery slice.

## Client Impact

No Phase 2I client contract, API method, handler, button, target selector, timer, polling loop, or local rule is required.

The Vue client continues to receive ordinary `GameSnapshot` updates. When an NPC Begin creates a human pending response, the existing projected Dodge/Fight Back controls appear only for that owner. The client cannot trigger an NPC turn, choose its action/target, supply `continueNpc`/`autoNpc`/`npcMode`, or infer canonical progression.

The public Combat route count remains exactly three before and after future Phase 2I implementation.

## AI Future Seam

AI is not involved in Phase 2I.

A future, separately approved chooser may propose only from a server-created safe legal action set:

```text
safe NPC narrative identity + legal Attack(target IDs/labels) and Pass options
→ AI proposal
→ strict server validation against the current legal set and revision
→ deterministic application executor
→ existing canonical transition
```

The AI may never directly commit a command or return authoritative rolls, bonuses, defender response, outcome, winner, damage, Armor, HP, turn, round, or death. Its context must exclude hidden player knowledge, secrets, raw canonical internals, credentials, and private policy data. No prompt, provider call, fallback, or AI policy is designed in this phase.

## Security Matrix

| Principal | May do | Must not do |
| --- | --- | --- |
| Host | submit the same three owned-investigator intents as any owner | trigger arbitrary NPC attack, choose NPC action/target, pass an NPC, choose NPC response, spoof the internal seam |
| Player | attack/pass only with their projected owned current investigator; respond only for their exact pending defender | submit an NPC actor, submit an NPC target choice, force continuation, skip pending response, provide NPC dice/damage, spoof trusted caller |
| Client | render snapshots and owner-specific existing response affordances | call an NPC route, provide continuation flags, calculate legality or canonical results |
| NPC continuation coordinator | choose deterministic Attack/Pass and target from committed private state | roll, resolve, damage, publish, access store, end Combat, answer for a human |
| AI | future narrative/proposal only after separate approval | directly commit canonical action/result, roll, damage, or select Phase 2I gameplay |

The internal NPC executor is not a public or Host capability. Its API surface is assembly/internal application infrastructure and revalidates all canonical conditions.

## Future Test Matrix

Future implementation must add focused tests for at least:

### Selection and stop behavior

- NPC current with one legal human target: Begin exactly once, exact target, pending human response, stop.
- NPC current with multiple legal targets: first active opposing investigator in stable canonical order is selected.
- inactive or defeated structurally valid non-targets are skipped; same-side/non-investigator command candidates are rejected; malformed or hidden-invalid canonical participants fail closed before selection.
- NPC current with a structurally valid supported empty legal-action set: canonical Pass once and inspect returned state; canonical corruption never enters this path.
- inactive Combat: no action.
- pending exchange: no action.
- pending damage: no action and consistency fail-closed.
- human current actor: no action.

### Human response and damage

- NPC attacks player, player Dodge/Fight Back remains owner-controlled.
- Fight Back damages and defeats NPC; existing order repair is retained; continuation uses Damage-returned state; no duplicate wrap.
- Resolve with no damage continues from Resolve-returned state.
- Resolve with damage continues from Damage-returned state.
- NPC driver itself never invokes Resolve or Damage for its attack against a human defender.

### Consecutive NPCs and bound

- `Player → NPC-A → NPC-B → Player-2`: deterministic continuation stops at human boundary.
- NPC-A Pass then NPC-B Attack: exact revision/publication order and no duplicate action.
- multiple NPC Pass actions wrap through existing canonical rules without duplicate round wrap or dying check.
- malformed state that would Pass forever reaches the `Order.Count` guard and fails closed.

### Revision, publication, and HTTP

- every NPC Begin/Pass increments one independent revision.
- every changed revision publishes once after commit, including intermediate revisions.
- final HTTP response is the latest viewer-specific projection after continuation.
- client is not required to observe every intermediate snapshot and never assumes `+1`.
- route enumeration remains exactly `melee-attack`, `respond`, and `pass`.

### Retry and concurrency

- stale/duplicate continuation trigger cannot duplicate Begin/Pass or create a second ExchangeId.
- two application flows observing the same NPC turn permit only one canonical action at that revision.
- lost HTTP response plus old player revision is stale and does not replay player or NPC work.
- Player Pass commit followed by NPC failure retains Pass without rollback.
- NPC Begin commit followed by response failure retains exact pending exchange and does not replay Begin.
- post-RNG invariant behavior remains non-retryable with no reroll.

### Disconnect and privacy

- disconnected active investigator remains a legal deterministic target.
- pending response survives defender disconnect with no auto-response.
- reconnect restores exact owner affordance with unchanged Game revision.
- other participants/nonparticipants never receive the exact response affordance.
- JSON and SignalR snapshots contain no driver decision/policy, internal command, hidden stats, or store data.

### Dependency and authority guards

- continuation coordinator has no `IGameStateStore`, dice, engine, notifier, Hub, connection-registry, AI, worker, queue, or persistence dependency.
- NPC Begin/Pass commands contain no player/Host identity or caller-set trusted flag.
- direct use of historical Host-as-NPC commands is absent from the driver.
- Host/public/client spoof matrices fail before mutation.
- existing Begin/Resolve/Damage/Pass canonical rule tests remain authoritative and green.

## Product Limitations

After a future Phase 2I implementation, Multiplayer Combat can automatically advance an already-active NPC opponent turn using one deterministic non-impaling melee policy. It can select the first legal active investigator, create the normal pending exchange, wait for that player, or canonically Pass when no target exists.

It still cannot start or end Combat publicly, choose tactics, vary attacker personality, select weapons, attack at range, use Firearms/Impaling, move, cast spells, manage inventory/ammunition, run Scenario encounters, substitute for disconnected players, recover process crashes, persist Combat, or scale across processes. It is a deterministic loop-completion slice, not an AI Combat system.

## Deferred

- tactical or AI NPC attacker selection;
- `NpcAttackerPolicy` variants and scenario-authored combat behavior;
- rotating/random target policy;
- weapon selection and switching;
- Firearms and Impaling;
- movement, range, maneuvers, spells, ammunition, and inventory;
- timeout, forfeit, Host substitution, and disconnect policy;
- public Start/End/Damage or NPC lifecycle routes;
- Scenario spawn, encounter outcome, reward, and next-scene integration;
- PvP;
- automatic recovery of pending damage after partial failure;
- durable operation IDs, persistence, DB, Redis, queues, crash recovery, and multi-instance coordination; and
- `PlayerKnowledgeState` mutation from observed NPC actions.

## Risks

| Risk | Mitigation |
| --- | --- |
| Existing internal Begin/Pass accidentally reuse Host as NPC identity | dedicated no-player internal NPC commands and tests rejecting Host/Player/client access |
| Phase 2H discards the latest post-Pass/post-Damage state | sealed application-only outcome retains every transition-returned state |
| Nested outer gate or private room lock deadlocks | exactly one HTTP-owned outer delivery gate; driver acquires neither gate nor private lock |
| Concurrent continuation duplicates an NPC action | same-room outer serialization, expected revision, exact actor, pending gates, and canonical compare/replace |
| Multi-transition request is mistaken for atomic | preserve independent commits, revisions, publications, and reconnectable partial state |
| Whole retry duplicates Pass, Begin, dice, or ExchangeId | never retry continuation as a unit; stale old revision fails closed |
| Pending damage is guessed/recovered automatically | stop/fail closed and defer recovery design |
| Infinite Pass/wrap cycle | iterative loop bounded by canonical `Order.Count` |
| Disconnected player silently becomes untargetable | connection status excluded from legal-target rules |
| Driver leaks private selection state | no new projection fields/events; exact JSON privacy tests |
| Deterministic selector grows into tactics framework | no attacker policy snapshot and strict deferred list |
| Synchronous request becomes slow | participant-derived bound, no external call, participant-only scans; reconsider worker model only with evidence |
| Start path creates an NPC-first stalled Combat | current Start integration is deferred; any future caller must invoke the same outer-gated continuation contract |

## Architecture Alternatives and Primary Recommendation

| Decision area | Alternatives compared | Primary recommendation |
| --- | --- | --- |
| Driver type | deterministic / random / AI | deterministic internal program driver |
| Target selection | stable first / rotating / random / AI | first legal active opposing investigator in canonical order |
| Trigger | synchronous / worker-background / client | synchronous bounded continuation |
| Application owner | player coordinator / dedicated continuation / GameCoordinator | dedicated `ICombatContinuationCoordinator` plus deterministic selector |
| State access | returned State / narrow query / direct store | transition-returned State only |
| Consecutive NPCs | until human boundary / one per trigger | drive until human or pending/end boundary |
| Safety bound | participant-derived / fixed / none | `CombatSession.Order.Count` successful automatic actions |
| Attacker policy state | none / aggressive-passive snapshot / tactics model | no new attacker policy snapshot |

## Exact Required Answers

1. **Who owns NPC action selection?** The dedicated server-internal `ICombatContinuationCoordinator`/`NpcCombatTurnDriver` application boundary.
2. **Is AI involved in Phase 2I?** No.
3. **How does NPC choose Attack vs Pass?** After structural validation succeeds, Attack when at least one legal melee target exists; otherwise use canonical Pass only for a supported valid empty action condition. Corruption fails closed.
4. **How does NPC select target?** The first legal active opposing investigator in stable canonical Combat order.
5. **Can Host choose NPC action?** No.
6. **Can client trigger NPC turn?** No.
7. **Can NPC attack disconnected player?** Yes, when that investigator remains canonically active and legal; connection status does not change legality.
8. **Can NPC answer a human defender's response?** No; only the exact player owner chooses Dodge/Fight Back.
9. **Does NPC driver roll dice?** No.
10. **Does NPC driver resolve damage?** No. Its normal attack path stops after Begin at the human response boundary; existing player-response orchestration later owns Resolve/Damage sequencing.
11. **Does driver read IGameStateStore?** No.
12. **How is canonical state passed between transitions?** Through each internal transition's returned `MultiplayerGameState State`, retained in a sealed application-only outcome/helper.
13. **Can one player intent cause several NPC revisions?** Yes.
14. **Are intermediate revisions published?** Yes, once per changed canonical transition after commit.
15. **What stops consecutive NPC loop?** Inactive/ended Combat, pending exchange, pending damage fail-close, human current actor, invariant failure, or the `Order.Count` automatic-action bound.
16. **What happens on partial failure?** Earlier commits remain canonical and reconnectable; no rollback or guessed recovery occurs, and continuation stops with the approved consistency boundary.
17. **Is whole continuation retried?** No.
18. **Does public route count change?** No; it remains exactly three.
19. **Does Vue gain NPC buttons?** No.
20. **Is Firearms included?** No.

## Recommendation

Approve Phase 2I as a deterministic server-internal continuation slice:

1. Add a dedicated `ICombatContinuationCoordinator` and small deterministic `NpcCombatTurnDriver` selector; extend `PlayerCombatIntentCoordinator` by exactly this one delegation dependency while leaving NPC policy outside it.
2. Implement narrow internal NPC Begin/Pass execution commands on the same canonical `GameCoordinator` instance. Carry exact NPC participant identity and revision but no PlayerId, Host identity, public flag, or caller-selected trust; split authority from shared mutation mechanics instead of duplicating Begin/Pass.
3. After structural validation, select the first active opposing investigator in stable canonical order; Attack when one exists, otherwise use canonical Pass only for a supported valid empty action condition. Fail closed on corruption.
4. Preserve latest committed internal state in a sealed application-only outcome and pass transition-returned State directly into bounded continuation; never read `IGameStateStore`.
5. Continue synchronously under the existing single request-level outer delivery gate, while each existing canonical transition manages its own private room lock, commit, revision, and publication.
6. Stop at any human/pending/end/invariant boundary and fail closed after at most `CombatSession.Order.Count` automatic NPC actions.
7. Preserve partial commits, prohibit whole-continuation retry, publish every intermediate revision, and return only the final fresh viewer-specific HTTP projection.
8. Keep connection status out of target legality and leave the exact human defender response pending through disconnect/reconnect.
9. Add no route, client control, attacker-policy snapshot, AI gameplay, store access, recovery loop, weapon selection, Firearms, or Scenario behavior.

This design is approved after continuation/executor boundary review. Its implementation plan is documented separately; implementation has not started.
