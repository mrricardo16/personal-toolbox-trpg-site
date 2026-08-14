/* v1.5.8 API Response Resilience：API/JSON 失败可以回滚或降级，但不能伪造游戏结果。 */
const API_RESPONSE_RESILIENCE_VERSION="1.0";
const API_RESPONSE_RESILIENCE_MAX_ATTEMPTS=3;
const API_RESPONSE_RETRYABLE_CODES=new Set([
  "AI_PROVIDER_EMPTY_CONTENT",
  "AI_PROVIDER_TIMEOUT",
  "AI_PROVIDER_NETWORK_ERROR",
  "AI_PROVIDER_RESPONSE_INVALID",
  "AI_RESPONSE_JSON_PARSE_FAILED"
]);
const API_RESPONSE_GRACEFUL_CODES=new Set([
  "AI_PROVIDER_EMPTY_CONTENT",
  "AI_PROVIDER_TIMEOUT",
  "AI_PROVIDER_NETWORK_ERROR",
  "AI_PROVIDER_RESPONSE_INVALID",
  "AI_PROVIDER_HTTP_ERROR",
  "AI_RESPONSE_JSON_PARSE_FAILED"
]);
let __apiResilienceLastLocationProviderError=null;

function apiReliabilityState(){
  const base={structuredRequests:0,apiAttempts:0,automaticRetries:0,retryExhausted:0,providerEmpty:0,providerTimeout:0,providerNetwork:0,providerHttp:0,providerInvalidResponse:0,jsonInvalid:0,gracefulFallbacks:0,hardFailures:0,lastFailureCode:null,lastFailureAt:null};
  if(!isPlainObject(state.runtime.apiReliability))state.runtime.apiReliability=base;
  else for(const [key,value] of Object.entries(base))if(state.runtime.apiReliability[key]===undefined)state.runtime.apiReliability[key]=value;
  return state.runtime.apiReliability
}
function apiReliabilityMark(code,field=null){const d=apiReliabilityState();if(field)d[field]=Number(d[field]||0)+1;d.lastFailureCode=code||null;d.lastFailureAt=nowIso();return d}
function apiHttpStatusFromMessage(message){const text=String(message||"");const direct=text.match(/API 返回\s+(\d{3})/);if(direct)return Number(direct[1]);if(/API Key 无效|未授权/.test(text))return 401;if(/API 无权限/.test(text))return 403;if(/API 地址或模型路径错误/.test(text))return 404;if(/请求频率过高|额度不足/.test(text))return 429;return null}
function classifyApiTransportError(error){
  if(error?.code)return error;
  const message=String(error?.message||"未知 API 错误");
  if(/请求已取消或超时/.test(message)){
    if(!state.runtime.activeRequestId)return protocolError("AI_REQUEST_CANCELLED","请求已取消");
    return protocolError("AI_PROVIDER_TIMEOUT","AI 服务响应超时",{retryable:true})
  }
  if(/网络请求失败|CORS|网络不可达/.test(message))return protocolError("AI_PROVIDER_NETWORK_ERROR",message,{retryable:true});
  const status=apiHttpStatusFromMessage(message);if(status!==null)return protocolError("AI_PROVIDER_HTTP_ERROR",message,{status,retryable:status===408||status===409||status===425||status===429||status>=500});
  if(/API 返回体不是有效 JSON|返回格式不是兼容的 Chat Completions 结构/.test(message))return protocolError("AI_PROVIDER_RESPONSE_INVALID",message,{retryable:true});
  return error
}
function normalizeStructuredAttemptError(error,raw){
  const normalized=classifyApiTransportError(error);if(normalized?.code==="AI_RESPONSE_JSON_PARSE_FAILED"&&!String(raw||"").trim())return withRawResponse(protocolError("AI_PROVIDER_EMPTY_CONTENT","AI 服务返回空 final content",{retryable:true}),"",normalized.stage);return normalized
}
function apiResponseRetryable(error){if(error?.code==="AI_PROVIDER_HTTP_ERROR")return error?.details?.retryable===true;return API_RESPONSE_RETRYABLE_CODES.has(error?.code)}
function apiResponseGraceful(error){return API_RESPONSE_GRACEFUL_CODES.has(error?.code)}
function markAttemptFailure(error){const code=error?.code;if(code==="AI_PROVIDER_EMPTY_CONTENT")apiReliabilityMark(code,"providerEmpty");else if(code==="AI_PROVIDER_TIMEOUT")apiReliabilityMark(code,"providerTimeout");else if(code==="AI_PROVIDER_NETWORK_ERROR")apiReliabilityMark(code,"providerNetwork");else if(code==="AI_PROVIDER_HTTP_ERROR")apiReliabilityMark(code,"providerHttp");else if(code==="AI_PROVIDER_RESPONSE_INVALID")apiReliabilityMark(code,"providerInvalidResponse");else if(code==="AI_RESPONSE_JSON_PARSE_FAILED")apiReliabilityMark(code,"jsonInvalid")}
function structuredRetryMessages(original,error,raw){
  const base=deepClone(original);if(error?.code!=="AI_RESPONSE_JSON_PARSE_FAILED"||!String(raw||"").trim())return base;
  return [...base,{role:"assistant",content:asString(raw,4000)},{role:"user",content:"上一次输出无法安全解析。不要解释、不要使用 Markdown，只重新返回与原请求完全同义且字段完整的单个 JSON 对象。不得新增任何游戏结果。"}]
}

const __apiResilienceCallChatCompletion=callChatCompletion;
callChatCompletion=async function(messages,options={}){
  try{const content=await __apiResilienceCallChatCompletion(messages,options);if(typeof content==="string"&&!content.trim())throw protocolError("AI_PROVIDER_EMPTY_CONTENT","AI 服务返回空 final content",{retryable:true});return content}catch(error){throw classifyApiTransportError(error)}
};

const __apiResilienceRequestStructuredAiJson=requestStructuredAiJson;
requestStructuredAiJson=async function(messages,meta,chatOptions={}){
  const originalMessages=deepClone(messages),diag=apiReliabilityState();diag.structuredRequests=Number(diag.structuredRequests||0)+1;let raw="",attemptMessages=originalMessages,lastError=null;
  for(let attempt=1;attempt<=API_RESPONSE_RESILIENCE_MAX_ATTEMPTS;attempt++){
    diag.apiAttempts=Number(diag.apiAttempts||0)+1;
    try{
      raw=await callChatCompletion(attemptMessages,chatOptions);
      if(!String(raw||"").trim())throw protocolError("AI_PROVIDER_EMPTY_CONTENT","AI 服务返回空 final content",{retryable:true});
      const parsed=await parseAndRepairAiResponse(raw,meta);return{raw,parsed,autoRetried:attempt>1,retryCount:attempt-1,resilienceVersion:API_RESPONSE_RESILIENCE_VERSION}
    }catch(caught){
      const error=normalizeStructuredAttemptError(caught,raw);lastError=error;markAttemptFailure(error);
      if(!apiResponseRetryable(error)||attempt>=API_RESPONSE_RESILIENCE_MAX_ATTEMPTS){if(apiResponseRetryable(error))diag.retryExhausted=Number(diag.retryExhausted||0)+1;throw withRawResponse(error,raw||error?.rawResponse||"",meta.stage)}
      if(state.runtime.activeRequestId!==meta.requestId)throw protocolError("STALE_RESPONSE","失败响应返回后请求已失效，不再自动重试");
      diag.automaticRetries=Number(diag.automaticRetries||0)+1;addLog("api_retry",`结构化 AI 请求第 ${attempt} 次失败（${error.code}），未应用任何结果；自动重试`,{requestId:meta.requestId});attemptMessages=structuredRetryMessages(originalMessages,error,raw);raw=""
    }
  }
  throw withRawResponse(lastError||protocolError("AI_PROVIDER_RESPONSE_INVALID","结构化 AI 请求未返回可用响应"),raw,meta.stage)
};

const __apiResilienceRepairLocationProtocol=repairLocationProtocol;
repairLocationProtocol=async function(...args){__apiResilienceLastLocationProviderError=null;try{return await __apiResilienceRepairLocationProtocol(...args)}catch(error){const normalized=normalizeStructuredAttemptError(error,error?.rawResponse||"");if(apiResponseGraceful(normalized))__apiResilienceLastLocationProviderError=normalized;throw normalized}};
const __apiResiliencePrepareAiTransactionWithLocationRepair=prepareAiTransactionWithLocationRepair;
prepareAiTransactionWithLocationRepair=async function(...args){__apiResilienceLastLocationProviderError=null;try{return await __apiResiliencePrepareAiTransactionWithLocationRepair(...args)}catch(error){if(__apiResilienceLastLocationProviderError){const provider=__apiResilienceLastLocationProviderError;__apiResilienceLastLocationProviderError=null;throw provider}throw error}finally{if(!__apiResilienceLastLocationProviderError)__apiResilienceLastLocationProviderError=null}};

function apiRecoveryMessage(kind,error){
  const code=error?.code||"AI_PROVIDER_RESPONSE_INVALID";
  if(kind==="continuation")return `【API 安全恢复】检定结果已保留，但主持续写未完成（${code}）。本次失败没有应用任何额外线索、物品、地点、剧情或结局变化。你可以沿用原骰点重试续写，也可以继续采取新的行动。`;
  return `【API 安全恢复】主持响应未完成（${code}）。本轮游戏状态没有产生变化，原行动已放回输入框；你可以修改后重发或直接再次发送。`
}
function recoverApiRequestFailure({kind,action="",playerMessageId=null,recordId=null,requestId,stage,rawResponse="",error}){
  const normalized=normalizeStructuredAttemptError(error,rawResponse||error?.rawResponse||""),reliability=deepClone(apiReliabilityState()),failed={kind,action:asString(action,4000),playerMessageId,recordId,requestId,stage,errorCode:normalized?.code||"AI_PROVIDER_RESPONSE_INVALID",errorMessage:redactSecrets(normalized?.message||"AI 服务响应失败"),errorDetails:deepClone(normalized?.details||{}),rawResponse:asString(rawResponse||normalized?.rawResponse,24000),at:nowIso(),recoverable:true,resilienceVersion:API_RESPONSE_RESILIENCE_VERSION};
  if(kind==="player_action"){
    const restored=restoreRequestRollback();state.runtime.apiReliability=reliability;if(!restored){state.runtime.activeRequestId=null;state.runtime.pendingPlayerAction=""}state.ui.actionDraft=failed.action;failed.playerMessageId=null
  }else{state.runtime.activeRequestId=null;state.runtime.currentResolutionRecordId=null}
  const diag=apiReliabilityState();diag.gracefulFallbacks=Number(diag.gracefulFallbacks||0)+1;diag.lastFailureCode=failed.errorCode;diag.lastFailureAt=failed.at;state.runtime.failedRequest=failed;state.runtime.lastError=null;state.runtime.lastRawAiResponse=failed.rawResponse;if(kind==="player_action")state.runtime.pendingPlayerAction="";setPhase("awaiting_player_action",{force:true});state.messages.push({id:uid("msg"),role:"system",content:apiRecoveryMessage(kind,normalized),time:nowIso(),requestId:requestId||null,kind:"providerRecovery"});if(state.messages.length>1200)state.messages.splice(0,state.messages.length-1200);bumpRevision();addLog("api_recovery",`${kind==="continuation"?"检定续写":"玩家行动"}已从 ${failed.errorCode} 安全恢复；未提交失败响应`,{requestId});renderAll();toast("API 响应异常，本轮已安全恢复","warn",6000);return failed
}

const __apiResilienceRecordRequestFailure=recordRequestFailure;
recordRequestFailure=function(payload){const error=normalizeStructuredAttemptError(payload?.error,payload?.rawResponse||payload?.error?.rawResponse||"");if(apiResponseGraceful(error))return recoverApiRequestFailure({...payload,error});const diag=apiReliabilityState();diag.hardFailures=Number(diag.hardFailures||0)+1;diag.lastFailureCode=error?.code||null;diag.lastFailureAt=nowIso();return __apiResilienceRecordRequestFailure({...payload,error})};

const __apiResilienceFailureTitle=failureTitle;
failureTitle=function(failed){const code=failed?.errorCode||"";const map={AI_PROVIDER_EMPTY_CONTENT:"AI 服务返回空响应",AI_PROVIDER_TIMEOUT:"AI 服务响应超时",AI_PROVIDER_NETWORK_ERROR:"AI 服务网络异常",AI_PROVIDER_HTTP_ERROR:"AI 服务 HTTP 异常",AI_PROVIDER_RESPONSE_INVALID:"AI 服务返回格式异常"};return map[code]||__apiResilienceFailureTitle(failed)};

function dismissApiRecovery(){const failed=state.runtime.failedRequest;if(!failed?.recoverable)return;if(failed.kind==="continuation"){state.runtime.lastContinuationPayload=null;state.runtime.pendingPlayerAction=""}state.runtime.failedRequest=null;state.runtime.lastRawAiResponse=null;addLog("api_recovery","玩家关闭 API 安全恢复提示并继续游戏");scheduleAutosave();renderAll()}
const __apiResilienceRenderChatLog=renderChatLog;
renderChatLog=function(options={}){
  __apiResilienceRenderChatLog(options);const failed=state.runtime.phase==="awaiting_player_action"&&state.runtime.failedRequest?.recoverable?state.runtime.failedRequest:null,log=$("#chatLog");if(!failed||!log)return;const continuation=failed.kind==="continuation",debug=Boolean(state.config.kpDebug),raw=asString(failed.rawResponse,24000);log.insertAdjacentHTML("beforeend",`<div class="proposal-card recovery-card"><h3>API 安全恢复 · 游戏仍可继续</h3><p>${escapeHtml(continuation?"刚才的检定结果已经保留，但 AI 续写没有完成；失败响应没有提交任何额外游戏状态。":"刚才的 AI 请求没有完成，失败回合已回滚；原行动已经放回输入框。")}</p><div class="row" style="flex-wrap:wrap"><button id="apiRecoveryRetryBtn" class="btn primary" type="button">${continuation?"沿用原骰点重试续写":"重新发送原行动"}</button><button id="apiRecoveryDismissBtn" class="btn" type="button">${continuation?"忽略续写并继续":"关闭提示"}</button></div>${debug?`<details><summary>技术详情 · ${escapeHtml(failed.errorCode||"")}</summary>${raw?`<pre class="raw-response">${escapeHtml(raw)}</pre>`:""}</details>`:""}</div>`);const retry=$("#apiRecoveryRetryBtn");if(retry)retry.onclick=()=>{const task=continuation?retryContinuation():submitPlayerAction(failed.action);Promise.resolve(task).catch(error=>toast(error.message,"danger",7000))};const dismiss=$("#apiRecoveryDismissBtn");if(dismiss)dismiss.onclick=dismissApiRecovery;if(options?.scrollToBottom!==false)log.scrollTop=log.scrollHeight
};
