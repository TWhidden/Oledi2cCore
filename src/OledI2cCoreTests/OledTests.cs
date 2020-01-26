using System;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Threading;
using FtdiCore;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OledI2cCore;

namespace Oled_i2c_bus_core_tests
{
    [TestClass]
    public class OledTests
    {
        /// <summary>
        /// Modify this screen driver to target the screen you are using.
        /// </summary>
        public const ScreenDriver DefaultTestScreenDriver = ScreenDriver.SH1106;
        private static readonly Logger Logger = new Logger();
        private const uint DeviceIndexForTesting = 0;

        /// <summary>
        /// Get the Oled ready for testing.
        /// </summary>
        /// <returns></returns>
        private static OledCore GetOledForTesting()
        {
            // Create the I2C Controller
            var i2C = new FtdiI2cCore(DeviceIndexForTesting, Logger);

            // Init the Mpsse 
            i2C.SetupMpsse();

            // Create the Oled Object, with the wrapper for the I2C and the logger.
            // Set the defaults for the testing screen used
            var oled = new OledCore(new I2CWrapper2(i2C, Logger), 128, 64, logger: Logger,
                screenDriver: DefaultTestScreenDriver);

            // Init the Oled (setup params, clear display, etc)
            var init = oled.Initialise();
            Assert.IsTrue(init, "Oled Failed Init.");

            return oled;
        }

        [TestMethod]
        public void TextSizesPlacement()
        {
            var oled = GetOledForTesting();

            oled.WriteString(0, 0, "My Test 1", 1.6);
            oled.WriteString(0, 13, "Another 2", 1);
            oled.WriteString(0, 24, "My Guess is 3", 1);
            oled.Update();
        }


        [TestMethod]
        public void TextFontObject()
        {
            var oled = GetOledForTesting();

            oled.SetCursor(10, 10);

            oled.WriteString(new Oled_Font_5x7(), 2, "1234567");

            oled.WriteString(5, 32, "Another Test!", 2);

            oled.UpdateDirtyBytes();
        }

        [TestMethod]
        public void LineMiddle()
        {
            var oled = GetOledForTesting();

            oled.DrawLine(0, (byte) (oled.Height / 2), oled.Width, (byte) (oled.Height / 2), 1);

            oled.Update();
        }

        [TestMethod]
        public void I2CScanDevices()
        {
            var i2C = new FtdiI2cCore(0, Logger);

            i2C.SetupMpsse();

            i2C.ScanDevicesAndQuit();
        }

        [TestMethod]
        public void TextOverwriteTests()
        {
            var oled = GetOledForTesting();

            // write text in the middle of the screen 
            var yPlacement = oled.Height / 2;
            oled.WriteString(0, yPlacement, "TEST 1111111");
            oled.UpdateDirtyBytes();
            oled.WriteString(0, yPlacement, "TEST 5", 1, oled.Width);
            oled.UpdateDirtyBytes();
        }

        [TestMethod]
        public void DrawBitmapAnimate()
        {
            var oled = GetOledForTesting();

            // write text in the middle of the screen 
            var data = GetResourceBytes("bitmaps.microsoftlogo.png");

            var oledImage = new OledImage(data);

            // Image reduction
            int multiplier = 20;
            while(multiplier > 5)
            {
                var percent = multiplier / 100d;

                var imgWidth = (int) (oledImage.ImageWidth * percent);

                var resize = oledImage.GetOledBytesMaxWidth(imgWidth);

                oled.ClearDisplay();

                oled.DrawBitmap(0, 0, resize);

                oled.UpdateDirtyBytes();

                multiplier = multiplier - 1;
            }
        }


        [TestMethod]
        public void DrawBitmapMaxWidth()
        {
            var oled = GetOledForTesting();

            // write text in the middle of the screen 
            var data = GetResourceBytes("bitmaps.microsoftlogo.png");

            var oledImage = new OledImage(data);

            var resize = oledImage.GetOledBytesMaxWidth(85);

            oled.DrawBitmap(0, 0, resize);

            oled.UpdateDirtyBytes();
        }

        [TestMethod]
        public void DrawBitmapMaxHeight()
        {
            var oled = GetOledForTesting();

            // write text in the middle of the screen 
            var data = GetResourceBytes("bitmaps.microsoftlogo.png");

            var oledImage = new OledImage(data);

            var resize = oledImage.GetOledBytesMaxHeight(40);

            oled.DrawBitmap(0, 0, resize);

            oled.UpdateDirtyBytes();
        }

        [TestMethod]
        public void DrawBitmapMaxHeightHigh()
        {
            var oled = GetOledForTesting();

            // write text in the middle of the screen 
            var data = GetResourceBytes("bitmaps.microsoftlogo.png");

            var oledImage = new OledImage(data);

            var resize = oledImage.GetOledBytesMaxSize(128, 64);

            oled.DrawBitmap(0, 0, resize);

            oled.UpdateDirtyBytes();
        }

        byte[] GetResourceBytes(string resourceName)
        {
            var assembly = GetType().Assembly;

            // Core seems to have changed resource names,
            // so hard coding the test name for now. 
            // OLD: assembly.GetName().Name
            var fullResourceName = string.Concat("Oled_i2c_bus_core_tests", ".", resourceName);

            var names = assembly.GetManifestResourceNames();

            using (var stream = assembly.GetManifestResourceStream(fullResourceName))
            {
                var buffer = new byte[stream.Length];
                stream.Read(buffer, 0, (int)stream.Length);
                return buffer;
            }
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