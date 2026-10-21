<#
.SYNOPSIS
    Publica o Atlasmith como um único .exe que roda em qualquer Windows,
    sem precisar instalar .NET nem nada.

.DESCRIPTION
    Saem dois arquivos idênticos em dist\:
      Atlasmith.exe               versão portátil: abre direto, não instala nada.
      Atlasmith-<versão>-setup.exe o mesmo programa; com "setup" no nome, abre o
                                   instalador (pasta do usuário, atalhos, desinstalação
                                   pelo Windows).

.EXAMPLE
    .\build.ps1
    .\build.ps1 -Runtime win-arm64
#>

[CmdletBinding()]
param(
    [ValidateSet('win-x64', 'win-arm64')]
    [string]$Runtime = 'win-x64',

    [string]$Output = 'dist'
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$project = Join-Path $root 'src\Atlasmith\Atlasmith.csproj'
$outDir = Join-Path $root $Output

[xml]$csproj = Get-Content $project
$version = ($csproj.Project.PropertyGroup | Where-Object { $_.Version } | Select-Object -First 1).Version

Write-Host "Publicando Atlasmith $version ($Runtime)..." -ForegroundColor Cyan

dotnet publish $project `
    --configuration Release `
    --runtime $Runtime `
    --self-contained true `
    --output $outDir `
    -p:PublishSingleFile=true `
    -p:EnableCompressionInSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:DebugType=none `
    -p:GenerateDocumentationFile=false

if ($LASTEXITCODE -ne 0) {
    throw "A publicação falhou (código $LASTEXITCODE)."
}

$exe = Join-Path $outDir 'Atlasmith.exe'
if (-not (Test-Path $exe)) {
    throw "O executável não foi gerado em $exe."
}

$suffix = if ($Runtime -eq 'win-x64') { '' } else { "-$($Runtime -replace '^win-', '')" }
$setup = Join-Path $outDir "Atlasmith-$version$suffix-setup.exe"
Copy-Item $exe $setup -Force

$sizeMb = [math]::Round((Get-Item $exe).Length / 1MB, 1)

Write-Host ""
Write-Host "Pronto ($sizeMb MB cada):" -ForegroundColor Green
Write-Host "  $exe"
Write-Host "      portátil: copie só ele para qualquer PC com Windows."
Write-Host "  $setup"
Write-Host "      instalador: atalhos opcionais e desinstalação pelo Windows."
