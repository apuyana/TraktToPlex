namespace SyncClient.Model
{
    public class ProgressReportMovie : ProgressReport
    {
        /// <summary>
        /// Year of the movie.
        /// </summary>
        public int? Year { get; set; }

        /// <summary>
        /// To string for the Element.
        /// </summary>
        /// <returns></returns>
        protected override string internalToString()
        {
            if (Year.HasValue)
            {
                return $"Id:{Id}; Process:{Name}; ItemName:{ItemName}({Year}); {CurrentItemCount}/{TotalItemsCount}; Status:{Status}; Message:{Message};";
            }
            else
            {
                return base.internalToString();
            }
        }
    }
}