namespace FtdiCore
{
    public interface IFtdiI2cCore
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
        void ShutdownFtdi();
        void ScanDevicesAndQuit();

        bool GetPinStatus(byte mask);
    }
}