using Unosquare.RaspberryIO;
using Unosquare.WiringPi;

namespace OledI2cCore.RaspberryPiExample;

public static class Program
{
    public const ScreenDriver DefaultTestScreenDriver = ScreenDriver.SH1106;

    public static void Main(string[] args)
    {
        // Create a logger
        var logger = new PiLogger();

        logger.Info("Init Wiring Pi...");
        Pi.Init<BootstrapWiringPi>();

        logger.Info("Creating I2C...");
        var i2C = new PiI2cCore();

        logger.Info("Create Oled...");

        // Create the Oled Object, with the wrapper for the I2C and the logger.
        // Set the defaults for the testing screen used
        var oled = new OledCore(i2C, 128, 64, logger: logger,
            screenDriver: DefaultTestScreenDriver);

        logger.Info("Init Oled...");
        // Init the Oled (setup params, clear display, etc)
        var init = oled.Initialise();

        logger.Info($"Init returned {init}");

        logger.Info("Writing to output...");
        oled.WriteString(0, 0, "My Test 1", 1);

        logger.Info("Calling oled update...");
        oled.UpdateDirtyBytes();

        logger.Info("done. Press enter to exit");

        Console.ReadLine();
    }



}