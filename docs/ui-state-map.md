# Godot UI 状态图

Gate 4A 在 Gate 3C 的完整热座闭环上把默认战场改为 3D/2.5D，同时保留隐藏的 legacy 2D 回归 presenter。`HotseatUiState` 与 `HotseatInteractionContext` 是两个 presenter 的共同输入，但不是规则真值；快照、合法行动、支付与胜负仍来自引擎。

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

重开不是复用旧 handle：先 dispose 旧 controller/session，再按产品随机配置重新 create/start，并回到初始完全遮挡。重复 dispose 安全，`SafeHandle` 保证 native destroy 至多一次。

表现后端只在应用启动时决定：默认进入 `3D`，精确启动参数 `--legacy-2d-board` 才进入 `legacy-2D`。它不是比赛中可切换的 UI 状态；两个后端必须走同一热座状态机与 surface intent 协调器。

## 热座与提交状态

```text
Covered(InitialReveal/PassingDevice)
  └─ Reveal（此刻才请求该 viewer）
       ├─ phase=Mulligan ───────────────> MulliganSelecting
       ├─ phase=Action ─────────────────> Action
       ├─ phase=Reaction,pending ───────> Reaction
       └─ phase=Finished ───────────────> Finished

MulliganSelecting / Action / Reaction
  └─ 来源与必要选择收敛为规范 LegalAction
       └─ PrepareSelectedCommand（不调用 native）
            └─ Resolving(CommandPrepared=true, PublicBoard!=null)
                 └─ 显示环境至少两次 FramePostDraw
                    （headless 使用两次 process-frame 栅栏）
                      └─ SubmitPreparedCommand
                           ├─ 调度成功 ──────> MulliganReview
                           │                    └─ CompleteMulliganReview
                           ├─ 同一操作者继续 ─> 对应可见状态
                           ├─ 操作者变化 ─────> Covered(next, PassingDevice)
                           ├─ 规则拒绝 ───────> 刷新后的旧 viewer 状态 + 中文原因
                           └─ native/协议错 ──> Faulted

任一可见状态发现 result!=Ongoing ──────> Finished
```

`Resolving` 只用于提交期间的中立公开投影；`Covered` 只用于初次揭示与真实换手。两者不能互换：公共投影改善连续操作反馈，完全遮挡保护即将接手玩家的私密信息。

## 直接交互选择步骤

```text
None
 └─ 点击/拖拽合法来源
      ├─ 多种动作 ───────────────> ChooseAction
      └─ 唯一动作 ───────────────> 下一个必要步骤
                                      │
                  ChooseDonor ─ ChooseSlot ─ ChooseTarget ─ ChooseAdvance
                                      │ 省略不适用步骤；顺序由候选决定
                                      ▼
                                   Ready
                                      │ 最后一个必要选择已完成
                                      └──────────────> PrepareSelectedCommand
```

- 第一次点击来源不提交。无目标、格位或组件的动作必须再按一次来源旁的明确动作按钮。
- 多个动作只在来源附近显示上下文按钮；单一动作直接高亮下一组合法对象。
- 点击和拖拽产生相同 intent，并经过相同候选过滤、revision 与支付预览。
- 目标、部署组件、具体格位和预支都是选择步骤，不再增加通用确认页。
- 调度保留一次整批确认，投降保留二次确认；结束回合固定按钮直接准备命令。
- `StepBackSelection()` 撤销最近一个显式步骤；自动补全的共同字段不单独进入历史。完整取消才清空来源。
- 无效拖放原位回弹，不调用 native，也不改变 revision、事件或游标。

## 3D 空间输入闸门

```text
鼠标/键盘输入
  ├─ HUD 命中或输入已被消费 ───────────> 停止；不发射空间射线
  ├─ mode 不是 Action / Reaction ─────> 停止；不读取碰撞对象
  └─ 允许空间输入
       └─ Camera3D raycast → actor/slot/leader/cast-zone
              └─ HotseatSurfaceRef → HotseatSurfaceIntent
```

- 按下到移动不足 8 px 视为点击；达到 8 px 才显示拖拽幽灵和目标反馈。
- 空间命中只标识 surface，合法性仍由同 revision 的候选决定；无效落点不能调用 native。
- viewer 透视只能在 `Covered` 的完全不透明帧内翻转并重建，揭示后近端始终是当前 viewer。
- `Resolving`、`Covered`、`MulliganSelecting/Review`、`Finished`、`Faulted` 与 `Disposed` 都锁死空间输入；调度继续使用专用 HUD。
- 3D actor 归还池时清除文字、材质、metadata、tooltip、碰撞、回调和拖拽 token，不能把上一 viewer 数据带入后续状态。

## 状态职责

| 状态 | 可以访问 native | 可以显示敏感数据 | 用户动作 |
|---|---|---|---|
| `Booting` | 仅 ABI/version 与预检 session | 否 | 退出 |
| `MainMenu` | 否 | 否 | 两席选牌组、开始、退出 |
| `CreatingMatch` | create/start | 否 | 退出 |
| `Covered(InitialReveal/PassingDevice)` | 不得读取等待中的新 viewer | 否；全画面完全不透明 | 揭示、返回菜单 |
| `MulliganSelecting` | 当前 viewer 的 view/actions/events | 是，仅该 viewer 安全视图 | 切换调度牌、整批确认 |
| `MulliganReview` | 当前 viewer 的替换后 view/events | 是，仅原调度玩家 | 阅读替换手牌、确认交接 |
| `Action` | 当前 viewer 的 view/actions/queries/payment/events | 是，仅当前行动玩家 | 直接出牌、攻击、进化、部署、结束、投降等 |
| `Reaction` | 当前 responder 的 view/actions/reaction/events | 是，仅当前响应玩家 | 选择合法伏策/目标，或不过 |
| `Resolving` | 只允许提交冻结命令及读取提交前 viewer 结果 | 否；仅中立公开投影 | 无；输入锁死 |
| `Finished` | 使用已取得的终局结果；不再发命令 | 只显示结果 | 重开、返回菜单 |
| `Faulted` | 不继续访问失败 session | 否 | 重试/返回菜单/退出 |
| `Disposed` | 否 | 否 | 无 |

## Resolving 公共投影不变量

`HotseatPublicBoardView` 由进入 `Resolving` 前的安全状态投影，但不持有 viewer 私有 DTO 或节点引用：

1. 双方手牌只保留数量，没有任何 definition、instance、名称或可反推 metadata。
2. 所有背面伏策统一匿名，没有 tooltip、详情或稳定 ID；公开单位、公开战备和公开资源可以显示。
3. 卡牌详情、事件日志、候选、高亮、拖拽 payload、旧 signal 回调和输入焦点全部清除。
4. `HotseatUiState.Snapshot`、`Viewer`、`LegalActions`、`PendingEvents` 在 `Resolving` 内为空。
5. 显示环境的公共投影至少经过两次 `FramePostDraw` 后才允许提交；无渲染循环的 headless smoke 使用两次 process-frame 栅栏。期间禁止 raycast、ACK、重复提交和旧 revision 回调。
6. 提交后操作者变化时，立即转入完全不透明的 `Covered(PassingDevice)`，不得短暂显示下一 viewer 数据。

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

Player0 与 Player1 的 cursor 独立。未完成渲染前不得 ACK；重读同一批次时替换当前日志而不是重复追加。`Covered` 与 `Resolving` 都不显示或 ACK viewer 事件；揭示后只读取对应 viewer 的脱敏事件。快照始终是状态真值，事件只用于日志和表现。

## revision 与失败恢复

- 所有候选、支付预览和提交命令绑定 `Snapshot.Revision`。
- revision 变化或 `StaleRevision` 会丢弃旧选择并重新执行“快照 → 合法行动 → 可选查询”，不会重放旧 intent。
- engine 规则拒绝不是异常；回到刷新后的旧 viewer 状态并显示中文原因。
- native/协议异常进入 `Faulted`，私密 UI 先清空；恢复路径释放旧 session。

## 发布前人工硬门

自动 signal smoke 可以覆盖状态遍历和导出启动，但不能替代两种参考分辨率的 3D 人工视觉遍历、物理 Apple Silicon Mac 整局/重开，也不能替代两名真人对空间拾取、公共结算、完全遮挡、透视翻转、设备交接和主动揭示的观察。
