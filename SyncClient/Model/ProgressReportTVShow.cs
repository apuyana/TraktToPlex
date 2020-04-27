namespace SyncClient.Model
{
    public class ProgressReportTVShow : ProgressReport
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

        /// <summary>
        /// To string for the Element.
        /// </summary>
        /// <returns></returns>
        protected override string internalToString()
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
                return base.internalToString();
            }
        }
    }
}