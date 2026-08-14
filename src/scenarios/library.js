"use strict";

/* ?????????????? */
"use strict";

/* =========================
   常量与默认数据
========================= */
const APP_VERSION = "1.6.10";
const SCHEMA_VERSION = 8;
const STORAGE_INDEX_KEY = "trpg-dm-assistant:index:v1";
const STORAGE_SLOT_PREFIX = "trpg-dm-assistant:slot:";
const STORAGE_API_KEY_PREFIX = "trpg-dm-assistant:api-key:";
const STORAGE_API_KEY_LEGACY = "trpg-dm-assistant:api-key";
const STORAGE_PREFS_KEY = "trpg-dm-assistant:prefs:v2";
const STORAGE_PREFS_KEY_LEGACY = "trpg-dm-assistant:prefs:v1";
const PHASES = new Set([
  "setup","character_ready","scenario_ready","awaiting_player_action","requesting_ai",
  "awaiting_check","rolling","requesting_ai_continuation","awaiting_node_confirmation","awaiting_ending_confirmation","campaign_ended","error"
]);
const TRANSITIONS = {
  setup:new Set(["character_ready","scenario_ready","awaiting_player_action","error"]),
  character_ready:new Set(["setup","scenario_ready","awaiting_player_action","error"]),
  scenario_ready:new Set(["setup","character_ready","awaiting_player_action","error"]),
  awaiting_player_action:new Set(["setup","character_ready","scenario_ready","requesting_ai","awaiting_check","error"]),
  requesting_ai:new Set(["awaiting_check","awaiting_player_action","awaiting_node_confirmation","error"]),
  awaiting_check:new Set(["rolling","requesting_ai_continuation","awaiting_player_action","error"]),
  rolling:new Set(["requesting_ai_continuation","error"]),
  requesting_ai_continuation:new Set(["awaiting_player_action","awaiting_check","awaiting_node_confirmation","error"]),
  awaiting_node_confirmation:new Set(["awaiting_player_action","awaiting_ending_confirmation","error"]),
  awaiting_ending_confirmation:new Set(["awaiting_player_action","campaign_ended","error"]),
  campaign_ended:new Set(["awaiting_player_action","setup","character_ready","scenario_ready","error"]),
  error:new Set(["setup","character_ready","scenario_ready","awaiting_player_action","awaiting_check","requesting_ai_continuation","awaiting_ending_confirmation"])
};
const ALLOWED_STATE_OPERATIONS = new Set([
  "adjustHp","adjustSan","adjustResource","addItem","removeItem","updateItemQuantity",
  "addStatus","removeStatus","addClue","revealClue","updateClue","addNpc","updateNpc",
  "setLocation","advanceTime","setScenarioFlag","clearScenarioFlag"
]);
const ALLOWED_CAMPAIGN_OPERATIONS = new Set([
  "addLead","resolveLead","addQuestion","resolveQuestion","adjustTension","adjustProgress",
  "addThreat","removeThreat","addRevealedTruth","addPinnedFact","setDirectorNote","setOutcome","advanceClock","resolveClock"
]);
const ALLOWED_OPERATIONS = new Set([...ALLOWED_STATE_OPERATIONS,...ALLOWED_CAMPAIGN_OPERATIONS]);
const DANGEROUS_KEYS = new Set(["__proto__","prototype","constructor"]);
const DEFAULT_CONFIG = {
  system:"coc7",scenarioMode:"preset",worldBackground:"1920年代，偏写实的克苏鲁调查故事。",
  narrativeStyle:"克制、调查档案式，避免替玩家决定行动。",ruleStrictness:"normal",
  contentBoundaries:"避免露骨色情内容；暴力描写保持适度。",
  apiUrl:"https://api.deepseek.com/chat/completions",model:"deepseek-v4-flash",temperature:0.45,timeoutMs:60000,
  occupationSkillCap:80,nonOccupationSkillCap:60,kpDebug:false,contextCharBudget:24000,loreCharBudget:6000
};
const OFFICIAL_MODEL_OPTIONS = [
  {value:"deepseek-v4-flash",label:"DeepSeek V4 Flash（跑团推荐，均衡）"},
  {value:"deepseek-v4-pro",label:"DeepSeek V4 Pro（更强，成本更高）"}
];
const RECOMMENDED_TEMPERATURE = 0.45;
const API_TRANSPORT_CONFIG_KEYS = Object.freeze(["apiUrl","model","temperature","timeoutMs"]);
const MAX_API_RESPONSE_BYTES = 200000;
const MAX_IMPORT_DEPTH = 32;
const MAX_IMPORT_NODES = 50000;
const MAX_IMPORT_STRING_LENGTH = 2000000;
const MAX_IMPORTED_SCENARIO_NODES = 500;
const MAX_IMPORTED_SCENARIO_CLUES = 3000;
const MAX_TEMPORARY_NODES_TOTAL = 6;
const MAX_TEMPORARY_NODES_PER_SCENE = 2;
const MAX_CONSECUTIVE_SPATIAL_MOVES = 2;
const SPATIAL_GENERIC_TERMS = ["房间","门","走廊","钥匙","地道","地下室","楼梯","密室","通道","门锁","铁门","木门"];


const COC_SKILL_DEFINITIONS = [{"id":"accounting","name":"会计","base":5,"category":"知识"},{"id":"anthropology","name":"人类学","base":1,"category":"知识"},{"id":"appraise","name":"估价","base":5,"category":"调查"},{"id":"archaeology","name":"考古学","base":1,"category":"知识"},{"id":"art_any","name":"艺术/手艺（自选）","base":5,"category":"技艺"},{"id":"art_literature","name":"艺术/手艺（文学）","base":5,"category":"技艺"},{"id":"art_photography","name":"艺术/手艺（摄影）","base":5,"category":"技艺"},{"id":"charm","name":"魅惑","base":15,"category":"社交"},{"id":"climb","name":"攀爬","base":20,"category":"行动"},{"id":"computer_use","name":"计算机使用","base":5,"category":"现代"},{"id":"credit_rating","name":"信用评级","base":0,"category":"社会"},{"id":"cthulhu_mythos","name":"克苏鲁神话","base":0,"category":"神话","locked":true},{"id":"disguise","name":"乔装","base":5,"category":"社交"},{"id":"dodge","name":"闪避","base":"half_dex","category":"战斗"},{"id":"drive_auto","name":"汽车驾驶","base":20,"category":"行动"},{"id":"electrical_repair","name":"电气维修","base":10,"category":"技艺"},{"id":"fast_talk","name":"话术","base":5,"category":"社交"},{"id":"fighting_brawl","name":"斗殴","base":25,"category":"战斗"},{"id":"firearms_handgun","name":"射击（手枪）","base":20,"category":"战斗"},{"id":"firearms_rifle","name":"射击（步枪/霰弹枪）","base":25,"category":"战斗"},{"id":"first_aid","name":"急救","base":30,"category":"医疗"},{"id":"history","name":"历史","base":5,"category":"知识"},{"id":"intimidate","name":"恐吓","base":15,"category":"社交"},{"id":"jump","name":"跳跃","base":20,"category":"行动"},{"id":"law","name":"法律","base":5,"category":"知识"},{"id":"library_use","name":"图书馆使用","base":20,"category":"调查"},{"id":"listen","name":"聆听","base":20,"category":"调查"},{"id":"locksmith","name":"锁匠","base":1,"category":"技艺"},{"id":"mechanical_repair","name":"机械维修","base":10,"category":"技艺"},{"id":"medicine","name":"医学","base":1,"category":"医疗"},{"id":"natural_world","name":"博物学","base":10,"category":"知识"},{"id":"navigate","name":"导航","base":10,"category":"行动"},{"id":"occult","name":"神秘学","base":5,"category":"知识"},{"id":"operate_heavy_machinery","name":"操作重型机械","base":1,"category":"技艺"},{"id":"other_language","name":"外语（自选）","base":1,"category":"语言"},{"id":"own_language","name":"母语","base":"edu","category":"语言"},{"id":"persuade","name":"说服","base":10,"category":"社交"},{"id":"psychology","name":"心理学","base":10,"category":"调查"},{"id":"psychoanalysis","name":"精神分析","base":1,"category":"医疗"},{"id":"ride","name":"骑术","base":5,"category":"行动"},{"id":"science_biology","name":"科学（生物学）","base":1,"category":"科学"},{"id":"science_chemistry","name":"科学（化学）","base":1,"category":"科学"},{"id":"science_geology","name":"科学（地质学）","base":1,"category":"科学"},{"id":"science_pharmacy","name":"科学（药学）","base":1,"category":"科学"},{"id":"science_physics","name":"科学（物理学）","base":1,"category":"科学"},{"id":"sleight_of_hand","name":"妙手","base":10,"category":"技艺"},{"id":"spot_hidden","name":"侦查","base":25,"category":"调查"},{"id":"stealth","name":"潜行","base":20,"category":"行动"},{"id":"survival","name":"生存（自选环境）","base":10,"category":"行动"},{"id":"swim","name":"游泳","base":20,"category":"行动"},{"id":"throw","name":"投掷","base":20,"category":"行动"},{"id":"track","name":"追踪","base":10,"category":"调查"}];
const COC_OCCUPATIONS = [{"id":"archaeologist","name":"考古学家","credit":[10,40],"formulas":[{"id":"edu4","label":"EDU × 4","terms":[["edu",4]]}],"fixedSkills":["appraise","archaeology","history","other_language","library_use","spot_hidden","mechanical_repair"],"choiceGroups":[{"label":"选择 1 项","choose":1,"skills":["navigate","science_geology"]}],"freeSkills":0},{"id":"author","name":"作家","credit":[9,30],"formulas":[{"id":"edu4","label":"EDU × 4","terms":[["edu",4]]}],"fixedSkills":["art_literature","history","library_use","other_language","own_language","psychology"],"choiceGroups":[{"label":"博物学或神秘学，选择 1 项","choose":1,"skills":["natural_world","occult"]}],"freeSkills":1},{"id":"cat_burglar","name":"飞贼","credit":[5,40],"formulas":[{"id":"edu2dex2","label":"EDU × 2 + DEX × 2","terms":[["edu",2],["dex",2]]}],"fixedSkills":["appraise","climb","listen","locksmith","sleight_of_hand","stealth","spot_hidden"],"choiceGroups":[{"label":"维修技能，选择 1 项","choose":1,"skills":["electrical_repair","mechanical_repair"]}],"freeSkills":0},{"id":"dilettante","name":"富家子弟","credit":[50,99],"formulas":[{"id":"edu2app2","label":"EDU × 2 + APP × 2","terms":[["edu",2],["app",2]]}],"fixedSkills":["art_any","firearms_handgun","other_language","ride"],"choiceGroups":[{"label":"社交技能，选择 1 项","choose":1,"skills":["charm","fast_talk","intimidate","persuade"]}],"freeSkills":3},{"id":"explorer","name":"探险家","credit":[55,80],"formulas":[{"id":"edu2app2","label":"EDU × 2 + APP × 2","terms":[["edu",2],["app",2]]},{"id":"edu2dex2","label":"EDU × 2 + DEX × 2","terms":[["edu",2],["dex",2]]},{"id":"edu2str2","label":"EDU × 2 + STR × 2","terms":[["edu",2],["str",2]]}],"fixedSkills":["firearms_rifle","history","jump","natural_world","navigate","other_language","survival"],"choiceGroups":[{"label":"攀爬或游泳，选择 1 项","choose":1,"skills":["climb","swim"]}],"freeSkills":0},{"id":"journalist","name":"调查记者","credit":[9,30],"formulas":[{"id":"edu4","label":"EDU × 4","terms":[["edu",4]]}],"fixedSkills":["art_photography","history","library_use","own_language","psychology"],"choiceGroups":[{"label":"社交技能，选择 1 项","choose":1,"skills":["charm","fast_talk","intimidate","persuade"]}],"freeSkills":2},{"id":"nurse","name":"护士","credit":[9,30],"formulas":[{"id":"edu4","label":"EDU × 4","terms":[["edu",4]]}],"fixedSkills":["first_aid","listen","medicine","psychology","science_biology","science_chemistry","spot_hidden"],"choiceGroups":[{"label":"社交技能，选择 1 项","choose":1,"skills":["charm","fast_talk","intimidate","persuade"]}],"freeSkills":0},{"id":"private_investigator","name":"私家侦探","credit":[9,30],"formulas":[{"id":"edu2dex2","label":"EDU × 2 + DEX × 2","terms":[["edu",2],["dex",2]]},{"id":"edu2str2","label":"EDU × 2 + STR × 2","terms":[["edu",2],["str",2]]}],"fixedSkills":["art_photography","disguise","law","library_use","psychology","spot_hidden"],"choiceGroups":[{"label":"社交技能，选择 1 项","choose":1,"skills":["charm","fast_talk","intimidate","persuade"]}],"freeSkills":1},{"id":"professor","name":"教授","credit":[20,70],"formulas":[{"id":"edu4","label":"EDU × 4","terms":[["edu",4]]}],"fixedSkills":["library_use","other_language","own_language","psychology"],"choiceGroups":[],"freeSkills":4},{"id":"soldier","name":"士兵","credit":[9,30],"formulas":[{"id":"edu2dex2","label":"EDU × 2 + DEX × 2","terms":[["edu",2],["dex",2]]},{"id":"edu2str2","label":"EDU × 2 + STR × 2","terms":[["edu",2],["str",2]]}],"fixedSkills":["dodge","fighting_brawl","firearms_rifle","stealth","survival"],"choiceGroups":[{"label":"攀爬或游泳，选择 1 项","choose":1,"skills":["climb","swim"]},{"label":"支援技能，选择 2 项","choose":2,"skills":["first_aid","mechanical_repair","other_language"]}],"freeSkills":0},{"id":"custom","name":"自定义职业","credit":[0,99],"formulas":[{"id":"edu4","label":"EDU × 4","terms":[["edu",4]]}],"fixedSkills":[],"choiceGroups":[],"freeSkills":8}];
const SCENARIO_LIBRARY = [{"id":"scenario-old-house","title":"旧宅失踪案","mode":"structured","system":"coc7","metadata":{"era":"1920s","eraLabel":"1920 年代","difficulty":"入门","themes":["宅邸调查","失踪案"],"estimatedMinutes":120,"combatLevel":"低","horrorLevel":"中","recommendedOccupations":["private_investigator","journalist","professor"],"recommendedSkills":["spot_hidden","library_use","psychology","law"],"sourceType":"original","sourceNote":"原创短模组；采用公开入门模组常见的地点调查与失败前进结构。"},"briefing":{"subtitle":"雨夜来信与失踪的修复师","era":"1927 年秋，江南近郊","playerRole":"你是一名接受私人委托的调查员。委托人林婉清希望你寻找失踪四天的兄长——文物修复师沈墨。","premise":"沈墨带着一批待鉴定的商会旧档案前往城郊顾宅，此后失去联系。林婉清收到一封迟到三天的求助信：『他们把门藏在书后。不要相信消毒水的味道。』","knownFacts":["顾宅近半年由本地商会租用。","管家周铭声称沈墨当晚已经离开。","暴雨导致山路塌方，天亮前无法离开。"],"objectives":["确认沈墨最后的活动区域。","调查书后暗门与消毒水气味。","带回能证明其下落的证据。"],"opening":"晚上 8:40，你抵达顾宅。管家周铭提着煤油灯站在门廊，只说：『沈先生早就走了。』","suggestedActions":["观察大厅","询问管家","检查沈墨留下的物品"],"contentNote":"玩家可见的无剧透提要。"},"keeperGuide":"沈墨发现商会以尸体保存技术掩盖非法实验，被困于地下封存室。关键线索分散在管家房、书房和档案室；失败不应卡死，可用噪声、气味或 NPC 反应提供替代推进。","director":{"knowledgeFacts":[{"id":"old-secret-door-fact","text":"书房书架后存在通往地下区域的暗门。","aliases":["书房后的暗门","书架后的暗门","书后暗门"],"knownBy":["old-butler"],"learnableFromClueIds":["old-scratch","old-blueprint"]},{"id":"old-low-temp-plan-fact","text":"商会正在资助名为低温封存的计划。","aliases":["低温封存计划","商会资助低温封存"],"knownBy":["old-shen"],"learnableFromClueIds":["old-ledger"]},{"id":"old-underground-experiment-fact","text":"地下封存区正在用遗体进行非法实验。","aliases":["地下非法实验","遗体非法实验","用遗体进行非法实验"],"knownBy":["old-shen"],"learnableFromClueIds":["old-notes"]},{"id":"old-shen-location-fact","text":"沈墨被锁在地下封存区的内侧冷库。","aliases":["沈墨被锁在内侧冷库","沈墨在地下冷库","沈墨被关在冷库"],"knownBy":["old-shen"],"learnableFromClueIds":[]}]},"chapters":[{"id":"scenario-old-house-chapter","title":"调查记录","scenes":[{"id":"scenario-old-house-scene","title":"旧宅失踪案","nodes":[{"id":"old-hall","title":"宅邸大厅","background":"湿冷的大厅堆着未拆封的档案箱，雨水从来客的外套和鞋底滴落，在地面留下斑驳水痕。","goals":["确认沈墨到过这里","判断管家是否说谎"],"clues":[{"id":"old-footprints","name":"泥脚印","description":"鞋底花纹与沈墨常穿的工作靴相符。","hidden":true}],"npcs":[{"id":"old-butler","name":"管家周铭","description":"疲惫而戒备，害怕商会追责。","attitude":"戒备"}],"optionalChecks":[{"id":"old-hall-passive-spot","system":"coc7","type":"skill","skillId":"spot_hidden","label":"侦查","difficulty":"regular","mandatory":true,"reason":"被动留意大厅中的异常细节","visibility":"secret","trigger":"on_enter","once":true,"successText":"你刚跨进大厅，余光便捕捉到一串被雨水冲淡的泥脚印。它们没有通向正门，而是径直延伸到东侧走廊。","successStateChanges":[{"operation":"revealClue","clueId":"old-footprints","reason":"被动注意到大厅痕迹"}]},{"id":"old-hall-spot","system":"coc7","type":"skill","skillId":"spot_hidden","label":"侦查","difficulty":"regular","mandatory":false,"reason":"主动检查大厅残留痕迹","visibility":"public","trigger":"on_action","once":true},{"id":"old-hall-psych","system":"coc7","type":"skill","skillId":"psychology","label":"心理学","difficulty":"regular","mandatory":false,"reason":"判断管家陈述是否可信","visibility":"public","trigger":"on_action","once":true}],"mandatoryChecks":[],"exits":[{"id":"old-to-room","label":"查看管家房","targetNodeId":"old-servant-room","condition":null},{"id":"old-to-study","label":"前往书房","targetNodeId":"old-study","condition":null}],"keeperNotes":"管家知道暗门存在，但不知道地下实验的全部真相。","rawText":""},{"id":"old-servant-room","title":"管家房","background":"狭小房间里有湿透的外套、药瓶和一把被擦拭过的铜钥匙。","goals":["查明管家隐瞒的原因"],"clues":[{"id":"old-key","name":"铜钥匙","description":"钥匙可开启档案室侧门。","hidden":true},{"id":"old-medicine","name":"消毒药瓶","description":"标签来自本地仁济诊所。","hidden":true}],"npcs":[{"id":"old-butler","name":"管家周铭","description":"若被温和追问，会承认夜里听见地下传来搬运声。","attitude":"动摇"}],"optionalChecks":[{"id":"old-room-listen","system":"coc7","type":"skill","skillId":"listen","label":"聆听","difficulty":"regular","mandatory":false,"reason":"辨认墙内传来的低频震动","visibility":"public","trigger":"on_action","once":true},{"id":"old-room-social","system":"coc7","type":"skill","skillId":"persuade","label":"说服","difficulty":"regular","mandatory":false,"reason":"让管家交出钥匙","visibility":"public","trigger":"on_action","once":true}],"mandatoryChecks":[],"exits":[{"id":"old-room-study","label":"前往书房","targetNodeId":"old-study","condition":null},{"id":"old-room-archive","label":"用钥匙进入档案室","targetNodeId":"old-archive","condition":null}],"keeperNotes":"即使社交失败，也可通过侦查发现钥匙；不要让玩家因一次失败失去档案室入口。","rawText":""},{"id":"old-study","title":"书房","background":"书架、书桌和墙面均有近期移动痕迹，一本账册缺了关键页。","goals":["寻找暗门机关","确认商会资金流向"],"clues":[{"id":"old-ledger","name":"残缺账本","description":"资金流向仁济诊所和一项名为『低温封存』的计划。","hidden":true},{"id":"old-scratch","name":"墙边划痕","description":"整排书架近期被推开过。","hidden":true}],"npcs":[],"optionalChecks":[{"id":"old-study-spot","system":"coc7","type":"skill","skillId":"spot_hidden","label":"侦查","difficulty":"regular","mandatory":false,"reason":"寻找暗门机关","visibility":"public","trigger":"on_action","once":true},{"id":"old-study-library","system":"coc7","type":"skill","skillId":"library_use","label":"图书馆使用","difficulty":"regular","mandatory":false,"reason":"整理账册和通信","visibility":"public","trigger":"on_action","once":true}],"mandatoryChecks":[],"exits":[{"id":"old-study-archive","label":"继续追查档案","targetNodeId":"old-archive","condition":null},{"id":"old-study-cellar","label":"推开书架进入地下","targetNodeId":"old-cellar","condition":{"flag":"secretDoorFound","equals":true}}],"keeperNotes":"侦查成功应设置 secretDoorFound。失败时，档案室的建筑图仍可揭示暗门。","rawText":""},{"id":"old-archive","title":"档案储藏室","background":"墙边堆着建筑图、运输清单和被撕下的账册页。","goals":["取得不依赖暗门检定的替代线索"],"clues":[{"id":"old-blueprint","name":"顾宅改建图","description":"图纸标出书房后方的下行楼梯。","hidden":true},{"id":"old-shipment","name":"运输清单","description":"最近运入大量冰块、消毒液和固定带。","hidden":true}],"npcs":[],"optionalChecks":[{"id":"old-archive-library","system":"coc7","type":"skill","skillId":"library_use","label":"图书馆使用","difficulty":"regular","mandatory":false,"reason":"从混乱档案中找出建筑图","visibility":"public","trigger":"on_action","once":true}],"mandatoryChecks":[],"exits":[{"id":"old-archive-study","label":"带图纸返回书房","targetNodeId":"old-study","condition":null},{"id":"old-archive-cellar","label":"沿维修门进入地下","targetNodeId":"old-cellar","condition":null}],"keeperNotes":"此节点是失败前进保障。找到图纸后应允许进入地下，不必再次要求侦查。","rawText":""},{"id":"old-cellar","title":"地下封存区","background":"石阶尽头排列着玻璃柜，消毒水气味覆盖着腐败气息。","goals":["确认沈墨是否仍活着","收集实验罪证"],"clues":[{"id":"old-notes","name":"沈墨的速记","description":"记录了实验参与者与封存室密码。","hidden":true}],"npcs":[],"optionalChecks":[],"mandatoryChecks":[{"id":"old-cellar-san","system":"coc7","type":"san","skillId":"san","label":"理智","difficulty":"regular","mandatory":true,"reason":"看见保存异常的遗体","loss":"0/1d4","visibility":"public","trigger":"on_enter","once":true}],"exits":[{"id":"old-cellar-rescue","label":"打开内侧冷库","targetNodeId":"old-rescue","condition":null},{"id":"old-cellar-evidence","label":"带证据撤回大厅","targetNodeId":"old-evidence","condition":null}],"keeperNotes":"沈墨被锁在内侧冷库，尚有微弱生命迹象。玩家可选择救人或优先保存证据。","rawText":""},{"id":"old-rescue","title":"内侧冷库","background":"沈墨被固定在金属床上，意识模糊；制冷机正在过载。","goals":["解救沈墨并安全撤离"],"clues":[],"npcs":[{"id":"old-shen","name":"沈墨","description":"虚弱但能指认商会成员。","attitude":"信任"}],"optionalChecks":[{"id":"old-rescue-mech","system":"coc7","type":"skill","skillId":"mechanical_repair","label":"机械维修","difficulty":"regular","mandatory":false,"reason":"关闭过载制冷机","visibility":"public","trigger":"on_action","once":true},{"id":"old-rescue-firstaid","system":"coc7","type":"skill","skillId":"first_aid","label":"急救","difficulty":"regular","mandatory":false,"reason":"稳定沈墨生命体征","visibility":"public","trigger":"on_action","once":true}],"mandatoryChecks":[],"exits":[{"id":"old-rescue-end","label":"带沈墨离开","targetNodeId":"old-ending-rescue","condition":null}],"keeperNotes":"机械或急救失败应造成伤害/时间损失，但不要直接杀死沈墨。","rawText":""},{"id":"old-evidence","title":"证据撤离路线","background":"暴雨中，你带着账本、图纸和速记返回大厅。管家必须决定是否作证。","goals":["确保关键证据不被销毁"],"clues":[],"npcs":[{"id":"old-butler","name":"管家周铭","description":"在证据面前可能转为合作。","attitude":"犹豫"}],"optionalChecks":[{"id":"old-evidence-persuade","system":"coc7","type":"skill","skillId":"persuade","label":"说服","difficulty":"regular","mandatory":false,"reason":"说服管家作证","visibility":"public","trigger":"on_action","once":true}],"mandatoryChecks":[],"exits":[{"id":"old-evidence-end","label":"离开顾宅","targetNodeId":"old-ending-evidence","condition":null}],"keeperNotes":"即使管家拒绝，物证仍足以形成阶段性胜利。","rawText":""},{"id":"old-ending-rescue","title":"结局：雨停之前","background":"你救出了沈墨，并取得足以撼动商会的证词。","goals":["决定如何处理证据"],"clues":[],"npcs":[],"optionalChecks":[],"mandatoryChecks":[],"exits":[],"keeperNotes":"阶段性圆满结局。","rawText":""},{"id":"old-ending-evidence","title":"结局：封存的名字","background":"沈墨仍下落不明，但你保住了账册与实验记录。","goals":["决定公开、交给警方或继续调查"],"clues":[],"npcs":[],"optionalChecks":[],"mandatoryChecks":[],"exits":[],"keeperNotes":"证据结局，可作为后续战役钩子。","rawText":""}]}]}]},{"id":"scenario-fog-harbor","title":"雾港夜航","mode":"structured","system":"coc7","metadata":{"era":"1920s","eraLabel":"1920 年代","difficulty":"普通","themes":["港口调查","时间压力"],"estimatedMinutes":150,"combatLevel":"中低","horrorLevel":"中","recommendedOccupations":["journalist","private_investigator","soldier"],"recommendedSkills":["listen","navigate","mechanical_repair","persuade"],"sourceType":"original","sourceNote":"原创短模组；参考公开短模组常见的灯塔、孤立地点与倒计时结构。"},"briefing":{"subtitle":"没有登记的货船与熄灭的灯塔","era":"1931 年冬，东海雾港","playerRole":"你受港务处或失踪船员家属委托，调查货船『海燕号』失联事件。","premise":"海燕号在港外失去无线电信号，港务日志却显示它从未获准入港。昨夜有人看见一艘无灯船驶向废弃灯塔。","knownFacts":["今晚午夜将出现全年最低潮。","灯塔守人三天未回港补给。","港务档案中有一页航线记录被替换。"],"objectives":["确认海燕号的航线。","查明灯塔和无灯船的联系。","在潮水上涨前撤离危险区域。"],"opening":"晚上 9:15，浓雾压在码头上。港务员把一串钥匙推给你，说午夜后旧防波堤会被海水淹没。","suggestedActions":["查询港务日志","询问酒馆船员","前往旧仓库"],"contentNote":"无剧透玩家提要。"},"keeperGuide":"海燕号运输一件会发出低频呼唤的海底石碑。走私者将船引到灯塔下的潮洞卸货，石碑正在诱使听见声音的人走向海中。倒计时以午夜潮汐体现。","director":{"threatClocks":[{"id":"harbor-tide","name":"午夜涨潮","current":0,"max":4,"consequence":"旧防波堤与潮洞退路被上涨海水切断。","authored":true,"maxAdvancePerEvaluation":1,"advanceRules":[{"id":"harbor-stall-pressure","event":"stall","turns":3,"amount":1,"once":false,"cooldownTurns":2},{"id":"harbor-threat-pressure","event":"semantic","kinds":["THREAT"],"amount":1,"once":false,"cooldownTurns":1}],"resolveRules":[{"id":"harbor-safe-exit","event":"node","nodeIds":["harbor-ending-light","harbor-ending-rescue","harbor-ending-flood"],"once":true}]}]},"chapters":[{"id":"scenario-fog-harbor-chapter","title":"调查记录","scenes":[{"id":"scenario-fog-harbor-scene","title":"雾港夜航","nodes":[{"id":"harbor-office","title":"港务办公室","background":"档案柜缺了一份入港许可，墙上的潮汐表标出午夜最低潮。","goals":["查明伪造航线"],"clues":[{"id":"harbor-log","name":"被替换的日志页","description":"纸张来自旧仓库的货单本。","hidden":true}],"npcs":[{"id":"harbor-clerk","name":"港务员罗升","description":"怕承担失职责任，但愿意提供钥匙。","attitude":"紧张"}],"optionalChecks":[{"id":"harbor-library","system":"coc7","type":"skill","skillId":"library_use","label":"图书馆使用","difficulty":"regular","mandatory":false,"reason":"核对航线与潮汐记录","visibility":"public","trigger":"on_action","once":true},{"id":"harbor-psych","system":"coc7","type":"skill","skillId":"psychology","label":"心理学","difficulty":"regular","mandatory":false,"reason":"识别港务员隐瞒的细节","visibility":"public","trigger":"on_action","once":true}],"mandatoryChecks":[],"exits":[{"id":"harbor-tavern","label":"前往海员酒馆","targetNodeId":"harbor-tavern","condition":null},{"id":"harbor-warehouse","label":"检查旧仓库","targetNodeId":"harbor-warehouse","condition":null}],"keeperNotes":"任何调查路径都应得到灯塔方向的线索。","rawText":""},{"id":"harbor-tavern","title":"海员酒馆","background":"醉酒水手反复说海面下有人敲船底，老板不愿谈失踪的灯塔守人。","goals":["收集目击证词"],"clues":[{"id":"harbor-witness","name":"水手证词","description":"无灯船在退潮时驶向灯塔北侧。","hidden":true}],"npcs":[{"id":"harbor-sailor","name":"水手阿坤","description":"惊恐、醉酒，听见过海下敲击声。","attitude":"不安"}],"optionalChecks":[{"id":"harbor-listen","system":"coc7","type":"skill","skillId":"listen","label":"聆听","difficulty":"regular","mandatory":false,"reason":"从嘈杂谈话中捕捉一致细节","visibility":"public","trigger":"on_action","once":true},{"id":"harbor-social","system":"coc7","type":"skill","skillId":"charm","label":"魅惑","difficulty":"regular","mandatory":false,"reason":"让酒馆老板开口","visibility":"public","trigger":"on_action","once":true}],"mandatoryChecks":[],"exits":[{"id":"harbor-warehouse-from-tavern","label":"前往旧仓库","targetNodeId":"harbor-warehouse","condition":null},{"id":"harbor-pier","label":"直接赶往旧防波堤","targetNodeId":"harbor-pier","condition":null}],"keeperNotes":"失败可通过付钱、出示委托或等待水手主动崩溃获得部分信息。","rawText":""},{"id":"harbor-warehouse","title":"旧仓库","background":"仓库地板有新鲜拖痕，角落堆着盐水浸透的固定架。","goals":["确认走私货物性质","找到前往灯塔的工具"],"clues":[{"id":"harbor-crate","name":"石碑固定架","description":"尺寸对应一件沉重而不规则的石制品。","hidden":true},{"id":"harbor-map","name":"潮洞草图","description":"标出低潮时可步行通过的礁石路。","hidden":true}],"npcs":[],"optionalChecks":[{"id":"harbor-spot","system":"coc7","type":"skill","skillId":"spot_hidden","label":"侦查","difficulty":"regular","mandatory":false,"reason":"寻找隐藏货单和草图","visibility":"public","trigger":"on_action","once":true},{"id":"harbor-mech","system":"coc7","type":"skill","skillId":"mechanical_repair","label":"机械维修","difficulty":"regular","mandatory":false,"reason":"修复一台旧绞盘","visibility":"public","trigger":"on_action","once":true}],"mandatoryChecks":[],"exits":[{"id":"harbor-pier-from-warehouse","label":"带着装备前往防波堤","targetNodeId":"harbor-pier","condition":null}],"keeperNotes":"草图是核心线索，检定失败也应以耗时或轻伤换取。","rawText":""},{"id":"harbor-pier","title":"旧防波堤","background":"雾中传来规律敲击，退潮露出通往灯塔的礁石路。","goals":["在潮水回升前抵达灯塔"],"clues":[],"npcs":[],"optionalChecks":[{"id":"harbor-nav","system":"coc7","type":"skill","skillId":"navigate","label":"导航","difficulty":"regular","mandatory":false,"reason":"在浓雾和礁石间选择路线","visibility":"public","trigger":"on_action","once":true},{"id":"harbor-listen-pier","system":"coc7","type":"skill","skillId":"listen","label":"聆听","difficulty":"regular","mandatory":false,"reason":"分辨敲击来自海面还是脑中","visibility":"public","trigger":"on_action","once":true}],"mandatoryChecks":[{"id":"harbor-san","system":"coc7","type":"san","skillId":"san","label":"理智","difficulty":"regular","mandatory":true,"reason":"意识到敲击声与自己的心跳同步","loss":"0/1d3","visibility":"public","trigger":"on_enter","once":true}],"exits":[{"id":"harbor-lighthouse","label":"抵达灯塔","targetNodeId":"harbor-lighthouse","condition":null}],"keeperNotes":"导航失败可造成时间推进、损失物品或 HP，但仍抵达灯塔。","rawText":""},{"id":"harbor-lighthouse","title":"废弃灯塔","background":"灯室熄灭，地下门后传来海水涌动和人声吟唱。","goals":["找到灯塔守人","阻止石碑被运入潮洞"],"clues":[{"id":"harbor-keeper-note","name":"守塔人笔记","description":"石碑的声音在灯亮时会减弱。","hidden":true}],"npcs":[{"id":"harbor-smuggler","name":"走私头目顾四","description":"想尽快卸货，对石碑影响毫无认识。","attitude":"敌对"}],"optionalChecks":[{"id":"harbor-electric","system":"coc7","type":"skill","skillId":"electrical_repair","label":"电气维修","difficulty":"regular","mandatory":false,"reason":"重新点亮灯塔","visibility":"public","trigger":"on_action","once":true},{"id":"harbor-intimidate","system":"coc7","type":"skill","skillId":"intimidate","label":"恐吓","difficulty":"regular","mandatory":false,"reason":"迫使走私者撤离","visibility":"public","trigger":"on_action","once":true}],"mandatoryChecks":[],"exits":[{"id":"harbor-cave","label":"进入潮洞","targetNodeId":"harbor-cave","condition":null},{"id":"harbor-light-end","label":"点亮灯塔并封锁入口","targetNodeId":"harbor-ending-light","condition":{"flag":"lighthouseLit","equals":true}}],"keeperNotes":"电气维修成功应设置 lighthouseLit。社交或冲突都能让走私者暂时退开。","rawText":""},{"id":"harbor-cave","title":"低潮洞穴","background":"石碑半浸在海水中，失踪船员围绕它站立，像在等待潮水。","goals":["切断石碑影响并带人撤离"],"clues":[],"npcs":[{"id":"harbor-crew","name":"失踪船员","description":"意识受控，对外界刺激反应迟缓。","attitude":"受控"}],"optionalChecks":[{"id":"harbor-cave-occult","system":"coc7","type":"skill","skillId":"occult","label":"神秘学","difficulty":"regular","mandatory":false,"reason":"判断仪式结构","visibility":"public","trigger":"on_action","once":true},{"id":"harbor-cave-mech","system":"coc7","type":"skill","skillId":"mechanical_repair","label":"机械维修","difficulty":"regular","mandatory":false,"reason":"破坏固定架并拖离石碑","visibility":"public","trigger":"on_action","once":true}],"mandatoryChecks":[{"id":"harbor-cave-san","system":"coc7","type":"san","skillId":"san","label":"理智","difficulty":"regular","mandatory":true,"reason":"看见海水中并不存在于潮洞尺寸内的巨大影子","loss":"1/1d6","visibility":"public","trigger":"on_enter","once":true}],"exits":[{"id":"harbor-rescue-end","label":"带船员撤离","targetNodeId":"harbor-ending-rescue","condition":null},{"id":"harbor-flood-end","label":"放弃石碑，抢在涨潮前离开","targetNodeId":"harbor-ending-flood","condition":null}],"keeperNotes":"不要让战斗成为唯一解。灯光、噪声、破坏固定架或说服受控船员都可构成解决方案。","rawText":""},{"id":"harbor-ending-light","title":"结局：灯塔重新亮起","background":"强光压制了呼唤，潮洞入口被港务队封锁。","goals":[],"clues":[],"npcs":[],"optionalChecks":[],"mandatoryChecks":[],"exits":[],"keeperNotes":"控制型结局。","rawText":""},{"id":"harbor-ending-rescue","title":"结局：退潮线之外","background":"你带回了部分船员和石碑运输证据，但海下敲击仍未彻底消失。","goals":[],"clues":[],"npcs":[],"optionalChecks":[],"mandatoryChecks":[],"exits":[],"keeperNotes":"救援结局。","rawText":""},{"id":"harbor-ending-flood","title":"结局：潮水抹去脚印","background":"你活着离开，石碑与无灯船一同消失在上涨的海水中。","goals":[],"clues":[],"npcs":[],"optionalChecks":[],"mandatoryChecks":[],"exits":[],"keeperNotes":"生还但未解决的结局。","rawText":""}]}]}]},{"id":"scenario-lightless-train","title":"无灯列车","mode":"structured","system":"coc7","metadata":{"era":"1920s","eraLabel":"1920 年代","difficulty":"普通","themes":["封闭空间","身份推理"],"estimatedMinutes":130,"combatLevel":"低","horrorLevel":"中","recommendedOccupations":["journalist","private_investigator","professor"],"recommendedSkills":["psychology","spot_hidden","listen","history"],"sourceType":"original","sourceNote":"原创短模组；采用封闭列车、证词矛盾与可回溯节点设计。"},"briefing":{"subtitle":"多出的一节车厢","era":"1928 年春，沪宁夜班列车","playerRole":"你乘夜班列车前往省城，可能是出差、访友或护送文件。","premise":"列车穿过隧道后突然熄灯。灯亮时，一名乘客失踪，列车尾部却多出一节没有编号的车厢。","knownFacts":["列车不会在天亮前停站。","失踪者随身携带一只封蜡公文箱。","乘务员坚称列车编组没有变化。"],"objectives":["确认失踪者身份与公文箱内容。","查明无编号车厢的来源。","避免列车驶入不存在的终点站。"],"opening":"凌晨 1:20，车灯熄灭了十二秒。灯亮后，邻座只剩一顶帽子，走廊尽头出现一扇此前不存在的车门。","suggestedActions":["询问乘客","检查座位","寻找乘务员"],"contentNote":"无剧透玩家提要。"},"keeperGuide":"失踪者是铁路测绘员，公文箱内的旧线路图记录了一条因事故被废弃的支线。无编号车厢由事故死者的集体记忆构成，正试图让整列车重走旧线。","chapters":[{"id":"scenario-lightless-train-chapter","title":"调查记录","scenes":[{"id":"scenario-lightless-train-scene","title":"无灯列车","nodes":[{"id":"train-dining","title":"餐车","background":"乘客聚集在餐车，关于熄灯前后发生的事说法互相冲突。","goals":["建立时间线","确认谁见过失踪者"],"clues":[],"npcs":[{"id":"train-conductor","name":"列车长许岩","description":"维护秩序但明显回避旧支线话题。","attitude":"克制"},{"id":"train-widow","name":"黑衣妇人","description":"声称认识失踪者，却说不出姓名。","attitude":"悲伤"}],"optionalChecks":[{"id":"train-psych","system":"coc7","type":"skill","skillId":"psychology","label":"心理学","difficulty":"regular","mandatory":false,"reason":"辨认证词中的真实恐惧","visibility":"public","trigger":"on_action","once":true},{"id":"train-listen","system":"coc7","type":"skill","skillId":"listen","label":"聆听","difficulty":"regular","mandatory":false,"reason":"捕捉车轮节奏的异常","visibility":"public","trigger":"on_action","once":true}],"mandatoryChecks":[],"exits":[{"id":"train-sleeper","label":"检查失踪者卧铺","targetNodeId":"train-sleeper","condition":null},{"id":"train-baggage","label":"前往行李车","targetNodeId":"train-baggage","condition":null}],"keeperNotes":"黑衣妇人是旧事故死者的亲属，她不是敌人。","rawText":""},{"id":"train-sleeper","title":"卧铺车厢","background":"失踪者座位下有撕碎的线路图，窗外地标与列车时刻不符。","goals":["拼出旧线路信息"],"clues":[{"id":"train-map-fragment","name":"线路图碎片","description":"标有已废弃的『北塘支线』。","hidden":true}],"npcs":[],"optionalChecks":[{"id":"train-spot","system":"coc7","type":"skill","skillId":"spot_hidden","label":"侦查","difficulty":"regular","mandatory":false,"reason":"寻找被藏起的图纸碎片","visibility":"public","trigger":"on_action","once":true},{"id":"train-history","system":"coc7","type":"skill","skillId":"history","label":"历史","difficulty":"regular","mandatory":false,"reason":"回忆北塘事故","visibility":"public","trigger":"on_action","once":true}],"mandatoryChecks":[],"exits":[{"id":"train-dining-back","label":"返回餐车","targetNodeId":"train-dining","condition":null},{"id":"train-baggage-from-sleeper","label":"追查公文箱","targetNodeId":"train-baggage","condition":null}],"keeperNotes":"图纸碎片为核心信息，失败可让黑衣妇人指出藏匿位置。","rawText":""},{"id":"train-baggage","title":"行李车","background":"一只封蜡公文箱被铁链固定，车厢另一端多出通向无编号车厢的门。","goals":["取得旧线路图","决定是否打开未知车门"],"clues":[{"id":"train-case","name":"公文箱","description":"内有完整旧线路图和事故调查报告。","hidden":true}],"npcs":[],"optionalChecks":[{"id":"train-lock","system":"coc7","type":"skill","skillId":"locksmith","label":"锁匠","difficulty":"regular","mandatory":false,"reason":"打开公文箱","visibility":"public","trigger":"on_action","once":true},{"id":"train-law","system":"coc7","type":"skill","skillId":"law","label":"法律","difficulty":"regular","mandatory":false,"reason":"从封条和文件编号判断委托方","visibility":"public","trigger":"on_action","once":true}],"mandatoryChecks":[],"exits":[{"id":"train-engine","label":"带报告去机车","targetNodeId":"train-engine","condition":null},{"id":"train-ghost-car","label":"进入无编号车厢","targetNodeId":"train-ghost-car","condition":null}],"keeperNotes":"开箱失败时可通过列车长钥匙、破坏铁链或与黑衣妇人合作推进。","rawText":""},{"id":"train-ghost-car","title":"无编号车厢","background":"车厢内坐满沉默乘客，每个人都保持着事故发生前一刻的姿势。","goals":["找到失踪者","理解车厢想要什么"],"clues":[],"npcs":[{"id":"train-surveyor","name":"失踪测绘员","description":"被迫重复绘制旧线终点。","attitude":"恍惚"}],"optionalChecks":[{"id":"train-ghost-psych","system":"coc7","type":"skill","skillId":"psychology","label":"心理学","difficulty":"regular","mandatory":false,"reason":"理解死者并非单纯攻击","visibility":"public","trigger":"on_action","once":true},{"id":"train-ghost-occult","system":"coc7","type":"skill","skillId":"occult","label":"神秘学","difficulty":"regular","mandatory":false,"reason":"判断车厢与路线记忆的联系","visibility":"public","trigger":"on_action","once":true}],"mandatoryChecks":[{"id":"train-ghost-san","system":"coc7","type":"san","skillId":"san","label":"理智","difficulty":"regular","mandatory":true,"reason":"发现沉默乘客都没有呼吸","loss":"0/1d4","visibility":"public","trigger":"on_enter","once":true}],"exits":[{"id":"train-engine-from-ghost","label":"带测绘员前往机车","targetNodeId":"train-engine","condition":null},{"id":"train-memory-end","label":"留下报告安抚车厢","targetNodeId":"train-ending-memory","condition":null}],"keeperNotes":"安抚死者需要承认事故真相，而非消灭车厢。","rawText":""},{"id":"train-engine","title":"机车驾驶室","background":"前方信号指向不存在的北塘支线，制动和道岔控制同时失灵。","goals":["让列车留在现实线路"],"clues":[],"npcs":[{"id":"train-driver","name":"司机周师傅","description":"正在与自行转动的控制杆搏斗。","attitude":"恐慌"}],"optionalChecks":[{"id":"train-mech","system":"coc7","type":"skill","skillId":"mechanical_repair","label":"机械维修","difficulty":"regular","mandatory":false,"reason":"恢复制动或道岔控制","visibility":"public","trigger":"on_action","once":true},{"id":"train-persuade","system":"coc7","type":"skill","skillId":"persuade","label":"说服","difficulty":"regular","mandatory":false,"reason":"让列车长公开事故真相并广播","visibility":"public","trigger":"on_action","once":true}],"mandatoryChecks":[],"exits":[{"id":"train-safe-end","label":"扳回主线","targetNodeId":"train-ending-safe","condition":null},{"id":"train-memory-end2","label":"以事故报告安抚列车","targetNodeId":"train-ending-memory","condition":null}],"keeperNotes":"机械成功或公开真相都可解决；失败应带来伤害和损失，但允许最后一次选择。","rawText":""},{"id":"train-ending-safe","title":"结局：晨光中的站台","background":"列车在黎明前回到主线，无编号车厢从编组表上消失。","goals":[],"clues":[],"npcs":[],"optionalChecks":[],"mandatoryChecks":[],"exits":[],"keeperNotes":"现实线结局。","rawText":""},{"id":"train-ending-memory","title":"结局：北塘终点","background":"你让被掩盖的事故重新被人记住。无编号车厢在一座废弃站台旁停靠，然后归于黑暗。","goals":[],"clues":[],"npcs":[],"optionalChecks":[],"mandatoryChecks":[],"exits":[],"keeperNotes":"和解结局。","rawText":""}]}]}]},{"id":"scenario-sanatorium","title":"山间疗养院","mode":"structured","system":"coc7","metadata":{"era":"modern","eraLabel":"现代","difficulty":"困难","themes":["医疗调查","心理恐怖"],"estimatedMinutes":170,"combatLevel":"低","horrorLevel":"高","recommendedOccupations":["nurse","journalist","private_investigator"],"recommendedSkills":["medicine","psychology","library_use","science_pharmacy"],"sourceType":"original","sourceNote":"原创短模组；采用病历调查、封闭病区和逐层揭露结构。"},"briefing":{"subtitle":"被改写的病历与凌晨广播","era":"现代，北方山地康复中心","playerRole":"你因调查失踪患者、医疗纠纷或旧友求助进入山间疗养院。","premise":"疗养院宣称一名患者已正常出院，但家属从未见其回家。每晚 2:17，院内广播会念出不存在于患者名单中的姓名。","knownFacts":["疗养院近期更换了全部纸质病历。","地下治疗区在官方平面图上不存在。","暴雪可能在数小时内封路。"],"objectives":["找到原始病历。","确认失踪患者去向。","判断广播姓名与治疗项目的联系。"],"opening":"下午 5:30，你在暴雪前抵达。前台要求你交出手机，值班医生则反复强调这里没有失踪患者。","suggestedActions":["询问前台","查看公开病历","观察住院区"],"contentNote":"高压心理恐怖主题，无剧透提要。"},"keeperGuide":"院方用实验性声波疗法诱导患者共享记忆，失败病例被转移至地下治疗区。广播是设备在自动读取未清除的患者档案。核心解决可以是关闭设备、救出患者或公开证据。","chapters":[{"id":"scenario-sanatorium-chapter","title":"调查记录","scenes":[{"id":"scenario-sanatorium-scene","title":"山间疗养院","nodes":[{"id":"san-reception","title":"接待大厅","background":"前台整洁得不自然，墙钟停在 2:17。","goals":["取得病历室权限","识别员工异常"],"clues":[],"npcs":[{"id":"san-doctor","name":"值班医生韩启","description":"态度礼貌但严格控制信息。","attitude":"防备"},{"id":"san-receptionist","name":"前台冯岚","description":"害怕夜间广播。","attitude":"紧张"}],"optionalChecks":[{"id":"san-psych","system":"coc7","type":"skill","skillId":"psychology","label":"心理学","difficulty":"regular","mandatory":false,"reason":"识别医生的回避模式","visibility":"public","trigger":"on_action","once":true},{"id":"san-persuade","system":"coc7","type":"skill","skillId":"persuade","label":"说服","difficulty":"regular","mandatory":false,"reason":"取得有限参观许可","visibility":"public","trigger":"on_action","once":true}],"mandatoryChecks":[],"exits":[{"id":"san-records","label":"前往病历室","targetNodeId":"san-records","condition":null},{"id":"san-ward","label":"进入住院区","targetNodeId":"san-ward","condition":null}],"keeperNotes":"前台可在医生离开后偷偷提供旧钥匙。","rawText":""},{"id":"san-records","title":"病历室","background":"电子档案记录完整，纸质档案却存在连续编号缺口。","goals":["找回被替换的原始病历"],"clues":[{"id":"san-index","name":"缺号索引","description":"缺失编号对应广播中的姓名。","hidden":true},{"id":"san-drug","name":"用药记录","description":"大量镇静剂被转入不存在的地下病区。","hidden":true}],"npcs":[],"optionalChecks":[{"id":"san-library","system":"coc7","type":"skill","skillId":"library_use","label":"图书馆使用","difficulty":"regular","mandatory":false,"reason":"比对新旧病历索引","visibility":"public","trigger":"on_action","once":true},{"id":"san-pharmacy","system":"coc7","type":"skill","skillId":"science_pharmacy","label":"科学（药学）","difficulty":"regular","mandatory":false,"reason":"分析异常用药","visibility":"public","trigger":"on_action","once":true}],"mandatoryChecks":[],"exits":[{"id":"san-ward-from-records","label":"前往住院区","targetNodeId":"san-ward","condition":null},{"id":"san-basement-from-records","label":"沿物流记录寻找地下入口","targetNodeId":"san-basement","condition":null}],"keeperNotes":"缺号索引是必得线索，失败以花费更多时间换取。","rawText":""},{"id":"san-ward","title":"封闭病区","background":"患者在广播响起前集体捂住耳朵，一名老人不断写同一个名字。","goals":["获得患者证词","确认广播影响"],"clues":[],"npcs":[{"id":"san-patient","name":"患者赵伯","description":"记得地下治疗室的电梯密码片段。","attitude":"恐惧"}],"optionalChecks":[{"id":"san-listen","system":"coc7","type":"skill","skillId":"listen","label":"聆听","difficulty":"regular","mandatory":false,"reason":"辨认广播中的反向低语","visibility":"public","trigger":"on_action","once":true},{"id":"san-psych2","system":"coc7","type":"skill","skillId":"psychology","label":"心理学","difficulty":"regular","mandatory":false,"reason":"让患者从重复状态中恢复","visibility":"public","trigger":"on_action","once":true}],"mandatoryChecks":[{"id":"san-ward-san","system":"coc7","type":"san","skillId":"san","label":"理智","difficulty":"regular","mandatory":true,"reason":"所有患者同时说出你的姓名","loss":"0/1d3","visibility":"public","trigger":"on_enter","once":true}],"exits":[{"id":"san-pharmacy","label":"检查配药间","targetNodeId":"san-pharmacy","condition":null},{"id":"san-basement","label":"寻找地下治疗区","targetNodeId":"san-basement","condition":null}],"keeperNotes":"患者证词可提供密码，失败时通过墙上抓痕给出替代提示。","rawText":""},{"id":"san-pharmacy","title":"配药间","background":"冰箱里存放着没有标签的注射剂，废纸篓中有地下病区交接单。","goals":["确认药物用途","获得进入地下区的证据"],"clues":[{"id":"san-transfer","name":"地下交接单","description":"记录失踪患者仍在院内。","hidden":true}],"npcs":[],"optionalChecks":[{"id":"san-medicine","system":"coc7","type":"skill","skillId":"medicine","label":"医学","difficulty":"regular","mandatory":false,"reason":"判断药物造成的记忆障碍","visibility":"public","trigger":"on_action","once":true},{"id":"san-spot","system":"coc7","type":"skill","skillId":"spot_hidden","label":"侦查","difficulty":"regular","mandatory":false,"reason":"找到隐藏交接单","visibility":"public","trigger":"on_action","once":true}],"mandatoryChecks":[],"exits":[{"id":"san-basement-from-pharmacy","label":"进入地下区","targetNodeId":"san-basement","condition":null}],"keeperNotes":"药物不是神话源头，只是控制患者的手段。","rawText":""},{"id":"san-basement","title":"地下治疗区","background":"走廊尽头的设备持续播放低频声波，玻璃病房内躺着多名被注销的患者。","goals":["关闭设备","救出失踪患者","保存实验记录"],"clues":[{"id":"san-protocol","name":"实验协议","description":"院方试图让不同患者共享创伤记忆。","hidden":true}],"npcs":[{"id":"san-missing","name":"失踪患者林澈","description":"意识断续，仍可被救出。","attitude":"虚弱"}],"optionalChecks":[{"id":"san-electric","system":"coc7","type":"skill","skillId":"electrical_repair","label":"电气维修","difficulty":"regular","mandatory":false,"reason":"安全关闭声波设备","visibility":"public","trigger":"on_action","once":true},{"id":"san-medicine2","system":"coc7","type":"skill","skillId":"medicine","label":"医学","difficulty":"regular","mandatory":false,"reason":"稳定被长期镇静的患者","visibility":"public","trigger":"on_action","once":true}],"mandatoryChecks":[{"id":"san-basement-san","system":"coc7","type":"san","skillId":"san","label":"理智","difficulty":"hard","mandatory":true,"reason":"设备把陌生人的死亡记忆投射进你的意识","loss":"1/1d6","visibility":"public","trigger":"on_enter","once":true}],"exits":[{"id":"san-rescue","label":"组织撤离","targetNodeId":"san-ending-rescue","condition":null},{"id":"san-evidence","label":"复制记录并报警","targetNodeId":"san-ending-evidence","condition":null}],"keeperNotes":"设备可被关闭、破坏或切断供电。失败会增加 SAN/HP 代价，但仍允许撤离。","rawText":""},{"id":"san-ending-rescue","title":"结局：雪线以下","background":"你带着幸存患者离开疗养院，部分人的记忆仍相互混杂。","goals":[],"clues":[],"npcs":[],"optionalChecks":[],"mandatoryChecks":[],"exits":[],"keeperNotes":"救援结局。","rawText":""},{"id":"san-ending-evidence","title":"结局：不存在的病历","background":"实验记录被公开，疗养院停业调查，但地下广播在断电后仍响了一次。","goals":[],"clues":[],"npcs":[],"optionalChecks":[],"mandatoryChecks":[],"exits":[],"keeperNotes":"证据结局。","rawText":""}]}]}]},{"id":"scenario-cinema","title":"消失的放映厅","mode":"structured","system":"coc7","metadata":{"era":"modern","eraLabel":"现代","difficulty":"入门","themes":["都市怪谈","影像调查"],"estimatedMinutes":110,"combatLevel":"无或极低","horrorLevel":"中","recommendedOccupations":["journalist","professor","private_investigator"],"recommendedSkills":["art_photography","electrical_repair","spot_hidden","occult"],"sourceType":"original","sourceNote":"原创短模组；采用单地点多层线索与非战斗解决结构。"},"briefing":{"subtitle":"最后一卷没有片名的胶片","era":"现代，停业十年的银星电影院","playerRole":"你受失踪放映员家属、城市档案馆或自媒体调查委托进入废弃电影院。","premise":"旧影院将在明早拆除。昨夜，失踪十年的放映员账号突然上传了一段只有十二秒的影片，画面显示影院内存在一间建筑图上没有的放映厅。","knownFacts":["影院在十年前一场小规模火灾后停业。","上传影片的时间戳是今晚 23:40。","拆除队称内部电力早已切断。"],"objectives":["找到影片拍摄位置。","查明放映员失踪原因。","在拆除前保存必要证据。"],"opening":"晚上 10:05，你推开影院侧门。售票厅灰尘厚重，远处却传来胶片转动声。","suggestedActions":["检查售票厅","寻找配电箱","前往放映室"],"contentNote":"无剧透玩家提要。"},"keeperGuide":"火灾当晚，放映员把一卷记录观众集体幻觉的实验胶片藏入夹层。胶片会把观看者的记忆剪入影像，并生成对应的空间。解决方式包括按正确顺序倒放、曝光胶片或彻底断电。","chapters":[{"id":"scenario-cinema-chapter","title":"调查记录","scenes":[{"id":"scenario-cinema-scene","title":"消失的放映厅","nodes":[{"id":"cinema-lobby","title":"售票厅","background":"海报已经褪色，售票窗口和地面覆着厚灰，远处却持续传来胶片转动的轻响。","goals":["确认影院近期有人进入","寻找供电来源"],"clues":[{"id":"cinema-ticket","name":"今日票根","description":"背面写着『不要从第一幕开始』。","hidden":true}],"npcs":[],"optionalChecks":[{"id":"cinema-passive-spot","system":"coc7","type":"skill","skillId":"spot_hidden","label":"侦查","difficulty":"regular","mandatory":true,"reason":"被动留意售票厅中的近期活动痕迹","visibility":"secret","trigger":"on_enter","once":true,"successText":"厚重灰尘并不完全平整：一串不久前留下的鞋印从侧门经过售票窗，窗台下还压着一张边缘干净的票根。","successStateChanges":[{"operation":"revealClue","clueId":"cinema-ticket","reason":"被动发现近期活动痕迹"}]},{"id":"cinema-spot","system":"coc7","type":"skill","skillId":"spot_hidden","label":"侦查","difficulty":"regular","mandatory":false,"reason":"主动检查新鲜脚印和售票窗口","visibility":"public","trigger":"on_action","once":true},{"id":"cinema-electric","system":"coc7","type":"skill","skillId":"electrical_repair","label":"电气维修","difficulty":"regular","mandatory":false,"reason":"追踪仍在工作的线路","visibility":"public","trigger":"on_action","once":true}],"mandatoryChecks":[],"exits":[{"id":"cinema-projection","label":"前往主放映室","targetNodeId":"cinema-projection","condition":null},{"id":"cinema-archive","label":"进入影片档案库","targetNodeId":"cinema-archive","condition":null}],"keeperNotes":"票根提示逆序观看，是解决胶片的关键之一。","rawText":""},{"id":"cinema-projection","title":"主放映室","background":"老式放映机自行转动，空片盘却投出模糊人影。","goals":["停止放映机","找到隐藏胶片编号"],"clues":[{"id":"cinema-reel-note","name":"放映记录","description":"最后一卷标记为 4-3-2-1。","hidden":true}],"npcs":[],"optionalChecks":[{"id":"cinema-mech","system":"coc7","type":"skill","skillId":"mechanical_repair","label":"机械维修","difficulty":"regular","mandatory":false,"reason":"安全停下放映机","visibility":"public","trigger":"on_action","once":true},{"id":"cinema-photo","system":"coc7","type":"skill","skillId":"art_photography","label":"艺术/手艺（摄影）","difficulty":"regular","mandatory":false,"reason":"分析异常画面层次","visibility":"public","trigger":"on_action","once":true}],"mandatoryChecks":[{"id":"cinema-san","system":"coc7","type":"san","skillId":"san","label":"理智","difficulty":"regular","mandatory":true,"reason":"投影中的自己转头看向放映窗","loss":"0/1d3","visibility":"public","trigger":"on_enter","once":true}],"exits":[{"id":"cinema-archive-from-projection","label":"检查档案库","targetNodeId":"cinema-archive","condition":null},{"id":"cinema-hidden","label":"循着异常投影寻找夹层","targetNodeId":"cinema-hidden","condition":null}],"keeperNotes":"即使停机失败，拔掉片盘或遮住镜头也能暂时中断影响。","rawText":""},{"id":"cinema-archive","title":"影片档案库","background":"铁柜中缺少四卷胶片，目录卡的顺序被人为调换。","goals":["重建胶片顺序","找到失踪放映员记录"],"clues":[{"id":"cinema-diary","name":"放映员日记","description":"写着『正序让房间长出来，逆序让它忘记。』","hidden":true}],"npcs":[],"optionalChecks":[{"id":"cinema-library","system":"coc7","type":"skill","skillId":"library_use","label":"图书馆使用","difficulty":"regular","mandatory":false,"reason":"整理目录与日记","visibility":"public","trigger":"on_action","once":true},{"id":"cinema-history","system":"coc7","type":"skill","skillId":"history","label":"历史","difficulty":"regular","mandatory":false,"reason":"调查影院火灾报道","visibility":"public","trigger":"on_action","once":true}],"mandatoryChecks":[],"exits":[{"id":"cinema-hidden-from-archive","label":"按线索寻找隐藏放映厅","targetNodeId":"cinema-hidden","condition":null},{"id":"cinema-projection-back","label":"返回主放映室","targetNodeId":"cinema-projection","condition":null}],"keeperNotes":"日记是核心解法提示，失败可通过目录卡明显的 4-3-2-1 顺序获得。","rawText":""},{"id":"cinema-hidden","title":"隐藏放映厅","background":"窄小房间里坐着十年前失踪的放映员。他没有衰老，目光始终停在银幕上。","goals":["唤醒放映员","决定如何处理胶片"],"clues":[],"npcs":[{"id":"cinema-projectionist","name":"放映员顾远","description":"意识被胶片困在重复的火灾夜晚。","attitude":"恍惚"}],"optionalChecks":[{"id":"cinema-psych","system":"coc7","type":"skill","skillId":"psychology","label":"心理学","difficulty":"regular","mandatory":false,"reason":"让放映员意识到时间已过去","visibility":"public","trigger":"on_action","once":true},{"id":"cinema-occult","system":"coc7","type":"skill","skillId":"occult","label":"神秘学","difficulty":"regular","mandatory":false,"reason":"理解胶片与记忆空间的规则","visibility":"public","trigger":"on_action","once":true}],"mandatoryChecks":[{"id":"cinema-hidden-san","system":"coc7","type":"san","skillId":"san","label":"理智","difficulty":"regular","mandatory":true,"reason":"银幕播放出你尚未经历的死亡场景","loss":"1/1d4","visibility":"public","trigger":"on_enter","once":true}],"exits":[{"id":"cinema-reverse","label":"逆序放映四卷胶片","targetNodeId":"cinema-ending-release","condition":null},{"id":"cinema-burn","label":"曝光并销毁胶片","targetNodeId":"cinema-ending-burn","condition":null}],"keeperNotes":"两种结局都可成立；逆序更可能救回放映员，销毁更彻底但可能让其记忆消失。","rawText":""},{"id":"cinema-ending-release","title":"结局：散场字幕","background":"逆序影像吞回不存在的空间，放映员在十年后的灰尘中醒来。","goals":[],"clues":[],"npcs":[],"optionalChecks":[],"mandatoryChecks":[],"exits":[],"keeperNotes":"救援结局。","rawText":""},{"id":"cinema-ending-burn","title":"结局：白光之后","background":"胶片在强光中化为透明，隐藏放映厅随之消失，只留下空座椅和一段无法证明的录像。","goals":[],"clues":[],"npcs":[],"optionalChecks":[],"mandatoryChecks":[],"exits":[],"keeperNotes":"封印结局。","rawText":""}]}]}]}];
const BUILTIN_CLUE_ROUTE_OVERRIDES = {
  "old-footprints": {
    "checks": [
      "old-hall-passive-spot",
      "old-hall-spot"
    ],
    "failureForward": [
      "old-hall-spot"
    ]
  },
  "old-key": {
    "checks": [
      "old-room-social"
    ],
    "automatic": true,
    "failureForward": [
      "old-room-social"
    ]
  },
  "old-medicine": {
    "automatic": true
  },
  "old-ledger": {
    "checks": [
      "old-study-library"
    ],
    "failureForward": [
      "old-study-library"
    ]
  },
  "old-scratch": {
    "checks": [
      "old-study-spot"
    ],
    "failureForward": [
      "old-study-spot"
    ]
  },
  "old-blueprint": {
    "checks": [
      "old-archive-library"
    ],
    "failureForward": [
      "old-archive-library"
    ]
  },
  "old-shipment": {
    "checks": [
      "old-archive-library"
    ],
    "failureForward": [
      "old-archive-library"
    ]
  },
  "old-notes": {
    "automatic": true
  },
  "harbor-log": {
    "checks": [
      "harbor-library"
    ],
    "failureForward": [
      "harbor-library"
    ]
  },
  "harbor-witness": {
    "checks": [
      "harbor-listen",
      "harbor-social"
    ],
    "failureForward": [
      "harbor-listen",
      "harbor-social"
    ]
  },
  "harbor-crate": {
    "checks": [
      "harbor-spot"
    ],
    "failureForward": [
      "harbor-spot"
    ]
  },
  "harbor-map": {
    "checks": [
      "harbor-spot"
    ],
    "failureForward": [
      "harbor-spot"
    ]
  },
  "harbor-keeper-note": {
    "automatic": true
  },
  "train-map-fragment": {
    "checks": [
      "train-spot"
    ],
    "failureForward": [
      "train-spot"
    ]
  },
  "train-case": {
    "checks": [
      "train-lock"
    ],
    "failureForward": [
      "train-lock"
    ]
  },
  "san-index": {
    "checks": [
      "san-library"
    ],
    "failureForward": [
      "san-library"
    ]
  },
  "san-drug": {
    "checks": [
      "san-library",
      "san-pharmacy"
    ],
    "failureForward": [
      "san-library",
      "san-pharmacy"
    ]
  },
  "san-transfer": {
    "checks": [
      "san-spot"
    ],
    "failureForward": [
      "san-spot"
    ]
  },
  "san-protocol": {
    "automatic": true
  },
  "cinema-ticket": {
    "checks": [
      "cinema-passive-spot",
      "cinema-spot"
    ],
    "failureForward": [
      "cinema-spot"
    ]
  },
  "cinema-reel-note": {
    "automatic": true
  },
  "cinema-diary": {
    "checks": [
      "cinema-library"
    ],
    "failureForward": [
      "cinema-library"
    ]
  }
};
function materializeBuiltinClueRoutes(clueId,spec){
  const routes=[];
  for(const checkId of spec.checks||[])routes.push({id:clueId+"-check-"+checkId,type:"check",checkId,minimumRank:"regular"});
  if(spec.automatic)routes.push({id:clueId+"-automatic",type:"automatic"});
  for(const checkId of spec.failureForward||[])routes.push({id:clueId+"-failure-forward-"+checkId,type:"failure_forward",checkId,cost:{tension:1}});
  return routes
}
(function applyBuiltinClueRouteOverrides(){
  for(const scenario of SCENARIO_LIBRARY)for(const chapter of scenario.chapters||[])for(const scene of chapter.scenes||[])for(const node of scene.nodes||[])for(const clue of node.clues||[]){
    const spec=BUILTIN_CLUE_ROUTE_OVERRIDES[clue.id];
    if(spec)clue.acquisitionRoutes=materializeBuiltinClueRoutes(clue.id,spec)
  }
})();

const PRESET_SCENARIO = SCENARIO_LIBRARY[0];


const PHASE_LABELS={setup:"等待配置",character_ready:"等待选择剧本",scenario_ready:"等待创建角色",awaiting_player_action:"等待玩家行动",requesting_ai:"AI 正在处理行动",awaiting_check:"等待玩家检定",rolling:"正在掷骰",requesting_ai_continuation:"AI 正在续写",awaiting_node_confirmation:"等待确认场景推进",awaiting_ending_confirmation:"等待确认结局",campaign_ended:"调查已结束",error:"请求失败"};
function phaseLabel(value){return PHASE_LABELS[value]||String(value||"未知阶段")}
function listStrings(value,limit=30,max=500){return Array.isArray(value)?value.map(item=>String(typeof item==="string"?item:(item&&item.text)||"").slice(0,max).trim()).filter(Boolean).slice(0,limit):[]}
function defaultDirectorState(){return{sceneTurns:0,totalTurns:0,tension:1,maxTension:6,progress:0,lastProgressTurn:0,lastEscalationTurn:0,activeThreats:[],revealedTruths:[],clocks:[],pendingPressure:null}}
function defaultNavigationState(){return{visitedNodeIds:[],transitionHistory:[],recentLocationSignatures:[],consecutiveSpatialMoves:0,temporaryNodeCount:0,temporaryNodeCountByScene:{},lastMeaningfulNodeId:null,lastProgressMarker:"",loopInterventions:0,lastConfirmedNodeId:null}}
function defaultHintUsage(){return{count:0,lastAt:null,lastCost:""}}
function genericEndings(title){return[
  {id:"ending-withdraw",title:"中止调查",playerTitle:"带着未解之谜离开",alwaysAvailable:true,priority:1,summary:`你选择结束对《${title}》的调查。部分疑问仍未得到回答，威胁也可能继续存在。`},
  {id:"ending-partial",title:"部分真相",playerTitle:"带着有限证据离开",minClues:2,priority:20,summary:"你带走了足以证明异常存在的证据，但核心真相仍不完整。"},
  {id:"ending-contained",title:"威胁受控",playerTitle:"暂时控制局势",requiredFlags:["threatContained"],priority:40,summary:"你暂时控制了眼前的威胁，但无法确认它是否被彻底消除。"},
  {id:"ending-full",title:"完整收束",playerTitle:"真相与代价",requiredFlags:["strongEvidence","threatContained"],minClues:3,priority:80,summary:"你掌握了较完整的证据，并使当前威胁得到控制。真相并未因此变得轻松。"},
  {id:"ending-spread",title:"威胁扩散",playerTitle:"活着离开并不等于结束",requiredFlags:["threatReleased"],priority:100,summary:"你离开了现场，但异常已经越过原本的边界。故事将在别处继续。"}
]}
function makeLoreCardsFromScenario(scenario){const cards=[];for(const chapter of scenario.chapters||[])for(const scene of chapter.scenes||[])for(const node of scene.nodes||[]){cards.push({id:`lore-${node.id}`,title:node.title,triggers:[node.title,...(node.npcs||[]).map(n=>n.name),...(node.clues||[]).map(c=>c.name)].filter(Boolean),content:[node.background,node.keeperNotes].filter(Boolean).join("\n"),visibility:"director",enabled:true});for(const npc of node.npcs||[])cards.push({id:`lore-${npc.id}`,title:npc.name,triggers:[npc.name],content:`${npc.description||""}${npc.attitude?`\n初始态度：${npc.attitude}`:""}`,visibility:"director",enabled:true})}return cards.slice(0,80)}
function augmentScenarioLibraryV13(){for(const scenario of SCENARIO_LIBRARY){scenario.briefing=scenario.briefing||{};scenario.briefing.caseObjectives=listStrings(scenario.briefing.caseObjectives||scenario.briefing.objectives,8,300);delete scenario.briefing.objectives;scenario.initialLeads=Array.isArray(scenario.initialLeads)?scenario.initialLeads:[];scenario.initialQuestions=Array.isArray(scenario.initialQuestions)?scenario.initialQuestions:[];scenario.director=scenario.director||{hiddenTruth:scenario.keeperGuide||"",centralMystery:scenario.briefing.subtitle||scenario.title,mandatoryRevelations:[],optionalRevelations:[],redHerrings:[],escalationPlan:["调查中出现第一处明显异常","NPC 或环境主动施加压力","关键真相浮现并进入收束"],possibleEndings:[]};scenario.endings=Array.isArray(scenario.endings)&&scenario.endings.length?scenario.endings:genericEndings(scenario.title);scenario.loreCards=Array.isArray(scenario.loreCards)&&scenario.loreCards.length?scenario.loreCards:makeLoreCardsFromScenario(scenario)}
  const old=SCENARIO_LIBRARY.find(s=>s.id==="scenario-old-house");if(old){old.briefing.caseObjectives=["确认沈墨的下落。","查明他在顾宅失踪前经历了什么。","在保证自身安全的前提下，带回可信证据。"];old.initialLeads=[{id:"lead-butler",text:"管家周铭声称沈墨已经离开，但其说法仍需核实。",status:"active"},{id:"lead-letter",text:"求助信提到了“书后的门”和“消毒水味”。",status:"active"}];old.initialQuestions=[{id:"question-shenmo",text:"沈墨是否仍然活着？",status:"open"},{id:"question-door",text:"求助信中的“书后的门”指什么？",status:"open"}];old.endings=[
    {id:"old-withdraw",title:"调查中止",playerTitle:"雨夜撤离",alwaysAvailable:true,priority:1,summary:"你选择在真相完全浮现前离开顾宅。沈墨的下落仍然不明，三天后顾宅在一场原因不明的火灾中被烧毁。"},
    {id:"old-rescue",title:"只救出沈墨",playerTitle:"带他离开",requiredFlags:["survivorRescued"],priority:40,summary:"沈墨活着离开了顾宅，但由于证据不足，商会否认一切。他也拒绝再谈地下室里发生的事。"},
    {id:"old-evidence",title:"只带回证据",playerTitle:"证据与遗憾",requiredFlags:["strongEvidence"],minClues:2,priority:45,summary:"你带回了足以证明沈墨并非自行失踪的证据，但未能让所有人安全离开。"},
    {id:"old-deal",title:"接受交易",playerTitle:"沉默的报酬",requiredFlags:["acceptedDeal"],priority:70,summary:"官方记录中，沈墨依然是自行离开。你换得了安全或报酬，但此后每逢雨夜都会收到没有寄件人的档案袋。"},
    {id:"old-public",title:"真相公开",playerTitle:"公开档案",requiredFlags:["strongEvidence","threatContained"],minClues:3,priority:90,summary:"你把完整证据带出顾宅并公开了案件。当地商会随即开始清理相关人员，调查并没有真正结束。"},
    {id:"old-spread",title:"威胁扩散",playerTitle:"消毒水的气味",requiredFlags:["threatReleased"],priority:100,summary:"你活着离开，但几周后，附近医院出现了相同的消毒水气味。"}
  ]}}
augmentScenarioLibraryV13();


/* =========================
   工具函数
========================= */
