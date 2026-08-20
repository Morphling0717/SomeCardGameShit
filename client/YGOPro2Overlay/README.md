# YGOPro2 overlay

这个目录保存准备复制到 YGOPro2 `Assets/SomeCardGame/` 的兼容层代码。

当前包含：

- SCGS 私有消息枚举；
- Unity 5.6 可用的旧式 C# 数据模型；
- PlayerState / UnitState 二进制解码；
- 一个不依赖场景对象的状态仓库；
- `Package.Fuction` / `Package.Data.reader` 适配器；
- 固定协议版本、长度和尾随字节检查；
- 与 C++ 固定字节向量一致的 C# golden vectors。

当前环境没有 Unity 5.6.7 或 C# 编译器，因此这些文件尚未完成编辑器编译验证。C++ 端的完整消息、YGOPro2 payload、固定字节向量和跨语言静态契约已经通过自动测试。

## 应用 overlay

先拉取锁定的 YGOPro2：

```bash
./scripts/bootstrap-upstream.sh
```

再运行：

```bash
python3 tools/apply_ygo2_overlay.py vendor/YGOProUnity_V2
```

该工具会：

1. 检查目标提交是否为锁定版本；
2. 检查 210–219 是否发生编号冲突；
3. 向 `Assets/YGOSharp/Enums/GameMessage.cs` 注入 SCGS 消息编号；
4. 把本目录中的 `Assets/SomeCardGame` 复制进 YGOPro2；
5. 重复运行时保持幂等，不重复插入枚举。

## 接到 `Ocgcore.logicalizeMessage`

YGOPro2 并不会把消息编号放在 `Package.Data` 中：

- `Package.Fuction` 是消息编号；
- `Package.Data.reader` 从协议版本字节开始。

因此不能把 `Data.reader` 当作一条包含消息编号的完整 SCGS 消息。建议在 `Ocgcore` 中持有：

```csharp
private readonly SomeCardGame.Protocol.ScgsStateStore scgsState =
    new SomeCardGame.Protocol.ScgsStateStore();

private SomeCardGame.Protocol.ScgsYgoProPackageAdapter scgsAdapter;
```

初始化：

```csharp
scgsAdapter = new SomeCardGame.Protocol.ScgsYgoProPackageAdapter(scgsState);
```

在 `logicalizeMessage(Package p)` 的 switch 中，为 210–219 增加路由：

```csharp
case GameMessage.ScgsPlayerState:
case GameMessage.ScgsUnitState:
{
    string error;
    if (!scgsAdapter.TryApply(p.Fuction, r, out error))
    {
        UnityEngine.Debug.LogError(error);
    }
    break;
}
```

当前只实现 PlayerState 与 UnitState。其余编号已经预留，但在对应 payload 模型完成前应明确报错，不应静默吞掉。

接下来让 `ScgsStateStore` 的事件更新 PP、生命和单位数值 UI，并使用 `ScgsProtocolGoldenVectors` 做 Unity EditMode 测试。

不要直接把 SCGS payload 当成原生 YGOPro `UpdateCard` 数据；它是独立、带版本号的消息。
