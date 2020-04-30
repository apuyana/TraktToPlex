namespace SyncClient.Model
{
    /// <summary>
    /// Report movie.
    /// </summary>
    public interface IProgressReportMovie : IProgressReport
    {
        /// <summary>
        /// Year of the movie.
        /// </summary>
        public int? Year { get; set; }
    }
}