using System;

namespace OledI2cCore
{
    public interface II2C
    {
        bool SendBytes(byte[] dataBuffer, int len);

        event EventHandler<bool> ReadyStateChanged;

        bool ReadyState { get; }
    }
}
