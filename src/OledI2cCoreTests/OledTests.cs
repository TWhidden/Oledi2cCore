using System;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using FtdiCore;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OledI2cCore;

namespace Oled_i2c_bus_core_tests
{
    [TestClass]
    public class OledTests
    {
        public const ScreenDriver DefaultTestScreenDriver = ScreenDriver.SH1106;
        private static readonly Logger Logger = new Logger();

        [TestMethod]
        public void GeneralTesting()
        {
            var i2C = new FtdiI2cCore(0, Logger);

            i2C.SetupMpsse();

            var oled = new OledCore(new I2CWrapper2(i2C, Logger), 128, 64, logger: Logger,
                screenDriver: DefaultTestScreenDriver);

            oled.Initialise();

            //oled.DrawPixel((byte)(oled.Width / 2), (byte)(oled.Height / 2), 1);

            //oled.DrawLine(0, 20, 127, 20, 1);
            var font = new Oled_Font_5x7();
            oled.WriteString(0, 0, "My Test 1", 1.6);
            oled.WriteString(0, 13, "Another 2", 1);

            oled.WriteString(0, 24, "My Guess is 3", 1);

            //oled.UpdateDirtyBytes();
            oled.Update();
        }


        [TestMethod]
        public void WriteText()
        {
            var i2C = new FtdiI2cCore(0, Logger);

            i2C.SetupMpsse();

            var oled = new OledCore(new I2CWrapper2(i2C, Logger), 128, 64, logger: Logger,
                screenDriver: DefaultTestScreenDriver);

            oled.Initialise();

            oled.SetCursor(10, 10);
            oled.WriteString(new Oled_Font_5x7(), 2, "1234567");

            oled.WriteString(5, 32, "Another Test!", 2);

            oled.UpdateDirtyBytes();
        }

        [TestMethod]
        public void DrawLineAcrossMiddle()
        {
            var i2C = new FtdiI2cCore(0, Logger);


            i2C.SetupMpsse();

            var oled = new OledCore(new I2CWrapper2(i2C, Logger), 128, 64, logger: Logger,
                screenDriver: DefaultTestScreenDriver);

            oled.Initialise();

            //Thread.Sleep(1000);

            oled.DrawLine(0, (byte) (oled.Height / 2), oled.Width, (byte) (oled.Height / 2), 1);

            //oled.WriteString(new Oled_Font_5x7(), 2, "H L Test");

            //oled.WriteString(0, 40, "Test", 1);

            oled.Update();
        }

        [TestMethod]
        public void ScanAddresses()
        {
            var i2C = new FtdiI2cCore(0, Logger);

            i2C.SetupMpsse();

            i2C.ScanDevicesAndQuit();
        }
    }

    public class Logger : ILogger, IOledLogger
    {
        public void Info(string logMessage, [CallerMemberName] string caller = "")
        {
            Trace.WriteLine($"[{caller}; {DateTime.Now:HH:mm:ss.fff}]: {logMessage}");
        }
    }

    public class I2CWrapper2 : II2C
    {
        private readonly IFtdiI2cCore _i2CWire;
        private readonly ILogger _logger;

        public I2CWrapper2(IFtdiI2cCore i2cWire, ILogger logger)
        {
            _i2CWire = i2cWire;
            _logger = logger;
        }
        
        public bool SendBytes(byte[] dataBuffer)
        {
            return _i2CWire.SendBytes(dataBuffer);
        }
    }
}