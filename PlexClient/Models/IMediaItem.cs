using Newtonsoft.Json;

namespace PlexClient.Models
{
    public interface IMediaItem : IHasId
    {
        string ExternalProvider { get; set; }

        string ExternalProviderId { get; set; }

        [JsonProperty("title")]
        string Title { get; set; }
    }
}