# 架构说明

## 现行结构

项目采用“规则真值、客户端契约、表现层”分层：

```text
冻结 v0.4 卡牌 + Game                 Gate 5A 产品目录（34＋1 全部锁定）
          │                                          │
共享 validate_* + 安全客户端 API       ProductBoard + ResolutionQueue synthetic 底座
          │                                          │
scgs_v04 / schema 1                    FoundationSession + schema-2 验证骨架
冻结旧对局、精确 14 导出                scgs_v05 / schema 2、精确 14 导出
          │                                          │
Scgs.Client v04                         Scgs.Client.V05
          └──────────────────┬───────────────────────┘
                     Scgs.Hotseat 编排
                             │
             Godot 4.7.2 .NET 产品表现边界
                  ┌──────────┴──────────┐
         过渡期 R2/R3 3D/2.5D   AnimeV1 独立审批样片
         当前旧对局与历史回归       无 native、八种状态
```

客户端不能直接读取 `PlayerState`、自行扣费或复算目标。它只能读取安全快照和查询结果，提交带 revision 的命令，再按观看者读取脱敏事件。legacy YGOPro2/Unity 代码不在现行调用链中。

## 原生 ABI 边界

`scgs_v04` 与 `scgs_v05` 都是纯 C11 动态库接口。公开头只出现明确宽度整数、UTF-8 字节缓冲区和进程内不复用的 64 位 token handle；Windows 固定 `__cdecl`，任何异常都在导出边界转换为 native 状态码。复杂 DTO 不镜像成易碎的 C struct，而是写入调用方所有的两段式 JSON 缓冲区。

ABI version、JSON schema version 与项目包版本相互独立。v04 固定 ABI 1.0／schema 1；v05 固定 ABI 2.0／schema 2，二者分别安装、加载和审计，各自只导出同构的 14 个 `scgs_v0x_*` 符号，v05 不从自己的库泄露 v04 符号。native/transport 错误和规则 `ErrorCode` 分离；规则枚举由显式映射冻结，不依赖 C++ enum 的底层值。动态库不返回需要跨 CRT 释放的内存，同一 handle 第一版只承诺顺序调用。详见 [`native-api-v04.md`](native-api-v04.md) 与 [`native-api-v05.md`](native-api-v05.md)。

冻结 v04 Native 适配层只序列化 Gate 1 的安全 DTO，并且只经 `make_view`、查询、统一命令和 `read_events` 访问对局；它不得先读取 `PlayerState` 或原始事件再做删字段式脱敏。v05 本轮尚无完整产品 `Game`，其临时 `FoundationSession` 直接从 `ProductBoard` 构造 schema-2 观看者安全 DTO，并由独立隐私测试约束；Gate 5C 接入完整产品查询 API 后必须收敛到与 v04 相同的“安全 API 在先”边界。

## C# 与 Godot 边界

`Scgs.Client` 是不依赖 Godot 的纯托管层，同时生成 `net8.0` 与 `net10.0`。现有命名空间绑定 v04；并行的 `Scgs.Client.V05` 使用独立 `LibraryImport`、SafeHandle、强类型 schema 2 DTO 和错误边界，两版通过同一 resolver 内按库名隔离的绝对路径绑定，不能让任一会话误加载另一版动态库。两版都统一处理两段式缓冲区、严格 UTF-8、native/engine 错误分层和每名 viewer 独立事件游标。

`Scgs.Hotseat` 同样生成两个 TFM。现有 v04 控制器继续负责同 revision 合法候选、规范命令冻结与操作者路由；产品选择状态另行表达 `ChooseMode`、`ChooseCards`、`OrderTriggers` 和 `ChooseAdditionalCost`。它只消费观看者安全的 `PendingChoiceView` 和短生命周期 opaque option ID，不能保存隐藏候选或自行判断卡牌效果。Godot 工程目标为 `net8.0`；`BootstrapController` 是组合根，场景代码不直接调用 P/Invoke 或复算规则。本轮 Godot 正式入口仍组合 v04，待 Gate 5C 完成产品整局后再切换。

所有 native 调用在 Godot 主线程顺序执行。动态库只从显式绝对路径加载：编辑器使用 `client/godot/native/<target>` 暂存目录，Windows 导出将 DLL 放在 EXE 同目录，macOS 导出将 dylib 放在 `.app/Contents/Frameworks`。详细约束见 [`godot-client-architecture.md`](godot-client-architecture.md)。

Gate 3C 把“结算”和“交接”拆为两种状态。准备命令后先进入不可交互的 `Resolving`，只保留不含 viewer 私有对象的中立公开战场，至少完整绘制两帧后才提交；初始揭示或操作者变化才进入完全不透明的 `Covered`。事件批次只有在相同 viewer/sequence 的日志完成绘制后才 ACK；换人时不得预取下一 viewer 的快照、查询或事件。

## Gate 4B-R2 表现与 Gate 4A.1 法术占位边界

Gate 4B-R2 在 Gate 4A 的 3D presenter、Gate 4A.1 的策略位法术规则和 Gate 4B-R1 的产品壳上重写战斗表现。`IScgsGameSession`、C ABI 1.0、schema 1、14 个导出与 legacy wire 均不改变，已有 `GameCommand.slot` 继续是 CastSpell 必填语义。`HotseatSurfaceInteractionCoordinator` 是 3D 与 2D 唯一共同操作入口：手牌、单位、策略、格位、战备和主战者都先转换为 `HotseatSurfaceRef`，点击与拖拽再转换为同一 `HotseatSurfaceIntent`。中央 CastZone 的冻结枚举值仅作兼容保留，任何对应 intent 都必须被拒绝；presenter 不能直接拼命令、扣费或推导目标。

产品默认使用 3D/2.5D 战场；仅测试/排障可通过精确参数 `--legacy-2d-board` 启用旧 2D presenter，该路径不是面向玩家的模式选择。两种 presenter 必须消费同一 `HotseatUiState`，并保持相同选择、响应、`Resolving`、换手与终局状态机。

authored 3D 场景把空间战场、相机相对的 `BattlefieldHandRig` 与 `CanvasLayer` HUD 分开：透视相机采用 58° FOV、约 58° 俯角，`BattlefieldViewportLayout` 按 1280/1600/2560 宽度档固定左右安全区，详情或日志显隐不会改变桌面 framing。手牌在独立前景景深层面向相机排成自适应弧线，桌面滚轮缩放不改变其屏幕卡高；对手手牌架只接收匿名共享卡背。靠近当前 viewer 的一侧只在完全遮挡期间重建。HUD 命中会阻止空间拾取，空间射线只在允许输入的 Action/Reaction 状态工作。拖拽超过 8 px 才成立，未达到阈值仍按点击处理；无效落点只恢复表现，不调用 native。

`CardVisualCatalog` 以 definition ID 映射 29 张唯一原创临时卡图，并提供无身份 fallback 正面与全局共享卡背。`MatchVisualIdentity` 只从公开的对局设置中得到两席牌组身份，`LeaderPortraitCatalog` 再映射两张临时头像；它们不读取 viewer 私密 DTO。Gate 4B-R2 冻结产品集连同卡背、菜单背景和 fallback 共 34 项，继续由原始 `ASSET_MANIFEST.json` 单独审计并供 R2 golden 引用；R3.1 的 1 张未批准候选地坪只登记在 `arena/R3_ASSET_MANIFEST.json`。联合资产审计校验两份清单间路径/哈希唯一且完整覆盖实际 35 项。视觉目录是可替换的表现数据，不是卡牌规则或第二套合法性数据。

`GlassHudTheme` 集中响应式安全矩形，`MatchHudPresenter` 只把安全 `MatchView` / `HotseatPublicBoardView` 和公开视觉身份绑定到左侧可收窄详情抽屉、两个独立玩家状态舱、阶段胶囊与悬浮控制。同一 viewport 只使用一个 `BackBufferCopy`，共享 screen-reading CanvasItem shader 为顶层面板提供模糊、半透明渐变、圆角与细描边。普通产品状态不使用全高不透明黑栏；只有物理交接的 `Covered` 必须完全不透明。安全 FX 队列只消费观看者安全 DTO、公开事件和公共投影，不推导规则。

主菜单在原生库不可用时仍可显示，只禁用本地热座。单人、在线、牌组编辑、图鉴和录像入口只能显示“开发中”，不得创建 session 或访问 native。视觉设置经 `IVisualSettingsStore` 持久化到 `user://settings.cfg`，且仅影响窗口、UI 缩放、VSync 和动效时长，不进入对局命令。

卡牌 actor 使用有限池复用。归还池前必须清除文字、definition-specific 卡图/材质与 shader 参数、Godot metadata、tooltip、碰撞掩码、signal/callback、tween、拖拽 token 与 viewer DTO 引用；从池中取出时再从当前公开/观看者安全状态完整赋值。隐藏 actor 只绑定共享卡背。进入 `Covered` 或 `Resolving` 时先执行同一敏感数据清理；`Covered` 清空全部可见战场并保持完全不透明，只有 `Resolving` 从 `HotseatPublicBoardView` 重建允许显示的公共 actor。显示提交屏障以至少两次 `FramePostDraw` 为准；无渲染循环的 headless smoke 使用两次 process-frame 栅栏作为专用回退。屏障期间禁止 raycast、事件 ACK、重复提交和旧 revision 回调。

Gate 4A full-match 继续使用 schema version 3 验证两局功能/surface/隐私；Gate 4B-R2 visual suite 升级为独立 schema version 4，验证四尺寸 16 种视觉状态、连续两个稳定 `FramePostDraw`、关键画面锚点、费用/身材/倒计时 GPU ROI、1600×900 golden、34 项资产和 600 帧资源/性能证据。两者是两套独立白名单与 validator。软件渲染只能豁免 GPU 时间阈值，不豁免功能、隐私、像素结构或资源零增长。

## Gate 5B 产品运行时底座

Gate 5B 在 `scgs::v2` 命名空间建立与冻结 v0.4 `Game` 并行的产品域，避免为加入护符、场地和私密选择而破坏 v04。`CardDefinition` 已具备字符串 `design_id`、职业／系列／中立标签、五种 `CardKind`、基础数值、分层关键词及通用模式／条件／效果类型；提交态 `product_catalog_v2.generated.cpp` 由 Gate 5A 权威设计清单与 `runtime-foundation.lock.json` 的结构化运行时形状共同确定性生成。当前产品目录只生成 AP-08／NT-04 的模式 ID 与目标形状、八张战备的 typed conditions 和 AP-S04 的精确额外代价筛选，逐卡 effect graph 仍为空。普通 CMake 构建直接编译生成物，不需要 Python；测试阶段的 `--check` 会重新计算并拒绝过期或手改的生成物。

每个生成定义都显式携带 `CardImplementationStatus` 与 `effects_compiled`。当前 34＋1 产品定义均为 `LockedNotImplemented/false`：可以用于 schema-2 安全视图和区域底座验证，但合法行动枚举与支付验证必须拒绝。只有 synthetic fixture 标为可执行；Gate 5C 完整编译某张牌的效果图后，才允许把该定义切换为产品可执行状态。这道闸门防止“目录里有卡名和模式”被误报为“牌已经能玩”。

`ProductBoard` 是区域与战斗不变量内核，而不是另一套 UI 规则：

- 五个 `MainBoard` 格允许随从和护符混合占用；护符不能攻击、进化或成为攻击目标；
- 每方独立 `Field` 格不占五格，新场地以 `FieldReplaced` 送墓且不标记为破坏；
- 所有移动以 `MoveReason` 记录，封存、弃牌、手满、额外代价、倒数结束、场地替换和终局清理不能混用“破坏”语义；
- 分层关键词把印刷、永久、回合内和已消耗状态分开，屏障消费不会误删其他来源，主动攻击吸血与防守反击分开；
- 护符倒数结束可用 resolution frame 预留原格，离场与衍生物召唤之间不能被其他永久物抢位。

`ResolutionQueue` 允许 effect frame 因模式、卡牌、触发排序或额外代价选择暂停。存在 `PendingChoice` 时，只允许选择者提交 `ResolveChoice`，双方仍可投降；非法、重复、越界或错误所有者选择不改变 revision。终局会幂等清空 pending choice 和剩余 frame，后续不能继续弹出选择或执行效果。`TriggerOrderPlanner` 先固定当前回合玩家组、再固定非当前玩家组；单一或完全等价的触发按印刷顺序自动排列，非等价多触发则生成该玩家私有的有序选择。

这一层当前只实现可复用基础语义和 synthetic fixture，并生成 34＋1 张牌的身份、基础数值、模式、目标形状、战备条件与额外代价筛选；所有产品定义仍显式锁为不可执行。它还不是完整的产品 `Game` 或逐卡效果解释器。过滤检索、置底、弃牌、模式、监听器、历史条件、职业充能、战备条件等字段已经有版本化表达边界，但每张锁定卡的完整执行和两副 30 张固定牌整局必须在 Gate 5C 完成后才能称为可玩。

`scgs_v05` 是 schema 2 的独立运输边界。它冻结 `CardKind` 0～4、`Zone` 0～8、原 `ActionKind` 0～10 并追加 11～13；`GameCommand`／`ActionQuery` 增加 `mode_id`、`choice_id`、有序 `selected_option_ids` 和 `additional_cost_cards`。`PlayerView` 公开五格混合主战场、三格策略区和独立可选场地。`PendingChoiceView` 只向选择者提供 opaque option ID 与私密候选，另一 viewer 只知道对方正在选择；实时快照和普通事件禁止包含产品 seed。本轮 deterministic foundation session 验证 14 导出运输骨架、生命周期、revision、双 viewer 脱敏和一条真实卡牌选择／恢复路径；目标／格位／组件目前为空，支付是零值 fixture，响应上下文固定为无响应。未实现的产品出牌动作受控返回规则错误，因此这不是完整运输语义或产品牌可玩声明。

## Gate 6A AnimeV1 视觉样片边界

AnimeV1 是全产品唯一长期视觉目标，不是可与科幻 R2/R3 并存的玩家皮肤。`design/product-decks-v1/anime-v1-visual.lock.json` 锁定未来 38 项主体美术和品牌、菜单、竞技场、卡框、图标、HUD、弹层、VFX、字体、fallback 与 shader 的迁移清单；Gate 6A 先提交 14 项原创候选素材用于用户审批。逐项 prompt、生成方式、SHA-256 和修改记录写在样片 provenance 与资产 manifest 中。

独立 `--anime-style-slice` 入口不创建 session、不加载 native，也不读取旧或新牌组状态。它只以静态安全数据绘制菜单、牌组设置、普通对局、手牌悬停、五类混合永久物、响应、`Covered` 和结果八态，验证固定斜视 2.5D 构图、相机相对扇形手牌、开放式幻想竞技场、五个主战场格、三个策略格与独立场地格。交互模式包含主战者呼吸／视差／入场、受击脉冲和胜负状态；自动截图模式会关闭这些时间相关动效，从而让同输入的像素证据可复现。样片通过不能证明 Gate 5C 产品牌可玩；用户明确批准后，Gate 6C 才能把动漫菜单、竞技场、卡牌、HUD、全部弹层和 VFX 设为唯一默认，并删除旧科幻产品 profile。

Gate 6A 初版的拼贴式卡框、悬浮费用／身材徽章、黑色名称条和独立类型文字已经判定为不合格，不再是 AnimeV1 的卡体基线。Gate 6A-R1 改由纯托管的共享 `CardFaceComposition` 统一 `design_id` 到插画裁切、连续框体、职业纹章、稀有度、名称与数值插槽的表现映射；预览、手牌、场上和详情 context 消费同一组合结果。真实 `CardActor3D` 在战场主 Viewport 中直接构成这些分层，不为每张卡建立 `SubViewport`，旧 v04 actor 路径则保留兼容边界。独立 `--anime-card-body-slice` 审批入口不创建 session、也不访问 native；隐藏牌在 composition 之前即分流，只允许共享卡背，不能查询或绑定正面目录。专项设计、启动方式和取证契约见 [Gate 6A-R1：AnimeV1 一体化卡体](anime-v1-card-body-r1.md)。这仍是待审批候选；代表插画、主战者、菜单主视觉和竞技场均未因此获得最终批准，也不表示 Gate 5C 新牌组已经可玩。

隐藏牌仍只允许统一卡背，任何身份纹理不得写入隐藏 actor、metadata、tooltip 或材质。样片纹理使用桌面 VRAM 压缩、高质量和 mipmap；审计把源文件、估算驻留显存、清单哈希与跨清单路径唯一性分开记录。最终商业发布仍需逐图人工检查人物手脸、构图、缩略可读性、文字／水印和潜在 IP 近似。

## 冻结 v0.4 规则域

### `CardDefinition` 与 `CardInstance`

`CardDefinition` 保存不随比赛改变的卡牌数据；`CardInstance` 保存控制者、区域、战斗状态、进化状态、部署来源及运行时组件等实例数据。内部实例 ID 用于引擎关联，但隐藏区域的客户端视图不得暴露稳定 ID。

### `PlayerState`

保存主战者、PP、裂痕、进化能量、每回合限制，以及牌组、手牌、5 个单位位、3 个策略位、战备、墓地和封存区。它是内部状态，不是客户端 DTO。

### `Game`

冻结 v0.4 的 `Game` 是旧对局的唯一状态变更入口。产品默认随机先手；测试可强制 `Player0` 或 `Player1`，并可提供 seed。v04 仍按冻结 schema 1 把实际 seed 和先手写入开局事件与安全快照；v05 schema 2 明确禁止实时输出 seed，只允许测试配置和赛后报告持有。本阶段都只保证同一工具链下同 seed 可复现，不承诺 `std::shuffle` 跨标准库产生相同排列。

## 客户端安全契约

### 快照

`make_view(viewer)` 生成 `MatchView`：

- 自己手牌包含完整可操作数据；
- 对方手牌只包含数量；
- 对方背面伏策不包含 definition ID 或 instance ID；
- 战备、单位、墓地和封存区公开；
- 快照包含单调递增的 `revision`；冻结 v04 还包含实际 seed 与先手，v05 只公开先手而隐藏 seed。

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
