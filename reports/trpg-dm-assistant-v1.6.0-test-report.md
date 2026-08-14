# TRPG DM Assistant v1.6.0 Test Report

## Release identity

- APP_VERSION: `1.6.0`
- Save Schema: `8`
- AI protocol: `1.3`
- CoC Resolution Engine: `1.0`
- Resolution authority: `browser_coc_resolution`
- Single HTML product: `outputs/trpg-dm-assistant.html`
- Validated product size: `512572 bytes`

v1.6.0 is the first CoC Resolution Engine release. It does **not** claim that all CoC rules, combat, SAN consequences, damage, or failure-forward consequence tables are complete.

## Core invariant

The browser owns mechanical resolution.

```text
AI/player intent
  -> browser Check Contract
  -> browser randomness
  -> browser Outcome Contract
  -> canonical mechanical result
  -> AI narrative interpretation
```

AI may request a check and narrate the result. It may not choose the browser character-sheet target, rewrite the roll, change the required difficulty, flip pass/fail, or exceed the browser-provided narrative budget.

This extends the existing project rule:

> BLOCK UNSAFE STATE, NOT PLAYER ACTION.

The engine blocks unauthorized mechanical outcomes, not ordinary player interaction.

## Check Contract

A required CoC check receives a browser-owned `resolutionContract` after the existing `normalizeCheck()` path has already derived the canonical target from the character sheet.

Locked fields include:

- `contractId`
- system / check type / skillId
- browser-derived target
- difficulty and actual difficulty target
- bonus / penalty dice
- mandatory / visibility
- source node and origin
- `authority = browser_coc_resolution`

Supported canonical target sources in this release:

- skill -> character skill value
- attribute -> character attribute value
- luck -> current Luck
- SAN -> current SAN

Before rolling, `resolveCheck()` verifies that the pending check still matches its contract. Target or difficulty drift fails closed with `COC_CHECK_CONTRACT_MISMATCH`.

## Outcome Contract

Every browser CoC roll receives an immutable `outcomeContract` containing:

- roll
- target
- requested difficulty
- difficulty threshold
- success rank
- rank label
- final `passed`
- clue-quality classification
- mechanical result
- narrative budget

The contract distinguishes raw success rank from whether that rank satisfies the requested difficulty. For example, a regular-ranked roll against a Hard requirement remains a failed mechanical outcome.

Narrative budget currently exposes:

- whether the result may be described as passed
- whether it may be described as failed
- whether a successful check may unlock its core clue
- maximum extra insight count
- limited critical advantage permission

The continuation payload now exposes `immutableOutcome` and explicitly prohibits changing roll, target, pass/fail, or exceeding the extra-insight budget.

## CoC boundaries covered

v1.6.0 deterministic tests cover:

- browser skill/attribute/Luck/SAN target ownership
- SAN mandatory/public normalization
- regular / hard / extreme thresholds
- critical / extreme / hard / regular / failure / fumble / skipped outcomes
- CoC fumble boundary: 96-100 below target 50, otherwise 100
- difficulty mismatch behavior
- pending-check tamper rejection
- Outcome Contract rank/pass/budget tamper rejection
- immutable continuation guidance
- old Schema 8 record reconstruction
- request-context authority
- prompt authority
- diagnostics

## Secret diagnostic boundary

A v1.6 audit found that the first diagnostic wrapper would have added secret-check Outcome Contracts to the default diagnostic package even though the existing product deliberately hides secret check records unless `includeSecrets=true`.

The release fixes this before PR creation:

- default diagnostics include public CoC outcomes only
- secret Outcome Contracts are included only with explicit `includeSecrets=true`
- diagnostic reads do not advance canonical revision
- lazy contract reconstruction preserves stable contract IDs across repeated reads

## Transport timeout regression discovered during real API gate

The first full v1.6 real-provider gate reached the local 90-second request timeout on the first player action but surfaced as generic `INITIAL_REQUEST_FAILED` with an empty raw response.

The mechanical Resolution Engine deterministic suite was already green, so this was investigated separately as a transport-classification defect.

Root cause boundary:

- `callChatCompletion()` explicitly aborts its local controller with reason `"timeout"`
- old catch logic relied primarily on `error.name === "AbortError"`
- Node 24 fetch may surface a custom-reason abort as a `TypeError` rather than the expected AbortError shape
- the explicit timeout fact could therefore be lost before API Response Resilience classification

v1.6.0 now checks the request's own `AbortSignal.reason`:

- `reason === "timeout"` -> `AI_PROVIDER_TIMEOUT`, retryable
- `reason === "user"` -> `AI_REQUEST_CANCELLED`, not provider-retryable

This is a narrow transport fix. It does not make arbitrary TypeError/network failures count as timeouts and does not retry player cancellation.

Four deterministic tests cover this boundary, including a Node-style `TypeError("invalid_argument")` simulation.

## Historical semantic-version test repair

The first full v1.6 historical run also exposed old v1.5.5/v1.5.6 test predicates that treated only the patch component as version ordering, e.g. `patch >= 5`. Such a predicate incorrectly rejects valid `1.6.0` because its patch component is `0`.

Those test-only predicates were converted to real numeric semantic-version ordering. Their behavior assertions are unchanged.

## Deterministic validation

### v1.6.0 new tests

- `test-v160-coc-resolution-engine.js`: **35 PASS / 0 FAIL**
- `test-v160-coc-resolution-diagnostics.js`: **4 PASS / 0 FAIL**
- `test-v160-transport-abort-classification.js`: **4 PASS / 0 FAIL**

v1.6.0 new total: **43 PASS / 0 FAIL**.

### Permanent historical baseline

v1.5.13 baseline: **458 PASS / 0 FAIL**.

### Combined release target/result

**501 deterministic PASS / 0 FAIL**.

The successful full validation also passed:

- all historical deterministic groups
- JavaScript syntax checks
- single-HTML build
- single-HTML verifier
- deterministic double build
- one-product-HTML invariant
- `git diff --check`

## Real DeepSeek current-runtime validation

Successful full v1.6 gate:

- workflow Run: `31367411977`
- model: `deepseek-v4-flash`
- player actions: `8`
- structured requests: `8`
- API attempts: `9`
- automatic retries: `1`
- provider empty responses: `1`
- retry exhausted: `0`
- graceful fallbacks observed: `0`
- technical leaks: `0`
- final phase: `campaign_ended`
- final ending: `ending-solved`

This run loaded the v1.6 Resolution Engine in the real current-runtime prompt/payload chain and completed the case without a hard protocol failure or interaction dead-end.

The run did not happen to request a browser check. Browser Check/Outcome Contract behavior is therefore proven by the 43 deterministic v1.6 tests rather than claimed from this provider sample.

## Compatibility

- Save Schema remains `8`.
- AI protocol remains `1.3`.
- Old Schema 8 CoC check records are lazily reconstructed in memory.
- Normal successful player actions do not gain an extra AI request.
- Existing v1.5.6-v1.5.13 state authority boundaries remain in place.
- Unknown operations and true protocol violations remain strict.

## Scope intentionally not completed in v1.6.0

The following are planned for later v1.6.x work rather than being implied by the first Resolution Engine release:

1. browser-owned structured mechanical consequence contract after a check
2. failure-forward cost taxonomy and deterministic cost authorization
3. SAN loss resolution beyond the existing SAN check trigger/roll foundation
4. HP/damage resolution policy
5. combat resolution / opposed or extended resolution as applicable to the chosen rule profile
6. broader authoring schema for rule-driven check outcomes

## Release decision

v1.6.0 is ready for permanent-CI integration and clean-PR validation once temporary release helpers are removed. Merge remains a separate explicit user action.
