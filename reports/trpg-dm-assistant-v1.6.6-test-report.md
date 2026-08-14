# TRPG DM Assistant v1.6.6 Test Report

## Release identity

- APP_VERSION: `1.6.6`
- Save Schema: `8`
- AI protocol: `1.3`
- Health Stabilization: `1.0`
- Authority: `browser_coc_health_stabilization`
- Product: `outputs/trpg-dm-assistant.html`
- Product size: `582277 bytes`

## Objective

v1.6.6 extends the browser-owned CoC7 injury model from v1.6.5 with explicit First Aid stabilization and round-by-round dying CON checks. The browser remains the mechanical authority; AI may narrate the already-decided result but may not heal, stabilize, kill, or advance dying time by prose alone.

A central time-authority rule is explicit: **chat turns are not automatically combat rounds**. Until a full combat-round engine exists, dying CON progression occurs only through an explicit local browser action.

## Dying round CON model

When the investigator is already in an active, unstabilized `dying` state:

- the browser exposes an explicit `结算下一轮 CON` action;
- each invocation creates exactly one browser-owned CON record;
- `roll <= CON` succeeds;
- a successful CON check keeps the investigator dying and does not itself stabilize them;
- a failed CON check creates canonical death with reason `dying_con_failure` and ends the dying state;
- repeated successful checks increment a stable round ordinal and preserve bounded history;
- no active dying state means no dying CON can be fabricated;
- once dead, no further dying CON check is allowed.

This mechanism deliberately does not infer elapsed combat rounds from AI messages, player messages, scene turns, or generic campaign turns.

## First Aid stabilization model

First Aid is also resolved locally by the browser.

- treatment requires explicit confirmation that the attempt is still within the injury's one-hour First Aid window;
- the rescuer's First Aid target must be a valid value from 1 to 100;
- the browser performs the percentile roll;
- success restores exactly 1 HP, capped by Max HP;
- a successful First Aid can rouse an unconscious investigator;
- when the investigator is dying, successful First Aid clears `dying`, writes browser-owned `stabilized`, restores 1 HP, and clears unconsciousness;
- Major Wound remains active and is not erased by First Aid;
- a failed First Aid does not heal or stabilize;
- death cannot be reversed by First Aid.

The UI defaults the target field from the player character's First Aid skill when available, while allowing a different rescuer's target to be entered explicitly.

## Stale stabilization invalidation

A post-release audit of the first v1.6.6 candidate found a cross-state edge case: a character could be successfully stabilized, later receive a new trusted damage event, enter a fresh dying/dead state, and still retain the previous standalone `stabilized` marker.

That first candidate was therefore not accepted as the final release candidate even though its initial release workflow passed.

The final candidate adds a narrow downstream wrapper around trusted `hpDamageApplyEvent()` results:

- only a newly tracked, non-deduplicated damage event is considered;
- if that event creates a fresh `dying` or `dead` state, any old active `stabilized` marker is cleared;
- unrelated damage events do not erase stabilization state unnecessarily;
- deduplicated reprocessing cannot invalidate state a second time.

A dedicated regression proves that stabilization is cleared when later trusted damage causes a fresh dying state.

## AI consequence boundary

v1.6.6 preserves the existing mechanical-authority chain.

When the current CoC check record is `first_aid`, positive AI-origin `adjustHp` operations in the continuation are stripped before canonical commit. Safe unrelated state changes in the same response remain available.

This keeps the permanent interaction invariant:

**BLOCK UNSAFE STATE, NOT PLAYER ACTION.**

The browser blocks duplicate or fabricated healing, but does not turn the whole player interaction into a technical failure.

## Canonical state and save compatibility

The v1.6.5 `character.healthState` remains the canonical health container and is extended conservatively with:

```text
stabilized
treatmentHistory
dying.checks
dying.nextRoundOrdinal
dying.roundChecksManaged
```

Save Schema remains `8`.

Legacy Schema 8 saves with an existing dying condition lazily receive empty dying-check history and the next ordinal without inventing past rolls. Existing Major Wound/death inference remains governed by the conservative v1.6.5 rules.

Treatment history is bounded to the latest 60 records. Dying CON history is bounded to the latest 40 records.

## API/request behavior

Health Stabilization is local browser logic.

- no extra normal AI request is added;
- First Aid and dying CON dice are not delegated to the provider;
- request payloads expose `cocHealthStabilization` so the AI can narrate within browser-owned state;
- diagnostics expose the same authority/state context;
- the system prompt explicitly prohibits AI-owned First Aid healing, stabilization, death declaration, and chat-turn-based dying progression.

## Deterministic validation

New focused suite:

- `build/test-v166-health-stabilization.js`
- **38 PASS / 0 FAIL**

Previous permanent baseline:

- **676 PASS / 0 FAIL**

v1.6.6 final deterministic total:

- **714 PASS / 0 FAIL**

The focused suite covers module identity, First Aid skill lookup, dying creation, CON equality/failure, repeated dying ordinals, death transition, invalid check rejection, legacy save normalization, one-hour First Aid gating, target range validation, +1 HP behavior, HP cap, unconsciousness recovery, dying stabilization, Major Wound preservation, failed First Aid, death non-reversal, treatment history, payload/diagnostics/prompt authority, AI continuation healing stripping with safe-state preservation, no chat-turn/combat-round coupling, deferred-rule boundaries, stale stabilization invalidation, build order, and single-HTML verification.

JavaScript syntax, deterministic double build, the single-product-HTML invariant, and the single-HTML verifier all passed.

Final single-HTML product size:

- **582277 bytes**

## Real DeepSeek current-runtime evidence

Final accepted release gate:

- Workflow Run: `31568850359`
- Result: SUCCESS
- Model: `deepseek-v4-flash`
- player actions: `8`
- browser checks observed in provider sample: `0`
- structured requests: `8`
- provider-successful structured requests: `8`
- API attempts: `11`
- automatic retries: `3`
- provider empty responses: `3`
- retry exhaustion: `0`
- graceful fallbacks: `0`
- technical leaks: `0`
- ending proposals: `1`
- ending confirmations: `1`
- provider-deferred ending: `false`
- final runtime phase: `campaign_ended`
- ending: `ending-solved`

This provider sample did **not** exercise First Aid or dying CON mechanics. Those mechanics are proven by the 38 focused deterministic tests. The real-provider run proves that the complete v1.6.6 runtime, prompt and payload additions remain compatible with the long-case interaction path.

## Compatibility

- Save Schema remains `8`.
- AI protocol remains `1.3`.
- No extra normal AI request.
- v1.6.5 remains the authority for trusted damage-event classification and Major Wound state.
- v1.6.1 Mechanical Consequence Contract still blocks unauthorized punitive mechanics.
- v1.6.2 Failure-Forward Cost Engine still owns authored failure-forward costs.
- v1.6.3/v1.6.4 SAN mechanics remain unchanged.
- Historical Guard, Interaction Availability, Ending Gate, Progress Semantics, NPC Knowledge Boundary and full-case suites remain green.

## Deferred v1.6 work

- Medicine recovery after stabilization
- Major Wound recovery / weekly healing semantics
- natural daily HP healing
- full combat-round ownership and automatic round timing
- attack / armor / damage generation
- opposed and extended resolution
- temporary/indefinite insanity recovery and expiry handling
