# Phase 2G Commit 2 Aggregate Report

## Repository

- Feature Commit 1: `c639b4a8b0c436e3fff4d391df0615b4e7db3ca0` — `feat: migrate combat damage rules`
- Feature Commit 2: `feat: integrate multiplayer combat damage` (the final commit SHA is reported by the post-commit synchronization check).
- Baseline before Commit 2: `c639b4a8b0c436e3fff4d391df0615b4e7db3ca0`, synchronized with `origin/main`.

## Server

- Restore: PASS.
- Build: PASS, 0 warnings / 0 errors.
- Test: 329 / 329 PASS.
- Format verify-no-changes: PASS.

## Client

- `npm ci`: PASS; 0 vulnerabilities. The existing transitive `glob@10.5.0` deprecation warning was observed.
- Vitest: 23 / 23 PASS.
- Production build: PASS.

## Conformance

- Check: 19 cases.
- HP: 10 cases.
- Stabilization: 21 cases.
- Combat Opposed: 19 cases.
- Combat Damage: 48 cases.
- Combat Damage exporter SHA 1: `0036133BF2BF1F37CBEF7DC7707832C258DE869870B46C17ADCA748FE905CEE7`.
- Combat Damage exporter SHA 2: `0036133BF2BF1F37CBEF7DC7707832C258DE869870B46C17ADCA748FE905CEE7`.
- Identical: YES.

## Single Player Regression Selection

The repository contains 40 files matching `build/test-*.js`.

- 37 scripts are explicitly executed by `.github/workflows/trpg-ci.yml` and are the authoritative offline regression baseline.
- `build/test-real-api-v1513.js` and `build/test-real-api-v152.js` are credentialed real-API acceptance scripts and were NOT RUN / NOT REQUIRED.
- `build/test-protocol-stability.js` is a legacy compatibility alias that only requires and re-executes `test-security-hardening.js`; it is not an independent authoritative regression suite.

Validation:

- Authoritative offline regressions: 37 / 37 PASS.
- Legacy compatibility alias: `test-protocol-stability.js` PASS.
- Credentialed real API: NOT RUN / NOT REQUIRED.
- JavaScript syntax: 69 / 69 PASS.
- Formal HTML: PASS; `VERIFY_SINGLE_HTML:PASS`; one HTML output; double-build SHA unchanged at `0A635D94CDD7284B35433092C834D92BCAD44961C14E30DBD565AB48B7E14D4D`.

## Core Invariants

- Consumed stale replay: PASS; no extra RNG, HP/vitality mutation, repair, revision, commit, or broadcast.
- Pending stale revision: rejected before RNG.
- Post-RNG replacement failure: internal `CombatDamageCommitInvariantException`, not retryable `StateConflict`; automatic re-roll: NO.
- Exactly once / Pending to Consumed: PASS; history trimming does not destroy consumption truth.
- Zero damage consumption: PASS; HP call: NO.
- Major Wound CON: only when the shared HP boundary requires it.
- Opponent vitality and investigator `IHpDamageEngine` authority: PASS.
- Dying is not Death; one investigator death does not automatically end combat: PASS.
- Stable order repair and already-wrapped no-double-wrap: PASS.
- Projection/privacy, commit-before-broadcast, stale-replay silence, reconnect without re-roll or revision mutation: PASS.

## Scope

- Public Combat API: 0.
- Vue Combat Damage controls: 0.
- Client Combat Damage authority: NO.
- Firearms: NO.
- Impaling: NO.
- Player Combat Intent: NO.
- Persistence: NO.

## Encoding and Diff

- UTF-8: PASS for every edited text file.
- `git diff --check`: PASS.

## Escalation Required

NO.
