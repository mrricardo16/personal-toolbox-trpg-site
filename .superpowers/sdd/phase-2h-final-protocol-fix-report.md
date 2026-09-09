# Phase 2H 最终协议修复报告

## 结果

- 三条公开战斗意图路由在读取请求体前先验证 bearer session 与房间归属。
- 所有早期拒绝均返回只含固定 `code` 的结构化 JSON：`invalid_session`、`not_member`、`invalid_intent` 或 `invalid_response`。
- `CombatDamageCommitInvariantException`、`CombatDamageStateInvariantException` 和专用 `PlayerCombatIntentInvariantException` 均在单一外层房间 mutation gate 内记录服务端错误，并返回固定 500 `combat_consistency_failure`；响应不含 revision、异常文本、堆栈或内部类型。
- 协调器原有四个显式 `InvalidOperationException` 结果契约守卫改为专用窄异常；未捕获通用 `InvalidOperationException` 或 `Exception`，未增加回滚、重试、骰子、状态转换或发布逻辑。

## TDD 证据

1. 首轮 RED：
   - 命令：`dotnet test multiplayer/server/Trpg.Multiplayer.slnx --no-restore --filter "FullyQualifiedName~GameApiTests.CombatIntentRoutes" --nologo -v:minimal`
   - 结果：19 项；9 失败、10 通过。
   - 原因：早期拒绝为空响应体；未认证畸形 JSON 在认证前返回 400；状态 invariant 异常未被安全映射；专用协调器 invariant 异常类型尚不存在。
2. 协调器 RED：
   - 命令：`dotnet test multiplayer/server/Trpg.Multiplayer.slnx --no-restore --filter "FullyQualifiedName~PlayerCombatIntentCoordinatorTests.CommittedBeginAndResolveResultContractFailures" --nologo -v:minimal`
   - 结果：2 项；2 失败、0 通过。
   - 原因：Committed Begin/Resolve 缺失战斗状态时实际抛出 `InvalidOperationException`，期望专用 invariant 类型。
3. 首轮 GREEN：
   - 要求的组合筛选首次通过 59/59。
4. 第二轮 RED：
   - 命令：`dotnet test multiplayer/server/Trpg.Multiplayer.slnx --no-restore --filter "FullyQualifiedName~GameApiTests.CombatIntentRoutes_ReturnedConsistencyFailureSuppressesRevisionAndIsNotRetried" --nologo -v:minimal`
   - 结果：1 项；1 失败、0 通过。
   - 原因：返回型 `CombatConsistencyFailure` 仍包含 `currentGameRevision: 23`。
5. 最终 GREEN：
   - 命令：`dotnet test multiplayer/server/Trpg.Multiplayer.slnx --no-restore --filter "FullyQualifiedName~GameApiTests.CombatIntentRoutes|FullyQualifiedName~GameApiTests.GameEndpoints|FullyQualifiedName~PlayerCombatIntentCoordinatorTests" --nologo -v:minimal`
   - 结果：60 项；60 通过、0 失败、0 跳过。

## 修改记录

- 修改日期：2026-09-08
- 修改位置：`GameApi` 战斗意图 handlers、请求读取与错误边界；`GameContracts` invariant 类型；`PlayerCombatIntentCoordinator` 结果契约守卫；对应 API 与协调器测试。
- 修改原因：落实 Phase 2H 最终审查中的统一安全错误协议，并阻止内部 invariant 细节泄露或被错误地视为可重试冲突。
- 业务影响：仅改变公开战斗路由的拒绝响应形状与指定 invariant 的安全 500 映射；成功路径、路由数量、bearer 权威、mutation gate、协调器调用次数和战斗状态转换架构不变。
- 性能影响：请求 JSON 在认证与房间归属验证后才解析，未认证请求减少无效解析；无重试或额外状态读取。
- 数据库、Redis、MQ、PLC、第三方接口影响：无。
- 剩余风险：未进行真实网络部署验证；覆盖基于 ASP.NET Core 测试宿主和协调器 focused tests。

## 最终审查补充修复

- 三条公开战斗意图 handler 在 bearer/session 与房间归属检查之后、JSON 反序列化之前统一执行 `HasJsonContentType()` 门禁。`text/plain` 或无 Content-Type 即使载荷是合法 JSON，也返回精确单字段 400 `invalid_intent`，且不调用协调器。
- 新增两个协调器行为测试，分别覆盖成功 Begin 结果缺少 `PendingExchange`、NPC 防御者缺少响应策略；两者均精确断言 `PlayerCombatIntentInvariantException`，并确认未调用后续 Resolve/Damage。

### 补充 TDD 证据

1. RED：
   - 命令：`dotnet test multiplayer/server/Trpg.Multiplayer.slnx --no-restore --filter "FullyQualifiedName~CombatIntentRoutes_RejectNonJsonMediaTypesAfterAuthorityChecks|FullyQualifiedName~CommittedBeginWithoutPendingExchange_ThrowsDedicatedInvariantException|FullyQualifiedName~CommittedBeginWithNpcMissingResponsePolicy_ThrowsDedicatedInvariantException" --nologo -v:minimal`
   - 结果：3 项；1 失败、2 通过、0 跳过。
   - 失败原因：`text/plain` 合法 JSON 被手工反序列化并进入协调器，实际返回 200，期望精确 400 `invalid_intent`。两个新增协调器分支测试直接通过，证明此前窄异常转换已覆盖这些分支。
2. focused GREEN：同一命令通过 3/3。
3. 扩展最终 GREEN：
   - 命令：`dotnet test multiplayer/server/Trpg.Multiplayer.slnx --no-restore --filter "FullyQualifiedName~GameApiTests.CombatIntentRoutes|FullyQualifiedName~GameApiTests.GameEndpoints|FullyQualifiedName~PlayerCombatIntentCoordinatorTests" --nologo -v:minimal`
   - 结果：63 项；63 通过、0 失败、0 跳过。

### 补充修改影响

- 业务影响：恢复原 typed binder 的 JSON 媒体类型约束，同时继续保证认证与房间权限判断先于正文/媒体类型判断。
- 性能影响：非 JSON 请求在反序列化前被拒绝，无额外协调器或状态访问。
- 架构影响：无；外层 mutation gate、协调器恰好一次调用、骰子、状态提交与发布流程均未改变。
