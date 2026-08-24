# Godot UI 状态图

Gate 4B-R2 在 Gate 4A/4A.1 的默认 3D/2.5D 完整热座闭环与 Gate 4B-R1 产品壳上重写前景手牌、卡牌数值、稳定镜头和战场/HUD 构图，同时保留隐藏的 legacy 2D 回归 presenter。`HotseatUiState` 与 `HotseatInteractionContext` 是两个 presenter 的共同输入，但不是规则真值；快照、合法行动、支付与胜负仍来自引擎。本轮不改变 11 种 `ActionKind`、两阶段提交或热座隐私状态机。

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

表现后端只在应用启动时决定：默认进入 `3D`，精确启动参数 `--legacy-2d-board` 才进入 `legacy-2D`。它不是比赛中可切换的 UI 状态；两个后端必须走同一热座状态机与 surface intent 协调器。legacy 2D 只是隐藏的自动回归后门，不在产品菜单中提供视觉等价的第二模式。

## Gate 4B-R2 战斗表现与 HUD

- `MainMenu` 是完整产品入口：本地热座可用，单人、在线、牌组、图鉴与回放显示“开发中”且不创建 session；设置、退出、版本与许可证入口可用。
- 本地热座设置允许两席独立选择 `midrange` / `advance`，允许同牌组。窗口/无边框全屏、窗口分辨率、UI 缩放、VSync 与减少动画持久化到 `user://settings.cfg`，非法值回退默认。
- 对局 HUD 不再使用左右全高黑栏：左侧详情和右侧控制使用固定响应式安全区，显隐不会触发相机缩放“呼吸”；阶段与结束回合保持紧凑。开发诊断文字只能由 `F3` 或 `--show-debug-ui` 显示。
- 己方手牌由相机相对 `BattlefieldHandRig` 在屏幕下方排成弧线，悬停/选中/拖拽仍汇入同一 surface intent；对手手牌架只能显示匿名共享卡背。
- 两张临时主战者头像只按对局设置中的公开牌组映射；同牌组两席可使用同头像，由席位标签、位置和活动光环区分。未知牌组只使用中性 fallback，头像不读取 viewer 私密 DTO。
- 调度、响应、战备、暂停、结果、错误和日志使用同一玻璃主题。`Covered` 是唯一必须保持全屏完全不透明的产品状态；`Resolving` 只保留公开战场和窄结算提示。
- 卡图、卡背、菜单背景和头像都是可替换临时素材，不是最终发布美术；本 Gate 没有加入音频。

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
       └─ Camera3D raycast → card actor / slot / leader / standby surface
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
| `MainMenu` | 否 | 否 | 产品导航、两席选牌组、视觉设置、开始、退出 |
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

## Gate 4B-R2 视觉与资源自动契约

- 视觉目录登记 34 项媒体：29 张独立卡图、通用正面 fallback、1 张卡背、1 张菜单背景和 2 张牌组头像。每项记录路径、SHA-256、用途、生成方式、日期与 prompt 摘要；未登记、缺失、重复或哈希不符都使自动审计失败。
- display-backed 视觉套件在 1280×720、1600×900、2560×1440 与 2560×1600 运行，覆盖历史 11 种产品状态以及单张/五张/十张手牌、悬停和场上可读性，共 16 种状态。
- 结构契约检查控件不越界、HUD 不重叠、正常模式无调试标签、战场物理安全区占比、调度托盘不遮手牌，并为桌面、主战者、手牌、HUD 与数值徽章记录最终 GPU 像素证据；每张图等待连续两个内容一致的 `FramePostDraw`。1600×900 golden 只能在人工审阅后显式更新，CI 不自动批准新 golden。
- 最大场面经过 300 帧预热和 300 帧测量，要求 actor/material/texture 数零增长。硬件加速 renderer 还必须满足 p95 ≤ 33.3 ms 且单帧 < 100 ms；被明确识别的纯软件 renderer 只豁免 GPU 时间阈值，11 状态功能、截图、布局、隐私、600 帧和资源零增长仍全部强制，其 timing 不作为硬件性能证据。

## 发布前人工硬门

Gate 4B-R2 的实现、四分辨率 16 状态 visual suite、导出和 CI 尚须由同提交实测证明；即使自动化完成，也不能替代以下三项发布硬门：

1. 在物理 Apple Silicon Mac 上完成整局、退出与重开；
2. 在未安装 Visual Studio 的 Windows x86-64 机器上从导出包完成整局；
3. 两名真人完成一局热座，并逐次观察空间拾取、公共结算、完全遮挡、透视翻转、设备交接与主动揭示。

三项都有真实记录前不允许标记 `v0.4-hotseat-alpha.1`。
