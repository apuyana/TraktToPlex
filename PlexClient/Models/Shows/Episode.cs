using Newtonsoft.Json;

namespace PlexClient.Models.Shows
{
    public class Episode : IHasId
    {
        public string Id { get; set; }

        [JsonProperty("index")]
        public int No { get; set; }

        [JsonProperty("viewCount")]
        public int ViewCount { get; set; }
    }
}