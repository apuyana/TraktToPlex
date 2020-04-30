namespace SyncClient.Model
{
    public interface IProgressReport
    {
        /// <summary>
        /// Current item count.
        /// </summary>
        int CurrentItemCount { get; set; }

        /// <summary>
        /// Id of the process reported.
        /// </summary>
        int Id { get; set; }

        /// <summary>
        /// Item name to report.
        /// </summary>
        string ItemName { get; set; }

        /// <summary>
        /// Progress message.
        /// </summary>
        string Message { get; set; }

        /// <summary>
        /// Name of the process.
        /// </summary>
        string Name { get; set; }

        /// <summary>
        /// Status.
        /// </summary>
        ProgressStatus Status { get; set; }

        /// <summary>
        /// Total items.
        /// </summary>
        int TotalItemsCount { get; set; }

        /// <summary>
        /// To string for the Element.
        /// </summary>
        /// <returns></returns>
        string ToString();
    }
}