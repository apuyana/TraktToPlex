using Newtonsoft.Json;
using System.Text.RegularExpressions;

namespace PlexClient.Models
{
    public class Movie : IMediaItem
    {
        public string ExternalProvider { get; set; }
        public string ExternalProviderId { get; set; }

        [JsonProperty("guid")]
        public string ExternalProviderInfo
        {
            get => null;
            set
            {
                var match = Regex.Match(value, @"\.(?<provider>[a-z]+)://(?<id>[^\?]+)");
                ExternalProvider = match.Groups["provider"].Value;
                ExternalProviderId = match.Groups["id"].Value;
            }
        }

        public string Id { get; set; }
        public string Title { get; set; }

        public int ViewCount { get; set; }
    }
}