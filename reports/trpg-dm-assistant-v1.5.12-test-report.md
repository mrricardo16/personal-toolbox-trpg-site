# TRPG DM Assistant v1.5.12 Test Report

## Release identity

- APP_VERSION: `1.5.12`
- Save Schema: `8` (unchanged)
- AI protocol: `1.3` (unchanged)
- Ending / Resolution Gate: `1.0`
- Product entry: `outputs/trpg-dm-assistant.html`

## Release invariant

v1.5.12 makes ending eligibility browser-owned canonical state rather than model narrative authority.

The permanent interaction invariant remains:

> **BLOCK UNSAFE STATE, NOT PLAYER ACTION.**

The AI may propose a resolution, victory, failure, withdrawal, escape, or other authored ending. The proposal itself cannot end the campaign. The browser evaluates the current canonical state and decides whether that ending is eligible to commit.

A premature known ending is recoverable: only the unsafe ending result is removed. Legal state changes from the same turn remain valid, and the player stays in a playable interaction state.

Unknown ending IDs, unknown operations, malformed protocol data, and check-stage premature effects remain strict protocol failures.

## Existing ending weaknesses addressed

Before v1.5.12, the project already had `endingConditionMatches()` and `availableEndings()`, but three boundaries were incomplete:

1. a known `endingProposal` whose conditions were not yet met caused the entire AI transaction to fail;
2. `confirmEndingProposal()` trusted the proposal snapshot and did not re-check current state, leaving a proposal-to-confirm state-drift window;
3. `applyEnding()` itself did not enforce a unified gate, so an internal/UI call with an ending object could bypass the proposal validator.

v1.5.12 closes all three without removing player-facing withdrawal or other authored paths.

## Browser-owned authored conditions

The gate keeps all existing ending conditions:

- `alwaysAvailable`
- `requiredFlags`
- `forbiddenFlags`
- `minClues`
- `requiresAnyClueIds`
- `outcomeRequirements`

It adds optional canonical conditions:

- `requiredClueIds`: every listed clue must already exist in player canonical clue state;
- `requiredResolvedLeadIds`: listed investigation leads must be resolved;
- `requiredResolvedQuestionIds`: listed investigation questions must be resolved;
- `requiredNodeIds`: current canonical node must be one of the authored ending nodes;
- `requiredClockStates`: authored threat clocks may require `active`, `triggered`, or `resolved`;
- `requireNoActiveThreats`: director active threat set must be empty;
- `requiredSemanticKinds`: requires browser-generated Progress Semantics evidence from `DISCOVERY`, `ACCESS`, `SOCIAL`, `THREAT`, or `RESOLUTION`.

These conditions are descriptive gates only. They do not create clues, resolve leads, move the player, resolve clocks, or manufacture Progress Semantics.

## Same-transaction draft-state eligibility

Ending validation runs against the prepared transaction draft.

This preserves a valid existing flow: if the same AI response performs a legal canonical operation that satisfies the final condition and then proposes the corresponding ending, the proposal may become eligible in that turn.

For example, a legal `setScenarioFlag` may make an authored `requiredFlags` ending ready. The browser still validates the state operation first; the model cannot bypass the state-change protocol by merely naming the ending.

## Premature known-ending recovery

When the model proposes an existing ending whose canonical gate is not ready:

1. the browser identifies `ENDING_GATE_NOT_READY`;
2. only `endingProposal` is removed;
3. explicit terminal narration such as "the case ends here" or "final victory" is locally neutralized;
4. the remaining response is passed through the normal transaction validator again;
5. legal state changes from the same response remain eligible to commit;
6. runtime diagnostics record the recovered ending ID and unmet conditions.

This is deliberately narrow. `ENDING_PROPOSAL_UNKNOWN` is not swallowed.

The deterministic suite verifies the full prepare + commit path, not only the pure recovery transform: a legal flag change survives the recovered transaction and `runtime.endingResolutionGate.lastRecovery` is written.

## Narrative boundary

Narrative neutralization is only applied in the context of a real known-but-premature ending proposal.

Ordinary narration is not rewritten. The recovery only removes explicit claims that the case/story has ended, a final ending has been reached, or final victory/failure has already been established. It then adds a short continuity statement that already validated consequences remain in force while the case is not formally resolved.

This keeps the defense from turning into a generic prose censor.

## Confirmation-time revalidation

`confirmEndingProposal()` now resolves the current canonical ending definition and re-evaluates the gate at confirmation time.

If the state changed after the proposal was created:

- no ending is committed;
- pending ending state is cleared;
- phase returns to `awaiting_player_action`;
- revision advances;
- a continuity message explains that the ending conditions changed;
- the player can continue normally.

This closes the proposal-to-confirm TOCTOU window.

## Unified actual-ending commit gate

`applyEnding()` is wrapped by the same canonical evaluator.

Every actual ending commit now:

1. resolves the current authored ending by ID from the active scenario;
2. rejects stale/unknown ending objects;
3. evaluates the current canonical gate;
4. commits only if ready.

A valid commit still passes through the existing Progress Semantics wrapper, so the final canonical ending produces `RESOLUTION` evidence.

`alwaysAvailable` remains intentionally ready. This preserves explicit player withdrawal / abort investigation paths and prevents the new defense from trapping a player in a case.

## Fail-closed authored configuration

The initial executable design review identified two authoring risks that would have made bad configuration more permissive:

- invalid `requiredSemanticKinds` could have been silently filtered out;
- invalid threat-clock states could have been normalized to a legal default.

The final implementation is fail-closed instead.

At runtime an invalid required semantic or invalid clock state cannot satisfy the gate. At Case Integrity time these become blocking errors:

- `ENDING_GATE_SEMANTIC_INVALID`
- `ENDING_GATE_CLOCK_STATE_INVALID`

Other static checks include:

- missing `requiredNodeIds` node -> blocking `ENDING_GATE_NODE_MISSING`;
- missing `requiredClockStates.clockId` -> blocking `ENDING_GATE_CLOCK_MISSING`;
- statically unknown required clue -> WARN because legal runtime/dynamic clues may still exist;
- statically unknown lead/question -> INFO when runtime creation cannot be disproved.

This preserves the Case Integrity rule: ERROR is for structurally provable damage, not merely dynamic uncertainty.

## Request-context integration

Structured AI requests now contain `endingResolutionGate` with:

- version `1.0`;
- authority `browser_canonical_resolution`;
- each authored ending's `ready` state;
- `alwaysAvailable` state;
- compact unmet-condition labels.

The system/user prompts state that only a `ready=true` ending may be proposed and that the AI must continue ordinary interaction if no ending is ready.

The prompt is advisory. Security does not depend on model compliance; the browser revalidates the response deterministically.

No additional AI request is introduced.

## v1.5.12 deterministic regression

Test file:

`trpg-dm-assistant/build/test-v1512-ending-resolution-gate.js`

Final temporary validation Run `31350516046` completed successfully after the README was also included in the validation path.

The hardened v1.5.12 suite contains **46 PASS / 0 FAIL**.

Coverage includes:

1. v1.5.12 identity / Schema 8 / protocol 1.3 / gate version.
2. `alwaysAvailable` remains ready.
3. legacy required flag rejection.
4. legacy required flag acceptance.
5. legacy forbidden flag behavior.
6. legacy minimum clue behavior.
7. all required clue IDs.
8. any-of clue IDs.
9. resolved lead requirement.
10. resolved question requirement.
11. real current-node rejection.
12. real `enterNode()` satisfaction.
13. active clock state.
14. triggered clock state.
15. resolved clock state.
16. no-active-threats requirement.
17. Progress Semantic requirement.
18. legacy outcome requirement.
19. multi-condition AND semantics.
20. available-ending filtering and priority.
21. dedicated unready ending code.
22. unknown ending remains strict.
23. same-transaction state change can satisfy ending eligibility.
24. premature proposal stripping preserves legal state.
25. explicit terminal narrative is locally neutralized.
26. ordinary narrative is preserved.
27. ready ending proposal does not trigger recovery.
28. confirmation re-check rejects state drift.
29. direct `applyEnding()` cannot bypass gate.
30. ready `applyEnding()` commits campaign end.
31. actual ending commit still records `RESOLUTION` Progress Semantic.
32. player withdrawal via `alwaysAvailable` remains possible.
33. context exposes browser authority.
34. request payload includes gate state.
35. system/user prompt restricts ending proposal to ready endings while preserving interaction.
36. diagnostics expose gate state and runtime.
37. old Schema 8 save lazily receives gate runtime.
38. missing authored node is blocking integrity error.
39. missing authored clock is blocking integrity error.
40. dynamic clue source uncertainty is WARN rather than blocking.
41. old endings without new fields retain legacy behavior.
42. check decision still rejects premature ending effects.
43. actual recovered commit writes diagnostics and preserves legal canonical change.
44. invalid required semantic is blocking.
45. invalid required clock state is blocking.
46. production build loads Ending / Resolution Gate after NPC Knowledge Boundary.

## Historical regression compatibility

The successful validation runs executed the complete historical deterministic suite from Security Hardening through v1.5.11. All historical groups passed.

The v1.5.11 release identity assertion was changed from exact `1.5.11` equality to numeric `>= 1.5.11`; its 34 NPC Knowledge Boundary behavior assertions were not relaxed.

Previous permanent total: **375 PASS / 0 FAIL**.

v1.5.12 adds 46 deterministic assertions, so the permanent complete-suite target is:

**421 PASS / 0 FAIL**.

## Build validation

Successful temporary validation generated:

- product: `outputs/trpg-dm-assistant.html`
- size: **500800 bytes**
- strict single HTML verifier: PASS
- deterministic double build: PASS
- exactly one TRPG product HTML: PASS
- JavaScript syntax: PASS
- `git diff --check`: PASS

The product build order is:

`... -> progress-semantics.js -> authored-threat-clock.js -> npc-knowledge-boundary.js -> ending-resolution-gate.js`

## Real API decision

A separate real-provider run is not required as a v1.5.12 release gate.

This release does not change provider transport, retry policy, AI protocol version, or API request count. The property under test is a deterministic post-response browser authorization boundary. The suite directly feeds model-shaped premature ending proposals and verifies that the browser strips only the unsafe ending, preserves legal state, revalidates confirmation, and refuses direct bypass.

A model following the new prompt should propose fewer premature endings; a model ignoring the prompt still cannot commit a protected ending through the paths covered by the gate.

## Permanent CI evidence

The first clean PR head was:

`90511ad82e0a4b52e639bb49d47793db2af0a186`

Permanent `TRPG DM Assistant CI` Run `31350703950` completed **SUCCESS** on that exact head. It passed every historical regression group, the v1.5.12 46-test gate suite, JavaScript syntax, and deterministic single-HTML build/verify.

This report update is documentation-only and does not modify product code. The resulting final PR head must receive another successful permanent CI run before merge.

## Release gate

Before merge:

1. README must identify v1.5.12 and document the gate;
2. permanent `TRPG DM Assistant CI` must include the v1.5.12 regression;
3. temporary validation workflow and patch helpers must be removed;
4. final diff must contain only formal release files;
5. release PR must target `main` from `agent/v1.5.12-ending-resolution-gate`;
6. permanent CI must pass on the exact final PR head with **421 PASS / 0 FAIL** plus JavaScript syntax and deterministic single-HTML build/verify;
7. do not merge automatically.
