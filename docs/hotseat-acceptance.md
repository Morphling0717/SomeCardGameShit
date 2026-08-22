# Gate 3A 热座客户端验收

本清单区分 Gate 3A 已承诺的“桌面骨架与首张安全快照”和 Gate 3B 才承诺的完整热座 Alpha。勾选只能依据同一提交上的真实实现、明确代码审计、测试与制品，不能依据计划文字。

## 工具链与构建

- [x] `global.json` 精确选择 .NET SDK 10.0.400，locked restore 成功；
- [x] Godot 4.7.2 .NET headless cold import 无 C# build/script error；
- [x] Windows x64 与 macOS arm64 原生库来自同一提交且精确导出 14 个函数；
- [x] Windows DLL 不依赖 `MSVCP140*` / `VCRUNTIME140*`；
- [x] 原生二进制未提交 Git，Noto 字体哈希与许可证匹配。

## 托管边界

- [x] 14 个签名、ABI/schema 和冻结枚举值有自动测试；
- [x] JSON omission、未知字段、结构错误、未知 keyword bits 有契约测试；
- [x] 两段式输出覆盖增长、短写、NUL、严格 UTF-8、超限和重试上限；
- [x] native 与 engine 错误分层，TLS last-error 在同一线程读取；
- [x] SafeHandle 只销毁一次，两个 viewer cursor 互不干扰；
- [x] 当前提交真实动态库完成 create/start、双 viewer 快照、全部查询 wrapper、一次合法调度、事件脱敏、revision 与 dispose 集成测试。

## 场景与隐私

- [x] `Bootstrap`、`MainMenu`、`Match`、`PassDeviceOverlay` 均可实例化；
- [x] 两席使用独立 selector，可分别选择 `midrange` / `advance`，代码不对相同牌组设互斥；
- [x] 产品路径以 DTO 默认值省略 seed、随机先手、开启洗牌；
- [x] CI 配置固定 seed、强制 Player0、关闭洗牌；
- [x] 遮挡完全不透明，揭示前 `GetView` 次数为零；
- [x] 揭示后 Label 内容来自真实 DTO，不是硬编码快照；
- [x] 真实首帧中自己手牌显示身份，对方手牌只显示无身份牌背/数量；native/DTO 契约保证背面伏策不泄露；
- [x] SafeHandle、重复 dispose 与 CI smoke 的主动释放已经验证；

## 视觉与导出

- [x] 1600×900 与 1280×720 下中文无缺字、主要区域不重叠；
- [x] Windows 导出中 DLL 与 EXE 同目录，启动 `--ci-smoke` 在 30 秒内成功退出；
- [x] macOS `.app` 仅含 arm64 可执行文件，dylib 位于 `Contents/Frameworks`，ad-hoc codesign 后可执行 smoke；
- [x] 两个导出包包含 GPL、Godot MIT、nlohmann MIT、Noto OFL 与第三方声明；
- [x] CI 上传 `SomeCardGameShit-gate3a-windows-x86_64.zip` 和保持执行权限的 macOS `.app.zip`。

## Gate 3B 动态交互复验

以下路径已有实现或底层契约，但 Gate 3A 的确定性 smoke 只进入第一张 Mulligan 快照，因此不把它们伪装成已做过的完整人工操作：

- [ ] 在 Godot 菜单中把两席实际选成同一牌组并开始比赛；
- [ ] 用产品随机配置重复启动，人工观察不同 seed / 先手与洗牌结果；
- [ ] 在 Godot 对局中实际渲染一张对方背面伏策并检查显示文本；
- [ ] 实际点击 Match 的返回菜单按钮，再创建第二个 session。

这些复验应随 Gate 3B 的调度、持续换手和命令提交一起执行；不改变 Gate 3A 已验证的工具链、边界、首帧隐私与桌面导出结论。

## 明确未验收

Gate 3A 不宣称调度 UI、出牌、攻击、进化、部署、伏策响应、结果/重开或真人完整一局已经可用；也不覆盖 Web、Linux 正式客户端、正式美术/音效、macOS Developer ID、公证或物理 Mac 测试。这些项目不能因 headless smoke 通过而标为完成。
