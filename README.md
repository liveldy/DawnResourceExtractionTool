# 英雄联盟数据提取工具 (LoL Data Extractor) 1.0.2

读取 `英雄联盟\Game\DATA` 下的 wad v3 资源包，把 64 位路径哈希还原成真实资源路径，
按树形目录浏览、就地预览、按需筛选和多线程解包。图形界面面向日常操作，命令行面向批处理与脚本。
导出的贴图直接是 PNG、音频直接是 MP3，语音还能就地转成文字存成备注。

## 功能

- **扫描**：识别 `Game\DATA`（或其上级目录），左侧按 `DATA` 的真实目录结构列出 wad 树（`FINAL` / `Champions` /
  每个英雄的 wad），每行带勾选框，上方搜索框按 wad 名或路径过滤，命中的目录自动展开。
  目录的勾选框会把该目录下的 wad 一起勾上（部分勾选显示为半选）。
  「全选 / 清空」只作用于当前显示的项（有搜索时就是搜索结果），不会顺带勾上被过滤掉的 wad。
  三栏之间的竖条可以拖动，按需要把 wad 列表、目录树或预览面板放宽。
- **名称还原**：用 CommunityDragon 哈希表把哈希还原成真实路径（本机实测 99.9%）。
- **树形浏览**：条目按「类别 → 目录 → 文件」分组，单链目录自动展开，可按路径/哈希搜索、按类别过滤；
  每行带勾选框，勾目录等于勾它下面全部条目，勾选结果换搜索也不会丢。
- **预览**：贴图（`.tex` / `.dds` / `.tga`）直接出图，模型（`.scb` / `.skn`）可旋转缩放的 3D 视图
  （左键拖拽旋转、右键拖拽平移、滚轮缩放，右上角「复位视角」回到初始机位），
  音频包（`.bnk` 与语音包 `.wpk`）列出每条流并试听，文本（`.txt` / `.xml` / `.json` / `.lua` …）只读显示，其余给十六进制。
- **模型动画**：打开模型时按目录就近自动匹配骨骼（`.skl`）、该皮肤的全部动画（`.anm`）与贴图（`.tex`），
  三者在预览页各给一个下拉列表，可随时换；「…」按钮还能跨 wad 手动挑条目。
  一张皮肤有多张贴图时，按子网格名字把每张图贴到对应部件上，控制条下方列出分配结果
  （贴图按命名约定区分颜色图与遮罩/粗糙度/加载图，遮罩类永不贴到网格上）。选中动画即可播放，
  骨骼蒙皮逐帧计算（30 fps 采样，含根关节位移），播放/停止与帧进度都在控制条上。
- **导出（二次解析过的格式）**：贴图 `.tex` / `.dds` 直接写成 PNG，音频 `.wem` / `.bnk` / `.wpk` 直接写成 MP3
  （可切 WAV 或保留原文件），其余条目照原样导出；一次导出里 PNG 与 MP3 混在一起也能用系统看图/播放器打开。
  勾选 wad、按通配符 / 扩展名 / 类别过滤，多线程解包，默认跳过已导出过的文件，并写 JSONL 清单
  （`file` 是首个产物，`outputs` 是全部产物，一个 bnk 会展开成一个同名目录）。
  导出范围可选「选择的 wad / 当前 wad / 全部 wad / 当前 wad 选中的内容」，类别可一键「仅纹理 / 仅模型 / 仅音频」，
  最后一项只导出目录树里勾选的条目（勾目录等于勾它下面全部），这时路径过滤 / 目录前缀 / 扩展名让位给勾选本身，
  还能用目录前缀只导某个英雄或某层目录（树右键「导出此目录」「导出此文件」「设为导出前缀」）。
  另有「只导出图片 / 音频（PNG + MP3）」一键按钮，跳过模型与数据，只留能直接看、直接听的媒体。
  预览页右上角的「导出此文件」只导出当前条目：音频包导出的是音频列表里**选中的那一条流**，
  文件名与整包导出时一致（`<条目名>/002 83226497.mp3`），两者字节完全相同。
- **音频备注与语音转写**：音频包里每条流都能写备注，改动即存到 `%LOCALAPPDATA%\LolDataExtractor\audio-notes.json`，
  关掉程序再打开还在，列表里显示备注（过长省略）。选中一条点「转换」就把这句话转成文字写进备注，
  点「批量转换」把整包里还没有备注的流一条条转完（约 0.8 秒/条，可随时停止）。
  转写在本机跑（whisper base，模型 141 MB，首次使用时从 hf-mirror 下载），音频不出本机；
  语言按 wad 名判断（`Ahri.zh_CN` → 中文，`Ahri.wad` → 自动识别）。

## 实测结果（本机 16.21 客户端）

| 指标 | 数值 |
| --- | --- |
| wad 文件 | 576 个 |
| 条目总数 | 923,687 |
| 唯一路径哈希 | 824,375 |
| 名称还原率 | 99.9%（823,519 条） |
| 全量扫描耗时 | 约 1 秒 |
| 贴图解码 | 160/160 张成功，最慢 21 ms |

## 依赖与来源

- 解析引擎：[LeagueToolkit](https://www.nuget.org/packages/LeagueToolkit) 3.0.3（NuGet）——wad v3、
  zstd 解压、`TEX` / `StaticObject` / `SimpleSkin` 读写；它内部已含 ZstdSharp，故未直接引用 Zstandard.Net。
- 贴图解码：[BCnEncoder.Net](https://www.nuget.org/packages/BCnEncoder.Net) 2.3.0（BC1–BC7），
  [Pfim](https://www.nuget.org/packages/Pfim) 0.11.4 负责 TGA 与未压缩 DDS。
- 3D 视图：[HelixToolkit.Wpf](https://www.nuget.org/packages/HelixToolkit.Wpf) 3.1.2（相机、轨道旋转、缩放）。
- 音频播放：[NAudio](https://www.nuget.org/packages/NAudio) 3.3.0；Wwise 编码的 wem 由
  [vgmstream](https://github.com/vgmstream/vgmstream)（r2168 win64）转成 wav，首次试听时自动下载到工具目录。
- MP3 编码：[NAudio.Lame](https://www.nuget.org/packages/NAudio.Lame) 2.1.0（libmp3lame 原生库随机分发，VBR 80 kbps）。
- 语音转写：[Whisper.net](https://www.nuget.org/packages/Whisper.net) 1.9.1 + `Whisper.net.Runtime`
  （CPU，模型 `ggml-base.bin` 141 MB，从 `hf-mirror.com` 下载，见「工具数据目录」）。
- 界面：[HandyControl](https://github.com/HandyOrg/HandyControl) 3.5.1。
- 路径名：[CommunityDragon/Data](https://github.com/CommunityDragon/Data) 的 `hashes/lol/hashes.game.txt.0..8`
  （约 222 MB，9 个分片），通过 GitHub contents API 下载，避开国内常被阻断的 raw.githubusercontent.com。
- **语音转写实测**：阿狸的中文 wad 有 1,378 条语音流，名字全是数字 id（`10038138.wem`），只能靠转写区分。
  whisper base 在本机约 0.8 秒/条（3 秒的中文台词），批量转一个 52 条的语音包约 40 秒；
  加一句简体中文提示词后输出是简体带标点（`脆弱的根基当然经不起风雨。`、`我们去某点真正的垃圾吧。`），
  参数完全相同的句子会串（`語言;`），但备注可以随手改。
  tiny 模型快一倍却把「他的力量在我体内流动」听成「他的力量 再我體內流他」，故默认 base。
  sherpa-onnx 的 paraformer-zh 快 20~40 倍且简体直出，但只认中文、没有标点，多语言 wad 用不了，本版未采用。

## 快速开始

```powershell
# 图形界面
dotnet run --project src/LolDataExtractor.App
# 或已发布的可执行文件
dist\app\LolDataExtractor.exe
```

1. 顶部填写或浏览到 `...\英雄联盟（含经典模式）\Game\DATA`，点「扫描」。
2. 首次使用点「下载哈希表」（约 222 MB，只下一次；已存在的分片自动跳过）。
3. 左侧勾选 wad（「全选」只勾当前显示的项，搜索后就是搜索结果；全选后按钮变为「全不选」），
   中间在目录树里点开文件，右侧「预览」页查看内容。
   选中 `.skn` / `.scb` 模型后，贴图 / 骨骼 / 动画三个下拉已自动填好，点「播放」看动画；不对就用「…」手动挑。
4. 右侧「导出与日志」页设置导出范围、输出目录、类别、目录前缀、过滤条件与线程数，点「开始导出」。
   只想拿能直接看的资源就点「只导出图片 / 音频（PNG + MP3）」；「预览」页右上角的「导出此文件」
   可直接导出当前条目（同样会转格式，音频包只导出音频列表里选中的那一条流），目录树右键还能「导出此目录」。
5. 打开音频包（`.wpk` / `_audio.bnk`）后，右侧音频区可「播放」「转换」（转成文字进备注）或「批量转换」；
   备注框随手可改，改完即存。

命令行：

```powershell
# 查看安装目录、索引与 wad 概况
dotnet run --project src/LolDataExtractor.Cli -- info

# 下载哈希表并生成本地名称索引
dotnet run --project src/LolDataExtractor.Cli -- hashes fetch --build

# 扫描全部 wad（可导出 JSON）
dotnet run --project src/LolDataExtractor.Cli -- scan --json out\scan.json

# 列出某个英雄 wad 的模型文件
dotnet run --project src/LolDataExtractor.Cli -- list Ahri --ext skn,skl --limit 20

# 导出阿狸的基础皮肤模型/贴图/语音（贴图转 PNG、音频转 MP3）
dotnet run --project src/LolDataExtractor.Cli -- extract --wad Ahri --out D:\LoLAssets `
    --filter "*skins/base/*" --category texture,model,audio --threads 8

# 只要二次解析后的媒体：贴图 PNG + 语音 MP3，其余一概不导出
dotnet run --project src/LolDataExtractor.Cli -- media --wad Ahri.zh_CN --out D:\LoLAssets
```

常用参数：`--root` 游戏 DATA 目录、`--wad` wad 通配符、`--filter` 路径通配符、`--path` 目录前缀、
`--category` texture/model/audio/bin/text/shader/other、`--ext` 扩展名列表、`--min-size` / `--max-size`、
`--flat` 扁平输出、`--overwrite` 覆盖已有文件、`--no-unnamed` 丢弃未命名条目、`--dry-run` 只看统计、
`--audio mp3|wav|keep` 音频格式（默认 mp3）、`--raw` 贴图音频都保留原文件。

`--root` 无效时直接报错，不会悄悄换成别的安装目录。

## 工具数据目录

`%LOCALAPPDATA%\LolDataExtractor`

| 路径 | 说明 |
| --- | --- |
| `hashtable\hashes.game.txt.0..8` | 原始哈希表（文本，222 MB） |
| `hashtable\names.idx` | 名称索引：只保留本地 wad 用到的项（68 MB，冷启动约 0.5 s） |
| `tools\vgmstream\` | 音频解码工具（首次试听时下载，约 4.6 MB） |
| `tools\whisper\ggml-base.bin` | 语音识别模型（141 MB，首次「转换」时从 hf-mirror 下载，也可手动放入） |
| `audio-notes.json` | 每条音频流的备注与转写结果（按 wad + 条目哈希 + 流序号索引） |
| `preview-cache\` | 试听用的临时 bnk / wav，可随时删除 |
| `settings.json` | 上次使用的游戏目录、输出目录（默认 `桌面\LoLAssets`） |

## 工作原理

- **wad v3**：`RW` 魔数 + 版本 + 256 字节 ECDSA 签名 + 校验 + TOC；条目按 `XXH64(小写路径)` 寻址，
  数据以未压缩 / zstd / zstd 分块存储。
- **名称还原**：扫描本地 wad 得到全部条目哈希，与哈希表文本求交集后写入 `names.idx`；
  查询时二分查找哈希，再从 UTF-8 路径池取字符串。
- **内容识别**：DDS `DDS `、纹理 `TEX\0`、静态模型 `r3d2Mesh`、骨骼 `0x22FD4FC3`、网格 `0x00112233`、
  动画 `r3d2anmd`/`r3d2canm`、数据 `PROP`、音频包 `BKHD`、`RIFF…WAVE`、纯文本等，
  未命名条目据此补扩展名并落到 `_unnamed\<哈希前两位>\<哈希>.<扩展名>`。
- **贴图分配**：`.skn` 只写子网格名（`Body` / `Tails` / `Ultcloth01` …），材质表不在包里，
  所以按贴图名的命名约定判断：先剥掉结尾的地图标记（`_tx_cm` / `_cm` / `_tx` / `_diffuse` 是颜色图），
  名字里带 `mask` / `deform` / `rmsa` / `ramp` / `loadscreen` / `glow` / `matcap` 等的是数据图，永不贴到网格；
  不含任何额外词的贴图就是模型自身的主色图。剩下的颜色图按「去掉皮肤名后多出来的词」与子网格名逐词比对
  （驼峰、字母数字、复数都会拆开，例如 `Tailset1`→`tail set 1`、`Mtails`→`tails`），
  多出来的词必须被子网格名全部解释，编号也要一致（`Lv3` 不会拿 `Lv4` 的图）；
  `body` / `main` / `default` 说的是「模型本体」而不是哪个部件，所以它们不会挡路
  （`gwen_base_main_doll_tx_cm` 就是玩偶的贴图）；少于三个字母的词只做前缀匹配
  （`e` 认成 `Eyes`，而技能图 `..._r_tx` 不会因为 "hair" 以 r 结尾就变成头发）。
  平分时取「额外词更少」的那张，即更朴素、更通用的图。
  全不匹配时回退到主色图（无主色图则取 body/整体那张）；整个目录只有遮罩与加载图时不贴图，避免把遮罩当彩色图。
  本机 3,454 个模型 / 15,290 个子网格上验证：按名字命中子部件颜色图的 3,927 个（上一版 3,853），
  其余回退主色图，只有 55 个（目录里仅有遮罩与加载图）不贴图——遮罩类贴图被当作彩色图使用的次数为 0；
  C# 生产代码与 JS 原型逐条比对 15,282 行完全一致。
- **预览**：`.tex` 先由 LeagueToolkit 转成 DDS，再由 BCnEncoder 解码成像素；`.skn` / `.scb` 取其子网格
  生成 `MeshGeometry3D`（scb 没有法线，按面法线做平面着色）；`.bnk` 解析 `BKHD` / `DIDX` / `DATA`
  列出每条 wem；`.wpk` 是 `r3d2` 头 + 每条流的偏移/长度/UTF-16 名字 + 裸 wem，直接按索引切出单条流；
  两者都交给 vgmstream 转码后由 NAudio 播放；扩展名与内容不一致时以内容为准（二进制退回十六进制）。
- **骨骼与动画**：`.skl` 的关节按名字做 ElfHash 与 `.anm` 的关键帧轨道对应；顶点骨骼下标是
  `Skeleton.Influences` 的下标，蒙皮矩阵 = 逆绑定矩阵 × 关节全局矩阵，全局矩阵按 `local × parent` 累积。
  自动匹配打分：同目录同名最高，其次同目录，最后同英雄同皮肤；有自有 `animations/` 目录时不回退到基线皮肤。
- **导出**：每个 wad 内按线程数并行解包（每条线程独占一个 wad 句柄），默认跳过已经导出过的文件，
  并在输出目录写 `extract-<时间>.jsonl` 清单（wad / hash / path / size / outputs / bytes）。
  贴图走 `TEX → DDS → BCnEncoder/Pfim → 自带 PNG 编码器`（Sub 滤波 + zlib，不额外引图像库）；
  `.wem` 交给 vgmstream 转 wav 再由 libmp3lame 编成 MP3，`.bnk` 一次 `vgmstream -S 0` 解出全部子流后逐条编码，
  `.wpk` 按索引切出每条 wem 再逐条转码，多条流的包输出成同名目录（`0001 <流名>.mp3`）。
  转码或解码失败时原样导出并记一条日志，不会丢文件。

## 限制

- 哈希表随游戏版本变化：客户端大版本更新后重新「下载哈希表」并重建索引（界面点一下即可）。
- 3D 预览支持 `.scb` / `.sco` / `.skn`；骨骼动画只解析 `.skl` + `.anm`（现代 LoL 的动画在
  `assets/characters/<英雄>/skins/<皮肤>/animations/` 下），不带根关节位移裁切，走路类动画会整体平移；
  `.mapgeo` / `.wgeo` / `.nvr` 体量太大，暂不渲染。
- 音频必须是 Wwise 的 wem（fmt 0xFFFF）。角色音效在 `.bnk`（`_audio.bnk` 有流，`_events.bnk` 只有事件），
  语音在 `.wpk`（`r3d2` 容器，逐条 wem）；少数 `_vo_audio.bnk` 只有元数据，预览会自动切到同名的 `.wpk`。
  试听依赖 vgmstream，缺失时界面提供一键下载。
- 不写入游戏目录：所有读取只针对 `DATA` 下的 wad，输出只写到用户指定目录。
- Windows 路径上限 260 字符，超长文件名会截断并追加 10 位哈希后缀（仍然唯一、可直接打开）。

## 开发

```powershell
dotnet build LolDataExtractor.sln
dotnet test tests\LolDataExtractor.Tests      # 182 项：哈希/名称表/识别/过滤/安装目录/合成 wad 解包/
                                             # 树构建/贴图解码/Wwise 与 WPK 解析/模型解析/骨骼动画/资源匹配/贴图分配/
                                             # PNG 编码/媒体格式规划/备注存储/转写语言/版本信息/预览分派/编码守卫
tools\check.ps1                               # 构建 + 测试 + 编码体检（UTF-8 无 BOM + LF）
tools\package.ps1                             # 打发布包：单文件 exe + runtimes\win-x64 原生库 + 带文件夹的 zip
tools\make-icon.ps1                           # 由 logo.png 重新生成 Assets\logo.ico（多尺寸 BMP 帧）
```

发布包用 `tools\package.ps1` 生成（版本号取自 `Directory.Build.props`）：程序目录里只有
`LolDataExtractor.exe`，所有原生 DLL 都收在 `runtimes\win-x64\`，zip 里再套一层同名文件夹，
解压出来就是一整个目录而不是一堆散件。原生库位置由 `src/LolDataExtractor.Shared/NativeSearchPath.cs`
在进程启动时注册（`SetDllDirectory` + 逐程序集的 P/Invoke 解析器），Whisper.net 自己会在
`runtimes\<rid>\` 里找。

截图旁边会同时写一份同名 `.log`（导出/转写/自检的日志行），脚本核对数字不必读像素；
`--wad-filter` 先按名称过滤 wad 列表再执行 `--select-all`，`--export-stream` 走的是预览页「导出此文件」，
把音频列表里选中的那一条流导出到给定目录。

目录结构：

```
src/LolDataExtractor.Core   解析、预览解码与导出核心（无 UI 依赖）
  Preview/                  WwiseBank（bnk + wpk）、TextureDecoder、ModelDecoder、SkeletonData、
                            AnimationClip、Skinning、AssetLinker、TextureBinder、PreviewService、VgmstreamTool
  MediaExporter.cs          贴图/音频转 PNG、MP3、WAV（含 Mp3Encoder）
  PngEncoder.cs             自带 PNG 编码器（RGBA8，Sub 滤波 + zlib）
  SpeechRecognizer.cs       whisper 模型下载与转写（Transcriber）
  AudioNoteStore.cs         音频备注的存取（audio-notes.json）
  AppInfo.cs                版本号与发布日期（取自程序集属性，界面副标题与 CLI info 共用）
src/LolDataExtractor.Cli    loldata 命令行
src/LolDataExtractor.Shared 两个可执行文件共用的启动代码（原生库搜索路径注册）
src/LolDataExtractor.App    WPF + HandyControl 界面（Controls/ModelPreviewView 为 3D 预览，
                            Services/ModelScene 负责逐帧蒙皮，Controls/EntryPickerWindow 为手动选资源）
tests/LolDataExtractor.Tests 单元与集成测试（合成 wad / 合成 bnk / 合成 DDS / 真实 Ahri wad 骨骼动画）
tools/check.ps1             一键体检
tools/make-icon.ps1         logo.png → logo.ico
```

## 许可证

GNU General Public License v3.0，见 [LICENSE](LICENSE)。本项目只读取本机游戏资源、不修改游戏文件，
也不分发任何 Riot Games 的素材；导出的资源版权归 Riot Games 所有，仅供个人学习与研究使用。
