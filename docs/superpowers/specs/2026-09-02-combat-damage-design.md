# Multiplayer Phase 2G Combat Damage Design

## Status

Approved design after retry-semantics review. Implementation plan pending.

**Date:** 2026-09-02

**Baseline:** `main` / `a1db594d49d4cbe0d527e5445e9b5f5a1802c997`

## Goal

Define the server-authoritative Multiplayer Combat Damage slice that consumes the deferred `DamageDisposition` created by Phase 2F, resolves the verified non-impaling melee damage rules, applies investigator damage through the existing HP domain, owns narrow opponent vitality, and repairs turn state atomically.

This design keeps Combat Damage as a separate trusted internal transition. It adds no public combat action, no client dice, no AI gameplay authority, and no production implementation in this documentation task.

## Scope

Phase 2G is designed to cover:

- a pure deterministic Combat Damage engine for the portable Single Player v1.6.9 rules;
- the actual `parseDiceExpression` grammar and damage-expression limits;
- STR/SIZ Damage Bonus derivation;
- regular, initiator-extreme-eligible, and Fight Back regular-cap damage;
- fixed Armor and zero-net-damage behavior;
- immutable combat-start snapshots of narrow investigator and opponent damage profiles;
- combat-scoped opponent HP and defeat state;
- a separate trusted internal `ResolveCombatDamage(exchangeId)` transition;
- one blocking pending disposition in normal canonical progression;
- secure server generic dice, exactly-once consumption, and durable results keyed by `ExchangeId`;
- positive investigator damage through the existing `IHpDamageEngine` in the same room-locked transaction;
- deterministic inactive-participant turn repair and minimum combat termination semantics;
- viewer-specific read-only projection, commit-before-broadcast, and reconnect recovery;
- future JS conformance and Multiplayer-only generalization tests.

## Non-goals

Phase 2G does not design or authorize implementation of:

- Firearms, Impaling, fighting maneuvers, multiple shots, range bands, reloads, automatic fire, or Dive for Cover;
- variable, ablative, location-specific, or random Armor;
- full NPC sheets, NPC Major Wound, NPC Dying, NPC Stabilization, or NPC treatment;
- inventory, equipment databases, encumbrance, ammunition, or a public weapon editor;
- public Combat Damage, Attack, Dodge, Fight Back, Pass, Start, Resolve, or End endpoints;
- Player Combat Intent, defender-response transport, timeout/disconnect UX, or player action controls;
- AI combat decisions, AI dice, AI damage, or AI HP authority;
- Scenario encounter outcomes, Location, Communication, or PlayerKnowledge runtime;
- persistence, event sourcing, database, Redis, matchmaking, accounts, billing, or migration of stored combats;
- changes to Single Player source, regressions, build scripts, outputs, or the formal HTML artifact.

## Existing Phase 2F Baseline

The actual baseline contains a canonical `CombatSession` attached to `MultiplayerGameState`. Investigator identity is `CharacterId`; `OwnerPlayerId` is authorization only. Combat start snapshots DEX, `fighting_brawl`, and Dodge from canonical `CharacterState.CheckValues`. Opponents are trusted internal definitions.

`BeginOpposedExchange` creates a stable `ExchangeId` without rolling. `ResolvePendingExchange` validates the exact pending exchange and defender authority, performs two server percentile rolls, records a bounded completed exchange, creates a non-null `DamageDisposition` when applicable, registers it independently in `PendingDamageDispositions`, increments counts, and advances the turn. The registry survives `LastExchange` replacement and the 120-entry history limit.

The actual `DamageDisposition` currently contains `ExchangeId`, owner/target participant IDs, mode, `Pending`, and `HpCommitted`. Phase 2F intentionally never consumes it. The actual normal flow can therefore accumulate multiple pending dispositions today; Phase 2G must add a causal gate.

Phase 2F already has per-room serialization, immutable-style state replacement, expected revision checks, commit-before-broadcast, safe `GameProjection`, reconnect snapshot recovery, per-character dying scheduling, and stable participants retained in `Participants`. `Order` and `TurnIndex` currently use active filtering through `FindNextActiveTurn` during normal advancement.

The existing HP boundary is `CocHpDamageEngine.Apply(CharacterHealthState, HpDamageInput)`. It requires positive damage, deduplicates by `EventKey` while that event remains in its bounded 80-entry history, derives HP/Major Wound/instant death/unconscious/dying/stabilization invalidation, and treats `CharacterHealthState.Dead` as investigator death truth. The current coordinator supplies a secure percentile fallback even for damage that ultimately does not need a CON check. Phase 2G should request a CON roll only when the HP engine's existing Major Wound rule requires one; it must not invent a second Major Wound rule.

No public Combat route or client action exists. `CombatSession` is not directly serialized. A room viewer who is not a participant receives `Combat = null`.

## Single Player Reference

The authoritative behavior audit used the actual repository files:

- `src/combat-damage.js`;
- `build/test-v169-combat-damage.js` (48 executable cases);
- `src/combat-opposed.js`;
- `src/check-engine.js`, including `parseDiceExpression`;
- `src/hp-damage-state.js` and `src/health-stabilization.js` for the investigator HP boundary;
- `src/firearms-impaling.js` only to confirm the later Firearms/Impaling boundary.

The portable reference is observable rule behavior, not browser-global storage, automatic rendering, mutable participant objects, AI prompt fields, or literal `player` identity.

## Portable Reference Semantics

### Damage Bonus

Damage Bonus is derived from `floor(STR) + floor(SIZ)`, with the reference clamping the effective sum to at least 2:

| STR + SIZ | Damage Bonus | Maximum contribution |
|---|---:|---:|
| 2..64 | -2 | -2 |
| 65..84 | -1 | -1 |
| 85..124 | 0 | 0 |
| 125..164 | 1d4 | 4 |
| 165..204 | 1d6 | 6 |
| 205..284 | 2d6 | 12 |
| each additional complete 80-point band | +1d6 | +6 |

For sums above 204, `count = 2 + floor((sum - 205) / 80)`. Damage Bonus is always derived from the immutable owner's profile; it is never accepted from a resolution caller.

### Weapon Damage

The v1.6.9 slice supports only `mode = melee_non_impaling`. A normalized weapon contains a stable ID, safe label, normalized damage expression, and `AddsDamageBonus`.

The actual expression grammar is case-insensitive, trimmed, and limited to 32 characters:

```text
^(\d*)d(\d+)([+-]\d+)?$
```

It accepts `NdM`, `dM`, `NdM+K`, and `NdM-K`; default count is 1. Count is 1..100, faces 2..10000, and modifier -100000..100000. Invalid syntax or bounds fail closed before any roll. The canonical parser should normalize to a structured `DiceExpression(count, faces, modifier, text)` and use checked arithmetic. Phase 2G preserves the grammar and bounds for conformance, while only trusted canonical profiles may supply expressions.

Regular damage rolls every weapon die, adds the fixed expression modifier, then adds the derived Damage Bonus when `AddsDamageBonus = true`. A flat negative Damage Bonus is retained. Gross damage is `max(0, weapon total + Damage Bonus total)`.

### Extreme

`initiator_extreme_eligible` maximizes the normalized weapon expression and the applicable Damage Bonus. Weapon maximum is `count * faces + modifier`. A dice Damage Bonus uses its maximum; a flat negative Damage Bonus remains negative. Gross damage is still clamped to zero.

No weapon or Damage Bonus dice are rolled for this non-impaling extreme mode. A critical attacker reaches this mode through the Phase 2F opposed result. This is not Impale and does not add an extra weapon roll.

### Fight Back

`fight_back_regular_cap` uses actual regular weapon and Damage Bonus rolls even when the defender's opposed success was extreme or critical. It never invokes extreme maximization.

### Armor

Fixed canonical Armor is an integer 0..99. Net damage is `max(0, grossDamage - targetArmor)`. Armor applies before any investigator HP threshold, so Major Wound and instant-death evaluation receive net damage, not gross damage.

### Zero Damage

A valid disposition can resolve to `NetDamage = 0` because of a negative Damage Bonus, low damage, or Armor. It is still successfully consumed, records one canonical `CombatDamageResult`, increments the game revision once, commits, projects, and broadcasts once. It does not call `IHpDamageEngine`, does not write an HP event, and does not change target HP.

### HP Damage Integration

For an investigator target and positive net damage, the application layer calls the existing HP engine inside the same room-locked transaction with:

```text
EventKey = "combat:" + ExchangeId
Damage   = NetDamage
ConRoll  = secure server percentile only when required by the existing HP rule
```

It does not pre-write `CurrentHp`, copy the Single Player browser mutation sequence, or patch conditions afterward. The HP engine exclusively determines investigator HP, Major Wound, instant death, unconsciousness, dying, and stale stabilization invalidation.

For an opponent target, Phase 2G subtracts net damage only from combat-scoped vitality. It does not create a fake `CharacterHealthState`.

## Single-player-specific Assumptions

The following reference behaviors are not Multiplayer contracts:

- one literal participant named `player` and one global `state.character`;
- browser-owned RNG, browser mutation, render calls, logs, and AI-busy guards;
- automatic damage consumption inside the `combatResolveMelee` wrapper;
- player loadout editing in the browser and lazy legacy-save initialization;
- legacy opponent defaults of STR 50, SIZ 50, HP 10, Armor 0, and unarmed 1d3;
- removing defeated IDs from the mutable `order` array;
- ending the entire combat when the single player dies;
- storing the result by mutating only the exchange/history object;
- exposing combat profile details through the Single Player sidebar/context/prompt;
- treating browser wall-clock timestamps or localized error messages as rule semantics.

Multiplayer keeps the verified damage math but generalizes identity, authority, state lifetime, privacy, multiple investigators, and opponent vitality.

## Potential Reference Issues

1. **Already-defeated target leaves the disposition pending.** `combatDamageApplyDisposition` returns `applied:false`, `reason:"target_already_defeated"`, performs no revision, and leaves `pending=true`. Under a Multiplayer causal gate this would deadlock progression. Multiplayer instead consumes it once as a durable `TargetAlreadyIneligible` no-op result without dice or HP mutation.
2. **Damage is consumed after turn advancement.** Single Player advances the turn, including any round wrap, before its wrapper consumes damage. Phase 2F does the same. Multiplayer must repair from the already-advanced canonical position and must never replay an action or a wrap.
3. **Single-player death ends all combat.** That is valid for one investigator but not for multiple investigators. Multiplayer inactivates only the dead investigator and ends only on an explicit aggregate terminal condition.
4. **Legacy opponent defaults conceal missing authority.** Multiplayer trusted start definitions must provide a complete, validated opponent profile; no STR/SIZ/HP/Armor/weapon fallback is copied.
5. **Browser participant mutation conflates profile, vitality, and projection.** Multiplayer uses immutable combat-start snapshots and aggregate replacement.
6. **History-only result storage is not exactly-once truth.** The reference rewrites `lastExchange` and the matching bounded history entry. Multiplayer retains status/result independently of bounded history.
7. **Loadout mutation is a Single Player UI feature.** Multiplayer adds no editor or mid-combat mutation. A profile is canonical before Combat start and snapshotted.
8. **Extreme metadata loses the weapon modifier field.** The reference numeric total correctly includes the modifier, but its maximized `weaponResult.modifier` is written as zero. Multiplayer conformance should compare the numeric semantic result while recording the actual normalized modifier in its internally consistent result metadata.

## Multiplayer Canonical Model

### Damage Profile

Each `CombatParticipantState` receives an immutable combat-start `CombatDamageProfile`:

```text
CombatDamageProfile
├─ Str
├─ Siz
├─ DerivedDamageBonusProfile
├─ WeaponProfile
├─ FixedArmor
└─ Vitality?                 // opponent only
```

The profile is validated before any disposition can be produced and never read from a command at damage-resolution time. Derived Damage Bonus may be stored for audit/convenience but must be recomputed and validated from STR/SIZ at construction so it cannot become independent caller state.

### Weapon Profile

`CombatWeaponProfile` contains `WeaponId`, `Label`, `DiceExpression`, `AddsDamageBonus`, and the only supported mode `MeleeNonImpaling`. Unsupported or unknown modes fail Combat start before any dice. Labels are display metadata, never identity.

Phase 2G adds no public weapon editor. The initial investigator profile uses a server-canonical narrow loadout (default unarmed 1d3, adds DB, Armor 0 where no separately trusted pre-combat loadout exists). The model permits a future trusted pre-combat loadout transition, but no future capability is silently exposed now.

### Investigator Profile

Primary choice: **CheckValues plus a narrow canonical loadout**.

- STR and SIZ come from the existing canonical `CharacterState.CheckValues` keys `str` and `siz`, alongside the Phase 2F DEX/Fighting/Dodge source.
- Missing or invalid STR/SIZ fails Combat start; no default 50 is invented.
- The narrow loadout is canonical character-side state, initialized by the server-supported default and not caller-selectable during damage resolution.
- Combat start snapshots STR, SIZ, derived DB, weapon, and Armor into the participant. Mid-combat changes to CharacterState cannot alter a pending disposition's result.

This avoids a second full character sheet (explicit profile option), avoids trusting arbitrary Start input, and is more extensible than hard-coding damage math directly to unarmed-only behavior.

### Opponent Profile

Primary choice: **combat-scoped opponent vitality/profile**.

The trusted internal `OpponentDefinition` is extended with required STR, SIZ, `CurrentHp`, `MaxHp`, fixed Armor, and a normalized non-impaling melee weapon. Values are validated and snapshotted at Combat start. No legacy defaults are allowed. Opponent current HP may be below max to support a trusted pre-injured encounter definition, but must be positive at start; zero-HP opponents are rejected rather than inserted inactive.

Opponent HP lives only in the Combat aggregate. `Hp <= 0` means combat-scoped defeated and inactive. It does not imply investigator conditions and does not create NPC Major Wound/Dying/Stabilization.

The alternative investigator-only-target slice is rejected because Phase 2F explicitly supports investigator attacks against opponents and cannot complete defeat/order behavior without opponent vitality.

### DamageDisposition Consumption

Primary choice: **a separate trusted internal `ResolveCombatDamage(exchangeId)` transition**.

This preserves Phase 2F's explicit deferred-consumer boundary and permits focused deterministic conformance. The command carries room ID, exact ExchangeId, and expected revision for a new mutation. It carries no owner/target, weapon, STR/SIZ, Armor, damage mode, roll, HP, or result fields.

The application resolves owner, target, mode, and both immutable profiles from canonical state. Only an exact registered disposition may be consumed. Labels, `LastExchange`, and bounded `History` are never lookup authority.

### CombatDamageResult

The internal canonical result contains at least:

```text
ExchangeId
OwnerParticipantId
TargetParticipantId
DamageMode
Outcome                         // Applied | TargetAlreadyIneligible
WeaponId / normalized expression
WeaponResult                    // expression, raw rolls, modifier, total, maximized
DamageBonusResult               // expression, raw rolls, modifier, total, maximized
GrossDamage
Armor
NetDamage
HpBefore / HpAfter
HpDamageApplied
TargetDefeated
ResolvedAt
```

Raw rolls are canonical internal audit data, not public projection. For `TargetAlreadyIneligible`, dice result fields are absent, HP is unchanged, and no RNG occurs.

### Consumption State

Primary choice: **retain the registry entry and change explicit status from Pending to Consumed with its immutable result**.

Conceptually:

```text
DamageDispositionState
├─ Disposition                  // immutable ExchangeId/owner/target/mode
├─ Status                       // Pending | Consumed
└─ Result?                      // required exactly when Consumed
```

This replaces ambiguous `Pending`/`HpCommitted` booleans. It retains exactly-once truth and reconnect/audit state independently of 120-entry exchange history and 80-entry HP history, without event sourcing or a second consumed-marker collection. It is retained for the life of the in-memory CombatSession; persistence and archival compaction are deferred.

Removing the entry plus a separate marker adds two collections and synchronization risk. A bounded consumed collection is rejected because eviction would permit re-consumption. Normal progression allows only one blocking Pending entry, although the registry can represent multiple entries for Phase 2F compatibility, diagnostics, and future migration.

`ResolveCombatDamage` validates room/game existence, trusted internal authority, and registry existence, then looks up the exact ExchangeId and its status **before** applying expected-revision validation for a Pending mutation. An exact retry whose status is Consumed returns the stored immutable result with `Changed=false`, even when the caller still carries the stale `ExpectedRevision` from the successful first attempt. This is an idempotent read of canonical truth, not a new mutation. It does not validate a mutation revision, roll, call HP, mutate vitality, repair again, increment revision, append history, commit, or broadcast.

Only a Pending entry enters the mutation path and requires `ExpectedRevision == CurrentGameRevision`. A stale Pending mutation fails before RNG. A missing ExchangeId fails closed. A malformed or internally inconsistent disposition/result/status combination is an internal invariant failure, not a reason to infer or recreate damage.

## Dice Authority

Primary choice: **extend the existing `IDiceRoller` as the single secure server dice authority**, rather than create a second security abstraction.

The extension should accept a validated structured dice request and return the complete raw results and total. `SecureDiceRoller` continues to use `RandomNumberGenerator`; deterministic fakes implement the same interface in tests. Percentile and generic dice remain distinct methods because CoC bonus/penalty percentile selection is not an NdM expression.

Parsing and maximum calculation are pure. The pure Combat Damage engine receives explicit validated roll results and never calls RNG. The application layer determines the required roll plan from canonical profiles, asks `IDiceRoller`, validates returned count/range against that plan, and passes it to the engine. Client, AI, HTTP payload, labels, and persisted browser rolls are never accepted.

For regular/Fight Back damage, roll weapon dice and dice DB only when applicable. Flat DB and `AddsDamageBonus=false` need no generic roll. Extreme non-impaling damage needs no generic roll. A CON percentile roll is separately requested only if positive investigator damage invokes a Major Wound check under the existing HP rule.

## Damage Consumption Transaction

`ResolveCombatDamage` runs under the existing per-room lock as one canonical transaction:

```text
load latest game/session
  -> validate room/game, trusted internal authority, and registry existence
  -> exact ExchangeId/status lookup
  -> if Consumed: return stored result Changed=false before revision validation
  -> if Pending: validate expected revision and blocking order
  -> validate owner/target, target eligibility classification, immutable profiles,
     damage mode, expression/STR/SIZ/Armor bounds, and complete roll plan
  -> if target already ineligible: build no-op consumed result without RNG
  -> otherwise build roll plan and obtain secure server dice
  -> pure CombatDamageEngine calculation
  -> investigator positive net: existing HpDamageEngine
     opponent: combat-scoped vitality subtraction
     zero net: no HP engine call
  -> consume disposition with immutable result
  -> synchronize investigator death or opponent defeat to participant Active
  -> deterministic turn repair / termination
  -> replace MultiplayerGameState and increment Revision exactly once
  -> commit
  -> viewer-specific projections
  -> existing realtime broadcast
```

There is no intermediate commit between HP mutation and disposition consumption. Before any damage RNG, while holding the per-room canonical mutation lock, the application must complete every ordinary fail-closed or conflict check: room/game and internal authority, exact ExchangeId, disposition existence/status, deterministic blocking order, Pending expected revision, owner and target existence, target eligibility classification, participant/profile validity, supported mode, weapon expression, canonical STR/SIZ, Armor, and the complete dice plan. No ordinary caller/state conflict remains after RNG begins.

After canonical damage RNG has begun, `TryReplace(expected, replacement) == false` is an **internal invariant/storage consistency failure**, not a normal retryable `StateConflict`. The caller must never be told to retry the same Pending ExchangeId in a way that re-rolls it, and the coordinator must not automatically re-run the roll. Under the current in-memory store, per-room `SemaphoreSlim`, and expected-object replacement, the serialized path makes a post-validation stale race invalid; failure indicates that the storage/locking invariant was broken and requires operational escalation.

If a future distributed database, optimistic transaction, or multi-process authority makes post-RNG commit conflict realistic, that architecture must first add durable roll reservation, a deterministic committed roll record, a transaction-owned RNG outcome, or an equivalent exactly-once mechanism. Re-rolling the same ExchangeId is never an acceptable conflict strategy.

## Causal Progression Gate

Primary choice: **a pending damage disposition blocks normal Combat progression until consumed**.

While any Pending disposition exists, the server rejects:

- `BeginOpposedExchange`;
- `PassCombatTurn`;
- another normal action or turn progression;
- trusted manual End Combat after an exchange has resolved to damage.

An unresolved `PendingCombatExchange` retains Phase 2F's trusted cancellation/end behavior because it has not yet resolved into a hit. A resolved blocking `DamageDisposition` is not cancelled, erased, or skipped by manual End: the already-adjudicated hit must be consumed first. After consumption, the combat either continues, terminates automatically from the damage result, or may later be ended by a trusted command. A resolved hit cannot disappear because the host ends combat.

The registry may physically hold multiple Pending entries from a pre-gate Phase 2F state. In that exceptional state, the blocking disposition is selected deterministically by completed exchange order and ExchangeId membership, and only it may be consumed. Normal Phase 2G flow can create at most one Pending entry because the first one closes the progression gate.

The Phase 2F turn has already advanced when the gate begins. No participant may act at that new position before damage consumption. Reconnect, refresh, delivery failure, chat, AI narrative, and projection do not consume or bypass the gate.

## HP Damage Boundary

Positive investigator net damage is routed exclusively through `IHpDamageEngine`. Phase 2G calls the engine directly as part of the already-held aggregate transaction rather than calling a coordinator method that would commit separately or acquire the lock recursively.

The stable HP key is `combat:{ExchangeId}`, matching the actual reference. Disposition status remains the primary exactly-once guard even after the HP engine's bounded history trims that event. HP dedupe is defense in depth, not Combat Damage consumption truth.

`CharacterHealthState.Dead` alone determines investigator death. `CurrentHp == 0` with Dying is not death and does not inactivate the participant. Fresh Dying created by combat damage updates `CombatSession.DyingSchedule` at the current round using the existing Phase 2F observation rule; no immediate dying check occurs. Instant death clears scheduling and inactivates the participant. Stabilization invalidation remains owned by the HP engine.

Zero net damage does not call the HP engine. An already-dead/inactive investigator target is handled by the `TargetAlreadyIneligible` consumed no-op, never by asking the HP engine to apply damage to a dead state.

## Opponent Defeat Boundary

Opponent positive net damage subtracts from combat-scoped `CurrentHp` with a floor of zero. Zero net damage leaves it unchanged. When HP reaches zero, set the existing participant `Active=false`; the opponent profile/vitality records the zero HP. No investigator health record or NPC condition is created.

An opponent profile is immutable except for combat-scoped current vitality and participation state. Weapon, STR/SIZ, DB, Armor, and MaxHp do not drift mid-combat.

## Turn / Order Repair

Primary choice: **keep stable `Order`; retain every participant; skip `Active=false` entries**.

This preserves participant identity for prior exchanges and avoids index shifts. After damage and any inactivation:

1. If a terminal condition is met, end the session and do not choose another actor.
2. If the already-advanced `TurnIndex` points to an active participant, leave it unchanged.
3. Otherwise scan forward from that same index for the next active participant. Do not restart from `TurnIndex + 1`, because the inactive current slot has not acted.
4. If found, set that index without changing action/response counts or round.
5. If none exists to the end, invoke the existing canonical round-wrap path exactly once, including its established counter reset and per-character dying schedule, then select the first active participant.

Example: `A -> B -> C`; A attacks B; Phase 2F advances to B; damage defeats B; repair scans from B's current index and selects C. A cannot act twice, C is not skipped, and no wrap occurs.

Damage resolution never replays the opposed action, increments action/response counts, or blindly advances by one. If Phase 2F already wrapped during opposed resolution, the current active index remains valid and no second wrap occurs.

## Combat Termination

The minimum aggregate semantics are:

- no active opposition participants: end with `opposition_defeated`;
- one investigator becomes dead: inactivate only that participant; continue when another investigator remains active;
- no active investigator participants: end with `investigators_defeated`;
- no active participants: terminal under the applicable side-specific reason; never leave an active session without a current actor;
- Dying, Unconscious, Major Wound, or HP zero without `DeadCondition` does not by itself inactivate an investigator;
- manual End remains internal and cannot erase a resolved blocking hit.

These are combat lifecycle mechanics, not Scenario success/failure or encounter rewards.

## Projection / Privacy Matrix

Canonical damage records are never directly serialized.

| Viewer | Allowed read-only information | Forbidden information |
|---|---|---|
| Owner of the damaged investigator | Existing owner-only exact Health; safe last-damage summary with owner/target IDs, safe weapon label, net damage, and defeated status | Raw weapon/DB dice, opponent STR/SIZ, exact opponent HP/MaxHp, Armor value, internal expression/profile, HP event/provenance |
| Owner of the damage-dealing investigator | Safe last-damage summary and their own already-authorized profile summary if a future DTO needs it | Target investigator exact HP/conditions, opponent exact HP/Armor, raw dice, internal result/domain records |
| Other combat participant | Damage occurred, semantic owner/target participant IDs, net damage, and target-defeated status | Exact investigator Health, exact opponent vitality/profile, raw dice, gross/Armor decomposition, source IDs/history |
| Room nonparticipant | No Combat projection, preserving Phase 2F default | All combat and damage details |

`DispositionPending` may remain a derived safe blocking indicator. A consumed safe summary is derived from the canonical result and does not include raw rolls. Exact opponent HP, STR/SIZ, DB, Armor, expression, and raw rolls are not exposed merely because a viewer participates in combat.

## Realtime / Reconnect

The existing pattern remains authoritative:

```text
canonical commit -> viewer-specific GameProjection -> existing GameSnapshot delivery
```

Delivery failure does not roll back or retry damage. AttachSession recovers the latest safe snapshot. A reconnect while damage is Pending observes a safe blocking status; it does not auto-roll. A reconnect after consumption observes the stored safe result; it does not re-run the engine. Duplicate commands return the existing canonical result without a new broadcast or revision.

No Combat Damage-specific event stream or client-side recovery state is introduced.

## Architecture Comparison

### Damage consumption

- Same-transaction consumption inside `ResolvePendingExchange` would eliminate intermediate state, but it would erase the explicit Phase 2F registry boundary and couple opposed conformance to damage/HP behavior.
- A separate internal `ResolveCombatDamage(exchangeId)` preserves that boundary and remains safe when paired with a causal gate.

**Primary Recommendation:** separate internal consumption.

### Causal ordering

- Automatic immediate consumption has no gate but collapses the selected separate transition.
- Allowing another action while damage is Pending can let an actually defeated participant act.
- Blocking normal progression preserves the already-resolved hit's causal priority.

**Primary Recommendation:** blocking causal gate until consumption; no normal accumulation of pending damage.

### Investigator damage profile

- An explicit full `CharacterState` combat profile duplicates the provisional canonical rule-value source.
- Trusted Start input allows the initiating caller to alter a character's damage profile per combat.
- Default-unarmed-only is safe but prematurely prevents a narrow canonical loadout.
- CheckValues for STR/SIZ plus a narrow canonical loadout reuses current authority and supports immutable start snapshots.

**Primary Recommendation:** CheckValues plus narrow canonical loadout and combat-start snapshot.

### Opponent vitality

- Investigator-only damage targets cannot complete the existing investigator-versus-opponent opposed flow.
- Combat-scoped opponent vitality supports damage and defeat without creating a full NPC health domain.

**Primary Recommendation:** required trusted combat-scoped opponent vitality/profile.

### Generic dice authority

- A separate secure generic-dice abstraction duplicates security/configuration and lets percentile and damage RNG drift.
- Extending `IDiceRoller` preserves one production RNG authority while keeping distinct percentile and NdM methods.

**Primary Recommendation:** extend the existing `IDiceRoller`.

### Disposition consumption storage

- Removing Pending plus adding a consumed marker/result requires two synchronized collections.
- Removing into a bounded consumed collection loses exactly-once truth after eviction.
- Retained status/result uses one keyed authority and survives ordinary history trim.

**Primary Recommendation:** retain the ExchangeId entry with `Pending -> Consumed` and immutable result.

### Defeated participant turn behavior

- Removing an ID from Order shifts indices and complicates historical participant resolution.
- Keeping a stable Order and skipping inactive entries preserves identity and supports scan-from-current repair.

**Primary Recommendation:** keep Order stable, retain Participants, and mark defeated participants inactive.

## Conformance Strategy

A future Commit 1 fixture exporter should execute the actual Single Player reference through the existing Node VM convention. It must call actual `combatDamageBonusProfile`, weapon normalization, dice parsing, amount resolution, disposition application, and HP reference boundaries; it must not copy those rules into the exporter.

Reference-conformance cases should cover:

- all DB thresholds: 64/65, 84/85, 124/125, 164/165, 204/205, 284/285;
- high DB interval growth;
- valid `dM`, `NdM`, positive/negative modifier expressions and every parser bound;
- invalid syntax, count, faces, modifier, length, and unsupported mode;
- regular weapon + positive/dice/zero/negative DB and `AddsDamageBonus=false`;
- extreme maximization with weapon modifier, positive dice DB, zero DB, and negative DB;
- Fight Back regular cap;
- Armor below/equal/above gross and zero net damage;
- investigator HP, Major Wound after Armor, instant death, and stable `combat:{ExchangeId}` identity;
- opponent partial HP, defeat, and multiple-opponent continuation;
- disposition status/result rewrite in the reference;
- reference `target_already_defeated` behavior labeled as a Potential Reference Issue;
- explicit Firearms/Impaling deferral.

Fixture metadata must distinguish `reference_conformance`, `multiplayer_generalization`, and `deferred`. It must not claim JS expected values for CharacterId ownership, server revisions, multiple investigators, registry status, or projection.

## Multiplayer Generalization Tests

Future server tests must cover at least:

- pure engine determinism and no dependency on coordinator, state, time, RNG, HTTP, SignalR, Vue, or AI;
- CheckValues STR/SIZ source, missing/invalid rejection, narrow loadout source, and combat-start snapshot immutability;
- required explicit opponent profile and no legacy defaults;
- generic secure dice calls only after validation, exact count/range, extreme no-roll, flat DB no-roll, and no client/AI dice path;
- exact ExchangeId lookup and rejection of labels, LastExchange, history index, owner, target, mode, or caller damage;
- one blocking Pending disposition in normal progression;
- Begin, Pass, normal progress, and manual End rejected while damage is Pending;
- registry storage capability with multiple legacy Pending entries and deterministic blocking order;
- successful consumption increments revision and broadcasts exactly once;
- consumed replay with the original stale ExpectedRevision returns the same stored result with `Changed=false` and no roll, HP, vitality, repair, revision, history, commit, or broadcast;
- Pending stale revision, missing disposition, wrong blocking ExchangeId, malformed mode, and invalid profile all fail before RNG;
- a forced post-RNG `TryReplace` failure is classified as an internal invariant/storage failure and never as an ordinary retryable conflict;
- status/result retained after more than 120 exchanges and after HP history exceeds 80 events;
- zero net damage consumes, records, commits, and skips HP engine;
- positive investigator damage exclusively uses HP engine and the exact event key;
- CON roll is secure and requested only when required;
- HP result creates Major Wound/Dying/Dead according to the existing HP engine;
- fresh Dying schedule observation and no same-round check;
- Dying investigator remains active; dead investigator becomes inactive;
- opponent HP subtraction, defeat, no fake health conditions, and no exact vitality projection;
- `TargetAlreadyIneligible` consumes without dice or HP and releases the gate;
- `A -> B -> C` next-actor repair, inactive first/middle/last slots, wrap exactly once, no double turn, and no skipped actor;
- one investigator death continuation, all-investigator termination, one-opponent continuation, and all-opposition termination;
- room-lock atomicity, concurrent duplicate consumption, and stale request isolation;
- raw dice/profile/history/source omission from serialized projections;
- owner, other participant, and nonparticipant privacy;
- commit-before-broadcast, cross-room isolation, delivery failure, and AttachSession recovery.

## Proposed Phase 2G Implementation Boundary

This is an architecture boundary, not an implementation plan.

A future first feature commit may contain only deterministic migration: structured dice-expression parsing, secure generic dice interface support, pure Combat Damage math, actual-JS conformance export, and focused pure tests. It should not modify Vue or expose a route.

A future second feature commit may contain canonical profiles, opponent vitality, internal disposition consumption, causal gate, HP integration, turn/termination repair, safe projection, realtime/reconnect coverage, and at most a read-only client summary. It must not add a player action or public Combat Damage API.

Implementation must begin only after this design is reviewed and a separate implementation plan is explicitly authorized and reviewed. This document does not authorize writing production code, tests, fixtures, Vue, or an implementation plan now.

## Deferred

- Firearms and Impaling, including the already-existing Single Player v1.6.10 extensions;
- variable Armor, maneuvers, multiple attacks, automatic fire, range, ammunition, reload, and Dive for Cover;
- full NPC health, NPC Major Wound, NPC Dying, NPC Stabilization, and NPC treatment;
- inventory, equipment database, non-default public loadout editing, and item ownership;
- public Combat Damage API, Player Combat Intent, defender-response flow, and combat controls;
- timeout, disconnect-forfeit, automatic NPC turns, and AI gameplay;
- Scenario encounter engine and outcome/reward linkage;
- Team Status Visibility beyond the narrow safe combat summary;
- Location, Communication, PlayerKnowledge, persistence, event sourcing, DB, Redis, matchmaking, accounts, and billing.

## Risks

1. **Gate deadlock.** Leaving an ineligible target Pending would block forever. The explicit no-op consumed result prevents that without applying damage to a dead target.
2. **Double HP after history trim.** HP history alone is bounded. Durable disposition status/result remains the primary guard.
3. **Profile drift.** Live CharacterState or caller input at resolution could change damage after a hit. Start snapshots are immutable.
4. **Turn corruption.** Removing IDs or advancing again can skip/double actors. Stable Order and scan-from-current repair are required.
5. **Extra RNG.** The existing damage coordinator path eagerly obtains a percentile fallback. Phase 2G must roll only the dice actually required.
6. **Privacy leakage.** Direct result/profile serialization would expose opponent internals and raw dice. Projection must be explicit and viewer-specific.
7. **Reference over-porting.** Automatic browser consumption, single-player death termination, legacy defaults, and mutable history are not safe Multiplayer contracts.
8. **Memory lifetime.** Retaining every consumed result grows with a long in-memory combat. Exactly-once truth takes priority in this phase; persistence/archival compaction requires a later explicit design and cannot use ordinary history trimming.
9. **Internal interface breadth.** Extending `IDiceRoller` affects test fakes. The change should remain narrow and mechanically update implementations without creating a second RNG authority.

## Open Questions

No unresolved product-level question blocks this internal-only design. The following are explicitly deferred rather than delegated to the reviewer as Phase 2G blockers:

- the future public source and editing UX for non-default investigator loadouts;
- archival/compaction once CombatSession persistence exists;
- broader team visibility of exact opponent or ally status;
- scenario-specific encounter termination and rewards;
- Firearms/Impaling profile and damage modes.

Review should confirm the selected architecture before any implementation plan is written: separate internal consumption, blocking causal gate, CheckValues plus narrow loadout, combat-scoped opponent vitality, one extended secure dice authority, retained status/result, and stable-order inactive skipping.

## Recommendation

Approve Phase 2G for a later, separately planned implementation using:

- `ResolveCombatDamage(exchangeId)` as a separate trusted internal canonical transition;
- a blocking causal gate with normally one Pending disposition;
- a pure deterministic `ICombatDamageEngine` and exact v1.6.9 non-impaling melee math;
- the existing secure `IDiceRoller` extended for validated generic dice;
- investigator STR/SIZ from canonical CheckValues plus a narrow canonical loadout, all snapshotted at Combat start;
- complete trusted opponent profiles with combat-scoped vitality and no NPC health-domain imitation;
- retained disposition status plus immutable result keyed by ExchangeId;
- positive investigator net damage exclusively through the existing HP engine, zero damage without an HP call, and `combat:{ExchangeId}` event identity;
- stable Order, inactive skipping, scan-from-current turn repair, and exactly one wrap when required;
- `opposition_defeated` / `investigators_defeated` minimum termination without Scenario semantics;
- viewer-specific safe summaries, hidden raw dice/opponent internals, and snapshot-based reconnect;
- no public API, no gameplay UI, no AI authority, no Firearms/Impaling, and no implementation until design and a future plan are reviewed.

## Design Invariants

1. ExchangeId is the only Combat Damage consumption identity.
2. A DamageDisposition is consumed at most once.
3. Retry never re-rolls damage.
4. Retry never applies HP twice.
5. Client never provides canonical damage dice.
6. AI never provides canonical damage dice.
7. Combat Damage never bypasses HpDamageEngine for investigator positive damage.
8. NetDamage=0 never sends invalid Damage=0 into HpDamageEngine.
9. Fixed Armor is server canonical.
10. STR/SIZ/loadout cannot be caller-selected at damage resolution time.
11. Damage Bonus is derived, not arbitrary caller state.
12. Unsupported weapon modes fail closed.
13. Firearms/Impaling remain deferred.
14. Investigator dying/dead truth remains CharacterHealthState.
15. Opponent defeat does not create fake investigator HealthState.
16. One investigator death does not automatically end Multiplayer Combat.
17. Defeat cannot cause turn skip or double-turn corruption.
18. Damage consumption and HP/defeat/order repair are atomic.
19. Projection never serializes internal damage domain directly.
20. Reconnect never re-rolls damage.
21. Public Combat Damage API remains absent.
22. History trimming cannot destroy exactly-once consumption truth.
23. ExchangeId/status lookup precedes Pending mutation revision validation, so a Consumed stale-revision replay returns its stored result.
24. After damage RNG begins, replacement failure is never a normal retry path and never authorizes re-rolling that ExchangeId.
