namespace GpioI2cCore
{
    public interface II2C
    {
        /// <summary>
        /// Get the current GPIO pin status.
        /// </summary>
        /// <param name="mask"></param>
        /// <returns></returns>
        bool GetPinStatus(byte mask);
    }
}
