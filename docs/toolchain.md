# 工具链基线

本仓库当前锁定以下客户端工具链：

| 工具 | 锁定版本 | 用途 |
|---|---:|---|
| CMake | 3.25 或更高 | C++ 配置与构建；`CMakePresets.json` schema 6 以 3.25 为最低版本 |
| C | C11 | `scgs_v04` 公共头与原生 consumer 测试 |
| C++ | C++20 | 权威规则引擎与 C ABI 适配层 |
| Python | 3.10 或更高 | legacy 兼容、制品/视觉素材审计、超时、Gate 3B/3C/4A full-match 及 Gate 4B-R1 visual/golden 契约测试 |
| Godot | **4.7.2 .NET** | Gate 4B-R1 authored 3D/2.5D 桌面热座、玻璃 HUD、signal-driven full-match/visual smoke 与导出 |
| .NET SDK | **10.0.400** | C# 绑定、测试与 Godot 项目；由根目录 `global.json` 精确锁定 |

正式客户端目标仅为 Windows x86-64 与 macOS Apple Silicon。本阶段明确不支持 Web；Godot .NET 的 Web 导出不属于 alpha 承诺，也不得在文档或 CI 中宣称已支持。

Gate 2 构建通过 CMake `FetchContent` 固定 `nlohmann/json` 3.12.0 归档与 SHA-256，不依赖开发机全局包。输出目标为 Windows x86-64 `scgs_v04.dll`、Linux `libscgs_v04.so` 与 macOS Apple Silicon `libscgs_v04.dylib`；公开 ABI 与 JSON schema 见 [`native-api-v04.md`](native-api-v04.md)。

纯托管 `Scgs.Client` 与 `Scgs.Hotseat` 都生成 `net8.0` / `net10.0`；Godot 项目使用官方桌面默认 `net8.0`，MSTest 使用 `net10.0`。三个项目的 NuGet restore 必须使用提交的 lock file 与 `--locked-mode`。Godot .NET 编辑器和 `4.7.2.stable.mono` export templates 必须来自官方发行物并校验固定哈希；本地及 CI 均通过显式可执行文件路径调用，不依赖 WinGet alias。

Gate 4B-R1 继续使用 Compatibility renderer；默认启动 authored 3D/2.5D presenter，精确参数 `--legacy-2d-board` 只开放历史 2D 源码回归。现代玻璃 HUD 只使用 Godot 自身的单一 `BackBufferCopy` 和共享 screen-reading CanvasItem shader，不引入渲染插件、新 CMake option 或第二套客户端工具链。Windows/macOS 导出仍只包含默认 3D 产品入口。

Godot 工程提交 34 项经清单审计的原创临时视觉资产：29 张卡图、卡背、菜单背景、未知卡 fallback 正面和 2 张头像。`client/godot/assets/visual/ASSET_MANIFEST.json` 记录路径与 SHA-256，Python 契约会检查数量、唯一性、实际文件与导出 NOTICE。这些临时资产不改变原生 ABI/schema，也不是正式卡牌表现工具链。

Gate 4B-R1 的四尺寸截图与性能套件必须在 Windows display-backed 窗口中运行，不能用 headless renderer 替代；尺寸固定为 1280×720、1600×900、2560×1440 和 2560×1600。1600×900 golden 只能经 `scripts/ci/update_gate4b_goldens.py` 显式更新，CI 不自动覆盖；感知阈值为归一化 MAE 不高于 0.025、边缘差不高于 0.08。macOS 保留结构、资源、ARM64、签名和真实启动审计，不跨平台复用 Windows 像素 golden。

视觉报告使用 Godot `RenderingServer.GetVideoAdapterName/GetVideoAdapterType` 记录适配器。硬件渲染应用 p95 不高于 33.3 ms、单帧低于 100 ms 的时间预算；CPU、Microsoft Basic Render Driver、llvmpipe、SwiftShader 或明确 software renderer 可以标记 `timing_budget_applicable=false`，但仍必须完成 300 帧预热 + 300 帧测量、11 状态功能/隐私检查与 actor/material/texture 零增长。该分类不能把软件渲染数据写成 GPU 性能通过。

Windows 客户端用原生库默认设置 `SCGS_MSVC_STATIC_RUNTIME=ON`，Release 为 `/MT`、Debug 为 `/MTd`。制品审计除架构和精确 14 个导出外，还拒绝 `MSVCP140*` / `VCRUNTIME140*` 动态依赖。原生 DLL/dylib 由同一提交构建并暂存，不进入 Git。

版本升级必须作为独立变更完成兼容性与导出验证，不能在功能提交中隐式漂移。legacy YGOPro2/Unity 工具链仅供历史代码考察，不属于现行构建要求。
