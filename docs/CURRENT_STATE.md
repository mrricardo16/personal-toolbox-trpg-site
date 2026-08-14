# CURRENT_STATE

## 1. 文档定位

这是动态状态文档。任何 Codex 开始工作前 MUST 重新从 current Repository 验证动态事实。

Current verification date：**2026-08-14**

---

# 2. Current Verified Implementation Facts

Source repository：

```text
mrricardo16/personal-toolbox
└─ trpg-dm-assistant/
```

Imported into dedicated repository：

```text
mrricardo16/personal-toolbox-trpg-site
└─ repository root
```

Verified branch：`main`

- APP_VERSION：`1.6.10`
- Save Schema：`8`
- AI Protocol：`1.3`
- Regression baseline：`892 PASS / 0 FAIL`
- v1.6.10 focused suite：`45 PASS / 0 FAIL`
- Current stable product：`outputs/trpg-dm-assistant.html`
- Formal HTML size：`662681 bytes`
- Latest verified TRPG commit：`10188be886e6609feca8a2a426dfdf8426a49a0e`
- Commit title：`Release TRPG DM Assistant v1.6.10 Firearms / Impaling`

---

# 3. Current Source Shape

```text
README.md
build/
outputs/
reports/
src/
```

`src/` 已经按功能拆成多 JS files。正式 single HTML 是 build artifact，不是唯一 source file。

---

# 4. Current CI

Current `trpg-ci.yml`：

- path scoped to TRPG project；
- Node 24；
- 运行现有 security/save/AI/scenario/CoC/SAN/HP/combat/firearms regressions；
- JavaScript syntax check；
- single HTML build/verify；
- deterministic double build/hash；
- generated output diff check；
- 验证正式 HTML artifact 唯一。

Current `trpg-real-api.yml`：

- manual workflow；
- deterministic preflight；
- 通过 `DS_KEY` repository secret 传入 runtime；
- real DeepSeek full-case E2E。

---

# 5. Current Development Phase

```text
Phase 0 Architecture Audit       ✅ completed
Project Context Consolidation    ✅ completed
Dedicated Repository Bootstrap   ✅ completed
Multiplayer Phase 1              ⏸ not started
```

---

# 6. Completed

## Stable Single Player

已完成并进入当前 baseline 的主要能力：

- structured state；
- save/import/export；
- Scenario Engine；
- Scenario Integrity；
- AI Protocol；
- state transaction；
- Player Action Guard；
- interaction availability；
- API response resilience；
- progress semantics；
- threat clock；
- NPC knowledge boundary；
- ending resolution；
- CoC resolution；
- consequence contract；
- failure forward；
- SAN loss / insanity tracking；
- HP damage state；
- dying / stabilization；
- healing recovery；
- combat opposed；
- combat damage / armor；
- Firearms / Impaling；
- deterministic regression；
- real provider acceptance workflow。

## Multiplayer Design

已完成：

- Multiplayer Architecture Audit；
- Server Authoritative decision；
- Vue 3 + Vite + TypeScript client decision；
- ASP.NET Core + SignalR server decision；
- BYOK/Host Provided API decision；
- Memory Only Phase 1 decision；
- Room/Credential boundary；
- PlayerSessionToken requirement；
- Reconnect principle；
- Player Projection requirement；
- phased migration plan；
- repository bootstrap prompt；
- Phase 1 first-two-commits prompt。

---

# 7. In Progress

当前没有 Multiplayer code implementation 正在进行。

当前 Bootstrap 已完成：稳定 Single Player 已导入 standalone repository，CI 路径已适配，Context Docs 已固化。

本轮未创建 Multiplayer implementation。

---

# 8. Next — Multiplayer Phase 1 First Two Commits

仅：

1. ASP.NET Core server skeleton；
2. `IRoomStore` + `InMemoryRoomStore` + minimal Room model/tests。

完成后 STOP。

---

# 9. Known Risks

## High

- shared global state；
- late module wrapping / monkey-patch dependency；
- canonical vs secret projection；
- concurrent Room mutations；
- configurable outbound AI endpoint SSRF；
- JS→Server semantic drift。

## Medium / High

- Single character → multiple player characters；
- current Combat assumes one main player character；
- AI Protocol multi-actor evolution；
- reconnect/host lifecycle；
- credential cleanup timing。

## Medium

- Save Schema 8 与 Multiplayer snapshot boundary；
- action batching policy；
- Room command scheduling。

---

# 10. Known Technical Debt

当前 Single Player 中已有但暂不要求立即清理：

- fixed script load order；
- global `state`；
- global helper dependency；
- function wrapping / decoration chain；
- `memory.js` 混合 runtime + UI；
- `check-engine.js` 混合 pure-ish rules 与 DOM/form logic；
- `scenarios/library.js` 同时承载 constants/default config/scenario data；
- `ui.js` 强 DOM coupling；
- Single Player API key persistence 属于 browser-side BYOK model。

这些 technical debt MUST NOT 在 Phase 1 被顺便全量重构。

---

# 11. Dynamic Facts Requiring Re-verification Before Coding

- APP_VERSION；
- Save Schema；
- AI Protocol；
- deterministic test count；
- current `main` commit；
- file tree；
- CI commands；
- dedicated repo 是否已存在；
- installed .NET SDK；
- installed Node/npm；
- required GitHub secrets 是否存在。

---

# 12. Historical Evidence Gaps

本次可检索历史没有足够直接证据说明：

- “之前 Codex 开发结果不满意”的具体失败案例；
- “为什么后来继续由网页版 ChatGPT 开发”的明确单一原因；
- 该切换发生的确切日期/对话标题。

已能确认的是：

- 项目长期在 ChatGPT Project 中积累设计和开发上下文；
- 2026-08-14 已生成专门的 Codex Repository Bootstrap 与 Phase 1 task prompts；
- 当前工作方式明确准备把工程执行逐步交给 Codex，同时用 Project/Docs 固化设计上下文。

若未来找到原对话，应补入 `CONTEXT_MAP.md`，不要反向编造原因。
