# Product Playable v1：新牌组动漫热座验收报告

日期：2026-09-05（Asia/Shanghai）  
分支：`codex/product-playable-v1`  
开发基底：`6e0204e391d0d0b377f7b62d18f1b1fd65d56e81`

## 当前结论

产品入口已切换为 `ProductGame → scgs_v05/schema 2 → Scgs.Client.V05 → ProductHotseatMatchController → ProductMatchScreen`。两副新牌组、14 种动作、私密选择、响应、终局及重开已经在本机真实运行；不再是无 native 的美术样片。

本机 Windows 原生、托管、四尺寸真实 UI、GPU 隐私和正式导出启动已经通过。**尚未记录当前实现提交的远端 CI 成功或 macOS 制品验收，不能沿用历史全绿结论。** ZIP 往返和远端结果在完成后补入。

历史 Gate 5B／6A／卡体 R1 报告完整保留在 [历史测试报告](docs/history/TEST_REPORT-gate5b-6a-card-body-r1.md)。其中旧分支、v04 默认、尚未可玩及旧 CI 时长只描述历史版本。

## 范围与兼容边界

- 34 个可构筑定义＋1 个衍生物使用声明式效果和通用条件；两副精确 30 张主牌、15 种定义、4 张不同公开战备。
- 随从/护符共用五格，法术占己方空策略位，双方独立场地格；可选目标跳过、额外封存代价、选择/响应期间投降均接入产品 UI。
- 旧 `midrange/advance`、旧产品数字定义和科幻美术退役，不提供新牌组别名。正式 v04 保留 ABI/schema 形状、14 导出；成功兼容测试仅由不安装、不打包的 `scgs_v04_fixture` 提供。legacy wire golden 不依赖旧产品牌。
- AnimeV1 是唯一可启动的产品视觉。旧 legacy 2D、R2/R3 与审批切片启动参数受控拒绝；历史报告合同仍使用独立 fixture 验证。
- 不声称胜率 48～52%、赢家 T10～12 或商业美术终审已经完成。双人真人热座、物理 Mac、音频、联机和正式签名仍为后续事项。

## 本机证据

### 原生与托管

逐卡语义、通用能力和修正规则见 [原生证据](docs/product-playable-v1-engine-evidence.md)。Windows 最终原生已通过 28/28 CTest（39.54 秒、Python ON），包括最后一项“每回合限一次”的跨玩家回合重置修复。ProductGame 为 39 cases / 1,397 assertions；v04／fixture／v05 均通过 x64、/MT、精确 14 导出审计。2,048-seed 压力属于独立合成 v04 覆盖，v05 真实固定牌为 32-seed 整局覆盖，二者均不代表平衡验收。

最终托管回归为 166/166、0 skipped（6.116 秒），使用同源码真实 v05 与独立 v04 fixture；Debug/Release C# 构建无警告、无错误。记录为 `build/managed-product-final-results/managed-product-final-166.trx`。全部 Python 脚本测试已通过 251/251（33.757 秒）。

### Godot 编辑器真实观察

本机通过当前 Codex 的 `godot-scgs` MCP 工具完成，不以终端协议探针或磁盘场景解析代替：

1. 确认连接项目为本仓库 `client/godot`，读取 live MainMenu Scene Tree 与 BackgroundBase 属性。
2. 从编辑器启动 Bootstrap；真实鼠标坐标点击本地热座、开始和主动揭示。
3. 读取运行时调度计数，真实点击手牌使选择从 0/4 变为 1/4；观察抬起手牌与完整卡牌详情。
4. 确认调度和调度结果后观察下一玩家完全遮挡；未绕过下一观看者主动揭示。
5. 读取运行日志并停止游戏。此人工 MCP 回路覆盖菜单/设置/调度/换手，不冒称覆盖全部动作。

原始记录：`build/product-mcp-observation.json`；实际游戏截图位于 Godot `user://screenshots/product-final-*.png`。

### 独立 v05 实际输入套件

`build/product-ui-display-1600-02` 已通过：

- 1600×900、真实 GPU 窗口、4 局、157 条命令、3 次重开、4 次释放。
- 14 种 ActionKind 的提交次数依序为 `[8,29,4,2,38,8,4,1,2,41,2,10,3,5]`。
- 两次自然终局、一次不可取消选择期间投降、一次响应期间投降；唯一且最后的 MatchEnded 均检查。
- 错误 owner 拖放、错误 zone 拖放各一次：原生调用（包含查询）、revision、事件游标均不变；真实 Esc 从选目标退回已明确选择的格位且保留来源。
- 真实 Input.ParseInputEvent 与物理射线命中，不以 EmitSignal、直接控制器提交或合法行动枚举补填动作数。
- 结算至少两个真实 FramePostDraw；零越权 viewer 读取、零私密查询、零未归因命令及引擎失败。
- 成功标记只在运行、视觉及隐私检查全部完成后输出一次：`SCGS_PRODUCT_V05_UI_SMOKE_OK`。

该目录保留为真实中间复验，随后新增规则修复和最终打包将使用新目录，不覆盖失败证据或旧报告。

### GPU 隐私与性能

同一次 display 套件，在**已经主动揭示**的真实 v05 手牌 actor 注入恶意文本、纯洋红纹理及回调/drag token，先在 GPU 正检观察到 19,050 个像素，再走正常调度提交：

- Resolving 连续两帧、Covered 连续两帧：私密文字、身份纹理、回调、碰撞、drag token、隐藏牌身份、私密查询与 viewer 读取全部为 0。
- 四张最终 GPU 截图洋红像素均为 0；不保存注入时的私密正检画面。
- 头less 结果只标记 structural-only，不冒充 GPU 验收。
- 真实 4＋4 主战场、300 帧预热＋300 帧测量：actor 44→44、material 80→80、texture 34→34；全局 resource 130→122（正常回收）。
- 每帧均不增长；p95 **5.0789 ms**、最大帧 **56.7236 ms**，满足 ≤33.3 ms／<100 ms。
- 曾发现“资源回收导致总量减少被误判为增长”的测试错误，已修复为每个采样帧不超过预热基线；不放宽任何帧时预算。失败目录保留。

## 执行命令

使用 Godot 4.7.2 .NET、精确 .NET SDK 10.0.400。Windows 默认 PATH 不保证有锁定 SDK，必须设置 `DOTNET_ROOT=C:\Users\ASUS\.dotnet` 并把该目录置于 PATH 首位。

```powershell
cmake --build build/v05-msvc --parallel 2
$env:SCGS_SMOKE_SEEDS = '2048'
ctest --test-dir build/v05-msvc --output-on-failure
python scripts/stage_godot_native.py --v05-library build/v05-msvc/scgs_v05.dll --destination-root client/godot/native --target windows-x86_64
dotnet build client/godot/SomeCardGameShit.csproj -c Debug --no-restore
python scripts/ci/run_product_smoke.py --executable <absolute-godot-exe> --project client/godot --artifact source --coverage full-ui --output <fresh-directory> --display --capture --performance
python scripts/dev/check_godot_mcp_export.py --export <fresh-export-exe>
python scripts/audit_godot_export.py --platform windows-x86_64 --export <fresh-export-exe>
python scripts/ci/run_product_smoke.py --executable <fresh-export-exe> --artifact export --coverage natural-ui --output <fresh-directory> --display
```

不重复运行已退役产品路径的长时间视觉套件。当前产品四尺寸与性能验收独立执行；CI 快速矩阵覆盖 native、managed、v05 源码整局、正式导出与 ZIP 重新解包启动，重型任务专门保存产品视觉/性能结果。

## 最终本机 UI / 导出复验

以下均使用最终 v05 DLL，SHA-256 为 `D125527D4F093434FAA00FFD37F8043772C1F5E7A04C7E7C9E89672BDB96C37A`，每次 4 局、157 条命令、14 种动作、2 次自然终局与 2 次选择/响应投降：

| 实际 GPU 尺寸 | 证据目录（build 下） | 结果 |
|---|---|---|
| 1280×720 | `product-ui-display-1280-fixed-01` | 15 张实际状态图、隐私通过 |
| 1600×900 | `product-ui-display-1600-final-01` | 15 张状态图、隐私、600 帧通过 |
| 2560×1440 | `product-ui-display-2560x1440-final-01` | 15 张状态图、隐私通过 |
| 2560×1600 | `product-ui-display-2560x1600-final-01` | 15 张状态图、隐私通过 |

1600 最终性能：p95 **4.8427 ms**、最大 **53.533 ms**，每帧资源不增长。人工观察发现初始极短攻击箭头退化为盖在卡图上的红块，限制不足箭头头部长的预览后，已在最终 1600 实拍确认消失；正常长距离箭头保留。其他三尺寸的布局与输入证据不是这项极短箭头的最终截图。

1280 复验曾失败：Godot `ViewportTexture.GetSize()` 返回 1024×576，但实际窗口与 GPU 均为 1280×720。检查器改为以真实 Window.Size 为目标并严格比对 GPU，有限等待同步，不重采样图片、不放宽分辨率。2560×1440 的同一 getter 返回 4096×2304，实际 GPU 验收仍要求 2560×1440。

Windows 正式导出位于 `artifacts/product-playable-v1-local/windows-x86_64`，Release 构建无警告。已通过 EXE/x64、v05 精确14导出及 /MT、许可证/清单、PCK 282 条目、project.binary 20 项设置及 global script class cache 审计：没有 MCP addon/autoload/配置/令牌/探针，也没有嵌入或混入旧原生库。实际 EXE 的 `natural-ui` 经 39 条真实输入命令自然结束，成功标记一次；日志没有 MCP server 启动，记录为 `build/product-export-natural-01`。导出测试子进程会清除继承的原生覆盖环境，不偷偷加载工作区 DLL。

## 待补的远端交付记录

- Windows ZIP 往返启动及摘要。
- 实现提交 SHA 与四项远端 CI，macOS ARM64 的实际构建/签名/启动结果。
- 最终文档尖端复验。
