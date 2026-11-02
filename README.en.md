# DawnResourceExtractionTool 1.2.0

[中文](README.md) | [English](README.en.md)

## Features

+ Scans, parses, restores and exports game assets
+ Rich browser: a folder tree, plus in-app previews for models, images, audio, video and text
+ Links a model's skeleton, textures and animations automatically, for easier previewing and exporting

## Dependencies

+ Container parsing: [LeagueToolkit](https://www.nuget.org/packages/LeagueToolkit) 3.0.3
+ Texture decoding: [BCnEncoder.Net](https://www.nuget.org/packages/BCnEncoder.Net) 2.3.0
+ 3D view: [HelixToolkit.Wpf](https://www.nuget.org/packages/HelixToolkit.Wpf) 3.1.2
+ Audio playback: [NAudio](https://www.nuget.org/packages/NAudio) 3.3.0 + [vgmstream](https://github.com/vgmstream/vgmstream)
+ MP3 encoding: [NAudio.Lame](https://www.nuget.org/packages/NAudio.Lame) 2.1.0
+ Speech transcription: [Whisper.net](https://www.nuget.org/packages/Whisper.net) 1.9.1
+ UI: [HandyControl](https://github.com/HandyOrg/HandyControl) 3.5.1

## Quick start

```powershell
# graphics window
dotnet run --project src/DawnResourceExtractionTool.App
# or the published executable
dist\app\DawnResourceExtractionTool.exe
```

Command line (the published `dawn.exe` next to `DawnResourceExtractionTool.exe`):

```powershell
# installation folder, index and wad summary
dotnet run --project src/DawnResourceExtractionTool.Cli -- info

# download the hash tables and build the local name index
dotnet run --project src/DawnResourceExtractionTool.Cli -- hashes fetch --build

# scan every wad (optionally as JSON)
dotnet run --project src/DawnResourceExtractionTool.Cli -- scan --json out\scan.json

# list the model files of one wad (<wad> is a wad name or a wildcard)
dotnet run --project src/DawnResourceExtractionTool.Cli -- list <wad> --ext skn,skl --limit 20

# export a champion's base skin model / textures / voice (textures become PNG, audio becomes MP3)
dotnet run --project src/DawnResourceExtractionTool.Cli -- extract --wad <wad> --out D:\LoLAssets `
    --filter "*skins/base/*" --category texture,model,audio --threads 8

# media only: texture PNG + voice MP3, everything else is dropped
dotnet run --project src/DawnResourceExtractionTool.Cli -- media --wad <wad>.zh_CN --out D:\LoLAssets

# classify every entry by category and count them (--bucket one section, --search test a path, --json details)
dotnet run --project src/DawnResourceExtractionTool.Cli -- catalog --root <install folder>
dotnet run --project src/DawnResourceExtractionTool.Cli -- catalog --bucket skins --limit 10

# client assets: point --root at the install folder and list / scan / extract / media include LeagueClient\plugins
dotnet run --project src/DawnResourceExtractionTool.Cli -- list <client wad> --root <install folder> --filter=*v1/champions/*.json
dotnet run --project src/DawnResourceExtractionTool.Cli -- extract --root <install folder> --wad <client wad> --filter=*champion-icons* --out D:\LoLAssets
```

Common options: `--root` the game DATA folder, `--wad` wad wildcard, `--filter` path wildcard, `--path` folder prefix,
`--category` texture/model/audio/bin/text/shader/other, `--ext` extension list, `--min-size` / `--max-size`,
`--flat` flat output, `--overwrite` replace existing files, `--no-unnamed` drop unnamed entries, `--dry-run` report only,
`--audio mp3|wav|keep` audio format (mp3 by default), `--raw` keep textures and audio untouched, `--lang zh|en` output language.

An invalid `--root` is an error; the tool never silently switches to another installation.

Client archives follow `--root`: the install folder also lists the archives under `LeagueClient\plugins`, `Game\DATA` scans the game only.

## Program data

Location: `%LOCALAPPDATA%\DawnResourceExtractionTool` (an older `LolDataExtractor` folder is moved over on first start)

| Path | Contents |
| --- | --- |
| `hashtable\` | hash tables and name index |
| `tools\vgmstream\` | audio decoder |
| `tools\whisper\ggml-base.bin` | speech model |
| `audio-notes.json` | audio notes |
| `catalog-overrides.json` | manual category corrections |
| `preview-cache\` | audio cache |
| `settings.json` | settings (interface theme and language included) |

## Localization

Every text in the interface comes from a localization key; the two languages live side by side in
`src/DawnResourceExtractionTool.Core/Localization/strings.*.json`:

```json
{
  "Ui.Main.Title": { "zh": "破晓资源提取工具", "en": "Dawn Resource Extraction Tool" }
}
```

Code calls `Strings.T("key")`, XAML uses `{loc:Loc key}`; another language is one more column per file.

The interface language also decides what an export holds: client json ships once per language folder
(`global\default` English, `global\zh_cn` Chinese), and an export by category takes the copy of the
interface language, falling back to English — no export folder ever gets two files of the same name.

## Development

```powershell
dotnet build DawnResourceExtractionTool.sln   # build
dotnet test tests\DawnResourceExtractionTool.Tests   # tests
tools\check.ps1   # build + tests + encoding check
tools\package.ps1   # package
tools\make-icon.ps1    # icon
```

## Notice

This project only reads the game files already on this machine, never modifies them, and redistributes no Riot Games assets. Exported assets remain the property of Riot Games and are meant for personal study and research only.
