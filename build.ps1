<#
    rabbit inside 빌드 스크립트

    - .NET SDK가 PATH에 없어도 동작합니다(사용자 폴더 설치 기준).
    - 사용자 프로필 읽기가 막힌 환경(샌드박스 등)에서도 빌드되도록 NuGet 관련
      경로를 저장소 안(.appdata / .dotnet-home / .nuget)으로 돌립니다.

    사용법:
        ./build.ps1            빌드
        ./build.ps1 -Run       빌드 후 실행
        ./build.ps1 -Configuration Release
#>
param(
    [string]$Configuration = 'Debug',
    [switch]$Run
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

& $dotnet build (Join-Path $repo 'Rabbit.slnx') -c $Configuration --nologo
if ($LASTEXITCODE -ne 0) {
    exit $LASTEXITCODE
}

if ($Run) {
    & (Join-Path $repo "src\Rabbit\bin\$Configuration\net10.0-windows\rabbit inside.exe")
}
