"use strict";

/* v1.6.0 CoC Resolution Engine
 * The browser owns check targets, randomness, pass/fail and mechanical outcome budgets.
 * AI may request a check and narrate the resulting contract; it cannot rewrite the contract.
 */
const COC_RESOLUTION_ENGINE_VERSION="1.0";
const COC_RESOLUTION_AUTHORITY="browser_coc_resolution";
const COC_RESOLUTION_DIFFICULTIES=new Set(["regular","hard","extreme"]);
const COC_RESOLUTION_RANKS=new Set(["critical","extreme","hard","regular","failure","fumble","skipped"]);

function cocResolutionInsightLimit(quality){
  if(quality==="hard")return 1;
  if(quality==="extreme"||quality==="critical")return 2;
  return 0
}
function cocResolutionContractSnapshot(check,{reconstructed=false}={}){
  if(!check||check.system!=="coc7")return null;
  const difficulty=COC_RESOLUTION_DIFFICULTIES.has(check.difficulty)?check.difficulty:"regular",target=clamp(Number(check.target||0),1,100);
  return{
    version:COC_RESOLUTION_ENGINE_VERSION,
    authority:COC_RESOLUTION_AUTHORITY,
    contractId:asString(check.resolutionContract?.contractId||check.resolutionContractId,120)||uid("coc-contract"),
    reconstructed:Boolean(reconstructed),
    system:"coc7",
    type:asString(check.type,40)||"skill",
    skillId:asString(check.skillId,80)||null,
    label:asString(check.label,80)||"检定",
    purpose:asString(check.purpose||check.reason,500),
    target,
    difficulty,
    difficultyTarget:cocDifficultyTarget(target,difficulty),
    bonusDice:clamp(Number(check.bonusDice||0),0,2),
    penaltyDice:clamp(Number(check.penaltyDice||0),0,2),
    mandatory:Boolean(check.mandatory),
    visibility:check.visibility==="secret"?"secret":"public",
    sourceNodeId:asString(check.sourceNodeId,120)||state?.campaign?.currentNodeId||null,
    sourceCheckId:asString(check.sourceCheckId,120)||null,
    origin:asString(check.origin,40)||"ai",
    createdAt:nowIso()
  }
}
function cocResolutionContractMatches(check,contract){
  if(!check||check.system!=="coc7"||!isPlainObject(contract))return false;
  const expected=cocResolutionContractSnapshot({...check,resolutionContract:{contractId:contract.contractId}},{reconstructed:Boolean(contract.reconstructed)});
  for(const key of ["system","type","skillId","target","difficulty","difficultyTarget","bonusDice","penaltyDice","mandatory","visibility","sourceNodeId"]){
    if(JSON.stringify(expected[key]??null)!==JSON.stringify(contract[key]??null))return false
  }
  return contract.version===COC_RESOLUTION_ENGINE_VERSION&&contract.authority===COC_RESOLUTION_AUTHORITY
}
function assertCocResolutionContract(check){
  if(!check||check.system!=="coc7")return true;
  if(!isPlainObject(check.resolutionContract))throw protocolError("COC_CHECK_CONTRACT_MISSING","COC 检定缺少浏览器 Check Contract");
  if(!cocResolutionContractMatches(check,check.resolutionContract))throw protocolError("COC_CHECK_CONTRACT_MISMATCH","COC 检定参数与已锁定 Check Contract 不一致",{contractId:check.resolutionContract.contractId||null});
  return true
}
function cocOutcomeContractFromRecord(record,{reconstructed=false}={}){
  if(!record||record.system!=="coc7")return null;
  const skipped=Boolean(record.skipped),difficulty=COC_RESOLUTION_DIFFICULTIES.has(record.difficulty)?record.difficulty:"regular",target=clamp(Number(record.target||0),1,100);
  if(!skipped)validateCocRollOutcome(record);
  const rank=skipped?"skipped":record.rank,passed=!skipped&&record.result===true,quality=skipped?"skipped":clueDiscoveryQuality(record);
  if(!COC_RESOLUTION_RANKS.has(rank))throw protocolError("COC_OUTCOME_RANK_INVALID","COC Outcome Contract 收到未知成功等级",{rank});
  const extraInsightLimit=cocResolutionInsightLimit(quality);
  return{
    version:COC_RESOLUTION_ENGINE_VERSION,
    authority:COC_RESOLUTION_AUTHORITY,
    contractId:asString(record.resolutionContract?.contractId,120)||null,
    recordId:asString(record.id,120)||null,
    reconstructed:Boolean(reconstructed),
    immutable:true,
    system:"coc7",
    target,
    difficulty,
    difficultyTarget:cocDifficultyTarget(target,difficulty),
    roll:skipped?null:Number(record.total),
    rank,
    rankLabel:cocRankLabel(rank),
    passed,
    quality,
    mechanicalResult:skipped?"skipped":passed?"success":"failure",
    narrativeBudget:{
      mayDescribeCheckAsPassed:passed,
      mayDescribeCheckAsFailed:!passed&&!skipped,
      coreClueOnSuccess:passed,
      extraInsightLimit,
      limitedAdvantageAllowed:quality==="critical"
    },
    generatedAt:nowIso()
  }
}
function validateCocOutcomeContract(record){
  if(!record||record.system!=="coc7")return true;
  const contract=record.outcomeContract;if(!isPlainObject(contract))throw protocolError("COC_OUTCOME_CONTRACT_MISSING","COC 检定记录缺少浏览器 Outcome Contract");
  const expected=cocOutcomeContractFromRecord({...record,outcomeContract:null},{reconstructed:Boolean(contract.reconstructed)});
  for(const key of ["authority","system","target","difficulty","difficultyTarget","roll","rank","passed","quality","mechanicalResult"]){
    if(JSON.stringify(expected[key]??null)!==JSON.stringify(contract[key]??null))throw protocolError("COC_OUTCOME_CONTRACT_MISMATCH","COC 检定结果与已锁定 Outcome Contract 不一致",{recordId:record.id||null,key,expected:expected[key]??null,actual:contract[key]??null})
  }
  if(Number(contract?.narrativeBudget?.extraInsightLimit)!==Number(expected.narrativeBudget.extraInsightLimit)||Boolean(contract?.narrativeBudget?.limitedAdvantageAllowed)!==Boolean(expected.narrativeBudget.limitedAdvantageAllowed))throw protocolError("COC_OUTCOME_CONTRACT_MISMATCH","COC 叙事预算与浏览器结果不一致",{recordId:record.id||null});
  return true
}
function ensureCocOutcomeContract(record){
  if(!record||record.system!=="coc7")return record;
  if(!isPlainObject(record.resolutionContract))record.resolutionContract=cocResolutionContractSnapshot({system:"coc7",type:record.type,skillId:record.skillId,label:record.label,purpose:record.purpose||record.reason,target:record.target,difficulty:record.difficulty,bonusDice:0,penaltyDice:0,mandatory:record.mandatory,visibility:record.visibility,sourceNodeId:record.sourceNodeId,sourceCheckId:record.sourceCheckId,origin:record.origin},{reconstructed:true});
  if(!isPlainObject(record.outcomeContract))record.outcomeContract=cocOutcomeContractFromRecord(record,{reconstructed:true});
  validateCocOutcomeContract(record);return record
}
function cocResolutionContext(){
  return{
    version:COC_RESOLUTION_ENGINE_VERSION,
    authority:COC_RESOLUTION_AUTHORITY,
    targetAuthority:"browser_character_sheet",
    randomnessAuthority:"browser_crypto",
    outcomeAuthority:"browser_check_record",
    aiAuthority:"request_check_and_narrate_only",
    rules:{difficulty:["regular","hard","extreme"],critical:"roll_1",fumble:"96-100 when target < 50; otherwise 100",bonusPenaltyDice:"browser_locked"}
  }
}

/* Lock the browser-maintained target/difficulty immediately after legacy check normalization. */
const __cocResolutionNormalizeCheck=normalizeCheck;
normalizeCheck=function(check){
  const normalized=__cocResolutionNormalizeCheck(check);
  if(normalized?.system==="coc7"&&normalized.required){
    normalized.resolutionContract=cocResolutionContractSnapshot(normalized)
  }
  return normalized
};

/* A pending CoC check cannot be edited after its contract has been created. */
const __cocResolutionResolveCheck=resolveCheck;
resolveCheck=function(check){if(check?.system==="coc7")assertCocResolutionContract(check);return __cocResolutionResolveCheck(check)};

/* Every browser roll produces an immutable Outcome Contract stored with the check record. */
const __cocResolutionMakeCheckRecord=makeCheckRecord;
makeCheckRecord=function(check,roll,skipped=false){
  const record=__cocResolutionMakeCheckRecord(check,roll,skipped);
  if(record?.system==="coc7"){
    record.skillId=asString(check?.skillId,80)||null;
    record.resolutionContract=deepClone(check?.resolutionContract||cocResolutionContractSnapshot(check,{reconstructed:true}));
    record.outcomeContract=cocOutcomeContractFromRecord(record);
    validateCocOutcomeContract(record)
  }
  return record
};

/* Continuation guidance consumes the stored browser contract instead of trusting model interpretation. */
const __cocResolutionCheckOutcomeGuidance=checkOutcomeGuidance;
checkOutcomeGuidance=function(record){
  const base=__cocResolutionCheckOutcomeGuidance(record);
  if(record?.system!=="coc7")return base;
  ensureCocOutcomeContract(record);const outcome=record.outcomeContract;
  return{
    ...base,
    authority:COC_RESOLUTION_AUTHORITY,
    resolutionVersion:COC_RESOLUTION_ENGINE_VERSION,
    immutableOutcome:{recordId:record.id,contractId:outcome.contractId,roll:outcome.roll,target:outcome.target,difficulty:outcome.difficulty,difficultyTarget:outcome.difficultyTarget,rank:outcome.rank,rankLabel:outcome.rankLabel,passed:outcome.passed,quality:outcome.quality,mechanicalResult:outcome.mechanicalResult},
    narrativeBudget:deepClone(outcome.narrativeBudget),
    prohibited:["do_not_change_roll","do_not_change_target","do_not_flip_pass_fail","do_not_exceed_extra_insight_limit"]
  }
};

/* Old Schema 8 saves are lazily upgraded in memory; no schema migration is required. */
if(typeof sanitizeRuntimeAfterLoad==="function"){
  const __cocResolutionSanitizeRuntimeAfterLoad=sanitizeRuntimeAfterLoad;
  sanitizeRuntimeAfterLoad=function(){const result=__cocResolutionSanitizeRuntimeAfterLoad();for(const record of state.checkRecords||[])if(record?.system==="coc7")ensureCocOutcomeContract(record);return result}
}

/* Expose explicit browser authority in every AI context without adding requests or changing protocol 1.3. */
const __cocResolutionBuildRequestPayload=buildRequestPayload;
buildRequestPayload=function(stage,requestId,baseRevision,extra={}){const payload=__cocResolutionBuildRequestPayload(stage,requestId,baseRevision,extra);payload.cocResolutionEngine=cocResolutionContext();return payload};
const __cocResolutionBuildSystemPrompt=buildSystemPrompt;
buildSystemPrompt=function(){return `${__cocResolutionBuildSystemPrompt()}\n23. CoC Resolution Engine：check target、difficulty、bonus/penalty dice、骰点、成功等级和 passed 均由浏览器 Check/Outcome Contract 决定。续写只能解释 immutableOutcome；不得把失败叙述成通过，也不得把通过叙述成失败，不得超出 narrativeBudget.extraInsightLimit。`};
const __cocResolutionBuildUserPrompt=buildUserPrompt;
buildUserPrompt=function(payload){return `${__cocResolutionBuildUserPrompt(payload)}\nCoC 机械裁决权威：${JSON.stringify(payload.cocResolutionEngine||cocResolutionContext())}`};

if(typeof buildDiagnosticPackage==="function"){
  const __cocResolutionBuildDiagnosticPackage=buildDiagnosticPackage;
  buildDiagnosticPackage=function(options={}){
    const pack=__cocResolutionBuildDiagnosticPackage(options),includeSecrets=Boolean(options?.includeSecrets),records=(state.checkRecords||[]).filter(record=>record?.system==="coc7"&&(includeSecrets||record.visibility!=="secret")).slice(-12);
    pack.cocResolutionEngine={...cocResolutionContext(),recentOutcomes:records.map(record=>{ensureCocOutcomeContract(record);return deepClone(record.outcomeContract)})};return pack
  }
}
