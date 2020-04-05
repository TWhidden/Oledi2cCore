using System;

namespace FtdiCore
{
    public interface IFtdiI2cCore : IDisposable
    {
        uint DeviceIndex { get; }
        bool ReadByteAndSendNAK();
        bool SendByteAndCheckACK(byte dwDataSend);
        bool SendByte(byte dwDataSend);
        bool SendBytes(byte[] data);
        bool SendAddressAndCheckACK(byte address, bool read);
        void SetI2CLinesIdle();
        void SetI2CStart();
        void SetI2CStop();
        bool SetupMpsse();
        bool ShutdownFtdi();
        void ScanDevicesAndQuit();

        bool GetPinStatus(byte mask);

        void InitCommandRegister(Func<bool> execute);

        void InitCommandReset();

        void InitAutoReconnectStart();

        void InitAutoReconnectStop();

        event EventHandler<bool> FtdiInitializeStateChanged;

        bool Ready { get; }
    }
}