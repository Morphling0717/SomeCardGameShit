# 路线图

## Gate 0：基线整理 — 已完成

- [x] README、架构、路线图、测试说明和交接文档统一为 v0.4 + Godot 路线
- [x] YGOPro2/Unity 文档与源码标为 legacy 历史参考
- [x] 移除一次性 M1 importer 和 marker，保留远端 M1 分支与 overlay/upstream
- [x] legacy protocol/delta 文档按实际投影语义纠偏，wire 字节不变
- [x] CMake 最低版本统一为 3.25
- [x] legacy Python 测试开关默认开启，开启时强制 Python 3.10+
- [x] CI 显式开启 legacy 测试并固定 Python
- [x] 锁定 Godot 4.7.2 .NET 与 .NET SDK 10.0.400，明确不支持 Web
- [x] 由本次最终实测填写 `TEST_REPORT.md` 的准确命令、断言数与 CI 状态

## Gate 1：引擎客户端化加固 — 已完成

### 规则与状态机

- [x] 结束回合 PP 清零及事件顺序
- [x] 三层响应真正 LIFO、反制过牌、法术声明响应和无悬空引用
- [x] 支付前完整目标验证，响应中目标失效只跳过依赖效果
- [x] 幂等终局，终局后无抽牌、倒计时或其他状态变化
- [x] 部署允许将即将封存的组件单位位置作为目标格
- [x] 所有公开命令安全拒绝非法玩家枚举
- [x] 进化解锁前不职业充能；解锁固定获得先手 2 / 后手 3；解锁后充能封顶 4
- [x] 随机/强制先手模式，seed 和实际先手进入快照与事件

### 客户端安全 API

- [x] `GameCommand` + `ActionKind` 统一命令入口
- [x] `MatchView` 观看者快照和单调 revision
- [x] 合法行动、目标、位置、组件来源、支付和响应上下文查询
- [x] 非破坏性、观看者脱敏的事件游标
- [x] 查询与执行共享验证，成功命令 revision +1，失败无副作用
- [x] 无界面代理只经“快照 → 查询 → 命令 → 事件”完成固定牌组整局

勾选状态应由最终实现和测试结果更新，不以计划文字代替验收。

## Gate 2：版本化 C ABI — 已完成

- [x] 设计不暴露 C++ STL/异常/类布局的版本化 ABI
- [x] 版本化 JSON、调用方所有的两段式缓冲区、错误码和 UTF-8 验证
- [x] C ABI 与直接 C++ 行为对照测试
- [x] C11 consumer、动态加载与导出表审计
- [x] Windows DLL、Linux so 与 macOS ARM64 dylib 安装/打包
- [x] GCC Release、Clang ASan/UBSan、MSVC Release 与 macOS ARM64 Release 四个 CI job

Gate 2 采用 64 位 token handle 与 schema 1 UTF-8 JSON，不直接展平含 string/vector/optional 的 C++ DTO。规范见 [`native-api-v04.md`](native-api-v04.md)。

## Gate 3A：Godot 桌面骨架与首张安全快照 — 已完成

- [x] 精确锁定 .NET SDK 10.0.400 与 Godot 4.7.2 .NET，使用 locked restore
- [x] `Scgs.Client` 双 TFM、完整 14 导出绑定、安全 handle/缓冲/JSON/错误/事件游标
- [x] 创建 `Bootstrap`、`MainMenu`、`Match`、`PassDeviceOverlay` 与桌面导出预设
- [x] 两席固定牌组选择、首次完全不透明换手遮挡及揭示后第一张真实 viewer 快照
- [x] 结构化只读战场显示双方公开状态、己方手牌身份与对方牌背
- [x] Windows x86-64 与 macOS ARM64 导出、架构/许可证审计及导出程序启动 smoke
- [x] GCC、Clang sanitizer、MSVC/Godot 与 macOS ARM64/Godot 四项 CI 全绿

Gate 3A 的历史被测界面只显示 Mulligan 首张快照，不提交任何 `GameCommand`；同提交动态库的托管集成测试会提交一次合法调度命令。界面使用 Noto 授权字体和纯色几何占位，不宣称已有正式美术。

## Gate 3B：完整热座 Alpha — 实现已接入，验收进行中

### 客户端闭环

- [x] 新增 Godot 无关、`net8.0` / `net10.0` 双 TFM 的 `Scgs.Hotseat` 编排层
- [x] 调度选择、替换手牌 review、双方持续换手和两阶段遮挡提交
- [x] 普通出牌、攻击、结束回合与投降
- [x] 目标/位置/组件/预支渐进选择和严格支付预览
- [x] 进化、部署、设施、伏策设置、反制/不过与响应 origin 展示
- [x] 每位 viewer 独立的非破坏事件读取，Godot 渲染后显式 ACK
- [x] 对局结果、返回菜单、重开与受控错误恢复
- [x] DTO 驱动的中文行动/事件/错误展示和无身份对手牌背

### 同轮引擎与制品加固

- [x] 支付预览与实际支付共享费用投影，不执行效果、不形成伏策侧信道
- [x] pending 响应提供公开 `ReactionOrigin`，ABI 1.0/schema 1/14 导出与 legacy v1 wire 不变
- [x] CI 脚本要求唯一 smoke 标记、Gate 3B 报告 schema、压缩包解包后复审与真实启动
- [ ] Gate 3B 最终提交的 Windows/macOS 导出与四项 CI 结果写入 `TEST_REPORT.md`

### 发布标签前硬门

- [ ] 物理 Apple Silicon Mac 完成启动、整局、退出与重开
- [ ] 两名真人在目标桌面构建完成热座整局并逐次检查遮挡/交接
- [ ] 未安装 Visual Studio 的 Windows x86-64 机器完成导出包整局验证
- [ ] 完成硬门后才标记 `v0.4-hotseat-alpha.1`

Gate 3B 使用 Noto 授权字体与原创纯色几何，不增加正式卡图、音效、动画或第二套规则/表现数据。自动 smoke 与 CI 不能替代上述物理设备和双人验收。Gate 3B 仍不承诺 Web 或 Linux 正式客户端；Developer ID 签名与公证也尚未完成。

## Alpha 后续

- [ ] 主战技与普通主动能力 UI
- [ ] 同时触发人工排序
- [ ] 固定牌组未使用关键词验收
- [ ] 正式卡图、音效、动画与独立表现数据
- [ ] 异地联机、录像与版本化网络协议
- [ ] 卡组编辑、内容扩展、平衡与真人数据

YGOPro2/Unity 路线已经停止投入；legacy 代码只为协议回归和历史研究保留。
