# Godot UI 状态图

Gate 3A 用最小状态机完成“启动 → 选牌组 → 安全揭示 → 第一张真实快照”。任何展示状态都不能成为规则真值。

## 应用状态

```text
Booting ──原生 ABI 可用──> MainMenu ──开始──> CreatingMatch
   │                                          │
   └──加载/架构/ABI/schema 失败──> NativeError │ create/start 成功
                                              ▼
                                      AwaitingReveal(P0)
                                              │ 玩家主动揭示
                                              ▼
                                        Snapshot(P0)
                                              │ 返回菜单 / 退出
                                              ▼
                                           Dispose
```

`NativeError` 必须显示脱敏、可操作的诊断；不能崩溃、弹出未捕获异常或继续创建 session。`Dispose` 无论从正常返回、错误恢复还是场景退出进入，都只销毁一次 handle。

## 状态职责

| 状态 | 可以读取 native | 可以显示敏感数据 | 用户动作 |
|---|---|---|---|
| `Booting` | 仅 ABI/version | 否 | 退出 |
| `MainMenu` | 否 | 否 | 选择两席牌组、开始、退出 |
| `CreatingMatch` | create/start | 否 | 退出 |
| `AwaitingReveal` | 不得调用新 viewer 的 view/events | 否；遮挡完全不透明 | 揭示、返回菜单 |
| `Snapshot` | 当前 viewer 的 view/queries/events | 仅当前 viewer 被允许的数据 | 返回菜单 |
| `NativeError` | 不继续访问失败 session | 否 | 返回菜单或退出 |

## 隐私不变量

1. 创建并启动比赛后，首先显示完全不透明的 `PassDeviceOverlay`。
2. 在用户点击“揭示”前，viewer 0 的 `GetView` 调用次数必须为零。
3. 将来每次操作者变化都先清除手牌节点、关闭详情和敏感日志，再进入遮挡；不得保留半透明战场。
4. 只有遮挡完成揭示后，才请求新 viewer 的快照、查询和事件。
5. 每个 viewer 使用独立事件 cursor；切换 viewer 不复用另一方 cursor。
6. 对方手牌只显示数量/牌背；对方背面伏策不得显示 definition、instance ID 或可反推身份的文本。
7. UI 遮挡只提供热座物理隐私，不能代替 `scgs_v04` 的观看者脱敏。

## 后续 Gate 3B 扩展点

`Snapshot` 之后会按 `MatchView.phase` 和 `ReactionContext` 增加 `Mulligan`、`Idle`、`SelectingTarget`、`SelectingSlot`、`SelectingDonor`、`ConfirmingPayment`、`Reaction` 与 `Finished`。revision 变化或 `StaleRevision` 必须清空所有临时选择并重新查询，不能尝试复用旧高亮或旧支付预览。
