# Project Documentation Index

## Purpose

本目录是 TRPG AI 主持助手的 authoritative project summary。

```text
ChatGPT Project = 完整设计历史、讨论与被否决方案
Git / docs      = 当前已确认结论、动态状态、施工边界
Codex / Agent   = 读取 docs + current repository 后执行工程任务
```

Repository 动态事实仍必须在施工前重新验证。

---

# Recommended Reading Order

1. [HANDOFF.md](HANDOFF.md)
2. [CURRENT_STATE.md](CURRENT_STATE.md)
3. [DESIGN_PRINCIPLES.md](DESIGN_PRINCIPLES.md)
4. [ARCHITECTURE.md](ARCHITECTURE.md)
5. [MULTIPLAYER_PLAN.md](MULTIPLAYER_PLAN.md)
6. [DECISION_LOG.md](DECISION_LOG.md)
7. [REJECTED_IDEAS.md](REJECTED_IDEAS.md)
8. [PRODUCT_VISION.md](PRODUCT_VISION.md)
9. [CONTEXT_MAP.md](CONTEXT_MAP.md)
10. [../KEYWORDS.md](../KEYWORDS.md)

---

# Documents

## HANDOFF.md
5 分钟接手文档：Current Goal、Phase、baseline、architecture、principles、Do Not Do、next task、open questions。

## CURRENT_STATE.md
动态事实：version、Schema、Protocol、regression、output、current phase、completed/next、risks、technical debt。施工前 MUST 再从 Repository 验证。

## DESIGN_PRINCIPLES.md
不能轻易破坏的原则，包括 AI/程序职责、Player Intent、Server Authoritative、projection、credential、small migration、test preservation。

## ARCHITECTURE.md
当前已确定的 Single Player、AI Protocol、Save Schema、Multiplayer Client/Server、Authority、State Layers、SignalR、Persistence、Deployment、Repository 与 rule migration direction。

## MULTIPLAYER_PLAN.md
Pre-Phase repository bootstrap、Phase 1 Foundation、Phase 2 Shared Game State、Phase 3 AI KP、Future 与 Open Questions。

## DECISION_LOG.md
ADR。尤其记录后续决定如何 supersede 旧方案。

## REJECTED_IDEAS.md
防止旧方案复活：full Vue rewrite、Browser Multiplayer authority、per-player AI、Phase 1 DB/Redis、Sites authoritative backend、full rule rewrite 等。

## PRODUCT_VISION.md
产品定义：项目是什么/不是什么、Single Player 价值、AI KP 定位、Multiplayer 愿景、BYOK、future roadmap、Project/Codex workflow。

## CONTEXT_MAP.md
用 date、conversation/file title、keywords 映射回 ChatGPT Project 历史，不依赖临时 turn/file citation。

## KEYWORDS.md
位于 `docs/`，作为 Codex Context Retrieval Index。

---

# Source Priority

## Implementation Facts

```text
Current Repository
>
CURRENT_STATE.md
>
old audit
>
old chat
```

## Design Decisions

```text
newer explicit ADR/docs
>
newer explicit conversation decision
>
older audit
>
older exploratory discussion
```

## Unknowns

无法确认时标记：

```text
OPEN QUESTION / NEED USER CONFIRMATION
```

不要自动生成折中方案。

---

# Current Documentation Status

本包生成于 2026-08-14。

本轮已完成 Dedicated Repository Bootstrap：迁移稳定 Single Player、适配 standalone CI、固化 Context Docs，并推送目标 repository。

下一工程任务：Multiplayer Phase 1 first two commits。该阶段仍未开始。
