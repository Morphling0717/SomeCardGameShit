# Gate 6A-R1：AnimeV1 一体化卡体

> 状态：`pending_user_approval` / `locked_not_final`

本轮只建立 AnimeV1 卡体的可审批视觉基线。它解决旧样片中卡框、费用、类型、名称和身材像多个独立零件拼接的问题，但仍不是最终商业卡框。用户明确批准前，不更新正式视觉 golden，也不据此批量生产剩余卡图。

## 卡体规范

- 所有正面使用统一 3:4 卡体；现有 2:3 代表插画通过带可配置焦点的 cover-crop 铺满完整卡面，保持原始比例，禁止拉伸，也不在卡图下方保留空白色块。
- 五种卡牌类型分别使用连续轮廓：随从、法术、护符、伏策和场地。费用宝石、独立不透明名称座、职业纹章及攻血／倒数座都嵌入同一轮廓；卡名不直接压在插画上，也不再使用悬浮圆形徽章、黑色名称条或大号类型按钮。
- 视觉组合覆盖五类型 × 三阵营（曜誓、契术、中立）× 四级稀有度（普通、稀有、史诗、传说）。`Evolved` 使用独立进化层，`Token` 使用无稀有度衍生物层；LO-11 与 AP-11 另有已生成的进化异画。
- 卡名与费用、攻击、生命、倒数数字使用 `NotoSerifCJKsc-SemiBold`；规则说明和通用 UI 继续使用 `NotoSansCJKsc-Regular`。卡名必须保持单行并完整显示，不允许省略或截断；2D 与真实 3D 卡体共用避开左右阵营装饰的铭牌中央内框，以字体实际宽度、ascent、descent 和描边度量缩放并绝对居中，1280×720 的十张手牌状态仍须达到锁定的可读字号、装饰安全边距与 GPU 字形阈值。完整规则由详情面板显示。
- 手牌、场上和详情三种尺寸共享同一 `CardFaceComposition`、归一化锚点、素材路径和裁切结果，避免预览与正式 3D 卡牌形成两套视觉规则。

## 真实渲染边界

审批样片使用真实 `CardActor3D`，在主战场 Viewport 中直接组合插画、连续卡框、材质、职业纹章、稀有度、进化／衍生物层和文字。每张卡不会创建 `SubViewport`。

隐藏牌不会建立正面 composition，也不会绑定 `design_id`、正面纹理、稀有度材质或 tooltip，只使用共享卡背。当前新增的卡体入口不加载 native，也不创建真实对局。

## 八种审批状态

`--anime-card-body-slice` 提供以下八个状态：

1. `contact-sheet`：五类型 × 三阵营 × 四稀有度接触表。
2. `representatives`：七张代表卡与两张进化异画。
3. `contexts`：详情、手牌、场上三档尺寸使用同一组合。
4. `hand-one`：一张手牌。
5. `hand-five`：五张手牌。
6. `hand-ten`：十张手牌。
7. `hand-hover`：悬停、抬起和相邻让位。
8. `values`：0／双位数费用、0/0、多位数身材、受伤、强化和倒数可读性。

报告固定声明 `ApprovalStatus=pending_user_approval`、`UsesRealCardActor3D=true`、`UsesPerCardSubViewport=false` 和 `UsesNativeSession=false`。

## 启动与捕获

从源码使用已配置的 Godot 4.7.2 .NET 编辑器启动交互样片：

```powershell
godot --path client/godot --windowed --resolution 1600x900 -- --anime-card-body-slice
```

可用 `--anime-card-body-state=<state>` 指定单个状态。自动捕获必须给出绝对输出目录，并使用真实窗口而不是 headless 渲染：

```powershell
godot --path client/godot --windowed --audio-driver Dummy --resolution 1600x900 -- "--anime-card-body-slice=C:\absolute\card-body-captures" --anime-card-body-slice-exit --ci-visual-viewport=1600x900
python scripts/ci/validate_anime_card_body_slice.py C:\absolute\card-body-captures\anime-card-body-slice.json --expected-viewport 1600x900
```

导出包须完整解压后再启动：

- Windows x64：双击 `PLAY_ANIME_CARD_BODY_SLICE.cmd`，它要求 `SomeCardGameShit.exe` 位于同一目录。
- macOS ARM64：运行 `PLAY_ANIME_CARD_BODY_SLICE.command`，它要求 `SomeCardGameShit.app` 位于同一目录。包目前仅 ad-hoc 签名、未公证，Gatekeeper 可能要求右键“打开”。

macOS 托管 CI 使用受限的 1024×684 窗口做结构与 shader smoke；正式人工视觉检查仍以 1280×720 及以上桌面尺寸为准。

## 验证

核心卡体和资产契约：

```powershell
dotnet test --project client/Scgs.Client.Tests/Scgs.Client.Tests.csproj --configuration Release --no-restore --filter "FullyQualifiedName~CardFaceContractsTests"
python -m unittest scripts.tests.test_gate4b_visual_pipeline.VisualAssetAuditTests -v
python scripts/audit_visual_assets.py --repo-root .
```

Godot 构建与样片报告：

```powershell
dotnet build client/godot/SomeCardGameShit.csproj --configuration Release --no-restore
python scripts/ci/validate_anime_card_body_slice.py <absolute-output>/anime-card-body-slice.json --expected-viewport 1600x900
```

审批报告使用 schema 4：每个状态最多采样30组相邻 `FramePostDraw`，只有连续两帧的尺寸、像素格式、字节长度和原始像素 SHA-256 完全相同才保存截图；超限会带最后一次差异明确失败。验证器会实际解码 8-bit RGBA PNG、检查 chunk CRC、尺寸和解码像素哈希，而不是只相信报告中的字符串。费用、攻击、生命与倒数逐个关闭标签后分别取证，真实字形的差分包围盒、高度、宽度和亮像素不足都会失败，避免其他卡牌或其他徽章的变化替消失数字通过。卡名同样按 actor 独立关闭后取证，必须逐字等于来源名称、不得含省略号、最终字号不得低于14，并要求 GPU 字形框相对名牌文字槽绝对居中；1280×720 的水平和垂直中心偏差均不得超过2像素，文字槽到名牌左右装饰区的真实屏幕安全距不得低于4像素，其他分辨率同比缩放。验证器同时校验八张截图的顺序、真实 actor 数量、style 数量、三种 context、零 `SubViewport` 和零 native session。素材审计继续分别冻结 Gate 4B 的34项、R3的1项与 Gate 6A的14项，同时独立校验本轮23项卡体素材，禁止路径遗漏、哈希漂移、重复文件及跨清单冲突。

## 素材与来源

- 卡体清单：`client/godot/assets/visual/anime_v1/card_body/CARD_BODY_ASSET_MANIFEST.json`
- 生成式材质来源与完整修改记录：`client/godot/assets/visual/anime_v1/card_body/PROVENANCE.md`
- 确定性资产包括五套卡框、三枚职业纹章、四层稀有度装饰、进化／衍生物层和四枚数值宝石。
- 两张生成式纹理只提供可替换的金属微雕与传说箔面细节；隐藏牌不得接收这些正面材质。

旧卡框与图标组合已判定不合格并重做。现有七张代表插画、两张进化异画和两张材质仍只是候选素材，本轮不会重画或将其标记为最终资产。

## 未完成事项

- 新誓卫／契术牌组尚未接入 native、Godot 产品入口或完整热座流程；本样片不能证明两副新牌组可玩或平衡达标。
- 剩余构筑卡与衍生物插画尚未批量生成。
- 稀有度当前只属于客户端视觉目录，不影响规则、抽卡、构筑、ABI 或 schema。
- 默认产品路径仍处于迁移期；只有用户明确批准本轮样片后，才能建立正式 golden、批量生产剩余美术，并在后续 Gate 6C 删除旧科幻卡体与产品 profile。
