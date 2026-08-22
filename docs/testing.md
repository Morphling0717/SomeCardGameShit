# 测试说明

## 测试层次

### C++ 规则与状态机

覆盖固定牌组规则、失败无副作用、每步不变量、结束回合、响应栈、目标失效、终局幂等、进化充能和先后手种子。测试名称和准确断言数以本次运行输出及 [`TEST_REPORT.md`](../TEST_REPORT.md) 为准。

### 客户端查询契约

客户端 API 必须验证：

- 每项枚举出的合法行动在同 revision 的游戏副本上可以成功提交；
- 支付预览与实际资源变化一致；
- 非法玩家、过期 revision、非法目标和错误阶段无状态/事件/revision 副作用；
- 敌方手牌、背面伏策和抽牌/调度/设置事件不泄露；
- 两个事件游标互不干扰；
- 无界面代理只经快照、查询、命令和事件完成整局，不读取 `PlayerState`。

### C ABI 契约

`scgs_native_api_contract`、纯 C consumer 与动态加载测试必须覆盖：

- C11/C++20 均可包含公开头，导出表不包含 C++ 符号；
- ABI/schema 不匹配、非法 UTF-8/JSON/枚举、空或过期 handle；
- 每个输出的 `NULL + 0`、容量不足、精确容量、NUL 与无部分写入；
- native 状态和规则状态分离，失败命令保持状态、事件和 revision 不变；
- C ABI 与直接 C++ 在相同 seed、相同命令流下语义一致；
- 敌方手牌、背面伏策和隐藏事件不泄露，两个 viewer 游标互不干扰；
- 仅通过 C ABI 的代理完成固定牌组整局。

动态加载测试必须从实际 `scgs_v04.dll` / `libscgs_v04.so` / `libscgs_v04.dylib` 查找符号，不能只静态链接 import target。安装后 smoke 还要从暂存目录编译独立 consumer，以验证交付包而非构建树偶然可用。

### C# ABI 消费边界

纯托管测试必须覆盖 14 个签名与冻结枚举、optional JSON omission、未知字段兼容、结构性枚举拒绝、未知事件/行动降级、未知 keyword bits 保留、严格 UTF-8、两段缓冲增长/NUL/短写/上限、TLS last-error、native/engine 错误分层、SafeHandle 单次销毁、两个 viewer cursor、Windows/macOS 已知原生库布局，以及揭示前零次 `GetView`。

同提交动态库集成测试至少完成 ABI 检查、create/start、双 viewer 快照、全部查询 wrapper、一次合法调度提交、事件脱敏、revision 和 dispose。测试不能读取 `PlayerState`，也不能用模拟 DTO 代替这项集成验证。

### Godot 与桌面导出

Godot headless 验证项目 import、无警告 C# build、四个首轮场景、节点路径、原生加载和第一张真实快照。遮挡测试要求用户揭示前 `GetView` 调用次数为零。CI smoke 固定 seed、强制 Player0、关闭洗牌，并验证场景 Label 来自 DTO 后输出唯一成功标记。

Windows/macOS job 还必须实际导出并启动产物；只在编辑器运行不算通过。Windows 审计 DLL 与 EXE 同目录、x86-64 和静态 CRT；macOS 审计 arm64、`Contents/Frameworks`、ad-hoc codesign 与执行权限。两者 30 秒内退出，日志不得含 C# exception 或 Godot error。

### legacy 兼容性

`scgs_wire_frozen_golden` 固定验证 v1 消息长度、字节序、消息 ID 和金标字节。Python overlay/协议契约测试由 `SCGS_ENABLE_LEGACY_YGO2_TESTS` 控制，默认开启；开启时 CMake 必须找到 Python 3.10+，不能静默只注册部分 CTest。

legacy 测试通过只证明历史兼容层仍可解析，不代表 YGOPro2/Unity 是现行客户端或已经实机可用。

### 压力与 sanitizer

默认压力矩阵为 Release 2,048 seeds 和 Clang ASan/UBSan 256 seeds：

```bash
./scripts/stress.sh
```

可用 `SCGS_RELEASE_STRESS_SEEDS`、`SCGS_ASAN_STRESS_SEEDS` 调整。常规烟雾 seed 数可用 `SCGS_SMOKE_SEEDS` 调整。

## 标准命令

```bash
cmake --preset dev
cmake --build --preset dev
ctest --preset dev

cmake --preset release
cmake --build --preset release
ctest --preset release

cmake --preset asan
cmake --build --preset asan
ctest --preset asan

git diff --check
```

Windows MSVC 使用 `scripts/test.ps1` 或等价的 Release 配置。CI 在 GCC Release、Clang ASan/UBSan、MSVC Release 和 macOS ARM64 Release 四个 job 中固定 Python 版本，并显式设置 `SCGS_ENABLE_LEGACY_YGO2_TESTS=ON`。每个平台还安装并审计原生库，上传仅供 CI 验收的暂存 artifact。

Linux 两个 job 保持纯原生。Windows 与 macOS job 在原生安装审计之后追加 locked managed restore/build/test、Godot headless、目标平台导出、导出包启动与审计；这不构成 Web 或 Linux 客户端支持声明。

## 报告规则

[`TEST_REPORT.md`](../TEST_REPORT.md) 只记录实际执行过的分支、commit、环境、命令、测试/断言数和结果。不得把以下内容写成已通过：

- 未推送分支的 GitHub CI；
- 当前机器无法运行的编译器或 sanitizer；
- 尚未创建的 Godot 工程；
- Godot 编辑器、桌面导出或真人完整对局；
- Web、网络、平衡或正式美术。

测试绿代表已覆盖范围内没有已知失败，不等于 Alpha 全产品验收完成。
