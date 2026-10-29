<#
    rabbit inside 배포 스크립트

    두 가지 방식으로 단일 exe를 만듭니다.

        ./publish.ps1                  프레임워크 종속(작음, .NET 데스크톱 런타임 필요)
        ./publish.ps1 -SelfContained   자기완결형(약 150MB, 설치 필요 없음)

    결과는 publish\<runtime>-<방식>\ 폴더에 나옵니다.
#>
param(
    [string]$Configuration = 'Release',
    [string]$Runtime = 'win-x64',
    [switch]$SelfContained,
    [string]$Output
)

$ErrorActionPreference = 'Stop'
$repo = $PSScriptRoot

$dotnet = Join-Path $env:LOCALAPPDATA 'Microsoft\dotnet\dotnet.exe'
if (-not (Test-Path $dotnet)) {
    $command = Get-Command dotnet -ErrorAction SilentlyContinue
    if (-not $command) {
        throw 'The .NET SDK was not found. Install it from https://dot.net and try again.'
    }

    $dotnet = $command.Source
}

$env:APPDATA = Join-Path $repo '.appdata'
$env:DOTNET_CLI_HOME = Join-Path $repo '.dotnet-home'
$env:NUGET_PACKAGES = Join-Path $repo '.nuget'
$env:DOTNET_NOLOGO = '1'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'

New-Item -ItemType Directory -Force -Path $env:APPDATA, $env:DOTNET_CLI_HOME, $env:NUGET_PACKAGES |
    Out-Null

$flavor = if ($SelfContained) { 'self-contained' } else { 'framework-dependent' }
if (-not $Output) {
    $Output = Join-Path $repo "publish\$Runtime-$flavor"
}

$arguments = @(
    'publish', (Join-Path $repo 'src\Rabbit\Rabbit.csproj'),
    '-c', $Configuration,
    '-r', $Runtime,
    '-o', $Output,
    '--nologo',
    '-p:PublishSingleFile=true',
    '-p:IncludeNativeLibrariesForSelfExtract=true'
)

if ($SelfContained) {
    $arguments += '--self-contained', 'true'
    $arguments += '-p:EnableCompressionInSingleFile=true'
}
else {
    $arguments += '--self-contained', 'false'
}

& $dotnet @arguments
if ($LASTEXITCODE -ne 0) {
    exit $LASTEXITCODE
}

Write-Output ''
Write-Output "Output: $Output"
Get-ChildItem $Output -File |
    Sort-Object Length -Descending |
    Select-Object Name, @{ Name = 'SizeMB'; Expression = { [math]::Round($_.Length / 1MB, 1) } } |
    Format-Table -AutoSize
