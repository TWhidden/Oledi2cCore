namespace OledI2cCore
{
    public interface II2C
    {
        uint DeviceIndex { get; }
        bool ReadByteAndSendNak();
        bool SendByteAndCheckAck(byte data);
        bool SendByte(byte data);
        bool SendBytes(byte[] dataBuffer);
        bool SendAddressAndCheckAck(byte data, bool read);
        void SetI2CLinesIdle();
        void SetI2CStart();
        void SetI2CStop();
        bool SetupMpsse();
        void ShutdownFtdi();
        void ScanDevicesAndQuit();
    }
}
