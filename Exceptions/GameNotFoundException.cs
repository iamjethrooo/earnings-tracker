using System;
using System.Collections.Generic;
using System.Text;

namespace nerve_earnings_tracker.Exceptions
{
    public class GameNotFoundException : Exception
    {
        public string GameName { get; }

        public GameNotFoundException(string gameName)
            : base($"Game '{gameName}' was not found.")
        {
            GameName = gameName;
        }
    }
}
