# Godot 热座客户端（Gate 3B）

此目录是 Godot 4.7.2 .NET、`net8.0` 的桌面热座客户端。Godot 层引用：

- `../Scgs.Client/Scgs.Client.csproj`：安全 ABI、DTO 与 `IScgsGameSession`；
- `../Scgs.Hotseat/Scgs.Hotseat.csproj`：无 Godot 依赖的热座状态机与命令编排。

两个纯托管项目同时生成 `net8.0` / `net10.0`；Godot 使用 `net8.0`，测试使用 `net10.0`。C++ 引擎仍是费用、目标、响应、状态和胜负的唯一规则真值。

## 本地运行

必须使用根目录 `global.json` 锁定的 .NET SDK 10.0.400 与 Godot 4.7.2 .NET。将当前提交构建并审计后的动态库放入：

- `native/windows-x86_64/scgs_v04.dll`
- `native/macos-arm64/libscgs_v04.dylib`

也可通过环境变量 `SCGS_NATIVE_LIBRARY`，或 Godot 用户参数 `--native-library=<绝对路径>` 指定库。只接受绝对路径；不会搜索当前目录或任意 `PATH`。随后用 Godot 4.7.2 .NET 打开本目录。

启动页会创建并立即释放一个未开局 session，以验证动态库、ABI 1.0 与 schema 1。预检失败时不会开始比赛或读取任何玩家快照。正式客户端目标仅为 Windows x64 与 macOS arm64；不支持 Web，也不承诺 Linux 正式客户端。

## 对局流程

1. 两席分别选择 `midrange` 或 `advance`（允许相同牌组）；
2. 产品配置省略 seed、随机决定先手并洗牌；
3. create/start 后先显示完全不透明的“请交给玩家 0”；
4. 玩家主动揭示后才请求其观看者快照；
5. 双方依次选择调度牌，提交后先查看自己的替换手牌，再遮挡交接；
6. 通过引擎合法行动完成出牌、目标/位置/组件/预支选择、支付确认、攻击、进化、部署、伏策响应/不过、结束回合和投降；
7. 操作者变化时重新完全遮挡，下一位主动揭示前不读取其 view/query/events；
8. 终局可重开或返回菜单；重开会先释放旧 session。

任何命令都先进入 `Covered(ResolvingCommand)` 并清除敏感 UI；Godot 等待遮挡完成至少一个完整绘制周期后才延迟提交。不要把这条两阶段顺序改成同步提交。

## 事件与隐私

- Player0 与 Player1 各有独立事件 cursor；
- `ReadEvents` 非破坏读取，事件完成日志渲染后才调用 ACK；
- 对手手牌只显示数量与无身份牌背；
- 对手背面伏策不保存 definition/instance ID、卡名 tooltip 或稳定 metadata；
- 遮挡会清除快照引用、手牌节点、卡牌详情、候选和敏感日志；
- 快照是状态真值，事件只用于日志/表现。

## CI smoke

基础入口使用真实原生库、固定 seed、强制 Player0 且关闭洗牌：

```text
godot --headless --path client/godot -- --ci-smoke --native-library=<绝对路径>
```

成功时只允许输出一次 `SCGS_GODOT_CI_SMOKE_OK` 并以 0 退出；缺库、错架构、ABI/schema 不符、C# exception 或 Godot error 必须失败。CI 还会在压缩前及 zip 解包后分别审计/启动导出包。Gate 3B 的具体场景、报告文件、超时和真实结果以仓库 CI 工作流及根目录 `TEST_REPORT.md` 为准。

本地视觉验收可额外传入 `--ci-screenshot=<绝对 PNG 路径>`；该参数只允许与 `--ci-smoke` 一起使用。

## 素材与许可证

卡框、图标和颜色均为原创占位几何。唯一新增二进制素材是 Noto Sans CJK SC 2.004 Regular，许可证、NOTICE 和 SHA-256 在 `assets/fonts/` 中。桌面导出还附带项目 GPL、Godot、.NET runtime、nlohmann/json、Noto 与第三方声明；finalize 与制品审计会强制检查。

Gate 3B 不包含正式卡图/音效/动画、独立正式表现 JSON、主战技、普通主动能力、同时触发人工排序、Developer ID 签名/公证、Web 或 Linux 正式客户端。
