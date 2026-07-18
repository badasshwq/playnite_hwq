using Newtonsoft.Json;
using Playnite.Common;
using Playnite.SDK;
using Playnite.Settings;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;

namespace Playnite
{
    /// <summary>
    /// 通过 SteamGridDB 官方 API 搜索游戏封面/图标/背景，替代不稳定的"抓 Google 网页"方式。
    /// 复用已安装的 SteamGridDB 元数据插件里配置好的 API Key（从其 config.json 读）。
    /// 结果转成与网页搜图统一的 GoogleImage 列表，供现有搜图弹窗直接使用。
    /// </summary>
    public class SteamGridDBImageDownloader
    {
        private static readonly ILogger logger = LogManager.GetLogger();

        private const string ApiBase = "https://www.steamgriddb.com/api/v2/";

        // 已安装的 SteamGridDB 元数据插件 GUID（其数据目录名 = 该 GUID）。
        private const string PluginGuid = "f9a763e1-1ccb-4d7d-b955-d59e708f71c1";

        private class ConfigDto
        {
            public string ApiKey { get; set; }
        }

        private class SearchItem
        {
            public int id { get; set; }
            public string name { get; set; }
        }

        private class ImageItem
        {
            public string url { get; set; }
            public string thumb { get; set; }
            public uint width { get; set; }
            public uint height { get; set; }
        }

        private class ApiResponse<T>
        {
            public bool success { get; set; }
            public T data { get; set; }
        }

        /// <summary>
        /// 读取 SteamGridDB 插件配置里的 API Key。未安装插件或未配置则返回 null。
        /// </summary>
        public static string GetApiKey()
        {
            try
            {
                var configPath = Path.Combine(PlaynitePaths.ExtensionsDataPath, PluginGuid, "config.json");
                if (!File.Exists(configPath))
                {
                    return null;
                }

                var config = Serialization.FromJsonFile<ConfigDto>(configPath);
                return config?.ApiKey.IsNullOrWhiteSpace() == false ? config.ApiKey : null;
            }
            catch (Exception e)
            {
                logger.Error(e, "Failed to read SteamGridDB API key from plugin config.");
                return null;
            }
        }

        public static bool IsConfigured()
        {
            return !GetApiKey().IsNullOrEmpty();
        }

        /// <summary>
        /// 按游戏名搜图。imageType 决定拉封面(grids)/图标(icons)/背景(heroes)。
        /// 先用 gameName 搜；搜不到且提供了 fallbackName（通常是安装目录的英文文件夹名）时，
        /// 用 fallbackName 再搜一次——解决用户把游戏名改成中文、SteamGridDB 只认英文原名的情况。
        /// 返回空列表表示无结果或出错（调用方据此提示）。
        /// </summary>
        public List<GoogleImage> GetImages(string gameName, WebImageType imageType, string fallbackName = null)
        {
            var apiKey = GetApiKey();
            if (apiKey.IsNullOrEmpty())
            {
                logger.Warn("SteamGridDB API key not configured, cannot search images.");
                return new List<GoogleImage>();
            }

            if (gameName.IsNullOrWhiteSpace() && fallbackName.IsNullOrWhiteSpace())
            {
                return new List<GoogleImage>();
            }

            try
            {
                using (var client = new HttpClient())
                {
                    client.Timeout = TimeSpan.FromSeconds(20);
                    client.DefaultRequestHeaders.Add("Authorization", "Bearer " + apiKey);

                    // 先用主搜索词（通常是游戏当前名）搜并拉图。
                    var images = TryGetImages(client, gameName, imageType);

                    // 拉不到图时，用 fallback（安装目录的英文文件夹名）重试。
                    // 关键：中文名可能歪打正着搜到同名的错游戏（其图库为空），所以"拉到空"也要回退，
                    // 不能只在"没搜到游戏"时才回退。
                    if (!images.HasItems() && !fallbackName.IsNullOrWhiteSpace()
                        && !string.Equals(fallbackName, gameName, StringComparison.OrdinalIgnoreCase))
                    {
                        logger.Info($"SteamGridDB: '{gameName}' returned no images, retrying with folder name '{fallbackName}'.");
                        images = TryGetImages(client, fallbackName, imageType);
                    }

                    if (!images.HasItems())
                    {
                        logger.Info($"SteamGridDB: no images found for '{gameName}' (fallback '{fallbackName}').");
                    }

                    return images;
                }
            }
            catch (Exception e)
            {
                logger.Error(e, $"SteamGridDB image search failed for '{gameName}'.");
                return new List<GoogleImage>();
            }
        }

        // 用一个搜索词搜游戏并拉指定类型的图，拿不到就返回空列表。
        private List<GoogleImage> TryGetImages(HttpClient client, string term, WebImageType imageType)
        {
            if (term.IsNullOrWhiteSpace())
            {
                return new List<GoogleImage>();
            }

            var gameId = SearchGameId(client, term);
            if (gameId == null)
            {
                return new List<GoogleImage>();
            }

            var endpoint = GetEndpointForType(imageType);
            var url = $"{ApiBase}{endpoint}/game/{gameId}?{GetQueryForType(imageType)}";
            var json = client.GetStringAsync(url).GetAwaiter().GetResult();
            var response = JsonConvert.DeserializeObject<ApiResponse<List<ImageItem>>>(json);
            if (response?.success != true || response.data == null)
            {
                return new List<GoogleImage>();
            }

            return response.data
                .Where(i => !i.url.IsNullOrEmpty())
                .Select(i => new GoogleImage
                {
                    ImageUrl = i.url,
                    ThumbUrl = i.thumb.IsNullOrEmpty() ? i.url : i.thumb,
                    Width = i.width,
                    Height = i.height
                })
                .ToList();
        }

        private int? SearchGameId(HttpClient client, string gameName)
        {
            var url = $"{ApiBase}search/autocomplete/{Uri.EscapeDataString(gameName)}";
            var json = client.GetStringAsync(url).GetAwaiter().GetResult();
            var response = JsonConvert.DeserializeObject<ApiResponse<List<SearchItem>>>(json);
            if (response?.success == true && response.data?.Any() == true)
            {
                return response.data[0].id;
            }

            return null;
        }

        private static string GetEndpointForType(WebImageType type)
        {
            switch (type)
            {
                case WebImageType.Icon:
                    return "icons";
                case WebImageType.Background:
                    return "heroes";
                case WebImageType.Cover:
                default:
                    return "grids";
            }
        }

        private static string GetQueryForType(WebImageType type)
        {
            // icons/logos 端点不接受 styles/dimensions，只过滤 nsfw/humor。
            switch (type)
            {
                case WebImageType.Icon:
                    return "nsfw=false&humor=false";
                case WebImageType.Background:
                    return "nsfw=false&humor=false";
                case WebImageType.Cover:
                default:
                    return "nsfw=false&humor=false";
            }
        }
    }
}
