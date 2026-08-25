# Multiplayer Phase 2E Health Stabilization Design

**Status:** Approved design pending implementation-plan review

**Goal:** Migrate the verified Single Player Health Stabilization semantics into a pure C# rule engine and integrate canonical server-side stabilization state without exposing arbitrary player health mutations.

## Scope and non-goals

This design delivers exactly two feature commits:

1. Health Stabilization deterministic migration and JS-to-C# conformance fixtures.
2. Canonical stabilization integration, internal GameCoordinator transitions, safe projection/realtime/reconnect, and read-only Vue display.

The implementation must not include Healing Recovery, Natural Healing, Medicine, weekly Major Wound healing, SAN, Combat, Firearms/Impaling, Scenario progression, Location, Communication, Player Knowledge runtime, AI gameplay, persistence, DB, Redis, matchmaking, accounts, or billing.

No public `/game/stabilize`, `/game/dying-round`, `/game/first-aid`, or equivalent arbitrary health route will be added.

## Authoritative behavior

The reference is the actual execution of `src/hp-damage-state.js`, `src/health-stabilization.js`, and `build/test-v166-health-stabilization.js`. Single Player source and formal HTML remain unchanged.

### Dying round CON

The pure rule input contains the canonical health state, an explicit percentile roll, non-authoritative record context where required, and an injected timestamp when a canonical record needs it. The engine derives the dying-check ordinal from the canonical active dying episode/check history and derives the CON target from `CharacterHealthState.Con`. Callers do not provide a duplicate ordinal or CON target, so the domain cannot receive conflicting values such as `Con = 60` with `target = 80` or history length 2 with ordinal 7. The engine accepts only an active, unstabilized dying state that is not dead.

On success, the engine records `ordinal`, `roll`, `target`, and `success`, advances `nextRoundOrdinal`, retains dying, and does not create stabilization. On failure, it records the failed check, creates dead with reason `dying_con_failure`, and clears dying, unconscious, and stabilized. A dead character and a character without an active unstabilized dying state are rejected without mutation.

Dying check history is bounded to the reference limit of 40 records and belongs to the active dying episode. When a reference transition clears dying because of death or successful First Aid stabilization, Phase 2E must not invent a permanent global dying-check archive. The transition result may return the resolving record for the current transaction/realtime result; treatment history remains separately persistent according to the reference. Timestamps are supplied by the application layer or fixed in fixture inputs; the pure engine never reads wall-clock time.

### First Aid

The pure rule input contains the canonical health state, an explicit canonical First Aid target, an explicit `withinHour` prerequisite, an explicit percentile roll, and an injected timestamp/source context where required. This input is internal/test-only in Phase 2E; it is not a client command contract.

The engine rejects dead characters, characters without a treatable injury, `withinHour != true`, and targets outside 1..100. A successful attempt heals exactly one HP up to max HP, wakes unconscious characters, and when the character is dying creates stabilized, clears dying and unconscious, and preserves Major Wound. A failed attempt does not heal or stabilize. Every accepted attempt records the semantic treatment result, including success, target, roll, within-hour confirmation, HP before/after, healed amount, prior dying/unconscious state, and stabilization/rousing outcomes. Treatment history is bounded to 60 records.

### Fresh damage

The HP/Stabilization domain transition, not GameCoordinator ad-hoc field assignment, owns invalidation of stale stabilization. A trusted new damage event that produces fresh dying or dead clears a prior active stabilized condition. A duplicate damage event does not re-roll or re-append history. This interaction is covered by sequence fixtures and integration tests.

## Architecture

### Commit 1: pure rule migration

Create a focused stabilization domain file beside the existing HP engine, with an interface such as `IHealthStabilizationEngine` and a `CocHealthStabilizationEngine` implementation. The engine consumes explicit deterministic inputs and returns a replacement health state plus a semantic result/record. It must have no dependency on HTTP, SignalR, stores, rooms, Vue, AI, `DateTime.Now`, `Random.Shared`, or credentials.

Extend `CharacterHealthState` only with gameplay-canonical stabilization fields required by the reference. The implementation plan must prefer structured condition/episode records whenever canonical condition-local data exists. In particular, active dying episode data must not be representable independently from whether dying is active; the domain must not permit contradictory parallel representations such as an inactive dying flag with an independently active dying-round sequence. Projection DTOs may expose simplified booleans/status labels without mirroring the canonical domain shape.

The expected shape is conceptually:

```text
CharacterHealthState
├─ MajorWound ...
├─ Unconscious ...
├─ DyingEpisode?
│  ├─ SourceEventKey
│  ├─ Checks[]
│  ├─ NextRoundOrdinal
│  └─ RoundChecksManaged
├─ StabilizedCondition?
├─ DeadCondition?
└─ TreatmentHistory[]
```

The exact C# type names remain an implementation decision, but the invariant is mandatory. The fields are:

- active stabilized condition;
- dying check records and `nextRoundOrdinal`/round-management state;
- bounded treatment history;
- any event identity needed to preserve fresh-damage invalidation.

Do not copy browser authority labels, UI fields, logs, prompts, diagnostics, or transport metadata into the C# domain.

The exporter loads the real Single Player HP and stabilization source in the established Node VM fixture pattern. It runs curated single-step and sequence cases, including dying success/failure, ordinal progression, First Aid success/failure/caps, unconscious wake, dying stabilization, dead rejection, and stabilized-then-fresh-damage invalidation. The committed JSON fixture is the only expected-value source for C# conformance.

### Commit 2: canonical server integration

Add server-internal application operations on `GameCoordinator` for dying-round and First Aid transitions. They run under the existing per-room serialization, load the latest state, validate character/state prerequisites, obtain production percentile rolls through the existing `IDiceRoller`, call the pure engine, replace the canonical character health state, increment revision for reference-defined canonical mutations, commit, and only then publish the latest viewer-specific `GameSnapshot`.

Internal forced-roll inputs are test-only. Production application methods do not accept client-supplied rolls. `withinHour`, round elapsed, helper identity, and First Aid target are internal validated prerequisites, not trusted arbitrary HTTP fields. Since Multiplayer has no canonical time, combat round, helper action, or knowledge system, no public action route is introduced.

Invalid/no-op transitions do not increment revision. Accepted failed First Aid may still increment revision when the reference records a treatment history entry; tests must establish and lock this policy from the fixture rather than infer it from HP delta alone.

While integrating the HP/Stabilization boundary, replace the existing `Random.Shared` fallback used by `ApplyDamageCore` for production HP Major Wound CON rolls with the existing `IDiceRoller`/secure percentile implementation. Forced HP CON rolls remain an internal/test seam only. This is a narrow authority/RNG consistency correction, not a new gameplay feature, and existing HP reference semantics and public API boundaries must remain unchanged.

### Projection and future team visibility

Canonical health state, viewer projection, and future player knowledge remain separate concepts. The current Phase 2E projection remains detailed owner-only: the owner receives only the UI-needed stabilization summary; non-owners do not receive raw rolls, targets, histories, source IDs, event keys, or provenance. The Vue layer renders projected fields only.

Documentation will record the approved future Team Status Visibility policy without adding runtime behavior:

- `AlwaysVisible`: a clipped party-visible health summary, never internal rolls/history/provenance;
- `Contextual`: summary updates only through future authoritative observation/communication;
- `Last Known Status`: contextual loss preserves stale last-known information;
- `PlayerKnowledgeState`: future viewer-specific knowledge separate from `CharacterHealthState`.

Phase 2E does not add `TeamStatusVisibilityMode`, Location, Communication, or knowledge storage.

### Realtime and reconnect

Successful internal mutations follow `engine -> canonical state commit -> revision -> GameProjection(viewer) -> SignalR`. The authoritative recovery path remains `AttachSession -> latest GameSnapshot`; no stabilization-specific reconnect protocol or store is created. Disconnect, reconnect, chat, SignalR delivery, and HTTP requests do not advance dying rounds or time.

## UI behavior

Vue adds read-only stabilization status rendering in the existing health display. It may show `STABILIZED`, `DYING`, `DEAD`, and `UNCONSCIOUS` when present in the server projection. It must not render First Aid, Dying Round, Stabilize, Heal, or Medicine buttons; roll CON; accept a canonical target; add HP; clear state; or infer a state from HP values.

## Verification strategy

Before each feature commit, run the focused red-green tests for the new behavior, then the complete applicable validation:

- `dotnet restore`, `dotnet build`, `dotnet test`, and `dotnet format --verify-no-changes`;
- client `npm ci`, `npm test -- --run`, and `npm run build`;
- Check conformance 19/19 and all existing HP conformance;
- stabilization exporter twice with identical bytes/hash;
- Single Player 37/37 regression scripts and 69/69 JavaScript syntax checks;
- formal HTML build, verifier, deterministic double build, one HTML output, unchanged formal artifact SHA;
- `git diff --check`, clean workspace, and local `main == origin/main` after each push.

Commit 1 must not modify Vue. Commit 2 may update `docs/CURRENT_STATE.md`, `docs/HANDOFF.md`, and minimally `docs/ARCHITECTURE.md`/related docs to fix stale top-level phase text and record the future team visibility policy. After Commit 2 is validated and pushed, stop and only recommend either Healing Recovery or Combat Opposed.
