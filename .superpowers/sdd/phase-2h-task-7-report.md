# Phase 2H Task 7 实施报告

## 范围

- 仅新增玩家近战攻击、响应与过回合三个公开 `POST` 路由。
- `PlayerId` 仅取自 bearer session；请求 DTO 不包含玩家、所有者、请求者或房主权限字段。
- 每个处理器只负责认证、房间匹配、请求校验、单次外层 `RoomMutationDeliveryGate` 与 `IPlayerCombatIntentCoordinator` 调用。
- 未新增客户端或实时层改动；战斗开始、结束、伤害、伤害结算、NPC、射击与武器路由继续返回 404。

## UTF-8 预检

修改前使用抛出式严格 UTF-8 解码检查：

- `multiplayer/server/src/Trpg.Multiplayer.Api/GameApi.cs`: `UTF8_OK`
- `multiplayer/server/src/Trpg.Multiplayer.Api/Program.cs`: `UTF8_OK`
- `multiplayer/server/tests/Trpg.Multiplayer.Api.Tests/Gameplay/GameApiTests.cs`: `UTF8_OK`
- 本报告为新文件。

## TDD 证据

### RED

命令：

```powershell
dotnet test multiplayer/server/Trpg.Multiplayer.slnx --no-restore --filter "FullyQualifiedName~GameApiTests.CombatIntentRoutes" --nologo -v:minimal
```

结果：编译成功；共 9 个测试，8 个按预期失败、1 个通过。失败均为三条待实现路由未映射导致的路由枚举为空或 HTTP 404；禁止路由保持 404 的测试通过。

### GREEN

命令：

```powershell
dotnet test multiplayer/server/Trpg.Multiplayer.slnx --no-restore --filter "FullyQualifiedName~GameApiTests.CombatIntentRoutes|FullyQualifiedName~GameApiTests.GameEndpoints|FullyQualifiedName~GameApiTests.InitializeAndGet" --nologo -v:minimal
```

结果：共 11 个测试，11 个通过，0 个失败，0 个跳过。

覆盖内容：精确三条 POST 路由、bearer/session 与跨房间拒绝、不可伪造请求体、非法标识/响应/revision、结构化安全错误映射、陈旧 revision、最终查看者投影、房主无 NPC 超级权限，以及既有 initialize/get 认证回归。

## 实现说明

- 三个成功响应直接返回应用协调器提供的最新 `GameSnapshot`。
- 应用错误映射为固定 snake-case code 与指定 HTTP 状态；只序列化 `code` 和可用时的 `currentGameRevision`，不序列化异常、内部枚举名称、状态或策略。
- `Program.cs` 已由前置任务注册 `IPlayerCombatIntentCoordinator` 并调用 `MapGameEndpoints()`，因此 Task 7 无需为制造差异而改写该文件。

## 修改记录

- 修改时间：2026-09-08 17:51:52
- 修改位置：`GameApi.MapGameEndpoints` 与三个战斗意图处理器。
- 修改内容：增加三个玩家战斗意图 HTTP 入口、最小请求/错误契约及安全错误映射。
- 修改原因：向认证房间成员开放前置任务已完成的玩家意图编排，同时保持权威状态与内部战斗转换私有。
- 业务影响：新增明确授权的三条公共 mutation；不改变既有 initialize/get/check、客户端、实时通知、存储、骰子或内部战斗生命周期。
- 性能影响：每个请求增加一次既有房间级异步 gate 获取；同房间 mutation 串行，不同房间仍可并行。
- 风险点：结构化 500 仅表示应用协调器报告的一致性失败；未捕获的基础设施异常仍由 ASP.NET Core 全局处理链负责。
- 回归建议：后续任务若调整应用错误码或 revision 语义，应同步更新精确 wire 映射测试，且不得扩展公开战斗路由集合。

## Major review 修复：invariant 异常安全边界

根因：`CombatDamageCommitInvariantException` 可从玩家意图协调器的权威伤害提交链抛出；原 HTTP handler 只映射返回型 `PlayerCombatIntentResult`，导致该异常进入 ASP.NET Core 开发异常响应并泄露内部文本。

RED 命令：

```powershell
dotnet test multiplayer/server/Trpg.Multiplayer.slnx --no-restore --filter "FullyQualifiedName~GameApiTests.CombatIntentRoutes_InvariantFailureReturnsSafeStructuredError" --nologo -v:minimal
```

RED 结果：3 个 case（`melee-attack`、`respond`、`pass`）全部失败；响应体为非 JSON 的开发异常文本，`JsonDocument.Parse` 抛出 `JsonReaderException`，证明真实 commit invariant 异常越过安全 HTTP 边界。

GREEN 结果：同一命令共 3 个 case，3 个通过，0 个失败，0 个跳过。

修复内容：三个公开 handler 共享 `RunCombatIntentAsync`；一次外层房间 gate 内仅捕获 `CombatDamageCommitInvariantException`，使用异常对象和结构化 `RoomId` 记录服务端错误，并返回固定 HTTP 500 `{ "code": "combat_consistency_failure" }`。响应不含 `currentGameRevision`、异常文本、堆栈或内部枚举；每个请求仅调用协调器一次。未捕获通用异常、未增加重试，返回型 stale/application error 继续经过原精确映射。
