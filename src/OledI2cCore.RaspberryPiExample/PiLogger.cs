using System.Runtime.CompilerServices;

namespace OledI2cCore.RaspberryPiExample;

internal class PiLogger : IOledLogger
{
    public void Info(string logMessage, [CallerMemberName] string caller = "")
    {
        Console.WriteLine($"[{caller}] {logMessage}");
    }
}