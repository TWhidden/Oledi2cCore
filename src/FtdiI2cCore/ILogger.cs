using System.Runtime.CompilerServices;

namespace FtdiI2cCore
{
    public interface ILogger
    {
        void Info(string logMessage, [CallerMemberName] string caller = "");
    }
}
