# TRPG DM Assistant v1.6.8 Test Report

## Release identity

- APP_VERSION: `1.6.8`
- Save Schema: `8`
- AI protocol: `1.3`
- Combat Round / Melee Opposed Contract: `1.0`
- Authority: `browser_coc_combat`
- Product: `outputs/trpg-dm-assistant.html`
- Product size: `624118 bytes`

## Objective

v1.6.8 introduces the first browser-owned combat round layer for CoC7. It owns explicit Combat Mode, DEX action order, melee Attack versus Dodge/Fight Back resolution, same-round outnumbered bonus tracking, and combat-round integration with the existing dying CON rules.

This release deliberately stops before weapon/armor damage generation. A winning melee exchange creates a browser-owned `damageDisposition`; it does not directly change HP. Weapon and armor damage remain a later browser rule layer.

The permanent principles remain:

**AI may propose or narrate; browser mechanics decide and commit.**

**BLOCK UNSAFE STATE, NOT PLAYER ACTION.**

## Combat Mode and round ownership

Combat Mode is explicit. Normal investigation/chat turns are not silently treated as combat rounds.

- participants are ordered by descending DEX;
- equal-DEX entries use stable insertion order as the implementation tie-breaker;
- each participant consumes one significant turn before the round wraps;
- round wrap resets per-round action/response counters;
- combat mutation is rejected while an AI request is active, avoiding revision races;
- Combat Mode can be ended explicitly without altering unrelated campaign state.

## Melee opposed resolution

Both attacker and defender rolls are browser percentile rolls using canonical participant values.

### Dodge

- attacker must achieve a strictly higher success level than the defender to hit;
- equal success levels favor the defender: the attack is dodged;
- if neither side succeeds, no damage disposition is created.

### Fight Back

- the defender must achieve a strictly higher success level to counter-hit;
- equal success levels favor the initiating attacker;
- if the defender wins, the damage disposition is capped at regular Fight Back damage even when the defender's roll reaches Extreme/critical;
- an initiating attacker that wins with Extreme/critical may receive `initiator_extreme_eligible` for the subsequent browser damage layer.

## Outnumbered tracking

Each melee defense increments the defender's per-round response count. Once the defender has already used their allowed response capacity in the current round, subsequent melee attacks against that defender receive one browser-owned bonus die.

The counter is reset when the round wraps.

## Damage authority boundary

v1.6.8 does not invent weapon damage or armor.

Successful exchanges create `damageDisposition` with:

- winner/owner;
- target;
- mode (`regular`, `initiator_extreme_eligible`, or `fight_back_regular_cap`);
- `pending=true`;
- `hpCommitted=false`;
- deferred authority `deferred_weapon_damage_engine`.

While Combat Mode is active, AI-origin `adjustHp` is stripped before canonical commit. Safe unrelated state changes in the same response remain available.

This prevents a split-authority failure where the browser decides who wins the exchange but AI prose decides the actual HP damage.

## Dying integration

v1.6.6 manual dying-round checks remain valid outside Combat Mode. During Combat Mode, the combat round engine owns the timing instead.

- the manual dying-round action is suppressed while Combat Mode is active;
- if the investigator is already dying when Combat Mode starts, round 1 is recorded as the first observed dying round;
- no dying CON is made at the end of that same round;
- the first automatic dying CON occurs at the end of the following combat round;
- each subsequent combat round continues the browser-owned dying CON while dying persists;
- a failed dying CON writes death and ends Combat Mode with `endReason="player_dead"`;
- after Combat Mode ends manually, unresolved dying returns to the existing manual v1.6.6 health action.

No AI request is added for these checks.

## Save compatibility and context

Save Schema remains `8`.

Old Schema 8 saves lazily receive an inactive `campaign.combat` state. No combat round, participant, response, winner, or damage result is invented during load.

Request payloads and diagnostics expose `cocCombatRound` with explicit browser authority. The system prompt tells the AI it may narrate the browser-owned combat state but may not decide attack rolls, Dodge/Fight Back winners, or apply combat HP changes.

## Deterministic validation

Focused suite:

- `build/test-v168-combat-opposed.js`
- **42 PASS / 0 FAIL**

Previous permanent baseline:

- **757 PASS / 0 FAIL**

v1.6.8 deterministic total:

- **799 PASS / 0 FAIL**

The focused suite covers participant validation, DEX ordering, equal-DEX stable tie behavior, duplicate-start rejection, Dodge and Fight Back equal-level semantics, higher-level win semantics, both-fail results, initiator Extreme eligibility, Fight Back regular-damage cap, per-round response tracking, outnumbered bonus, turn advancement, round wrap/reset, invalid/same-side attacks, deferred damage authority, AI combat HP stripping with safe-state preservation, manual dying suppression, automatic next-round dying CON timing, repeated dying checks, death-ending combat, post-combat manual dying restoration, AI-request race protection, payload/diagnostic/prompt authority, old-save initialization, build inclusion, and verifier inclusion.

JavaScript syntax, deterministic double build, and the single-HTML verifier all passed.

Final single-HTML product size:

- **624118 bytes**

## Real DeepSeek current-runtime evidence

Release gate:

- Workflow Run: `31762927920`
- Result: SUCCESS
- model: `deepseek-v4-flash`
- actions: `8`
- browser checks observed in provider sample: `0`
- structured requests: `8`
- provider-successful structured requests: `6`
- API attempts: `14`
- automatic retries: `6`
- provider empty responses: `8`
- retry exhaustion: `2`
- graceful fallbacks: `2`
- technical leaks: `0`
- ending proposals: `1`
- ending confirmations: `1`
- provider-deferred ending: `false`
- final phase: `campaign_ended`
- ending: `ending-solved`

The provider sample did not enter Combat Mode. Combat mechanics are therefore proven by the 42 focused deterministic tests. The real-provider evidence verifies that the complete v1.6.8 runtime, including the new combat payload/prompt layer, remains compatible with the long-case current-runtime path and survives provider empty-response volatility without a technical dead end.

## Compatibility

- Save Schema remains `8`.
- AI protocol remains `1.3`.
- no additional normal AI round trip is introduced by combat mechanics;
- v1.6.5 remains authoritative for actual trusted HP damage state once a later damage engine commits HP;
- v1.6.6 remains authoritative for dying CON mechanics and First Aid stabilization;
- v1.6.7 remains authoritative for healing recovery;
- all earlier consequence, Guard, NPC knowledge, threat clock, ending and long-case boundaries remain intact.

## Deferred v1.6 work

- weapon damage generation and damage bonus;
- armor reduction;
- firearms/readied-firearm DEX handling, point-blank and dive-for-cover rules;
- richer authored NPC combat profiles;
- fighting maneuvers/build;
- SAN condition recovery and expiry;
- broader non-combat opposed/extended resolution.
