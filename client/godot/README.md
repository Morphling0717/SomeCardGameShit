# Godot 热座客户端（Gate 3A）

此目录是 Godot 4.7.2 .NET、`net8.0` 的桌面客户端骨架。C# 界面只消费
`../Scgs.Client/Scgs.Client.csproj` 的安全接口；C++ 引擎仍是唯一规则真值。

## 本地运行

将当前平台的 Gate 2 动态库放入以下位置之一：

- `native/windows-x86_64/scgs_v04.dll`
- `native/macos-arm64/libscgs_v04.dylib`

也可通过环境变量 `SCGS_NATIVE_LIBRARY`，或 Godot 用户参数
`--native-library=<绝对路径>` 指定库。随后用 Godot 4.7.2 .NET 打开本目录。
启动页会先创建并立即释放一个未开局 session，以验证动态库与 ABI；预检失败时不会
允许创建比赛，也不会读取任何玩家快照。Gate 3A 只支持 Windows x64 与 macOS arm64。

CI smoke 使用真实原生库、固定 seed 和固定先手：

```text
godot --headless --path client/godot -- --ci-smoke --native-library=<绝对路径>
```

成功时输出唯一标记 `SCGS_GODOT_CI_SMOKE_OK` 并以 0 退出。
本地视觉验收可额外传入 `--ci-screenshot=<绝对 PNG 路径>`；客户端会在首张
安全快照完成渲染后保存截图，再输出成功标记并主动退出。该参数只允许与
`--ci-smoke` 一起使用。

## 隐私边界

`MatchScreen` 在进入比赛时只展示完全不透明的交接层，不读取任何观看者快照。
只有交接层发出明确的“揭示”事件后，`ViewerRevealGate` 才允许调用 `GetView`。
遮挡恢复时已渲染的手牌与快照引用会被立即清除。

界面目前只显示第一张结构化快照，不实现规则动作。卡框、图标和颜色均为原创占位几何；
唯一第三方素材是随项目分发并单独记录许可证与 SHA-256 的 Noto Sans CJK SC。
桌面导出还会附带 Godot、其内置第三方组件、.NET runtime、nlohmann/json 与字体的
完整许可证/声明；这些文件由 finalize 脚本复制并由导出审计强制检查。
