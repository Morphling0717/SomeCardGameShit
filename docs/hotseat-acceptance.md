# Gate 3B 热座 Alpha 验收

本清单把“源码已接入”“自动化已通过”和“真人/物理设备已验收”分开记录。勾选只能依据同一提交上的实现、测试日志或人工记录；不能用计划文字、headless smoke 或 CI 架构审计代替真人热座与物理 Mac 验收。准确运行结果以 [`../TEST_REPORT.md`](../TEST_REPORT.md) 为准。

## 已建立的 Gate 3A 基线

- [x] `.NET SDK 10.0.400`、Godot 4.7.2 .NET、locked restore 与桌面导出工具链；
- [x] `Scgs.Client` 的 14 个 ABI 绑定、严格 UTF-8/缓冲边界、schema 1 DTO、SafeHandle 和 native/engine 错误分层；
- [x] Windows x64 / macOS arm64 同提交原生库暂存、导出布局、架构与许可证审计；
- [x] create/start 后先遮挡，首次主动揭示前不读取 Player0 快照；
- [x] 自己手牌完整、对方手牌与背面伏策脱敏的首张真实快照。

Gate 3A 的历史通过结论不自动证明 Gate 3B 的完整交互仍然通过；Gate 3B 必须重新跑完整矩阵。

## Gate 3B 源码契约

### 编排与隐私

- [x] 新增无 Godot 依赖、双 TFM（`net8.0` / `net10.0`）的 `Scgs.Hotseat`；
- [x] `HotseatMatchController` 只依赖 `IScgsGameSession`，Godot 不复制费用、目标、响应或胜负规则；
- [x] `Covered`、`MulliganSelecting`、`MulliganReview`、`Action`、`Reaction`、`Finished`、`Faulted`、`Disposed` 有明确状态；
- [x] 调度、普通行动和响应都采用“准备命令 → 清敏感数据并完全遮挡 → Godot 延迟提交”的两阶段路径；
- [x] 操作者变化后不读取新 viewer，必须等下一位主动揭示；
- [x] 对方手牌/背面伏策的 UI 节点不保存身份、tooltip 或稳定 metadata；
- [x] 每位 viewer 独立保存事件 cursor，事件渲染完成后显式 ACK 才推进；
- [x] 返回菜单、错误恢复与重开路径释放旧 session，重复 dispose 安全。

### 完整操作闭环

- [x] 双方可选择任意起手牌调度，并在提交后查看自己的替换手牌再交接；
- [x] 手牌、单位、策略、战备、主战者和空位均可由 DTO/合法行动候选驱动选择；
- [x] 单位、法术、设施、伏策、攻击、结束回合与投降使用统一 `GameCommand` 提交；
- [x] 目标、单位/策略位置、部署组件来源与预支选择逐步缩小引擎候选；
- [x] 支付确认显示 PP 及有变化的容量/裂痕/进化能量，并标出燃耗/预支构成，且只接受同 revision 预览；
- [x] 进化、部署和组件选择走合法查询，不在 C# 重算条件；
- [x] 响应页显示公开 origin、当前 responder、可发动伏策与“不过”；反制后的下一 responder 重新遮挡；
- [x] 终局显示胜负/平局，提供重开和返回菜单；规则错误、native 错误与协议错误分层展示；
- [x] 中文行动、事件与规则错误文本由 DTO/冻结 code 映射生成，未知未来值有受控降级。

### 引擎客户端安全补强

- [x] `PaymentPreview` 是严格费用投影，不执行卡牌效果，也不通过隐藏伏策改变结果；
- [x] 响应上下文公开 `ReactionOrigin`，pending 必有、非 pending 省略；
- [x] ABI 仍为 1.0、schema 仍为 1、导出仍精确 14 个，legacy v1 wire 字节不变。

## 自动化复验（本分支必须重新完成）

以下项目只有在最终提交上有真实输出后才能勾选；不要在 CI 尚未结束时提前修改：

- [ ] MSVC Release、GCC Release、Clang ASan/UBSan 与 AppleClang ARM64 的原生矩阵全绿；
- [ ] 2,048-seed Release 与 256-seed sanitizer 压力、legacy wire/Python、精确 14 导出和 `git diff --check` 通过；
- [ ] managed 单元测试覆盖两阶段遮挡、调度 review、渐进候选、支付一致、stale revision、响应换手、独立 cursor/ACK、未知值与 dispose；
- [ ] 同提交真实动态库测试只经安全接口完成确定性整局，并覆盖终局/重开与双 viewer 隐私；
- [ ] Godot 冷导入、四个主场景及新增面板/overlay 实例化无 C# exception/Godot error；
- [ ] 当前工程与 Windows/macOS 导出各自只输出一次 `SCGS_GODOT_CI_SMOKE_OK`，并生成通过严格 schema 校验的 Gate 3B 报告；
- [ ] 导出包完成解包后再次审计和真实启动，不只检查压缩前目录；
- [ ] Windows x86-64 与 macOS arm64 Gate 3B artifact 名称、架构、native 布局、许可证和 SHA-256 已记录。

## 必跑的 Gate 3B 场景

自动化报告与人工观察合并后至少覆盖：

- [ ] `privacy-mulligan`：揭示前零 viewer 读取、两席调度、替换手牌 review、每次交接全遮挡；
- [ ] `full-match`：固定牌组从 Mulligan 打到唯一终局，包含普通出牌、攻击、结束回合、投降或致命结果；
- [ ] `resources`：预支、燃耗、裂痕/修复/增长及支付预览与实际资源变化一致；
- [ ] `evolve-deploy`：进化、战备部署、位置和组件来源选择；
- [ ] `reaction`：设施/伏策、公开 origin、发动/不过、反制换手和 LIFO 结果；
- [ ] `terminal-restart`：结果遮挡、旧 session 释放、随机配置重开、返回菜单和受控错误恢复。

## 发布标签前硬门（当前未完成）

- [ ] 在物理 Apple Silicon Mac 上运行未正式签名的 arm64 `.app`，完成整局、退出并重开；
- [ ] 两名真人在目标桌面构建上完成一局，逐次确认“遮挡 → 交接 → 主动揭示”，无旁观者手牌/伏策/日志泄露；
- [ ] 至少一台未安装 Visual Studio 的 Windows x86-64 机器验证导出包可启动并完成整局；
- [ ] 对真人测试发现的问题完成回归并重跑自动化矩阵；
- [ ] 上述硬门完成后才允许创建 `v0.4-hotseat-alpha.1` 标签。

## 明确不在 Gate 3B

主战技、普通主动能力、同时触发人工排序、固定牌组未使用关键词、正式卡图/音效/动画、独立正式表现 JSON、Developer ID 签名与公证、Web/Linux 正式客户端、联机、录像和卡组编辑均延后。同一玩家的同时触发继续使用确定性场地顺序，并作为 Alpha 限制公开记录。
