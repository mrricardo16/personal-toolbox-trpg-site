# Multiplayer Phase 2G Combat Damage Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Migrate the verified Single Player non-impaling melee damage rules into deterministic C# and integrate exactly-once internal Combat Damage consumption into the canonical Multiplayer combat aggregate without adding a public gameplay action.

**Architecture:** Feature Commit 1 builds the pure dice-expression, Damage Bonus, weapon, generic secure dice, and Combat Damage rule layer from an actual-JS deterministic fixture. Feature Commit 2 snapshots canonical investigator/opponent profiles at Combat start, gates unresolved damage, consumes `ExchangeId` through a room-locked internal transition, delegates positive investigator damage to the existing HP engine, repairs stable turn order, and projects only safe read-only summaries. Consumed replay is checked before Pending revision validation; every ordinary failure is resolved before RNG, and post-RNG replacement failure is an internal invariant/storage failure, never a normal re-roll path.

**Tech Stack:** .NET 8, C# records and xUnit, ASP.NET Core Minimal APIs/SignalR, Vue 3 + TypeScript + Vitest, Node 24 VM fixture exporters, PowerShell, Git.

## Global Constraints

- Start only from the reviewed design and synchronized `origin/main`; re-check status, branch, HEAD, divergence, and unknown changes before implementation.
- Keep every edited text file strict UTF-8. Stop before rewriting any file that is not strict UTF-8.
- Produce exactly two future feature commits: `feat: migrate combat damage rules`, then `feat: integrate multiplayer combat damage`.
- Preserve `src/`, `build/`, `outputs/trpg-dm-assistant.html`, and all Single Player semantics. The exporter reads actual JS but never modifies it.
- Feature Commit 1 must not modify `GameCoordinator`, canonical combat state, HP integration, opponent vitality mutation, projection, realtime, Vue, or routes.
- Feature Commit 2 remains internal-only. Public Combat API count stays zero; no player can submit damage rolls, damage amount, weapon, Armor, STR/SIZ, owner, target, mode, HP, or result.
- No Firearms, Impaling, fighting maneuvers, variable/location Armor, ammunition, reload, multiple shots, full NPC sheet, NPC Major Wound/Dying/Stabilization, inventory, equipment database, public loadout editor, Player Combat Intent, defender response transport, timeout/disconnect forfeit, AI gameplay, Scenario engine, Location, Communication, PlayerKnowledge runtime, persistence, database, or Redis.
- `ExchangeId` is the only consumption key. Registry status/result outlives 120-entry combat history and 80-entry HP history.
- A Consumed replay returns the stored immutable result with `Changed=false` before Pending expected-revision validation, even with the original stale revision. It performs no RNG, HP/vitality mutation, repair, revision, commit, or broadcast.
- A Pending mutation requires current expected revision and deterministic blocking order. All ordinary validation occurs before RNG.
- After RNG begins, `TryReplace` failure is an internal invariant/storage consistency failure. It must not return an ordinary retryable conflict or re-run the ExchangeId.
- Positive investigator net damage uses the existing `IHpDamageEngine`; zero net damage never calls it. CON is rolled only when the existing HP threshold requires it.
- Every task below is test-first. Do not weaken assertions, disable format, or rewrite expected fixture output to manufacture green.
- Commit 1 and Commit 2 each receive their complete independent validation gate and push verification before the next commit starts.

---

## File Map

### Feature Commit 1 — deterministic migration

- Create `multiplayer/server/src/Trpg.Multiplayer.Api/Gameplay/DiceExpression.cs`: exact reference grammar, structured expression, semantic parse errors.
- Create `multiplayer/server/src/Trpg.Multiplayer.Api/Gameplay/CombatDamageResolution.cs`: Damage Bonus, weapon/profile records, explicit roll records, pure engine, pure calculation result.
- Modify `multiplayer/server/src/Trpg.Multiplayer.Api/Gameplay/CheckResolution.cs`: add narrow generic `RollDice` to the existing secure `IDiceRoller`.
- Create `multiplayer/server/tests/Fixtures/export-combat-damage-conformance.js`: execute actual Single Player dependencies in a Node VM and emit the deterministic fixture.
- Create `multiplayer/server/tests/Trpg.Multiplayer.Api.Tests/Fixtures/combat-damage.json`: generated 48-case reference fixture.
- Create `multiplayer/server/tests/Trpg.Multiplayer.Api.Tests/Gameplay/CombatDamageResolutionTests.cs`: parser, profile, pure math, and fixture conformance.
- Modify `multiplayer/server/tests/Trpg.Multiplayer.Api.Tests/Gameplay/CheckResolutionTests.cs`: secure generic dice contract and unchanged percentile behavior.
- Modify existing test fakes implementing `IDiceRoller` only as mechanically required to add deterministic `RollDice`.

### Feature Commit 2 — canonical Multiplayer integration

- Modify `multiplayer/server/src/Trpg.Multiplayer.Api/Gameplay/MultiplayerGameState.cs`: narrow canonical investigator loadout and default unarmed profile.
- Modify `multiplayer/server/src/Trpg.Multiplayer.Api/Gameplay/CombatSessionState.cs`: start-snapshotted damage profiles, opponent vitality, status/result registry.
- Modify `multiplayer/server/src/Trpg.Multiplayer.Api/Gameplay/GameContracts.cs`: trusted opponent profile fields, internal resolve command/result, safe snapshot DTO, explicit error/invariant semantics.
- Modify `multiplayer/server/src/Trpg.Multiplayer.Api/Gameplay/IGameCoordinator.cs`: add only internal Combat Damage resolution.
- Modify `multiplayer/server/src/Trpg.Multiplayer.Api/Gameplay/GameCoordinator.cs`: profile snapshot, gate, validation order, server rolls, HP/vitality, exactly-once consumption, repair, termination, commit/broadcast.
- Modify `multiplayer/server/src/Trpg.Multiplayer.Api/Gameplay/HpDamageResolution.cs`: expose one shared `RequiresConRoll` decision used by the existing HP engine and combat application.
- Modify `multiplayer/server/src/Trpg.Multiplayer.Api/Gameplay/GameProjection.cs`: derived safe last-damage summary only.
- Modify `multiplayer/server/tests/Trpg.Multiplayer.Api.Tests/Gameplay/GameStateTests.cs`: canonical profile/gate/consumption/HP/vitality/order/termination/durability tests.
- Modify `multiplayer/server/tests/Trpg.Multiplayer.Api.Tests/Gameplay/HpDamageResolutionTests.cs`: shared CON-requirement boundary.
- Modify `multiplayer/server/tests/Trpg.Multiplayer.Api.Tests/Gameplay/GameApiTests.cs`: public route absence.
- Modify `multiplayer/server/tests/Trpg.Multiplayer.Api.Tests/Realtime/SignalRGameDeliveryTests.cs`: commit-before-broadcast, privacy, reconnect, consumed replay.
- Modify `multiplayer/client/src/contracts/rooms.ts`: safe read-only damage snapshot type.
- Modify `multiplayer/client/src/components/LobbyView.vue`: render safe result/defeated state without actions.
- Modify `multiplayer/client/src/components/HomeView.test.ts`: read-only rendering and forbidden-control assertions.
- Modify `docs/CURRENT_STATE.md`, `docs/HANDOFF.md`, and minimally `docs/ARCHITECTURE.md`: record actual completed Phase 2G facts only after all implementation validation passes.

---

## Feature Commit 1 — `feat: migrate combat damage rules`

### Task 1: Establish RED parser and 48-case conformance contracts

**Files:**
- Create: `multiplayer/server/tests/Trpg.Multiplayer.Api.Tests/Gameplay/CombatDamageResolutionTests.cs`
- Future fixture: `multiplayer/server/tests/Trpg.Multiplayer.Api.Tests/Fixtures/combat-damage.json`

**Interfaces:**
- Consumes: actual JS case schema `{ version, referenceSources, cases }`.
- Produces: failing tests for `DiceExpressionParser`, `CocCombatDamageRules`, `CocCombatDamageEngine`, and the missing fixture.

- [ ] **Step 1: Write compile-time RED tests for the exact public rule surface.**

Use these signatures in the tests; later tasks must implement the same names:

```csharp
var parsed = DiceExpressionParser.Parse("  D6+2 ");
Assert.Equal(new DiceExpression("d6+2", 1, 6, 2), parsed);

var bonus = CocCombatDamageRules.DeriveDamageBonus(65, 60);
Assert.Equal("1d4", bonus.Expression);

var result = new CocCombatDamageEngine().Resolve(new CombatDamageInput(
    Weapon: weapon,
    DamageBonus: bonus,
    Mode: CombatDamageMode.Regular,
    WeaponRoll: new GenericDiceRoll(1, 6, [4], 4),
    DamageBonusRoll: new GenericDiceRoll(1, 4, [3], 3),
    Armor: 2));
Assert.Equal(5, result.NetDamage);
```

- [ ] **Step 2: Add a fixture loader that requires exactly 48 cases and actual source metadata.**

Assert `referenceSources` includes `src/check-engine.js`, `src/combat-opposed.js`, `src/combat-damage.js`, and `src/hp-damage-state.js`. Assert the 48 IDs are unique and every case is labeled `reference_conformance`, `potential_reference_issue`, or `deferred`; do not compare Multiplayer-only behavior as JS expected.

- [ ] **Step 3: Run the focused test and verify RED for missing production types/fixture.**

```powershell
dotnet test multiplayer/server/Trpg.Multiplayer.slnx --no-restore --filter FullyQualifiedName~CombatDamageResolutionTests --nologo -v:minimal
```

Expected RED: compile errors for `DiceExpressionParser`, `CocCombatDamageRules`, and `CocCombatDamageEngine`, or a missing `combat-damage.json`; no unrelated baseline failure.

- [ ] **Step 4: Stop/escalate if RED is caused by an existing baseline failure or if an actual reference boundary contradicts the finalized spec.**

Do not change Single Player or silently reinterpret the approved architecture.

### Task 2: Implement exact dice-expression parsing and one secure generic dice authority

**Files:**
- Create: `multiplayer/server/src/Trpg.Multiplayer.Api/Gameplay/DiceExpression.cs`
- Modify: `multiplayer/server/src/Trpg.Multiplayer.Api/Gameplay/CheckResolution.cs`
- Modify: `multiplayer/server/tests/Trpg.Multiplayer.Api.Tests/Gameplay/CheckResolutionTests.cs`
- Modify: existing `IDiceRoller` fakes in server tests only to satisfy the new method.

**Interfaces:**
- Produces:

```csharp
public sealed record DiceExpression(string Text, int Count, int Faces, int Modifier);
public enum DiceExpressionError { InvalidLength, InvalidFormat, InvalidCount, InvalidFaces, InvalidModifier }
public sealed class DiceExpressionRuleException : ArgumentException {
    public DiceExpressionRuleException(DiceExpressionError error);
    public DiceExpressionError Error { get; }
}
public static class DiceExpressionParser { public static DiceExpression Parse(string input); }
public sealed record DiceRollRequest(int Count, int Faces);
public sealed record GenericDiceRoll(int Count, int Faces, IReadOnlyList<int> RawRolls, int Total);
public interface IDiceRoller {
    PercentileDiceRoll RollPercentile(int bonusDice, int penaltyDice);
    GenericDiceRoll RollDice(DiceRollRequest request);
}
```

- [ ] **Step 1: Add RED tests for parser boundaries and generic dice.**

Cover `d3`, `1d3`, `1d6+2`, `2d4-1`, trim/lowercase normalization, empty, malformed, count 0/101, faces 1/10001, modifier -100000/+100000 accepted, -100001/+100001 rejected, and 33-character input rejected. For `SecureDiceRoller.RollDice(new(100, 10000))`, assert exactly 100 raw values, every value 1..10000, and `Total == RawRolls.Sum()`.

- [ ] **Step 2: Run focused tests and verify expected RED.**

```powershell
dotnet test multiplayer/server/Trpg.Multiplayer.slnx --no-restore --filter "FullyQualifiedName~CombatDamageResolutionTests|FullyQualifiedName~CheckResolutionTests" --nologo -v:minimal
```

Expected RED: missing parser/generic method, not an assertion unrelated to these contracts.

- [ ] **Step 3: Implement the exact reference grammar and bounds.**

Use `^(\d*)d(\d+)([+-]\d+)?$`, maximum input length 32, count 1..100, faces 2..10000, modifier -100000..100000, invariant-culture integer parsing, trim, lowercase, and checked arithmetic. Do not expand the grammar.

- [ ] **Step 4: Implement `SecureDiceRoller.RollDice` with `RandomNumberGenerator.GetInt32(1, Faces + 1)`.**

Validate the request before allocating or rolling. Return raw values and their checked sum. Modifier remains parser/engine-owned and is never rolled. Keep existing percentile implementation and tests unchanged.

- [ ] **Step 5: Update deterministic fakes explicitly.**

Each fake must either dequeue exact generic values or throw `InvalidOperationException("No deterministic generic roll remains.")`; invalid-path tests use a throwing fake so accidental RNG fails the test.

- [ ] **Step 6: Run focused tests.**

Use the Step 2 command. Expected: parser and generic dice tests pass; Combat Damage engine tests remain RED only for the still-missing engine.

- [ ] **Step 7: Stop/escalate if exact parser parity cannot be achieved without broad changes to `check-engine.js` or if generic dice requires a second RNG authority.**

### Task 3: Implement the pure deterministic Combat Damage engine

**Files:**
- Create: `multiplayer/server/src/Trpg.Multiplayer.Api/Gameplay/CombatDamageResolution.cs`
- Modify: `multiplayer/server/tests/Trpg.Multiplayer.Api.Tests/Gameplay/CombatDamageResolutionTests.cs`

**Interfaces:**
- Produces:

```csharp
public enum DamageBonusKind { Flat, Dice }
public enum CombatDamageMode { Regular, InitiatorExtremeEligible, FightBackRegularCap }
public sealed record DamageBonusProfile(int Sum, DamageBonusKind Kind, int FlatValue, int Count, int Faces, string Expression, int Maximum);
public sealed record CombatWeaponProfile(string WeaponId, string Label, DiceExpression Damage, bool AddsDamageBonus, string Mode);
public sealed record CombatDamageInput(CombatWeaponProfile Weapon, DamageBonusProfile DamageBonus, CombatDamageMode Mode, GenericDiceRoll? WeaponRoll, GenericDiceRoll? DamageBonusRoll, int Armor);
public sealed record DamageComponentResult(string Expression, IReadOnlyList<int> RawRolls, int Modifier, int Total, bool Maximized);
public sealed record CombatDamageCalculation(DamageComponentResult WeaponResult, DamageComponentResult DamageBonusResult, int GrossDamage, int Armor, int NetDamage);
public interface ICombatDamageEngine { CombatDamageCalculation Resolve(CombatDamageInput input); }
public sealed class CocCombatDamageEngine : ICombatDamageEngine { public CombatDamageCalculation Resolve(CombatDamageInput input); }
public static class CocCombatDamageRules {
    public static DamageBonusProfile DeriveDamageBonus(int str, int siz);
    public static CombatWeaponProfile NormalizeWeapon(string weaponId, string label, string expression, bool addsDamageBonus, string mode);
}
```

- [ ] **Step 1: Write RED unit tests for all deterministic paths.**

Cover DB sums 64, 65, 84, 85, 124, 125, 164, 165, 204, 205, 284, 285 and the next 80-point band; supported `melee_non_impaling`; unsupported mode; regular dice DB; flat -1/-2; `AddsDamageBonus=false`; gross floor zero; extreme weapon modifier maximum; positive/zero/negative DB maximum; Fight Back regular cap; Armor partial/equal/above gross; and exact roll count/faces/range mismatch rejection.

- [ ] **Step 2: Run focused tests and verify assertion RED.**

```powershell
dotnet test multiplayer/server/Trpg.Multiplayer.slnx --no-restore --filter FullyQualifiedName~CombatDamageResolutionTests --nologo -v:minimal
```

Expected RED: missing engine/rule implementation or incorrect new assertions only.

- [ ] **Step 3: Implement Damage Bonus and weapon normalization.**

Use the finalized table and `2 + floor((sum - 205) / 80)` above 204. Require STR/SIZ positive and use checked sum/max. Normalize only `melee_non_impaling`; unsupported modes fail before rolls.

- [ ] **Step 4: Implement pure roll validation and regular/Fight Back math.**

For required rolls, validate `Count`, `Faces`, raw count, every raw value, and raw sum. Apply weapon modifier in the component result. Apply flat or rolled DB only when enabled. Use `GrossDamage = Math.Max(0, weapon + db)` and `NetDamage = Math.Max(0, gross - armor)`.

- [ ] **Step 5: Implement extreme math without RNG inputs.**

Require both roll inputs null. Weapon total is `count * faces + modifier`; dice DB is its maximum; flat negative DB stays negative; result metadata records the actual weapon modifier rather than the Single Player metadata inconsistency.

- [ ] **Step 6: Run focused parser/dice/engine tests.**

```powershell
dotnet test multiplayer/server/Trpg.Multiplayer.slnx --no-restore --filter "FullyQualifiedName~CombatDamageResolutionTests|FullyQualifiedName~CheckResolutionTests" --nologo -v:minimal
```

Expected: all non-fixture tests green; fixture test remains RED only until Task 4 generates it.

- [ ] **Step 7: Stop/escalate if the engine needs CharacterState, CombatSession, time, RNG, HTTP, SignalR, Vue, or AI.**

### Task 4: Generate actual-JS fixture, validate, and publish Feature Commit 1

**Files:**
- Create: `multiplayer/server/tests/Fixtures/export-combat-damage-conformance.js`
- Create: `multiplayer/server/tests/Trpg.Multiplayer.Api.Tests/Fixtures/combat-damage.json`
- Modify: `multiplayer/server/tests/Trpg.Multiplayer.Api.Tests/Gameplay/CombatDamageResolutionTests.cs`

**Interfaces:**
- Consumes: actual `src/check-engine.js`, `src/hp-damage-state.js`, `src/health-stabilization.js`, `src/combat-opposed.js`, and `src/combat-damage.js` through the established VM harness.
- Produces: deterministic UTF-8 JSON with exactly 48 cases and no current-time semantic fields.

- [ ] **Step 1: Implement the VM exporter by adapting the existing opposed/stabilization harness, not by copying expected math.**

Expose thin helpers that call actual `parseDiceExpression`, `combatDamageBonusProfile`, `combatDamageNormalizeWeapon`, `combatDamageResolveAmount`, and controlled `combatDamageApplyDisposition`. Forced roller callbacks provide stable raw values; expected values come only from actual functions.

- [ ] **Step 2: Emit exactly these 48 stable case IDs.**

```text
db-64, db-65, db-84, db-85, db-124, db-125,
db-164, db-165, db-204, db-205, db-284, db-285,
dice-d3, dice-1d3, dice-1d6-plus-2, dice-2d4-minus-1,
dice-trim-uppercase, dice-empty, dice-invalid-format, dice-count-zero,
dice-count-101, dice-faces-one, dice-faces-10001,
dice-modifier-min, dice-modifier-below-min, dice-modifier-max,
dice-modifier-above-max, dice-length-33,
weapon-melee-non-impaling, weapon-unsupported-mode,
regular-dice-db, regular-flat-negative-db, regular-gross-floor-zero,
regular-adds-db-false, extreme-weapon-modifier-max,
extreme-positive-db-max, extreme-zero-db, extreme-negative-db,
fight-back-regular-cap, armor-partial, armor-equal-gross,
armor-above-gross, zero-net-no-hp-event, investigator-positive-hp,
major-wound-after-armor, instant-death-after-armor,
opponent-defeat, target-already-defeated-reference-issue
```

Label browser-only/result-storage behavior accurately; `target-already-defeated-reference-issue` must preserve the actual reference's `applied=false`, unchanged revision, and still-pending disposition rather than writing the Multiplayer generalization into JS expected.

- [ ] **Step 3: Generate twice and require byte-identical SHA.**

```powershell
node multiplayer/server/tests/Fixtures/export-combat-damage-conformance.js
$first=(Get-FileHash multiplayer/server/tests/Trpg.Multiplayer.Api.Tests/Fixtures/combat-damage.json -Algorithm SHA256).Hash
node multiplayer/server/tests/Fixtures/export-combat-damage-conformance.js
$second=(Get-FileHash multiplayer/server/tests/Trpg.Multiplayer.Api.Tests/Fixtures/combat-damage.json -Algorithm SHA256).Hash
if($first -ne $second){throw "Combat Damage fixture is not deterministic"}
$fixture=Get-Content -Raw multiplayer/server/tests/Trpg.Multiplayer.Api.Tests/Fixtures/combat-damage.json | ConvertFrom-Json
if($fixture.cases.Count -ne 48){throw "Expected 48 cases, got $($fixture.cases.Count)"}
"COMBAT_DAMAGE_CASES=48 SHA256=$first"
```

Expected: both hashes identical and case count exactly 48.

- [ ] **Step 4: Run focused fixture conformance.**

```powershell
dotnet test multiplayer/server/Trpg.Multiplayer.slnx --no-restore --filter FullyQualifiedName~CombatDamageResolutionTests --nologo -v:minimal
```

Expected: all portable parser/profile/math cases pass. Potential reference issues remain labeled, not forced into Multiplayer semantics.

- [ ] **Step 5: Run Feature Commit 1 full server validation.**

```powershell
dotnet restore multiplayer/server/Trpg.Multiplayer.slnx --nologo
dotnet build multiplayer/server/Trpg.Multiplayer.slnx --no-restore --nologo -v:minimal
dotnet test multiplayer/server/Trpg.Multiplayer.slnx --no-restore --nologo -v:minimal
dotnet format multiplayer/server/Trpg.Multiplayer.slnx --verify-no-changes --no-restore --verbosity minimal
```

Expected: restore/build/tests/format all pass. Record exact total server count and Combat Damage fixture SHA.

- [ ] **Step 6: Enforce Commit 1 scope.**

`git diff --name-only` may contain only the Commit 1 file map. It must contain no `GameCoordinator.cs`, `CombatSessionState.cs`, `MultiplayerGameState.cs`, `GameProjection.cs`, `GameApi.cs`, realtime, client, `src/`, `build/`, or `outputs/` change.

- [ ] **Step 7: Commit and push Feature Commit 1.**

```powershell
git add multiplayer/server/src/Trpg.Multiplayer.Api/Gameplay/DiceExpression.cs multiplayer/server/src/Trpg.Multiplayer.Api/Gameplay/CombatDamageResolution.cs multiplayer/server/src/Trpg.Multiplayer.Api/Gameplay/CheckResolution.cs multiplayer/server/tests/Fixtures/export-combat-damage-conformance.js multiplayer/server/tests/Trpg.Multiplayer.Api.Tests/Fixtures/combat-damage.json multiplayer/server/tests/Trpg.Multiplayer.Api.Tests
git diff --cached --check
git commit -m "feat: migrate combat damage rules"
git push origin HEAD:main
git rev-list --left-right --count origin/main...HEAD
```

Expected: exactly one feature commit, push without force, `HEAD == origin/main`, divergence `0 0`. Stop before Commit 2 if scope, tests, hash, or sync fails.

---

## Feature Commit 2 — `feat: integrate multiplayer combat damage`

### Task 5: Add canonical profiles and fail-closed Combat start snapshots

**Files:**
- Modify: `multiplayer/server/src/Trpg.Multiplayer.Api/Gameplay/MultiplayerGameState.cs`
- Modify: `multiplayer/server/src/Trpg.Multiplayer.Api/Gameplay/CombatSessionState.cs`
- Modify: `multiplayer/server/src/Trpg.Multiplayer.Api/Gameplay/GameContracts.cs`
- Modify: `multiplayer/server/src/Trpg.Multiplayer.Api/Gameplay/GameCoordinator.cs`
- Modify: `multiplayer/server/tests/Trpg.Multiplayer.Api.Tests/Gameplay/GameStateTests.cs`

**Interfaces:**
- Produces:

```csharp
public sealed record CharacterCombatLoadout(CombatWeaponProfile Weapon, int FixedArmor);
public sealed record CombatDamageProfile(int Str, int Siz, DamageBonusProfile DamageBonus, CombatWeaponProfile Weapon, int FixedArmor);
public sealed record OpponentVitalityState(int CurrentHp, int MaxHp);

internal sealed record OpponentDefinition(
    string Label, int Dex, int Fighting, int Dodge,
    IReadOnlyList<CombatResponse> AvailableResponses,
    int ResponseAllowance, string ResponsePolicy,
    int Str, int Siz, int CurrentHp, int MaxHp,
    int FixedArmor, CombatWeaponProfile Weapon);
```

`CharacterState` gains `CombatLoadout`; its compatibility constructor assigns canonical unarmed (`unarmed`, `徒手/拳脚`, `1d3`, adds DB, `melee_non_impaling`) and Armor 0. `CombatParticipantState` gains an immutable `DamageProfile` and optional `OpponentVitality`.

- [ ] **Step 1: Write RED start tests.**

Assert investigators require canonical `str` and `siz` in 1..100, use the narrow character loadout, and snapshot DEX/Fighting/Dodge/STR/SIZ/DB/weapon/Armor. Assert missing/invalid keys fail without dice/revision. Assert opponent profiles require STR/SIZ 1..999, `CurrentHp > 0`, `MaxHp >= CurrentHp`, Armor 0..99, supported weapon, and no legacy fallback.

- [ ] **Step 2: Add snapshot immutability RED.**

Start Combat, replace the source CharacterState loadout/check dictionary in a controlled test seam, and assert participant STR/SIZ/DB/weapon/Armor remain the start snapshot.

- [ ] **Step 3: Run RED.**

```powershell
dotnet test multiplayer/server/Trpg.Multiplayer.slnx --no-restore --filter "FullyQualifiedName~GameStateTests.InternalCombat_Start|FullyQualifiedName~GameStateTests.CombatDamageProfile" --nologo -v:minimal
```

Expected RED: missing fields/constructor validation only.

- [ ] **Step 4: Implement the narrow profile model and Start validation.**

Derive DB through `CocCombatDamageRules.DeriveDamageBonus`; never accept DB directly. Copy normalized immutable records into participants. No resolve command field and no mid-combat live lookup may override a profile.

- [ ] **Step 5: Run focused tests and all existing Phase 2F GameState start tests.**

Expected: new and prior start/order/authorization tests pass without opponent defaults in production code.

- [ ] **Step 6: Stop/escalate if a profile requires a public loadout editor, full inventory, or full NPC health domain.**

### Task 6: Replace disposition booleans and enforce the causal gate

**Files:**
- Modify: `multiplayer/server/src/Trpg.Multiplayer.Api/Gameplay/CombatSessionState.cs`
- Modify: `multiplayer/server/src/Trpg.Multiplayer.Api/Gameplay/GameContracts.cs`
- Modify: `multiplayer/server/src/Trpg.Multiplayer.Api/Gameplay/GameCoordinator.cs`
- Modify: `multiplayer/server/tests/Trpg.Multiplayer.Api.Tests/Gameplay/GameStateTests.cs`

**Interfaces:**
- Produces:

```csharp
public enum DamageDispositionStatus { Pending, Consumed }
public enum CombatDamageOutcome { Applied, TargetAlreadyIneligible }
public sealed record DamageDispositionData(string ExchangeId, CombatParticipantId OwnerParticipantId, CombatParticipantId TargetParticipantId, CombatDamageMode Mode, long CreatedGameRevision);
public sealed record DamageDispositionState(DamageDispositionData Disposition, DamageDispositionStatus Status, CombatDamageResult? Result);
```

The registry becomes `IReadOnlyDictionary<string, DamageDispositionState> DamageDispositions`. `Status` is the only canonical pending/consumed truth; remove `Pending` and `HpCommitted` rather than retaining contradictory booleans.

- [ ] **Step 1: Write RED migration/durability tests.**

Assert ResolvePendingExchange registers `Pending` with `Result=null` and `CreatedGameRevision` equal to the committed resolve revision. Assert no-hit creates no entry. Assert registry entries survive `LastExchange` replacement and 120-history trim.

- [ ] **Step 2: Write RED causal gate tests.**

After a damage-eligible Resolve advances turn, assert Begin, Pass, normal progression, and manual End return `PendingConflict` before dice and without revision. Trusted End may still cancel an unresolved `PendingCombatExchange` that has not created a disposition.

- [ ] **Step 3: Write RED legacy multi-pending order tests.**

Construct multiple Pending entries and define the blocker as lowest `CreatedGameRevision`, then ordinal `ExchangeId` tie-break. Resolving a later entry first fails before dice. Normal new flow never creates a second Pending entry.

- [ ] **Step 4: Run RED.**

```powershell
dotnet test multiplayer/server/Trpg.Multiplayer.slnx --no-restore --filter "FullyQualifiedName~GameStateTests.InternalCombat|FullyQualifiedName~GameStateTests.CombatDamageGate" --nologo -v:minimal
```

Expected RED: old booleans/registry and missing gate behavior.

- [ ] **Step 5: Implement the single status model and gate helper.**

Add a pure coordinator helper that returns the deterministic first Pending entry. Call it in Begin, Pass, and End before any action/dice. Preserve unresolved PendingExchange End cancellation exactly.

- [ ] **Step 6: Run focused tests.**

Expected: disposition migration, history durability, gate, manual End distinction, and legacy order pass.

- [ ] **Step 7: Stop/escalate if implementing the gate requires deleting consumed entries or erasing a resolved hit.**

### Task 7: Implement validation order, stale consumed replay, and post-RNG failure policy

**Files:**
- Modify: `multiplayer/server/src/Trpg.Multiplayer.Api/Gameplay/GameContracts.cs`
- Modify: `multiplayer/server/src/Trpg.Multiplayer.Api/Gameplay/IGameCoordinator.cs`
- Modify: `multiplayer/server/src/Trpg.Multiplayer.Api/Gameplay/GameCoordinator.cs`
- Modify: `multiplayer/server/tests/Trpg.Multiplayer.Api.Tests/Gameplay/GameStateTests.cs`

**Interfaces:**
- Produces:

```csharp
internal sealed record ResolveCombatDamageCommand(Guid RoomId, long ExpectedGameRevision, string ExchangeId);
internal sealed record ResolveCombatDamageResult(MultiplayerGameState State, CombatDamageResult Damage);
internal interface IInternalCombatResolutionCoordinator {
    // existing methods retained
    Task<GameResult<ResolveCombatDamageResult>> ResolveCombatDamageAsync(ResolveCombatDamageCommand command);
}
```

- [ ] **Step 1: Write the blocking stale-consumed replay RED test.**

Consume with `ExpectedRevision=N`, obtaining revision `N+1`. Retry the same ExchangeId with `ExpectedRevision=N`. Assert success, identical immutable result, `Changed=false`, unchanged revision, and zero additional generic/percentile calls, HP calls, vitality writes, repair calls, store replacements, notifier calls, or history entries. Assert it is not `StateConflict`.

- [ ] **Step 2: Write Pending stale/missing/wrong-blocker/malformed-profile RED tests with throwing dice.**

For each path, assert failure occurs before `RollDice`/`RollPercentile`. Pending stale revision remains `StateConflict`; missing and wrong blocker fail closed; malformed status/result is an internal invariant failure.

- [ ] **Step 3: Write `TargetAlreadyIneligible` RED.**

Make the canonical target inactive/dead before consumption. Assert no dice/HP/vitality/second defeat, one Pending-to-Consumed replacement, result outcome `TargetAlreadyIneligible`, revision +1, gate release, and stale-revision replay of the stored result with `Changed=false`.

- [ ] **Step 4: Write post-RNG store-failure RED using a controlled `IGameStateStore` fake.**

The fake accepts setup replacements and rejects the armed consumption replacement after dice. Assert `ResolveCombatDamageAsync` throws or returns a dedicated internal invariant failure that is not `StateConflict`, never auto-retries, and never publishes. The test name must state that rerunning the same Pending ExchangeId is prohibited.

- [ ] **Step 5: Run RED.**

```powershell
dotnet test multiplayer/server/Trpg.Multiplayer.slnx --no-restore --filter "FullyQualifiedName~GameStateTests.ResolveCombatDamage" --nologo -v:minimal
```

Expected RED: method absent or incorrect validation order.

- [ ] **Step 6: Implement this exact lookup order before any RNG.**

```text
room/game/internal interface authority
-> registry
-> exact ExchangeId
-> if Consumed: immutable result, Changed=false, return before revision
-> Pending expected revision
-> deterministic blocker
-> owner/target/profile/mode/expression/STR/SIZ/Armor/eligibility/roll-plan validation
-> TargetAlreadyIneligible no-RNG consumption OR begin RNG
```

- [ ] **Step 7: Implement post-RNG commit as an invariant boundary.**

After the first `RollDice`/required `RollPercentile`, a failed `TryReplace` must throw a dedicated internal exception such as `CombatDamageCommitInvariantException`. Do not translate it to `GameErrorCode.StateConflict`, do not loop, and do not call the notifier. Document in the exception message that the ExchangeId must not be re-rolled. If the current locking/storage implementation cannot guarantee this classification, stop and escalate rather than weakening exactly-once.

- [ ] **Step 8: Run focused tests.**

Expected: stale Consumed replay, Pending stale failure, no-RNG paths, and post-RNG failure classification all pass.

### Task 8: Integrate HP, opponent vitality, order repair, and termination atomically

**Files:**
- Modify: `multiplayer/server/src/Trpg.Multiplayer.Api/Gameplay/HpDamageResolution.cs`
- Modify: `multiplayer/server/src/Trpg.Multiplayer.Api/Gameplay/GameCoordinator.cs`
- Modify: `multiplayer/server/tests/Trpg.Multiplayer.Api.Tests/Gameplay/HpDamageResolutionTests.cs`
- Modify: `multiplayer/server/tests/Trpg.Multiplayer.Api.Tests/Gameplay/GameStateTests.cs`

**Interfaces:**
- Produces:

```csharp
public sealed record CombatDamageResult(
    string ExchangeId,
    CombatParticipantId OwnerParticipantId,
    CombatParticipantId TargetParticipantId,
    CombatDamageMode DamageMode,
    CombatDamageOutcome Outcome,
    string? WeaponId,
    string? WeaponExpression,
    DamageComponentResult? WeaponResult,
    DamageComponentResult? DamageBonusResult,
    int GrossDamage,
    int Armor,
    int NetDamage,
    int HpBefore,
    int HpAfter,
    bool HpDamageApplied,
    bool TargetDefeated,
    DateTimeOffset ResolvedAt);

public static bool CocHpDamageEngine.RequiresConRoll(CharacterHealthState state, int damage);
```

- [ ] **Step 1: Write RED shared HP-boundary tests.**

Assert `RequiresConRoll` is false for zero/ordinary positive/instant-death damage and true only for positive non-instant damage at or above `MajorWoundThreshold`. Refactor `CocHpDamageEngine.Apply` to call this helper so Combat does not copy the threshold.

- [ ] **Step 2: Write investigator flow RED tests.**

Cover ordinary HP loss, Major Wound success/failure, unconsciousness, Dying, instant death, stale stabilization invalidation, deterministic `combat:{ExchangeId}` key, and no second HP event on replay. Assert zero net skips HP and CON. Assert below-threshold positive damage gets no CON roll; threshold damage gets exactly one secure percentile roll.

- [ ] **Step 3: Write investigator participation RED tests.**

Assert HP 0 + Dying remains active and scheduled; only `CharacterHealthState.Dead` inactivates and clears scheduling. One dead investigator with another active does not end combat; all inactive/dead investigators end `investigators_defeated`.

- [ ] **Step 4: Write opponent vitality RED tests.**

Assert positive net subtracts with floor zero; zero does not change vitality; zero HP marks participant inactive; no CharacterHealthState, Major Wound, Dying, Stabilization, or CON roll exists; immutable profile fields do not change; all opponents inactive ends `opposition_defeated` while one remaining opponent continues.

- [ ] **Step 5: Write order and wrap RED tests.**

For `A -> B -> C`, after A's opposed action has advanced to B, defeat B and assert current becomes C by scanning from B's same index. Cover inactive first/middle/last slots. Cover an opposed resolve that already wrapped before damage: consumption must not increment round again, reset counts twice, or duplicate dying checks. Cover the case that repair genuinely reaches end and invokes exactly one canonical wrap.

- [ ] **Step 6: Run RED.**

```powershell
dotnet test multiplayer/server/Trpg.Multiplayer.slnx --no-restore --filter "FullyQualifiedName~HpDamageResolutionTests|FullyQualifiedName~GameStateTests.ResolveCombatDamage|FullyQualifiedName~GameStateTests.CombatDamageOrder" --nologo -v:minimal
```

- [ ] **Step 7: Implement the atomic application.**

Build the pure calculation from canonical profiles and server generic rolls. For investigator positive damage, compute `ConRoll` only through `RequiresConRoll`, then call `hpDamageEngine.Apply` directly inside the held lock with `new HpDamageInput($"combat:{exchangeId}", netDamage, conRoll)`. For opponent damage, replace only vitality. Store one immutable result, status Consumed, participant/activity/schedule repair, termination, and one revision in one replacement.

- [ ] **Step 8: Implement stable-order repair.**

Never delete `Order` or `Participants`. If the already-advanced current slot is inactive, scan from the same index. Leave a valid active current actor unchanged. Wrap only when no active index remains from the current position, using the existing round/dying path exactly once.

- [ ] **Step 9: Run focused tests.**

Expected: HP, opponent vitality, Dying/death distinction, event identity, order repair, wrap, and termination tests pass.

- [ ] **Step 10: Stop/escalate if applying HP requires a second commit, copied HP thresholds, eager CON RNG, or fake NPC health conditions.**

### Task 9: Add safe projection, realtime/reconnect, and route-absence protection

**Files:**
- Modify: `multiplayer/server/src/Trpg.Multiplayer.Api/Gameplay/GameContracts.cs`
- Modify: `multiplayer/server/src/Trpg.Multiplayer.Api/Gameplay/GameProjection.cs`
- Modify: `multiplayer/server/tests/Trpg.Multiplayer.Api.Tests/Gameplay/GameStateTests.cs`
- Modify: `multiplayer/server/tests/Trpg.Multiplayer.Api.Tests/Gameplay/GameApiTests.cs`
- Modify: `multiplayer/server/tests/Trpg.Multiplayer.Api.Tests/Realtime/SignalRGameDeliveryTests.cs`

**Interfaces:**
- Produces:

```csharp
public sealed record CombatDamageSnapshot(
    string ExchangeId,
    string OwnerParticipantId,
    string TargetParticipantId,
    string Outcome,
    int NetDamage,
    bool TargetDefeated);
```

Add nullable `LastDamage` to `CombatSnapshot`. It is derived from the latest consumed canonical result; it is not the consumption authority.

- [ ] **Step 1: Write RED projection matrix tests.**

Owners retain existing exact own Health. Combat participants receive only ExchangeId, safe owner/target participant IDs, outcome, net damage, and defeated state. Assert serialized JSON excludes raw weapon/DB rolls, opponent STR/SIZ/DB, exact opponent HP/MaxHp, Armor, full weapon expression/profile, internal registry/result, HP event key, source IDs, provenance, and histories. Room nonparticipant still gets `Combat=null`.

- [ ] **Step 2: Write RED commit/broadcast and stale replay tests.**

Successful consumption commits revision/result/repair first, then publishes one viewer-safe snapshot. Consumed replay with stale revision publishes zero events. Post-RNG store failure publishes zero. Cross-room clients receive nothing.

- [ ] **Step 3: Write RED reconnect tests.**

Disconnect, consume internally, reattach same session, and assert latest own Health, safe defeated state, repaired current actor/order, safe consumed summary, and no dice/revision during AttachSession.

- [ ] **Step 4: Write public API absence tests.**

Enumerate endpoint route patterns and assert no `combat`, `damage`, `resolve-damage`, `weapon`, `armor`, Attack, Dodge, Fight Back, Pass, Start, or End gameplay route exists. `GameApi` must remain initialize/get/check only; public Combat API count is zero.

- [ ] **Step 5: Run RED.**

```powershell
dotnet test multiplayer/server/Trpg.Multiplayer.slnx --no-restore --filter "FullyQualifiedName~GameStateTests.Projection|FullyQualifiedName~SignalRGameDeliveryTests|FullyQualifiedName~GameApiTests" --nologo -v:minimal
```

- [ ] **Step 6: Implement only the derived DTO/projection and existing notifier flow.**

Do not serialize `CombatSession`, `DamageDispositionState`, `CombatDamageResult`, or raw component records. Do not add a route or damage-specific event stream.

- [ ] **Step 7: Run focused tests.**

Expected: privacy, commit-before-broadcast, cross-room isolation, stale replay silence, reconnect, and route absence pass.

- [ ] **Step 8: Stop/escalate on any need to expose opponent internals or add a public action to make tests pass.**

### Task 10: Add read-only Vue summary, validate all products, document, and publish Feature Commit 2

**Files:**
- Modify: `multiplayer/client/src/contracts/rooms.ts`
- Modify: `multiplayer/client/src/components/LobbyView.vue`
- Modify: `multiplayer/client/src/components/HomeView.test.ts`
- Modify: `docs/CURRENT_STATE.md`
- Modify: `docs/HANDOFF.md`
- Modify: `docs/ARCHITECTURE.md`

**Interfaces:**
- Consumes: `CombatSnapshot.lastDamage?: CombatDamageSnapshot | null`.
- Produces: read-only safe summary and actual post-validation documentation facts.

- [ ] **Step 1: Write RED client tests.**

Render a snapshot with safe last damage and defeated participant. Assert semantic owner/target, net damage, defeated label, own existing HP, and current actor render. Assert no button/input/text or API call matches `Roll Damage|Apply Damage|Damage Input|Weapon|Armor|Attack|Dodge|Fight Back|Pass|Start Combat|End Combat`; assert no client dice or damage calculation function exists.

- [ ] **Step 2: Run RED.**

```powershell
Push-Location multiplayer/client
npm test -- --run src/components/HomeView.test.ts
Pop-Location
```

Expected RED: missing type/summary rendering only.

- [ ] **Step 3: Implement the read-only contract and rendering.**

Use only server fields. Do not infer damage, HP, defeat, Armor, or turn from local values and do not add an event handler/API method.

- [ ] **Step 4: Run full client validation.**

```powershell
Push-Location multiplayer/client
npm ci
npm test -- --run
npm run build
Pop-Location
```

Expected: clean install, all Vitest tests, and Vite build pass. Record exact client count.

- [ ] **Step 5: Run full server validation.**

```powershell
dotnet restore multiplayer/server/Trpg.Multiplayer.slnx --nologo
dotnet build multiplayer/server/Trpg.Multiplayer.slnx --no-restore --nologo -v:minimal
dotnet test multiplayer/server/Trpg.Multiplayer.slnx --no-restore --nologo -v:minimal
dotnet format multiplayer/server/Trpg.Multiplayer.slnx --verify-no-changes --no-restore --verbosity minimal
```

Expected: all pass. Record exact server test count, warnings, and errors.

- [ ] **Step 6: Verify every conformance fixture count and regenerate Combat Damage twice.**

```powershell
$expect=@{
  'check-resolution.json'=19
  'hp-damage.json'=10
  'health-stabilization.json'=21
  'combat-opposed.json'=19
  'combat-damage.json'=48
}
foreach($entry in $expect.GetEnumerator()){
  $json=Get-Content -Raw (Join-Path 'multiplayer/server/tests/Trpg.Multiplayer.Api.Tests/Fixtures' $entry.Key) | ConvertFrom-Json
  if($json.cases.Count -ne $entry.Value){throw "$($entry.Key): expected $($entry.Value), got $($json.cases.Count)"}
}
node multiplayer/server/tests/Fixtures/export-combat-damage-conformance.js
$sha1=(Get-FileHash multiplayer/server/tests/Trpg.Multiplayer.Api.Tests/Fixtures/combat-damage.json -Algorithm SHA256).Hash
node multiplayer/server/tests/Fixtures/export-combat-damage-conformance.js
$sha2=(Get-FileHash multiplayer/server/tests/Trpg.Multiplayer.Api.Tests/Fixtures/combat-damage.json -Algorithm SHA256).Hash
if($sha1 -ne $sha2){throw 'Combat Damage fixture SHA mismatch'}
```

Expected: Check 19, HP 10, Stabilization 21, Combat Opposed 19, Combat Damage 48, and identical Combat Damage SHA.

- [ ] **Step 7: Run all 37 authoritative Single Player regressions.**

```powershell
$regressions=@(Get-ChildItem build -File -Filter 'test-*.js' | Sort-Object Name)
if($regressions.Count -ne 37){throw "Expected 37 regressions, got $($regressions.Count)"}
foreach($test in $regressions){node $test.FullName; if($LASTEXITCODE -ne 0){throw "Regression failed: $($test.Name)"}}
```

Expected: all 37 scripts exit 0, including v1.6.9 48 PASS / 0 FAIL and the overall authoritative offline baseline.

- [ ] **Step 8: Run all 69 JS syntax checks.**

```powershell
$scripts=@(Get-ChildItem src,build -Recurse -File -Filter '*.js' | Sort-Object FullName)
if($scripts.Count -ne 69){throw "Expected 69 JS files, got $($scripts.Count)"}
foreach($script in $scripts){node --check $script.FullName; if($LASTEXITCODE -ne 0){throw "Syntax failed: $($script.FullName)"}}
```

Expected: 69/69 pass.

- [ ] **Step 9: Verify formal artifact unchanged through build/verify/double build.**

```powershell
$expected='0A635D94CDD7284B35433092C834D92BCAD44961C14E30DBD565AB48B7E14D4D'
$before=(Get-FileHash outputs/trpg-dm-assistant.html -Algorithm SHA256).Hash
if($before -ne $expected){throw "Unexpected formal baseline $before"}
node build/build-single-html.js
node build/verify-single-html.js
$first=(Get-FileHash outputs/trpg-dm-assistant.html -Algorithm SHA256).Hash
node build/build-single-html.js
$second=(Get-FileHash outputs/trpg-dm-assistant.html -Algorithm SHA256).Hash
if($first -ne $second -or $second -ne $expected){throw "Formal artifact changed: $first / $second"}
$htmlCount=@(Get-ChildItem outputs -File -Filter '*.html').Count
if($htmlCount -ne 1){throw "Expected one formal HTML, got $htmlCount"}
git diff --exit-code -- outputs/trpg-dm-assistant.html
```

Expected: `VERIFY_SINGLE_HTML:PASS`, identical double-build SHA, exact approved SHA, one HTML, no output diff. If baseline differs before build, stop and explain; do not accept a new SHA silently.

- [ ] **Step 10: Update dynamic docs only with actual evidence.**

Record final class/method names, fixture SHA/count, server/client counts, both feature SHAs, internal-only route absence, privacy, retry semantics, and unchanged formal SHA. Do not claim public gameplay readiness, Firearms/Impaling, persistence, Scenario, or AI gameplay.

- [ ] **Step 11: Perform final scope/non-goal/UTF-8 review.**

Run `git diff --check`, strict UTF-8 decoding for every changed text file, and `git diff --stat`. Confirm no Single Player source/build/output change, no fixture drift after regeneration, no public route, no action controls, no duplicate disposition truth source, and no deferred feature symbol outside documentation/tests asserting absence.

- [ ] **Step 12: Commit and push Feature Commit 2.**

```powershell
git add multiplayer/server/src multiplayer/server/tests multiplayer/client/src docs/CURRENT_STATE.md docs/HANDOFF.md docs/ARCHITECTURE.md
git diff --cached --check
git commit -m "feat: integrate multiplayer combat damage"
git push origin HEAD:main
git status -sb
git rev-parse HEAD
git rev-parse origin/main
git rev-list --left-right --count origin/main...HEAD
```

Expected: exactly the second feature commit, clean workspace, `HEAD == origin/main`, divergence `0 0`. Record both feature SHAs and stop. Do not begin Firearms/Impaling or another gameplay slice.

## Final Review Gate

Before declaring Phase 2G implemented, the reviewer must be able to answer YES to every item:

- Feature Commit 1 contains only deterministic migration and actual-JS conformance.
- Feature Commit 2 contains canonical integration and at most read-only UI.
- Consumed stale-revision replay returns the same stored result before Pending revision validation.
- Pending stale revision and every ordinary rejection happen before RNG.
- Post-RNG replacement failure is an internal invariant/storage failure, never a normal retry/re-roll.
- TargetAlreadyIneligible consumes once without dice or HP and releases the gate.
- Positive investigator damage uses existing HP semantics and condition truth.
- Zero damage skips HP and CON.
- Opponent vitality is combat-scoped and has no fake investigator conditions.
- Stable Order repair cannot double-turn, skip, or double-wrap.
- One investigator death does not end combat while another remains active.
- Internal registry/raw dice/opponent internals never enter projection.
- Reconnect and duplicate replay never roll.
- Public Combat API and gameplay damage controls remain absent.
- Check 19, HP 10, Stabilization 21, Combat Opposed 19, Combat Damage 48 all pass.
- Server/client/full Single Player/syntax/formal artifact gates all pass.
- Firearms, Impaling, and every hard non-goal remain deferred.

If any answer is NO, do not create or push the corresponding feature commit. Fix only an in-scope defect with its test, or stop and escalate the exact blocker.
