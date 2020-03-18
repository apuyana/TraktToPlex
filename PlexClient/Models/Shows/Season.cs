using Newtonsoft.Json;

namespace PlexClient.Models.Shows
{
    public class Season
    {
        public Episode[] Episodes { get; set; }

        [JsonProperty("ratingKey")]
        public string Id { get; set; }

        [JsonProperty("index")]
        public int No { get; set; }
    }
}