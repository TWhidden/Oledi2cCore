namespace OledI2cCore
{
    public interface II2C
    {
        bool SendBytes(byte[] dataBuffer);
    }
}
