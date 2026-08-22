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

Windows MSVC 使用 `scripts/test.ps1` 或等价的 Release 配置。CI 在 GCC Release、Clang ASan/UBSan 和 MSVC Release 三个 job 中固定 Python 版本，并显式设置 `SCGS_ENABLE_LEGACY_YGO2_TESTS=ON`。

## 报告规则

[`TEST_REPORT.md`](../TEST_REPORT.md) 只记录实际执行过的分支、commit、环境、命令、测试/断言数和结果。不得把以下内容写成已通过：

- 未推送分支的 GitHub CI；
- 当前机器无法运行的编译器或 sanitizer；
- 尚未创建的 C ABI/Godot 工程；
- Godot 编辑器、桌面导出或真人完整对局；
- Web、网络、平衡或正式美术。

测试绿代表已覆盖范围内没有已知失败，不等于 Alpha 全产品验收完成。
