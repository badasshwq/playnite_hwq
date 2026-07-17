# 一键编译并启动 Playnite 桌面版（Debug/x86）
# 用途：编辑用 IDEA、Git 用 IDEA，运行只需双击 run.cmd（它会调用本脚本）
# 自动处理三个常见坑：先杀掉占用 DLL 的旧实例、强制走 x86 平台、编译 sln 而非单个 csproj。

param(
    [switch]$Release,          # 加 -Release 出 Release 版；默认 Debug
    [switch]$NoRun             # 只编译不启动
)

$ErrorActionPreference = "Stop"
$repo = $PSScriptRoot
$config = if ($Release) { "Release" } else { "Debug" }
$msbuild = "D:\develop\VS2022\MSBuild\Current\Bin\MSBuild.exe"
$sln = Join-Path $repo "source\Playnite.sln"
$exe = Join-Path $repo "source\Playnite.DesktopApp\bin\x86\$config\Playnite.DesktopApp.exe"

function Write-Step($msg) { Write-Host "`n==> $msg" -ForegroundColor Cyan }

# 1) 杀掉正在运行的 Playnite 及其 CefSharp 子进程（否则 DLL 被锁，编译失败）
Write-Step "关闭正在运行的 Playnite 实例..."
$procs = Get-Process Playnite.DesktopApp, Playnite.FullscreenApp, CefSharp.BrowserSubprocess -ErrorAction SilentlyContinue
if ($procs) {
    $procs | Stop-Process -Force
    Start-Sleep -Seconds 2
    Write-Host "  已终止 $($procs.Count) 个进程" -ForegroundColor DarkGray
} else {
    Write-Host "  无需终止" -ForegroundColor DarkGray
}

# 2) 校验 MSBuild 存在
if (-not (Test-Path $msbuild)) {
    Write-Host "找不到 MSBuild：$msbuild" -ForegroundColor Red
    Write-Host "如果你的 VS 装在别处，请修改本脚本顶部的 `$msbuild 路径。" -ForegroundColor Yellow
    exit 1
}

# 3) 编译桌面版 + 全屏版（两个 exe 必须都在同一输出目录，否则模式切换会“找不到文件”）
#    必须编 sln + 显式 x86，不能直接编单个 csproj。
Write-Step "编译 Playnite.DesktopApp + Playnite.FullscreenApp（$config / x86）..."
& $msbuild $sln /t:"Playnite_DesktopApp;Playnite_FullscreenApp" /p:Configuration=$config /p:Platform=x86 /m /v:minimal /nologo
if ($LASTEXITCODE -ne 0) {
    Write-Host "`n编译失败（退出码 $LASTEXITCODE）。请查看上面的 error 信息。" -ForegroundColor Red
    exit $LASTEXITCODE
}
Write-Host "  编译成功" -ForegroundColor Green

# 3b) 把全屏版产物合并到桌面版输出目录。
#     桌面版“切换到全屏模式”会去自己所在目录找 Playnite.FullscreenApp.exe（正式打包时两个 exe 同目录），
#     但开发时它们各自编到独立的 bin 目录，导致切换全屏时“找不到文件”。这里把全屏产物拷过来补齐。
Write-Step "合并全屏版产物到桌面版目录..."
$desktopOut = Join-Path $repo "source\Playnite.DesktopApp\bin\x86\$config"
$fullscreenOut = Join-Path $repo "source\Playnite.FullscreenApp\bin\x86\$config"
if (Test-Path $fullscreenOut) {
    # 只拷贝全屏版特有、桌面版目录里尚不存在或更旧的文件，避免覆盖共享 DLL 造成的无谓改动
    Copy-Item (Join-Path $fullscreenOut "Playnite.FullscreenApp.exe") $desktopOut -Force
    $fsConfig = Join-Path $fullscreenOut "Playnite.FullscreenApp.exe.config"
    if (Test-Path $fsConfig) { Copy-Item $fsConfig $desktopOut -Force }
    # 全屏版专属主题目录（Themes\Fullscreen）
    $fsThemes = Join-Path $fullscreenOut "Themes\Fullscreen"
    if (Test-Path $fsThemes) {
        Copy-Item $fsThemes (Join-Path $desktopOut "Themes") -Recurse -Force
    }
    Write-Host "  已合并全屏版 exe/主题" -ForegroundColor Green
} else {
    Write-Host "  警告：未找到全屏版输出目录，切换全屏可能失败" -ForegroundColor Yellow
}

# 4) 启动
if ($NoRun) {
    Write-Step "已按 -NoRun 跳过启动。产物：$exe"
    exit 0
}
if (-not (Test-Path $exe)) {
    Write-Host "编译成功但找不到 exe：$exe" -ForegroundColor Red
    exit 1
}
Write-Step "启动 Playnite..."
Start-Process $exe
Write-Host "  已启动：$exe" -ForegroundColor Green
