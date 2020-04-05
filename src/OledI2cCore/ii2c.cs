using System;

namespace OledI2cCore
{
    public interface II2C
    {
        bool SendBytes(byte[] dataBuffer);

        event EventHandler<bool> ReadyStateChanged;

        bool ReadyState { get; }
    }
}
