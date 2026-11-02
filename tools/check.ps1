# Builds the solution, runs the tests and checks that every source file is
# UTF-8 without BOM and uses LF line endings.
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
Push-Location $root
try {
    Write-Host '== build ==' -ForegroundColor Cyan
    dotnet build DawnResourceExtractionTool.sln -v q --nologo
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

    Write-Host '== test ==' -ForegroundColor Cyan
    dotnet test tests\DawnResourceExtractionTool.Tests -v q --nologo
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

    Write-Host '== encoding ==' -ForegroundColor Cyan
    $patterns = @('*.cs', '*.xaml', '*.csproj', '*.props', '*.sln', '*.md', '*.json', '*.ps1', '*.yml')
    $files = Get-ChildItem -Path $root -Recurse -File -Include $patterns |
        Where-Object { $_.FullName -notmatch '\\(bin|obj|out|dist|testdata)\\' }
    $offenders = @()
    foreach ($file in $files) {
        $bytes = [System.IO.File]::ReadAllBytes($file.FullName)
        if ($bytes.Length -ge 3 -and $bytes[0] -eq 0xEF -and $bytes[1] -eq 0xBB -and $bytes[2] -eq 0xBF) {
            $offenders += "BOM  $($file.FullName)"
            continue
        }
        $text = [System.Text.Encoding]::UTF8.GetString($bytes)
        if ($text.Contains("`r`n")) { $offenders += "CRLF $($file.FullName)" }
    }
    if ($offenders.Count -gt 0) {
        $offenders | ForEach-Object { Write-Host $_ -ForegroundColor Red }
        exit 1
    }
    Write-Host "checked $($files.Count) files: utf-8 no BOM, LF only" -ForegroundColor Green
}
finally {
    Pop-Location
}
