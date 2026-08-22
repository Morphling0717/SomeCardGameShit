# 工具链基线

本仓库当前锁定以下客户端工具链：

| 工具 | 锁定版本 | 用途 |
|---|---:|---|
| CMake | 3.25 或更高 | C++ 配置与构建；`CMakePresets.json` schema 6 以 3.25 为最低版本 |
| C++ | C++20 | 权威规则引擎 |
| Python | 3.10 或更高 | legacy YGOPro2 overlay 与协议契约回归测试 |
| Godot | **4.7.2 .NET** | 后续桌面客户端；本轮 Gate 0+1 尚不创建 Godot 工程 |
| .NET SDK | **10.0.400** | 后续 Godot C# 项目；由根目录 `global.json` 精确锁定 |

正式客户端目标仅为 Windows x86-64 与 macOS Apple Silicon。本阶段明确不支持 Web；Godot .NET 的 Web 导出不属于 alpha 承诺，也不得在文档或 CI 中宣称已支持。

版本升级必须作为独立变更完成兼容性与导出验证，不能在功能提交中隐式漂移。legacy YGOPro2/Unity 工具链仅供历史代码考察，不属于现行构建要求。
