using System;

namespace PatchMaker.Utility
{
    public static class Log
    {
        public static void Error(string message)
        {
            Console.Error.WriteLine("Error: " + message);
        }
    }
}
