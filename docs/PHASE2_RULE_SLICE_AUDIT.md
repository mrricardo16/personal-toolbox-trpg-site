# Multiplayer Phase 2C Rule Slice Audit Report

审计日期：2026-08-17
审计基线：`main` / `a59b55cd6f5f5f54731ec45c6ae448e7ce15c166`
审计结论：选择 `HP / Damage State` 作为下一条 Multiplayer deterministic vertical slice。

## 1. 审计边界与结论摘要

本轮是审计，不是实现。审计只读取当前 Single Player 源码、构建顺序、回归测试、当前 Multiplayer GameState/Projection 实现和权威项目文档；没有实现任何 Multiplayer gameplay rule，也没有修改 Single Player rule code。

Primary recommendation：**HP / Damage State（单次可信伤害事件的 HP 状态分类）**。

Runner-up：**SAN Loss（单次 SAN shock + SAN Loss Window）**，暂缓到 HP slice 之后。它的语义核心也有稳定的浏览器回归，但 `san-loss-window.js` 将累计损失绑定到 scenario activation、chapter boundary 和 canonical SAN transaction，隐藏状态与 lifecycle 依赖明显高于 HP slice。

Confidence：**High**。理由是：

- HP slice 已有窄而明确的入口：可信负向 `adjustHp` 被抽取为独立 damage event，再写入既有 `character.healthState`；
- 它不依赖 Scenario、AI continuation、Combat Mode 或时间推进；
- 它是 Health Stabilization、Healing Recovery、Combat Damage、Firearms/Impaling 的共同下游基础；
- 当前 Server 已有独立 `MultiplayerGameState`、per-game mutation lock、revision、owner projection 和 realtime snapshot delivery，可直接承接这一类原子状态变更；
- 现有行为覆盖已经明确排除了 healing、dying round、weapon damage 等后续语义，边界可验证。

本报告不批准实现，不新增 fixture/harness，不修改 `src/`、`multiplayer/` production/client，也不修改正式 HTML。

## 2. 已读取的权威材料与实际基线

已读取：

- `AGENTS.md`；
- `docs/README.md`、`docs/PRODUCT_VISION.md`、`docs/DESIGN_PRINCIPLES.md`、`docs/ARCHITECTURE.md`；
- `docs/CURRENT_STATE.md`、`docs/MULTIPLAYER_PLAN.md`、`docs/DECISION_LOG.md`、`docs/REJECTED_IDEAS.md`；
- `docs/HANDOFF.md`、`docs/CONTEXT_MAP.md`、`docs/KEYWORDS.md`。

当前仓库事实：

- 工作树在审计开始时 clean；当前分支为 `main`；`HEAD` 与 `origin/main` 均为 `a59b55cd6f5f5f54731ec45c6ae448e7ce15c166`；
- Phase 2B 已完成 realtime GameState synchronization、viewer-safe projection、CheckResolved semantic delivery 和最小 Check gameplay client；
- 当前 Server 的 `CharacterState` 仍只有 `CharacterId`、`OwnerPlayerId`、`Name`、`CheckValues`，尚未承载 HP/SAN/Combat；
- `GameCoordinator` 已有 per-game `SemaphoreSlim` mutation gate，`Revision` 在成功变更时递增；
- `GameProjection` 已按 viewer 隐藏其他角色的 CheckValues，realtime notifier 只发送 projection；
- 当前已有 19-case Check/Dice conformance fixture，尚无 HP/SAN/Combat Multiplayer conformance fixture；
- `build/build-single-html.js` 与 verifier 都把模块按以下实际顺序装配：`san-loss-resolution.js` → `san-loss-window.js` → `hp-damage-state.js` → `health-stabilization.js` → `healing-recovery.js` → `combat-opposed.js` → `combat-damage.js` → `firearms-impaling.js`。

当前 Single Player 规则源及对应回归文件：

| 候选 slice | 实际源码 | 直接回归文件 | 测试数 | `assert` token 次数（近似） |
|---|---|---|---:|---:|
| HP / Damage State | `src/hp-damage-state.js` | `build/test-v165-hp-damage-state.js` | 32 | 96 |
| Stabilization | `src/health-stabilization.js` | `build/test-v166-health-stabilization.js` | 38 | 90 |
| Healing | `src/healing-recovery.js` | `build/test-v167-healing-recovery.js` | 43 | 97 |
| SAN Loss Resolution | `src/san-loss-resolution.js` | `build/test-v163-san-loss-resolution.js` | 37 | 100 |
| SAN Loss Window | `src/san-loss-window.js` | `build/test-v164-indefinite-insanity-tracking.js` | 31 | 82 |
| Combat Opposed | `src/combat-opposed.js` | `build/test-v168-combat-opposed.js` | 42 | 84 |
| Combat Damage | `src/combat-damage.js` | `build/test-v169-combat-damage.js` | 48 | 114 |
| Firearms / Impaling | `src/firearms-impaling.js` | `build/test-v1610-firearms-impaling.js` | 45 | 93 |

上表的 assertion 数是对测试源码中 `assert` 调用 token 的可复核近似统计，不等同于独立语义断言总数。所有候选仍受现有 `src/state.js`、`src/check-engine.js`、`src/coc-resolution-engine.js`、`src/coc-consequence-contract.js`、`src/ai-protocol.js` 及构建装配顺序影响。

## 3. 实际调用路径与依赖图

### 3.1 Common transaction path

当前 Single Player 的通用状态变化路径是：

```text
AI structured response / authored check result
        ↓
normalizeAiProtocolShape
        ↓
cocConsequencePrepareParsed
        ↓
prepareAiTransaction → prepareStateChanges
        ↓
commitAiTransaction → commitPreparedChanges → state.revision
        ↓
buildRequestPayload / diagnostics / renderAll
```

`coc-consequence-contract.js` 在进入通用 transaction 前剥离未授权的负向 `adjustHp`、任意 `adjustSan` 等 punitive operations；因此 HP audit 必须读取 `transaction.parsed`，不能绕过 inner guard 直接从原始 AI JSON 抽取事件。`build/test-v165-hp-damage-state.js` 已明确覆盖这一点。

### 3.2 Candidate dependency graph

```text
state.js + check-engine.js + coc-resolution-engine.js
                    ↓
             ai-protocol.js
                    ↓
       coc-consequence-contract.js
                    ├───────────────┐
                    ↓               ↓
       san-loss-resolution.js   scenario / chapter hooks
                    ↓               ↓
       san-loss-window.js ←──── commitAiTransaction
                    ↓
             hp-damage-state.js
                    ↓
         health-stabilization.js
                    ↓
           healing-recovery.js

combat-opposed.js
        ↓              ↘
combat-damage.js       health-stabilization.js
        ↓
firearms-impaling.js
```

这不是 import graph，而是按当前源码的 function wrapping 和真实调用点整理的行为 graph：

- `hp-damage-state.js` 包装 `prepareAiTransaction`、`commitAiTransaction` 和 `applySecretCheckOutcome`，消费可信负向 `adjustHp`；
- `health-stabilization.js` 扩展 HP snapshot，并包装 `hpDamageApplyEvent`、`prepareAiTransaction`；
- `healing-recovery.js` 直接依赖 `hpDamageStateSnapshot` / `normalizeHpDamageState`，并只在 Medicine continuation 中剥离 AI 正向 `adjustHp`；
- `combat-opposed.js` 写入 `state.campaign.combat`，并调用 Health Stabilization 的 dying round 能力；
- `combat-damage.js` 消费 Combat Opposed 生成的 `damageDisposition`，滚武器/DB/Armor 后直接更新目标 HP，再进入 `hpDamageApplyEvent`；
- `firearms-impaling.js` 在 Combat Opposed、Combat Damage 之上改变 initiative、point-blank、Dive for Cover、attack-forfeit 和 Impale damage；
- `san-loss-window.js` 包装 scenario activation、chapter transition、SAN resolution 和 canonical transaction，故其依赖不是纯数值函数。

## 4. 候选逐项审计

### 4.1 HP / Damage State

**实际语义职责**：每个可信负向 `adjustHp` operation 是一个独立 damage event；单次伤害达到 `ceil(maxHp / 2)` 时记录 Major Wound 并进行 CON check；单次伤害达到 `maxHp` 时 instant death；HP 到 0 时保守标记 unconscious；已有 Major Wound 且 HP 到 0 时进入 dying。正向 HP 不创建 damage event，也不自动清除 Major Wound。

**真实路径**：

1. `hpDamageExtractEvents` 从 `transaction.parsed.stateChanges` 按 operation 顺序提取负向伤害，保留 `hpBefore` / `hpAfter`、原始 damage、source 和 event key；
2. `hpDamageApplyEvents` 调用 `hpDamageApplyEvent`，以 event key 去重；
3. `hpDamageApplyEvent` 只写 `character.healthState`，并在 Major Wound 时消耗一次 CON roller；
4. `commitAiTransaction` 和 `applySecretCheckOutcome` 各自接入可信路径，然后 bump revision、log、render；
5. request payload / diagnostic 暴露 health context，但模块本身没有 API round trip。

**状态写入**：`healthState.majorWound`、`unconscious`、`dying`、`dead`、`lastDamageEvent`、bounded `history`；HP 数值仍在 `character.hp`。旧 Schema 8 只对 zero HP 保守补 unconscious，不从存档猜测 Major Wound、dying 或 death。

**确定性与 RNG**：大多数分类是纯函数；只有 Major Wound CON check 需要一次 d100。event key 使重复提交不重骰、不重复 history。测试已覆盖 amount/by/delta、operation 顺序、两次小伤不合并、overkill 依据原始单次 damage 而不是 clamp 后 HP 差值。

**隐藏状态与投影**：health history、source event key、CON roll 和 death provenance 不应直接广播给所有玩家；owner 的当前 HP/condition 与其他玩家可见的伤势摘要需要由 Server projection policy 明确拆开。Single Player 的 browser context 不能直接作为 Multiplayer snapshot。

**当前覆盖**：32 tests，包含 module/build/verifier contract、阈值、Major Wound、CON success/failure、instant death、zero HP、dying transition、独立 damage events、positive healing separation、dedupe、legacy normalization、真实 prepare/commit、AI guard、payload/diagnostics、no-round-trip 和构建顺序。

**判断**：依赖最浅、Scenario/AI 依赖最低、fixture 边界最窄，是可复用性最高的下一步；但不是“只复制 HP 字段”，必须同时定义 event identity、CON RNG、legacy normalization 和 projection。

### 4.2 SAN Loss：Resolution + Window

`src/san-loss-resolution.js` 负责单次 SAN loss 后的 momentary reaction、5 点阈值、INT shock、temporary insanity bout；`src/san-loss-window.js` 负责 starting SAN、累计损失、1/5 threshold、scenario start、chapter boundary、SAN check 和 trusted canonical SAN transaction 的接入。

这两个模块合计 68 tests、约 182 个 `assert` token，且没有 API round trip。优势是数值规则与去重语义明确；代价是：

- `sanityState` 同时包含 baseline、temporary、history、lossWindow、indefinite condition；
- window 的正确起点依赖 scenario lifecycle 和 authored chapter，而不是单个角色命令；
- SAN loss 既可能来自 check record，也可能来自 canonical transaction；
- temporary/indefinite insanity 的叙事可见性、history、trigger provenance 都需要 projection policy；
- v1.6.3 明确不自动判定 indefinite insanity，v1.6.4 又引入了 authoritative window，迁移时必须区分这两个历史阶段。

因此它是 runner-up，而不是本轮 primary。

### 4.3 Stabilization

`src/health-stabilization.js` 直接扩展 HP Damage State：dying CON round、First Aid 一小时窗口、+1 HP、唤醒 unconscious、稳定 dying、treatment history。38 tests、约 90 个 assertion token，覆盖时间边界、失败、死亡、旧存档、AI continuation guard 和 Combat round deferred policy。

它的规则本身较确定，但必须依赖 HP 的 condition schema，并引入显式时间语义和后续 Combat round 观察点；若先迁移会把 HP foundation、First Aid 和 dying timing 绑定在一个事务里，增加 schema 与 fixture blast radius。应在 HP slice 稳定后作为紧邻的后续 slice。

### 4.4 Healing Recovery

`src/healing-recovery.js` 依赖 HP state 与 Stabilization，负责 explicit day natural healing、explicit week Major Wound healing、Medicine one-hour/equipment gates、same-day Regular/later Hard、D3 healing 和解除 Major Wound。43 tests、约 97 个 assertion token。

它有明确浏览器 action，但同时引入时间推进、治疗记录、技能/难度、First Aid prerequisite、Major Wound 清除条件和 AI continuation 剥离；对 Multiplayer command scheduling 和 action intent 的压力高于 HP event。必须后置。

### 4.5 Combat Opposed

`src/combat-opposed.js` 负责 Combat Mode、participants、DEX order、Dodge/Fight Back success-level comparison、outnumbered bonus、round/turn、response/action counts 和 `damageDisposition`，不直接提交 HP。42 tests、约 84 个 assertion token。

它的纯 opposed outcome 相对清晰，但实际路径写入 `state.campaign.combat`，依赖 participant schema、current actor、turn transaction、response allowance、AI busy guard，并调用 Health Stabilization 的 dying round timing。Multiplayer 还要把当前单一 `state.character` 演化为多角色 owned CharacterState，同时处理多玩家 command ordering。因此它不是当前最小 slice。

### 4.6 Combat Damage

`src/combat-damage.js` 消费 `damageDisposition`，依赖 Combat Opposed 的 attacker/target/mode，再处理 weapon dice、STR/SIZ Damage Bonus、fixed Armor、玩家与 NPC HP、defeat repair，最后把玩家伤害送回 HP Damage State。48 tests、约 114 个 assertion token。

其确定性仍可测试，但 schema 同时涉及 combat participant、loadout、weapon expression、armor、HP、defeat、turn repair；一次命中可能改变 damage record、HP state、combat order、combat end 和 projection。它是 HP 后的自然复用者，不应作为 HP 前的入口。

### 4.7 Firearms / Impaling

`src/firearms-impaling.js` 叠加 Combat Damage 与 Combat Opposed，增加 weapon mode、Firearms skill、readied DEX+50、point-blank、Dive for Cover、attack-forfeit、single-shot range 和 Impale damage。45 tests、约 93 个 assertion token。

它的 regression 数量不低，但 coverage 依赖层最多；实际 mutation 同时跨 initiative、response、damage mode、weapon schema 和 HP Damage State。多发射击、automatic fire、malfunction、long-range bands、reload 等仍被源码明确 deferred。迁移 blast radius 最大，最后处理。

## 5. Comparison Matrix

评分说明：`低` 表示本轮 Multiplayer slice 的压力低，`高` 表示需要先解决更多跨层契约；`高` 的 Determinism 表示规则更容易以固定输入/随机序列复现，而不是风险更高。

### 5.1 规则与状态压力

| Candidate | 依赖深度 | 状态 schema pressure | Determinism | hidden state | Scenario dependency | AI dependency | Character schema impact | transaction complexity |
|---|---|---|---|---|---|---|---|---|
| HP / Damage State | 低 | 中 | 高 | 中 | 低 | 低 | 中 | 中 |
| SAN Resolution + Window | 中 | 高 | 高 | 高 | 高 | 中 | 中 | 中 |
| Stabilization | 中 | 中 | 高 | 中 | 低 | 低 | 中 | 中 |
| Healing | 中高 | 中高 | 高 | 中 | 中（day/week intent） | 低中 | 中 | 中高 |
| Combat Opposed | 高 | 高 | 高 | 中高 | 低 | 低 | 高（multi-character participant） | 高 |
| Combat Damage | 高 | 高 | 高 | 中 | 低 | 低 | 高 | 高 |
| Firearms / Impaling | 最高 | 最高 | 中高 | 中高 | 低 | 低 | 最高 | 最高 |

### 5.2 Multiplayer 交付压力

| Candidate | conformance fixture 可行性 | concurrency | projection | realtime | 现有 regression | migration blast radius | reusable foundation |
|---|---|---|---|---|---:|---|---|
| HP / Damage State | 高：单事件 + 明确 random sequence | 中：revision + event dedupe | 中高：owner/private history | 低中：commit 后 snapshot | 32 | 低中 | 最高 |
| SAN Resolution + Window | 中高：需 scenario/window lifecycle | 中：多来源 SAN event dedupe | 高：temporary/history/indefinite 可见性 | 中：window/condition semantic event | 68 | 中高 | 中 |
| Stabilization | 高：显式 local action + d100 | 中：time/action idempotency | 中：治疗/CON record | 中：condition change | 38 | 中 | 高，但依赖 HP |
| Healing | 中：day/week/Medicine context | 中高：时间确认与重复 action | 中：治疗 history 与伤势 | 中：recovery event | 43 | 中高 | 中 |
| Combat Opposed | 中：多参与者、turn sequence | 高：current actor/round command race | 高：参与者、响应、隐藏 NPC data | 高：turn/exchange events | 42 | 高 | 高，但依赖 health timing |
| Combat Damage | 中：weapon/DB/armor/RNG | 高：命中后多对象原子更新 | 高：weapon/armor/history/HP | 高：exchange + defeat | 48 | 高 | 中高，但依赖 opposed |
| Firearms / Impaling | 中：多 mode/range/response | 最高：initiative + forfeit + damage | 高：weapon/response/secret roll | 最高：shot/exchange/damage | 45 | 最高 | 中，依赖两层 combat |

结论不是简单选择测试最多或代码最少的模块，而是选择在现有 Server contracts 上能形成最小闭环、并能减少后续迁移重复工作的模块。按该标准，HP / Damage State 明显领先。

## 6. Primary Recommendation

### Selected slice

**HP / Damage State：Server authoritative per-event damage classification。**

本名称不包含：First Aid、dying round CON、Medicine、natural healing、weekly healing、weapon dice、Armor、Combat Opposed、Firearms、SAN、Scenario progression 或 AI KP gameplay。

### Why this slice now

1. 它可以复用当前 `MultiplayerGameState` 的 room/game revision、character ownership、projection、mutation lock 和 realtime delivery；
2. 它不需要先引入 scenario scheduler、AI proposal protocol、Combat turn state 或时间推进；
3. 它建立后，Stabilization 可以复用 health condition；Combat Damage 可以复用 damage event；Firearms 可以复用最终 HP mutation；
4. 它能把“多个小伤不聚合”“原始单次 damage 决定严重度”“重复 event 不重骰”这些最容易产生 Server/JS drift 的语义先固定下来；
5. 它的 fixture 可以从现有 v1.6.5 reference behavior 直接缩成少量 case groups，不需要把整个 892-test Single Player regression 翻译为 C#。

### Runner-up and deferral reason

Runner-up 是 **SAN Loss Resolution + SAN Loss Window**。它的 direct regression 最完整之一，且没有 API round trip；但它要求先定义 scenario start、chapter transition、baseline ownership、多个 SAN loss source 的合并/去重，以及 temporary/indefinite insanity 的 player projection。若先做，会把当前 Multiplayer 尚未拥有的 Scenario lifecycle 一并变成新契约，迁移范围超过本轮应有的 deterministic slice。

## 7. Proposed Separate Implementation Boundary

以下只是下一轮 implementation prompt 的边界，不是本轮批准的代码改动：

### Include

- 在独立 Multiplayer model 中增加明确的 character health profile/state，不把 Multiplayer runtime 塞回 Single Player Save Schema 8；
- 增加一个仅由 Server-authorized/trusted source 调用的 HP damage command/service；客户端不能凭空提交“我造成了 N 点伤害”的 world fact；
- 输入至少需要 character identity、trusted source/event identity、single-event damage amount 和 expected game revision；
- Server 以 `maxHp`、当前 HP、CON 和固定的 injected random sequence/production RNG 计算 HP Damage State；
- 采用 event identity 去重，重复 event 不重复扣 HP、不重复 CON roll、不重复追加 history；
- 成功提交时原子更新 current HP、health conditions、last event、bounded event history 和 Game revision；
- commit 后生成 semantic health event，再走既有 GameSnapshot projection/realtime notifier；
- 以现有 Single Player v1.6.5 behavior 生成第一批 JS reference / Server conformance cases。

### Exclude

- 不实现 First Aid、dying round、Medicine、natural/weekly healing；
- 不实现 Combat Opposed、Combat Damage、Firearms/Impaling、Armor 或 weapon schema；
- 不实现 SAN、Scenario progression、AI proposal/continuation、Phase 3；
- 不向 client 暴露任意 damage injection endpoint；
- 不修改 `src/`、`build/`、`outputs/`、Single Player tests 或正式 HTML；
- 不把 `healthState.authority: browser_*` 等 Single Player transport/context 字段原样当作 Server domain contract。

## 8. Proposed Fixture Case Groups

这些是从实际 `build/test-v165-hp-damage-state.js` 行为提取的 case groups；本轮不填写未经新导出的 expected values。下一轮应先从 JS reference 导出 `initialState`、`command`、`randomSequence`、`expectedState`、`expectedEvents`，再实现 Server parity。

1. 单次低于半血阈值：无 Major Wound、无 CON check；
2. 恰好达到半血阈值：Major Wound，CON roll 等于 CON 的 success 分支；
3. Major Wound CON failure：记录 unconscious；
4. 单次 damage 达到 max HP：instant death，且不再掷 Major Wound CON；
5. 0 HP 但无 Major Wound：只标记 unconscious；
6. 已有 Major Wound 后的小伤把 HP 打到 0：进入 dying；
7. 两个独立小伤：保留两个 event，不合并成一个 Major Wound；
8. overkill：严重度使用原始 single-event damage，不使用 clamp 后实际 HP 差值；
9. 正向 HP adjustment：不创建 damage event；
10. 已有 Major Wound 后的正向 HP：不自动清除 Major Wound；
11. 相同 event key 重复提交：去重、不重骰、不重复 history；
12. event extraction：保留 operation 顺序和逐击 `hpBefore` / `hpAfter`；
13. `amount` / `by` / `delta` 的现有 protocol aliases；
14. legacy Schema 8：0 HP 只保守补 unconscious，正 HP 不猜测伤势；
15. trusted canonical transaction 与 secret authored check 两条来源路径都只处理经过既有 guard 的可信 HP change。

## 9. Required State and Projection Decisions for the Next Prompt

### Required Server-side state additions

建议在 Multiplayer `CharacterState` 中明确拆分：

- `HealthProfile`：当前 HP、Max HP、CON 等 HP 规则输入；
- `HealthState`：Major Wound、Unconscious、Dying、Dead、last damage event、bounded damage history；
- event identity/source identity 与 game revision 关系；
- 不复用 `CheckValues` dictionary 承载 HP/CON，也不把浏览器 payload/context 直接作为 domain state。

当前 `MultiplayerGameState.Revision` 与 `CharacterState.OwnerPlayerId` 可以复用；但 `GameContracts`、`GameProjection`、initialize request、error contract 和 test fixture 都需要在下一轮单独设计。

### Projection policy proposal

- 角色 owner：可见当前 HP/Max HP、本人可见的 active health conditions，以及必要的“最近结果”摘要；
- 非 owner：默认不发送 damage history、source event key、CON roll、secret authored provenance 或其他玩家的完整 health diagnostics；是否发送“受伤/昏迷/濒死”等 party-visible summary 需要明确产品决定，不能从 Single Player UI 自动推断；
- Server canonical state、semantic event、player projection 三者分离；SignalR 只发送 commit 后 projection；
- reconnect/Attach 必须重新生成 viewer-safe snapshot，而不是缓存一个可能泄露其他玩家 history 的完整对象。

### Risks and open questions

- 当前 Multiplayer character initialization 没有 HP、Max HP、CON；需要决定这些值的来源、合法范围和是否允许 host 初始化；
- “trusted source” 在没有 Phase 3 AI protocol 的前提下由谁发起，需要 internal command / authored check bridge 的明确边界；
- event key 应由 rule command 生成还是由上游 transaction 提供；重试、并发和跨连接重复提交必须一致；
- HP state history 的保留上限、是否持久化、是否向 owner 展示仍是 projection/product decision；
- Server RNG 与 conformance deterministic RNG 必须分离：production 不接受 client random sequence，fixture 需要可控 test RNG；
- 旧 Schema 8 的保守 normalization 语义只适用于 reference parity；Multiplayer 新 character 是否允许从“HP=0 无事件证据”开始，需要显式定义；
- current JS wrapper chain 不是 Server 架构模板。迁移目标是 behavior parity，不是复制 global `state`、late wrapping 或 browser render side effects。

## 10. Reference Behavior Issues to Preserve or Revisit Explicitly

本轮未发现需要立即修改 Single Player 的 blocker，但下一轮必须把以下行为写入 fixture/decision：

- v1.6.5 将多个 negative `adjustHp` operation 保持为多个 damage events，不能按一个请求聚合；
- 单次伤害严重度按原始 event damage 判定，和 clamp 后 HP 变化不同；
- zero HP 的 legacy save 只补 unconscious，不凭空产生 Major Wound、dying 或 death；
- HP Damage State 明确把 First Aid、Medicine、major wound healing、dying round CON 留给后续；
- `healthStabilization.js` 在后续层扩展 `healthState`，故 Server 第一轮不能把 Stabilized/treatment history 偷渡进 HP slice；
- Combat Damage 进入玩家 HP 时才调用 HP Damage State；因此未来 Combat Damage fixture 必须复用同一 health rule，而不是再定义一套战斗专用 HP classification；
- AI 可以叙事，但不能用 narrative、`adjustHp` 或 continuation 重复提交 browser/server 已确定的 damage consequence。

## 11. Scope Safety and Validation Record

本轮实际变更类型：documentation-only。

- Production code changed? **NO**
- Single Player rule code changed? **NO**
- Multiplayer behavior changed? **NO**
- Formal HTML changed? **NO**
- New production fixture/harness added? **NO**
- Allowed write scope used: `docs/` only。

文档验证要求：

- 所有读取和新增文档均按严格 UTF-8 处理；
- `git diff --check`；
- `git status --short`；
- `git diff --stat` 与 changed-file allowlist；
- 本轮没有代码、测试、构建或 HTML 变更，因此不强行运行完整 Single Player / .NET / client suite；
- 下一轮真正实现 HP slice 时，必须运行现有 JS baseline、Server tests、new conformance fixture、projection/realtime tests 和 deterministic double-build 相关检查。

## 12. Final Decision

Primary：**HP / Damage State**。
Runner-up：**SAN Loss Resolution + SAN Loss Window**，因 scenario lifecycle、hidden state 和 projection pressure 延后。
Confidence：**High**。

Phase 2C audit complete.
Selected next deterministic vertical slice: HP / Damage State.
Ready for a separate implementation prompt.
