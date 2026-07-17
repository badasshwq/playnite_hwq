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

# 3) 编译解决方案的桌面版目标（必须编 sln + 显式 x86，不能直接编 csproj）
Write-Step "编译 Playnite.DesktopApp（$config / x86）..."
& $msbuild $sln /t:Playnite_DesktopApp /p:Configuration=$config /p:Platform=x86 /m /v:minimal /nologo
if ($LASTEXITCODE -ne 0) {
    Write-Host "`n编译失败（退出码 $LASTEXITCODE）。请查看上面的 error 信息。" -ForegroundColor Red
    exit $LASTEXITCODE
}
Write-Host "  编译成功" -ForegroundColor Green

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
