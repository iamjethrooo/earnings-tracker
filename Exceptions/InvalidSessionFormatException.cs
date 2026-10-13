namespace nerve_earnings_tracker.Exceptions
{
    public class InvalidSessionFormatException : Exception
    {
        public InvalidSessionFormatException()
            : base("The provided text is not in a supported Nerve session format. Please make sure you copied the session data directly from Nerve.")
        {
        }

        public InvalidSessionFormatException(string message)
            : base(message)
        {
        }
    }
}
