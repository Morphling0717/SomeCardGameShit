# Godot 热座客户端（Gate 4A / 4A.1）

此目录是 Godot 4.7.2 .NET、`net8.0` 的桌面热座客户端。产品默认使用 3D/2.5D 占位战场；legacy 2D 仅保留为隐藏回归路径。法术没有中央施放区，必须点击或拖到当前玩家自己的具体空策略位；三个位置全满时不可施法。Godot 层引用：

- `../Scgs.Client/Scgs.Client.csproj`：安全 ABI、DTO 与 `IScgsGameSession`；
- `../Scgs.Hotseat/Scgs.Hotseat.csproj`：无 Godot 依赖的热座状态机与命令编排。

两个纯托管项目同时生成 `net8.0` / `net10.0`；Godot 使用 `net8.0`，测试使用 `net10.0`。C++ 引擎仍是费用、目标、响应、状态和胜负的唯一规则真值。

## 本地运行

必须使用根目录 `global.json` 锁定的 .NET SDK 10.0.400 与 Godot 4.7.2 .NET。将当前提交构建并审计后的动态库放入：

- `native/windows-x86_64/scgs_v04.dll`
- `native/macos-arm64/libscgs_v04.dylib`

也可通过环境变量 `SCGS_NATIVE_LIBRARY`，或 Godot 用户参数 `--native-library=<绝对路径>` 指定库。只接受绝对路径；不会搜索当前目录或任意 `PATH`。随后用 Godot 4.7.2 .NET 打开本目录。

启动页会创建并立即释放一个未开局 session，以验证动态库、ABI 1.0 与 schema 1。预检失败时不会开始比赛或读取任何玩家快照。正式客户端目标仅为 Windows x64 与 macOS arm64；不支持 Web，也不承诺 Linux 正式客户端。

不传表现参数时始终创建默认 3D presenter。只有测试或排障时可以传入精确参数 `--legacy-2d-board`；它不会出现在主菜单，也不能用于替代 3D 产品验收：

```text
godot --path client/godot -- --legacy-2d-board --native-library=<绝对路径>
```

## 对局流程

1. 两席分别选择 `midrange` 或 `advance`（允许相同牌组）；
2. 产品配置省略 seed、随机决定先手并洗牌；
3. create/start 后先显示完全不透明的“请交给玩家 0”；
4. 玩家主动揭示后才请求其观看者快照；
5. 双方依次选择调度牌，提交后先查看自己的替换手牌，再遮挡交接；
6. 点击或拖拽手牌/场上来源，直接选择高亮的目标、具体格位或部署组件；复杂分支与响应使用贴近来源的上下文按钮；
7. 操作者变化时重新完全遮挡，下一位主动揭示前不读取其 view/query/events；
8. 终局可重开或返回菜单；重开会先释放旧 session。

目标、格位或组件是命令选择的一部分：最后一个必要选择完成后直接提交，不再出现通用确认页。无目标动作必须再按一次明确的动作按钮；调度保留一次整批确认，投降保留二次确认，结束回合直接执行。

命令准备后进入不可交互的 `Resolving`，显示环境先经过至少两次 `FramePostDraw`（headless 使用两次 process-frame 栅栏）的中立公开战场，再延迟提交。此投影不引用 viewer 快照：双方手牌仅保留数量，所有背面伏策统一匿名，详情、日志、tooltip、metadata、候选和回调全部清除。只有初始揭示与实际操作者变化才进入完全不透明的 `Covered`；下一位主动揭示前不得读取其 view/query/events。

鼠标操作同时支持“点击来源 → 点击目的地”和拖拽。拖拽只是同一 intent 的快捷方式，不能产生不同命令；无效拖放原位回弹且不调用 native。Esc 或右键空白回退一个显式选择步骤，再次取消才清空来源；悬停显示详情，右键可固定详情。

3D presenter 使用 70° FOV、约 58° 俯角和 8 px 拖拽阈值。HUD 位于独立 CanvasLayer，命中 HUD 时不得继续发射空间射线；viewer 透视只在完全遮挡内切换。卡牌 actor 归还池时清除文字、材质、tooltip、metadata、碰撞、callback 和拖拽 token，防止跨 viewer 复用泄露。

## 事件与隐私

- Player0 与 Player1 各有独立事件 cursor；
- `ReadEvents` 非破坏读取，事件完成日志渲染后才调用 ACK；
- 对手手牌只显示数量与无身份牌背；
- 对手背面伏策不保存 definition/instance ID、卡名 tooltip 或稳定 metadata；
- `Resolving` 公共投影不包含任何 viewer 私有对象；完全遮挡会进一步清除全部可见战场并等待主动揭示；
- 快照是状态真值，事件只用于日志/表现。

## CI smoke

基础入口使用真实原生库、固定 seed、强制 Player0 且关闭洗牌：

```text
godot --headless --path client/godot -- --ci-smoke --native-library=<绝对路径>
```

成功时只允许输出一次 `SCGS_GODOT_CI_SMOKE_OK` 并以 0 退出；缺库、错架构、ABI/schema 不符、C# exception 或 Godot error 必须失败。严格 Gate 4A schema v3 报告继承 Gate 3C 的全部真实 signal 两局闭环、11 种 `ActionKind`、选择、隐私、重开、投降和释放约束；默认 3D 还验证真实 surface/raycast、HUD 拦截、固定镜头/阈值、透视重建、actor 池复用、锁定输入和零空间私密泄露。

两平台 CI 都运行默认 3D 当前工程、默认 3D 导出与默认 3D ZIP 往返；另各运行一次隐藏 legacy 2D 源码回归：

```text
godot --headless --path client/godot -- --ci-smoke --legacy-2d-board \
  --native-library=<绝对路径> --ci-report=<绝对路径>
```

legacy 报告必须证明仍经过共享 surface intent，同时不伪报 raycast、镜头、透视或 actor 池证据。真实结果以根目录 `TEST_REPORT.md` 为准。

本地视觉验收可额外传入 `--ci-screenshot=<绝对 PNG 路径>`；该参数只允许与 `--ci-smoke` 一起使用，并在恶意私密哨兵被清除后截取首个 `Resolving` 完整绘制帧。默认 headless smoke 等待两个 process-frame 栅栏，不生成截图。

## 素材与许可证

卡框、图标和颜色均为原创占位几何。唯一新增二进制素材是 Noto Sans CJK SC 2.004 Regular，许可证、NOTICE 和 SHA-256 在 `assets/fonts/` 中。桌面导出还附带项目 GPL、Godot、.NET runtime、nlohmann/json、Noto 与第三方声明；finalize 与制品审计会强制检查。

Gate 4A / 4A.1 不包含正式卡图/音效/复杂动画、独立正式表现 JSON、主战技、普通主动能力、同时触发人工排序、触摸/手柄、Developer ID 签名/公证、Web 或 Linux 正式客户端。
