# TRPG DM Assistant v1.6.5 Test Report

## Release identity

- APP_VERSION: `1.6.5`
- Save Schema: `8`
- AI protocol: `1.3`
- HP Damage State: `1.0`
- Authority: `browser_coc_health`
- Product: `outputs/trpg-dm-assistant.html`
- Product size: `567950 bytes`

## Objective

v1.6.5 moves CoC7 single-damage-event severity and immediate injury state into the browser after a trusted `adjustHp` operation has been authorized by the existing mechanical consequence layers. AI remains the narrator and may not manufacture, merge, clear, or rewrite Major Wound, unconsciousness, dying, or instant-death state.

This stage intentionally covers the **damage-event state transition** only. First Aid, Medicine recovery, Major Wound healing, stabilization and repeated dying checks remain deferred rather than being approximated.

## Browser-owned damage event model

Each trusted negative `adjustHp` operation is treated as exactly one damage event.

- Separate damage operations are never aggregated. With Max HP 12, `-3` followed by `-3` remains two 3-point hits and does not become a synthetic 6-point Major Wound.
- Major Wound threshold is `ceil(Max HP / 2)`.
- A single damage event at or above that threshold creates Major Wound state and immediately resolves the browser-owned CON check.
- CON roll `<= CON` succeeds; a failed CON check marks the investigator unconscious.
- HP reaching `0` marks unconsciousness.
- HP reaching `0` while a Major Wound is active marks dying.
- A single damage event whose original amount is at least Max HP is classified as instant death.
- Severity is evaluated from the original trusted damage amount, not the post-clamp HP decrease. For example, an investigator at 2/10 HP who receives a trusted 10-point hit is still classified as receiving Max-HP-level damage even though canonical HP can only fall by 2.

Positive HP changes do not create damage events and do not silently clear a previously established Major Wound or other injury condition.

## Authority chain and interaction availability

v1.6.5 is downstream from the existing v1.6.1 Mechanical Consequence Contract and v1.6.2 Failure-Forward Cost Engine.

The HP module extracts events from the **prepared/guarded transaction** (`transaction.parsed.stateChanges`) rather than the raw AI response. This means unauthorized AI-origin HP punishment that an earlier authority layer strips cannot reappear as a v1.6.5 injury event. Browser-authored/trusted HP costs continue through the normal canonical transaction and then receive injury-state classification.

The interaction invariant remains:

**BLOCK UNSAFE STATE, NOT PLAYER ACTION.**

A serious injury can change canonical health state, but the defensive layer does not convert ordinary player interaction into a technical refusal.

## Canonical state and save compatibility

CoC7 investigators now use:

```text
character.healthState = {
  version,
  authority,
  majorWound,
  unconscious,
  dying,
  dead,
  lastDamageEvent,
  history
}
```

Save Schema remains `8`.

For legacy Schema 8 saves, normalization is deliberately conservative:

- a legacy character already at 0 HP may be represented as unconscious;
- Major Wound is not reconstructed without a recorded damage event;
- dying is not inferred merely from 0 HP;
- death is never fabricated from old state without damage-event evidence.

Damage-event history is bounded to the latest 80 records. Reprocessing the same event key is idempotent and does not reroll the Major Wound CON check.

## Deferred recovery semantics

v1.6.5 deliberately does **not** implement or guess:

- First Aid stabilization;
- Medicine recovery;
- Major Wound healing/removal;
- round-by-round dying CON checks;
- combat attack/damage generation;
- opposed or extended resolution.

The canonical `dying` state explicitly records `roundChecksManaged=false` so future work can add the next rules layer without pretending it already exists.

## API/request behavior

HP Damage State is local browser logic.

- no extra normal AI request is added;
- no provider call is used to decide Major Wound, CON, unconsciousness, dying, or instant death;
- current request payloads and diagnostics expose the browser-owned health state so AI can narrate within the already-decided result;
- Save Schema and AI protocol remain unchanged.

## Deterministic validation

New suite:

- `build/test-v165-hp-damage-state.js`
- **32 PASS / 0 FAIL**

Previous permanent baseline:

- **644 PASS / 0 FAIL**

v1.6.5 release deterministic total:

- **676 PASS / 0 FAIL**

The focused suite covers threshold boundaries, odd Max HP rounding, below-threshold damage, exact-threshold Major Wound, CON equality/failure, instant death, 0 HP without Major Wound, dying with active Major Wound, multi-hit non-aggregation, original-damage severity, healing behavior, event idempotency, operation ordering, aliases, unrelated operations, history bound, pure snapshot reads, legacy save normalization, real prepare/commit integration, prior-Guard authority preservation, request context, diagnostics, prompt constraints, explicit deferred recovery, no additional API round trip, build order and single-HTML verification.

JavaScript syntax, deterministic double build, the single-product-HTML invariant and the single-HTML verifier also passed.

## Real DeepSeek current-runtime evidence

Final release gate:

- Workflow Run: `31555873032`
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
- technical leaks: `0`
- provider-deferred ending: `true`
- final runtime phase: `awaiting_player_action`

The sample passed through the existing API Resilience provider-deferred-ending acceptance path: the current runtime remained playable, no technical state leaked, no hard rule-engine failure occurred, and 7/8 structured requests received usable provider responses.

This provider sample did **not** exercise a browser HP damage event. HP mechanics are therefore proven by the 32 focused deterministic tests; the provider run proves that loading the complete v1.6.5 runtime did not break the live interaction/transport path.

## Compatibility

- Save Schema remains `8`.
- AI protocol remains `1.3`.
- No extra normal AI request.
- v1.6.1 Mechanical Consequence Contract remains upstream authority for unauthorized punishment stripping.
- v1.6.2 Failure-Forward Cost Engine remains the source of authored failure-forward HP costs.
- v1.6.3 SAN Loss Resolution and v1.6.4 SAN Loss Window remain green.
- Historical Guard, Interaction Availability, Ending Gate, Progress Semantics, NPC Knowledge Boundary and full-case suites remain green.

## Deferred v1.6 work

- First Aid / stabilization
- Medicine and Major Wound recovery
- repeated dying checks and death transition after failed dying care/checks
- combat attack / damage / armor flow
- opposed and extended resolution
- temporary/indefinite insanity recovery and expiry handling
