namespace SyncClient.Model
{
    /// <summary>
    /// Represents a processed episode.
    /// </summary>
    public class EpisodeProcessed
    {
        /// <summary>
        /// Episode number.
        /// </summary>
        public int? Number { get; set; }

        /// <summary>
        /// Seaspm number.
        /// </summary>
        public int? SeasonNumber { get; set; }

        /// <summary>
        /// Id of the trakt Show.
        /// </summary>
        public uint ShowTraktId { get; set; }
    }
}