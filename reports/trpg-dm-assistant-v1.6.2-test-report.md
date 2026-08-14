# TRPG DM Assistant v1.6.2 Test Report

## Release identity

- APP_VERSION: `1.6.2`
- Save Schema: `8`
- AI protocol: `1.3`
- Failure-Forward Cost Engine: `1.0`
- Authority: `browser_authored_failure_forward`
- Product: `outputs/trpg-dm-assistant.html`
- Product size: `538000 bytes`

## Objective

v1.6.2 makes failure-forward cost author-owned and browser-applied. The AI may select and narrate an eligible authored failure-forward route, but cannot invent, enlarge, shrink, or substitute its mechanical cost.

The permanent interaction invariant remains:

> BLOCK UNSAFE STATE, NOT PLAYER ACTION.

## Supported authored cost bundle

A `failure_forward` clue route may declare:

- `tension`
- `hp`
- `san`
- `progress`
- `resources`

Compatibility rules:

- missing `tension` preserves legacy default `1`;
- explicit `tension: 0` is allowed when another positive cost exists;
- an all-zero cost is invalid;
- unknown cost keys are invalid;
- fumble keeps at least 2 tension only when authored tension is positive;
- HP cost is nonlethal and cannot reduce HP below 1;
- SAN/progress/resources are clamped against current canonical availability;
- multiple routes are deduplicated by clue+route, authored costs are aggregated once, then clamped.

## Legacy tension takeover

Before v1.6.2, `validateClueAcquisition()` both proved a failure-forward route and directly mutated `validationContext.failureForwardTension`. That legacy side effect would double-charge tension once the new cost bundle was introduced and could not represent `tension: 0` because the old calculation forced a minimum of one.

v1.6.2 preserves the old route-proof logic but neutralizes only that tension side effect for `failure_forward`. The Cost Engine then applies the complete authored cost bundle exactly once.

Normal `revealClue` investigation progress is intentionally preserved. Therefore a failure-forward clue can still grant its normal clue-progress value while a separately authored `progress` cost is subtracted in the same transaction. Example proven in the suite: starting progress `7`, failure clue gain `+3`, authored progress cost `-4`, net progress `6`.

## Deterministic validation

New suite:

- `build/test-v162-failure-forward-cost-engine.js`
- **41 PASS / 0 FAIL**

Previous permanent baseline: **535 PASS / 0 FAIL**.

v1.6.2 release deterministic total: **576 PASS / 0 FAIL**.

The suite covers cost schema validation, Case Integrity blocking errors, legacy default cost, explicit zero-tension cost, success/skipped rejection, record/route binding, duplicate-route prevention, HP/SAN/progress/resource clamping, multi-route aggregation, fumble rules, compatibility with v1.6.1 Consequence Contract, AI cost exaggeration/spoofing, actual transaction application, safe same-turn state preservation, strict unknown operations, prompt/context authority, production build order and verifier coverage.

The release gate also passed JavaScript syntax, deterministic double build, single-HTML verification, and the one-product-HTML invariant.

## Real DeepSeek current-runtime evidence

Release workflow Run `31453752316`: **SUCCESS**.

- model: `deepseek-v4-flash`
- player actions: `8`
- structured requests: `8`
- API attempts: `10`
- automatic retries: `2`
- provider empty responses: `3`
- retry exhaustion: `1`
- graceful fallbacks observed: `1`
- technical leaks: `0`
- final phase: `campaign_ended`
- ending: `ending-solved`

The provider sample did not trigger a browser check/failure-forward route, so it is runtime-compatibility evidence rather than proof of failure-forward mechanics. Mechanical correctness is established by the 41 deterministic tests.

The provider did exhibit one bounded retry exhaustion. API Response Resilience converted it to a graceful fallback; the case remained playable, later actions completed normally, and no canonical corruption or technical leak occurred.

## Compatibility

- Save Schema remains 8.
- AI protocol remains 1.3.
- Normal successful action request count is unchanged.
- Existing Check/Outcome Contract and Mechanical Consequence Contract remain authoritative.
- Existing clue progress semantics remain intact.
- Existing failure-forward route legality proof remains intact; only its old tension side effect is replaced.

## Deferred v1.6 work

- SAN loss and insanity resolution
- HP damage / major wound / dying policy
- combat / opposed / extended resolution
- broader authored consequence categories
