# SCGS ↔ YGOPro2 协议 v1

所有多字节整数使用小端序。逻辑上的完整消息为：

```text
uint8 message_id
uint8 protocol_version  // 当前为 1
payload fields...
```

YGOPro2 的 `Package` 会把这两部分拆开：

```text
Package.Fuction     = message_id
Package.Data.reader = protocol_version + payload fields
```

因此 C++ 同时提供“完整消息”和“payload-only”编码/解码；C# 同时提供 `DecodePlayerState(...)` 与 `DecodePlayerStatePayload(...)` 两类入口。完整消息用于 golden-vector 和独立传输测试，接入 `Ocgcore.logicalizeMessage` 时必须使用 payload 入口。

## PlayerState（211）

```text
uint8  player
int16  leader_health
int16  maximum_leader_health
uint8  current_pp
uint8  maximum_pp
uint8  evolution_points
uint8  own_turn_number
uint8  flags
```

`flags`：

- bit 0：本回合已进化；
- bit 1：本回合已高级召唤；
- bit 2：本回合已设置伏策；
- bit 3：本局已使用主战技。

固定测试向量：

```text
D3 01 01 11 00 19 00 03 07 02 06 03
```

## UnitState（212）

```text
uint8  controller
uint8  sequence
uint64 instance_id
int16  attack
int16  health
int16  maximum_health
uint32 keywords
uint8  inherited_imprint
uint8  flags
```

`flags`：

- bit 0：已进化；
- bit 1：本回合已攻击；
- bit 2：本回合入场；
- bit 3：本回合通过高级召唤入场；
- bit 4：背面表示。

固定测试向量：

```text
D4 01 00 03 08 07 06 05 04 03 02 01
07 00 05 00 08 00 03 00 00 00 01 09
```

C++ 编码器和解码器不仅互相往返，还会与以上固定字节逐字比较，防止两边同时写错却仍然通过 round-trip 测试。
