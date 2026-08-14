# TRPG DM Assistant v1.6.10 Test Report

## Release identity

- APP_VERSION: `1.6.10`
- Save Schema: `8`
- AI protocol: `1.3`
- Firearms / Impaling Resolution: `1.0`
- Product: `outputs/trpg-dm-assistant.html`
- Product size: **662681 bytes**

## Scope

v1.6.10 extends the browser-owned combat stack with a bounded Firearms / Impaling layer. It deliberately does not replace v1.6.8 Combat Opposed or v1.6.9 Combat Damage; it consumes those browser-owned results and preserves their authority boundaries.

Supported in this release:

- `melee_impaling` weapons through the existing melee opposed exchange;
- Base Range single-shot `firearm_impaling` attacks;
- Firearms (Handgun) / Firearms (Rifle/Shotgun) browser skill values;
- readied-firearm initiative at DEX + 50;
- point-blank bonus die at DEX / 5 feet;
- Dive for Cover with browser Dodge, successful-cover shooter penalty, and next-attack forfeiture;
- regular firearm damage without Damage Bonus;
- firearm Extreme/critical Impale damage;
- melee impaling Extreme/critical damage including applicable Damage Bonus;
- Fight Back remaining ordinary damage and never gaining Impale;
- fixed Armor before canonical HP loss;
- existing HP Damage State / Major Wound / dying / instant-death integration;
- opponent HP, defeat, combat termination, and turn-order repair from the existing v1.6.9 engine;
- old Schema 8 firearm loadout normalization with explicit preservation of valid `firearmReadied` state.

Explicitly deferred:

- multiple shots in one round;
- automatic fire;
- firearm malfunction;
- long-range bands beyond Base Range;
- shotgun pellet/range damage bands;
- reload / re-ready action economy.

## Deterministic validation

- v1.6.10 Firearms / Impaling focused suite: **45 PASS / 0 FAIL**
- Previous permanent baseline: **847 PASS / 0 FAIL**
- New permanent deterministic total: **892 PASS / 0 FAIL**
- v1.6.9 Combat Damage compatibility suite: **48 PASS / 0 FAIL** during development gate
- JavaScript syntax: PASS
- deterministic double build: PASS
- single-HTML verifier: PASS
- `git diff --check`: PASS

Focused coverage includes weapon-mode normalization, firearm skill restrictions, readied initiative, point-blank, Base Range fail-closed behavior, Dive for Cover success/failure, attack forfeiture, regular firearm damage, firearm Impale, melee Impale, negative Damage Bonus, Fight Back no-Impale behavior, fixed Armor, HP Damage State integration, Major Wound, instant death, opponent defeat/combat completion, payload/diagnostic/prompt authority, legacy load preservation, production build order, and verifier enforcement.

Two integration defects were caught and fixed by the focused gate:

1. v1.6.9 generic damage commit initially discarded the optional Impale audit fields even though the numeric damage was correct. The generic result now preserves `impaling` and `impaleExtraResult` without re-rolling or changing damage.
2. the old-load normalization order initially discarded a valid firearm `firearmReadied=true` value. v1.6.10 now captures the raw value before inner v1.6.9 normalization and restores it only when the normalized weapon is actually a firearm.

The v1.6.9 historical APP_VERSION assertion was changed from exact equality to a semantic `>= 1.6.9` check. No v1.6.9 combat behavior assertion was relaxed.

## Real provider acceptance

Release gate Run `31765966430` completed successfully using the current runtime with DeepSeek V4 Flash.

Observed runtime statistics:

- actions: 8
- checks: 0
- structured requests: 8
- usable provider responses: 6
- API attempts: 14
- automatic retries: 6
- provider empty responses: 8
- retry exhaustion: 2
- graceful fallbacks observed: 2
- JSON-invalid responses: 0
- technical leaks: 0
- provider-deferred ending: true
- final phase: `awaiting_player_action`
- committed ending: none

This run passed the established provider-deferred ending acceptance path: the browser-owned ending gate was ready, provider failures remained bounded and recoverable, a majority of structured requests returned usable responses, no hard protocol failure occurred, and the player remained in a valid interactive phase.

The provider sample did not enter Combat Mode, so it is compatibility/resilience evidence for the v1.6.10 runtime rather than proof of firearm mechanics. Firearms / Impaling correctness is established by the 45 deterministic focused tests.

## Authority / compatibility

- AI may narrate browser-confirmed firearm and Impale outcomes but cannot roll or commit them.
- Firearm hit/miss, point-blank, Dive for Cover, initiative modifier, Impale, Armor, and HP consequences are browser-owned.
- Player assertions and AI prose cannot create firearm hits or wounds.
- Save Schema remains 8 and AI protocol remains 1.3.
- No extra normal AI round trip is introduced by Firearms / Impaling resolution.
- **BLOCK UNSAFE STATE, NOT PLAYER ACTION** remains the interaction invariant.
