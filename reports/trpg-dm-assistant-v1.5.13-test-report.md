# TRPG DM Assistant v1.5.13 Test Report

## Release identity

- APP_VERSION: `1.5.13`
- Save Schema: `8` (unchanged)
- AI protocol: `1.3` (unchanged)
- Product entry: `outputs/trpg-dm-assistant.html`
- Product size: **500957 bytes**
- New deterministic full-case E2E: `build/test-v1513-full-case-e2e.js`
- New current-runtime real API E2E: `build/test-real-api-v1513.js`

## Release purpose

v1.5.13 is the integration release after the browser-owned rule layers introduced in v1.5.6 through v1.5.12.

Previous releases individually hardened:

- player assertions and action chaining;
- interaction availability;
- case integrity;
- provider response resilience;
- browser-derived Progress Semantics;
- authored threat clocks;
- NPC knowledge boundaries;
- ending / resolution authorization.

v1.5.13 verifies that those rules remain coherent when they are exercised as one complete case rather than as isolated module tests.

The permanent invariant remains:

> **BLOCK UNSAFE STATE, NOT PLAYER ACTION.**

## Deterministic full-case scenario

`test-v1513-full-case-e2e.js` creates a test-only structured case with three locations:

1. hall;
2. study;
3. cellar.

It contains:

- three canonical clues: ledger, blueprint, experiment notes;
- a butler and a captive NPC;
- authored protected facts separating what each NPC knows;
- an authored storm threat clock;
- a resolvable captive-status question;
- an always-available withdrawal ending;
- a protected solved ending requiring canonical evidence, flags, resolved question, resolved threat clock, and browser-derived semantic history.

The test uses the production scenario engine, protocol validator, transaction commit path, Guard layers, Progress Semantics, threat-clock engine, NPC knowledge boundary, and Ending Gate.

## Full-case path covered

The deterministic case explicitly proves this continuous sequence:

1. A player states a completed multi-step result such as already finding the cellar and taking all evidence.
2. Player Assertion / Action Chaining Guard prevents those downstream results from becoming canonical state.
3. Interaction remains playable rather than entering an error dead-end.
4. A no-benefit observation remains a valid action.
5. The first clue is acquired through its formal clue route and generates `DISCOVERY`.
6. The butler attempts to claim a protected fact he does not know; the forbidden claim is stripped while a legal relationship update remains.
7. The player explicitly shares an already revealed clue with the NPC; the browser records only that validated knowledge transfer.
8. A real node proposal is confirmed and commits an actual location transition, generating `ACCESS`.
9. A known but premature ending is proposed; only the ending result is stripped and terminal narration is neutralized.
10. Authored stall pressure advances the threat clock and generates `THREAT`.
11. A second clue is acquired.
12. The butler may retain an authored fact he legitimately knows.
13. The player moves into the cellar and the captive is materialized with only the captive's authored knowledge.
14. The third clue is acquired.
15. The captive-status question is resolved through canonical state.
16. Rescue and threat-control flags are committed through the normal transaction path.
17. The authored clock resolves from the browser-owned flag rule and emits `RESOLUTION`.
18. The final ending gate becomes ready only after all required evidence exists.
19. The AI can create only a pending ending proposal.
20. Player confirmation re-evaluates the browser gate and commits the ending.
21. The final ending creates `RESOLUTION` evidence.
22. NPC knowledge separation remains correct after campaign end.
23. Diagnostics contain Progress Semantics, authored clock, NPC knowledge, and ending-gate state.
24. Schema 8 normalization preserves the completed long-case canonical result.
25. The complete flow never enters a technical `error` dead-end.

## Narrow provider-driven protocol compatibility

The real DeepSeek E2E exposed one repeatable model-shape mismatch that was safe to normalize narrowly.

DeepSeek returned the known legal operation:

```json
{
  "operation": "addRevealedTruth",
  "description": "沈墨亲口证实：地下室存在非法实验。"
}
```

The formal protocol expects the payload field `text`.

Before v1.5.13 this was correctly rejected as `STATE_CHANGE_PARAMETER_INVALID`.

v1.5.13 adds only this compatibility rule inside protocol shape normalization:

- operation must already be exactly `addRevealedTruth`;
- `text` must be absent;
- `description` must be a non-empty string;
- only then is `description` copied to `text`.

This does not authorize a new operation, does not bypass Player Assertion Guard or NPC Knowledge Boundary, and does not make unknown/empty business state changes recoverable.

A deterministic assertion verifies this exact alias.

## Deterministic result

v1.5.13 adds **37 PASS / 0 FAIL**.

Previous permanent total: **421 PASS / 0 FAIL**.

New permanent deterministic total:

**458 PASS / 0 FAIL**.

The successful validation also ran every historical deterministic suite through v1.5.12, JavaScript syntax checks, deterministic double build, strict single-HTML verification, and `git diff --check`.

## Real API architecture

The previous manual real API acceptance was based on the older v1.5.2 stress script and a simplified model-response template.

`test-real-api-v1513.js` instead loads the current runtime and calls the production player action path.

The live path therefore includes:

- `requestPlayerAction()`;
- current system/user context construction;
- `requestStructuredAiJson()`;
- API Response Resilience retry accounting;
- JSON repair when applicable;
- `validateAiResponse()`;
- player assertion / interaction recovery;
- NPC knowledge authorization;
- ending authorization;
- `prepareAiTransaction()` / `commitAiTransaction()`;
- browser dice plus continuation AI call when a public check is requested;
- actual node confirmation when proposed;
- actual ending confirmation when the solved ending is ready.

The test also scans player-visible AI messages for internal backend/protocol identifiers and rejects any technical leakage.

## Real API test isolation

The real E2E explicitly disables `maybeAutoSummarize()` inside its VM harness.

Reason: the test script submits several actions immediately with no human pause. The production background summarizer can still be running when the script fires the next action, correctly causing the product concurrency guard to reject the synthetic overlap with `当前已有请求正在进行`.

That race is a test-harness artifact rather than the property under validation. Production summarization code is not modified by v1.5.13.

The player action, structured response, browser check continuation, provider retry, canonical transaction, and ending paths remain real.

## Real API development runs

### Run 31354801458

- Deterministic suites: PASS.
- Real API: failed because the synthetic script immediately submitted another player action while background auto-summary still held request concurrency.
- Classification: **harness concurrency issue**, not canonical corruption or provider protocol defect.
- Resolution: disable auto-summary only inside the real-E2E VM.

### Run 31355037015

- Deterministic suites: PASS.
- Real API reached a strict `STATE_CHANGE_PARAMETER_INVALID` response.
- Initial diagnostics were insufficient to identify the exact operation payload.
- Classification: **real business-protocol rejection requiring more evidence**.
- Resolution: add strict failure diagnostics to the test harness rather than weakening the protocol blindly.

### Run 31355289895

- Deterministic suites: PASS.
- Real E2E later failed its authored-clock setup assertion because earlier live model turns had changed `sceneTurns / lastProgressTurn / authoredClockLastEvaluationTurn`.
- Classification: **harness state-preparation assumption**.
- Resolution: derive a fresh deterministic stall state from the current live canonical director state before evaluating the authored clock.

### Run 31355431304

- Deterministic suites: PASS.
- Real API reproduced a strict business-protocol failure and captured the raw model response.
- Root cause: DeepSeek emitted legal `addRevealedTruth` with `description` instead of required `text`.
- Browser rejected the malformed state operation; no canonical corruption occurred.
- Resolution: add the narrow known-operation `description -> text` alias described above.

### Run 31355661648 — first final successful real runtime gate

Result:

- model: `deepseek-v4-flash`
- player actions: **8**
- browser public checks: **0**
- ending proposals observed: **1**
- ending confirmations: **1**
- structured requests: **8**
- actual API attempts: **13**
- automatic retries: **5**
- provider empty responses: **6**
- JSON-invalid responses: **0**
- retry exhausted: **1**
- graceful fallbacks observed: **1**
- technical leaks: **0**
- final phase: `campaign_ended`
- ending: `ending-solved`

This run proves that provider empty-content instability can occur repeatedly during a full case while API Response Resilience preserves interaction and canonical state.

### Run 31355896683 — persisted formal-source validation

After enabling write-back for already validated formal release files, the entire validation was run again.

Result:

- model: `deepseek-v4-flash`
- player actions: **8**
- browser public checks: **1**
- ending proposals observed: **1**
- ending confirmations: **1**
- structured requests: **9**
- actual API attempts: **11**
- automatic retries: **2**
- provider empty responses: **3**
- JSON-invalid responses: **0**
- retry exhausted: **1**
- graceful fallbacks observed: **1**
- technical leaks: **0**
- final phase: `campaign_ended`
- ending: `ending-solved`

This second successful run is important because it also exercised the browser public-check continuation path and then committed the validated release files to the branch.

## Real provider interpretation

Across the two final successful runtime runs:

- **16 player actions** completed through the current product runtime;
- **17 structured AI requests** were issued;
- **24 actual API attempts** were required;
- **7 automatic retries** occurred;
- **9 provider empty responses** were observed;
- **2 retry-exhausted graceful fallbacks** were observed;
- **1 browser public check + continuation path** was exercised;
- **2 solved endings** were successfully confirmed;
- **0 technical ID/protocol leaks** were detected;
- **0 final canonical corruption** occurred;
- **0 interaction dead-end** remained at the end of either run.

Provider instability is therefore treated as an observed runtime fact rather than hidden by the release report.

## Permanent workflow changes

### `TRPG DM Assistant CI`

Permanent CI now adds:

```text
v1.5.13 full-case E2E regression
```

The complete deterministic target becomes **458 PASS / 0 FAIL** before syntax and build/verify.

### `TRPG DM Assistant Real API Acceptance`

The manual workflow now runs:

1. API Response Resilience deterministic preflight;
2. v1.5.12 Ending Gate preflight;
3. v1.5.13 full-case deterministic preflight;
4. single-HTML build and verifier;
5. `test-real-api-v1513.js` against `deepseek-v4-flash`.

The old `test-real-api-v152.js` is retained as historical evidence but is no longer the permanent manual acceptance entrypoint.

## Request-count / authority impact

v1.5.13 introduces no extra API request into ordinary gameplay.

The new real-E2E test can generate many requests because it intentionally drives a multi-action test case and exercises provider recovery. That is test infrastructure, not a production gameplay request multiplier.

The narrow `addRevealedTruth.description -> text` normalization does not change AI authority. The browser still performs all existing business, assertion, knowledge, location, clock, and ending authorization.

## Release artifacts

Formal release files include:

- `src/ai-protocol.js` narrow revealed-truth field alias;
- `src/scenarios/library.js` v1.5.13 identity;
- `build/test-v1512-ending-resolution-gate.js` forward-compatible version assertion;
- `build/test-v1513-full-case-e2e.js`;
- `build/test-real-api-v1513.js`;
- permanent CI workflow update;
- permanent Real API workflow update;
- README v1.5.13 release notes;
- regenerated `outputs/trpg-dm-assistant.html`;
- this report.

Temporary v1.5.13 validation and documentation workflows/patchers are removed before the release PR.

## Release gate

Before merge:

1. formal diff must contain no temporary v1.5.13 workflow or patcher;
2. PR must target `main` from `agent/v1.5.13-full-case-e2e`;
3. exact clean PR head must pass permanent `TRPG DM Assistant CI` with **458 PASS / 0 FAIL**, JavaScript syntax, deterministic build and verifier;
4. permanent manual Real API Acceptance must be runnable against the current runtime; the successful temporary current-runtime runs above provide live-provider release evidence;
5. if this report is updated after a clean-head CI run, the new report-only head must receive another permanent CI run;
6. do not merge automatically.

Clean PR-head permanent CI Run `31356432365` on head `9bb634c2dd262dc472c4b4a3ec34d0da5ad8262f` completed successfully: the full **458 PASS / 0 FAIL** deterministic suite, JavaScript syntax, deterministic single-HTML build, and verifier all passed. Because this evidence update changes only the report file and therefore creates a new final head, that final report-only head must receive one additional permanent CI run before merge.
