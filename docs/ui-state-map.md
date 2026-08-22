# Godot UI 状态图

Gate 3B 把“首次安全快照”扩展为完整热座编排。`HotseatUiState` 是 Godot 的唯一输入，但不是规则真值；快照、合法行动、支付和胜负仍来自引擎。

## 应用生命周期

```text
Booting ──ABI/schema 可用──> MainMenu ──开始──> CreatingMatch
   │                                      │ create/start 成功
   └──加载/架构/协议失败──> NativeError   ▼
                                      Covered(P0, InitialReveal)
                                              │ 玩家主动揭示
                                              ▼
                                      HotseatMatchController
                                              │
                         ┌────────────────────┼────────────────────┐
                         ▼                    ▼                    ▼
                     可见对局             Finished             Faulted
                         │                    │                    │
                         └──返回/重开─────────┴──返回/重试─────────┘
                                              ▼
                                  Dispose old session（native handle 至多销毁一次）
```

重开不是复用旧 handle：必须先 dispose 旧 controller/session，再按产品随机配置重新 create/start，并回到 `Covered(Player0, InitialReveal)`。上层重复调用 dispose 必须安全，`SafeHandle` 保证 native destroy 至多一次。

## 热座状态

```text
Covered(InitialReveal/PassingDevice)
  └─ Reveal（此刻才请求该 viewer）
       ├─ phase=Mulligan ───────────────> MulliganSelecting
       ├─ phase=Action ─────────────────> Action
       ├─ phase=Reaction,pending ───────> Reaction
       └─ phase=Finished ───────────────> Finished

MulliganSelecting / Action / Reaction
  └─ 选择规范 LegalAction
       └─ ConfirmSelection
            └─ Covered(ResolvingCommand, CommandPrepared=true)
                 └─ Godot 延迟回调 SubmitPreparedCommand
                      ├─ 调度成功 ──────> MulliganReview
                      │                    └─ CompleteMulliganReview
                      ├─ 同一操作者继续 ─> 对应可见状态
                      ├─ 操作者变化 ─────> Covered(next, PassingDevice)
                      ├─ 规则拒绝 ───────> 刷新后的可见状态 + 中文原因
                      └─ native/协议错 ──> Faulted

任一可见状态发现 result!=Ongoing ──────> Finished
```

`Action` 与 `Reaction` 内部的目标、位置、组件来源和预支选择是 `HotseatActionSelection` 的渐进状态，不另造规则状态。候选来自同 revision 的合法行动集合；候选收敛到唯一规范命令后才允许预览与确认。

## 状态职责

| 状态 | 可以访问 native | 可以显示敏感数据 | 用户动作 |
|---|---|---|---|
| `Booting` | 仅 ABI/version 与预检 session | 否 | 退出 |
| `MainMenu` | 否 | 否 | 两席选牌组、开始、退出 |
| `CreatingMatch` | create/start | 否 | 退出 |
| `Covered(InitialReveal/PassingDevice)` | 不得读取等待中的新 viewer | 否；全画面完全不透明 | 揭示、返回菜单 |
| `Covered(ResolvingCommand)` | 只允许延迟提交已冻结命令，并读取提交前 viewer 的结果 | 否 | 无；避免重复提交 |
| `MulliganSelecting` | 当前 viewer 的 view/actions/events | 是，仅该 viewer 安全视图 | 选/取消起手牌、确认 |
| `MulliganReview` | 当前 viewer 的替换后 view/events | 是，仅原调度玩家 | 阅读替换手牌、确认交接 |
| `Action` | 当前 viewer 的 view/actions/queries/payment/events | 是，仅当前行动玩家 | 出牌、攻击、进化、部署、结束、投降等 |
| `Reaction` | 当前 responder 的 view/actions/reaction/events | 是，仅当前响应玩家 | 发动合法伏策或不过 |
| `Finished` | 使用控制器已取得的终局结果；不再发命令 | 只显示结果；战场、详情和日志已关闭 | 重开、返回菜单 |
| `Faulted` | 不继续访问失败 session | 否 | 重试/返回菜单/退出 |
| `Disposed` | 否 | 否 | 无 |

## 命令提交的绘制屏障

`ConfirmSelection()` 只冻结引擎枚举出的规范命令，绝不立即调用 `SubmitCommand`。它先发布 `Covered(ResolvingCommand)`，使 `MatchScreen` 清除手牌节点、卡牌详情、候选 metadata 和敏感日志，并让不透明遮挡至少完成一次 UI 更新；随后才以 Godot 延迟调用执行 `SubmitPreparedCommand()`。

这条顺序是热座隐私硬约束，不是视觉优化。尤其对结束回合、调度和响应命令，提交后操作者可能立即变化；新操作者的 `GetView`、queries 和 events 必须等其主动揭示后才发生。

## 事件读取与 ACK

```text
ReadEvents(viewer, cursor[viewer])
        │ 非破坏读取
        ▼
PendingEvents + PendingEventLastSequence
        │ Godot 已把全部事件渲染到该 viewer 日志
        ▼
AcknowledgeEvents()
        │
        └─ cursor[viewer] = last_sequence
```

约束：

1. Player0 与 Player1 的 cursor 独立；切换观看者不得复制或推进另一方 cursor。
2. 未完成渲染前不得 ACK。刷新或重入若再次读到同一 sequence，日志替换当前批次而不是重复追加。
3. 遮挡时日志和 `PendingEvents` 不显示；揭示后只读取对应 viewer 的脱敏事件。
4. 事件只驱动日志/表现，不能用于推演权威战场；每次提交后都以新快照为准。

## 隐私不变量

1. create/start 后首先显示完全不透明的 `PassDeviceOverlay`；首次揭示前 Player0 的 `GetView` 次数必须为零。
2. 操作者变化前必须清除手牌、卡牌详情、选中态、tooltip、metadata 和敏感日志；不得保留半透明战场。
3. 只有等待中的玩家主动揭示后，才请求该 viewer 的快照、合法查询和事件。
4. 对方手牌只显示数量/无身份牌背；对方背面伏策不得包含 definition、instance ID、卡名 tooltip 或稳定 metadata。
5. 响应 origin 只描述公开的原行动；可发动伏策身份仍只对 responder 可见。
6. UI 遮挡只提供热座物理隐私，不能代替 `scgs_v04` 的观看者脱敏。

## revision 与失败恢复

- 所有候选、支付预览和提交命令都绑定 `Snapshot.Revision`。
- revision 变化或 `StaleRevision` 会丢弃旧选择并重新执行“快照 → 合法行动 → 可选查询”；不会自动重试旧命令。
- engine 规则拒绝不会被当作异常，UI 显示 `EngineCodeZhCnFormatter` 的中文原因；未知 code 显示数值。
- native/协议异常进入 `Faulted`，敏感 UI 先清空；恢复路径必须释放旧 session。

## 发布前仍需人工完成

自动 smoke 可以覆盖状态遍历和导出启动，但不能替代以下硬门：物理 Apple Silicon Mac 整局/重开，以及两名真人在目标桌面构建上的遮挡交接整局。
