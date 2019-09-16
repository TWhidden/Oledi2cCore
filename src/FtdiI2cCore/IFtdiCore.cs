namespace FtdiI2cCore
{
    public interface IFtdiCore
    {
        uint DeviceIndex { get; }
        bool ReadByteAndSendNAK();
        bool SendByteAndCheckACK(byte dwDataSend);
        bool SendByte(byte dwDataSend);
        bool SendAddressAndCheckACK(byte address, bool read);
        void SetI2CLinesIdle();
        void SetI2CStart();
        void SetI2CStop();
        bool SetupMpsse();
        void ShutdownFtdi();
        void ScanDevicesAndQuit();
    }
}