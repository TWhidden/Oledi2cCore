﻿using System.Runtime.CompilerServices;

namespace FtdiCore
{
    public interface ILogger
    {
        void Info(string logMessage, [CallerMemberName] string caller = "");
    }
}
