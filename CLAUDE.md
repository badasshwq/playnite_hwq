# Playnite

开源 Windows 游戏库管理器/启动器（Josef Nemec，MIT）：把 Steam、Epic、GOG、Xbox、战网等商店及模拟器游戏统一管理和启动。仅支持 Windows 10/11，数据全部本地存储。本仓库为上游 `JosefNemec/Playnite` 的克隆，用于二次开发（基于 master@10.56）。

## 技术栈

- **.NET Framework 4.6.2 + WPF**，C# 7.3；老式 csproj + `packages.config`（非 SDK-style 工程）
- MVVM（Prism 6.3 + Microsoft.Xaml.Behaviors）
- 数据库 **LiteDB 4**（刻意不升 v5，见 `ItemCollection.cs` 注释）；内嵌浏览器 CefSharp；日志 NLog；手柄输入 SDL；脚本运行时 Windows PowerShell 5.1
- 规模：`source\` 约 748 个 .cs / 14.3 万行，286 个 XAML

## 解决方案结构（source\Playnite.sln，18 个项目）

依赖方向：**DesktopApp / FullscreenApp → Playnite（核心）→ PlayniteSDK（契约）**

| 项目 | 职责 |
|---|---|
| `PlayniteSDK` | 对外 SDK（`Playnite.SDK.dll`，NuGet 包名 `PlayniteSDK`）：领域模型（`Models\Game.cs` 等）、插件基类、`IPlayniteAPI` |
| `Playnite` | 核心库（最大）：数据层、插件加载器、元数据下载、游戏启动控制器、模拟器、脚本引擎、`IPlayniteAPI` 实现 |
| `Playnite.DesktopApp` | 桌面模式 UI（键鼠，`Playnite.exe`），入口 `ProgramEntry.cs` |
| `Playnite.FullscreenApp` | 全屏/沙发模式 UI（手柄导航），结构与桌面版镜像 |
| `Tools` | `Playnite.Toolbox`（扩展/主题脚手架与打包 CLI）、`PlayniteInstaller`、`Playnite.Utilities` |
| `Tests` | NUnit 3 单元测试 + 测试用样例插件（TestGameLibrary/TestPlugin 等） |

关键文件速查：

- 领域模型：`source\PlayniteSDK\Models\Game.cs`（基类 `DatabaseObject`，`Guid Id`）
- 数据层：`source\Playnite\Database\GameDatabase.cs`、`Collections\ItemCollection.cs` — 每个集合（games/platforms/genres…）一个独立 LiteDB 文件 + `files` 媒体目录，启动时并行加载进内存 `ConcurrentDictionary`，内建损坏检测与重建
- 启动/生命周期：`source\Playnite\App\PlayniteApplication.cs`（抽象基类，单实例 Mutex+命名管道）→ `DesktopApplication.cs` / `FullscreenApplication.cs`
- 插件加载：`source\Playnite\Plugins\ExtensionFactory.cs`；清单类 `source\Playnite\Manifests\`；路径常量 `source\Playnite\Settings\PlaynitePaths.cs`
- 主题管理：`source\Playnite\Themes.cs`（XAML ResourceDictionary 覆盖机制，Desktop/Fullscreen 两条独立主题线）
- 本地化：`LocalizationKeys.cs` 是生成物，由 `build\buildLocConstants.ps1` 从 `source\Playnite\Localization\LocSource.xaml` 生成，改动 LocSource.xaml 后需重跑

## 扩展体系

四种扩展方式，统一用 `extension.yaml` / `theme.yaml` 清单，打包为 `.pext` / `.pthm`：

1. .NET 插件（引用 PlayniteSDK 的 class library）：`LibraryPlugin`（商店集成）/ `MetadataPlugin`（元数据源）/ `GenericPlugin`（事件、菜单、自定义 UI），基类在 `source\PlayniteSDK\Plugins\`
2. PowerShell 脚本扩展（`.psm1`，自动注入 `$PlayniteApi`；依赖 Windows PowerShell 5.1）
3. 主题（纯 XAML 覆盖，主题 API 版本当前 2.9.0，主版本号必须匹配）
4. 插件一切能力经 `IPlayniteAPI`（`source\PlayniteSDK\IPlayniteAPI.cs`，静态入口 `API.Instance`）

**注意：Steam/Epic/GOG 等官方库插件的实现不在本仓库**，它们是独立仓库维护、打包时合入的普通插件；本仓库只在 `source\PlayniteSDK\BuiltInExtensions.cs` 登记其 GUID。改商店导入逻辑要去对应插件仓库。

本地调试插件免打包：`ExtensionFactory` 支持"外部开发路径"清单文件（每行一个开发目录）。扩展模板在 `source\Tools\Playnite.Toolbox\Templates\`。

## 构建与运行

日常开发（推荐）：

1. 环境：VS 2019/2022（.NET desktop 工作负载）+ .NET Framework 4.6.2 Targeting Pack；构建脚本另需 PowerShell 7 和 powershell-yaml 模块（`Install-Module powershell-yaml`，脚本不会自动装，缺了在 YAML 校验步骤报 ConvertFrom-Yaml 不存在）
2. VS 打开 `source\Playnite.sln`，启动项目设为 **`Playnite.DesktopApp`**，配置选 **Debug / x86**，F5 运行
3. restore 用 `nuget.exe restore source\Playnite.sln`（packages.config 老格式，**不能用 `dotnet restore`**）

命令行构建（本机 MSBuild 在 `D:\develop\VS2022\MSBuild\Current\Bin\`）：

- **必须编 sln，不能直接编 csproj**：`/p:Platform=x86` 会传染给只有 AnyCPU 配置的 PlayniteSDK 等被引用项目，报 "没有为项目设置 OutputPath" 错；sln 会把解决方案平台正确映射到各项目
  ```
  MSBuild.exe source\Playnite.sln /t:Playnite_DesktopApp /p:Configuration=Debug /p:Platform=x86 /m
  ```
- 产物：`source\Playnite.DesktopApp\bin\x86\Debug\Playnite.DesktopApp.exe`（源码构建就叫这个名，改名 `Playnite.exe` 是官方打包阶段的事；全屏版同理为 `Playnite.FullscreenApp.exe`）

完整打包：`cd build; .\build.ps1`（默认 Release/x86；`-Package` 出 zip）。CI 的 `-LicensedDependenciesUrl` 是私有授权依赖，本地拿不到，但只影响出正式安装包，不影响编译调试。

测试：单元测试为 NUnit 3（VS Test Explorer 里跑，CI 不跑测试）；`tests\` 下另有 Pester + PSNativeAutomation 的 UI 端到端测试（`RunTests.ps1`，需先构建应用并配置 `TestConfig.yaml`）。

## 约定与坑

- 上游分支策略：`master` = 已发布状态；**PR 应提交到 `devel`**。上游 P10 已基本冻结（Playnite 11 在私有仓库重写中），只接受带测试的小改动
- Release 配置开了 `TreatWarningsAsErrors`，日常改代码用 Debug
- 平台默认 **x86**，别和 SDK 的 AnyCPU 混淆
- 代码风格（上游 README）：私有字段 camelCase、方法/属性 PascalCase、4 空格缩进、if/else 必须带花括号
- 文档：SDK/扩展开发 https://api.playnite.link/docs/ ；插件市场 https://playnite.link/addons.html
