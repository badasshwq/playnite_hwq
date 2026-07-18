using Playnite.SDK;
using Playnite.ViewModels;
using Playnite.Windows;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace Playnite.DesktopApp.ViewModels
{
    public class GoogleImageDownloadViewModel : ObservableObject
    {
        private static readonly ILogger logger = LogManager.GetLogger();
        private readonly IWindowFactory window;
        private readonly IResourceProvider resources;
        private bool closingHanled = false;
        private readonly GoogleImageDownloader downloader;
        private readonly SteamGridDBImageDownloader sgdbDownloader = new SteamGridDBImageDownloader();
        private readonly WebImageType imageType;
        // SteamGridDB 搜不到当前搜索词时的回退词（通常是游戏安装目录的英文文件夹名）。
        private readonly string steamGridDBFallback;

        public double ItemWidth { get; set; } = 240;
        public double ItemHeight { get; set; } = 180;

        private bool showLoadMore = false;
        public bool ShowLoadMore
        {
            get => showLoadMore;
            set
            {
                showLoadMore = value;
                OnPropertyChanged();
            }
        }

        private bool transparent = false;
        public bool Transparent
        {
            get => transparent;
            set
            {
                var old = transparent;
                transparent = value;
                OnPropertyChanged();
                if (old != transparent)
                {
                    Search();
                }
            }
        }

        private WebImageSearchSource source = WebImageSearchSource.Google;
        public WebImageSearchSource Source
        {
            get => source;
            set
            {
                var old = source;
                source = value;
                OnPropertyChanged();
                if (old != source)
                {
                    Search();
                }
            }
        }

        private string searchTerm;
        public string SearchTerm
        {
            get => searchTerm;
            set
            {
                searchTerm = value;
                OnPropertyChanged();
            }
        }

        private int? searchWidth;
        public int? SearchWidth
        {
            get => searchWidth;
            set
            {
                searchWidth = value;
                OnPropertyChanged();
            }
        }

        private int? searchHeight;
        public int? SearchHeight
        {
            get => searchHeight;
            set
            {
                searchHeight = value;
                OnPropertyChanged();
            }
        }

        private SafeSearchSettings safeSearch;
        public SafeSearchSettings SafeSearch
        {
            get => safeSearch;
            set
            {
                safeSearch = value;
                OnPropertyChanged();
                Search();
            }
        }

        private List<GoogleImage> images = new List<GoogleImage>();
        public List<GoogleImage> AvailableImages
        {
            get
            {
                return images;
            }

            set
            {
                images = value;
                OnPropertyChanged();
            }
        }

        public ObservableCollection<GoogleImage> DisplayImages
        {
            get;
        } = new ObservableCollection<GoogleImage>();

        private GoogleImage selectedImage;
        public GoogleImage SelectedImage
        {
            get => selectedImage;
            set
            {
                selectedImage = value;
                OnPropertyChanged();
            }
        }

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
            }, (a) => SelectedImage != null);
        }

        public RelayCommand<object> ItemDoubleClickCommand
        {
            get => new RelayCommand<object>((a) =>
            {
                ConfirmDialog();
            });
        }

        public RelayCommand<object> WindowClosingCommand
        {
            get => new RelayCommand<object>((a) =>
            {
                WindowClosing();
            });
        }

        public RelayCommand<object> LoadMoreCommand
        {
            get => new RelayCommand<object>((a) =>
            {
                LoadMore();
            });
        }

        public RelayCommand<object> SearchCommand
        {
            get => new RelayCommand<object>((a) =>
            {
                Search();
            }, (a) => !string.IsNullOrEmpty(SearchTerm));
        }

        public RelayCommand<string> SetSearchResolutionCommand
        {
            get => new RelayCommand<string>((resolution) =>
            {
                SetSearchResolution(resolution);
            });
        }

        public RelayCommand<string> ClearSearchResolutionCommand
        {
            get => new RelayCommand<string>((a) =>
            {
                ClearSearchResolution();
            });
        }

        public GoogleImageDownloadViewModel(
            IWindowFactory window,
            IResourceProvider resources,
            string initialSearch,
            SafeSearchSettings safeSearch,
            WebImageSearchSource source,
            double itemWidth = 0,
            double itemHeigth = 0,
            WebImageType imageType = WebImageType.Any,
            string steamGridDBFallback = null)
        {
            this.window = window;
            this.resources = resources;
            this.safeSearch = safeSearch;
            this.source = source;
            this.imageType = imageType;
            this.steamGridDBFallback = steamGridDBFallback;
            if (itemWidth != 0)
            {
                ItemWidth = itemWidth;
            }

            if (itemHeigth != 0)
            {
                ItemHeight = itemHeigth;
            }

            downloader = new GoogleImageDownloader();
            SearchTerm = initialSearch;
            if (!initialSearch.IsNullOrEmpty())
            {
                Search();
            }
        }

        public bool? OpenView()
        {
            return window.CreateAndOpenDialog(this);
        }

        public void CloseView(bool? result)
        {
            downloader.Dispose();
            closingHanled = true;
            window.Close(result);
        }

        public void ConfirmDialog()
        {
            downloader.Dispose();
            closingHanled = true;
            CloseView(true);
        }

        public void Search()
        {
            DisplayImages.Clear();
            AvailableImages = new List<GoogleImage>();
            var query = SearchTerm;
            if (source == WebImageSearchSource.Google && SearchWidth != null && SearchHeight != null && !query.Contains("imagesize:"))
            {
                query = $"{query} imagesize:{SearchWidth}x{SearchHeight}";
            }

            if (GlobalProgress.ActivateProgress((_) =>
            {
                if (source == WebImageSearchSource.SteamGridDB)
                    AvailableImages = sgdbDownloader.GetImages(GetSteamGridDBQuery(query), imageType, steamGridDBFallback);
                else if (source == WebImageSearchSource.Google)
                    AvailableImages = downloader.GetImages(query, SafeSearch, Transparent).GetAwaiter().GetResult();
                else
                    AvailableImages = downloader.GetDdgImages(query, Transparent);
            }, new GlobalProgressOptions("LOCDownloadingLabel")).Result == true)
            {
                if (!AvailableImages.HasItems())
                {
                    if (source == WebImageSearchSource.SteamGridDB && !SteamGridDBImageDownloader.IsConfigured())
                    {
                        Dialogs.ShowErrorMessage(LOC.SgdbNotifyKeyMissing.GetLocalized(), "");
                    }
                    else
                    {
                        Dialogs.ShowErrorMessage(LOC.WebImageDownloadError.GetLocalized() + "\n\n" + "https://playnite.link/webimageissues", "");
                    }
                    return;
                }

                if (AvailableImages.Count > 20)
                {
                    DisplayImages.AddRange(AvailableImages.Take(20));
                    AvailableImages.RemoveRange(0, 20);
                    ShowLoadMore = true;
                }
                else if (AvailableImages.Count > 0)
                {
                    DisplayImages.AddRange(AvailableImages);
                    AvailableImages.Clear();
                    ShowLoadMore = false;
                }
            }
        }

        // SteamGridDB 按游戏名搜索，不需要网页搜图那些 icon/cover/wallpaper 英文后缀（带上反而搜不到）。
        // 去掉常见后缀，尽量还原成纯游戏名。
        private static string GetSteamGridDBQuery(string query)
        {
            if (query.IsNullOrWhiteSpace())
            {
                return query;
            }

            var trimmed = query.Trim();
            foreach (var suffix in new[] { " icon", " cover", " wallpaper", " background", " logo" })
            {
                if (trimmed.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                {
                    trimmed = trimmed.Substring(0, trimmed.Length - suffix.Length).Trim();
                }
            }

            // 去掉网页搜图模板给游戏名加的成对双引号（如 "土豆兄弟" -> 土豆兄弟），否则 SteamGridDB 搜不到。
            trimmed = trimmed.Replace("\"", "").Trim();

            // 去掉可能残留的 imagesize: 过滤词
            var sizeIdx = trimmed.IndexOf("imagesize:", StringComparison.OrdinalIgnoreCase);
            if (sizeIdx >= 0)
            {
                trimmed = trimmed.Substring(0, sizeIdx).Trim();
            }

            return trimmed;
        }

        public void SetSearchResolution(string resolution)
        {
            var regex = Regex.Match(resolution, @"(\d+)x(\d+)");
            if (regex.Success)
            {
                SearchWidth = int.Parse(regex.Groups[1].Value);
                SearchHeight = int.Parse(regex.Groups[2].Value);
            }
        }

        public void ClearSearchResolution()
        {
            SearchWidth = null;
            SearchHeight = null;
        }

        public void LoadMore()
        {
            if (AvailableImages.Count > 20)
            {
                DisplayImages.AddRange(AvailableImages.Take(20));
                AvailableImages.RemoveRange(0, 20);
                ShowLoadMore = true;
            }
            else if (AvailableImages.Count > 0)
            {
                DisplayImages.AddRange(AvailableImages);
                AvailableImages.Clear();
                ShowLoadMore = false;
            }
        }

        public void WindowClosing()
        {
            if (!closingHanled)
            {
                downloader.Dispose();
            }
        }
    }
}