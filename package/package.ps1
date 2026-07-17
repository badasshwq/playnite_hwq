#Requires -Version 7

# 打包脚本：编译 Playnite 并在本目录（package\）生成带版本号的便携版 zip
# 用法：.\package.ps1              （默认 Release/x86）
#       .\package.ps1 -Configuration Debug
param(
    [ValidateSet("Release", "Debug")]
    [string]$Configuration = "Release",

    [ValidateSet("x86", "x64")]
    [string]$Platform = "x86"
)

$ErrorActionPreference = "Stop"
$pkgDir = $PSScriptRoot
$root = Split-Path $pkgDir -Parent

# 官方 build.ps1 依赖 powershell-yaml 模块做清单校验，缺了先装
if (!(Get-Module powershell-yaml -ListAvailable))
{
    Install-Module powershell-yaml -Force -Scope CurrentUser
}

# 官方脚本内部会 Set-Location 到 build 目录，用 Push/Pop 保护当前位置
Push-Location $root
try
{
    # -Package 生成 zip；-InstallerDir 指定 zip 落到 package 目录
    & (Join-Path $root "build\build.ps1") -Configuration $Configuration -Platform $Platform -Package -InstallerDir $pkgDir
    if (!$?)
    {
        throw "构建失败，详见上方输出。"
    }
}
finally
{
    Pop-Location
}

# 从编译产物读版本号，重命名 zip
$outputDll = Join-Path $root "build\$Configuration\Playnite.dll"
$version = (Get-Item $outputDll).VersionInfo.FileVersion
$targetZip = Join-Path $pkgDir "Playnite-$version-hwq.zip"
Move-Item (Join-Path $pkgDir "Playnite.zip") $targetZip -Force

Write-Host "打包完成：$targetZip" -ForegroundColor Green
