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
- [x] MSVC、GCC、Clang sanitizer 与 macOS ARM64 CI

Gate 2 采用 64 位 token handle 与 schema 1 UTF-8 JSON，不直接展平含 string/vector/optional 的 C++ DTO。规范见 [`native-api-v04.md`](native-api-v04.md)。

## Gate 3：Godot 热座 Alpha — 后续

- [ ] 创建 Godot 4.7.2 .NET 工程和桌面导出预设
- [ ] C# P/Invoke 与原生库加载
- [ ] 调度、热座遮屏、出牌、攻击、进化、部署、响应、结束和投降 UI
- [ ] Windows x86-64 与 macOS Apple Silicon 实机完整一局
- [ ] 仅用原创或明确授权的正式素材

Gate 2 不创建场景、不实现正式 UI/美术，也不承诺 Web。

## Alpha 后续

- [ ] 主战技与普通主动能力 UI
- [ ] 同时触发人工排序
- [ ] 固定牌组未使用关键词验收
- [ ] 异地联机、录像与版本化网络协议
- [ ] 卡组编辑、内容扩展、平衡与真人数据

YGOPro2/Unity 路线已经停止投入；legacy 代码只为协议回归和历史研究保留。
