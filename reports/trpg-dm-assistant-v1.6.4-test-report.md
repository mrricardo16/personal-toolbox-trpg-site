# TRPG DM Assistant v1.6.4 Test Report

## Release identity

- APP_VERSION: `1.6.4`
- Save Schema: `8`
- AI protocol: `1.3`
- SAN Loss Resolution: `1.0`
- SAN Loss Window / Indefinite Insanity Tracking: `1.0`
- Authority: `browser_coc_sanity_window`
- Product: `outputs/trpg-dm-assistant.html`
- Product size: `556994 bytes`

## Objective

v1.6.4 closes the cumulative SAN-loss gap intentionally left by v1.6.3. The browser now owns an authoritative Starting SAN window, cumulative SAN loss within that window, and the one-fifth threshold used to mark indefinite insanity. AI remains a narrator and cannot reset the window, alter cumulative loss, predeclare the condition, or clear it.

This stage deliberately does **not** fabricate recovery or expiry timing for indefinite insanity. It records the browser-proven condition and leaves recovery lifecycle work for a later v1.6.x stage.

## Browser-owned SAN loss window

A CoC7 investigator receives an authoritative SAN-loss window at scenario activation. The window records:

- `baselineSan`: current SAN at window start;
- `threshold`: `floor(baselineSan / 5)`, with at least one actual point of loss required for very low baselines;
- `cumulativeLoss`;
- source / sourceId / start time;
- bounded event history;
- whether the threshold has been crossed and the exact triggering event.

The threshold is fixed from the window baseline. Later current-SAN changes do not move that threshold retroactively.

## Window boundaries

The browser starts a fresh authoritative window when:

1. a structured scenario is activated; or
2. play crosses into a different authored chapter.

The new window uses the investigator's current SAN as its new Starting SAN and resets only cumulative loss for the new window. It does not restore SAN and does not erase an already-active indefinite-insanity condition.

Same-chapter node movement does not reset the window.

## SAN-loss accounting

Loss is registered from browser-trusted canonical sources:

- v1.6.3 SAN check records after the existing SAN loss has already been applied;
- trusted canonical transaction SAN decreases, including authored failure-forward costs.

Events are deduplicated by source identity so retrying the same SAN check does not double-count loss. Positive SAN changes are not counted as loss. Event history is capped at the most recent 80 entries.

When cumulative loss first reaches or exceeds the window threshold, the browser writes a canonical `indefinite` condition containing the source window, threshold, cumulative loss at trigger, trigger event and timestamp. Further SAN loss continues to accumulate without retriggering the condition.

## Save and read boundaries

Save Schema remains `8`. The new `lossWindow` and `indefinite` state is lazily normalized inside the existing sanity state.

`sanityStateSnapshot()` remains a pure read. Payload/context/diagnostic inspection does not silently create or advance SAN-window state.

If no authoritative window exists, the engine refuses to infer one from a legacy current-SAN difference. This prevents a loaded legacy state from being falsely treated as one continuous scenario/chapter window.

## AI/request behavior

The module adds no provider request and no new AI round trip.

The existing SAN context now dynamically includes browser-owned window and indefinite-condition information. The system prompt explicitly states that AI may narrate those values but may not reset the window, alter cumulative loss, prematurely declare indefinite insanity, or clear the condition. Guarded behavior must still preserve normal player interaction.

## Deterministic validation

New suite:

- `build/test-v164-indefinite-insanity-tracking.js`
- **31 PASS / 0 FAIL**

Previous permanent baseline:

- **613 PASS / 0 FAIL**

v1.6.4 release deterministic total:

- **644 PASS / 0 FAIL**

The 31 focused tests cover one-fifth threshold calculation, scenario-start window creation, fixed baseline semantics, cumulative threshold crossing, event deduplication, no-window fail-safe behavior, idempotency after trigger, explicit recovery deferral, fresh-window semantics, SAN-check accounting, trusted transaction accounting, SAN recovery exclusion, authored chapter reset, same-chapter stability, history cap 80, pure snapshots, Schema 8 persistence, request context authority, prompt restrictions, no extra API round trip, build ordering and verifier coverage.

The complete historical permanent suite also passed, together with JavaScript syntax checks, deterministic double build, single-HTML verification and the one-product-HTML invariant.

## Real DeepSeek current-runtime evidence

Final release gate:

- Workflow Run: `31553994885`
- Result: SUCCESS
- Model: `deepseek-v4-flash`
- player actions: `8`
- browser checks observed in this provider sample: `0`
- structured requests: `8`
- provider-successful structured requests: `7`
- API attempts: `14`
- automatic retries: `6`
- provider empty responses: `7`
- retry exhaustion: `1`
- graceful fallbacks observed: `1`
- ending proposals: `1`
- ending confirmations: `1`
- technical leaks: `0`
- provider-deferred ending: `false`
- final runtime phase: `campaign_ended`
- ending: `ending-solved`

The provider sample did not trigger a SAN check or cumulative SAN threshold. v1.6.4 mechanical correctness is therefore established by the 31 deterministic browser-authority tests rather than overstating live-provider coverage. The real run proves that loading the v1.6.4 SAN-window module into the full current runtime preserves protocol behavior, API resilience and normal case completion.

## Compatibility

- Save Schema remains `8`.
- AI protocol remains `1.3`.
- No extra normal AI request.
- Existing SAN shock, Failure-Forward, Mechanical Consequence, Resolution, Ending Gate, Guard, Progress Semantics and historical suites remain green.
- v1.6.3 same-record SAN-shock idempotency is preserved.
- Existing provider-deferred ending acceptance remains unchanged; the final v1.6.4 provider sample completed normally instead.

## Deferred v1.6 work

- temporary-insanity expiry / recovery lifecycle
- indefinite-insanity recovery lifecycle
- HP damage / major wound / unconsciousness / dying
- combat, opposed and extended resolution
