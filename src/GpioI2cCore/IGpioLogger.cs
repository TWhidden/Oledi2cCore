using System.Runtime.CompilerServices;

namespace GpioI2cCore
{
    public interface IGpioLogger
    {
        void Info(string logMessage, [CallerMemberName] string caller = "");
    }
}
