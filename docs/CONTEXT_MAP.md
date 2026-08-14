# CONTEXT_MAP

## 1. 用途

把正式文档中的主题映射回 ChatGPT Project / File Library 历史。长期索引使用日期、对话标题、文件标题和稳定关键词，不依赖临时 turn/file ID。

---

# 2. Project Origin / Single HTML Prototype

## Relevant conversation

### 2026-08-05 — 跑团API设计方案

可确认主题：

- 最初方向为单人使用；
- 单 HTML；
- 玩家与 AI（DM/KP）通过 API Roleplay；
- 用户提供自己的 API key。

## Search keywords

```text
跑团API设计方案
单html
玩家与ds
DM
KP
roleplay
```

---

# 3. Early Single Player Evolution

## Relevant files

2026-08-06 File Library：

- `trpg-dm-assistant-v1.4.0.html`
- `trpg-dm-assistant-v1.4.1.html`
- `trpg-dm-assistant-v1.4.2.html`
- `trpg-dm-assistant.html`

这些早期 artifact 已出现 structured state/campaign/AI operation 等持续演进迹象。

## Search keywords

```text
v1.4
stateChanges
campaignChanges
addPinnedFact
TRPG DM Assistant
```

---

# 4. AI Authority / Browser Authority

## Relevant material

### 2026-08-14 — Multiplayer Architecture Audit

源代码级审计 v1.6.10 的 AI Protocol、Browser Authority、transaction、revision 和 rule layers。

## Search keywords

```text
AI Protocol 1.3
browser_coc_resolution
commitAiTransaction
requestId
baseRevision
AI负责叙事
程序负责规则
```

---

# 5. Player Action Authority

## Relevant code/docs

- `src/player-action-guard.js`
- Architecture Audit

## Search keywords

```text
player-action-guard
玩家行动权威边界
玩家输入不是世界事实
BLOCK UNSAFE STATE, NOT PLAYER ACTION
```

---

# 6. Scenario / Investigation Integrity

## Relevant code topics

- `scenario-engine.js`
- `case-integrity.js`
- clue-route regressions
- investigation stability
- progress semantics
- authored threat clock
- NPC knowledge boundary
- ending resolution gate

## Search keywords

```text
Scenario Engine
Case Integrity
clue route
critical clue
failure forward
threat clock
NPC knowledge
ending gate
```

---

# 7. CoC Rule Engine Evolution

Current v1.6.x release chain：

- v1.6.0 CoC Resolution
- v1.6.1 Mechanical Consequence
- v1.6.2 Failure Forward
- v1.6.3 SAN Loss
- v1.6.4 Insanity
- v1.6.5 HP Damage
- v1.6.6 Stabilization
- v1.6.7 Healing
- v1.6.8 Combat Opposed
- v1.6.9 Combat Damage
- v1.6.10 Firearms / Impaling

## Search keywords

```text
v1.6.0
SAN
HP
dying
healing
Combat Opposed
Combat Damage
Firearms
Impaling
browser-owned
```

---

# 8. Save / Memory / Resilience

## Relevant source topics

- `saves.js`
- `memory.js`
- `api-response-resilience.js`
- Real API acceptance

## Search keywords

```text
Save Schema 8
serializeSave
apiKey
turn snapshot
rollback
API response resilience
retry exhaustion
provider empty
```

---

# 9. Multiplayer Product Direction

## Relevant conversation

### 2026-08-14 — 网页支持Skill使用

后半讨论：Sites 的作用、在线/并发、从单人到联机、开房、Host 提供 key、后续匹配、多个真人玩家共享 AI。

## Search keywords

```text
联机游戏
开房
建房的时候需要key
匹配模式
一个剧本匹配多个
Sites
```

---

# 10. Multiplayer Architecture Audit

## Relevant file

### 2026-08-14 — `粘贴的 markdown (1)。md`

标题：`TRPG AI 主持助手 Multiplayer Architecture Audit 与迁移方案报告`

## Key topics

- Progressive Migration
- Vue 3 + Vite + TypeScript
- ASP.NET Core + SignalR
- Server Authoritative
- Canonical State
- Player Projection
- Credential Lifecycle
- Memory Only
- golden/conformance
- deployment

## Important superseded point

Audit 当时写：`Phase 1 暂留 personal-toolbox；Phase 1 stable 后迁 dedicated repo`。

搜索到该旧结论时，必须再读 `01_REPO_BOOTSTRAP_PROMPT.md`。

---

# 11. Dedicated Repository Bootstrap

## Relevant file

### 2026-08-14 — `01_REPO_BOOTSTRAP_PROMPT.md`

最新工作流决定：

- new repo `mrricardo16/personal-toolbox-trpg-site`
- PRIVATE
- old repo untouched
- verify legacy baseline
- copy stable project contents to new repo root
- migrate docs/CI
- validate tests
- only then create/push remote
- STOP before Multiplayer implementation

## Search keywords

```text
Dedicated TRPG Repository Bootstrap
personal-toolbox-trpg-site
old repository untouched
bootstrap
stable baseline
```

---

# 12. Phase 1 Codex Start

## Relevant file

### 2026-08-14 — `02_PHASE1_START_PROMPT.md`

要求：

1. 先读 authoritative docs；
2. 只 plan first two commits；
3. Commit 1 = server skeleton；
4. Commit 2 = InMemoryRoomStore；
5. build/test；
6. STOP。

## Search keywords

```text
Start Multiplayer Phase 1
server skeleton
InMemoryRoomStore
first two commits
STOP
```

---

# 13. ChatGPT Project / Codex Workflow

当前明确分工：

```text
ChatGPT Project = full design history
Git / Docs      = current decisions
Codex           = constrained engineering executor
```

## Search keywords

```text
Project Context Consolidation
Codex
设计记忆
handoff
authoritative docs
```

---

# 14. Codex Dissatisfaction / Web ChatGPT Development — Evidence Gap

用户要求查找“之前 Codex 开发结果不满意的原因”和“为什么后来继续由网页版 ChatGPT 开发”。

本次 Project/Library 检索没有找到足够直接的原始证据来可靠还原：

- 具体是哪次 Codex task；
- 具体错误是什么；
- 是否因为质量、上下文、额度、工作流或其他原因；
- 切换发生在哪个具体日期。

当前状态：

```text
OPEN QUESTION / HISTORICAL EVIDENCE MISSING
```

未来找到原对话后补 conversation title、date、concrete problem、resulting workflow decision。不要根据现在的工具偏好反推历史原因。

---

# 15. Sites Discussion

## Relevant conversation

### 2026-08-14 — 网页支持Skill使用

## Current conclusion

- Sites 不作为 authoritative Multiplayer Server；
- ASP.NET Core + SignalR 是 canonical runtime；
- Sites future frontend/demo role 尚未确定。

## Search keywords

```text
Sites
在线
部署
authoritative server
ASP.NET Core runtime
```

---

# 16. Repository Facts

需要当前事实时，不从聊天猜测。直接读取 current repository：

```text
README.md
src/
build/
outputs/
.github/workflows/trpg-ci.yml
.github/workflows/trpg-real-api.yml
```

关键词：

```text
APP_VERSION
SCHEMA_VERSION
AI_PROTOCOL_VERSION
deterministic
outputs/trpg-dm-assistant.html
```

聊天主要用于 Design Intent 和 Decision History。
