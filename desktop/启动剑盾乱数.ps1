param(
    [switch]$SkipBuild,
    [string]$Python = 'python',
    [string]$ToolRoot = 'D:\CodexTools\auto-swsh-rng'
)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
& $Python -c 'import PySide6, cv2, numpy, serial'
if ($LASTEXITCODE -ne 0) {
    Write-Host '请先安装界面依赖：'
    Write-Host "python -m pip install -r `"$PSScriptRoot\requirements.txt`""
    exit 1
}
$packagedBackend = Join-Path $PSScriptRoot 'backend\AutoSwshRng.Cli.exe'
$buildProject = Join-Path $projectRoot 'src\AutoSwshRng.Cli\AutoSwshRng.Cli.csproj'
if ((Test-Path -LiteralPath $packagedBackend) -and ($SkipBuild -or -not (Test-Path -LiteralPath $buildProject))) {
    & $Python (Join-Path $PSScriptRoot 'run_pyside6_gui.py') --backend $packagedBackend
    exit $LASTEXITCODE
}
$localDotnet = Join-Path $ToolRoot 'dotnet\dotnet.exe'
$dotnetExe = if (Test-Path -LiteralPath $localDotnet) { $localDotnet } else { 'dotnet' }
$artifactRoot = Join-Path $ToolRoot 'artifacts'
$env:DOTNET_CLI_HOME = Join-Path $ToolRoot 'cli-home'
$env:NUGET_PACKAGES = Join-Path $ToolRoot 'nuget-packages'
$env:TEMP = Join-Path $ToolRoot 'temp'
$env:TMP = $env:TEMP
New-Item -ItemType Directory -Force -Path $env:TEMP | Out-Null
if (Test-Path -LiteralPath $localDotnet) { $env:DOTNET_ROOT = Split-Path -Parent $localDotnet }
if (-not $SkipBuild) {
    & $dotnetExe build $buildProject --artifacts-path $artifactRoot --verbosity quiet
    if ($LASTEXITCODE -ne 0) { throw '计算服务构建失败，请检查上方输出。' }
}
$backendExe = Join-Path $artifactRoot 'bin\AutoSwshRng.Cli\debug\AutoSwshRng.Cli.exe'
if (-not (Test-Path -LiteralPath $backendExe)) { throw "未找到计算服务：$backendExe" }
& $Python (Join-Path $PSScriptRoot 'run_pyside6_gui.py') --backend $backendExe
exit $LASTEXITCODE
