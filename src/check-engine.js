/* ???COC/DND ???????? */
function parseDiceExpression(input){
  const text=String(input||"").trim().toLowerCase();
  if(text.length===0||text.length>32)return {ok:false,error:"骰式长度无效"};
  const m=text.match(/^(\d*)d(\d+)([+-]\d+)?$/);if(!m)return {ok:false,error:"仅支持 NdM、dM、NdM+K、NdM-K"};
  const count=m[1]?Number(m[1]):1,faces=Number(m[2]),modifier=m[3]?Number(m[3]):0;
  if(!Number.isInteger(count)||count<1||count>100)return {ok:false,error:"骰子数量必须为 1 到 100"};
  if(!Number.isInteger(faces)||faces<2||faces>10000)return {ok:false,error:"骰子面数必须为 2 到 10000"};
  if(!Number.isInteger(modifier)||modifier<-100000||modifier>100000)return {ok:false,error:"修正值超出范围"};
  return {ok:true,value:{text,count,faces,modifier}};
}
function rollDiceExpression(input){
  const parsed=parseDiceExpression(input);if(!parsed.ok)return parsed;const {count,faces,modifier,text}=parsed.value;
  const rolls=[];for(let i=0;i<count;i++)rolls.push(randomInt(1,faces));const subtotal=rolls.reduce((a,b)=>a+b,0);
  return {ok:true,value:{expression:text,rawRolls:rolls,modifier,total:subtotal+modifier}};
}
const COC_DIFFICULTY_LABELS={regular:"普通",hard:"困难",extreme:"极难"};
const COC_RANK_LABELS={critical:"大成功",extreme:"极难成功",hard:"困难成功",regular:"普通成功",failure:"失败",fumble:"大失败",skipped:"已跳过"};
function cocDifficultyLabel(value){return COC_DIFFICULTY_LABELS[value]||String(value||"普通")}
function cocRankLabel(value){return COC_RANK_LABELS[value]||String(value||"未知")}
function cocDifficultyTarget(target,difficulty="regular"){
  const value=clamp(Number(target||0),1,100);if(difficulty==="hard")return Math.floor(value/2);if(difficulty==="extreme")return Math.floor(value/5);return value
}
function cocRank(roll,target){
  const critical=roll===1;const fumble=target<50?roll>=96:roll===100;
  if(critical)return "critical";if(fumble)return "fumble";if(roll<=Math.floor(target/5))return "extreme";
  if(roll<=Math.floor(target/2))return "hard";if(roll<=target)return "regular";return "failure";
}
function cocDifficultyPass(rank,difficulty){
  const order={fumble:0,failure:0,regular:1,hard:2,extreme:3,critical:4};const need={regular:1,hard:2,extreme:3}[difficulty||"regular"]||1;return order[rank]>=need;
}
function clueDiscoveryQuality(record){
  if(!record||record.skipped)return "skipped";if(record.rank==="fumble")return "fumble";if(record.result!==true)return "failure";return ["critical","extreme","hard","regular"].includes(record.rank)?record.rank:"regular"
}
function validateCocRollOutcome(roll){
  if(!roll||roll.skipped)return true;const total=Number(roll.total),target=Number(roll.target),difficulty=roll.difficulty||"regular";
  const expectedRank=cocRank(total,target),expectedResult=cocDifficultyPass(expectedRank,difficulty),expectedTarget=cocDifficultyTarget(target,difficulty);
  if(roll.rank!==expectedRank||Boolean(roll.result)!==expectedResult||Number(roll.difficultyTarget)!==expectedTarget)throw new Error(`COC 判定不一致：骰点 ${total} / 技能 ${target} / 难度 ${difficulty}`);return true
}
function rollCocPercentile(check){
  const rawBonus=clamp(Number(check.bonusDice||0),0,2),rawPenalty=clamp(Number(check.penaltyDice||0),0,2);
  const net=rawBonus-rawPenalty,bonus=Math.max(0,net),penalty=Math.max(0,-net),extra=Math.max(bonus,penalty);
  const ones=randomInt(0,9);const tens=[];for(let i=0;i<1+extra;i++)tens.push(randomInt(0,9));
  const values=tens.map(t=>{const v=t*10+ones;return v===0?100:v});
  const selected=bonus?Math.min(...values):penalty?Math.max(...values):values[0],target=clamp(Number(check.target||0),1,100),difficulty=["regular","hard","extreme"].includes(check.difficulty)?check.difficulty:"regular";
  const rank=cocRank(selected,target),result=cocDifficultyPass(rank,difficulty),difficultyTarget=cocDifficultyTarget(target,difficulty),out={expression:"1d100",rawRolls:values,modifier:0,total:selected,target,difficulty,difficultyTarget,rank,result};validateCocRollOutcome(out);return out;
}
function rollDnd(check){
  const mode=check.advantage&&check.disadvantage?"normal":check.advantage?"advantage":check.disadvantage?"disadvantage":"normal";const rolls=[randomInt(1,20)];if(mode!=="normal")rolls.push(randomInt(1,20));
  const natural=mode==="advantage"?Math.max(...rolls):mode==="disadvantage"?Math.min(...rolls):rolls[0];const modifier=Number(check.modifier||0);const total=natural+modifier;const dc=Number(check.dc||10);
  return {expression:"1d20",rawRolls:rolls,modifier,total,natural,target:dc,mode,result:total>=dc,natural20:natural===20,natural1:natural===1};
}
function resolveCheck(check){
  if(check.system==="coc7")return rollCocPercentile(check);
  if(check.system==="dnd5e")return rollDnd(check);
  const rolled=rollDiceExpression(check.expression||"1d20");if(!rolled.ok)throw new Error(rolled.error);const target=Number(check.target||0);
  return {...rolled.value,target,result:check.compare==="lte"?rolled.value.total<=target:rolled.value.total>=target};
}
function rollSanLoss(loss,resultSuccess){
  const parts=String(loss||"0/1d6").split("/");const expr=(resultSuccess?parts[0]:parts[1])||"0";if(/^\d+$/.test(expr))return {amount:Number(expr),expression:expr,rawRolls:[]};
  const rolled=rollDiceExpression(expr);if(!rolled.ok)return {amount:0,expression:expr,rawRolls:[]};return {amount:rolled.value.total,expression:expr,rawRolls:rolled.value.rawRolls};
}

/* =========================
   状态、日志与状态机
========================= */

function rollDiceSum(count,faces){let total=0;for(let i=0;i<count;i++)total+=randomInt(1,faces);return total}
function rollCocLuck(){return rollDiceSum(3,6)*5}
function rollCocAttributeSet(){return{str:rollDiceSum(3,6)*5,con:rollDiceSum(3,6)*5,siz:(rollDiceSum(2,6)+6)*5,dex:rollDiceSum(3,6)*5,app:rollDiceSum(3,6)*5,int:(rollDiceSum(2,6)+6)*5,pow:rollDiceSum(3,6)*5,edu:(rollDiceSum(2,6)+6)*5,luck:rollCocLuck()}}
function cocDerived(attributes){return{maxHp:Math.floor((Number(attributes.con||0)+Number(attributes.siz||0))/10),initialSan:Number(attributes.pow||0)}}
function readCocAttributes(form){const out={};for(const key of COC_ATTRIBUTE_KEYS)out[key]=Number(form.get(key));return out}
function validateCocAttributes(attributes,method){for(const key of COC_ATTRIBUTE_KEYS){const value=Number(attributes[key]);if(!Number.isInteger(value))throw new Error(`${COC_ATTRIBUTE_LABELS[key]} 必须是整数`);const min=["siz","int","edu"].includes(key)?40:15;if(value<min||value>90)throw new Error(`${COC_ATTRIBUTE_LABELS[key]} 必须在 ${min} 到 90 之间`);if(method==="pointbuy480"&&value%5!==0)throw new Error("480 购点的属性必须是 5 的倍数")}const total=COC_ATTRIBUTE_KEYS.reduce((sum,key)=>sum+attributes[key],0);if(method==="pointbuy480"&&total!==480)throw new Error(`480 购点要求八项属性合计恰好为 480，当前为 ${total}`);return total}
function getCocSkillBase(skillId,attributes){const skill=COC_SKILL_MAP[skillId];if(!skill)return 0;if(skill.base==="half_dex")return Math.floor(Number(attributes.dex||0)/2);if(skill.base==="edu")return Number(attributes.edu||0);return Number(skill.base||0)}
function getOccupation(id){return COC_OCCUPATION_MAP[id]||COC_OCCUPATION_MAP.custom}
function calculateOccupationPoints(occupation,formulaId,attributes){const formula=occupation.formulas.find(item=>item.id===formulaId)||occupation.formulas[0];return formula.terms.reduce((sum,[key,multiplier])=>sum+Number(attributes[key]||0)*multiplier,0)}
function getOccupationSelectionsFromForm(form,occupation){const read=name=>typeof form?.get==="function"?form.get(name):form?.elements?.namedItem(name)?.value,selected=[];for(let groupIndex=0;groupIndex<(occupation.choiceGroups||[]).length;groupIndex++){const group=occupation.choiceGroups[groupIndex];for(let slot=0;slot<group.choose;slot++){const value=String(read(`occ_choice_${groupIndex}_${slot}`)||"");if(value)selected.push(value)}}for(let slot=0;slot<(occupation.freeSkills||0);slot++){const value=String(read(`occ_free_${slot}`)||"");if(value)selected.push(value)}return selected}
function getOccupationSkillIds(occupation,selections){return Array.from(new Set([...(occupation.fixedSkills||[]),...selections,"credit_rating"]))}
function skillName(id){return COC_SKILL_MAP[id]?.name||id}
function validateOccupationSelections(occupation,selections){const expected=(occupation.choiceGroups||[]).reduce((sum,g)=>sum+g.choose,0)+(occupation.freeSkills||0);if(selections.length!==expected)throw new Error(`职业技能尚未选择完整：需要选择 ${expected} 项`);const combined=[...(occupation.fixedSkills||[]),...selections];if(new Set(combined).size!==combined.length)throw new Error("职业技能不能与固定技能或其他已选技能重复");for(const id of selections)if(!COC_SKILL_MAP[id])throw new Error(`未知职业技能：${id}`)}
function readSkillAllocations(){const result=[];for(const row of $$('[data-skill-row]')){const id=row.dataset.skillRow,occupationPoints=Number(row.querySelector('[data-occupation-points]')?.value||0),interestPoints=Number(row.querySelector('[data-interest-points]')?.value||0);result.push({id,occupationPoints,interestPoints})}return result}
function buildCocCharacter(form){
  const creationMethod=form.get("creationMethod")==="pointbuy480"?"pointbuy480":"coc5",attributes=readCocAttributes(form),attributeTotal=validateCocAttributes(attributes,creationMethod),derived=cocDerived(attributes);
  const luck=Number(form.get("luck"));if(!Number.isInteger(luck)||luck<15||luck>90||luck%5!==0)throw new Error("LUCK 必须是 15～90 的 5 的倍数");
  const occupation=getOccupation(String(form.get("occupationId")||"custom")),formulaId=String(form.get("occupationFormulaId")||occupation.formulas[0].id),selections=getOccupationSelectionsFromForm(form,occupation);validateOccupationSelections(occupation,selections);
  const occupationSkillIds=getOccupationSkillIds(occupation,selections),occupationTotal=calculateOccupationPoints(occupation,formulaId,attributes),interestTotal=Number(attributes.int||0)*2,occupationCap=clamp(Number(state.config.occupationSkillCap||80),1,99),nonOccupationCap=clamp(Number(state.config.nonOccupationSkillCap||60),1,99),allocations=readSkillAllocations();
  let occupationSpent=0,interestSpent=0;const skills=[];
  for(const definition of COC_SKILL_DEFINITIONS){const allocation=allocations.find(item=>item.id===definition.id)||{occupationPoints:0,interestPoints:0},base=getCocSkillBase(definition.id,attributes),isOccupation=occupationSkillIds.includes(definition.id),op=Math.max(0,Math.floor(allocation.occupationPoints)),ip=Math.max(0,Math.floor(allocation.interestPoints));if(definition.locked&&(op||ip))throw new Error("克苏鲁神话在普通创角阶段不能投入技能点");if(!isOccupation&&op>0)throw new Error(`${definition.name} 不是职业技能，不能投入职业技能点`);const cap=Math.max(base,isOccupation?occupationCap:nonOccupationCap),value=base+op+ip;if(value>cap)throw new Error(`${definition.name} 最终值 ${value} 超过上限 ${cap}`);occupationSpent+=op;interestSpent+=ip;skills.push({id:definition.id,name:definition.name,base,occupationPoints:op,interestPoints:ip,value,occupationSkill:isOccupation,category:definition.category})}
  if(occupationSpent!==occupationTotal)throw new Error(`职业技能点必须分配完：已使用 ${occupationSpent} / ${occupationTotal}`);if(interestSpent!==interestTotal)throw new Error(`个人兴趣点必须分配完：已使用 ${interestSpent} / ${interestTotal}`);
  const credit=skills.find(item=>item.id==="credit_rating")?.value||0;if(credit<occupation.credit[0]||credit>occupation.credit[1])throw new Error(`信用评级必须处于 ${occupation.credit[0]}～${occupation.credit[1]}，当前为 ${credit}`);
  const hp=clamp(Number(form.get("current_hp")||derived.maxHp),0,derived.maxHp),san=clamp(Number(form.get("current_san")||derived.initialSan),0,99);
  return{system:"coc7",name:asString(form.get("name"),80)||"无名调查员",occupation:occupation.name,occupationId:occupation.id,occupationFormulaId:formulaId,occupationSkillIds,occupationChoices:selections,age:clamp(Number(form.get("age")||28),15,99),creationMethod,attributeTotal,hp,maxHp:derived.maxHp,san,maxSan:99,luck,attributes,skills,skillPointPools:{occupationTotal,occupationSpent,interestTotal,interestSpent,occupationCap,nonOccupationCap},background:asString(form.get("background"),2000)}
}
function abilityMod(score){return Math.floor((score-10)/2)}
function buildDndCharacter(form){const attrs={str:Number(form.get("str")||10),dex:Number(form.get("dex")||14),con:Number(form.get("con")||12),int:Number(form.get("int")||12),wis:Number(form.get("wis")||14),cha:Number(form.get("cha")||10)};for(const k of Object.keys(attrs))attrs[k]=clamp(attrs[k],1,30);const hp=clamp(Number(form.get("hp")||10),1,999);return{system:"dnd5e",name:asString(form.get("name"),80)||"无名冒险者",race:asString(form.get("race"),80)||"人类",className:asString(form.get("className"),80)||"游荡者",level:clamp(Number(form.get("level")||1),1,20),hp,maxHp:hp,ac:clamp(Number(form.get("ac")||14),1,40),proficiency:clamp(Number(form.get("proficiency")||2),2,10),attributes:attrs,mods:Object.fromEntries(Object.entries(attrs).map(([k,v])=>[k,abilityMod(v)])),saves:Object.fromEntries(Object.entries(attrs).map(([k,v])=>[k,abilityMod(v)])),skills:{perception:abilityMod(attrs.wis),investigation:abilityMod(attrs.int),stealth:abilityMod(attrs.dex)},background:asString(form.get("background"),2000)}}
function buildCustomCharacter(form){const hp=clamp(Number(form.get("hp")||10),0,9999);return{system:"custom",name:asString(form.get("name"),80)||"角色",hp,maxHp:hp,notes:asString(form.get("background"),3000),skills:[]}}

/* =========================
   剧本解析与校验
========================= */
