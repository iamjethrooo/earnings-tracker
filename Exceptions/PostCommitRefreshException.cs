namespace EarningsTracker.Exceptions
{
    // Callers must treat this as a completed save and must not offer to repeat it.
    internal sealed class PostCommitRefreshException : Exception
    {
        public PostCommitRefreshException(Exception innerException)
            : base("Changes were saved, but the display could not be refreshed. Reopen the affected page to reload it.", innerException)
        {
        }
    }
}
