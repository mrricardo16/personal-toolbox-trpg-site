# TRPG AI 主持助手 v1.6.10

一个无依赖、可直接双击打开的单人 TRPG AI 主持小游戏。唯一正式产品入口为 [outputs/trpg-dm-assistant.html](outputs/trpg-dm-assistant.html)，当前版本为 v1.6.10。

## 快速开始

1. 双击打开 [outputs/trpg-dm-assistant.html](outputs/trpg-dm-assistant.html)。
2. 选择跑团系统、运行模式和剧本。
3. COC 角色选择“天命五选一（.coc5）”或“480 点购点（不含幸运）”。
4. 创建角色后阅读预设剧本前情提要。
5. 接受委托，输入行动，按页面提示进行检定。
6. 使用保存、导出和导入功能管理存档。

首次体验可以先完成创角和预设剧本前情提要流程；继续进行 AI 叙事时，需要填写自己的兼容 API Key。

## v1.6.10 更新内容

- 新增 Firearms / Impaling Resolution 1.0：在 v1.6.8 Combat Opposed 与 v1.6.9 Combat Damage 之上补齐浏览器拥有的单发枪械与穿刺伤害层。
- 支持 `melee_impaling` 与 Base Range 内单发 `firearm_impaling`；枪械技能仅接受 Firearms (Handgun) / Firearms (Rifle/Shotgun)，枪械不会错误加入 STR/SIZ Damage Bonus。
- 已准备枪械按 DEX +50 参与 Combat Mode 行动顺序；point-blank 以 shooter DEX / 5 feet 判定并给 1 个 bonus die。
- Dive for Cover 由浏览器执行 Dodge：成功时射手获得 1 个 penalty die；无论成功失败，目标都会失去下一次攻击机会。聊天回合仍不会被偷换成战斗轮。
- Firearm Extreme/critical 使用 Impale：最大武器伤害 + 额外一次武器伤害骰，不加入 DB；近战穿刺 Extreme/critical 为最大武器伤害 + 适用的最大 DB + 额外一次武器伤害骰。
- Fight Back 即使以 Extreme/critical 获胜也不触发 Impale，继续使用普通实际伤害，保持 v1.6.8 的反击上限语义。
- 固定 Armor、玩家 HP Damage State、Major Wound、dying、instant death、对手 HP/defeat 与最后敌人倒下自动结束 Combat Mode 全部复用既有浏览器权威链。
- 修复两个集成边界：通用伤害审计现在保留 `impaling / impaleExtraResult`；旧 Schema 8 firearm loadout 在内层归一化前捕获并安全恢复合法 `firearmReadied`。
- v1.6.10 focused suite 为 **45 PASS / 0 FAIL**；永久 deterministic 基线由 847 提升到 **892 PASS / 0 FAIL**，并通过 JavaScript syntax、deterministic double build 与 single-HTML verifier。
- 真实 DeepSeek release Run `31765966430`：8 actions / 8 structured / 6 usable / 14 API attempts / 6 retries / 8 provider empty / 2 retry exhaustion / 2 graceful fallback / 0 JSON-invalid / 0 technical leaks；浏览器结局门已 ready，provider 在结案阶段耗尽重试后保持 `awaiting_player_action`，属于严格 provider-deferred ending。
- APP_VERSION 为 v1.6.10，Save Schema 仍为 8，AI protocol 仍为 1.3；正式单 HTML 为 **662681 bytes**，Firearms / Impaling 不增加正常 AI round trip。
- 多发射击、自动火力、故障、Base Range 外距离档、霰弹距离伤害、reload / re-ready 行动经济继续后置，不把未实现枪械规则包装成已完成。
- AI 只能叙述浏览器确认的 firearm / Impale 结果，不得自行决定命中、Dive、穿刺、Armor 或 HP；继续遵守 **BLOCK UNSAFE STATE, NOT PLAYER ACTION**。

## v1.6.9 更新内容

- 新增 Combat Damage / Armor 1.0：v1.6.8 浏览器产生的 `damageDisposition` 现在由浏览器继续结算为可信伤害，不再把武器骰、Damage Bonus 或 Armor 交给 AI 决定。
- 当前正式覆盖非穿刺近战：普通命中按武器伤害骰 + STR/SIZ 派生 DB；固定 Armor 在 HP 扣减前降低净伤害，净伤害最低为 0。
- 发起攻击者取得 Extreme/critical 且属于受支持的非穿刺近战时，浏览器使用最大武器伤害 + 最大正向 DB；负 DB 仍照常应用，不会因 Extreme 被忽略。
- Fight Back 即使以 Extreme/critical 获胜，也保持普通实际掷骰伤害，不获得发起攻击 Extreme 的最大化伤害。
- 玩家实际受到的 Armor 后净伤害直接进入既有 HP Damage State，因此 Major Wound、0 HP、dying 与 instant death 继续复用 v1.6.5 以后已经验证的浏览器规则链。
- 对手现在拥有明确的 HP / MaxHP / Armor / weapon / DB profile；HP 降到 0 会标记 defeated，最后一个 hostile opponent 被击倒时自动结束 Combat Mode，并修复后续 DEX 行动顺序。
- 旧 Schema 8 / v1.6.8 风格战斗数据保守补默认徒手 `1d3+DB` 与 Armor 0；Combat Mode 或 AI 请求活动期间不能偷换玩家伤害配置。
- AI 只可叙述 browser-owned damage result，不能自行掷武器伤害、Damage Bonus、应用护甲或提交 combat HP；继续遵守 **BLOCK UNSAFE STATE, NOT PLAYER ACTION**。
- v1.6.9 focused suite 为 **48 PASS / 0 FAIL**；连同既有 799 条，永久 deterministic 基线提升到 **847 PASS / 0 FAIL**，并通过 JavaScript syntax、deterministic double build 与 single-HTML verifier。
- 真实 DeepSeek release Run `31764627135`：8 actions / 8 structured / 7 usable / 15 API attempts / 7 retries / 8 provider empty / 1 retry exhaustion / 1 graceful fallback / 0 technical leaks；最终正常 `campaign_ended`，结局 `ending-solved`。样本未进入 Combat Mode，因此 Combat Damage 机械正确性以 48 条 deterministic 专项为证。
- APP_VERSION 为 v1.6.9，Save Schema 仍为 8，AI protocol 仍为 1.3；正式单 HTML 为 **640079 bytes**，Combat Damage 不增加正常 AI 请求。
- Firearms / impaling、准备枪械 DEX、point-blank / dive for cover、可变或特殊 Armor 等继续留给后续 v1.6.x，不把未实现内容包装成已完成。

## v1.6.8 更新内容

- 新增 Combat Round / Melee Opposed Contract 1.0：只有显式进入 Combat Mode 后，浏览器才按战斗轮推进；普通调查/聊天消息不会被偷偷当成战斗轮。
- 参与者按 DEX 从高到低行动；同 DEX 以稳定录入顺序作为实现层 tie-breaker。每名参与者消耗一次主要行动后轮次才 wrap，并重置该轮 response/action 计数。
- 近战 Attack vs Dodge 由浏览器双方百分骰比较成功等级：攻击者必须取得严格更高等级才命中；相同等级时 Dodge 成功避开。
- Attack vs Fight Back 同样由浏览器比较成功等级，但平手时发起攻击者获胜；Fight Back 即使以 Extreme/critical 获胜，也只标记普通反击伤害上限。
- 同一轮中目标已经进行过近战防守后，后续超出 response allowance 的近战攻击获得浏览器 1 个 bonus die；新一轮自动清零。
- 本版刻意不生成武器/护甲伤害。胜负只产生 browser-owned `damageDisposition`（regular / initiator_extreme_eligible / fight_back_regular_cap），并标记 `hpCommitted=false`；下一规则层再接武器伤害。
- Combat Mode 激活期间，AI 返回的 `adjustHp` 会被局部剥离，但同响应中的安全旗标、NPC 连续性和普通交互继续保留，继续遵守 **BLOCK UNSAFE STATE, NOT PLAYER ACTION**。
- v1.6.6 dying 与 Combat Round 已接轨：Combat Mode 内关闭手动 dying-round 按钮；角色首次在某轮被观察为 dying 后，不在该轮末立即检定，而从下一轮结束开始自动执行浏览器 CON，之后每轮持续；失败死亡并结束 Combat Mode。
- 新增 `cocCombatRound` payload/diagnostic authority；AI 可以叙述浏览器已确认的战斗状态，但不能自行决定命中、Dodge/Fight Back 胜负或战斗 HP。
- v1.6.8 focused suite 为 **42 PASS / 0 FAIL**；连同既有 757 条，永久 deterministic 基线提升到 **799 PASS / 0 FAIL**，并通过 JavaScript syntax、deterministic double build 与 single-HTML verifier。
- 真实 DeepSeek release Run `31762927920`：8 actions / 8 structured / 6 usable / 14 API attempts / 6 retries / 8 provider empty / 2 retry exhaustion / 2 graceful fallback / 0 technical leaks；最终正常 `campaign_ended`，结局 `ending-solved`。样本未进入 Combat Mode，因此战斗机械正确性以 42 条 deterministic 专项为证。
- APP_VERSION 为 v1.6.8，Save Schema 仍为 8，AI protocol 仍为 1.3；正式单 HTML 为 **624118 bytes**，Combat Round 不增加正常 AI 请求。
- 武器伤害、Damage Bonus、护甲、射击/准备枪械 DEX、point-blank / dive for cover，以及更完整 NPC combat profile 留给后续 v1.6.x。

## v1.6.7 更新内容

- 新增 Healing Recovery 1.0：恢复期机械继续由浏览器拥有；普通聊天回合不会被偷偷当作“过了一天”或“过了一周”。
- 无 Major Wound 且未 dying 的受伤角色，可显式确认经过 1 天并由浏览器恢复 1 HP；满 HP、dying、Major Wound 都不能误走这个每日恢复入口。
- Major Wound 改为显式每周 CON 恢复：成功恢复 1D3；Extreme/critical 恢复 2D3；Extreme/critical 或 HP 恢复到 Max HP 一半以上时，由浏览器解除 Major Wound。
- Medicine 由浏览器按明确施救者技能值结算；必须确认至少治疗 1 小时并具备合适设备/物资。同日为 Regular，非同日为 Hard，成功恢复 1D3 HP。
- active dying 不能直接进入 Medicine；必须先经过 v1.6.6 的 First Aid 成功稳定，再进行后续医疗。
- Medicine continuation 中 AI 自报的正向 `adjustHp` 会被剥离，但安全旗标、NPC 连续性和普通叙事继续保留，避免重复回血同时遵守 **BLOCK UNSAFE STATE, NOT PLAYER ACTION**。
- 新增 `cocHealingRecovery` payload/diagnostic authority；AI 可以叙述浏览器已经确认的恢复状态，但不能自行推进天/周、决定 Medicine 成败、恢复 HP 或解除 Major Wound。
- Real API release gate 发现 DeepSeek 会把合法 `addPinnedFact` 的 `text` 偶发写成 `fact`。现仅对已知 `addPinnedFact`、仅在 `text` 缺失且 `fact` 为非空字符串时执行窄 `fact → text` 归一；显式 `text` 优先，空 `fact` 与未知 operation 仍严格不放行。
- v1.6.7 focused suite 最终为 **43 PASS / 0 FAIL**；连同既有 714 条，永久 deterministic 基线提升到 **757 PASS / 0 FAIL**，并通过 JavaScript syntax、deterministic double build 与 single-HTML verifier。
- 真实 DeepSeek final Run `31761711529`：8 actions / 8 structured / 6 usable / 16 API attempts / 8 retries / 10 provider empty / 2 retry exhaustion / 2 graceful fallback / 0 technical leaks；最终正常 `campaign_ended`，结局 `ending-solved`。
- APP_VERSION 为 v1.6.7，Save Schema 仍为 8，AI protocol 仍为 1.3；正式单 HTML 为 **599664 bytes**，恢复机械不增加正常 AI 请求。
- Combat Round、攻击/闪避/反击、护甲与武器伤害、SAN 状态恢复/过期等继续留给后续 v1.6.x，不把未实现内容包装成已完成。

## v1.6.6 更新内容

- 新增 Health Stabilization / Dying Round Checks 1.0：v1.6.5 的 dying 状态现在可由浏览器继续执行逐轮 CON、生死转换与 First Aid 稳定，AI 只负责叙述 browser-owned 结果。
- 不把聊天消息、玩家行动次数或普通 scene turn 偷偷等同为 CoC 战斗轮；未建立完整 Combat Round Engine 前，濒死 CON 只通过本地“结算下一轮 CON”显式推进。
- 未稳定 dying 的逐轮 CON 由浏览器读取角色 CON 并掷百分骰；成功仍保持 dying、等待下一轮或急救，失败立即写入 canonical death，AI 无权翻转结果。
- First Aid 需要显式确认仍在受伤后一小时内，并由浏览器按救助者 1-100 的急救技能值掷骰；成功固定恢复 1 HP，可唤醒昏迷者，并可将 dying 角色稳定；Major Wound 本身不会被急救直接清除。
- 急救失败不会恢复 HP 或稳定 dying；角色已死亡时不能通过 First Aid 逆转。
- 修复跨状态边界：角色曾被成功稳定后，如果后续新的可信伤害再次造成 dying/dead，旧 `stabilized` 标记会立即失效，不能跨新的致命伤错误沿用。
- First Aid continuation 中 AI 自报的正向 `adjustHp` 会在 canonical commit 前被剥离；同响应中的安全旗标/NPC/普通叙事仍可继续，继续遵守 **BLOCK UNSAFE STATE, NOT PLAYER ACTION**。
- 侧栏在对应伤势状态下提供本地“结算下一轮 CON”和“浏览器急救检定”入口；这些本地机械不增加正常 AI 请求。
- Save Schema 仍为 8，AI protocol 仍为 1.3；旧 Schema 8 dying 存档会保守补齐空的逐轮检定历史，不伪造过去骰点。
- 新增 38 条 v1.6.6 deterministic 回归；连同既有 676 条，正式 release gate 为 **714 PASS / 0 FAIL**，并通过 JavaScript syntax、deterministic double build 与 single-HTML verifier。
- 真实 DeepSeek final Run `31568850359`：8 actions / 8 structured requests / 8 usable provider responses / 11 API attempts / 3 retries / 3 provider empty / 0 retry exhaustion / 0 graceful fallback / 0 technical leaks；最终正常 `campaign_ended`，结局为 `ending-solved`。该样本未触发 First Aid/dying，因此其机械正确性以 38 条 deterministic 专项为证。
- APP_VERSION 为 v1.6.6，正式单 HTML 为 **582277 bytes**。
- Medicine、Major Wound 周期恢复、自然 HP 恢复、完整战斗轮/攻击/护甲/伤害生成，以及 SAN 状态恢复继续留给后续 v1.6.x，不把未实现内容包装成已完成。

## v1.6.5 更新内容

- 新增 HP Damage State / Major Wound 1.0：可信 `adjustHp` 伤害落地后，浏览器继续负责单次伤害严重度、Major Wound、CON 昏迷、0 HP、dying 与 instant death；AI 只在 browser-owned 结果内叙述。
- 每一条负向 `adjustHp` 都是独立伤害事件，绝不把多次小伤合并成一次重伤。例如 Max HP 12 时连续 `-3/-3` 仍是两次 3 点伤害，不会被伪造成一次 6 点 Major Wound。
- Major Wound 阈值按 `ceil(Max HP / 2)`；单次伤害达到阈值会由浏览器立即执行 CON 检定，`roll <= CON` 成功，失败则写入昏迷状态。
- HP 降到 0 会写入 unconscious；只有已经存在 Major Wound 时，0 HP 才进入 dying。单次原始伤害达到 Max HP 时直接标记 instant death，不按 HP clamp 后实际减少量降级。
- HP 恢复不会生成伤害事件，也不会擅自清除 Major Wound 等既有 condition；First Aid、Medicine、Major Wound 恢复、dying 每轮 CON 继续明确留给后续版本。
- 新增 `character.healthState`，继续沿用 Save Schema 8。旧存档若已经是 0 HP，只会保守补 unconscious，不凭空反推 Major Wound、dying 或 death。
- v1.6.5 从经过 v1.6.1/v1.6.2 过滤后的 `transaction.parsed.stateChanges` 提取可信伤害，因此被 Mechanical Consequence Contract 剥离的 AI 非法扣血不会重新变成合法伤势；作者定义的可信 HP 成本仍正常进入伤势状态。
- 防御继续遵守 **BLOCK UNSAFE STATE, NOT PLAYER ACTION**：伤势状态可以严肃落地，但不会因为角色受伤而把正常玩家交互变成技术拒绝。
- 新增 32 条 v1.6.5 deterministic 回归；连同既有 644 条，正式 release gate 为 **676 PASS / 0 FAIL**，并通过 JavaScript syntax、deterministic double build 与 single-HTML verifier。
- 真实 DeepSeek Run `31555873032`：8 actions / 8 structured requests / 7 usable provider responses / 14 API attempts / 6 retries / 7 provider empty / 1 retry exhaustion / 1 graceful fallback / 0 technical leaks；本次通过 provider-deferred-ending 路径保持 `awaiting_player_action` 可继续交互。该样本没有触发 HP 伤害，因此 HP 机械正确性以 32 条 deterministic 专项为证。
- APP_VERSION 为 v1.6.5，Save Schema 仍为 8，AI protocol 仍为 1.3，不增加正常 AI 请求；正式单 HTML 为 **567950 bytes**。
- First Aid / 稳定、Medicine / Major Wound 恢复、dying 连续检定、战斗/护甲/伤害生成，以及 SAN 状态恢复仍留在后续 v1.6.x，不把未实现内容包装成已完成。

## v1.6.4 更新内容

- 新增 SAN Loss Window / Indefinite Insanity Tracking 1.0：浏览器现在拥有 Starting SAN 基线窗口、窗口内累计 SAN 损失与不定期疯狂 1/5 阈值；AI 只可叙述 browser-owned 结果。
- 启用结构化剧本时，以当前 SAN 建立 authoritative scenario window；跨 authored chapter 时用当时 current SAN 建立新窗口，同 chapter 内节点移动不会重置累计值。
- 阈值固定为窗口起始 SAN 的五分之一（向下取整，极低 SAN 至少需要实际损失 1 点），后续 current SAN 变化不会反向移动阈值。
- v1.6.3 SAN check 的实际损失与 trusted canonical transaction 的 SAN 下降都会进入同一累计窗口；相同 source event 自动去重，SAN 恢复不会被误记为损失。
- 累计损失首次达到或超过阈值时，浏览器写入 canonical indefinite condition，并记录来源窗口、阈值、触发时累计损失与触发事件；后续损失继续累计但不会重复触发。
- 新窗口只重置新的累计基线，不恢复 SAN，也不会擅自清除已经存在的不定期疯狂状态；本版明确不伪造恢复/过期时长。
- 旧 Schema 8 状态如果没有 authoritative window，不会根据“当前 SAN 比旧基线低多少”反推一个虚假的连续窗口；sanityStateSnapshot 继续保持纯读取。
- 系统提示与 SAN context 明确 Starting SAN window、累计值和 indefinite condition 均为浏览器权威；AI 不得重置窗口、篡改累计损失、提前宣告或解除不定期疯狂，且防御不能阻断正常玩家交互。
- 新增 31 条 v1.6.4 deterministic 回归；连同既有 613 条，正式 release gate 为 **644 PASS / 0 FAIL**，并通过 JavaScript syntax、deterministic double build 与 single-HTML verifier。
- 真实 DeepSeek final Run `31553994885`：8 actions / 8 structured requests / 7 usable provider responses / 14 API attempts / 6 retries / 7 provider empty / 1 retry exhaustion / 1 graceful fallback / 0 technical leaks；最终正常 `campaign_ended`，结局为 `ending-solved`。该样本未触发 SAN 累计阈值，因此 SAN window 机械正确性以 31 条 deterministic 专项为证。
- APP_VERSION 为 v1.6.4，Save Schema 仍为 8，AI protocol 仍为 1.3，不增加正常 AI 请求；正式单 HTML 为 **556994 bytes**。
- 临时疯狂过期/恢复、不定期疯狂恢复，以及 HP/重伤/昏迷/濒死规则仍留在后续 v1.6.x，不把未实现内容包装成已完成。

## v1.6.3 更新内容

- 新增 SAN Loss Resolution 1.0：现有浏览器 SAN 检定与 SAN 损失数值之后，单次 SAN 冲击链继续由浏览器裁决，AI 只负责在 immutable 结果内叙述。
- 单次损失 0 不触发冲击；1-4 点只保留即时非自主反应语义；单次损失 >=5 时浏览器执行 INT 百分骰，INT 成功进入临时疯狂。
- 临时疯狂由浏览器继续生成 1D10 小时持续时间、1D10 疯狂发作类别与 1D10 轮发作时长；十项发作表固定为失忆、心因性障碍、暴力、偏执、重要之人、昏厥、惊恐逃离、歇斯底里、恐惧症、躁狂症。
- sanResolution 固定写入原 check record；同一记录重试 AI continuation 不重骰、不重复写历史，也不增加第二个 API round trip。
- 新增 character.sanityState，但继续沿用 Save Schema 8：新角色记录 creation baseline，旧存档补 legacy_current baseline；两者都明确 indefiniteTrackingReady=false，本版不伪造跨窗口不定期疯狂判定。
- payload/diagnostics 改为纯读取 sanityStateSnapshot，不会因为查看上下文而静默修改 canonical state 或 revision。
- v1.6.1 Mechanical Consequence Contract 继续阻止 continuation 再次 adjustSan，避免 SAN 损失和冲击链重复结算；防御受限时仍保持玩家交互可继续。
- 新增 37 条 v1.6.3 deterministic 回归；连同既有 576 条，正式 release gate 为 613 PASS / 0 FAIL，并通过 JavaScript syntax、deterministic double build 与 single-HTML verifier。
- 真实 DeepSeek final Run 31456000489：8 actions / 8 structured requests / 7 usable provider responses / 17 API attempts / 9 retries / 10 provider empty / 1 retry exhaustion / 1 graceful fallback / 0 technical leaks。该样本未触发 SAN 检定，因此 SAN 机械正确性以 37 条 deterministic 专项为证。
- Real API Acceptance 同步与 v1.5.8 Resilience 语义对齐：若浏览器 Ending Gate 已 ready 且无 missing、0 hard failure/0 leak，bounded provider fallback 后仍保持可交互，并且至少一半结构化请求真实成功，可分类为 provider-deferred ending，而不是误判规则引擎失败；最终通过样本为 7/8 成功。
- APP_VERSION 为 v1.6.3，Save Schema 仍为 8，AI protocol 仍为 1.3，正常成功回合不增加 API 请求；正式单 HTML 为 546989 bytes。
- 不定期疯狂的累计窗口、临时疯狂过期/恢复，以及 HP/重伤/濒死规则仍留在后续 v1.6.x，不把未实现内容包装成已完成。

## v1.6.2 更新内容


- 新增 Failure-Forward Cost Engine 1.0：失败前进的机械代价改为‘剧本作者声明、浏览器执行’，AI 只可选择/叙述合法 route，不能增减、替换或另造代价。
- `failure_forward` route.cost 现支持 tension / hp / san / progress / resources；未声明 tension 时保持旧兼容默认 1，显式 tension:0 可用于纯非张力代价，但全零成本会被 Case Integrity 阻断。
- 浏览器会去重同一 clue+route、先聚合作者成本，再按当前 canonical HP/SAN/调查进度/资源库存/张力空间截断；failure-forward HP 成本保持非致死，最低保留 1 HP。
- 大失败只在 authored tension > 0 时保持至少 2 点张力；显式 tension:0 不会因为 fumble 被强行改回张力成本。
- v1.6.2 完整接管旧 failure-forward 张力结算：既有 validateClueAcquisition 仍负责证明 route 合法，但旧 tension side effect 被中和，完整成本包只由 Cost Engine 结算一次，避免双扣并允许 tension:0。
- 既有 revealClue 调查进度奖励继续保留；例如初始 progress 7、失败线索 +3、作者 progress cost -4，实际净值为 6，而不是删除旧奖励。
- v1.6.1 Mechanical Consequence Contract 继续先剥离 AI 自报的 HP/SAN/resource/tension 等惩罚，再由 v1.6.2 注入作者定义成本，因此模型无法把合法失败前进代价放大。
- 新增 41 条 v1.6.2 deterministic 回归；连同既有 535 条，正式 release gate 为 576 PASS / 0 FAIL，并通过 JavaScript syntax、deterministic double build 和 single-HTML verifier。
- 真实 DeepSeek Run 31453752316：8 actions / 8 structured requests / 10 API attempts / 2 retries / 3 provider empty / 1 retry exhaustion / 1 graceful fallback / 0 technical leaks，最终正常 ending-solved。该样本未触发 failure-forward，因此机械正确性以 41 条 deterministic 专项为证。
- APP_VERSION 为 v1.6.2，Save Schema 仍为 8，AI protocol 仍为 1.3，正常成功回合不增加 API 请求；正式单 HTML 为 538000 bytes。

## v1.6.1 更新内容


- 新增 Mechanical Consequence Contract 1.0：在 v1.6.0 已由浏览器决定骰点和成功等级的基础上，继续把‘检定后允许落地什么惩罚性机械后果’收回浏览器权威层。
- CoC 检定续写中，AI 不能凭一次失败自行扣除 HP/SAN/资源、删除或减少物品、调整张力、新增威胁、推进威胁时钟或降低调查进度；未经浏览器证据授权的惩罚性操作会在 canonical transaction 前被剥离。
- 防御继续遵守 BLOCK UNSAFE STATE, NOT PLAYER ACTION：非法机械代价被剥离时，同一响应中的安全旗标、NPC 变化和正常叙事仍可继续，不把玩家交互变成技术失败。
- public node-origin check 的 authored successStateChanges/failureStateChanges 由浏览器重新从剧本定义读取并精确注入；AI 不能仅靠复制 checkId 获得作者权限。
- secret node check 维持旧暗骰链：authored effect 已在暗骰结束时由浏览器应用，continuation 不会再次注入，避免双重结算。
- SAN 检定损失仍由浏览器在 AI 续写前直接结算；Consequence Contract 将其标为 alreadyApplied，并阻止 continuation 再次 adjustSan。
- failure-forward 的张力代价现在由浏览器从 authored clue route 计算并补齐，AI 试图放大代价会被替换为作者定义值；大失败继续保持至少 2 点张力的既有规则。
- 当前边界刻意只收紧惩罚性后果：正向 HP/资源/调查进度等旧兼容行为暂不在本阶段统一收权，完整 reward/damage/combat/SAN consequence taxonomy 留在后续 v1.6.x。
- 新增 34 条 v1.6.1 deterministic 回归；连同既有 501 条，正式 release gate 为 535 PASS / 0 FAIL，并通过 JavaScript syntax、deterministic double build 和 single-HTML verifier。
- 真实 DeepSeek Run 31452147354：8 actions / 8 structured requests / 13 API attempts / 5 automatic retries / 5 provider empty / 0 retry exhaustion / 0 graceful fallback / 0 technical leaks，最终正常 ending-solved。该样本未触发浏览器检定，所以 mechanical consequence 正确性以 34 条 deterministic 专项为证，不夸大 provider 覆盖。
- APP_VERSION 为 v1.6.1，Save Schema 仍为 8，AI protocol 仍为 1.3，正常成功回合不增加 API 请求；正式单 HTML 产物为 524690 bytes。

## v1.6.0 更新内容


- 新增 CoC Resolution Engine 1.0：在既有浏览器掷骰基础上加入 browser-owned Check Contract 与 Outcome Contract，把检定目标、难度、奖惩骰、骰点、成功等级和最终 passed 明确收回浏览器机械裁决层。
- AI 仍可请求检定并叙述结果，但角色技能/属性/Luck/SAN 目标值来自浏览器角色卡；Check Contract 建立后若 target、difficulty 等被篡改，会在掷骰前 fail-closed。
- 每次 CoC 浏览器骰都会生成 immutable Outcome Contract，区分 raw success rank 与是否满足本次难度，并向 AI 续写只暴露受控 narrativeBudget；禁止改骰点、改目标、翻转成功/失败或超出额外洞察预算。
- 覆盖 critical / extreme / hard / regular / failure / fumble / skipped，以及困难/极难要求不足时的失败语义；现有 CoC 96/100 大失败边界继续由浏览器规则决定。
- 旧 Schema 8 检定记录无需迁移，载入时可懒重建 Resolution/Outcome Contract；Save Schema 仍为 8，AI protocol 仍为 1.3。
- 诊断边界保持暗骰保密：默认 diagnostics 只包含公开 Outcome Contract，只有显式 includeSecrets=true 才包含暗骰；诊断读取不会推进 canonical revision。
- v1.6 真实 provider gate 还暴露了一个 transport 边界：Node 24 对 AbortController.abort("timeout") 可能表现为 TypeError。现在按请求自身 AbortSignal.reason 精确区分 timeout 与 user cancel，timeout 进入 API Resilience，可取消操作仍不自动重试。
- 修复历史测试中把版本号仅按 patch component 比较的问题，使 v1.6.0 不会被旧 v1.5.5/v1.5.6 测试误判为更低版本；所有行为断言保持不变。
- 新增 43 条 v1.6.0 deterministic 回归，永久完整套件目标提升为 501 PASS / 0 FAIL；成功 real DeepSeek Run 31367411977 完成 8 actions / 8 structured requests / 9 API attempts / 1 retry / 0 technical leaks，并正常进入 ending-solved。
- 本版没有给正常成功回合增加额外 AI 请求，也没有扩大 AI canonical 权限。v1.6.0 是 Resolution Engine 第一阶段；结构化 failure-forward consequence、SAN 损失、伤害与战斗规则留在后续 v1.6.x。

## v1.5.13 更新内容

- 新增 Full Case E2E：用一条完整调查案件把 Player Assertion Guard、Interaction Availability、Clue Route、Progress Semantics、Authored Threat Clock、NPC Knowledge Boundary 和 Ending / Resolution Gate 串成同一运行时闭环，不再只依赖各模块孤立单测。
- 新增 37 条 v1.5.13 确定性长局回归，覆盖完成式多步玩家断言、无收益行动、三条正式线索、NPC 禁知/知识传播、两次真实节点切换、威胁推进/解决、提前结局恢复、最终结局确认、诊断与 Schema 8 归一。
- 永久确定性套件从 421 提升为 458 PASS / 0 FAIL；Save Schema 仍为 8，AI protocol 仍为 1.3。
- 新增当前产品运行时真实 API E2E：直接调用正式 `requestPlayerAction()`、API Response Resilience、协议校验、事务提交、浏览器明骰续写、节点/结局确认链，而不是旧版手写简化 JSON 模板。
- 永久 `TRPG DM Assistant Real API Acceptance` 已升级为运行 `test-real-api-v1513.js`，真实 provider 验收使用 `deepseek-v4-flash` 并保留运行时请求/重试/空响应统计。
- 真实长局发现 DeepSeek 会把合法 `addRevealedTruth` 的正式 `text` 字段输出为 `description`。v1.5.13 只对这个已知 operation 增加窄兼容：仅当 `text` 缺失且 `description` 为非空字符串时归一为 `text`；未知 operation、空参数与其它业务协议错误继续严格失败。
- 两次最终成功的真实运行都完成案件结局且无技术 ID 泄露：Run 31355661648 为 8 actions / 8 structured requests / 13 API attempts / 5 retries；Run 31355896683 为 8 actions / 1 browser check / 9 structured requests / 11 API attempts / 2 retries。两次均经历 provider empty 与一次 graceful retry exhaustion，但都没有 canonical corruption 或 interaction dead-end。
- 真实 E2E 明确关闭测试 VM 内与本目标无关的后台自动摘要，以避免测试脚本连续提交动作时人为制造摘要并发竞争；正式产品摘要逻辑没有被修改。
- 本版没有新增正常游戏 API 请求，也没有扩大 AI 的 canonical 状态权限；新增的是更完整的验证链与一个由真实 provider 证据驱动的窄字段别名。

## v1.5.12 更新内容

- 新增 Ending / Resolution Gate：AI 可以提出结局，但正式收束只由浏览器根据当前 canonical state 决定，AI 叙事本身没有结束案件的权限。
- 兼容并统一执行既有 `alwaysAvailable / requiredFlags / forbiddenFlags / minClues / requiresAnyClueIds / outcomeRequirements`，新增 `requiredClueIds`、`requiredResolvedLeadIds`、`requiredResolvedQuestionIds`、`requiredNodeIds`、`requiredClockStates`、`requireNoActiveThreats` 与 `requiredSemanticKinds`。
- 已知但条件未满足的 `endingProposal` 不再让整个状态事务失败：页面只剥离非法结局提议并局部中和明确的提前终局叙事，同一响应中已经通过校验的线索、旗标、NPC、物品等合法变化继续提交。
- 玩家确认 AI 提议的结局时会重新读取当前剧本结局定义并二次校验 canonical gate，防止 proposal 到确认之间的状态漂移；条件已变化时清除待确认结局并回到可交互状态。
- `applyEnding()` 统一经过同一浏览器 gate，避免内部调用或 UI 路径绕过结局条件；合法提交仍由 v1.5.9 Progress Semantics 记录 `RESOLUTION`。
- `alwaysAvailable` 的主动撤离/中止调查保持可用，不会因为新增门禁而被锁死。
- authored ending 的不存在 node / clock 引用、非法 Progress Semantic 和非法 clock state 在 Case Integrity 阶段作为 blocking ERROR；可能来自运行时动态内容的 clue / lead / question 仍按 WARN/INFO 处理，不把无法静态证明等同于错误。
- 请求上下文新增 `endingResolutionGate`，明确每个结局的 `ready` 状态和 `browser_canonical_resolution` 权威；提示只允许 AI 对 `ready=true` 的 endingId 发起 proposal，同时明确 gate 未满足时必须继续正常互动。
- 未知 endingId、未知 operation、检定前抢跑结局等真正协议错误继续严格失败；本版不把所有结局错误都安全吞掉。
- Save Schema 保持 8、AI protocol 保持 1.3，不增加 API 请求；新增 46 条 v1.5.12 确定性回归，永久完整套件目标为 421 PASS / 0 FAIL。

## v1.5.11 更新内容

- 新增 NPC Knowledge Boundary：剧本作者可通过 `director.knowledgeFacts` 声明受保护事实、初始知情 NPC（`knownBy`）以及合法线索传播来源（`learnableFromClueIds`）；AI 的 KP 全知上下文不再自动等于 NPC 知识。
- NPC continuity 新增浏览器持有的 `knownFactIds / knownClueIds`。作者声明的初始知识随 NPC 实体化进入运行态，旧 Schema 8 存档缺字段时自动归一，无需迁移。
- 玩家可以真实地把已获得线索告诉 NPC：只有线索已揭示、当前行动明确向目标 NPC 出示/转述该线索、且事实声明了对应来源时，浏览器才允许知识传播。
- `learnClueIds / learnFactIds` 只是 AI 提议；浏览器校验通过后才转为内部可信字段。AI 直接伪造内部 knowledge 字段或验证标记会被清除，不能绕过权限边界。
- 支持合法的 knowledge-only `updateNpc`：例如“把账本给管家看”可以只改变 NPC 已知信息，不需要伪造 attitude / claim / lastInteraction 来满足旧协议字段要求。
- NPC 若把未知的受保护事实写入 claim/description/lastInteraction，非法字段会本地剥离；同一操作中的合法 relationship 等更新以及同回合其它合法状态变化继续执行。
- NPC 叙事越权泄密会局部中和，但“我不知道 / 无法确认”、拒绝、猜测、撒谎和普通社交互动不会被知识防御误杀，继续遵循 `BLOCK UNSAFE STATE, NOT PLAYER ACTION`。
- “旧宅失踪案”加入首组 authored knowledge facts：管家可以知道书房暗门，但不会因为 KP 知道真相而自动知道地下遗体实验；更深事实需要对应线索传播或作者初始授权。
- authored knowledge 配置在剧本启用前静态校验 fact ID、NPC/clue 引用和 alias 歧义；坏配置不会覆盖当前案件。
- 修复 `normalizeDirectorSituation` 原本会丢弃新 `knowledgeFacts` 的集成问题，并为经过浏览器验证的 knowledge-only NPC 更新增加窄提交路径；没有放宽普通 `updateNpc` 或未知 operation 的权限。
- Save Schema 保持 8、AI protocol 保持 1.3，不增加 API 请求；新增 34 条 v1.5.11 确定性回归，永久套件目标为 375 PASS / 0 FAIL。

## v1.5.10 更新内容

- 新增 Authored Threat Clock：剧本作者可以为威胁时钟声明浏览器执行的推进 / 解决条件，AI 不再直接拥有 authored clock 的最终状态权限。
- 固定支持 `stall`、`semantic`、`flag`、`clue`、`node`、`tension`、`turn` 七类确定性规则事件，并支持 once、cooldown 和单次推进预算。
- authored clock 的 AI `advanceClock` / `resolveClock` 越权提议会被本地剥离，但同一响应中的合法状态变化继续执行，遵循 `BLOCK UNSAFE STATE, NOT PLAYER ACTION`。
- post-commit 解析只允许立即 resolve、绝不借新状态额外 advance；实际 `enterNode()`、AI canonical commit 和暗骰结果可在同回合满足解决条件，避免时钟晚一回合才解除。
- authored 时钟推进 / 触发写入 v1.5.9 Progress Semantics 的 `THREAT`，解决写入 `RESOLUTION`；时钟自身产生的语义不会递归触发自身。
- 无 authored clock 的旧剧本保持原五轮停滞 fallback，legacy clock 仍允许既有 AI `advanceClock` / `resolveClock` 操作。
- “雾港夜航”新增首个正式 authored clock“午夜涨潮”，按调查停滞、外部 THREAT 证据和结局节点确定性运行。
- 修复旧 legacy `advanceClock` 分支引用未定义 `reason` 导致状态事务失败的问题，不改变其原有权限范围。
- authored 配置在剧本启用前静态校验重复 ID、非法规则、无效 semantic kind、缺失 node / clue 引用等，坏配置不会覆盖当前案件。
- Save Schema 保持 8、AI protocol 保持 1.3，不增加 AI 请求；新增 32 条 v1.5.10 确定性回归。

## v1.5.9 更新内容

- 新增浏览器持有的 Progress Semantics：只依据**已经提交的 canonical state 前后差异**分类回合后果，不信任 AI 自报的“进展”或叙事措辞。
- 固定六类语义：`NONE`、`DISCOVERY`、`ACCESS`、`SOCIAL`、`THREAT`、`RESOLUTION`；`NONE` 是合法且可玩的结果，不会为了“有进展”强送线索或奖励。
- `DISCOVERY` 覆盖真实线索/事实发现与调查问题解决；`ACCESS` 覆盖实际节点进入和物品获取；`SOCIAL` 覆盖 NPC canonical continuity 变化。
- `THREAT` 覆盖张力、HP/SAN 损失、威胁与时钟推进；`RESOLUTION` 覆盖案件 outcome、Ending、威胁/时钟解决。
- 同一 canonical commit 可以同时记录多种语义，并保留 evidence；primary 仅用于后续节奏消费，不会反向修改状态。
- 保留旧 `lastTurnImpact` 作为兼容字段，但新的 Progress Semantics 不依赖旧的 AI operation 预判。即使旧 impact 声称 transition，只要 canonical state 没变，新语义仍是 `NONE`。
- 新语义进入压缩上下文、world continuity 与诊断包，供后续 Threat Clock、Ending Gate 和节奏系统使用；不会新增 AI 请求。
- 旧 Schema 8 存档缺少该字段时自动懒初始化，无需迁移；AI protocol 仍为 1.3。
- 新增 23 条 v1.5.9 确定性回归；v1.5.8 版本断言改为向前兼容，但其 20 条 API Resilience 行为断言保持不变。

## v1.5.8 更新内容

- 新增 API Response Resilience：正常成功响应仍只发起 1 次请求；空 final content、超时、网络异常、可重试 HTTP 错误与最终 JSON 解析失败使用统一、有限的恢复链。
- 结构化主持请求最多 3 次总尝试，不做无限重试；非空但无法安全解析的 JSON 只用受限纠错提示重新请求，禁止在 repair 中新增游戏结果。
- 新增明确 provider 错误分类：`AI_PROVIDER_EMPTY_CONTENT`、`AI_PROVIDER_TIMEOUT`、`AI_PROVIDER_NETWORK_ERROR`、`AI_PROVIDER_HTTP_ERROR`、`AI_PROVIDER_RESPONSE_INVALID`，与真正业务协议错误分离。
- 玩家初始行动若 provider 失败耗尽重试，浏览器恢复请求前 canonical state、回合数与张力，并把原行动放回输入框；不会把正常游戏卡进 `error`。
- 检定续写若 provider 失败，已完成的浏览器骰点和检定记录保留，但失败 AI 响应不会提交线索、物品、地点、剧情或结局变化；玩家可沿用原骰点重试续写或继续行动。
- 新增 API 可靠性诊断计数：结构化请求、实际 API 尝试、自动重试、空响应、JSON 失败、安全 fallback 与 hard failure 可独立审计。
- 未知 operation、requestId/revision 不匹配和非法状态事务仍保持严格失败，不会被 provider recovery 静默吞掉。
- Save Schema 保持 8，AI protocol 保持 1.3；新增 20 条 v1.5.8 deterministic chaos 回归。

## v1.5.7 更新内容

- 新增 Case Integrity Validator：启用剧本前检查节点拓扑、线索 acquisitionRoute、依赖环、关键线索单骰软锁和 Ending 静态可满足性。
- ERROR 只用于可证明的结构损坏并阻止坏剧本覆盖当前案件；不可达节点、动态 NPC/线索/flag 来源、脆弱 Ending 等使用 WARN/INFO，默认允许继续游戏。
- 新增 Interaction Availability Invariant：安全层遵循 `BLOCK UNSAFE STATE, NOT PLAYER ACTION`，防御机制不能让 AI 无法执行玩家的正常交互。
- v1.5.6 的玩家断言 / 多步行动 Guard 越权不再直接变成技术失败；页面本地剥离非法后续状态并生成中性叙事，继续处理第一个合法步骤。
- Guard 恢复叙事不会复述未验证的玩家断言，避免伪造 NPC 台词、世界事实或结果重新污染聊天上下文。
- 合法 check 会在恢复中保留；合法单步移动、普通调查、等待和无收益行动不会触发 recovery，未知 operation 等真正协议错误仍严格拒绝。
- AI protocol 保持 1.3，Save Schema 保持 8；不新增第二次 AI 请求，本版没有使用 DS_KEY。
- 新增 34 条 v1.5.7 专项回归，完整确定性回归达到 259 PASS / 0 FAIL。

## v1.5.6 更新内容

- 新增 Player Assertion Guard：玩家输入永久视为非权威行动声明，“我找到了 / 拿到了 / 进入了 / NPC 告诉我”等完成式措辞必须重新裁决，不能直接成为世界事实。
- 新增 Action Chaining Guard：包含“然后 / 之后 / 成功后”等多步计划时，只允许结算第一个尚未确定的关键步骤；检定续写也不会自动执行后半段。
- 未建立的世界事实、NPC 台词和发现结果若被 AI 顺着玩家原话直接确认，会被确定性拒绝；已建立事实和合法状态事务不受影响。
- 保留原有地点安全链：nodeProposal、targetNodeId、节点存在、合法出口与地点连续性仍全部强制验证。
- 不新增第二次 AI 解析请求，不增加正常跑团 API 消耗；AI protocol 保持 1.3，存档 Schema 保持 8。
- 新增 25 条 v1.5.6 专项回归，覆盖完成式结果、世界事实注入、NPC 台词注入、多步移动/取物/检定续写和正常单步兼容。

## v1.5.5 更新内容

- 修复真实 API E2E 暴露的地点枚举漂移：AI 返回 locationEffect.type="transition" 时安全归一为正式协议的 transition_proposal。
- 该兼容只处理确定等价的枚举别名，不推测地点；targetNodeId、nodeProposal、当前节点合法出口与地点连续性校验全部保持严格。
- 标准 transition_proposal、stay、blocked、searched、returned、uncertain 行为保持不变，未知地点枚举仍拒绝。
- 新增 v1.5.5 地点 transition 别名专项回归 9 项，覆盖合法移动、nodeProposal.id 组合、未知枚举、不存在节点和缺少节点提议。
- 存档 Schema 保持 8，无需迁移。

## v1.5.4 更新内容

- 兼容 AI 将已知白名单 operation 放错 stateChanges / campaignChanges 的情况：页面按 operation 类型安全归位，未知 operation 仍严格拒绝。
- 修复结构化节点 NPC 未进入运行态的问题：启用模组、进入节点和载入旧存档时只实体化当前节点已声明 NPC，不提前生成未来人物，也不覆盖已有连续性。
- 兼容 nodeProposal.id → targetNodeId 与 name → title；仍必须通过当前节点合法出口验证，不会从叙事猜测目标。
- 结构化 AI 返回空内容时自动重试同一请求一次；只处理空/全空白响应，不自动洗掉非空畸形 JSON 或业务协议错误。
- 对 removeItem、removeStatus、revealClue、updateNpc、resolveLead、resolveQuestion、advanceClock 等操作兼容通用 id，并按 operation 精确映射为类型化 ID 字段；未知操作不推测。
- 新增 50 项 v1.5.4 确定性回归，覆盖协议归位、NPC 实体化、地点别名、空响应重试和实体 ID 别名。
- 存档 Schema 保持 8，无需迁移。

## v1.5.3 更新内容

- 修复检定续写中的线索来源误绑定：内部 currentCheckRecordId 只作为本轮上下文，不再自动抢占所有受保护线索的来源判定。
- 显式 sourceRouteId 现在优先按对应路线校验；automatic / flag / npc / clue 路线不会因为本轮存在无关检定而被错误拒绝。
- 显式 sourceCheckRecordId 仍保持严格校验，错误或无关的检定记录不会被静默放行。
- failure_forward 路线绑定具体 checkId，不能借用另一项无关失败检定来获取当前线索。
- 5 个内置模组的 22 个隐藏线索改为显式获取路线；“无灯列车”的 train-map-fragment 明确绑定 train-spot，不再依赖关键词猜测。
- 保持 v1.5.x 的开放调查原则：本轮可以只有自然叙事而没有线索、状态变化或调查进度；发生过检定也不意味着必须产生线索。
- 新增 v1.5.3 线索路线回归测试，覆盖 automatic、flag、显式/上下文检定、失败前进和 train-map-fragment 原始故障场景。
- 存档 Schema 保持 8，无需迁移。

## v1.5.2 更新内容

- 增加固定长度世界事件时间线，让长团上下文记住最近真正发生过的变化，而不是依赖完整聊天。
- 增加叙事重复度检测；高重复不会让整轮报错，而会在后续主持指令中要求停止换词复述，允许明确“没有新发现”。
- 识别等待、休息与长时间搜索的时间意图，只要求合理时间感，不自动给予线索、进度或惩罚。
- 连续停滞时优先驱动已有 NPC、威胁、时钟与环境，不创建无来源的新谜团或正确答案。
- 上下文新增 worldContinuity，并把最近聊天改为剩余预算内裁剪，保证固定事实、NPC 连续性和世界时间线优先。
- KP 调试模式新增长团诊断；普通玩家和普通诊断包不暴露内部重复度与优先级信息。
- 新增 100 轮确定性长团压力测试，以及使用 GitHub Secret 的真实 API 20/50 轮验收脚本。

## v1.5.1 更新内容

- 调查主循环允许真正的无收益行动：普通观察、闲聊、等待、重复搜索和走错方向可以只产生自然叙事，不强制线索、奖励、检定或调查进度。
- 调整无进展节奏：连续 3～4 轮只允许轻微世界变化；连续 5 轮以上让既有 NPC、威胁、时间或环境主动行动，但不强送关键线索或正确答案。
- 扩展现有 `updateNpc` 为 NPC 连续性载体，可记录重要说法、关系、当前意图和最近互动；历史说法去重并限制数量。
- API 上下文加入优先级 NPC continuity；普通预览隐藏 NPC 当前意图和幕后动机，KP/API 上下文仍可使用内部连续性。
- 上下文治理改为核心状态、固定事实、调查方向、未解决问题、NPC 连续性与相关 Lore 优先；最近聊天默认从 20 条降至最多 12 条。
- API payload 仍保留 canonical trueState，memory 层改用精简 contextCore，减少重复状态占用。
- 页面自动记录 neutral / informational / progress / risk / transition 回合影响，仅用于节奏、审计与上下文，不向玩家泄露正确路线。
- 新增 v1.5.1 调查稳定性回归测试。

## v1.5.0 更新内容

- 重构玩家输入生命周期：发送后输入框立即清空，内部行动快照与可编辑草稿彻底分离。
- 请求失败改为“本轮未生效”恢复卡，提供重试原行动、编辑后重发、放弃本轮；原始响应默认折叠为技术详情。
- 编辑或放弃失败回合会恢复请求前完整状态，避免网络或协议错误消耗回合、推进张力或留下检定记录。
- 地点结果新增 blocked、searched、returned、uncertain；单次行动允许没有线索、没有进度和没有奖励。
- 后台可按地点标题唯一匹配内部节点，但不会向玩家展示真实出口或正确路线。
- 地点协议不一致时只允许一次低温度校正；校正只能修改 narrative、locationEffect、nodeProposal，不能修改检定、线索、状态、剧情变化或结局。
- 新增输入恢复、请求回滚、地点校正不可变字段和无意义行动回归测试。

## v1.4.6 更新内容

- 强化 AI JSON 本地确定性修复：兼容代码围栏、前后说明、中文结构标点、单引号、注释、尾逗号、裸键值、字符串内换行和缺失闭合符；未知操作与纯自然语言仍严格拒绝。
- 剧情态势侧栏增加阶段说明：张力显示当前危险阶段及含义，调查进度显示当前调查成熟度；明确张力本身不会直接修改骰点。
- 检定卡和检定记录明确显示技能值、要求难度、实际通过线、成功等级与最终判定。
- 增加 CoC 判定一致性校验和边界回归：等于普通、困难或极难通过线均正确成功。
- 区分『达到某成功等级』与『是否满足本次要求难度』，避免把困难检定中的普通成功显示成含糊的失败。
- 线索按大失败前进、失败前进、普通、困难、极难和大成功记录发现质量；失败只提供最低限度信息，高等级成功允许逐级额外洞察。
- 大失败采用失败前进时张力代价至少为 2；普通失败默认至少为 1。
- 新增 CoC 结果分层回归测试。

## v1.4.5 更新内容

- 修复存档页面重绘后按钮事件丢失，保存、另存为、导入、导出、诊断和槽位按钮持续可用。
- 将存档交互抽离为专用 `bindSaveEvents()`，并由 `renderSaves()` 每次重绘后立即重新绑定。
- 增加当前槽位、最后保存时间、未保存变更和当前槽位标识。
- 新增存档交互回归测试并纳入 CI。

## v1.4.4 更新内容

- 存档不再保存或恢复 API 地址、模型、温度和超时；API Key 按 API 主机隔离，远程接口强制 HTTPS。
- 导入存档清空临时运行态，并限制嵌套深度、对象节点、剧本节点和线索数量。
- API 协议自检改为使用正式 `validateAiResponse` 检测真实 TRPG `no_check` 响应。
- JSON 修复改为本地确定性修复，不再让 AI 重写业务响应。
- API 返回体和模型文本增加 200KB 上限，有限枚举统一规范化。
- 正式成品不再暴露 `window.__TRPG_TEST_API__`；新增真实行为测试和永久 CI。
- 构建校验器从源码动态读取版本。

## v1.4.3 更新内容

- 修复 `decision=check` 因缺少冗余 `required` 字段而整轮失败。
- 兼容协议版本数字 `1.3` 与字符串 `1.3.0`。
- 安全归一化 `amount` 的 `by` / `delta` 别名。
- 补充检定和状态操作参数提示，未知 operation 仍严格拒绝。

## v1.4.2 更新内容

- 修复当前 Schema 存档导出后无法重新导入的问题；当前版本使用 Schema 8，并拒绝未来版本。
- 威胁时钟统一使用 `current / max / active / triggered / resolved`，兼容旧存档的 `value` 字段。
- 修复请求提示只改变 `value`、实际威胁时钟没有推进的问题。
- 明确区分威胁触发和威胁解决：时钟填满触发威胁，玩家完成条件后才标记解决。
- 增加“撤销上一轮”，回退角色、消息、检定、线索、节点、NPC、物品、时钟和上下文；撤销快照只保存在当前页面内，不写入存档，避免存档体积翻倍。
- “测试连接”升级为 API 协议自检，覆盖鉴权、JSON、requestId、`response_format` 和兼容回退。
- 增加诊断包导出，默认排除主持秘密、暗骰和原始 AI 回复。
- 聊天首次只渲染最近 100 条消息，可分批加载旧记录；完整记录仍保存在存档中。
- 结局状态改为玩家可读中文，不直接展示内部 JSON；KP 调试模式仍可查看内部数据。
- 存档页增加槽位大小、本地占用、消息/审计数量和审计日志清理。

- 请求提示：开场行动参考只显示一次；正式跑团中可回顾已知信息，或经二次确认请求一条 KP 方向提示。
- 提示安全：KP 方向提示只使用玩家可见信息，不能直接授予线索、触发检定、移动地点、修改状态或推进节点；请求失败不结算代价。
- 场景连续性保护：新增地点协议、导航历史和场景校正，阻止未经节点确认的跳转、重复门/房间循环和无进展空间移动。
- Schema 7 存档迁移：旧存档自动补充导航结构，保留原有角色、剧本、聊天、骰点和调查状态。
- 配置体验：模型改为下拉选择，默认 `deepseek-v4-flash`；增加规则稳定、跑团推荐和表现力温度预设。
- 上下文配置：补充上下文字符预算、Lore Cards 字符预算说明，并提供一键跑团推荐配置。

- 明骰与暗骰：明骰显示在当前聊天，暗骰只保存在内部检定记录和审计日志中，不显示于聊天和侧栏。
- 节点检定：支持进入节点自动触发强制检定，以及根据玩家行动意图触发侦查、聆听、心理学等检定。
- SAN Check：明确的恐怖场面自动规范为强制明骰，不能跳过；SAN 损失仍由页面规则处理。
- 检定流程：强制检定只有掷骰，非强制明骰可以掷骰或跳过。
- AI 协议：严格校验 `stateChanges`、`campaignChanges`、检定和节点提议，非法响应整批拒绝。
- 错误恢复：初始 AI 请求失败后可以重新请求本轮，或返回行动阶段；检定续写失败可以沿用原骰点重试。
- 并发安全：旧请求、旧响应和重复状态变化不会覆盖当前状态。
- 结构化请求优先使用 JSON 模式，温度限制在适合协议输出的范围内，并对不支持的接口自动回退。

## 既有功能

- COC 7 角色创建：
  - 天命五选一：一次生成五组 STR、CON、SIZ、DEX、APP、INT、POW、EDU 和 LUCK。
  - 480 点购点：八项基础属性合计必须为 480，幸运单独计算。
  - HP = floor((CON + SIZ) / 10)，SAN = POW，当前版本不维护 MP。
- 浏览器负责安全骰点和真实状态，AI 无权直接覆盖属性、技能、骰点或 revision。
- 强制检定只提供掷骰；非强制检定提供掷骰或跳过。
- 内置剧本包含玩家可见前情提要、成功/失败分支、线索、NPC、状态变化和节点确认。
- 支持 UTF-8 TXT / Markdown 剧本本地解析、预览、编辑和确认导入。
- 支持多供应商 OpenAI Chat Completions 兼容接口。
- 支持本地自动保存、多槽位、JSON 导出和导入新槽位。

## 版本记录

- v1.5.10：加入 browser-owned Authored Threat Clock，用剧本作者规则驱动威胁推进 / 解决，AI authored clock 越权操作本地剥离，并支持同回合 post-commit 即时解决。
- v1.5.9：加入 browser-owned Progress Semantics，以已提交 canonical diff 分类 NONE / DISCOVERY / ACCESS / SOCIAL / THREAT / RESOLUTION，为后续威胁时钟和结局门控提供确定性语义。
- v1.5.8：加入 API Response Resilience，对 provider 空响应、超时、网络异常与可恢复 JSON 失败进行有限重试和安全 fallback。
- v1.5.7：加入 Case Integrity Validator 与 Interaction Availability Invariant，检查案件软锁风险，同时确保 Guard 只挡非法状态、不挡正常玩家交互。
- v1.5.6：加入 Player Assertion Guard 与 Action Chaining Guard，阻止玩家完成式措辞和多步行动直接写入结果。
- v1.5.3：修复线索来源与本轮检定误绑定，显式化内置线索路线，并保持无收益行动合法。
- v1.5.2：强化长团世界连续性、重复叙事治理、时间意识与真实 API 压力验收。
- v1.5.1：稳定调查主循环，允许无收益行动，加入 NPC 连续性与上下文优先级治理。
- v1.5.0：重构输入失败恢复与地点响应协议，允许无进展行动，并以受限后台校正保持场景连续性。
- v1.4.6：修复检定难度透明度与边界核查，加入分层线索质量和更明确的失败前进代价。
- v1.4.5：修复存档页面重绘后按钮失效，并增加交互回归测试。
- v1.4.4：严格隔离 API 密钥与存档，强化导入、响应上限、本地确定性修复、真实协议测试、生产构建安全和永久 CI。
- v1.4.3：修复裁决协议稳定性与自由行动阻断。
- v1.4.2：修复 Schema 8 存档导入，统一威胁时钟，加入回合撤销、协议自检、诊断包、长聊天分批渲染和存储管理。
- v1.4.1：加入请求提示、场景连续性保护、导航历史、模型/温度预设和 Schema 7 存档迁移。
- v1.3.2：加入明骰/暗骰、节点驱动检定、SAN Check 保护、严格状态协议和安全错误恢复。
- v1.2：完成 COC 创角、预设剧本、前情提要、浏览器骰点、节点确认和本地存档闭环。

版本以 `outputs/trpg-dm-assistant.html` 内置的 `APP_VERSION` 为准；本目录只保留一个产品入口，不同时提供多个版本页面。

## API 配置

普通模式选择供应商后只需要填写 API Key。高级设置可以修改模型、温度、超时和兼容接口地址。

真实 API 模式需要服务端允许浏览器 CORS。单 HTML 在浏览器中运行，无法真正隐藏 API Key；不要把个人 Key 写入源码、公开仓库、聊天记录或存档。保存 Key 只适合个人设备。

## 文件说明

- `src/`：v1.5.10 的模块化源码；按状态、检定、AI 协议、剧本、记忆、安全边界、API 韧性、Progress Semantics、Authored Threat Clock、存档、UI、场景库和样式拆分。
- `src/shell.template`：单文件产品的 HTML 外壳模板。
- `build/build-single-html.js`：无依赖 Node 构建脚本，将源码拼接为唯一正式产品。
- `build/verify-single-html.js`：校验成品结构、版本、依赖和唯一产品 HTML。
- `build/test-security-hardening.js`：运行 API、安全、存档导入和正式协议行为测试。
- `build/test-save-ui.js`：验证存档页面重绘后的按钮重绑定和主要操作。
- `build/test-coc-outcomes.js`：验证 CoC 等值边界、难度通过线、成功等级和分层线索规则。
- `build/test-v151-investigation-stability.js`：验证无收益行动、NPC 连续性、回合影响分类和上下文治理。
- `build/test-v1510-authored-threat-clock.js`：验证 authored clock 权限、规则、幂等、post-commit 即时解决、legacy 兼容和静态配置校验。
- `build/test-v159-progress-semantics.js`：验证 canonical diff 六类语义、旧存档兼容、上下文暴露与 AI 自报不具权威性。
- `outputs/trpg-dm-assistant.html`：完整、可直接双击打开的唯一产品 HTML。
- `reports/`：各版本测试报告归档；当前版本报告为 `reports/trpg-dm-assistant-v1.5.10-test-report.md`。
- `../.github/workflows/trpg-ci.yml`：针对 TRPG 项目的持续集成配置。

## 模块化构建

无需安装 npm 或任何依赖。在本目录执行：

```powershell
node build/build-single-html.js
```

构建会稳定生成 `outputs/trpg-dm-assistant.html`，不使用 CDN、外部资源或运行时模块加载。可使用以下聚焦验证：

```powershell
node build/verify-single-html.js
```

完整安全与协议回归测试：

```powershell
node build/test-security-hardening.js
```

GitHub Actions 还会检查所有 JavaScript 语法、连续两次构建结果一致、仓库中仅有一个 TRPG 产品 HTML，以及构建产物与源码同步。

## 当前边界

- 不包含远程多人联机、地图、语音、图片生成或服务端代理。
- 不包含完整 COC 战斗、对抗、追逐、幸运改骰和疯狂症状引擎。
- 不包含完整 DND 战斗轮、法术系统、怪物资料库和死亡豁免流程。
- 本地存档不是防篡改存储；用户仍可通过浏览器开发者工具修改本地数据。