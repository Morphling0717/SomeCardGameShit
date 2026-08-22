# 工具链基线

本仓库当前锁定以下客户端工具链：

| 工具 | 锁定版本 | 用途 |
|---|---:|---|
| CMake | 3.25 或更高 | C++ 配置与构建；`CMakePresets.json` schema 6 以 3.25 为最低版本 |
| C | C11 | `scgs_v04` 公共头与原生 consumer 测试 |
| C++ | C++20 | 权威规则引擎与 C ABI 适配层 |
| Python | 3.10 或更高 | legacy YGOPro2 overlay 与协议契约回归测试 |
| Godot | **4.7.2 .NET** | Gate 3A 桌面工程、headless smoke 与导出 |
| .NET SDK | **10.0.400** | C# 绑定、测试与 Godot 项目；由根目录 `global.json` 精确锁定 |

正式客户端目标仅为 Windows x86-64 与 macOS Apple Silicon。本阶段明确不支持 Web；Godot .NET 的 Web 导出不属于 alpha 承诺，也不得在文档或 CI 中宣称已支持。

Gate 2 构建通过 CMake `FetchContent` 固定 `nlohmann/json` 3.12.0 归档与 SHA-256，不依赖开发机全局包。输出目标为 Windows x86-64 `scgs_v04.dll`、Linux `libscgs_v04.so` 与 macOS Apple Silicon `libscgs_v04.dylib`；公开 ABI 与 JSON schema 见 [`native-api-v04.md`](native-api-v04.md)。

Gate 3A 的纯托管 `Scgs.Client` 同时生成 `net8.0` / `net10.0`；Godot 项目使用官方桌面默认 `net8.0`，MSTest 使用 `net10.0`。NuGet 必须使用提交的 lock file 与 `--locked-mode`。Godot .NET 编辑器和 `4.7.2.stable.mono` export templates 必须来自官方发行物并校验固定哈希；本地及 CI 均通过显式可执行文件路径调用，不依赖 WinGet alias。

Windows 客户端用原生库默认设置 `SCGS_MSVC_STATIC_RUNTIME=ON`，Release 为 `/MT`、Debug 为 `/MTd`。制品审计除架构和精确 14 个导出外，还拒绝 `MSVCP140*` / `VCRUNTIME140*` 动态依赖。原生 DLL/dylib 由同一提交构建并暂存，不进入 Git。

版本升级必须作为独立变更完成兼容性与导出验证，不能在功能提交中隐式漂移。legacy YGOPro2/Unity 工具链仅供历史代码考察，不属于现行构建要求。
