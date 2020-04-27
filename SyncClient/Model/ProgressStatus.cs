namespace SyncClient.Model
{
    /// <summary>
    /// Progress status.
    /// </summary>
    public enum ProgressStatus
    {
        /// <summary>
        /// Nothing to do.
        /// </summary>
        Nothing = 0,

        /// <summary>
        /// Is a message.
        /// </summary>
        Message = 1,

        /// <summary>
        /// Sync the content.
        /// </summary>
        Sync = 2,

        /// <summary>
        /// Remove the content.
        /// </summary>
        Remove = 3,

        /// <summary>
        /// Not found on remote.
        /// </summary>
        NotFoundRemote = 4,
        NotWatchedRemote = 5,
        ErrorAddRemote = 6,
        WatchedRemote = 7,
        Processing = 8,
        NotSupported = 9,
        AddRemote = 10,
        ShouldRemove = 11
    }
}