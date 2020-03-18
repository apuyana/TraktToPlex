using Newtonsoft.Json;

namespace PlexClient.Models
{
    public interface IHasId
    {
        [JsonProperty("ratingKey")]
        string Id { get; set; }
    }
}