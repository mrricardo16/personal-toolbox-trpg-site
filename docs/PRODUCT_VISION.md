# PRODUCT_VISION

## 1. 文档定位

本文件定义 TRPG AI 主持助手“要做什么、不要做什么、长期希望成为怎样的产品”。

它是 Design Intent，不是版本发布说明，也不是 Marketing Copy。

状态标记：

- `FINAL DECISION`：已经确定。
- `CURRENT DIRECTION`：当前方向，可经工程验证后调整。
- `FUTURE`：长期可能性，不是当前任务。
- `OPEN QUESTION`：尚未确认。

---

## 2. 这个项目是什么

TRPG AI 主持助手的核心目标，是把大型语言模型的自然语言能力用于 TRPG 的：

- 主持叙事；
- NPC 扮演；
- 场景描述；
- 理解玩家自然语言行动；
- 生成合理的叙事反馈；
- 帮助推进调查与场景；

同时避免把本应确定、可验证的游戏机械全部交给 AI 自由发挥。

项目长期遵循：

> AI 负责叙事，程序负责规则。

这意味着 AI KP / AI DM 是主持与叙事层，而不是数据库、随机数生成器或最终规则裁判。

`FINAL DECISION`

---

## 3. 这个项目不是什么

本项目不是：

1. 让 LLM 完全自由决定所有游戏规则的聊天机器人；
2. 仅靠 Prompt 维持 HP、SAN、线索和战斗状态的 Roleplay 窗口；
3. 为了“现代化技术栈”而重写全部稳定规则的框架实验；
4. 第一阶段就包含账号、支付、排行榜、匹配和云存档的完整商业平台；
5. 每个玩家分别调用自己的 AI、各自推进世界的多聊天窗口集合；
6. 需要 AI 判断“玩家是否有资格说某句话”的强限制输入系统。

项目允许玩家自由描述行动，但只有通过可信规则流程的结果才能进入 canonical state。

---

## 4. 项目起点与 Single Player

### 4.1 可确认的历史起点

2026-08-05 的项目相关讨论中，最初方向是一个：

- 单人使用；
- 单 HTML；
- 玩家通过页面与 AI（DM/KP）进行 Roleplay；
- 通过用户自己的 API Key 调用模型；

的轻量工具。

后续开发没有停留在“AI 聊天界面”。

它逐步加入：

- Game State；
- Save；
- Scenario；
- Check；
- AI structured protocol；
- State transaction；
- Player Action Guard；
- Scenario Integrity；
- response resilience；
- SAN；
- HP；
- dying / recovery；
- Combat；
- Damage；
- Firearms / Impaling；
- deterministic regression。

到 2026-08-14，Single Player 已经成为有正式 build、CI 和回归基线的稳定产品。

### 4.2 Single Player 的长期价值

Single Player MUST 保留，因为它具备以下独立价值：

- 无服务端依赖；
- 可直接打开使用；
- 单 HTML 易分发；
- 已有成熟的调查和 CoC 规则链；
- 有大量 deterministic regression；
- 是 Multiplayer rule migration 的行为参考实现。

Multiplayer 不是“修掉旧 Single Player”，而是在它旁边建立新的 authoritative architecture。

`FINAL DECISION`

---

## 5. AI KP 的定位

AI KP SHOULD：

- 理解玩家自然语言；
- 扮演 NPC；
- 生成环境和事件叙事；
- 根据程序已经确认的结果进行描述；
- 提出结构化的世界状态变更；
- 在规则允许范围内建议 Check、节点、结局或下一步行动。

AI KP MUST NOT：

- 自行伪造骰点；
- 绕过程序直接写 HP/SAN；
- 把玩家完成式描述自动认定为事实；
- 直接覆盖 canonical world state；
- 因为输出自然语言很合理，就绕过 structured validation；
- 在 Multiplayer 中为每个玩家分别创造独立世界推进。

`FINAL DECISION`

---

## 6. 玩家体验目标

### 6.1 自由表达

玩家可以像真实 TRPG 一样自然输入：

- “我踹开门。”
- “我说服他告诉我真相。”
- “我已经找到了尸检报告。”
- “我朝怪物开枪。”

系统不应因为措辞是完成式，就禁止玩家输入。

### 6.2 世界结果可信

输入本身不等于事实。

真正进入 world state 的结果必须经过：

- Scenario constraints；
- deterministic rules；
- Check；
- combat resolution；
- AI structured proposal validation；
- transaction commit；

等可信过程。

这形成项目核心体验原则：

> BLOCK UNSAFE STATE, NOT PLAYER ACTION.

`FINAL DECISION`

---

## 7. Multiplayer 长期愿景

Multiplayer 的长期目标，是让多名真人玩家加入同一个 Room：

```text
Players
  ↓
Room
  ↓
One Canonical World State
  ↓
One Canonical AI KP Context
  ↓
Server Validation / Rules
  ↓
Shared Narrative
```

关键体验不是“多人同时打开同一个页面”，而是：

- 大家看到同一个可信世界；
- 每个玩家有自己的身份与角色；
- 一个动作只按规则结算一次；
- 隐藏信息可以按玩家 projection 隔离；
- AI KP 能理解多人行动，并在合适时机统一推进世界；
- 战斗按确定的 Server rules 和 turn order 进行；
- 网络断线不自动等于离开游戏。

`FINAL DECISION`

---

## 8. Host Provided API / BYOK

初始 Multiplayer 使用：

> Host Provided API / BYOK

Host 创建 Room 时提供：

- provider；
- model；
- endpoint（受 Server 安全策略约束）；
- API key。

其他玩家：

- 使用 nickname / invite flow 加入；
- 不需要提供 AI API key；
- 不应看到 Host credential。

这样一个 Room 的 AI 消耗和 AI context 保持统一。

`FINAL DECISION for initial Multiplayer`

未来是否提供 Platform Provided AI 或其他付费模式，属于 `FUTURE`。

---

## 9. Multiplayer 不等于每玩家一次 AI Turn

调查场景中，多名玩家可能连续表达各自动作。

Server MAY：

- 排队；
- 分别执行纯规则检查；
- 等待更多输入；
- 合并多个 actions；
- 最终只进行一次 world advance。

因此：

> Player Message != Full AI World Advance.

战斗则按 deterministic turn order 逐个结算。

`FINAL DECISION at principle level`

具体 batching policy 属于 `OPEN QUESTION / future implementation detail`。

---

## 10. 公开房间与匹配

长期可能出现：

- public rooms；
- matchmaking；
- scenario discovery；
- accounts；
- friends；
- persistent profiles；
- Platform Provided AI；
- cloud saves；
- billing modes；
- human keeper mode。

这些都属于 `FUTURE`。

当前 Multiplayer Foundation MUST NOT 为这些功能引入大量未使用抽象。

---

## 11. ChatGPT Sites 的产品定位

Sites 曾被讨论为：

- 快速托管；
- public demo；
- frontend experiment；
- landing page；
- 轻量 web app 入口。

当前共识是：

> Sites 不承担 authoritative Multiplayer Server。

Server authoritative runtime 当前选型仍是 ASP.NET Core + SignalR。

未来 Sites 是否作为 frontend / landing / demo 入口，需要实际验证部署和连接行为。

`FINAL DECISION`：不作为 authoritative server。  
`OPEN QUESTION`：未来是否使用为 presentation/frontend layer。

---

## 12. Repository 与工程协作愿景

设计历史与工程事实分层：

```text
ChatGPT Project
= 完整设计历史、讨论、否决方案

Git / Docs
= 当前已经确认的设计与状态

Codex
= 读取 docs + repository 后执行受约束工程任务
```

当前工作方式正在转向：

- ChatGPT Project：保留长期产品/架构上下文；
- 正式 Markdown：提供 concise authoritative summary；
- Codex：在独立 repository 中做小步实现、build/test、diff、commit。

`FINAL WORKFLOW DECISION / CURRENT EXECUTION DIRECTION`

---

## 13. 产品成功标准

项目成功不以“使用了多少框架”衡量。

优先级是：

1. 玩家能自由表达；
2. 游戏世界结果一致；
3. AI 不越权；
4. deterministic mechanics 可验证；
5. 长局不会因 context/state 漂移失真；
6. Multiplayer 多端看到同一 authoritative reality；
7. 迁移不破坏已经验证的 Single Player 行为；
8. 每一阶段都能被 tests 和清晰的 state boundary 解释。
