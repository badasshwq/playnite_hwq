using Playnite;
using Playnite.Database;
using Playnite.SDK.Models;
using Playnite.SDK;
using Playnite.Commands;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Playnite.Common;
using System.Diagnostics;
using System.Drawing.Imaging;
using Playnite.Windows;
using System.Drawing;
using Playnite.Common.Media.Icons;
using System.Windows.Data;

namespace Playnite.DesktopApp.ViewModels
{
    public class InstalledGamesViewModel : ObservableObject
    {
        public enum ProgramType
        {
            Win32,
            UWP
        }

        public class ImportableProgram : SelectableItem<Program>
        {
            public static BitmapImage EmptyImage { get; set; }

            public ProgramType Type
            {
                get; set;
            }

            public string DisplayPath
            {
                get; set;
            }

            private ImageSource iconSource;
            public ImageSource IconSource
            {
                get
                {
                    if (string.IsNullOrEmpty(Item.Icon))
                    {
                        return null;
                    }

                    if (iconSource != null)
                    {
                        return iconSource;
                    }

                    if (Type == ProgramType.UWP)
                    {
                        iconSource = BitmapExtensions.CreateSourceFromURI(Item.Icon);
                    }
                    else
                    {
                        string path;
                        var match = Regex.Match(Item.Icon, @"(.*),(\d+)");
                        if (match.Success)
                        {
                            path = match.Groups[1].Value;
                            if (string.IsNullOrEmpty(path))
                            {
                                path = Item.Path;
                            }
                        }
                        else
                        {
                            path = Item.Icon;
                        }

                        var index = match.Groups[2].Value;
                        if (!File.Exists(path))
                        {
                            return null;
                        }

                        if (path.EndsWith(".ico", StringComparison.OrdinalIgnoreCase))
                        {
                            iconSource = BitmapExtensions.CreateSourceFromURI(path);
                        }
                        else
                        {
                            var icon = IconExtractor.ExtractMainIconFromFile(path);
                            if (icon != null)
                            {
                                try
                                {
                                    iconSource = icon.ToImageSource();
                                }
                                catch (Exception e)
                                {
                                    logger.Error(e, "Failed to convert icon.");
                                }
                                finally
                                {
                                    icon.Dispose();
                                }
                            }
                        }
                    }

                    if (iconSource == null)
                    {
                        iconSource = EmptyImage;
                    }

                    return iconSource;
                }
            }

            private bool import;
            public bool Import
            {
                get => import;
                set
                {
                    import = value;
                    OnPropertyChanged();
                }
            }

            // 该 exe 所属的“游戏单元目录”（根目录下第一层子文件夹）。排除时排除整个单元目录树，而非单个 exe。
            public string GameUnitDir { get; set; }

            public ImportableProgram(Program program, ProgramType type) : base(program)
            {
                Type = type;
                DisplayPath = type == ProgramType.Win32 ? program.Path : "Microsoft Store";
            }
        }

        public List<GameMetadata> SelectedGames
        {
            get;
            private set;
        } = new List<GameMetadata>();

        private ObservableCollection<ImportableProgram> programs = new ObservableCollection<ImportableProgram>();
        public ObservableCollection<ImportableProgram> Programs
        {
            get
            {
                return programs;
            }

            set
            {
                programs = value;
                OnPropertyChanged();
            }
        }

        private ImportableProgram selectedProgram;
        public ImportableProgram SelectedProgram
        {
            get
            {
                return selectedProgram;
            }

            set
            {
                selectedProgram = value;
                OnPropertyChanged();
            }
        }

        private ListCollectionView collectionView;
        public ListCollectionView CollectionView
        {
            get => collectionView;
            private set
            {
                collectionView = value;
                OnPropertyChanged();
            }
        }

        private bool hideImported = true;
        public bool HideImported
        {
            get => hideImported;
            set
            {
                hideImported = value;
                OnPropertyChanged();
                CollectionView.Refresh();
            }
        }

        private bool markImportAll;
        public bool MarkImportAll
        {
            get => markImportAll;
            set
            {
                markImportAll = value;
                OnPropertyChanged();
                CollectionView.Cast<ImportableProgram>().ForEach(a => a.Import = markImportAll);
            }
        }

        private readonly object listSyncLock = new object();
        private readonly HashSet<string> importedExes;
        private static ILogger logger = LogManager.GetLogger();
        private IWindowFactory window;
        private IDialogsFactory dialogs;

        public RelayCommand<object> CloseCommand
        {
            get => new RelayCommand<object>((a) =>
            {
                CloseView(false);
            });
        }

        public RelayCommand<object> ConfirmCommand
        {
            get => new RelayCommand<object>((a) =>
            {
                ConfirmDialog();
            });
        }

        public RelayCommand<object> SelectExecutableCommand
        {
            get => new RelayCommand<object>((a) =>
            {
                SelectExecutable();
            });
        }

        public RelayCommand<object> ScanFolderCommand
        {
            get => new RelayCommand<object>((a) =>
            {
                ScanFolder();
            });
        }

        // 是否显示“排除所选”按钮（仅“游戏目录”自动扫描场景为 true）。
        private bool showExcludeButton;
        public bool ShowExcludeButton
        {
            get => showExcludeButton;
            set { showExcludeButton = value; OnPropertyChanged(); }
        }

        // 外部（DesktopAppViewModel）注入：把用户勾选要排除的 exe 路径持久化进排除名单。
        public Action<List<string>> ExcludeSelectedHandler { get; set; }

        public RelayCommand<object> ExcludeSelectedCommand
        {
            get => new RelayCommand<object>((a) =>
            {
                ExcludeSelected();
            });
        }

        public void ExcludeSelected()
        {
            var toExclude = CollectionView.Cast<ImportableProgram>().Where(p => p.Import).ToList();
            if (toExclude.Count == 0)
            {
                return;
            }

            // 排除的是每个 exe 所属的“游戏单元目录”（整个游戏文件夹），而非单个 exe。
            var unitDirs = toExclude
                .Select(p => string.IsNullOrEmpty(p.GameUnitDir) ? Path.GetDirectoryName(p.Item.Path) : p.GameUnitDir)
                .Where(d => !string.IsNullOrEmpty(d))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            ExcludeSelectedHandler?.Invoke(unitDirs);

            // 从当前列表移除所有落在被排除单元目录树下的项。
            var excludedNow = new HashSet<string>(unitDirs.Select(NormalizePath), StringComparer.OrdinalIgnoreCase);
            var removeItems = Programs.Where(p => IsPathUnderAny(p.Item.Path, excludedNow)).ToList();
            foreach (var p in removeItems)
            {
                Programs.Remove(p);
            }

            CollectionView.Refresh();
        }

        public RelayCommand<object> DetectInstalledCommand
        {
            get => new RelayCommand<object>((a) =>
            {
                DetectInstalled();
            });
        }

        public InstalledGamesViewModel(IWindowFactory window, IDialogsFactory dialogs, IGameDatabaseMain database)
        {
            this.window = window;
            this.dialogs = dialogs;
            importedExes = database.GetImportedExeFiles();
            CollectionView = (ListCollectionView)CollectionViewSource.GetDefaultView(Programs);
            CollectionView.Filter = ListFilter;
            BindingOperations.EnableCollectionSynchronization(Programs, listSyncLock);
            ImportableProgram.EmptyImage = new BitmapImage(); // This is initialized here because the bitmap has to be created on main thread
        }

        public bool? OpenView()
        {
            return window.CreateAndOpenDialog(this);
        }

        public bool? OpenView(string directory)
        {
            if (!string.IsNullOrEmpty(directory))
            {
#pragma warning disable CS4014
                ScanFolder(directory);
#pragma warning restore CS4014
            }

            return window.CreateAndOpenDialog(this);
        }

        public bool? OpenViewOnWindowsApps()
        {
            DetectWindowsStoreApps();
            return window.CreateAndOpenDialog(this);
        }

        public void CloseView(bool? result)
        {
            window.Close(result);
        }

        public void ConfirmDialog()
        {
            SelectedGames = new List<GameMetadata>();
            foreach (var program in CollectionView.Cast<ImportableProgram>())
            {
                if (!program.Import)
                {
                    continue;
                }

                var newGame = new GameMetadata()
                {
                    Name = program.Item.Name.RemoveTrademarks(),
                    GameId = program.Item.AppId,
                    InstallDirectory = program.Item.WorkDir,
                    Source = program.Type == ProgramType.UWP ? new MetadataNameProperty("Microsoft Store") : null,
                    IsInstalled = true,
                    Platforms = new HashSet<MetadataProperty> { new MetadataSpecProperty("pc_windows") }
                };

                var path = program.Item.Path;
                if (program.Type == ProgramType.Win32 && !string.IsNullOrEmpty(program.Item.WorkDir))
                {
                    path = program.Item.Path.Replace(program.Item.WorkDir.EndWithDirSeparator(), ExpandableVariables.InstallationDirectory.EndWithDirSeparator());
                }

                newGame.GameActions = new List<GameAction>
                {
                     new GameAction()
                    {
                        Path = path,
                        Arguments = program.Item.Arguments,
                        Type = GameActionType.File,
                        WorkingDir = program.Type == ProgramType.Win32 ? ExpandableVariables.InstallationDirectory : string.Empty,
                        Name = newGame.Name,
                        IsPlayAction = true
                    }
                };

                if (program.IconSource != null &&  program.IconSource != ImportableProgram.EmptyImage)
                {
                    try
                    {
                        var bitmap = (BitmapSource)program.IconSource;
                        newGame.Icon = new MetadataFile(Guid.NewGuid().ToString() + ".png", bitmap.ToPngArray());
                    }
                    catch (Exception e)
                    {
                        logger.Error(e, "Failed to convert bitmap to png.");
                    }
                }

                SelectedGames.Add(newGame);
            }

            CloseView(true);
        }

        public void SelectExecutable()
        {
            var path = dialogs.SelectFile("Executable (.exe,.bat,lnk)|*.exe;*.bat;*.lnk");
            if (string.IsNullOrEmpty(path))
            {
                return;
            }

            if (!path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) &&
                !path.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase) &&
                !path.EndsWith(".bat", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            var program = Common.Programs.GetProgramData(path);
            var import = new ImportableProgram(program, ProgramType.Win32)
            {
                Selected = true
            };

            // Use shortcut name as game name for .lnk shortcuts
            if (path.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase))
            {
                var shortcutName = Path.GetFileNameWithoutExtension(path);
                if (!shortcutName.IsNullOrEmpty())
                {
                    import.Item.Name = shortcutName;
                }
            }

            Programs.Add(import);
            SelectedProgram = import;
        }

        public void DetectInstalled()
        {
            dialogs.ActivateGlobalProgress(async (progArgs) =>
            {
                try
                {
                    var allApps = new List<ImportableProgram>();
                    var installed = await Playnite.Common.Programs.GetInstalledPrograms(progArgs.CancelToken);
                    if (installed != null)
                    {
                        allApps.AddRange(installed.Select(a => new ImportableProgram(a, ProgramType.Win32)));
                        if (Computer.WindowsVersion == WindowsVersion.Win10 || Computer.WindowsVersion == WindowsVersion.Win11)
                        {
                            allApps.AddRange(Playnite.Common.Programs.GetUWPApps().Select(a => new ImportableProgram(a, ProgramType.UWP)));
                        }

                        progArgs.MainContext.Send(_ =>
                        {
                            Programs.Clear();
                            Programs.AddRange(allApps.OrderBy(a => a.Item.Name));
                        }, null);
                    }
                }
                catch (Exception exc) when (!PlayniteEnvironment.ThrowAllErrors)
                {
                    logger.Error(exc, "Failed to load list of installed apps.");
                }
            }, new GlobalProgressOptions(LOC.EmuWizardScanning, true));
        }

        public void DetectWindowsStoreApps()
        {
            try
            {
                var winApps = Playnite.Common.Programs.GetUWPApps().Select(a => new ImportableProgram(a, ProgramType.UWP));
                Programs.Clear();
                Programs.AddRange(winApps.OrderBy(a => a.Item.Name));
            }
                catch (Exception e) when(!PlayniteEnvironment.ThrowAllErrors)
            {
                logger.Error(e, "Failed to detect Windows Store apps.");
            }
        }

        public void ScanFolder()
        {
            var path = dialogs.SelectFolder();
            if (string.IsNullOrEmpty(path))
            {
                return;
            }

            ScanFolder(path);
        }

        public void ScanFolder(string path)
        {
            dialogs.ActivateGlobalProgress(async (progArgs) =>
            {
                try
                {
                    var executables = await Playnite.Common.Programs.GetExecutablesFromFolder(path, SearchOption.AllDirectories, progArgs.CancelToken);
                    if (executables != null)
                    {
                        var apps = executables.Select(a => new ImportableProgram(a, ProgramType.Win32)).OrderBy(a => a.Item.Name);
                        progArgs.MainContext.Send(_ =>
                        {
                            Programs.Clear();
                            Programs.AddRange(apps);
                        }, null);
                    }
                }
                catch (Exception exc) when (!PlayniteEnvironment.ThrowAllErrors)
                {
                    logger.Error(exc, "Failed to scan folder for executables: " + path);
                }
            }, new GlobalProgressOptions(LOC.EmuWizardScanning, true));
        }

        // 扫描多个“游戏目录”根文件夹（供“游戏目录”自动扫描用）。
        // 规则（贴合“解压即玩：每个游戏一个文件夹”）：
        //  0. 先过滤掉没有内嵌图标的 exe（命令行/后台小工具通常无图标，游戏主程序一般有图标）。
        //  1. 以根目录下的“直接子文件夹”为一个游戏单元，每个单元取该单元树里体积最大 + 第二大的两个 exe
        //     （兼顾“启动器 + 本体”两 exe 的情况），滤掉其余小工具。根目录下直接摆放的 exe 各自独立成一个单元。
        //  2. 若某个游戏单元的目录树下已经有游戏被导入过（按已导入 exe 所在目录判断），则整个单元及其所有子目录都跳过。
        public bool? OpenViewOnFolders(IEnumerable<string> directories, IEnumerable<string> excludedExes = null)
        {
            var excluded = new HashSet<string>(
                (excludedExes ?? Enumerable.Empty<string>())
                    .Where(e => !string.IsNullOrEmpty(e))
                    .Select(NormalizePath),
                StringComparer.OrdinalIgnoreCase);
            var roots = directories?.Where(d => !string.IsNullOrEmpty(d) && Directory.Exists(d))
                .Select(d => Path.GetFullPath(d)).ToList() ?? new List<string>();
            if (roots.Count > 0)
            {
                dialogs.ActivateGlobalProgress(async (progArgs) =>
                {
                    // 已导入游戏所在的目录集合（用已导入 exe 的目录去重出目录树的“根”），用于整单元跳过。
                    var importedDirs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    foreach (var exe in importedExes)
                    {
                        try
                        {
                            var dir = Path.GetDirectoryName(exe.Split(' ')[0]);
                            if (!string.IsNullOrEmpty(dir))
                            {
                                importedDirs.Add(Path.GetFullPath(dir));
                            }
                        }
                        catch
                        {
                            // 忽略无法解析的路径
                        }
                    }

                    foreach (var root in roots)
                    {
                        if (progArgs.CancelToken.IsCancellationRequested)
                        {
                            break;
                        }

                        try
                        {
                            var executables = await Playnite.Common.Programs.GetExecutablesFromFolder(root, SearchOption.AllDirectories, progArgs.CancelToken);
                            if (executables == null)
                            {
                                continue;
                            }

                            // 规则0：过滤掉没有内嵌图标的 exe。
                            var withIcon = executables
                                .Where(exe => HasEmbeddedIcon(exe.Path)).ToList();

                            var rootFull = root.EndWithDirSeparator();
                            // 按“游戏单元”分组：根下第一层子文件夹路径；直接在根下的 exe 以其自身路径为独立单元键。
                            var groups = withIcon.GroupBy(exe => GetGameUnitKey(exe.Path, rootFull));
                            var picked = new List<ImportableProgram>();
                            foreach (var group in groups)
                            {
                                var unitDir = group.Key;
                                // 规则2：该单元目录树下已有导入过的游戏 -> 整单元跳过。
                                if (IsUnitAlreadyImported(unitDir, importedDirs))
                                {
                                    continue;
                                }

                                // 规则3：该单元目录（或其祖先）在排除名单里 -> 整单元跳过。
                                if (IsPathUnderAny(unitDir, excluded))
                                {
                                    continue;
                                }

                                // 规则1：单元内取体积最大 + 第二大的两个 exe。
                                var top = group.OrderByDescending(exe => GetFileLength(exe.Path)).Take(2);
                                foreach (var exe in top)
                                {
                                    picked.Add(new ImportableProgram(exe, ProgramType.Win32) { GameUnitDir = unitDir });
                                }
                            }

                            if (picked.Count > 0)
                            {
                                progArgs.MainContext.Send(_ => Programs.AddRange(picked), null);
                            }
                        }
                        catch (Exception exc) when (!PlayniteEnvironment.ThrowAllErrors)
                        {
                            logger.Error(exc, "Failed to scan folder for executables: " + root);
                        }
                    }
                }, new GlobalProgressOptions(LOC.EmuWizardScanning, true));
            }

            return window.CreateAndOpenDialog(this);
        }

        // 返回 exe 所属的“游戏单元”键：根目录下的第一层子文件夹绝对路径；
        // 若 exe 直接位于根目录下，则以该 exe 自身路径为独立单元（每个直接 exe 一个单元）。
        private static string GetGameUnitKey(string exePath, string rootWithSep)
        {
            var full = Path.GetFullPath(exePath);
            if (!full.StartsWith(rootWithSep, StringComparison.OrdinalIgnoreCase))
            {
                return Path.GetDirectoryName(full) ?? full;
            }

            var relative = full.Substring(rootWithSep.Length);
            var sepIndex = relative.IndexOf(Path.DirectorySeparatorChar);
            if (sepIndex < 0)
            {
                // 直接在根目录下的 exe，无子文件夹 -> 独立单元
                return full;
            }

            var firstSegment = relative.Substring(0, sepIndex);
            return Path.Combine(rootWithSep, firstSegment);
        }

        // 判断某游戏单元目录树是否已包含导入过的游戏（unitDir 是文件夹或就是某个 exe 路径）。
        private static bool IsUnitAlreadyImported(string unitDir, HashSet<string> importedDirs)
        {
            var unit = Path.GetFullPath(unitDir);
            var isFile = File.Exists(unit);
            var unitFolder = isFile ? Path.GetDirectoryName(unit) : unit;
            if (string.IsNullOrEmpty(unitFolder))
            {
                return false;
            }

            var unitFolderSep = unitFolder.EndWithDirSeparator();
            foreach (var imp in importedDirs)
            {
                var impSep = imp.EndWithDirSeparator();
                // 已导入目录落在单元树内，或单元恰是已导入目录，视为该单元已被占用。
                if (impSep.StartsWith(unitFolderSep, StringComparison.OrdinalIgnoreCase) ||
                    unitFolderSep.StartsWith(impSep, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        // path（文件或目录）是否位于 excludedDirs 中任一目录（或其子目录树）下。
        private static bool IsPathUnderAny(string path, HashSet<string> excludedDirs)
        {
            if (excludedDirs == null || excludedDirs.Count == 0 || string.IsNullOrEmpty(path))
            {
                return false;
            }

            string full;
            try
            {
                full = Path.GetFullPath(path.Split(' ')[0]);
            }
            catch
            {
                return false;
            }

            foreach (var dir in excludedDirs)
            {
                var dirSep = dir.EndWithDirSeparator();
                // path 本身等于该目录，或位于其子树内。
                if (full.Equals(dir, StringComparison.OrdinalIgnoreCase) ||
                    full.EndWithDirSeparator().StartsWith(dirSep, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        private static string NormalizePath(string path)
        {
            try
            {
                return Path.GetFullPath(path.Split(' ')[0]).TrimEnd(Path.DirectorySeparatorChar);
            }
            catch
            {
                return path;
            }
        }

        private static long GetFileLength(string path)
        {
            try
            {
                return new FileInfo(path).Length;
            }
            catch
            {
                return 0;
            }
        }

        // 判断 exe 是否含内嵌图标：非 .exe（.lnk/.bat）一律保留；.exe 则尝试提取主图标，提不出即视为无图标。
        private static bool HasEmbeddedIcon(string path)
        {
            if (path.IsNullOrEmpty() || !path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            try
            {
                var icon = Common.Media.Icons.IconExtractor.ExtractMainIconFromFile(path);
                if (icon != null)
                {
                    icon.Dispose();
                    return true;
                }

                return false;
            }
            catch
            {
                return false;
            }
        }

        // 返回扫描后过滤掉“已导入”的可见项数量，用于判断是否有新游戏（无新游戏时不弹窗）。
        public int VisibleNewCount => CollectionView.Cast<ImportableProgram>().Count();

        public static List<Game> AddImportableGamesToDb(List<GameMetadata> games, IGameDatabaseMain database)
        {
            var statusSettings = database.GetCompletionStatusSettings();
            using (var buffer = database.BufferedUpdate())
            {
                var addedGames = new List<Game>();
                foreach (var game in games)
                {
                    var added = database.ImportGame(game);
                    if (statusSettings.DefaultStatus != Guid.Empty)
                    {
                        added.CompletionStatusId = statusSettings.DefaultStatus;
                        database.Games.Update(added);
                    }

                    addedGames.Add(added);
                }

                return addedGames;
            }
        }

        private bool ListFilter(object item)
        {
            var program = (ImportableProgram)item;
            if (HideImported)
            {
                return !importedExes.ContainsString(program.Item.Path + program.Item.Arguments ?? string.Empty, StringComparison.OrdinalIgnoreCase);
            }

            return true;
        }
    }
}
