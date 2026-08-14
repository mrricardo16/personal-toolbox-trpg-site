# TRPG DM Assistant v1.5.10 Test Report

## Release identity

- APP_VERSION: `1.5.10`
- Save Schema: `8` (unchanged)
- AI protocol: `1.3` (unchanged)
- Authored Threat Clock: `1.0`
- Product entry: `outputs/trpg-dm-assistant.html`

## Release invariant

v1.5.10 turns scenario-authored threat clocks into a browser-owned rules layer instead of granting the model direct authority over authored pressure.

The invariant is:

> **BLOCK UNSAFE STATE, NOT PLAYER ACTION.**

For authored clocks:

- scenario authors define legal advance / resolve conditions;
- the browser evaluates those conditions against canonical state;
- AI `advanceClock` / `resolveClock` proposals targeting an authored clock are stripped locally instead of becoming a player-facing protocol dead-end;
- other legal operations in the same AI transaction are preserved;
- semantic tags describe committed consequences and do not cause them;
- post-commit checks may resolve a clock immediately, but never use a new canonical commit as an excuse to advance it.

Legacy clocks keep their prior behavior. A scenario without authored clocks still uses the existing generic deterministic pacing fallback, and legacy AI `advanceClock` / `resolveClock` operations remain available.

## Authored rule model

An authored threat clock uses `authored: true` (or explicit authored rules), plus `advanceRules` and / or `resolveRules`.

Supported deterministic rule events are fixed to:

- `stall`
- `semantic`
- `flag`
- `clue`
- `node`
- `tension`
- `turn`

Rules support deterministic controls including:

- `once`
- `cooldownTurns`
- `amount` for advance rules
- `atLeast` / `turns` thresholds where applicable
- Progress Semantic kinds for semantic rules
- canonical flag / clue / node references

The browser validates authored definitions before replacing the currently running scenario. Duplicate clock IDs, invalid rule events, invalid semantic kinds, missing node/clue references, invalid limits, and authored clocks without any advance/resolve rule are rejected before activation.

## Built-in authored example

`scenario-fog-harbor` now includes the first built-in authored clock:

- ID: `harbor-tide`
- Name: `午夜涨潮`
- Max: `4`
- Fictional consequence: rising water cuts off the old breakwater / tidal-cave retreat.

Its authored rules include:

- repeated investigation stall pressure with a cooldown;
- externally committed `THREAT` Progress Semantics as pressure evidence;
- browser-owned resolution when the investigation reaches one of the authored ending nodes.

This replaces generic, model-shaped pressure for that clock with scenario-specific deterministic pressure.

## Canonical timing and idempotency

The authored evaluator has two distinct responsibilities:

1. **Pre-action evaluation** may advance or resolve authored clocks from authored rules.
2. **Post-commit evaluation** is resolution-only. It runs after canonical commits such as an actual node transition, AI canonical state commit, or secret-check outcome, so a newly satisfied resolve condition takes effect immediately in the same turn.

Post-commit evaluation cannot advance an authored clock.

This prevents a visible one-action delay when a player reaches a resolution node after the turn's pre-action pacing has already run.

## Stale clock reference defect found during validation

Early validation exposed repeated same-turn advancement even though rule state appeared to be recorded.

Root cause:

- `advanceThreatClockInDirector()` and `resolveThreatClockInDirector()` normalize director clocks;
- normalization replaces the `clocks` array with newly normalized clock objects;
- the first authored evaluator retained an older clock object reference;
- rule-fire / cooldown / evaluation metadata was therefore written onto a stale object and did not persist in canonical state.

Fix:

- authored evaluation iterates stable clock IDs;
- it reacquires the live canonical clock by ID after any helper that may normalize clocks;
- it uses the live clock returned by advance / resolve helpers;
- a director-level same-turn evaluation gate prevents duplicate pre-action advancement;
- post-commit resolution bypasses the advance gate only for monotonic resolve checks.

Regression coverage verifies same-turn idempotency, cooldown persistence, semantic-event deduplication, and immediate post-commit resolution.

## Legacy `advanceClock` compatibility defect found during validation

The v1.5.10 compatibility test also exposed a pre-existing bug in `ai-protocol.js`:

```text
advanceThreatClockInDirector(..., { clockId, amount, reason })
```

referenced an undefined local variable `reason` inside the legacy `advanceClock` branch, producing `STATE_CHANGE_PARAMETER_INVALID: reason is not defined`.

The branch now reads the optional value from the actual operation payload:

```text
reason: asString(raw.reason, 300)
```

This restores the existing legacy clock operation without granting any additional authority to authored clocks.

## Progress Semantics integration

Authoritative clock effects are recorded through the v1.5.9 browser-owned Progress Semantics layer:

- authored clock advance / trigger → `THREAT`
- authored clock resolve → `RESOLUTION`
- legacy deterministic clock movement is also described as a browser-observed semantic consequence

Authored clock semantic events are excluded from feeding back into authored semantic pressure rules, preventing the clock from recursively advancing itself because its own previous advancement produced `THREAT`.

## v1.5.10 deterministic regression

Test file:

`trpg-dm-assistant/build/test-v1510-authored-threat-clock.js`

Final temporary validation Run `31322522848`: **32 PASS / 0 FAIL**.

Coverage includes:

1. v1.5.10 identity with Schema 8 / protocol 1.3 unchanged.
2. Fixed authored rule event whitelist.
3. Fog Harbor midnight tide is browser-authored.
4. Authored clocks are not accidentally driven by generic legacy pacing.
5. Stall threshold advance.
6. Same-turn pre-action idempotency.
7. Cooldown blocks an early repeat.
8. Cooldown allows a later repeat.
9. External `THREAT` semantic can trigger an authored semantic rule.
10. One semantic event is consumed once.
11. Authored clock's own semantic output cannot recursively trigger itself.
12. Authored advance records `THREAT` evidence.
13. Authored node rule resolves the clock.
14. Reaching max triggers the threat and director event.
15. AI authored `advanceClock` is stripped instead of failing the player turn.
16. AI authored `resolveClock` is stripped instead of failing the player turn.
17. A legal adjacent canonical operation survives authored-clock stripping.
18. Legacy AI `advanceClock` remains functional.
19. Legacy scenarios keep the five-stall generic fallback.
20. Legacy deterministic clock movement enters Progress Semantics.
21. Authored flag rules use canonical browser flags.
22. Authored rule-fire state survives Schema 8 normalization / load.
23. Diagnostics expose authored clock authority and rule state.
24. Duplicate authored clock IDs block activation without replacing the current case.
25. Missing authored node references are rejected.
26. Missing authored clue references are rejected.
27. Invalid Progress Semantic kinds are rejected.
28. An authored clock must define advance and/or resolve rules.
29. Production build loads Authored Threat Clock after Progress Semantics.
30. Real `enterNode()` resolves an authored clock immediately even after same-turn pre-action pacing.
31. Post-commit evaluation is resolution-only and cannot advance a clock from the new node.
32. An AI canonical flag commit can satisfy and immediately resolve an authored rule in the same turn.

## Historical regression compatibility

The final temporary validation run also passed:

- v1.5.9 Progress Semantics: **23 PASS / 0 FAIL**
- v1.5.8 API Response Resilience: **20 PASS / 0 FAIL**
- v1.5.7 Case Integrity / Interaction Availability: **34 PASS / 0 FAIL**
- v1.5.7 canonical assertion state: **7 PASS / 0 FAIL**
- JavaScript syntax: **PASS**
- single HTML verifier: **PASS**
- two consecutive builds: **deterministic**
- exactly one TRPG product HTML: **PASS**

The v1.5.9 release-identity assertion was changed from exact `1.5.9` equality to `>= 1.5.9`; its 23 semantic behavior assertions were not relaxed.

The first permanent PR CI run (`31322736395`) exposed one additional historical-test compatibility defect before the NPC behavior assertions executed: `test-v154-npc-materialization.js` used a regex that accepted one-digit patch versions such as `1.5.9` but rejected the valid newer version `1.5.10`. That release-identity gate was replaced with numeric semantic-version comparison (`>= 1.5.3`). The NPC materialization behavior assertions were not changed.

The permanent suite target increases from 309 to **341 deterministic PASS / 0 FAIL** after adding the 32 v1.5.10 tests.

## Build validation

Final temporary validation Run `31322522848` generated:

- product: `outputs/trpg-dm-assistant.html`
- size: **465644 bytes**
- single HTML verifier: PASS
- deterministic double build: PASS
- JavaScript syntax: PASS
- `git diff --check`: PASS

## Permanent PR CI

After the v1.5.4 historical version-gate correction, permanent PR CI Run `31322827718` completed successfully on code head `d94bae9e898b9f0049a567cc8e349d1d5765fa5d`.

It passed:

- all permanent historical regression groups from security hardening through v1.5.9;
- v1.5.10 Authored Threat Clock regression;
- JavaScript syntax;
- deterministic single-HTML build and verifier.

Result: **SUCCESS**, with the permanent deterministic target at **341 PASS / 0 FAIL**.

## Real API decision

A new real DeepSeek run is not required as a v1.5.10 release gate.

Reason:

- AI protocol remains `1.3`;
- provider transport, prompt contract, and request count are unchanged;
- authored clock decisions are browser-owned deterministic rules, not model-generated classifications;
- AI attempts to directly control authored clocks are explicitly removed before state preparation;
- v1.5.8 real-provider E2E remains the transport / response-resilience coverage for the unchanged request pipeline.

## Release decision

The implementation and formal release files are ready. The final documentation-only head must re-run permanent `TRPG DM Assistant CI`; if that exact head remains green, PR #7 is ready for squash merge. No automatic merge is performed.
