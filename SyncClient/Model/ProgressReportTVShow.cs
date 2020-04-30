namespace SyncClient.Model
{
    public class ProgressReportTVShow : IProgressReportTVShow
    {
        /// <summary>
        /// Current item count.
        /// </summary>
        public int CurrentItemCount { get; set; }

        /// <summary>
        /// Episode to report.
        /// </summary>
        public int? Episode { get; set; }

        /// <summary>
        /// External provider Id.
        /// </summary>
        public string ExternalProviderId { get; set; }

        /// <summary>
        /// Id of the process reported.
        /// </summary>
        public int Id { get; set; }

        /// <summary>
        /// Item name to report.
        /// </summary>
        public string ItemName { get; set; }

        /// <summary>
        /// Progress message.
        /// </summary>
        public string Message { get; set; }

        /// <summary>
        /// Name of the process.
        /// </summary>
        public string Name { get; set; }

        /// <summary>
        /// Season to report.
        /// </summary>
        public int? Season { get; set; }

        /// <summary>
        /// Status.
        /// </summary>
        public ProgressStatus Status { get; set; }

        /// <summary>
        /// Total items.
        /// </summary>
        public int TotalItemsCount { get; set; }

        /// <summary>
        /// To string for the Element.
        /// </summary>
        /// <returns></returns>
        public override string ToString()
        {
            if (Episode.HasValue && Season.HasValue)
            {
                return $"Id:{Id}; Process:{Name}; ItemName:{ItemName}S{Season.Value.ToString("00")}E{Episode.Value.ToString("00")}; {CurrentItemCount}/{TotalItemsCount}; Status:{Status}; Message:{Message};";
            }
            else if (Season.HasValue)
            {
                return $"Id:{Id}; Process:{Name}; ItemName:{ItemName}S{Season.Value.ToString("00")}; {CurrentItemCount}/{TotalItemsCount}; Status:{Status}; Message:{Message};";
            }
            else
            {
                if (Status == ProgressStatus.Message)
                {
                    return $"Id:{Id}; Process:{Name}; Message:{Message};";
                }
                else
                {
                    return $"Id:{Id}; Process:{Name}; ItemName:{ItemName}; {CurrentItemCount}/{TotalItemsCount}; Status:{Status}; Message:{Message};";
                }
            }
        }
    }
}