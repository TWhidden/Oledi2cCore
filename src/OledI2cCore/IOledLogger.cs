using System.Runtime.CompilerServices;

namespace OledI2cCore
{
    public interface IOledLogger
    {
        void Info(string logMessage, [CallerMemberName] string caller = "");
    }
}
