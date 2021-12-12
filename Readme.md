# Oled I2C .net Core + FT232H USB I2C

This project takes bits of the internet and makes it work for the project I am working on. 

Porting C++ code to C#, figuring out how to work with these different OLED displays.

Currently working with an FT232H (https://www.adafruit.com/product/2264) Device so I can remotely control a screen.

Currently targeting .net6; (you can compile for 3 without problems); Using VS 2022 with latest language features. Working to reduce allocations using some of the new language features and buffer pools.

Current Display I am using is an SH1106 (https://www.amazon.com/gp/product/B01MRR4LVE) - Note, this display, probably cost me 20 hours of my life with the odd-ball extra stuff you have to do with it due to it actually having a memory buffer 132x64 but only displaying 128x64.  

Example: 

[![OLED Screen Example](https://i9.ytimg.com/vi_webp/OpSDj8zTyMs/mqdefault.webp?v=61b64227&sqp=CICE2Y0G&rs=AOn4CLAKfFiadgZJddWw0xAdaYykdDPw6A)](https://youtu.be/StTqXEQ2l-Y "OLED Screen Example")


The FtdiI2cCore project is the abstraction over the I2C / USB FT232H. This can be used to talk to any I2C with this chip set and .net core. 

The OledI2cCore is a port from some Oled code I used previously with Node, and cut parts from C++ found around the web. 

I have a Test lib so you can test your ideas out, and make sure they are working. 

# Example OLED on Raspberry Pi

```
// Create an interface to write to your hardware. In this case the RP

using OledI2cCore;
using Unosquare.RaspberryIO;

internal class PiI2CCore : II2C
{
    public bool ReadyState => true;

    public event EventHandler<bool>? ReadyStateChanged;

    public bool SendBytes(byte[] dataBuffer, int len)
    {
        // first byte is the address
        var addr = dataBuffer[0];
        // get the device - this will add if needed
        var dev = Pi.I2C.AddDevice(addr);
        // get the register to write to
        var register = dataBuffer[1];

        // loop over the rest of the payload
        for (var i = 2; i < len; i++)
        {
            var data = dataBuffer[i];
            dev.WriteAddressByte(register, data);
        }

        return true;
    }
}
```


```
public const ScreenDriver DefaultTestScreenDriver = ScreenDriver.SH1106;

public static void Main(string[] args)
    {
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
```

