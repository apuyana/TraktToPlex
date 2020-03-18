using Newtonsoft.Json;

namespace PlexClient.Models
{
    public class Section
    {
        [JsonProperty("key")]
        public string Id { get; set; }

        [JsonProperty("type")]
        public string Type { get; set; }
    }
}