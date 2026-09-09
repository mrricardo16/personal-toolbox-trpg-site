# Multiplayer Phase 2H Player Combat Intent Protocol Implementation Report

## Scope and outcome

Phase 2H adds only the approved player Combat intent protocol and thin projected controls. Public authority is exactly melee attack, respond, and pass. Start, End, Damage, ResolveDamage, NPC attacker/pass, Host gameplay bypass, PvP, IntentId registry, Firearms/Impaling, weapon switching, movement/range, timeout/forfeit, AI gameplay, Scenario, persistence, database, Redis, matchmaking, and Azure SignalR remain deferred.

No protected Single Player fixture semantics or formal HTML baseline changed. Credentialed real-API tests were not run and were not required.

## Commit chain

- `4df39e26ce35ece7ceb48f3d4775489465131c14` — `feat: snapshot npc combat response policy`
- `45955c7f360df9ada7b7bec816bc34dbf69f3b0e` — `feat: project player combat intent actions`
- `8eb25311577a32ff61a7eced5a2b5058e78bf139` — `feat: define player combat intent coordinator`
- `af9046134689cf4afb7be0b74ce680bb6db75916` — `feat: orchestrate player melee attacks`
- `af0ea836cb7d12dfd4a25fd72e29c775bbb7aa74` — `feat: orchestrate player combat responses`
- `882cfad0643896e308108f055eafec7ac9dc3b2a` — `feat: orchestrate player combat pass`
- `fa8d2333c51c2a2ca2651319a713555d2e8bbb13` — `feat: expose player combat intent routes`
- `f4abdc86279bb6d743ab20d96290a29caac56fcb` — `test: cover player combat intent delivery`
- `b32f685ff6138c66df09f7388f8fcf1043b8ef0b` — `feat: add player combat intent controls`
- `3755ddd093638b368a5b5ab2c491dce20562e05e` — `test: align player combat intent aggregate guards`
- `6d458e2e424c905f5a930e8d7c00f505eba1b5f7` — `test: format combat intent API tests`

The final factual documentation is committed separately as `docs: record player combat intent validation`; its SHA is verified after commit rather than self-embedded in this report.

## Task 1–9 RED/GREEN and review evidence

- Task 1: RED was two `CS1061` errors for the absent typed NPC policy. GREEN passed 46/46. Review was clean.
- Task 2: initial RED was ten compile errors for absent viewer actions; GREEN passed 14/14. Review found inactive combat still actionable (1 failing regression); correction passed 15/15.
- Task 3: initial RED was 3/3 failures for absent coordinator/contract; GREEN passed 3/3. The review-strengthened dependency-category guard was demonstrated with a temporary test probe and returned GREEN after probe removal.
- Task 4: melee orchestration RED failed 12/12; required GREEN passed 29/29 and complete coordinator coverage passed 15/15. Review was clean.
- Task 5: respond orchestration RED failed 11/11; required GREEN passed 15/15 and complete coordinator coverage passed 26/26. Review was clean.
- Task 6: pass orchestration RED failed 7/7; GREEN passed 11/11. Review added a real pending-blocker regression (1 failed, 6 passed), then passed 12/12 after the approved correction.
- Task 7: route RED had 8 failures and one expected forbidden-route pass; GREEN passed 11/11. Review reproduced three unsafe invariant responses and then passed all 3/3 after the safe structured-error boundary correction.
- Task 8: the initial test fixture had four compile errors; no production change was needed. Required selection passed 11/11. Full delivery selection exposed three test-fixture/assertion defects, then passed 27/27. Review correction reruns passed 27/27 plus 6/6 focused privacy/reconnect/partial-state tests.
- Task 9: client RED had 9 failures and 18 passes; GREEN passed 27/27. Review RED had 2 failures and 27 passes; correction passed 29/29. Finite-revision coverage passed 13/13 and the exact focused suite passed 30/30.

## Task 10 aggregate guard and correction evidence

The server already had stronger-than-sample aggregate guards: the exact three-route list and exact two-constructor dependency list. A missing client aggregate source guard was added for public Start/End/Damage/NPC actor APIs and the exact forbidden canonical calculation symbols. Existing behavior remained GREEN; no RED was manufactured:

- server aggregate selection: 21/21 passed;
- client aggregate selection: 31/31 passed.

The first full server gate exposed one test assertion/fixture inconsistency: 395 passed, 1 failed. `MeleeAttackRig.Create(hasPendingDamage: true)` correctly begins with a resolved exchange in History, while `AssertPassFailureWithoutMutation` incorrectly required History to be empty. The narrow test-only correction compares initial and current turn, round, pending exchange, damage dispositions, History, and LastExchange while retaining zero Pass/Begin/Resolve/Damage commands, zero dice/exchange generation/publications, and unchanged revision/state identity.

- exact blocker test after correction: 1/1 passed;
- full `Pass_` family: 7/7 passed;
- full server suite after correction: 396/396 passed.

The initial format verification then reported only six Task 7 anonymous-object property lines. They were wrapped without behavior change in the separate `test: format combat intent API tests` commit. `dotnet format --verify-no-changes --no-restore` then passed.

## Authority, privacy, and state-flow evidence

- `PlayerCombatIntentCoordinator` constructor contains exactly `IGameCoordinator` and `IInternalCombatResolutionCoordinator`; direct `IGameStateStore`, dice, damage engine, notifier/Hub, persistence, and AI dependencies are absent.
- Pre-mutation legality comes from the viewer projection. Canonical internal transitions revalidate state before mutation and dice.
- NPC defender selection uses only the typed private policy in `BeginOpposedExchangeResult.State`.
- Damage selection uses only the exact disposition and returned revision in `ResolvePendingExchangeResult.State`.
- HTTP success returns a fresh viewer-specific `GameSnapshot`, never internal state or transition results.
- A human defender commits Begin only; only the exact owner receives the pending-response affordance.
- Stale requests on all three intents produce no mutation, dice, exchange ID, revision, or broadcast.
- Player identity is bearer-session derived. Request bodies cannot spoof player/owner/requester identity, dice, target facts, policy, damage, HP, weapon/armor, next actor, or round.
- Host identity grants no NPC or other-player gameplay bypass. Same-side and hidden/ineligible targets are rejected.

## Realtime, partial commits, and reconnect evidence

- NPC damage publishes Begin, Resolve, and Damage revisions once each in committed order; no-damage publishes Begin and Resolve; a human defender publishes Begin only.
- The application coordinator publishes zero times, preventing duplicate delivery.
- Viewer-specific snapshots hide NPC policy, allowance, checks/rolls, damage registry, dying schedule, histories, source, and provenance.
- Begin committed plus Resolve pre-RNG failure retains the canonical pending exchange.
- Resolve committed plus Damage pre-RNG failure retains the canonical pending damage disposition.
- A post-RNG damage invariant failure is non-result/non-retryable and never rerolls.
- Lost-success retry with the old revision is stale and cannot duplicate transitions, dice, exchange IDs, revisions, or publication.
- Disconnect performs no response, pass, advance, or damage. Reconnect changes no Game revision and restores the exact owner-only pending response.

## Client authority evidence

Vue renders only `CombatViewerActionsSnapshot` actor, eligible target IDs, response choices, and safe projected summaries. It submits exact melee/respond/pass intents. It contains no dice parsing/rolling, target-legality or defender-ownership calculation, opposed resolution, damage/HP mutation, or turn/round advancement. A stale conflict fetches one authoritative snapshot, displays `Combat changed; choose again`, and never automatically replays the intent.

## Aggregate validation

Commands were run from `E:\personal-toolbox-trpg-site` unless a client working directory is shown.

- Client clean install: `npm ci` passed. It reported the existing `glob@10.5.0` deprecation warning and two moderate audit findings; no forced dependency mutation was authorized.
- Full client tests: `npm test -- --run` passed 39/39 across six files, 0 failed, 0 skipped.
- Client build: `npm run build` passed (`vue-tsc --noEmit` and Vite; 50 modules transformed).
- Server restore: `dotnet restore multiplayer/server/Trpg.Multiplayer.slnx --nologo` passed.
- Server build: passed with 0 warnings and 0 errors.
- Full server tests: passed 396/396, 0 failed, 0 skipped.
- Format: `dotnet format multiplayer/server/Trpg.Multiplayer.slnx --verify-no-changes --no-restore` passed.
- Conformance case counts: Check 19, HP 10, Stabilization 21, Combat Opposed 19, Combat Damage 48.
- Combat Damage exporter: two runs passed; SHA-256 matched exactly at `0036133BF2BF1F37CBEF7DC7707832C258DE869870B46C17ADCA748FE905CEE7`; fixture diff was empty.
- The explicit ordered allowlist matched `.github/workflows/trpg-ci.yml`: 37/37 authoritative offline Single Player regressions passed.
- Legacy `build/test-protocol-stability.js`: passed.
- Credentialed `build/test-real-api-v1513.js` and `build/test-real-api-v152.js`: not run / not required.
- JavaScript syntax: 69/69 passed.
- Formal build/verification: `VERIFY_SINGLE_HTML:PASS`; both builds were 662681 bytes; HTML inventory was exactly one; artifact diff was empty.
- Formal SHA-256 before, first build, and second build: `0A635D94CDD7284B35433092C834D92BCAD44961C14E30DBD565AB48B7E14D4D`.
- Strict UTF-8 decoding passed for every Phase 2H edited text file and evidence report.
- `git diff --check` passed.
- Raw broad forbidden searches self-match only the aggregate test regexes in `HomeView.test.ts`; production-filtered searches return zero coordinator forbidden dependencies, zero forbidden public surfaces, and zero client canonical calculation symbols.
- Public route source and endpoint tests confirm exactly three Combat routes: melee attack, respond, and pass. Start/End/Damage/ResolveDamage remain absent/404.

## Remaining risks and non-goals

The client dependency audit currently reports two moderate findings and a deprecated transitive `glob@10.5.0`; Task 10 did not authorize dependency upgrades. Memory-only server state, reconnect grace/expiry, persistence/scale-out, and all explicitly deferred gameplay remain future work. Real provider acceptance depends on separately configured credentials and was intentionally excluded.

No Sol High escalation was required. No production data, schema migration, destructive operation, credential change, or new concurrency architecture was involved.
