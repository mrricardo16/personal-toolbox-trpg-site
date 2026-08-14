# TRPG DM Assistant v1.6.7 Test Report

## Release identity

- APP_VERSION: `1.6.7`
- Save Schema: `8`
- AI protocol: `1.3`
- Healing Recovery: `1.0`
- Authority: `browser_coc_healing_recovery`
- Product: `outputs/trpg-dm-assistant.html`
- Product size: `599664 bytes`

## Objective

v1.6.7 extends the browser-owned CoC7 health model from immediate damage and stabilization into explicit recovery over time. It adds natural daily healing, Major Wound weekly healing, and Medicine treatment without allowing chat messages or AI prose to silently advance recovery time or directly award player-character HP.

The central rule remains:

**AI may propose or narrate; browser mechanics decide and commit.**

And the permanent interaction invariant remains:

**BLOCK UNSAFE STATE, NOT PLAYER ACTION.**

## Explicit time authority

Recovery time is never inferred from generic chat turns, scene turns, or number of AI exchanges.

- normal injury without Major Wound may use an explicit `1 day elapsed` browser action;
- Major Wound recovery may use an explicit `1 week elapsed` browser action;
- Medicine requires explicit treatment-time and equipment confirmation;
- no recovery action is allowed while an AI request is active, preventing revision races.

This keeps recovery mechanics deterministic until a later dedicated time/combat-round engine exists.

## Natural daily healing

For a living, non-dying investigator without an active Major Wound:

- explicitly advancing one recovery day restores exactly `1 HP`;
- healing is capped by Max HP;
- full HP rejects meaningless healing;
- active dying must be stabilized first;
- active Major Wound cannot use the daily +1 path and must use weekly healing instead.

## Major Wound weekly healing

For an active Major Wound after the investigator is no longer dying:

- the browser performs one CON percentile roll for an explicitly elapsed week;
- regular/hard success at the normal CON check level restores `1D3 HP`;
- extreme or critical success restores `2D3 HP`;
- failure restores `0 HP` for that week;
- Major Wound clears on extreme/critical weekly success, or once HP reaches at least `ceil(Max HP / 2)`;
- healing never exceeds Max HP.

The weekly roll is recorded as a browser-owned public health check record.

## Medicine treatment

Medicine is a local browser health action rather than an AI-owned healing effect.

- the rescuer's Medicine target must be explicitly supplied in the range `1..100`;
- treatment must explicitly confirm at least one hour of care;
- treatment must explicitly confirm appropriate equipment/materials;
- same-day treatment uses Regular difficulty;
- later treatment uses Hard difficulty;
- success restores `1D3 HP`, capped by Max HP;
- if the restored HP reaches at least half Max HP, an active Major Wound clears;
- failure does not alter HP or Major Wound;
- an active dying investigator must first be stabilized through First Aid before Medicine can proceed.

First Aid and Medicine therefore stack only through separate browser-owned results; the AI cannot combine them into a prose-only heal.

## AI consequence boundary

When a CoC continuation is tied to a Medicine check record, positive AI-origin `adjustHp` is stripped before canonical commit. Safe unrelated operations in the same response remain available.

This prevents duplicate healing while preserving interaction availability.

The request payload exposes `cocHealingRecovery`, and the system prompt states that the AI may narrate current recovery state but may not:

- advance a day/week by prose;
- decide Medicine success/failure;
- award Medicine HP directly;
- clear Major Wound without browser evidence.

## Provider compatibility fix discovered by release E2E

The first real-provider release attempt exposed a separate protocol-format variance unrelated to Healing Recovery.

DeepSeek returned:

```json
{"operation":"addPinnedFact","fact":"..."}
```

while the formal operation schema requires `text`.

The browser correctly rejected that response as `STATE_CHANGE_PARAMETER_INVALID`; no canonical state was corrupted.

A narrow compatibility normalization was then added:

- applies only to the known `addPinnedFact` operation;
- only when `text` is absent;
- only when `fact` is a non-empty string;
- explicit `text` always wins;
- empty `fact` does not fabricate a value;
- unknown operations do not receive the alias.

This mirrors the existing narrow `addRevealedTruth.description -> text` policy without weakening unknown-operation or parameter validation.

Four focused deterministic regressions prove these boundaries.

## Save compatibility

Save Schema remains `8`.

`character.healingRecovery` is lazily normalized for old Schema 8 saves and keeps bounded recovery history. No past days, weeks, Medicine rolls, or healing are invented during load.

Existing v1.6.5 HP Damage State and v1.6.6 Health Stabilization remain the upstream authority for Major Wound, dying, First Aid, death, and stabilization.

## Deterministic validation

New focused suite:

- `build/test-v167-healing-recovery.js`
- **43 PASS / 0 FAIL**

The original Healing Recovery implementation contributed 39 tests; four additional tests cover the real-provider `addPinnedFact.fact -> text` compatibility boundary.

Previous permanent baseline:

- **714 PASS / 0 FAIL**

v1.6.7 final deterministic total:

- **757 PASS / 0 FAIL**

The focused suite covers natural daily healing, full-HP rejection, Major Wound daily-healing rejection, dying gating, weekly CON success/failure, `1D3` and `2D3` recovery, extreme/critical Major Wound clearance, half-Max-HP clearance, Medicine same-day/later difficulty, required treatment time/equipment, target validation, First Aid -> Medicine ordering, HP cap, AI-request race protection, payload/diagnostic/prompt authority, AI duplicate-healing stripping with safe-operation preservation, legacy Schema 8 initialization, build/verifier inclusion, and the narrow `addPinnedFact.fact` provider alias.

JavaScript syntax, deterministic double build, and the single-HTML verifier all passed.

Final single-HTML product size:

- **599664 bytes**

## Real DeepSeek current-runtime evidence

Final accepted release gate:

- Workflow Run: `31761711529`
- Result: SUCCESS
- Model: `deepseek-v4-flash`
- player actions: `8`
- browser checks observed in provider sample: `0`
- structured requests: `8`
- provider-successful structured requests: `6`
- API attempts: `16`
- automatic retries: `8`
- provider empty responses: `10`
- retry exhaustion: `2`
- graceful fallbacks: `2`
- technical leaks: `0`
- ending proposals: `1`
- ending confirmations: `1`
- provider-deferred ending: `false`
- final runtime phase: `campaign_ended`
- ending: `ending-solved`

The provider sample did not exercise natural healing, weekly Major Wound healing, or Medicine mechanics. Those mechanics are proven by the 43 focused deterministic tests. The provider evidence verifies that the complete v1.6.7 runtime, including the new payload/prompt module and the narrow pinned-fact compatibility normalization, remains playable through the long-case path despite substantial provider empty-response volatility.

## Compatibility

- Save Schema remains `8`.
- AI protocol remains `1.3`.
- No extra normal AI request is introduced by recovery mechanics.
- v1.6.5 remains authoritative for trusted damage events and Major Wound creation.
- v1.6.6 remains authoritative for dying round CON and First Aid stabilization.
- v1.6.1/v1.6.2 consequence and failure-forward authority remain intact.
- v1.6.3/v1.6.4 SAN authority remains unchanged.
- unknown operations and malformed business protocol remain strict.
- historical Guard, Interaction Availability, NPC Knowledge Boundary, Ending Gate, Progress Semantics, threat-clock and full-case suites remain green.

## Deferred v1.6 work

- combat round ownership and initiative/order
- attack / dodge / fight-back / opposed combat resolution
- armor and weapon damage generation
- repeated treatment limits / richer authored medical constraints if needed
- temporary/indefinite insanity recovery and expiry handling
- broader opposed and extended resolution
