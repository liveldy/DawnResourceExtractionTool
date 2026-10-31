# Builds the two release packages.
#
#   powershell -ExecutionPolicy Bypass -File tools\package.ps1
#
# Each package is a self contained single file executable plus one runtimes\win-x64 folder
# that holds every native library, and the zip carries the folder itself so unpacking does
# not scatter files. Output: dist\<name>-V<version>-win-x64.zip

[CmdletBinding()]
param(
    [string]$OutputDirectory = 'dist'
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
Set-Location $root

$props = Get-Content 'Directory.Build.props' -Raw
$version = [regex]::Match($props, '<Version>([^<]+)</Version>').Groups[1].Value
if (-not $version) { throw 'Version not found in Directory.Build.props' }
Write-Host "version $version"

function New-Package {
    param(
        [string]$Project,
        [string]$Name,
        [string]$Executable
    )

    $rid = 'win-x64'
    $raw = Join-Path $OutputDirectory "raw-$Name"
    $folderName = "$Name-V$version-$rid"
    $folder = Join-Path $OutputDirectory $folderName
    $zip = Join-Path $OutputDirectory "$folderName.zip"

    foreach ($path in @($raw, $folder, $zip)) {
        if (Test-Path $path) { Remove-Item $path -Recurse -Force }
    }

    dotnet publish $Project -c Release -r $rid --self-contained true --nologo `
        -p:PublishSingleFile=true `
        -p:EnableCompressionInSingleFile=true `
        -p:IncludeNativeLibrariesForSelfExtract=false `
        -p:DebugType=none `
        -o $raw | Out-Host
    if ($LASTEXITCODE -ne 0) { throw "publish failed: $Project" }

    New-Item -ItemType Directory -Path $folder -Force | Out-Null

    # The executable stays alone at the top; every native library moves into runtimes\<rid>.
    $nativeFolder = Join-Path $folder "runtimes\$rid"
    New-Item -ItemType Directory -Path $nativeFolder -Force | Out-Null
    Move-Item (Join-Path $raw $Executable) (Join-Path $folder $Executable)

    # Startup page background video (App package only): sits next to the exe, ships inside the zip.
    $video = Join-Path $raw 'load.mp4'
    if (Test-Path $video) { Move-Item $video (Join-Path $folder 'load.mp4') }

    Get-ChildItem $raw -Filter *.dll -File | Move-Item -Destination $nativeFolder
    if (Test-Path (Join-Path $raw "runtimes\$rid")) {
        Get-ChildItem (Join-Path $raw "runtimes\$rid") -File | Move-Item -Destination $nativeFolder
    }

    # Leftovers of the package set: other platforms' natives, macOS shaders, symbols.
    Remove-Item $raw -Recurse -Force

    Compress-Archive -Path $folder -DestinationPath $zip -CompressionLevel Optimal
    Remove-Item $folder -Recurse -Force

    $size = (Get-Item $zip).Length / 1MB
    Write-Host ("{0}  {1:N1} MB" -f (Split-Path $zip -Leaf), $size)
}

New-Package -Project 'src/LolDataExtractor.App/LolDataExtractor.App.csproj' `
             -Name 'LolDataExtractor' -Executable 'LolDataExtractor.exe'
New-Package -Project 'src/LolDataExtractor.Cli/LolDataExtractor.Cli.csproj' `
             -Name 'LolDataExtractor-Cli' -Executable 'loldata.exe'

Write-Host 'done'
