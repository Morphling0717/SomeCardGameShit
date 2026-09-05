# 卡框精修 R1：独立审阅候选

状态：**候选，等待真实 GPU 检查与用户视觉批准**。本轮聚焦卡框的实体轮廓、
雕花、宝石镶座、顶部独立名牌和文字可读性，不是新的战斗演出交付，也没有
将候选卡框设为正常产品的默认画面。

## 入口与范围

在 Godot Editor 中直接打开 `res://scenes/review/CardFrameReview.tscn` 并运行当前场景
（F6 或已连接 MCP 的场景运行工具）。该场景继承原审阅场景，根节点的受控
`CardFrameReview=true` 导出属性选择新卡框；它不会自动开始或绕过玩家揭示。
原 `BattlePresentationReview.tscn` 仍选择旧入口，两种标记不能同时打开。

在含本实现的导出目录中使用：

```powershell
& .\SomeCardGameShit.exe -- --card-frame-review
```

普通启动仍进入现有产品；`--battle-presentation-review` 保留上一轮卡体与演出
审阅路径。两个 review 参数不能一起使用，也不能与 `--ci-product-smoke` 混用。
本轮仍只将 LO-11「曜誓大团长·蕾奥妮」、AP-11「禁忌毕业生·诺克缇娅」和
NT-04「界域裁定」作为新卡框代表，不是全 35 个定义的换装。

三个入口复用 `PresentationReviewScenario`：使用当前真实 v05 固定牌组、合法
查询与普通命令准备场面。玩家必须经过完全不透明 Covered 并主动揭示，然后
亲手选择手牌、具体格位、必要模式／目标或进化。不能把合成排版图片当成这一步。

`CardFrameReviewRuntime.Enabled` 与上一轮 `BattlePresentationReviewRuntime`
是独立的视觉开关。卡框入口保留已有 `Presenting` 公共事件播放，以便观察
「界域裁定」在同一命令内进入后排、命中、送墓的真实中间位置；**沿用的基础
动作不计作本轮演出升级**。正常产品入口与热座揭示、两帧公共结算、命令 revision、
独立 viewer 游标、取消和隐私清理规则不因卡框审阅改变。

## 模型、贴图与文字的职责

- 编辑源与概念图留在项目外的 `art/card_frame_r1/`；Blender 生成脚本为
  `scripts/art/build_card_frame_r1.py`。它们不是 Godot 玩家包的运行素材。
- 运行时使用 `assets/visual/anime_v1/card_frame_r1/` 中的高／低细节 GLB 及
  四张 PNG。模型的实际轮廓、材质槽、变换和三角形数由
  `scripts/check_card_frame_r1_assets.py` 对真实 GLB 字节检查；源文件与输出
  哈希由 `art/card_frame_r1/frame-manifest.json` 跟踪。
- `platinum-albedo-source.png` 是内建图像生成产生的无字表面素材。
  `relief-normal.png`、`relief-ao.png`、`relief-roughness.png` 是 Blender Cycles
  从高精度雕刻网格向接收网格执行 selected-to-active 的真实 NORMAL／AO／
  ROUGHNESS 烘焙，**不是 AI 生成的法线／AO／粗糙度图**，也不是从彩色噪声
  随意求导得到的替代品。粗糙度 bake 反映源材质，不宣称额外绘制了变化花纹。
- 画窗、顶部名牌、宝石数字槽与实体镶座承担布局；完整卡名、费用和身材仍
  由可控文字组件绘制，不烘焙进生成图片。卡名不省略，文字不能借用左右雕饰区。
  小尺寸验收要检查实际 GPU 字形、中心、底板深度及斜视状态，不以合同通过代替。

四张运行 PNG 逐项进入总视觉清单；概念图是项目外的设计来源，不混入该运行
图片数量。完整生成 prompt、选中源文件、非 AI bake 过程和修改记录保存在
`assets/visual/anime_v1/card_frame_r1/GENERATION_RECORD.json`。来源记录、素材
清单与 `ASSET_NOTICES.md` 随新导出包提供；不会复制第三方游戏的模型或卡框。

三种变体在同一底部框结中选择真实几何镶纹（日轮／裂隙／圆环），而非另画
三套布局；回池时连各子网格的可见性也统一清除。R1 独立入口使用固定 58°
俯角的正交桌面镜头，安全滚轮范围为 12.7～13.0 世界单位；这是为消除远端
卡牌透视缩小而做的候选构图调整，不是放大单个数字徽章。手牌按实际镜头
投影保持屏幕尺寸，场上整卡 1.16 倍仍在既有格位边界内。默认产品和旧 V2
审阅保留原透视镜头，本候选获认可前不会推广。

## 实测与合成证据必须分开

真实场面的准备 trace 沿用 `user://review-evidence/`，新文件的 suite 为
`real-product-card-frame-review`，并明确 `synthetic=false`。trace 可能含测试
seed、完整开发命令和私密选择，不属于公开 UI 或随包资源。源码 SHA 只记录
操作者声明或基线加工作区，不能据此声称工作区干净或导出可复现。

原 MCP `ReviewDescribe()` 保留状态、revision、揭示后的合法操作坐标等字段，
新增入口标记；它不提前读取下一 viewer。安全与性能探针仍只报告实际读取到的
节点和播放数据，不能宣布视觉批准。

`CardFrameSyntheticSamples` 独立提供零费用／0/0、多位数、受伤、长完整卡名
及无身材法术五组**合成排版数据**。它们不是 `CardView`、命令或 native 状态。
`ReviewDescribeSyntheticSamples()` 是只读数据目录，明确
`synthetic=true, rendered=false, native_session_accessed=false`；返回目录本身
不表示已经渲染、检查了字体或通过了 GPU 验收。将其用于独立视觉测试时必须
显示「合成排版样本 · 非真实对局状态」，并与真实对局截图分开存档。

在已经主动揭示、处于 `Action` 的新卡框入口中，`ProductMatch` 提供以下开发
观察方法；都不提交命令、不读取 session、不 ACK 事件，也不打开 viewer gate：

- `ReviewCardFrameEnvironment()`：记录实际 Godot、项目绝对路径、显卡/API、
  窗口与逻辑 viewport 尺寸。Compatibility 的 `DeviceType.Other` 不是软件
  渲染的充分依据；环境信息本身也不是性能通过结论。
- `ReviewStartCardFrameGlyphCapture()` 与
  `ReviewCardFrameGlyphCaptureResult()`：对当前真实可见的代表卡，取得文字
  开／关两张实际 GPU 图片，再恢复每个标签原来的可见性。每一状态等待两个
  完整绘制帧；viewer、revision、镜头或 actor 绑定变化时中止。记录保存至
  `user://screenshots/card-frame-r1/<capture-id>/`，只属于已揭示的开发截图。
  `measurement_revision=2` 将相机逻辑坐标按实际图片宽高换成物理像素，分别
  输出两套 socket 坐标；旧未缩放 ROI 的全零结果不能当作缺字或通过的证据。
  字形边界来自 on/off 的真实像素差（含描边），不是 Label 的逻辑行 AABB；
  相邻卡遮挡、背景运动和抗锯齿仍需人工检查，不能自动宣布可读性合格。
- `ReviewShowSyntheticCardFrameLayout(key)` 打开独立的 720×960 真实渲染
  viewport。key 为 `zero-follower`、`multi-digit-follower`、
  `wounded-follower`、`long-name` 或 `zero-spell`。它使用同一 R1 卡体，
  但只从合成目录构建视觉值；醒目的合成标题直接进入截图像素。
  `ReviewStartSyntheticCardFrameCapture()` 和
  `ReviewSyntheticCardFrameCaptureResult()` 将图片与独立 manifest 保存至
  `user://screenshots/card-frame-r1/synthetic/<capture-id>/`。绑定样本本身
  不标记 `rendered=true`；实际 GPU 捕获后才记录该值。
- `ReviewHideSyntheticCardFrameLayout()` 或 Esc 关闭样本并清空临时卡体。
  模式／viewer／revision 变化自动关闭，不能覆盖下一位的交接遮挡。合成
  弹层阻断真实战场输入；大尺寸 detail 字体不是最低分辨率场上 16 px 的证明。

真实字形取证、合成 viewport 和静态性能采样互斥，避免用额外窗口改变另一项
验收的数据。对局截图与合成样本始终分开，不自动更新 golden 或视觉批准状态。

`ReviewStartCardFramePoolProbe()` 另启动明确 `synthetic=true` 的 **24 个真实
CardActor3D 回池验证**。六轮预热让每个 actor 绑定三卡与高／低两套 GLB；
之后二十四轮反复执行公开合成卡面 → 清空 → 无身份统一卡背 → 清空。每一
状态等待两个实际绘制帧，同阶段对比真实节点、网格、材质和纹理引用的 ID
指纹，而不是靠固定预期数量宣布没有增长。另一次故意注入合成文字、metadata、
实际 R1 art shader 洋红纹理、旧测试纹理、悬停 callback 和碰撞，再检查清敏。

使用 `ReviewCardFramePoolProbeResult()` 读取结果，或用
`ReviewCancelCardFramePoolProbe()` 取消。报告和标有合成标题的图片写入
`user://review-evidence/card-frame-r1-pool/<capture-id>/`。它不改变真实场面，
不访问 native，结束后释放独立 viewport。该测试与其他三个观察入口互斥；
不是真实满场、不覆盖全部 35 卡，也不能证明托管堆或
Godot 全局缓存绝对不变。全局资源计数单独记录，材质引用检查不包含字体
atlas 与环境 Sky 的内部 GPU 分配。

首次完成预热后的 24 张公开合成卡会额外静止绘制 **600 帧**：前 300 帧预热，
后 300 帧用连续 `FramePostDraw` 的单调时钟间隔记录 p95／max。报告的
`static_heavy.synthetic_heavy=true` 指明这是独立 1440×1200 viewport 叠加
当前实际场景的总帧时间，不是 native 最大场或单独 GPU draw-call 时间。
它不更改用户 VSync／FPS 限制，实际图像尺寸只在计时外通过 GPU readback
取得；测量期间不截屏。资源 ID 在第 299 帧和第 600 帧后对比，全局资源数
逐测量帧记录；不把这两个结构采样点说成逐帧追踪了所有字体 atlas／显存分配。
仅当实际完成时才输出该 600 帧报告，取消不会补足或伪造样本。

### 六秒卡体斜视与光照观察

在已主动揭示、空闲 `Action` 的 R1 场景中，通过 MCP 调用
`ReviewStartCardFrameTurntable("LO-11")`（也接受 `AP-11` 或 `NT-04`），
再用 `ReviewCardFrameTurntableResult()` 读取完成报告。
`ReviewCancelCardFrameTurntable()`、Esc 或界面的结束按钮可以取消。
该入口与字形捕获、合成排版、回池及性能采样互斥；模式、viewer、revision、
窗口尺寸／模式变化及场景退出会中止并释放临时 actor、viewport、计时器与回调。

这是一张公开设计卡的 **720×960 独立 GPU 设计展示**，使用真实 R1 模型、
材质、字体和原插画，但费用／身材来自固定公开印刷设计值，不来自或注入
native session。画面像素始终标有「卡框设计展示 · 非对局状态」。六秒内只
温和改变卡体角度、相机与光照，检查轮廓、切面、表面层次及斜视闪烁；
**不是六秒真实对局动作录像，不证明出牌、施法、进化或命中演出质量**，
也不替代真实手牌／场上截图、最低尺寸数字检查和用户视觉批准。

取证目录为 `user://screenshots/card-frame-r1/turntable/<capture-id>/`。
报告返回 `manifest.json` 和各 PNG 的绝对路径，记录真实 `FramePostDraw`、
`Stopwatch` 单调时钟频率、帧时间戳、显示中的姿态时间、逐帧 SHA-256、实际
采样数量／速率及渲染设备。请求采样上限为 30 fps、最多 180 张，15 秒没有
完成则中止；绘制或 PNG 保存错过的时刻直接跳过，不复制帧伪造恒定帧率。
因此后处理录像应按 manifest 的相邻 `time_seconds` 差值进行 **VFR 编码**，
保留正常时间长度，不能简单把 PNG 列表解释为固定 30／60 fps。

manifest 中的 `model_sha256`／`artwork_sha256` 只在能读取原始 GLB／PNG
源字节时填写；导出 PCK 可能仅保存引擎导入后的 `.scn`／`.ctex` 和 remap，
此时源哈希明确为 `null`，不把导入资源的哈希或空字节哈希冒充源哈希。
玩家包的源资产追溯以随包资产清单与构建审计为准；PNG 帧哈希则始终对应
本次真正保存的画面字节。此节描述能力与证据边界，不代表已经实测完成。

## 验收边界

先检查三张代表卡的正视近照、真实手牌与场上斜视，再确认完整卡名、零值／
多位数、进化异画、隐藏牌与回池材质。真实布局与视觉结果应通过已连接的
Godot MCP 运行、截图、输入和再运行闭环核实。

本说明不宣称新模型已经通过用户质量关、不替代最终测试报告，也不把入口或
资产审计通过写成四尺寸 GPU、全部卡牌、完整动作、跨平台演出或性能验收。
用户认可卡框母版后，才规划扩大覆盖；不因本候选存在就自动开启第二阶段。
