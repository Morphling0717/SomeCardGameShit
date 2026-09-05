# 卡框精修 R1：本机候选真实测试报告

记录日期：2026-09-06（Asia/Shanghai）。**卡框候选，用户尚未批准；不自动更新 golden。**
本报告分别记录自动合同、真实 v05 操作、GPU 取证和合成设计展示；任何一项
通过都不等于“已经达到用户要求的美术品质”，也不代表第二阶段已经完成。

## 版本、环境与范围

- 本轮实现基线为 `c891105b0cd2bd4c13ee84e6b28157f30400fd0e`；最后的
  `compactPublicBacks` 两文件修补及其测试位于该基线后的工作区。
  最后 NT-04 公开帧来自 **c891105 + 该修补**，不是干净 c891105 的复现证明。
- 自动回归原始记录的当时 HEAD 为 `a7eb363ba0b3790cced6dc03646bbd8ca2c2aa0c`
  加 R1 工作区，见 `build/card-frame-r1/final-candidate-20260906/README.md`。
  不将后来提交的 SHA 回填成这些早期测试的执行版本。
- 初始本机 `client/godot/project.godot`／`addons/` MCP 配置保留；没有用
  reset、stash 清除它们。图片名称中的 final／final2／sha 只是操作者标签。
  普通静态 PNG 未内嵌源码 SHA，收集时 HEAD 不是每张图的捕获版本认证。
- Godot `4.7.2-stable (official)`、Windows、Compatibility／`opengl3`；GPU
  为 NVIDIA GeForce RTX 4080 Laptop GPU，API 报告 `3.3.0 NVIDIA 595.79`。
  报告未检测到软件渲染；`DeviceType.Other` 在 Compatibility 下不是 WARP 证据。
  未另行推断操作系统驱动包版本。托管构建使用锁定 SDK `10.0.400`。
- R1 只在 `--card-frame-review` 或 `CardFrameReview.tscn` 启用，仅覆盖
  LO-11、AP-11、NT-04；默认产品与旧 `--battle-presentation-review` 保留。
  复用已有动作，不宣称本轮重做了出牌、命中或进化演出。

## 自动回归与素材合同

| 检查 | 实际结果 | 原始记录 |
| --- | --- | --- |
| 完整 managed Release，真实 native fixture | 231/231，0 失败、0 跳过，10.702 秒 | `final-candidate-20260906/managed/Scgs.Client.Tests_net10.0_x64.trx` |
| Windows CTest，`SCGS_SMOKE_SEEDS=2048` | 28/28，50.19 秒 | `ctest-windows-release-2048-fixed.log` |
| 六组相关 Python 合同 | 111/111，39.112 秒 | `python-related-fixed.log` |
| 新 R1 镜头／边界合同，含最后公共牌背修补 | 11/11，0.144 秒 | `python scripts/tests/test_card_frame_projection.py -v` |
| 运行图片清单 | 73 项，其中本轮登记 4 张 R1 PNG | `visual-assets.log` |
| Blender 源、GLB、四贴图哈希／真实几何 | 通过 | `frame-model-audit.json` |
| 当时工作区 `git diff --check` | exit 0，仅换行警告 | `diff-check.log` |

表中未写全目录的日志位于 `build/card-frame-r1/final-candidate-20260906/`。
CTest 包含部分 Python 合同，不能把这些数量相加宣传成互不重复的独立测试。
该次 CTest 使用既有未改规则的 native 二进制；managed 通过不是 Godot GPU 验收。
最初 Python 110/111、CTest 27/28 的失败日志仍保留，修正的是过时的源码形状
断言；最终回归仍要求 ClearSensitive 清空所有子网格可见性和 override 材质。

高／低 LOD 实际三角数分别为 10,451／4,691。严格审计读取 GLB POSITION 字节
及节点变换；场上整卡 1.16 倍后占地约 1.77414×2.43885，仍在 1.88×2.48 格内。
新合同还锁定：默认透视与五／三主格位置不变；R1 正交范围 12.7～13；手牌按
真实镜头投影定屏幕尺寸；公共牌背压缩默认 false，唯一启用条件为 R1 且非私密
投影，并只缩小近端匿名背面。**这些源码／几何合同不代替实际字形或遮挡检查。**

候选高模 SHA-256：`d0463ad88882ca010a8eb1485df633f30903d2b7eb03ea446d8319bf6b2ed72e`。
候选低模 SHA-256：`38304eea238c8919c176c3f3d58826947ca73bd9a19bf90adeaaea05bf9987ab`。
模型源 manifest SHA-256：`a52ed8971c9773c74155c7780cdfc63f795acf44db6a93a15844e8fe042bdcdc`。
三个最终设计录像的运行 manifest 独立记录了上述高模哈希；普通截图没有同等绑定证明。

## 真实编辑器、对局与四尺寸实拍

主代理通过实际 `godot-scgs` MCP 连接本仓库 `client/godot`，打开并运行
`CardFrameReview.tscn`。场面准备使用真实 v05 查询及合法命令，没有写入伪造
native 状态；Covered 后主动揭示，再用真实输入出牌、选择精确格位和进化。
已实测 LO-11、AP-11 落位与进化后的真实场上卡和异画，以及 NT-04 后排施法。

开发准备 trace 位于 `user://review-evidence/`，suite 为
`real-product-card-frame-review`、`synthetic=false`。相关记录包括
`20260905-203228140-card-frame-oathguard-77de65127f234d858d560d99f088c0fb.json`、
`20260905-203538665-card-frame-pactmage-4a81750c79c94b53a7427376c7c79139.json`、
`20260905-204940849-card-frame-spell-fb1add32117d4278a254ceb9221e60ed.json`。
trace 是含测试配置／私密选择的开发证据，不作为公开 UI 或玩家包内容。

| 证据 | 实际范围 |
| --- | --- |
| `card-frame-r1-final-{lo,ap,nt}-close.png` | 三卡共享详情 GPU viewport，均 576×768；不是完整战场图 |
| `card-frame-r1-final2-lo-evolved-<尺寸>.png` | 真实 LO 进化终态，四种尺寸 |
| `card-frame-r1-final2-ap-evolved-<尺寸>.png` | 真实 AP 进化终态，四种尺寸 |
| `card-frame-r1-actual-nt-hover-<尺寸>.png` | 真实 NT-04 手牌悬停及共享详情，四种尺寸 |
| `card-frame-r1-final2-{hand,ap}-hover-1280.png` | 1280 宽真实手牌悬停 |
| `card-frame-r1-final-lo-normal-1280.png` | LO 未进化的真实场上终态 |
| `card-frame-r1-real-handoff-covered-1280.png` | 完全不透明的真实交接遮挡 |

原图在 `user://screenshots/`。已读取 PNG 头核对两名王牌，以及 MCP 返回的 NT 实际图像尺寸各为
1280×720、1600×900、2560×1440、2560×1600，不只按文件名认定尺寸。
精选字节副本、SHA-256 与边界说明在
`artifacts/card-frame-r1/evidence/final-review/`，没有为整理证据重新绘图或改图。
这不是全部状态×全部三卡×四尺寸的完整矩阵；NT-04 悬停／详情覆盖四尺寸，
公开施法中间帧另在 1280×720 检查。此前在编辑器嵌入模式下尝试改窗口尺寸
实际仍为 1280×720，那三张文件名含较大尺寸的图被排除；最终 `actual-` 图
来自关闭嵌入后的独立窗口，并逐张检查实际返回尺寸，结束已恢复原嵌入设置。

最后 NT-04 修复经实际 MCP 完成“手牌 → 伤害模式 → 己方策略格 1 → 敌方目标”。
`card-frame-r1-public-backs-fixed-1280.png` 为真实 1280×720 GPU 图：
`Presenting / revision=11 / viewer=null / surfaces=[]`，精确后排法术正面可见，
近端匿名小牌背不再遮住它。为保留中间帧，主代理短暂暂停 SceneTree，截图后
恢复并确认 `Action / revision=11`；**此静帧不证明正常速度演出质量**。
旧 c891105 施法被牌背遮挡的图片另放 `historical-not-final/`，不作为修复通过证据。

## 真实 GPU 字形，不用 AABB 冒充可见数字

原始 manifest 位于 `user://screenshots/card-frame-r1/<ID>/manifest.json`。
两次均为实际 1280×720 图、逻辑 1600×900，`measurement_revision=2` 采用
0.8 坐标换算。文字开／关各等两个 FramePostDraw，按 socket 附近真实 GPU
像素差取边界（含描边），不是文字 AABB、旁路字符串或 OCR。

| 捕获 ID | 真实对象／方向 | 费用／攻击／生命字高 |
| --- | --- | --- |
| `20260905T202757548-d836954b` | LO-11 进化 10/10，Player0，rev 23，近端 | 20／17／17 px |
| `20260905T202836865-e83d0ba6` | 同一 LO-11，Player1，rev 24，远端 | 21／17／16 px |

两次手中 NT-04 费用 4 均为 26 px。上述已测主要数字满足 16 px 底线；
不能外推所有卡、受伤、多位数或所有斜角均通过。LO 场上完整卡名字高实际
近端 7／远端 6 px，不能冒称场上全名有 16 px；完整阅读依赖共享详情与手牌。
manifest 记录标签恢复成功、session/命令/ACK/reveal 调用全为零。
旧版本未换算逻辑 ROI 的全零结果，以及旧近端费用仅 12 px 的失败取证不抹除，
均不纳入本次成功数字。字形居中、笔画清楚和艺术效果仍保留人工／用户判断。

## 资源回池与性能：两个负载分别记录

| 负载与 ID | 尺寸／采样 | 实际 p95／max |
| --- | --- | --- |
| 当前真实 Action 场面；`20260905T202418608-64c5ffe6` | 实际 GPU 2560×1494；300 预热＋300 测量 | 6.0024／42.3455 ms |
| 24 卡合成高 LOD 静态重场；`20260905T202923572-a8ff74a8` | 独立 1440×1200 viewport；300 预热＋300 测量 | 4.2578／14.1094 ms |

第一份在 `user://review-evidence/card-frame-r1-performance/<ID>/performance.json`：
双主战场当时均为 0 卡，是**当前单场静态负载**，不是最大场；其尺寸不是四种
正式目标尺寸之一，不伪装成 2560×1440／1600 或四尺寸性能覆盖。
第二份为 `card-frame-r1-pool/<ID>/pool.json` 的 `static_heavy`：
`synthetic=true / synthetic_heavy=true / native_maximum_board=false`，时钟覆盖
实际游戏加额外 viewport 的整帧，不是独立 GPU 执行时间。两次 VSync 都记录
Enabled、FPS limit 0，探针未更改设置；不能声称完成关闭 VSync 的无上限性能关。
测量区间均无 GPU readback，实际图像尺寸在区间外取得。

24 actor 回池另有 6 轮预热＋24 轮测量，三卡高／低细节、清空和匿名牌背反复绑定，
总计 782 次实际 FramePostDraw（含上述 600 帧）。失败列表为空；同阶段真实
资源 ID 指纹稳定，临时 viewport 已释放。清空／匿名状态的身份、文字、metadata、
碰撞、tween、R1 材质 override、卡图绑定及可见子网格残留均为 0；实际注入
洋红 sentinel 的 actor 为 24。合成重场前后引用为 1325 节点／68 网格／138 材质／
7 纹理，全局资源数测量期间 149→149。这里不证明字体 atlas、驱动分配或托管堆
逐帧绝对零增长，也不是整局隐私、全部卡牌或连续动作性能矩阵。

## 三段卡体斜视录像：设计展示，不是动作录像

最终设计录像来自 `user://screenshots/card-frame-r1/turntable/`：

| 卡 | 捕获 ID | 真实采样／编码帧数 | 最后一帧时间 |
| --- | --- | --- | --- |
| LO-11 | `20260905T203357820-4431364b` | 59／59 | 5.987702 秒 |
| AP-11 | `20260905T203437133-666ab811` | 60／60 | 5.980580 秒 |
| NT-04 | `20260905T203453757-742c1ecc` | 51／51 | 5.907810 秒 |

三图像序列均为 720×960，像素中标“卡框设计展示 · 非对局状态”。它们用公开
印刷值，session/命令/ACK/reveal 为零；只检查真实 GLB、光照、切面和字体。
最终 MP4 与编码报告在 `artifacts/card-frame-r1/evidence/design-turntables/final/`。
VFR 保留实际单调时间戳，FFprobe 核验逐帧一一对应、误差 0 秒；没有插帧或
重复尾帧。实际采样约 9.687／9.865／8.463 fps，不能宣传固定 30／60 fps。
这三段不是出牌、进化、施法、攻击录像，不计为本轮新增战斗演出。

## 交付验证与尚未完成项

- 用户视觉质量批准：**尚未获得**；不锁定母版、不切换默认入口、不进入全量阶段。
- 实现 `c891105` 的四项主 CI 已全部成功：Windows MSVC、macOS ARM64、Linux
  GCC、Clang sanitizers。[实际 run 33990600635](https://github.com/Morphling0717/SomeCardGameShit/actions/runs/33990600635)
  包含两平台源码 full-ui、导出真实启动和 ZIP 解包启动。冷资源导入分别用时
  Windows 5 分 16 秒、macOS 15 分 22 秒；这些不是本机硬件性能验收。
- 最后 compactPublicBacks 修补版已重新 Windows Release 构建（0 警告／错误）、
  导出和暂存同提交来源 v05。MCP 隔离实查 PCK 330 项、20 项项目设置和 1 份
  class cache，通过无插件／autoload／令牌／探针残留检查。
- 本机新导出 full-ui smoke 已通过，实际 1600×900 NVIDIA 硬件窗口，日志唯一
  `SCGS_PRODUCT_V05_UI_SMOKE_OK`；证据为
  `artifacts/card-frame-r1/export-final-product-smoke/`。这验证默认 v05 路径，
  不拿它代替 R1 卡框视觉检查。另用真实玩家窗口进入卡框审阅、Covered 和主动
  揭示后的 AP 手牌；正式包没有 MCP，使用电脑操作工具，窗口抓取曾不稳定，
  因而它不承担精确像素测量；卡框主要画面证据来自前述真实编辑器 MCP。
- 最终 ZIP 为 `artifacts/packages/SomeCardGameShit-card-frame-r1-windows-x86_64-review.zip`，
  SHA-256 `9235be63d059cad9f814692b97e82c9fde520f150454b259d4e22a36993cc039`。
  重新解包后的 full-ui 实启已通过，证据
  `artifacts/card-frame-r1/final-zip-product-smoke/`，成功标记恰好一次。
  包含 `PLAY_CARD_FRAME_R1_REVIEW.cmd`、14 导出 v05、许可及资产审计清单；
  `REVIEW_PACKAGE.json` 如实标记 c891105 + 当时工作区，不宣称打包器重新构建。
  包体不包含编辑用 Blender 源／MCP／私密准备 trace。包含最后修补与报告的分支
  尖端还须独立重新运行四项 CI，不能把 c891105 的结果当作任意后续 SHA 的结果。
- macOS 已有上述 CI ARM64 导出验收；没有物理 Mac 的人工卡框品质批准。
- 未证明全 35 卡换装、全 14 Action 的新演出、完整动态性能／全部隐私组合、
  四尺寸全状态矩阵；本轮也没有重做主菜单、卡图或音频。
- 新 golden 仅能在用户明确认可后人工更新。自动通过、静态终态与设计录像都不能
  替代这项质量决定。入口及日常操作说明见 `docs/card-frame-r1.md`。
