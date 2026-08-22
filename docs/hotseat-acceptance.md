# Gate 3C 直接交互热座验收

本清单把 Gate 3B 已验收基线、Gate 3C 源码契约、自动化结果与真人/物理设备验收分开记录。勾选只能依据同一提交的实现、测试日志或人工记录；准确运行数量与远端 CI 状态只以 [`../TEST_REPORT.md`](../TEST_REPORT.md) 为准。

## 必须保留的 Gate 3B 基线

- [x] Godot 4.7.2 .NET、.NET SDK 10.0.400、locked restore 与 Windows x64/macOS arm64 导出链；
- [x] `Scgs.Client` 的 ABI 1.0/schema 1/14 导出、安全 JSON/UTF-8/handle 和 native/engine 错误分层；
- [x] `Scgs.Hotseat` 只依赖 `IScgsGameSession`，Godot 不复制规则；
- [x] 双方调度、行动、响应、终局/重开源码闭环和两位 viewer 独立事件 cursor/ACK；
- [x] 初始及换手完全遮挡，主动揭示前新 viewer 调用为零；
- [x] 自己手牌完整、对方手牌与背面伏策脱敏；
- [x] native、managed、Godot 整局、桌面导出、ZIP 往返审计与启动回归均继续保留。

Gate 3B 的历史通过不能证明 Gate 3C 的交互或公共投影安全；Gate 3C 必须在最终尖端重新跑完整矩阵。

## Gate 3C 源码契约

### 选择与命令

- [x] `HotseatUiMode.Resolving` 与 `Covered` 分离；选择公开 `HotseatSelectionStep` 和 `HotseatInteractionContext`；
- [x] 来源、动作、目标、格位、组件与预支只过滤同 revision 的引擎 `LegalAction`，不拼第二套命令；
- [x] 点击与拖拽进入同一 intent，最终得到逐字段相等的 `GameCommandRequest`；
- [x] 第一次点击来源不提交；单一动作直接进入下一选择，多动作才显示来源旁上下文按钮；
- [x] 最后一个必要目标/格位/组件选择后立即准备命令，不出现通用确认页；
- [x] 无目标动作要求明确动作按钮；调度整批确认、投降二次确认、结束回合直接执行；
- [x] `StepBackSelection` 逐个撤销显式步骤，自动补全不占历史；完整取消才清空来源；
- [x] 无效拖放不调用 native，不改变 revision、事件或游标；
- [x] 支付提示完全来自 `PreviewPayment`，预支/燃耗/容量/裂痕变化醒目但不追加确认。

### 战场输入与反馈

- [x] 手牌、场上单位/策略、战备、主战者与空位可直接点击或拖拽，高亮不只依赖颜色；
- [x] 攻击显示目标连线，放置显示幽灵牌，部署显示组件及成本；
- [x] 右栏只承载可折叠详情/日志，不再列出主行动流程；
- [x] 悬停显示详情，右键固定详情或在空白处回退，Esc 与键盘确认路径可用；
- [x] 响应以居中上下文层显示公开 origin、合法伏策/目标与“不过”。

### Resolving 与热座隐私

- [x] 命令准备后先绘制至少两个完整帧的 `HotseatPublicBoardView`，期间输入、ACK、重复提交和旧 revision 回调全部锁死；
- [x] 公共投影中双方手牌只有数量，所有背面伏策匿名，且没有详情、日志、tooltip、metadata、候选或私密回调；
- [x] `Resolving` 内 `Snapshot`、`Viewer`、`LegalActions` 与 `PendingEvents` 为空，不保留 viewer DTO/节点引用；
- [x] 同一操作者继续时才刷新原 viewer；操作者变化立即完全遮挡，新 viewer 主动揭示前调用为零；
- [x] engine 拒绝回到刷新后的原 viewer 状态，native/协议故障先清私密状态再进入受控错误页。

## 被测实现自动化

以下项目依据实现提交 `087d53a` 的本地输出与远端 run `32592594368` 勾选；包含本报告的文档尖端仍须由同一工作流再次全绿：

- [x] GCC Release、Clang ASan/UBSan、MSVC Release 与 AppleClang ARM64 四项原生矩阵全绿；
- [x] 2,048-seed Release、256-seed sanitizer、legacy wire/Python、精确 14 导出和 `git diff --check` 通过；
- [x] managed 测试覆盖每种动作的选择步骤、自动补全、逐步回退、规范命令收敛、支付与公共投影隐私；
- [x] Godot 通过真实控件 signal 覆盖调度、出牌、攻击、进化、部署、伏策发动/不过、结束回合、投降、终局和重开；
- [x] 点击/拖拽路径产生相同规范命令；最后必要选择后无通用确认；
- [x] 恶意私密 DTO 不能泄露到 `Resolving` 的截图、节点 metadata、tooltip 或回调；
- [x] Gate 3C schema v2 报告严格通过：`action_kinds` 为 0～10、`gate="3C"`、三项直接交互布尔为 true、`resolving_public_frames>=2`、两类泄露计数为 0、`restarts>=1`、`surrender_terminals>=1`、`disposed_sessions>=2`；
- [x] 当前工程、Windows/macOS 导出及 ZIP 解包后启动各只输出一次 `SCGS_GODOT_CI_SMOKE_OK`；
- [x] artifact 使用 `SomeCardGameShit-gate3c-windows-x86_64` 与 `SomeCardGameShit-gate3c-macos-arm64`，架构、native 布局、许可证和摘要已记录。

## 视觉与人工验收

- [ ] 1600×900 与 1280×720 的主要选择/响应/结算/换手状态无重叠，中文完整可读；
- [ ] 两名真人完成一局并确认直接操作可理解、公共结算不泄露、每次实际换手完全遮挡；
- [ ] 物理 Apple Silicon Mac 完成整局、退出和重开；
- [ ] 未安装 Visual Studio 的 Windows x86-64 机器启动导出包并完成整局；
- [ ] 人工发现的问题加入回归并重跑完整矩阵；以上完成后才允许 `v0.4-hotseat-alpha.1` 标签。

## Gate 3C 之外

正式卡图/音效/复杂动画、触摸/手柄、主战技、普通主动能力、同时触发人工排序、固定牌组未使用关键词、独立正式表现 JSON、Developer ID 签名/公证、Web/Linux 正式客户端、联机、录像和卡组编辑均延后。同一玩家同时触发继续使用确定性场地顺序，并作为 Alpha 限制公开记录。
