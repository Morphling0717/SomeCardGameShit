# Godot 产品客户端

当前默认入口是两副新牌组的真实 **v05 / schema 2** 热座对局：
誓卫「曜誓骑士团」与契术「渊契魔导院」。使用 AnimeV1 菜单、开放式幻想竞技场、相机相对手牌、一体化卡体和原创动漫卡图。

本分支的交付验证状态以根目录 [TEST_REPORT.md](../../TEST_REPORT.md) 为准。
“能运行”和“完成双人真人平衡验收”不是同一个结论；胜率、终局回合数尚未经过真人调优。

## 试玩

完整解压对应平台包后启动 `SomeCardGameShit.exe`（Windows x64），或
`SomeCardGameShit.app`（macOS ARM64）。不要直接在 ZIP 中运行。
macOS 包仅 ad-hoc 签名，未做 Developer ID 签名与公证；不支持 Intel Mac、Web 或 Linux 正式客户端。

主菜单选择「本地热座」，两席各选择一副牌组，允许相同牌组。普通对局使用随机 seed、随机先手和洗牌。
创建后先交接设备；玩家主动按「揭示画面」才会读取其手牌与私密选项。

操作：

- 调度：点手牌切换，按一次「确认调度」，看清新手牌后交接。
- 随从／护符：点手牌，再点己方主战场空格。两种永久物共享五格。
- 法术／伏策：选择己方三个策略格中的具体空格。法术等待响应期间正面留在该格，结算后送墓。
- 场地：选手牌，再按明确的「展开场地」按钮，有支付选择时按提示完成。它使用独立场地格，替换己方旧场地，不占五格主战场。
- 攻击：选攻击者，再点随从或主战者；也可拖线。
- 进化：选场上随从，再按卡旁「进化」。
- 战备：点己方战备牌堆，选可部署牌，按提示完成额外代价／格位／目标。
- 模式、选牌和触发排序：使用当前公开的选择面板。可选目标与零张选牌有明确「不选择」入口；支付后的选择不能取消。
- 响应：在响应面板选择可发动伏策及必要目标，或按「不过」直接放弃本次响应。
- Esc／右键空白回退选择；没有选择时 Esc 打开暂停。暂停菜单中的投降保留确认，选择／响应期间也可投降；结束回合直接执行。
- 悬停看完整卡名和规则，右键固定详情。Tab／方向键导航，Enter／Space 操作。
- 拖放不是另一套规则：错误归属、区域、目标或过期 revision 会被拒绝；不能拖到对手格位而自动映射为自己的格位。

## 开发：先观察真实运行结果

Godot 4.7.2 .NET、SDK 10.0.400 精确锁定。使用仓库脚本，避免系统 PATH 中缺少 SDK 的 dotnet：

```powershell
pwsh scripts/dev/start_godot_editor.ps1
C:/Users/ASUS/.dotnet/dotnet.exe build client/godot/SomeCardGameShit.csproj -c Debug --no-restore
python scripts/stage_godot_native.py --v05-library build/v05-msvc/scgs_v05.dll --destination-root client/godot/native --target windows-x86_64
```

已配置的 `godot-scgs` MCP 连接当前编辑器。UI、场景、布局、交互和视觉工作必须先
**打开场景 → 检查 live tree／属性 → 运行 → 截图与日志 → 实际输入 → 修改 → 再运行**。
禁止用构建成功或磁盘 tscn 解析代替产品画面验收。具体安装与能力边界见
[Godot Editor MCP](../../docs/godot-editor-mcp.md)。

当前 `Bootstrap` 只启动 v05 产品，不再接受 `--ci-smoke`、`--legacy-2d-board`、
`--r3-visual-slice` 或 `--anime-*` 旧样片入口。
旧报告 validator 继续验证独立历史 fixture，不表示旧产品模式还可使用。
v04 的 ABI 1.0、schema 1 形状、14 导出及 legacy wire 保留；旧牌组创建成功路径退役。
只在测试目标中构建的 `scgs_v04_fixture` 绝不能进入试玩包。

## 验证分层

独立产品整局报告：`product-v05-ui` schema 1；
独立视觉报告：`product-v05-visual` schema 1。
它们不冒用旧 Gate 3C／4A／R2 报告或 golden。

```text
python scripts/ci/run_product_smoke.py --executable <绝对 Godot 路径> --project client/godot --artifact source --coverage full-ui --output <新证据目录> --display
```

`full-ui` 通过真实 Godot 鼠标／键盘事件覆盖 14 种动作、自然终局、投降、重开与释放，
包括错误拖放与回退；不直接发 button signal 或把合法行动列表当成已执行证据。
首局不洗牌，后续必要覆盖局使用固定 seed 排程与轮换先手。
`natural-ui` 用于导出及 ZIP 解包后的真实自然对局启动，不能代替 source 的全动作验收。

增加 `--capture` 后，从真实菜单与设置开始捕获九个必要状态：
菜单、设置、Covered、调度、行动、选择、响应、Resolving、结果；还记录遇到的目标／模式等细分状态。
每张图等待稳定的连续两帧 GPU 输出。四个尺寸：
1280×720、1600×900、2560×1440、2560×1600。

1600×900 增加 `--performance`，在自然形成的双方各至少三格、合计至少八格 Action 战场上
测量 300 帧预热＋300 帧。actor／material／texture 和全局资源不得增长（允许 GC 回收下降），
p95 ≤33.3 ms，单帧 <100 ms。未遇到重场是失败，不拿菜单或空场代替。
证据写入忽略的 `build/` 或 `artifacts/`；CI 不自动更新历史 golden。

## 架构与隐私

`BootstrapController` 只管理菜单、会话生命周期与屏幕切换。
`ProductMatchScreen` 管理状态、提交栅栏及交接；`ProductHotseatMatchController` 从
同 revision 的合法命令派生选择；C++ 是唯一规则真值。
`Battlefield3DPresenter`、`BattlefieldHandRig`、`CardActor3D` 与 HUD 只渲染安全 DTO。

准备命令后进入 `Resolving`，清空 viewer、私密快照、候选、日志、详情、回调、拖拽和隐藏身份，
绘制两帧中立公共战场后才提交。操作者改变则进入全不透明 Covered；新观看者主动揭示前零读取。
事件游标每个观看者独立，规则失败不改 revision，旧 revision 的回调不执行。

## 美术、许可证与导出

当前联合视觉清单 66 项：一个通用正面、14 个原动漫样片资源、23 个卡体组件、
28 张新增卡图。7 张原基础卡图＋28 张新图覆盖 34 个可构筑定义与1个衍生物，
另有两张王牌进化异画。两张头像由公开牌组身份选取主战者母图的缓存头肩裁切。
旧科幻卡图、卡背、头像、菜单和 R3 工业场地已删除，不作为隐藏皮肤保留。

清单、完整生成来源、SHA-256 与许可证随导出包提供。
未知卡使用匿名通用正面，隐藏牌只能使用共享卡背。字体是 Noto Sans CJK SC（OFL）。
原生库不提交 Git，产品包只在目标平台外部位置放 `scgs_v05`。

MCP addon 仅用于开发。通过其 export stripping hook 导出，再执行：

```text
python scripts/dev/check_godot_mcp_export.py --export <新导出目录>
python scripts/audit_godot_export.py --help
```

addon、autoload/plugin 引用、令牌、探针、v04 fixture 或内嵌原生库不得出现在玩家 PCK。
不为本轮加入音频、联机或商业签名。
