using System;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace FtdiCore.Tests
{
    [TestClass]
    public class FtdiTests
    {
        private static Logger _logger = new Logger();

        [TestMethod]
        public void GetDevices()
        {
            var ftdi = new FtdiI2cCore(0, _logger);

            if(!ftdi.GetDeviceByIndex(0, out var device)) Assert.Fail("Failed searching for devices");

            //Assert.IsTrue(listOfDevices.Count > 0, "Device List had no elements and expected one. Is it plugged in?");

        }

        [TestMethod]
        public async Task TestReconnect()
        {
            var ftdi = new FtdiI2cCore(0, _logger);

            // First have it connected in this test.
            if (!ftdi.GetDeviceByIndex(0, out var device)) Assert.Fail("Failed searching for devices");

            Assert.IsNotNull(device, "Please plugin device for this test");

            var checkCount = 0;

            while (true)
            {
                if (!ftdi.GetDeviceByIndex(0, out device)) Assert.Fail("Failed searching for devices");

                // Wait for it have no devices
                if (device == null) break;

                await Task.Delay(500);

                checkCount++;

                if(checkCount == 20) Assert.Fail("Never detected device disconnection");
            }

            checkCount = 0;


            while (true)
            {
                if (!ftdi.GetDeviceByIndex(0, out device)) Assert.Fail("Failed searching for devices");

                // Wait for it have no devices
                if (device != null) break;

                await Task.Delay(500);

                checkCount++;

                if(checkCount == 20) Assert.Fail("Never detected device reconnection");
            }

        }

        public class Logger : ILogger
        {
            public void Info(string logMessage, [CallerMemberName] string caller = "")
            {
                Trace.WriteLine($"[{caller}; {DateTime.Now:HH:mm:ss.fff}]: {logMessage}");
            }
        }
    }
}
