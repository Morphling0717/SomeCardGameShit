# 架构说明

## 现行结构

项目采用“规则真值、客户端契约、表现层”分层：

```text
卡牌定义 + Game 状态机（C++20，唯一规则真值）
                         │
             共享 validate_* 合法性逻辑
                         │
     ┌───────────────────┴───────────────────┐
     │                                       │
观看者安全查询                         统一 GameCommand
MatchView / LegalAction / Preview       expected_revision
     │                                       │
     └────────── GameEventView ──────────────┘
                    │
       scgs_v04 C11 + UTF-8 JSON（Gate 2）
                    │
       Scgs.Client 纯托管边界（Gate 3A）
                     │
      Scgs.Hotseat 热座与 surface intent 编排（Gate 4B-R2）
                     │
     Godot 4.7.2 .NET 产品表现边界（Gate 4B-R2）
          ┌──────────┴──────────┐
 authored 默认 3D/2.5D   隐藏 legacy 2D
 CardVisual/Portrait 目录   同源功能回归
 Camera-relative Hand Rig / HUD / 安全 FX
```

客户端不能直接读取 `PlayerState`、自行扣费或复算目标。它只能读取安全快照和查询结果，提交带 revision 的命令，再按观看者读取脱敏事件。legacy YGOPro2/Unity 代码不在现行调用链中。

## 原生 ABI 边界

`scgs_v04` 是纯 C11 动态库接口。公开头只出现明确宽度整数、UTF-8 字节缓冲区和进程内不复用的 64 位 token handle；Windows 固定 `__cdecl`，任何异常都在导出边界转换为 native 状态码。复杂 DTO 不镜像成易碎的 C struct，而是写入调用方所有的两段式 JSON 缓冲区。

ABI version、JSON schema version 与项目包版本相互独立。native/transport 错误和规则 `ErrorCode` 分离；规则枚举由显式映射冻结，不依赖 C++ enum 的底层值。动态库不返回需要跨 CRT 释放的内存，同一 handle 第一版只承诺顺序调用。详见 [`native-api-v04.md`](native-api-v04.md)。

Native 适配层只序列化 Gate 1 的安全 DTO，并且只经 `make_view`、查询、统一命令和 `read_events` 访问对局；它不得先读取 `PlayerState` 或原始事件再做删字段式脱敏。

## C# 与 Godot 边界

`Scgs.Client` 是不依赖 Godot 的纯托管层，同时生成 `net8.0` 与 `net10.0`。它以 `LibraryImport` + `cdecl` 绑定全部 14 个 ABI 导出，用 `SafeHandle` 管理 64 位 token，并统一处理绝对路径加载、两段式缓冲区、严格 UTF-8、schema 1 JSON 和 native/engine 错误分层。`Scgs.Hotseat` 同样生成两个 TFM，只依赖 `IScgsGameSession`，负责同 revision 合法候选、上下文选择步骤、规范命令冻结、双 viewer 事件游标和操作者路由。Godot 工程目标为 `net8.0`；`BootstrapController` 是组合根，场景代码不直接调用 P/Invoke 或复算规则。

所有 native 调用在 Godot 主线程顺序执行。动态库只从显式绝对路径加载：编辑器使用 `client/godot/native/<target>` 暂存目录，Windows 导出将 DLL 放在 EXE 同目录，macOS 导出将 dylib 放在 `.app/Contents/Frameworks`。详细约束见 [`godot-client-architecture.md`](godot-client-architecture.md)。

Gate 3C 把“结算”和“交接”拆为两种状态。准备命令后先进入不可交互的 `Resolving`，只保留不含 viewer 私有对象的中立公开战场，至少完整绘制两帧后才提交；初始揭示或操作者变化才进入完全不透明的 `Covered`。事件批次只有在相同 viewer/sequence 的日志完成绘制后才 ACK；换人时不得预取下一 viewer 的快照、查询或事件。

## Gate 4B-R2 表现与 Gate 4A.1 法术占位边界

Gate 4B-R2 在 Gate 4A 的 3D presenter、Gate 4A.1 的策略位法术规则和 Gate 4B-R1 的产品壳上重写战斗表现。`IScgsGameSession`、C ABI 1.0、schema 1、14 个导出与 legacy wire 均不改变，已有 `GameCommand.slot` 继续是 CastSpell 必填语义。`HotseatSurfaceInteractionCoordinator` 是 3D 与 2D 唯一共同操作入口：手牌、单位、策略、格位、战备和主战者都先转换为 `HotseatSurfaceRef`，点击与拖拽再转换为同一 `HotseatSurfaceIntent`。中央 CastZone 的冻结枚举值仅作兼容保留，任何对应 intent 都必须被拒绝；presenter 不能直接拼命令、扣费或推导目标。

产品默认使用 3D/2.5D 战场；仅测试/排障可通过精确参数 `--legacy-2d-board` 启用旧 2D presenter，该路径不是面向玩家的模式选择。两种 presenter 必须消费同一 `HotseatUiState`，并保持相同选择、响应、`Resolving`、换手与终局状态机。

authored 3D 场景把空间战场、相机相对的 `BattlefieldHandRig` 与 `CanvasLayer` HUD 分开：透视相机采用 58° FOV、约 58° 俯角，`BattlefieldViewportLayout` 按 1280/1600/2560 宽度档固定左右安全区，详情或日志显隐不会改变桌面 framing。手牌在独立前景景深层面向相机排成自适应弧线，桌面滚轮缩放不改变其屏幕卡高；对手手牌架只接收匿名共享卡背。靠近当前 viewer 的一侧只在完全遮挡期间重建。HUD 命中会阻止空间拾取，空间射线只在允许输入的 Action/Reaction 状态工作。拖拽超过 8 px 才成立，未达到阈值仍按点击处理；无效落点只恢复表现，不调用 native。

`CardVisualCatalog` 以 definition ID 映射 29 张唯一原创临时卡图，并提供无身份 fallback 正面与全局共享卡背。`MatchVisualIdentity` 只从公开的对局设置中得到两席牌组身份，`LeaderPortraitCatalog` 再映射两张临时头像；它们不读取 viewer 私密 DTO。连同卡背、菜单背景和 fallback 在内，`ASSET_MANIFEST.json` 对 34 项视觉资产做路径与 SHA-256 审计。视觉目录是可替换的表现数据，不是卡牌规则或第二套合法性数据。

`GlassHudTheme` 集中响应式安全矩形，`MatchHudPresenter` 只把安全 `MatchView` / `HotseatPublicBoardView` 和公开视觉身份绑定到左侧可收窄详情抽屉、两个独立玩家状态舱、阶段胶囊与悬浮控制。同一 viewport 只使用一个 `BackBufferCopy`，共享 screen-reading CanvasItem shader 为顶层面板提供模糊、半透明渐变、圆角与细描边。普通产品状态不使用全高不透明黑栏；只有物理交接的 `Covered` 必须完全不透明。安全 FX 队列只消费观看者安全 DTO、公开事件和公共投影，不推导规则。

主菜单在原生库不可用时仍可显示，只禁用本地热座。单人、在线、牌组编辑、图鉴和录像入口只能显示“开发中”，不得创建 session 或访问 native。视觉设置经 `IVisualSettingsStore` 持久化到 `user://settings.cfg`，且仅影响窗口、UI 缩放、VSync 和动效时长，不进入对局命令。

卡牌 actor 使用有限池复用。归还池前必须清除文字、definition-specific 卡图/材质与 shader 参数、Godot metadata、tooltip、碰撞掩码、signal/callback、tween、拖拽 token 与 viewer DTO 引用；从池中取出时再从当前公开/观看者安全状态完整赋值。隐藏 actor 只绑定共享卡背。进入 `Covered` 或 `Resolving` 时先执行同一敏感数据清理；`Covered` 清空全部可见战场并保持完全不透明，只有 `Resolving` 从 `HotseatPublicBoardView` 重建允许显示的公共 actor。显示提交屏障以至少两次 `FramePostDraw` 为准；无渲染循环的 headless smoke 使用两次 process-frame 栅栏作为专用回退。屏障期间禁止 raycast、事件 ACK、重复提交和旧 revision 回调。

Gate 4A full-match 继续使用 schema version 3 验证两局功能/surface/隐私；Gate 4B-R2 visual suite 升级为独立 schema version 4，验证四尺寸 16 种视觉状态、连续两个稳定 `FramePostDraw`、关键画面锚点、费用/身材/倒计时 GPU ROI、1600×900 golden、34 项资产和 600 帧资源/性能证据。两者是两套独立白名单与 validator。软件渲染只能豁免 GPU 时间阈值，不豁免功能、隐私、像素结构或资源零增长。

## 规则域

### `CardDefinition` 与 `CardInstance`

`CardDefinition` 保存不随比赛改变的卡牌数据；`CardInstance` 保存控制者、区域、战斗状态、进化状态、部署来源及运行时组件等实例数据。内部实例 ID 用于引擎关联，但隐藏区域的客户端视图不得暴露稳定 ID。

### `PlayerState`

保存主战者、PP、裂痕、进化能量、每回合限制，以及牌组、手牌、5 个单位位、3 个策略位、战备、墓地和封存区。它是内部状态，不是客户端 DTO。

### `Game`

`Game` 是唯一状态变更入口。产品默认随机先手；测试可强制 `Player0` 或 `Player1`，并可提供 seed。实际 seed 和先手写入开局事件与安全快照。本阶段只保证同一工具链下同 seed 可复现，不承诺 `std::shuffle` 跨标准库产生相同排列。

## 客户端安全契约

### 快照

`make_view(viewer)` 生成 `MatchView`：

- 自己手牌包含完整可操作数据；
- 对方手牌只包含数量；
- 对方背面伏策不包含 definition ID 或 instance ID；
- 战备、单位、墓地和封存区公开；
- 快照包含单调递增的 `revision`、实际 seed 与先手。

快照是最终状态真值；事件只用于日志和表现。

### 查询与命令

`list_legal_actions`、`list_valid_targets`、`list_valid_slots`、`list_valid_donors`、`preview_payment` 和 `get_reaction_context` 与 `submit_command` 共享同一套 `validate_*` 逻辑。不得为旧强类型命令和新统一命令维护两套规则判断。

每条 `GameCommand` 携带 `expected_revision`：

- 成功命令完整结算后，revision 恰好增加一次；
- 失败或过期命令不改变状态、事件历史或 revision；
- 所有公开入口先验证 `PlayerId`，非法枚举返回 `InvalidPlayer`，不得索引数组。

### 事件

`read_events(viewer, after_sequence)` 非破坏性读取追加式事件历史。两个观看者可以使用独立游标，不会互相消费事件。抽牌、调度、设置伏策等隐藏事件按观看者脱敏；隐藏文本不得包含卡名或稳定实例 ID。

Gate 1 的合法行动枚举以事务副本复用命令验证，优先保证固定牌组 alpha 的查询/执行一致性。它会随候选数和事件历史增长而变贵；在开放卡组编辑或扩大手牌上限前，应把纯验证路径从副本执行中抽出并做性能基准。

## 状态机与事务边界

普通命令的核心顺序是：

```text
验证完整输入 → 支付成本 → 执行动作/开响应窗 → LIFO 结算
→ 同时死亡批次 → 胜负检查 → 记录事件 → revision + 1
```

目标必须在支付前完整验证。如果目标在响应期间失效，只跳过依赖该目标的效果；同一效果记录中的其他效果继续，已支付成本不回滚。

结束回合顺序固定为：

```text
结束效果 → 清除临时状态 → 当前 PP 清零并发事件
→ TurnEnded → 对方回合开始
```

终局是幂等终态：一局只产生一个 `MatchEnded`。进入终局后不再抽牌、不处理设施倒计时，也不接受其他会改变状态的命令。

响应栈最多三层，按“反制 → 响应 → 原行动”的 LIFO 顺序结算。实现不得持有跨 `std::vector::push_back` 的元素引用；反制层过牌只关闭该机会，不得丢弃底层伏策或原行动。

## 同时死亡与 alpha 限制

同一批死亡先全部移出场，再按当前回合方、非当前回合方处理触发。人工排序尚未进入 alpha，同一玩家内部暂按确定性场地顺序。这是明确限制，不是最终 UI 规则。

Alpha 只验收现有两副固定牌组。主战技 UI、普通主动能力、人工触发排序和固定牌组未使用关键词延后。

## 不变量与兼容性

`Game::validate_invariants()` 检查区域唯一性、控制权、序列、区域类型、战斗数值、资源、响应层和终局一致性。无界面代理应在每个命令后检查不变量。

legacy v1 wire 的消息 ID、长度、字节序和金标字节保持冻结。当前引擎状态到 legacy 字段的投影语义见 [`protocol.md`](protocol.md)；它不是 Godot 同进程接口，Godot 后续只消费独立的 `scgs_v04` ABI。
