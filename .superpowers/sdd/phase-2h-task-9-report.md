# Phase 2H Task 9 report

## Scope

Implemented the approved player combat intent client boundary only. The client receives server-projected `viewerActions`, submits exact attack/respond/pass intents, and renders no combat rules engine or optimistic combat state.

## TDD evidence

Strict UTF-8 precheck passed before editing all seven pre-existing authorized client files.

RED command:

```powershell
Push-Location multiplayer/client
npm test -- --run src/api/client.test.ts src/components/HomeView.test.ts src/state/gameSnapshot.test.ts
Pop-Location
```

RED result: 27 tests total; 9 expected failures and 18 passing. The failures identified missing three combat API methods, missing structured stale-error properties, and missing projected controls.

GREEN result: 3 test files passed; 27/27 tests passed.

## Validation

`npm run build` passed (`vue-tsc --noEmit && vite build`).

The UI uses only the viewer-projected actor, target IDs, and response values. A stale `409 stale_game_revision` fetches one authoritative game snapshot, emits it, shows `Combat changed; choose again`, and does not replay the mutation. No Start, End, Damage, NPC, dice, opposed-result, turn, or round-calculation control was added.

## Review-fix evidence

Review fixes add an explicit safe structured-error allowlist and prevent a selected target from being submitted after a newer authoritative snapshot removes it from `eligibleTargetParticipantIds`.

Review-fix RED used the same focused command above. Result: 29 tests total; 2 behavior failures and 27 passing. The failures proved unapproved structured error fields were retained and a no-longer-eligible selected target was still enabled.

Review-fix GREEN: 3 test files passed; 29/29 tests passed. `npm run build` passed again. Strict UTF-8 validation passed for all eight authorized files, and `git diff --check` passed.

## Finite revision coverage

Added exact JSON coverage for `{"code":"stale_game_revision","currentGameRevision":1e999}`. This is coverage for the existing finite-number guard, so it passed immediately: `src/api/client.test.ts` passed 13/13 and the exact Task 9 focused suite passed 30/30. The resulting error retains only the safe stale code and omits `currentGameRevision`.
