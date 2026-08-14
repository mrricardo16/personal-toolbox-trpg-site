# TRPG DM Assistant v1.6.3 Test Report

## Release identity

- APP_VERSION: `1.6.3`
- Save Schema: `8`
- AI protocol: `1.3`
- SAN Loss Resolution: `1.0`
- Authority: `browser_coc_sanity`
- Product: `outputs/trpg-dm-assistant.html`
- Product size: `546989 bytes`

## Objective

v1.6.3 moves the immediate CoC7 sanity-shock chain into the browser after the browser has already resolved the SAN check and SAN-loss amount. AI remains the narrator, not the authority over the shock roll or temporary-insanity result.

This stage intentionally implements the **single-loss shock chain only**. It does not claim complete indefinite-insanity support because that requires an authoritative starting-SAN baseline plus a cumulative-loss tracking window.

## Browser-owned SAN shock chain

For a CoC7 SAN check record:

- SAN loss `0`: no involuntary reaction and no INT shock check.
- SAN loss `1-4`: momentary involuntary reaction may be narrated, but no INT shock check.
- single SAN loss `>=5`: browser rolls `1D100` against INT.
- INT roll `<= INT`: temporary insanity is triggered.
- browser then rolls:
  - `1D10` temporary-insanity duration in hours;
  - `1D10` bout-of-madness type;
  - `1D10` bout duration in rounds.

The bout table is browser-owned:

1. amnesia
2. psychosomatic disability
3. violence
4. paranoia
5. significant person
6. faint
7. flee in panic
8. hysterics / emotional outburst
9. phobia
10. mania

The generated `sanResolution` is immutable and stored on the check record. Retrying AI continuation for the same record reuses that exact result and never rerolls it.

## Canonical state and save compatibility

`character.sanityState` is lazily normalized inside Save Schema 8.

New investigators record the creation-time current SAN as a baseline marker, while legacy investigators that lack this field receive a `legacy_current` baseline. In both cases `indefiniteTrackingReady=false`; v1.6.3 deliberately does not pretend that this is sufficient to evaluate cumulative indefinite insanity.

Payload/diagnostic reads use a pure `sanityStateSnapshot()` and do not silently mutate canonical state or increment revision.

Temporary insanity, when triggered, is stored in canonical sanity state with its source record. Automatic expiry is not fabricated; `expiryManaged=false` explicitly marks that later time/condition handling is still required.

## API/request behavior

SAN loss itself is still applied by the existing browser check flow before AI continuation. v1.6.3 adds only local browser rolls and state derivation before the normal continuation payload is built.

- no extra AI request is added;
- same-record retry does not reroll SAN shock;
- v1.6.1 Mechanical Consequence Contract continues to strip continuation-time `adjustSan`, preventing duplicate SAN loss;
- interaction returns to a playable state rather than creating a technical dead-end.

## Deterministic validation

New suite:

- `build/test-v163-san-loss-resolution.js`
- **37 PASS / 0 FAIL**

Previous permanent baseline:

- **576 PASS / 0 FAIL**

v1.6.3 release deterministic total:

- **613 PASS / 0 FAIL**

The 37 tests cover threshold semantics, INT equality, INT failure, bout mapping and bounds, duration, explicit indefinite-insanity deferral, new/legacy baseline normalization, pure payload/diagnostic reads, no duplicate SAN loss, temporary-state persistence, same-record idempotency, history cap 40, old Schema 8 lazy normalization, immutable guidance/payload authority, system prompt constraints, v1.6.1 SAN duplicate protection, real `requestContinuation()` behavior, one structured request only, retry without reroll, interaction availability, build order and single-HTML verification.

JavaScript syntax, deterministic double build, single-HTML verification and the one-product-HTML invariant also passed.

## Real DeepSeek current-runtime evidence

Final release gate:

- Workflow Run: `31456000489`
- Result: SUCCESS
- Model: `deepseek-v4-flash`
- player actions: `8`
- browser checks observed in this provider sample: `0`
- structured requests: `8`
- provider-successful structured requests: `7`
- API attempts: `17`
- automatic retries: `9`
- provider empty responses: `10`
- retry exhaustion: `1`
- graceful fallbacks observed: `1`
- technical leaks: `0`
- provider-deferred ending: `true`
- final runtime phase: `awaiting_player_action`

This provider sample did not trigger a browser SAN check; SAN mechanics are therefore proven by the 37 deterministic tests rather than overstating provider coverage.

### Provider-deferred ending classification

Two earlier release-gate runs exposed a real acceptance mismatch: the browser Ending Gate was already `ready=true` with `missing=[]`, no hard failure and no leak, but DeepSeek repeatedly returned empty final content during explicit ending requests. One run recorded `16` empty responses in `20` API attempts and `4` graceful fallbacks.

The live-provider acceptance was therefore aligned with the existing v1.5.8 API Resilience product contract. A current-runtime provider sample passes when either:

1. it reaches browser-confirmed `ending-solved`; or
2. ending is deferred only by bounded provider fallback and all of the following hold:
   - runtime remains `awaiting_player_action`;
   - browser Ending Gate is ready with no missing conditions;
   - at least one graceful fallback is accounted for;
   - no hard failure;
   - no technical leak;
   - at least half of structured requests received usable provider responses.

The successful final run satisfied the second path with `7/8` provider-successful structured requests. This does not loosen deterministic ending rules; it prevents provider unavailability from being mislabeled as a browser rule-engine failure.

## Compatibility

- Save Schema remains `8`.
- AI protocol remains `1.3`.
- No extra normal AI request.
- Failure-Forward, Mechanical Consequence, Resolution, Ending Gate, Guard, Progress Semantics and historical suites remain green.
- SAN shock resolution remains browser-owned and repeatable.

## Deferred v1.6 work

- authoritative cumulative SAN-loss window / indefinite insanity
- temporary-insanity expiry and recovery timing
- HP damage / major wound / unconsciousness / dying
- combat, opposed and extended resolution
