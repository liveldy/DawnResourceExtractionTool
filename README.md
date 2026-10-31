# 英雄联盟数据提取工具 (LoL Data Extractor) 1.1.1

## 功能与特色

+ 游戏资源扫描、解析、还原和导出
+ 丰富化浏览，目录支持树形结构、可在程序内浏览模型、图像、音频、视频、文本等信息
+ 自动匹配模型的骨骼、纹理和动作，方便导出和浏览

## 依赖与来源

+ 解析引擎 [LeagueToolkit](https://www.nuget.org/packages/LeagueToolkit) 3.0.3
+ 贴图解码：[BCnEncoder.Net](https://www.nuget.org/packages/BCnEncoder.Net) 2.3.0
+ 3D 视图：[HelixToolkit.Wpf](https://www.nuget.org/packages/HelixToolkit.Wpf) 3.1.2
+ 音频播放：[NAudio](https://www.nuget.org/packages/NAudio) 3.3.0 + [vgmstream](https://github.com/vgmstream/vgmstream)
+ MP3 编码：[NAudio.Lame](https://www.nuget.org/packages/NAudio.Lame) 2.1.0
+ 语音转写：[Whisper.net](https://www.nuget.org/packages/Whisper.net) 1.9.1
+ UI界面：[HandyControl](https://github.com/HandyOrg/HandyControl) 3.5.1

## 快速开始

```powershell
# 图形界面
dotnet run --project src/LolDataExtractor.App
# 或已发布的可执行文件
dist\app\LolDataExtractor.exe
```

命令行：

```powershell
# 查看安装目录、索引与 wad 概况
dotnet run --project src/LolDataExtractor.Cli -- info

# 下载哈希表并生成本地名称索引
dotnet run --project src/LolDataExtractor.Cli -- hashes fetch --build

# 扫描全部 wad（可导出 JSON）
dotnet run --project src/LolDataExtractor.Cli -- scan --json out\scan.json

# 列出某个 wad 里的模型文件（<wad> 换成 wad 名或通配符，例如 Champions 下的某个名字）
dotnet run --project src/LolDataExtractor.Cli -- list <wad> --ext skn,skl --limit 20

# 导出某个英雄的基础皮肤模型/贴图/语音（贴图转 PNG、音频转 MP3）
dotnet run --project src/LolDataExtractor.Cli -- extract --wad <wad> --out D:\LoLAssets `
    --filter "*skins/base/*" --category texture,model,audio --threads 8

# 只要二次解析后的媒体：贴图 PNG + 语音 MP3，其余一概不导出
dotnet run --project src/LolDataExtractor.Cli -- media --wad <wad>.zh_CN --out D:\LoLAssets

# 按类别把全部条目分类，看每类的条目数（--bucket 只看一类，--search 试搜路径，--json 存明细）
dotnet run --project src/LolDataExtractor.Cli -- catalog --root <安装目录>
dotnet run --project src/LolDataExtractor.Cli -- catalog --bucket 皮肤 --limit 10

# 客户端资产：--root 指到安装根目录，list / scan / extract / media 就一起带上 LeagueClient\plugins 下的包
dotnet run --project src/LolDataExtractor.Cli -- list <客户端 wad> --root <安装目录> --filter=*v1/champions/*.json
dotnet run --project src/LolDataExtractor.Cli -- extract --root <安装目录> --wad <客户端 wad> --filter=*champion-icons* --out D:\LoLAssets
```

常用参数：`--root` 游戏 DATA 目录、`--wad` wad 通配符、`--filter` 路径通配符、`--path` 目录前缀、
`--category` texture/model/audio/bin/text/shader/other、`--ext` 扩展名列表、`--min-size` / `--max-size`、
`--flat` 扁平输出、`--overwrite` 覆盖已有文件、`--no-unnamed` 丢弃未命名条目、`--dry-run` 只看统计、
`--audio mp3|wav|keep` 音频格式（默认 mp3）、`--raw` 贴图音频都保留原文件。

`--root` 无效时直接报错，不会悄悄换成别的安装目录。

客户端包跟着 `--root` 走：指到安装根目录时一起列出 `LeagueClient\plugins` 下的包，指到 `Game\DATA` 时只扫游戏包。

## 程序数据

位置： `%LOCALAPPDATA%\LolDataExtractor`

| 路径 | 说明 |
| --- | --- |
| `hashtable\` |哈希表与名称索引|
| `tools\vgmstream\`| 音频解码工具|
| `tools\whisper\ggml-base.bin` |语音识别模型|
| `audio-notes.json` |音频数据|
| `catalog-overrides.json` |分类修正配置|
| `preview-cache\` |音频缓存|
| `settings.json` |程序配置|

## 开发

```powershell
dotnet build LolDataExtractor.sln   # 构建
dotnet test tests\LolDataExtractor.Tests   # 测试
tools\check.ps1   # 构建 + 测试 + 编码体检
tools\package.ps1   # 打包
tools\make-icon.ps1    #生成图标
```

## 声明

本项目只读取本机游戏资源、不修改游戏文件，也不分发任何 Riot Games 的素材；导出的资源版权归 Riot Games 所有，仅供个人学习与研究使用。
