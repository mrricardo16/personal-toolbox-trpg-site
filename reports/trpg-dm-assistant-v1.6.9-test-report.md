# TRPG DM Assistant v1.6.9 Test Report

## Release identity

- APP_VERSION: `1.6.9`
- Save Schema: `8`
- AI protocol: `1.3`
- Combat Damage: `1.0`
- Product: `outputs/trpg-dm-assistant.html`
- Product size: **640079 bytes**

## Scope

v1.6.9 closes the deferred damage boundary from v1.6.8 Combat Opposed for the supported non-impaling melee subset.

Browser-owned combat damage now consumes the already-resolved `damageDisposition` and applies:

- weapon damage dice and fixed modifiers;
- browser-derived CoC Damage Bonus from STR + SIZ;
- fixed Armor reduction before canonical HP loss;
- non-impaling initiator Extreme damage as maximum weapon damage plus maximum positive DB;
- negative DB even on Extreme damage;
- Fight Back damage capped to ordinary rolled damage rather than Extreme maximum;
- player net damage through the existing HP Damage State / Major Wound / dying / instant-death chain;
- opponent HP, defeat state, turn-order repair, and automatic combat end when the last hostile opponent is defeated.

The engine remains intentionally fail-closed for unsupported firearm / impaling modes. Variable or special armor and the broader firearm action economy remain deferred.

## Deterministic validation

- v1.6.9 Combat Damage focused suite: **48 PASS / 0 FAIL**
- Previous permanent baseline: **799 PASS / 0 FAIL**
- New permanent deterministic total: **847 PASS / 0 FAIL**
- JavaScript syntax: PASS
- deterministic double build: PASS
- single-HTML verifier: PASS
- `git diff --check`: PASS

Focused coverage includes DB boundaries, high STR+SIZ extension, weapon expression validation, regular and Extreme damage, Fight Back cap, fixed Armor, zero net damage, HP Damage State integration, Major Wound after Armor, instant death, opponent defeat, combat completion, turn repair, damageDisposition commit identity, payload/diagnostic/prompt authority, old Schema 8 loadout normalization, build order, and verifier enforcement.

The v1.6.8 historical version assertion was changed from exact equality to a semantic `>= 1.6.8` check. No v1.6.8 behavior assertion was relaxed.

## Real provider acceptance

Release gate Run `31764627135` completed successfully using the current runtime with DeepSeek V4 Flash.

Observed runtime statistics:

- actions: 8
- structured requests: 8
- usable provider responses: 7
- API attempts: 15
- automatic retries: 7
- provider empty responses: 8
- retry exhaustion: 1
- graceful fallbacks observed: 1
- technical leaks: 0
- final phase: `campaign_ended`
- ending: `ending-solved`

The provider sample did not enter Combat Mode, so it is compatibility/resilience evidence for the v1.6.9 runtime rather than proof of combat-damage mechanics. Combat damage correctness is established by the 48 deterministic focused tests.

## Authority / compatibility

- AI may narrate browser-confirmed combat damage but cannot roll or commit it.
- Damage is only consumed from browser-owned combat exchange state.
- Armor is applied before player HP Damage State classification.
- Existing Major Wound, dying, death, First Aid, recovery, and Combat Round ownership remain intact.
- Save Schema remains 8 and AI protocol remains 1.3.
- No extra normal AI round trip is introduced by combat damage.
- **BLOCK UNSAFE STATE, NOT PLAYER ACTION** remains the interaction invariant.

## Deferred

Subsequent v1.6.x work still includes firearm/impaling damage, firearm action economy and readied-gun DEX handling, point-blank / dive-for-cover behavior, variable or special armor, and any broader combat authoring schema not yet browser-owned.
