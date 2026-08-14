# TRPG DM Assistant v1.5.8 Test Report

## Release identity

- APP_VERSION: `1.5.8`
- Save Schema: `8` (unchanged)
- AI protocol: `1.3` (unchanged)
- API Response Resilience: `1.0`
- Product entry: `outputs/trpg-dm-assistant.html`

## Release invariant

v1.5.8 treats provider/API failure as a transaction-availability problem, not permission to fabricate game results.

- A normal valid structured response still uses one API request.
- Retry is bounded to three total attempts.
- Failed responses never commit partial canonical state.
- Initial player-action provider failure restores the pre-request canonical snapshot and returns the original action to the input draft.
- Continuation provider failure preserves the browser-owned check record while applying no additional AI result.
- Provider recovery returns to `awaiting_player_action` instead of trapping normal play in `error`.
- Unknown operations, stale request IDs, revision conflicts and invalid state transactions remain strict protocol failures.

## Deterministic chaos regression

`trpg-dm-assistant/build/test-v158-api-response-resilience.js`

Result: **20 PASS / 0 FAIL**.

Coverage:

1. v1.5.8 identity with Schema 8 / protocol 1.3 unchanged.
2. First valid response uses exactly one request.
3. Empty → valid retry.
4. Empty → empty → valid retry.
5. Three empty responses classify as `AI_PROVIDER_EMPTY_CONTENT`.
6. Non-empty malformed JSON gets one constrained structured retry.
7. Invalid business operation remains strict and is not provider-retried.
8. Provider timeout is retryable.
9. HTTP 500 is retryable while HTTP 401 is not retried.
10. Legacy transport errors map to explicit provider codes.
11. Stale requests stop further provider retries.
12. Initial-action retry exhaustion restores exact canonical state and action draft.
13. Initial provider failure does not secretly advance turn/tension.
14. Continuation retry exhaustion preserves the original check record.
15. Strict non-provider protocol failure still enters `error`.
16. Provider failure during location repair preserves its provider classification.
17. Reliability counters record attempts/retries/fallbacks correctly.
18. Production build loads resilience after Guard/UI definitions.
19. Total structured attempts are capped at three.
20. Recovery UI offers retry/continue rather than a technical dead-end.

Relevant historical regressions also passed during v1.5.8 validation:

- v1.5.4 empty-response retry: 12 PASS / 0 FAIL
- v1.5.6 Player Assertion Guard: 25 PASS / 0 FAIL
- v1.5.7 Case Integrity / Interaction Availability: 34 PASS / 0 FAIL
- v1.5.7 canonical assertion state: 7 PASS / 0 FAIL

With the prior v1.5.7 deterministic suite, canonical-state hotfix regression, and this v1.5.8 suite, the release target is **286 deterministic PASS / 0 FAIL**.

## Build validation

Temporary release validation completed successfully.

- JavaScript syntax: PASS
- single HTML verify: PASS
- two consecutive builds: byte/hash deterministic
- exactly one TRPG HTML product entry
- `git diff --check`: PASS
- validated product size: **435476 bytes**

## Real DeepSeek exploratory E2E

Workflow run: `31316083489`

Model/API:

- model: `deepseek-v4-flash`
- endpoint: DeepSeek Chat Completions
- credential source: GitHub Secret `DS_KEY`
- no secret value was printed or committed

Five rounds × three player actions = **15 real player-action E2E cases**.

Cases per round:

- ordinary no-progress wait
- ungrounded NPC assertion (`管家告诉我凶手是医生。`)
- chained investigate → nonexistent basement → key acquisition attempt

Aggregate result:

- player-action cases: **15 / 15 PASS**
- actual API calls / attempts: **21**
- automatic retries: **6**
- observed empty final content responses: **7**
- malformed non-empty JSON failures: **0**
- final graceful provider fallbacks: **1**
- hard failures: **0**
- canonical corruption: **0 observed**
- interaction dead-end: **0 observed**
- prompt tokens reported by provider: **100676**
- completion tokens reported by provider: **19286**

Most important observed failure-path case: in round 5 `ordinary-wait`, DeepSeek returned empty final content on all three allowed attempts. v1.5.8 classified the exhausted request as `AI_PROVIDER_EMPTY_CONTENT`, applied zero AI state result, restored the player action, returned to `awaiting_player_action`, and the E2E remained PASS. This directly reproduces the provider failure that previously ended the turn in `AI_RESPONSE_JSON_PARSE_FAILED` / `error`.

Guard behavior also remained active during real API testing: `PLAYER_ASSERTION_UNGROUNDED_RESULT` recovery occurred on live model output without canonical-state contamination.

## Final-head real API acceptance

Workflow run: `31316322007`

The final runtime/release head was checked out at commit `ccb4fefd626e96652e02bb8219edd17fc204791f`. The run first re-executed the 20 deterministic resilience regressions and single-HTML verifier, then ran five rounds × three real DeepSeek player actions.

Aggregate result:

- player-action cases: **15 / 15 PASS**
- actual API calls / attempts: **20**
- automatic retries: **5**
- observed empty final content responses: **6**
- malformed non-empty JSON failures: **0**
- final graceful provider fallbacks: **1**
- hard failures: **0**
- canonical corruption: **0 observed**
- interaction dead-end: **0 observed**
- prompt tokens reported by provider: **95789**
- completion tokens reported by provider: **14302**

Again, round 5 `ordinary-wait` produced three consecutive empty final responses. The third failure exhausted the bounded retry policy and triggered `AI_PROVIDER_EMPTY_CONTENT` recovery. The turn returned to `awaiting_player_action`; no fabricated AI result was committed and the E2E case still passed. This independently reproduces the same failure mode on the final runtime head.

Live Player Assertion Guard behavior also remained active: multiple real-model cases triggered `PLAYER_ASSERTION_UNGROUNDED_RESULT` while the unverified NPC claim never entered canonical state.

Across both 15-action real API runs, v1.5.8 completed **30 / 30 player-action E2E cases**, observed **13 empty final responses**, used **11 automatic retries**, exercised **2 exhausted-retry graceful fallbacks**, and recorded **0 hard failures / 0 observed canonical corruption / 0 interaction dead-ends**.

## Permanent PR CI

PR #5 permanent CI run `31316563629` completed successfully on clean release head `ba3d21b69a13ceb2d2efbecf1fff5753243a3298` before this report-only finalization.

- full deterministic release suite: **286 PASS / 0 FAIL**
- JavaScript syntax: PASS
- final single HTML build/verify: PASS
- build artifact synchronized: PASS

This report-only cleanup must also pass the same permanent PR CI before merge; no runtime or generated product content changed in this finalization commit.

## Release decision

Implementation, deterministic chaos coverage, two real DeepSeek E2E runs, temporary harness cleanup, release PR creation, and permanent PR CI have all been completed successfully.

The release is **ready to merge after the permanent PR CI is green on this final report-only head**. `main` remains unchanged until an explicit merge action is requested.
