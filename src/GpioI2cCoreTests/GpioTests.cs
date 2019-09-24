using System;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Threading;
using FtdiCore;
using GpioI2cCore;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Microsoft.VisualStudio.TestTools.UnitTesting.Logging;

namespace GpioI2cCoreTests
{
    [TestClass]
    public class GpioTests
    {
        private static readonly Logger Logger = new Logger();

        [TestMethod]
        public void TestButtonPress()
        {
            var i2C = new FtdiI2cCore(0, Logger, (byte)GpioPin.Pin8);

            var mpsseInit = i2C.SetupMpsse();
            Assert.IsTrue(mpsseInit, "Mpsse could not init!");

            ManualResetEvent statsUpdatedEvent = new ManualResetEvent(false);

            var buttonCore = new GpioButtonCore(new I2CWrapper2(i2C, Logger), GpioPin.Pin8);
            bool pressed = false;
            buttonCore.PressState += (sender, state) =>
            {
                pressed = state == GpioPressState.Pressed;
                if(pressed) statsUpdatedEvent.Set();
            };

            try
            {
                buttonCore.Initialize();
                statsUpdatedEvent.WaitOne(10000, false);

                Assert.IsTrue(pressed, "Button was not pressed!");
            }
            finally
            {
                buttonCore.UnInitialize();
            }
        }
    }

    public class Logger : ILogger, IGpioLogger
    {
        public void Info(string logMessage, [CallerMemberName] string caller = "")
        {
            Trace.WriteLine($"[{caller}; {DateTime.Now:HH:mm:ss.fff}]: {logMessage}");
        }
    }

    public class I2CWrapper2 : II2C
    {
        private readonly FtdiI2cCore _i2CWire;
        private readonly ILogger _logger;

        public I2CWrapper2(FtdiI2cCore i2cWire, ILogger logger)
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

        public bool GetPinStatus(byte mask)
        {
            return _i2CWire.GetPinStatus(mask);
        }
    }
}
