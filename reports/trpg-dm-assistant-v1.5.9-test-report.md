# TRPG DM Assistant v1.5.9 Test Report

## Release identity

- APP_VERSION: `1.5.9`
- Save Schema: `8` (unchanged)
- AI protocol: `1.3` (unchanged)
- Progress Semantics: `1.0`
- Product entry: `outputs/trpg-dm-assistant.html`

## Release invariant

v1.5.9 introduces browser-owned semantic classification of **committed canonical consequences**.

The semantic layer is descriptive, not authoritative:

- AI prose does not determine progress semantics.
- AI operation self-report does not determine progress semantics.
- Semantic tags cannot create clues, grant items, move locations, damage characters, advance clocks, or unlock endings.
- Classification happens from browser-owned canonical state snapshots before and after a committed operation.
- `NONE` is a valid result and must not be converted into fake progress.
- Existing `lastTurnImpact` remains for compatibility, but it is not a source of authority for v1.5.9 semantics.

The fixed semantic vocabulary is:

- `NONE`
- `DISCOVERY`
- `ACCESS`
- `SOCIAL`
- `THREAT`
- `RESOLUTION`

A single commit can contain multiple semantic kinds. `primary` is selected deterministically for downstream pacing, while `evidence` records the actual canonical differences that caused classification.

## Canonical evidence mapping

### DISCOVERY

Examples include:

- clue added or materially changed;
- revealed truth added;
- investigation lead resolved;
- unresolved question resolved.

### ACCESS

Examples include:

- actual current node changed;
- item acquired;
- item quantity increased.

### SOCIAL

Examples include canonical NPC continuity changes such as claims, relationship, attitude, description, or current intent.

### THREAT

Examples include:

- tension increased;
- HP decreased;
- SAN decreased;
- active threat added;
- threat clock advanced or triggered.

### RESOLUTION

Examples include:

- campaign outcome changed;
- ending committed;
- active threat removed;
- threat clock resolved.

## State and compatibility

Progress semantics are stored in optional campaign state:

```text
campaign.progressSemantics = {
  version,
  last,
  history
}
```

The history is capped at 80 entries. Old Schema 8 saves that do not contain this field are lazily initialized during load; no save migration or Schema bump is required.

The latest semantic context is included in safe context, world continuity, and diagnostics so future browser-owned pacing systems can consume it without inferring meaning from AI prose.

## v1.5.9 deterministic regression

Test file:

`trpg-dm-assistant/build/test-v159-progress-semantics.js`

Result in temporary validation Run `31319525912`: **23 PASS / 0 FAIL**.

Coverage:

1. v1.5.9 identity with Schema 8 / protocol 1.3 unchanged.
2. Exact six-category semantic vocabulary.
3. No canonical change → `NONE`.
4. Clue added → `DISCOVERY`.
5. Actual node change → `ACCESS`.
6. Item acquisition → `ACCESS`.
7. NPC canonical continuity change → `SOCIAL`.
8. Tension increase → `THREAT`.
9. HP loss → `THREAT`.
10. SAN loss → `THREAT`.
11. Threat clock advance → `THREAT`.
12. Threat clock resolved → `RESOLUTION`.
13. Campaign outcome change → `RESOLUTION`.
14. Lead resolution → `DISCOVERY`.
15. One commit can retain multiple semantic kinds with deterministic primary priority.
16. Legacy AI `turnImpact=transition` with no committed state change still yields `NONE`.
17. Actual `enterNode` records `ACCESS`.
18. Actual `applyEnding` records `RESOLUTION`.
19. Missing field in an old Schema 8 save is lazily initialized.
20. Semantics are available in safe context, world continuity, and diagnostics.
21. History is capped at 80 entries.
22. Pure classification does not mutate canonical state.
23. Production build loads Progress Semantics after API Response Resilience.

## Historical regression compatibility

The same successful validation run re-executed the most relevant prior safety layers:

- v1.5.7 Case Integrity / Interaction Availability: **34 PASS / 0 FAIL**
- v1.5.7 canonical assertion state: **7 PASS / 0 FAIL**
- v1.5.8 API Response Resilience: **20 PASS / 0 FAIL**

The v1.5.8 test's release-identity assertion was changed from exact `1.5.8` equality to `>= 1.5.8`, matching the existing forward-compatible historical-test pattern. Its API Resilience behavior assertions were not relaxed.

The permanent PR suite now totals **309 deterministic PASS / 0 FAIL**.

## Build validation

Temporary validation Run `31319525912` completed all runtime gates before release wiring was written back to the branch:

- v1.5.9 deterministic regression: PASS
- relevant historical regressions: PASS
- JavaScript syntax: PASS
- single HTML verifier: PASS
- two consecutive builds: deterministic
- exactly one TRPG HTML product entry: PASS
- `git diff --check`: PASS
- generated product size: **447708 bytes**

The first attempt to push validated release wiring failed only because a GitHub Actions `GITHUB_TOKEN` is not permitted to modify `.github/workflows/trpg-ci.yml` without workflow permission. No product or test gate failed. The workflow was then restricted to ordinary release files, and permanent CI wiring was written separately through the GitHub connector instead of broadening Actions permissions.

## Permanent PR CI

PR #6 clean release head `c0d8c103ce6d8f9fcaf88c16743b7a09382d9d31` was validated by permanent workflow Run `31319776880`.

Result: **SUCCESS**.

The permanent workflow passed every historical test group, the new v1.5.9 Progress Semantics regression, JavaScript syntax validation, and final single-HTML deterministic build verification.

## Real API decision

No new real DeepSeek run is required as a v1.5.9 release gate.

Reason:

- v1.5.9 does not change the AI protocol (`1.3`), provider transport, prompt contract, or request count;
- semantic classification is derived locally from post-commit browser state, not from model-declared semantic labels;
- deterministic canonical before/after fixtures directly exercise the classifier with more precision than nondeterministic model prose;
- v1.5.8 already validated the current real-provider request/recovery pipeline with 30 / 30 player-action E2E cases.

A future long-session E2E may audit how the semantic history behaves over an authored case, but model output is not an authority source for these tags.

## Release decision

The temporary validation workflow has been removed. The release diff contains only formal v1.5.9 files, and permanent PR CI has passed on the cleaned release code head. A final CI rerun on the documentation-updated PR head is the remaining merge gate.
