namespace SyncClient.Model
{
    /// <summary>
    /// Report movie.
    /// </summary>
    public interface IProgressReportTVShow : IProgressReport
    {
        /// <summary>
        /// Episode to report.
        /// </summary>
        public int? Episode { get; set; }

        /// <summary>
        /// External provider Id.
        /// </summary>
        public string ExternalProviderId { get; set; }

        /// <summary>
        /// Season to report.
        /// </summary>
        public int? Season { get; set; }
    }
}