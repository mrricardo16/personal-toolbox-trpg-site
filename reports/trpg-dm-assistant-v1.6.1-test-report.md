# TRPG DM Assistant v1.6.1 Test Report

## Release identity

- APP_VERSION: `1.6.1`
- Save Schema: `8`
- AI protocol: `1.3`
- CoC Resolution Engine: `1.0`
- Mechanical Consequence Contract: `1.0`
- Browser authority: `browser_coc_consequence`
- Product: `outputs/trpg-dm-assistant.html`
- Validated product size: `524690 bytes`

## Objective

v1.6.1 closes the first post-roll authority gap introduced after v1.6.0: the browser already owns the roll and success level, but an AI continuation could still attempt to invent additional punitive canonical consequences. The new contract preserves the project invariant:

> BLOCK UNSAFE STATE, NOT PLAYER ACTION.

The player action and safe narrative continue. Unauthorized punitive state is stripped before canonical transaction preparation.

## Mechanical Consequence Contract 1.0

For CoC check continuations, the browser now governs punitive consequences including:

- negative HP changes;
- direct SAN adjustment after a browser-owned SAN resolution;
- negative resources;
- item removal or quantity loss;
- tension changes;
- new threats;
- positive threat-clock advancement;
- negative investigation progress.

This is intentionally a narrow punitive boundary, not a complete reward, damage, combat, or SAN rules engine. Positive HP/resource/progress and other ordinary non-punitive state changes remain compatible unless governed by another existing authority boundary.

### Authored node-check effects

For public node-origin checks, `successStateChanges` / `failureStateChanges` are looked up again from the browser-owned scenario definition and injected by the browser. A model cannot gain this authority merely by copying a check ID because the runtime record must be `origin="node"` and match the source node/check definition.

Secret node checks retain their existing path: authored effects are already applied by `applySecretCheckOutcome()` before continuation, so the v1.6.1 contract deliberately does not inject them a second time.

### SAN duplicate prevention

Public SAN checks already calculate and commit SAN loss in the browser before AI continuation. v1.6.1 exposes the resulting loss as `alreadyApplied=true` and strips continuation-time `adjustSan`, preventing duplicate SAN loss.

### Failure-forward cost

For an authorized `failure_forward` clue route, the browser derives the tension cost from the authored acquisition route and injects that exact amount. AI-supplied tension changes are stripped first. Existing fumble behavior remains: failure-forward tension is at least `2` on a fumble.

## Deterministic validation

New v1.6.1 suite:

- `build/test-v161-mechanical-consequence-contract.js`
- **34 PASS / 0 FAIL**

The suite proves:

1. unauthorized HP/SAN/resource/item/tension/threat/clock/progress penalties are stripped;
2. safe same-turn state changes remain available;
3. local narrative neutralization removes false mechanical loss claims while preserving surrounding narrative;
4. SAN loss cannot be double-applied by continuation;
5. public authored node effects are injected from browser scenario truth;
6. model-spoofed authored effects are replaced by the browser-authored version;
7. AI-origin checks cannot impersonate authored node checks;
8. secret authored effects are not double-injected;
9. failure-forward tension comes from the authored route;
10. fumble cost preserves the existing minimum-two rule;
11. unknown operations remain strict protocol errors;
12. request context and prompts expose browser consequence authority;
13. production build and single-HTML verifier require the new module.

Previous permanent deterministic baseline: **501 PASS / 0 FAIL**.

Release deterministic total: **535 PASS / 0 FAIL**.

The release gate also passed JavaScript syntax checking, deterministic double-build, the single-HTML verifier, and the one-product-HTML invariant.

## Real DeepSeek current-runtime evidence

Release gate:

- Workflow Run: `31452147354`
- Model: `deepseek-v4-flash`
- Result: SUCCESS
- Player actions: `8`
- Structured requests: `8`
- API attempts: `13`
- Automatic retries: `5`
- Provider empty responses: `5`
- Retry exhaustion: `0`
- Graceful fallbacks: `0`
- Technical leaks: `0`
- Final phase: `campaign_ended`
- Ending: `ending-solved`

This sample did not happen to trigger a browser check, so it is evidence that the v1.6.1 module is compatible with the current real-provider runtime and does not introduce interaction or protocol dead-ends. The actual mechanical consequence behavior is proven by the 34 deterministic tests rather than overstating this provider sample.

The high empty-response count is provider variability, not a release failure: API Response Resilience recovered all five events within the bounded retry policy and no canonical corruption or technical leak was observed.

## Compatibility

- Save Schema remains `8`.
- AI protocol remains `1.3`.
- Normal successful player-action request count is unchanged.
- Existing Guard, Case Integrity, Progress Semantics, Authored Threat Clock, NPC Knowledge Boundary, Ending Gate, and v1.6.0 Resolution Engine regressions remain green.
- Ordinary turns without `currentCheckRecordId` continue through the previous transaction behavior.

## Deferred v1.6 work

v1.6.1 does not claim completion of:

- structured failure-forward cost taxonomy beyond the currently authored tension cost;
- complete SAN loss consequences and temporary/indefinite insanity rules;
- HP damage policy, major wounds, unconsciousness, dying/death;
- combat, opposed rolls, or extended tests;
- a generalized authored consequence schema for all consequence categories.

These remain subsequent v1.6.x stages.
