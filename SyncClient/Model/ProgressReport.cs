namespace SyncClient.Model
{
    public abstract class ProgressReport
    {
        /// <summary>
        /// Current item count.
        /// </summary>
        public int CurrentItemCount { get; set; }

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
        protected virtual string internalToString()
        {
            return $"Id:{Id}; Process:{Name}; ItemName:{ItemName}; {CurrentItemCount}/{TotalItemsCount}; Status:{Status}; Message:{Message};";
        }

        /// <summary>
        /// To string for the Element.
        /// </summary>
        /// <returns></returns>
        public override string ToString()
        {
            if (Status == ProgressStatus.Message)
            {
                return $"Id:{Id}; Process:{Name}; Message:{Message};";
            }
            else
            {
                return internalToString();
            }
        }
    }
}