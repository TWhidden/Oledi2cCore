using System;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using FtdiI2cCore;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OledI2cCore;

namespace Oled_i2c_bus_core_tests
{
    [TestClass]
    public class OledTests
    {
        private static readonly Logger Logger = new Logger();

        [TestMethod]
        public void GeneralTesting()
        {
            var i2C = new FtdiCore(0, Logger);

            i2C.SetupMpsse();

            var oled = new OledCore(new I2CWrapper2(i2C, Logger), width:128, height: 64, logger: Logger);

            oled.Initialise();

            oled.DrawPixel((byte)(oled.Width / 2), (byte)(oled.Height / 2), 1);

            oled.DrawLine(0, 20, 127, 20, 1);

            oled.WriteString(new Oled_Font_5x7(), 2, "123456");

            oled.UpdateDirtyBytes();
        }


        [TestMethod]
        public void WriteText()
        {
            var i2C = new FtdiCore(0, Logger);

            i2C.SetupMpsse();

            var oled = new OledCore(new I2CWrapper2(i2C, Logger), width: 128, height: 64, logger: Logger);

            oled.Initialise();

            oled.SetCursor(10,10);
            oled.WriteString(new Oled_Font_5x7(), 2, "1234567", sync: true);
        }

        [TestMethod]
        public void DrawLineAcrossMiddle()
        {
            var i2C = new FtdiCore(0, Logger);

            i2C.SetupMpsse();

            var oled = new OledCore(new I2CWrapper2(i2C, Logger), width: 128, height: 64, logger: Logger);

            oled.Initialise();

            oled.DrawLine(0, (byte)(oled.Height/2), oled.Width, (byte)(oled.Height / 2), 1, true);

            oled.WriteString(new Oled_Font_5x7(), 2, "H L Test", sync: true);
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
        private readonly FtdiCore _i2CWire;
        private readonly ILogger _logger;

        public I2CWrapper2(FtdiCore i2cWire, ILogger logger)
        {
            _i2CWire = i2cWire;
            _logger = logger;
        }

        public uint DeviceIndex => _i2CWire.DeviceIndex;

        public bool ReadByteAndSendNak()
        {
            return _i2CWire.ReadByteAndSendNAK();
        }

        public bool SendByteAndCheckAck(byte data)
        {
            return _i2CWire.SendByteAndCheckACK(data);
        }

        public bool SendByte(byte data)
        {
            return _i2CWire.SendByte(data);
        }

        public bool SendBytes(byte[] dataBuffer)
        {
            return _i2CWire.SendByteRaw(dataBuffer);
        }

        public bool SendAddressAndCheckAck(byte data, bool read)
        {
            return _i2CWire.SendAddressAndCheckACK(data, read);
        }

        public void SetI2CLinesIdle()
        {
            _i2CWire.SetI2CLinesIdle();
        }

        public void SetI2CStart()
        {
            _i2CWire.SetI2CStart();
        }

        public void SetI2CStop()
        {
            _i2CWire.SetI2CStop();
        }

        public bool SetupMpsse()
        {
            return _i2CWire.SetupMpsse();
        }

        public void ShutdownFtdi()
        {
            _i2CWire.ShutdownFtdi();
        }

        public void ScanDevicesAndQuit()
        {
            _i2CWire.ScanDevicesAndQuit();
        }
    }
}
