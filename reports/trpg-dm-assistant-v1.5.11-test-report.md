# TRPG DM Assistant v1.5.11 Test Report

## Release identity

- APP_VERSION: `1.5.11`
- Save Schema: `8` (unchanged)
- AI protocol: `1.3` (unchanged)
- NPC Knowledge Boundary: `1.0`
- Product entry: `outputs/trpg-dm-assistant.html`

## Release invariant

v1.5.11 makes protected NPC knowledge browser-validated instead of implicitly inheriting the KP/model's omniscient context.

The permanent interaction invariant remains:

> **BLOCK UNSAFE STATE, NOT PLAYER ACTION.**

The boundary blocks an unauthorized NPC knowledge result from becoming canonical state or a confirmed NPC statement. It does not block the player from asking, accusing, guessing, showing evidence, lying, or otherwise interacting with the NPC.

NPCs may still say they do not know, refuse to answer, lie, guess, misremember, or react emotionally. The boundary is specifically about turning a protected fact that an NPC has not learned into an authoritative known fact.

## Author-owned protected facts

A structured scenario may declare `director.knowledgeFacts`.

Each protected fact contains:

- `id`
- `text`
- `aliases`
- `knownBy`: NPC IDs that know the fact from scenario start / authored continuity
- `learnableFromClueIds`: canonical clue IDs that may legitimately teach the fact

The browser validates these definitions before replacing the currently active case.

Rejected configurations include:

- duplicate fact IDs;
- missing fact text / usable aliases;
- `knownBy` references to NPCs that do not exist in the scenario;
- `learnableFromClueIds` references to clues that do not exist;
- ambiguous aliases shared across multiple protected facts;
- fact count beyond the bounded authoring limit.

## NPC canonical knowledge state

NPC continuity is extended lazily with:

- `knownFactIds`
- `knownClueIds`

These fields survive normal Schema 8 save/load normalization. Existing saves and scenarios without `director.knowledgeFacts` do not require a schema migration.

Initial `knownFactIds` are derived from authored `knownBy` declarations when an NPC is materialized or normalized.

## Player-to-NPC knowledge propagation

The model may propose `updateNpc.learnClueIds` / `updateNpc.learnFactIds`, but those proposal fields are not authority.

The browser accepts a learning proposal only when all required evidence is present:

1. the relevant clue is already revealed in canonical player state;
2. the current player action explicitly shows, tells, explains, hands over, or otherwise discloses that clue to the target NPC;
3. the target NPC is actually referenced by the player's disclosure action;
4. an authored fact may be learned only when the disclosed clue appears in that fact's `learnableFromClueIds`.

After validation, the browser rewrites the proposal to internal trusted fields (`knowledgeClueIds`, `knowledgeFactIds`) and a private validation marker. Model-supplied copies of these trusted fields are stripped before validation, preventing the model from forging browser authorization.

A legitimate knowledge-only `updateNpc` is also supported. This is required for actions such as "I show the ledger to the butler" where the only canonical consequence is that the NPC learned something. The old `updateNpc` protocol required a description / attitude / claim / continuity text field, so v1.5.11 adds a narrow browser-trusted path for knowledge-only commits instead of fabricating unrelated NPC state.

## Output boundary

Protected knowledge is enforced in two places.

### Canonical NPC state

Unauthorized protected facts in NPC `claim`, `lastInteraction`, or `description` updates are removed locally. Unrelated legal fields in the same `updateNpc` are preserved.

If removing the unauthorized field leaves no remaining valid operation, that operation disappears instead of turning the player's action into a technical failure.

### NPC narrative

A protected fact spoken as known by an unauthorized NPC is locally neutralized. The leaking sentence is replaced by a neutral statement that the NPC did not provide confirmatory information. Surrounding safe narration remains.

Explicit negative knowledge such as "I don't know" / "I cannot confirm that" is allowed and is not misclassified as a leak.

Environmental/KP narration that merely contains a protected phrase is not automatically treated as NPC speech; the guard requires an NPC attribution plus a speech/knowledge pattern.

## First authored example: Old House

`scenario-old-house` contains the first protected knowledge set:

- `old-secret-door-fact`
  - initially known by `old-butler`
  - learnable from `old-scratch` / `old-blueprint`
- `old-low-temp-plan-fact`
  - initially known by `old-shen`
  - learnable from `old-ledger`
- `old-underground-experiment-fact`
  - initially known by `old-shen`
  - learnable from `old-notes`
- `old-shen-location-fact`
  - initially known by `old-shen`
  - no clue-based learning route

This allows the butler to know the study's secret-door fact without automatically knowing the deeper illegal-experiment truth simply because the KP/model knows it.

## Validation defect: director normalizer dropped knowledgeFacts

The first executable v1.5.11 test found that `director.knowledgeFacts` existed in the authored scenario source but disappeared after scenario activation.

Root cause:

- `prepareScenarioForPlay()` normalizes the director block;
- `normalizeDirectorSituation()` used an explicit allowlist of existing director fields;
- the new `knowledgeFacts` field was therefore discarded.

Fix:

- v1.5.11 wraps the existing director normalizer;
- all pre-existing normalized director fields are preserved;
- `knowledgeFacts` are normalized and appended through the same formal scenario preparation path.

The implementation does not bypass scenario preparation or maintain a second shadow scenario object.

## Validation defect: knowledge-only updateNpc was rejected

The next executable integration test found that a valid knowledge transfer with no other NPC mutation was rejected by the old `updateNpc` contract:

`必须提供 description、attitude 或 NPC 连续性字段`

This is a real integration gap because learning a clue can be the only canonical social consequence of a turn.

Fix:

- ordinary `updateNpc` operations continue through the existing protocol unchanged;
- internally validated knowledge-only operations are separated before the old validator;
- the old validator handles all ordinary operations and all unrelated state changes;
- the browser applies the validated knowledge IDs directly to the draft NPC continuity;
- the operation still participates in normal prepared-change counts / summaries;
- untrusted AI cannot manufacture the internal authorization marker.

No existing `updateNpc` business permission was relaxed.

## Interaction availability

The knowledge boundary composes with v1.5.6/v1.5.7 guards.

Examples:

- player asks an NPC about a secret they do not know → interaction continues; NPC may deny knowledge, refuse, lie, or react;
- AI incorrectly makes that NPC reveal the protected fact → only the unsafe statement/state is removed;
- AI also updates a legitimate relationship or item in the same response → that legal consequence remains;
- player fabricates "the butler told me X" → existing Player Assertion Guard and the NPC knowledge boundary both prevent the assertion from becoming canonical truth, without creating a player-facing dead end;
- unknown operations remain strict protocol failures.

## Request-context integration

The structured request payload now contains `npcKnowledgeBoundary` with:

- `authority: browser_validated_npc_knowledge`
- protected facts and their authoring metadata;
- each relevant NPC's `knownFactIds` / `knownClueIds`;
- explicit `allowedFacts` / `forbiddenFacts`.

The system prompt explicitly states that KP omniscience must not be treated as NPC knowledge, while also stating that the rule cannot be used to block ordinary social interaction.

No additional API request is added.

## v1.5.11 deterministic regression

Test file:

`trpg-dm-assistant/build/test-v1511-npc-knowledge-boundary.js`

Final temporary validation Run `31347802971`: **34 PASS / 0 FAIL**.

Coverage includes:

1. v1.5.11 identity / Schema 8 / protocol 1.3.
2. Old House authored knowledge facts.
3. Initial known fact assignment.
4. Initial forbidden fact boundary.
5. Later NPC materialization with authored knowledge.
6. Explicit allowed / forbidden request context.
7. Known protected claim remains valid.
8. Unknown protected claim is stripped while legal adjacent NPC state remains.
9. Pure unauthorized claim is removed without protocol dead-end.
10. NPC narrative leak is locally neutralized.
11. Explicit "does not know" is not falsely blocked.
12. Asking alone does not teach the NPC.
13. Unrevealed clues cannot teach.
14. Revealed clues not actually disclosed cannot teach.
15. Explicit disclosure of a revealed clue can teach the clue.
16. A declared clue source can authorize a protected fact in the same turn.
17. Legal knowledge propagation commits to NPC continuity.
18. Learned knowledge survives Schema 8 normalization.
19. A shallow fact does not imply a deeper protected fact.
20. A deeper fact requires its own declared clue source.
21. Unsafe knowledge stripping preserves unrelated legal operations.
22. Ordinary unprotected NPC claims remain compatible.
23. KP/environment narration is not mistaken for NPC speech.
24. Fabricated player NPC quotes remain recoverable under existing guards.
25. Unknown operations remain strict.
26. Scenarios without authored knowledge facts retain legacy NPC behavior.
27. Invalid `knownBy` reference is statically rejected without replacing current case.
28. Invalid clue reference is statically rejected.
29. Duplicate fact ID is statically rejected.
30. Cross-fact alias collision is statically rejected.
31. Request payload exposes browser-owned knowledge authority explicitly.
32. Prompt requires non-omniscient NPC behavior while preserving interaction.
33. Diagnostic package exposes boundary state.
34. Production build order loads NPC Knowledge Boundary after Authored Threat Clock.

## Historical regression compatibility

The same successful temporary validation Run `31347802971` ran the full historical deterministic suite from security hardening through v1.5.10. Every group passed, including:

- Security Hardening: 11 PASS
- Save UI: 9 PASS
- CoC outcome: 14 PASS
- Situation UI: 10 PASS
- AI JSON repair: 11 PASS
- v1.5.0: 18 PASS
- v1.5.1: 25 PASS
- v1.5.2: 26 PASS
- v1.5.3: 17 PASS
- v1.5.4 protocol routing: 10 PASS
- v1.5.4 NPC materialization: 10 PASS
- v1.5.4 location alias: 7 PASS
- v1.5.4 empty-response retry: 12 PASS
- v1.5.4 operation aliases: 11 PASS
- v1.5.5: 9 PASS
- v1.5.6: 25 PASS
- v1.5.7 Case Integrity / Interaction Availability: 34 PASS
- v1.5.7 canonical assertion: 7 PASS
- v1.5.8 API Response Resilience: 20 PASS
- v1.5.9 Progress Semantics: 23 PASS
- v1.5.10 Authored Threat Clock: 32 PASS

The v1.5.10 release-identity assertion was changed from exact `1.5.10` equality to numeric `>= 1.5.10`; its 32 behavior assertions were not relaxed.

The permanent suite target increases from 341 to **375 deterministic PASS / 0 FAIL**.

## Build validation

Final temporary validation Run `31347802971` generated:

- product: `outputs/trpg-dm-assistant.html`
- size: **484411 bytes**
- single HTML verifier: PASS
- deterministic double build: PASS
- exactly one TRPG product HTML: PASS
- JavaScript syntax: PASS
- `git diff --check`: PASS

## Real API decision

A separate real-provider run is not required as a release gate for v1.5.11.

The prompt/context contract is expanded, but the security property does not depend on the model obeying it: the deterministic browser boundary is explicitly tested against unsafe model-shaped outputs. The transport pipeline, AI protocol version, retry behavior, and request count are unchanged. A model that follows the new context produces cleaner narration; a model that ignores it still cannot directly commit the protected NPC knowledge covered by the authored boundary.

## Permanent PR CI evidence

Release PR: **#8**.

First cleaned PR head:

`3a46de5e79d15917bc4d74ceb24bf3005d892903`

Permanent `TRPG DM Assistant CI` Run `31348112458`: **SUCCESS**.

That run passed every permanent historical regression group, the v1.5.11 NPC Knowledge Boundary suite, JavaScript syntax, and deterministic single-HTML build / verify. With the new 34 v1.5.11 cases, the permanent deterministic suite is **375 PASS / 0 FAIL**.

## Release decision

All release-preparation gates are complete:

1. README updated to v1.5.11;
2. v1.5.11 regression added to permanent `TRPG DM Assistant CI`;
3. temporary v1.5.11 validation workflow and all four patch helpers removed;
4. final diff audited against `main` with only formal release files remaining;
5. release PR #8 opened;
6. permanent CI passed on the cleaned PR head;
7. `main` remains unchanged until an explicit merge request.

This report-only evidence update intentionally changes the PR head once more. The permanent CI for that final exact head is the last merge gate.
