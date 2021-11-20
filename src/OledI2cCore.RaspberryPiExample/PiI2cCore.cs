using Unosquare.RaspberryIO;

namespace OledI2cCore.RaspberryPiExample;

internal class PiI2cCore : II2C
{
    public bool ReadyState => true;

    public event EventHandler<bool>? ReadyStateChanged;

    public bool SendBytes(byte[] dataBuffer)
    {
        // first byte is the address
        var addr = dataBuffer[0];
        // get the device - this will add if needed
        var dev = Pi.I2C.AddDevice(addr);
        // get the register to write too
        var register = dataBuffer[1];

        // loop over the rest of the payload
        for (var i = 2; i < dataBuffer.Length; i++)
        {
            var data = dataBuffer[i];
            dev.WriteAddressByte(register, data);
        }

        return true;
    }
}