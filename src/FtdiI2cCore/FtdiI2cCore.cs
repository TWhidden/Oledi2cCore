using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Timers;
using FtdiCore._3rdParty;
using Timer = System.Timers.Timer;

namespace FtdiCore
{
    public class FtdiI2cCore : IFtdiI2cCore
    {
        private static readonly ConcurrentDictionary<uint, List<Func<bool>>> InitCommands =
            new ConcurrentDictionary<uint, List<Func<bool>>>();

        private readonly Timer _autoReconnectTimer = new Timer();
        private readonly byte[] _byteDataRead = new byte[15]; // Array for storing the data which was read from the I2C Slave
        private FTDI _ftdiDevice;
        private readonly byte _gpioDMask;
        private readonly byte[] _inputBuffer = new byte[2048]; // Buffer to hold Data bytes read from FT232H
        private readonly ILogger _logger;
        private readonly object _functionLock = new object();

        // I2C FUNCTIONS

        // Prevent allocations
        private readonly FTDI.FT_DEVICE_INFO_NODE[] _nodeBuffer = new FTDI.FT_DEVICE_INFO_NODE[10];
        private readonly byte[] _outputBuffer = new byte[2048]; // Buffer to hold MPSSE commands and data to be sent to FT232H

        // #########################################################################################
        // FT232H I2C BUS SCAN
        //
        // Scan the I2C bus of an FT232H and display the address of any devices found
        // It simply calls the address and either gets an ACK or doesn't. 
        // Requires FTDI D2XX drivers
        // ######################################################################################### 

        //        // ###### Driver defines ######
        //private FTDI.FT_STATUS _ftStatus; // Status defined in D2XX to indicate operation result
        private bool _initCheckExecuting;
        private bool _ready;

        //uint dwClockDivisor = 0x32;
        //faster one crashes device or something
        private readonly uint dwClockDivisor = 0xC8; // 100khz -- 60mhz / ((1+dwClockDivisor)*2) = clock speed in mhz

        /// <summary>
        ///     The I2C can be used with GPIO, but the GPIO pins need to bet setup with their
        ///     directions (in/out).  Use this mask durint Init to setup.
        ///     Note, Pins 0-2 are reserved on the FT232 for I2C
        /// </summary>
        /// <param name="deviceIndex"></param>
        /// <param name="logger"></param>
        /// <param name="gpioMask"></param>
        public FtdiI2cCore(uint deviceIndex, ILogger logger, byte gpioMask = 0)
        {
            _logger = logger;
            _ftdiDevice = new FTDI(_logger);

            // Create the backing API object
            CreateDevice();

            var maskExcludingReservedDPins = 0x1F;
            var maskIncludingDefaultDPins = 0xC0;

            gpioMask = (byte) (gpioMask & maskExcludingReservedDPins);
            gpioMask = (byte) (gpioMask | maskIncludingDefaultDPins);

            _gpioDMask = gpioMask;

            DeviceIndex = deviceIndex;
        }

        private void CreateDevice()
        {
            _ftdiDevice?.Close();
            _ftdiDevice = new FTDI(_logger);
        }

        public uint DeviceIndex { get; }

        /// <summary>
        ///     Register init execution plans for when a device is first initialized.
        /// </summary>
        /// <param name="execute"></param>
        public void InitCommandRegister(Func<bool> execute)
        {
            var collection = InitCommands.GetOrAdd(DeviceIndex, x => new List<Func<bool>>());

            // Add to the startup Execution
            collection.Add(execute);
        }

        /// <summary>
        ///     Removes all the init commands
        /// </summary>
        public void InitCommandReset()
        {
            var collection = InitCommands.GetOrAdd(DeviceIndex, x => new List<Func<bool>>());
            collection.Clear();
        }

        public void InitAutoReconnectStart()
        {
            _autoReconnectTimer.Stop();

            // Immediate Attempt to init.
            InitReconnectImpl();

            _autoReconnectTimer.Elapsed += _autoReconnectTimer_Elapsed;
            _autoReconnectTimer.Interval = 1000; // Recheck every 1000 ms that its connected.
            _autoReconnectTimer.Start();
        }

        public void InitAutoReconnectStop()
        {
            _autoReconnectTimer?.Stop();
        }

        public event EventHandler<bool>? FtdiInitializeStateChanged;

        public bool Ready
        {
            get => _ready;
            set
            {
                // Hold a copy of previous value
                var v = _ready;

                // Set new value
                _ready = value;

                // Check if value changed for event state changed
                if (v != value) OnFtdiInitializeStateChanged(value);
            }
        }

        // ####################################################################################################################
        // Function to read 1 byte from the I2C slave
        //     Clock in one byte from the I2C Slave which is the actual data to be read
        //     Clock out one bit to the I2C Slave which is the ACK/NAK bit
        //     Put lines back to the idle state (idle between start and stop is clock low, data high (open-drain)
        // This function reads only one byte from the I2C Slave. It therefore sends a '1' as the ACK/NAK bit. This is NAKing 
        // the first byte of data, to tell the slave we dont want to read any more bytes. 
        // The one byte of data read from the I2C Slave is put into ByteDataRead[0]
        // ####################################################################################################################
        public bool ReadByteAndSendNAK()
        {
            lock (_functionLock)
            {
                var dwNumBytesToSend = 0; // Clear output buffer
                uint dwNumBytesSent = 0;
                uint dwNumBytesRead = 0;

                // Clock one byte of data in...
                _outputBuffer[dwNumBytesToSend++] = 0x20; // Command to clock data byte in on the clock rising edge
                _outputBuffer[dwNumBytesToSend++] = 0x00; // Length (low)
                _outputBuffer[dwNumBytesToSend++] = 0x00; // Length (high)   Length 0x0000 means clock ONE byte in 

                // Now clock out one bit (the ACK/NAK bit). This bit has value '1' to send a NAK to the I2C Slave
                _outputBuffer[dwNumBytesToSend++] = 0x13; // Command to clock data bits out on clock falling edge
                _outputBuffer[dwNumBytesToSend++] = 0x00; // Length of 0x00 means clock out ONE bit
                _outputBuffer[dwNumBytesToSend++] = 0xFF; // Command will send bit 7 of this byte, we send a '1' here

                // Put I2C line back to idle (during transfer) state... Clock line driven low, Data line high (open drain)
                _outputBuffer[dwNumBytesToSend++] =
                    0x80; // Command to set lower 8 bits of port (ADbus 0-7 on the FT232H)
                _outputBuffer[dwNumBytesToSend++] = 0x1A; // gpo1, rst, and data high 00111011 
                _outputBuffer[dwNumBytesToSend++] = 0x3B; // 00111011

                // AD0 (SCL) is output driven low
                // AD1 (DATA OUT) is output high (open drain)
                // AD2 (DATA IN) is input (therefore the output value specified is ignored)
                // AD3 to AD7 are inputs driven high (not used in this application)

                // This command then tells the MPSSE to send any results gathered back immediately
                _outputBuffer[dwNumBytesToSend++] = 0x87; // Send answer back immediate command

                var ftStatus = _ftdiDevice?.Write(_outputBuffer, dwNumBytesToSend,
                    ref dwNumBytesSent); // Send off the commands to the FT232H

                // ===============================================================
                // Now wait for the byte which we read to come back to the host PC
                // ===============================================================

                uint dwNumInputBuffer = 0;
                var readTimeoutCounter = 0;

                ftStatus =
                    _ftdiDevice?.GetRxBytesAvailable(ref dwNumInputBuffer); // Get number of bytes in the input buffer

                while (dwNumInputBuffer < 1 && ftStatus == FTDI.FT_STATUS.FT_OK && readTimeoutCounter < 500)
                {
                    // Sit in this loop until
                    // (1) we receive the one byte expected
                    // or (2) a hardware error occurs causing the GetQueueStatus to return an error code
                    // or (3) we have checked 500 times and the expected byte is not coming 
                    ftStatus =
                        _ftdiDevice?.GetRxBytesAvailable(
                            ref dwNumInputBuffer); // Get number of bytes in the input buffer
                    readTimeoutCounter++;
                }

                // If the loop above exited due to the byte coming back (not an error code and not a timeout)
                // then read the byte available and return True to indicate success
                if (ftStatus == FTDI.FT_STATUS.FT_OK && readTimeoutCounter < 500)
                {
                    ftStatus = _ftdiDevice?.Read(_inputBuffer, dwNumInputBuffer,
                        ref dwNumBytesRead); // Now read the data
                    _byteDataRead[0] = _inputBuffer[0]; // return the data read in the global array ByteDataRead
                    return true; // Indicate success
                }

                return false; // Failed to get any data back or got an error code back
            }
        }

        // ##############################################################################################################
        // Function to write 1 byte, and check if it returns an ACK or NACK by clocking in one bit
        //     We clock one byte out to the I2C Slave
        //     We then clock in one bit from the Slave which is the ACK/NAK bit
        //     Put lines back to the idle state (idle between start and stop is clock low, data high (open-drain)
        // Returns TRUE if the write was ACKed
        // ##############################################################################################################

        public bool SendByteAndCheckACK(byte dwDataSend)
        {
            lock (_functionLock)
            {
                var dwNumBytesToSend = 0; // Clear output buffer
                uint dwNumBytesSent = 0;
                uint dwNumBytesRead = 0;

                var ftStatus = FTDI.FT_STATUS.FT_OK;

                _outputBuffer[dwNumBytesToSend++] =
                    0x11; // command to clock data bytes out MSB first on clock falling edge
                _outputBuffer[dwNumBytesToSend++] = 0x00; // 
                _outputBuffer[dwNumBytesToSend++] = 0x00; // Data length of 0x0000 means 1 byte data to clock out
                _outputBuffer[dwNumBytesToSend++] = dwDataSend; // Actual byte to clock out

                // Put I2C line back to idle (during transfer) state... Clock line driven low, Data line high (open drain)
                _outputBuffer[dwNumBytesToSend++] =
                    0x80; // Command to set lower 8 bits of port (ADbus 0-7 on the FT232H)
                _outputBuffer[dwNumBytesToSend++] =
                    0x1A; // Set the value of the pins (only affects those set as output)
                _outputBuffer[dwNumBytesToSend++] =
                    0x3B; // Set the directions - all pins as output except Bit2(data_in)

                // AD0 (SCL) is output driven low
                // AD1 (DATA OUT) is output high (open drain)
                // AD2 (DATA IN) is input (therefore the output value specified is ignored)
                // AD3 to AD7 are inputs driven high (not used in this application)

                _outputBuffer[dwNumBytesToSend++] = 0x22; // Command to clock in bits MSB first on clock rising edge
                _outputBuffer[dwNumBytesToSend++] = 0x00; // Length of 0x00 means to scan in 1 bit

                // This command then tells the MPSSE to send any results gathered back immediately
                _outputBuffer[dwNumBytesToSend++] = 0x87; //Send answer back immediate command

                ftStatus = _ftdiDevice.Write(_outputBuffer, dwNumBytesToSend,
                    ref dwNumBytesSent); //Send off the commands
                //_logger.Info("Byte Sent, waiting for ACK...");    
                // ===============================================================
                // Now wait for the byte which we read to come back to the host PC
                // ===============================================================

                uint dwNumInputBuffer = 0;
                var readTimeoutCounter = 0;

                ftStatus = _ftdiDevice.GetRxBytesAvailable(
                    ref dwNumInputBuffer); // Get number of bytes in the input buffer

                while (dwNumInputBuffer < 1 && ftStatus == FTDI.FT_STATUS.FT_OK && readTimeoutCounter < 5500)
                {
                    // Sit in this loop until
                    // (1) we receive the one byte expected
                    // or (2) a hardware error occurs causing the GetQueueStatus to return an error code
                    // or (3) we have checked 500 times and the expected byte is not coming 
                    ftStatus = _ftdiDevice.GetRxBytesAvailable(
                        ref dwNumInputBuffer); // Get number of bytes in the input buffer
                    //_logger.Info("counter: %d, bytes: %d, ftStatus: %d", ReadTimeoutCounter, dwNumInputBuffer, ftStatus);
                    readTimeoutCounter++;
                }

                // If the loop above exited due to the byte coming back (not an error code and not a timeout)

                if (ftStatus == FTDI.FT_STATUS.FT_OK && readTimeoutCounter < 2500)
                {
                    ftStatus = _ftdiDevice.Read(_inputBuffer, dwNumInputBuffer,
                        ref dwNumBytesRead); // Now read the data
                    //_logger.Info("status was %d, input was 0x%X", ftStatus, InputBuffer[0]);  
                    if ((_inputBuffer[0] & 0x01) == 0x0) //Check ACK bit 0 on data byte read out
                        //_logger.Info("received ACK.");
                        return true; // Return True if the ACK was received
                    _logger.Info(
                        $"Failed to get ACK from I2C Slave when sending {dwDataSend} First Byte: {_inputBuffer[0]:X}");
                    return false; //Error, can't get the ACK bit 
                }

                _logger.Info($"Error: {ftStatus} status; Count: {readTimeoutCounter}");
                return false; // Failed to get any data back or got an error code back
            }
        }

        //my function
        public bool SendByte(byte dwDataSend)
        {
            lock (_functionLock)
            {
                var dwNumBytesToSend = 0; // Clear output buffer
                uint dwNumBytesSent = 0;

                var ftStatus = FTDI.FT_STATUS.FT_OK;

                _outputBuffer[dwNumBytesToSend++] =
                    0x11; // command to clock data bytes out MSB first on clock falling edge
                _outputBuffer[dwNumBytesToSend++] = 0x00; // 
                _outputBuffer[dwNumBytesToSend++] = 0x00; // Data length of 0x0000 means 1 byte data to clock out
                _outputBuffer[dwNumBytesToSend++] = dwDataSend; // Actual byte to clock out

                // Put I2C line back to idle (during transfer) state... Clock line driven low, Data line high (open drain)
                _outputBuffer[dwNumBytesToSend++] =
                    0x80; // Command to set lower 8 bits of port (ADbus 0-7 on the FT232H)
                _outputBuffer[dwNumBytesToSend++] =
                    0x1A; // Set the value of the pins (only affects those set as output)
                _outputBuffer[dwNumBytesToSend++] =
                    0x3B; // Set the directions - all pins as output except Bit2(data_in)

                // AD0 (SCL) is output driven low
                // AD1 (DATA OUT) is output high (open drain)
                // AD2 (DATA IN) is input (therefore the output value specified is ignored)
                // AD3 to AD7 are inputs driven high (not used in this application)

                _outputBuffer[dwNumBytesToSend++] = 0x22; // Command to clock in bits MSB first on clock rising edge
                _outputBuffer[dwNumBytesToSend++] = 0x00; // Length of 0x00 means to scan in 1 bit

                // This command then tells the MPSSE to send any results gathered back immediately
                _outputBuffer[dwNumBytesToSend++] = 0x87; //Send answer back immediate command

                ftStatus = _ftdiDevice.Write(_outputBuffer, dwNumBytesToSend,
                    ref dwNumBytesSent); //Send off the commands
                _logger.Info(
                    $"Status: {ftStatus};Sent: {dwDataSend:X}; Send {dwNumBytesToSend}; Sent: {dwNumBytesSent}");

                return true;
            }
        }

        /// <summary>
        ///     Fully configured send. First byte should be address
        ///     Send Idea from https://raw.githubusercontent.com/gurvindrasingh/AnyI2C/master/AnyI2cLib/FT232HI2C.cs
        /// </summary>
        /// <param name="data"></param>
        /// <returns></returns>
        public bool SendBytes(params byte[] data)
        {
            lock (_functionLock)
            {
                var succeeded = false;
                SetI2CLinesIdle(); // Set idle line condition
                try
                {
                    SetI2CStart(); // Send the start condition
                    if (data != null)
                        for (var i = 0; i < data.Length; i++)
                        {
                            succeeded = i == 0
                                ? SendAddressAndCheckACK(data[0], false)
                                : SendByteAndCheckACK(data[i]);

                            if (!succeeded)
                            {
                                _logger.Info($"Send failed on byte index {i}");
                                
                                // Attempt to re-init the Mpsse
                                ShutdownFtdi();
                                return false;
                            }
                        }
                }
                finally
                {
                    SetI2CStop(); // Send the stop condition	
                }

                return succeeded;
            }
        }


        // ##############################################################################################################
        // Function to write 1 byte, and check if it returns an ACK or NACK by clocking in one bit
        // This function combines the data and the Read/Write bit to make a single 8-bit value
        //     We clock one byte out to the I2C Slave
        //     We then clock in one bit from the Slave which is the ACK/NAK bit
        //     Put lines back to the idle state (idle between start and stop is clock low, data high (open-drain)
        // Returns TRUE if the write was ACKed by the slave
        // ##############################################################################################################

        public bool SendAddressAndCheckACK(byte address, bool read)
        {
            lock (_functionLock)
            {
                var dwNumBytesToSend = 0; // Clear output buffer
                uint dwNumBytesSent = 0;
                uint dwNumBytesRead = 0;

                var ftStatus = FTDI.FT_STATUS.FT_OK;

                // Combine the Read/Write bit and the actual data to make a single byte with 7 data bits and the R/W in the LSB
                if (read)
                    address = (byte) ((address << 1) | 0x01);
                else
                    address = (byte) ((address << 1) & 0xFE);

                _outputBuffer[dwNumBytesToSend++] =
                    0x11; // command to clock data bytes out MSB first on clock falling edge
                _outputBuffer[dwNumBytesToSend++] = 0x00; // 
                _outputBuffer[dwNumBytesToSend++] = 0x00; // Data length of 0x0000 means 1 byte data to clock out
                _outputBuffer[dwNumBytesToSend++] = address; // Actual byte to clock out

                // Put I2C line back to idle (during transfer) state... Clock line driven low, Data line high (open drain)
                _outputBuffer[dwNumBytesToSend++] =
                    0x80; // Command to set lower 8 bits of port (ADbus 0-7 on the FT232H)
                _outputBuffer[dwNumBytesToSend++] = 0x1A; // 00011010
                _outputBuffer[dwNumBytesToSend++] = 0x3B; // 00111011

                // AD0 (SCL) is output driven low
                // AD1 (DATA OUT) is output high (open drain)
                // AD2 (DATA IN) is input (therefore the output value specified is ignored)
                // AD3 to AD7 are inputs driven high (not used in this application)

                _outputBuffer[dwNumBytesToSend++] = 0x22; // Command to clock in bits MSB first on clock rising edge
                _outputBuffer[dwNumBytesToSend++] = 0x00; // Length of 0x00 means to scan in 1 bit

                // This command then tells the MPSSE to send any results gathered back immediately
                _outputBuffer[dwNumBytesToSend++] = 0x87; //Send answer back immediate command

                ftStatus = _ftdiDevice.Write(_outputBuffer, dwNumBytesToSend,
                    ref dwNumBytesSent); //Send off the commands

                //Check if ACK bit received by reading the byte sent back from the FT232H containing the ACK bit
                ftStatus = _ftdiDevice.Read(_inputBuffer, 1,
                    ref dwNumBytesRead); //Read one byte from device receive buffer

                if (ftStatus != FTDI.FT_STATUS.FT_OK || dwNumBytesRead == 0)
                {
                    _logger.Info("Failed to get ACK from I2C Slave - 0 bytes read");
                    return false; //Error, can't get the ACK bit
                }

                var v = _inputBuffer[0];
                var ack = v & 0x01;
                if (ack != 0x00) //Check ACK bit 0 on data byte read out
                    //_logger.Info("Failed to get ACK from I2C Slave - Response was 0x%X", InputBuffer[0]);
                    return false; //Error, can't get the ACK bit 
                //_logger.Info("Received ACK bit from Address 0x%X - 0x%X", dwDataSend, InputBuffer[0]);
                return true; // Return True if the ACK was received
            }
        }

        // ##############################################################################################################
        // Function to set all lines to idle states
        // For I2C lines, it releases the I2C clock and data lines to be pulled high externally
        // For the remainder of port AD, it sets AD3/4/5/6/7 as inputs as they are unused in this application
        // For the LED control, it sets AC6 as an output with initial state high (LED off)
        // For the remainder of port AC, it sets AC0/1/2/3/4/5/7 as inputs as they are unused in this application
        // ##############################################################################################################

        public void SetI2CLinesIdle()
        {
            lock (_functionLock)
            {
                var dwNumBytesToSend = 0; //Clear output buffer
                uint dwNumBytesSent = 0;

                // Set the idle states for the AD lines
                _outputBuffer[dwNumBytesToSend++] =
                    0x80; // Command to set directions of ADbus and data values for pins set as o/p
                _outputBuffer[dwNumBytesToSend++] = 0x1B; // 00011011
                _outputBuffer[dwNumBytesToSend++] = _gpioDMask; //0xD4; //0x3B;    // 00111011

                // IDLE line states are ...
                // AD0 (SCL) is output high (open drain, pulled up externally)
                // AD1 (DATA OUT) is output high (open drain, pulled up externally)
                // AD2 (DATA IN) is input (therefore the output value specified is ignored)

                // Set the idle states for the AC lines
                _outputBuffer[dwNumBytesToSend++] =
                    0x82; // Command to set directions of ACbus and data values for pins set as o/p
                _outputBuffer[dwNumBytesToSend++] = 0xFF; // 11111111
                _outputBuffer[dwNumBytesToSend++] = 0x40; // 01000000
                //_logger.Info("i2c lines set to idle");

                // IDLE line states are ...
                // AC6 (LED) is output driving high
                // AC0/1/2/3/4/5/7 are inputs (not used in this application)

                var ftStatus = _ftdiDevice.Write(_outputBuffer, dwNumBytesToSend,
                    ref dwNumBytesSent); //Send off the commands
            }
        }


        // ##############################################################################################################
        // Function to set the I2C Start state on the I2C clock and data lines
        // It pulls the data line low and then pulls the clock line low to produce the start condition
        // It also sends a GPIO command to set bit 6 of ACbus low to turn on the LED. This acts as an activity indicator
        // Turns on (low) during the I2C Start and off (high) during the I2C stop condition, giving a short blink.  
        // ##############################################################################################################
        public void SetI2CStart()
        {
            lock (_functionLock)
            {
                var dwNumBytesToSend = 0; //Clear output buffer
                uint dwNumBytesSent = 0;

                uint dwCount;

                // Pull Data line low, leaving clock high (open-drain)
                for (dwCount = 0;
                    dwCount < 4;
                    dwCount++) // Repeat commands to ensure the minimum period of the start hold time is achieved
                {
                    _outputBuffer[dwNumBytesToSend++] =
                        0x80; // Command to set directions of ADbus and data values for pins set as o/p
                    _outputBuffer[dwNumBytesToSend++] = 0xFD; // Bring data out low (bit 1)
                    _outputBuffer[dwNumBytesToSend++] =
                        0xFB; // Set all pins as output except bit 2 which is the data_in
                }

                // Pull Clock line low now, making both clock and data low
                for (dwCount = 0;
                    dwCount < 4;
                    dwCount++) // Repeat commands to ensure the minimum period of the start setup time is achieved
                {
                    _outputBuffer[dwNumBytesToSend++] =
                        0x80; // Command to set directions of ADbus and data values for pins set as o/p
                    _outputBuffer[dwNumBytesToSend++] = 0xFC; // Bring clock line low too to make clock and data low
                    _outputBuffer[dwNumBytesToSend++] =
                        0xFB; // Set all pins as output except bit 2 which is the data_in
                }

                // Turn the LED on by setting port AC6 low.
                _outputBuffer[dwNumBytesToSend++] =
                    0x82; // Command to set directions of upper 8 pins and force value on bits set as output
                _outputBuffer[dwNumBytesToSend++] = 0xBF; // 10111111
                _outputBuffer[dwNumBytesToSend++] = 0x40; // 01000000

                var ftStatus = _ftdiDevice.Write(_outputBuffer, dwNumBytesToSend,
                    ref dwNumBytesSent); //Send off the commands
                //_logger.Info("i2c lines set to start");
            }
        }


        // ##############################################################################################################
        // Function to set the I2C Stop state on the I2C clock and data lines
        // It takes the clock line high whilst keeping data low, and then takes both lines high
        // It also sends a GPIO command to set bit 6 of ACbus high to turn off the LED. This acts as an activity indicator
        // Turns on (low) during the I2C Start and off (high) during the I2C stop condition, giving a short blink.  
        // ##############################################################################################################

        public void SetI2CStop()
        {
            lock (_functionLock)
            {
                var dwNumBytesToSend = 0; //Clear output buffer
                uint dwNumBytesSent = 0;

                uint dwCount;

                // Initial condition for the I2C Stop - Pull data low (Clock will already be low and is kept low)
                for (dwCount = 0;
                    dwCount < 4;
                    dwCount++) // Repeat commands to ensure the minimum period of the stop setup time is achieved
                {
                    _outputBuffer[dwNumBytesToSend++] =
                        0x80; // Command to set directions of ADbus and data values for pins set as o/p
                    _outputBuffer[dwNumBytesToSend++] = 0xFC; // put data and clock low
                    _outputBuffer[dwNumBytesToSend++] =
                        0xFB; // Set all pins as output except bit 2 which is the data_in
                }

                // Clock now goes high (open drain)
                for (dwCount = 0;
                    dwCount < 4;
                    dwCount++) // Repeat commands to ensure the minimum period of the stop setup time is achieved
                {
                    _outputBuffer[dwNumBytesToSend++] =
                        0x80; // Command to set directions of ADbus and data values for pins set as o/p
                    _outputBuffer[dwNumBytesToSend++] =
                        0xFD; // put data low, clock remains high (open drain, pulled up externally)
                    _outputBuffer[dwNumBytesToSend++] =
                        0xFB; // Set all pins as output except bit 2 which is the data_in
                }

                // Data now goes high too (both clock and data now high / open drain)
                for (dwCount = 0;
                    dwCount < 4;
                    dwCount++) // Repeat commands to ensure the minimum period of the stop hold time is achieved
                {
                    _outputBuffer[dwNumBytesToSend++] =
                        0x80; // Command to set directions of ADbus and data values for pins set as o/p
                    _outputBuffer[dwNumBytesToSend++] =
                        0xFF; // both clock and data now high (open drain, pulled up externally)
                    _outputBuffer[dwNumBytesToSend++] =
                        0xFB; // Set all pins as output except bit 2 which is the data_in
                }

                // Turn the LED off by setting port AC6 high.
                _outputBuffer[dwNumBytesToSend++] =
                    0x82; // Command to set directions of upper 8 pins and force value on bits set as output
                _outputBuffer[dwNumBytesToSend++] = 0xFF; // 11111111
                _outputBuffer[dwNumBytesToSend++] = 0x40; // 01000000

                var ftStatus = _ftdiDevice.Write(_outputBuffer, dwNumBytesToSend,
                    ref dwNumBytesSent); //Send off the commands
                //_logger.Info("i2c stop");
            }
        }

        // OPENING DEVICE AND MPSSE CONFIGURATION

        public bool SetupMpsse()
        {
            lock (_functionLock)
            {
                // Open the FT232H module by it's description in the EEPROM
                // Note: See FT_OpenEX in the D2xx Programmers Guide for other options available
                var ftStatus = _ftdiDevice.OpenByIndex(DeviceIndex);

                // Check if Open was successful
                if (ftStatus != FTDI.FT_STATUS.FT_OK)
                {
                    _logger.Info($"Can't open {DeviceIndex} device!");
                    return false;
                }
                // #########################################################################################
                // After opening the device, Put it into MPSSE mode
                // #########################################################################################

                // Print message to show port opened successfully
                _logger.Info("Successfully opened FT232H device.");

                // Reset the FT232H
                ftStatus |= _ftdiDevice.ResetDevice();

                _logger.Info($"Reset: {ftStatus}");

                uint dwNumInputBuffer = 0;
                uint dwNumBytesRead = 0;
                uint dwNumBytesSent = 0;

                // Purge USB receive buffer ... Get the number of bytes in the FT232H receive buffer and then read them
                ftStatus |= _ftdiDevice.GetRxBytesAvailable(ref dwNumInputBuffer);
                if (ftStatus == FTDI.FT_STATUS.FT_OK && dwNumInputBuffer > 0)
                    _ftdiDevice.Read(_inputBuffer, dwNumInputBuffer, ref dwNumBytesRead);
                _logger.Info("Purged receive buffer.");

                var pinSetupBuffer = new byte[]
                {
                    0x80,
                    0,
                    0xBB
                };

                //ftStatus |= _ftdiDevice.SetBaudRate(9600);
                ftStatus |= _ftdiDevice.InTransferSize(65536); // Set USB request transfer sizes
                _logger.Info($"InTransferSize: {ftStatus}");
                ftStatus |= _ftdiDevice.SetCharacters(0, false, 0, false); // Disable event and error characters
                _logger.Info($"SetCharacters: {ftStatus}");
                ftStatus |= _ftdiDevice.SetTimeouts(5000, 5000); // Set the read and write timeouts to 5 seconds
                _logger.Info($"SetTimeouts: {ftStatus}");
                ftStatus |= _ftdiDevice.SetLatency(16); // Keep the latency timer at default of 16ms
                _logger.Info($"SetLatency: {ftStatus}");
                ftStatus |=
                    _ftdiDevice.SetBitMode(0x0,
                        FTDI.FT_BIT_MODES.FT_BIT_MODE_RESET); // Reset the mode to whatever is set in EEPROM
                _logger.Info($"SetBitmode: {ftStatus}");
                ftStatus |= _ftdiDevice.SetBitMode(0x0, FTDI.FT_BIT_MODES.FT_BIT_MODE_MPSSE); // Enable MPSSE mode
                _logger.Info($"SetBitmode: {ftStatus}");
                ftStatus |= _ftdiDevice.Write(pinSetupBuffer, pinSetupBuffer.Length, ref dwNumBytesSent);
                _logger.Info($"Write: {ftStatus}");

                // Inform the user if any errors were encountered
                if (ftStatus != FTDI.FT_STATUS.FT_OK)
                {
                    _logger.Info("failure to initialize device! ");
                    return false;
                    //return 1;
                }

                _logger.Info("MPSSE initialized.");

                // #########################################################################################
                // Synchronise the MPSSE by sending bad command AA to it
                // #########################################################################################

                var dwNumBytesToSend = 0;

                _outputBuffer[dwNumBytesToSend++] = 0x84;
                // Enable internal loopback
                ftStatus = _ftdiDevice.Write(_outputBuffer, dwNumBytesToSend, ref dwNumBytesSent);
                if (ftStatus != FTDI.FT_STATUS.FT_OK) _logger.Info("failed");

                dwNumBytesToSend = 0;

                ftStatus = _ftdiDevice.GetRxBytesAvailable(ref dwNumBytesRead);
                //_logger.Info("");

                if (dwNumBytesRead != 0)
                {
                    _logger.Info("Error - MPSSE receive buffer should be empty");
                    _ftdiDevice.SetBitMode(0x0, 0x00);
                    _ftdiDevice.Close();
                    return false;
                }

                dwNumBytesToSend = 0;
                _outputBuffer[dwNumBytesToSend++] = 0xAA;
                // Bogus command added to queue
                ftStatus = _ftdiDevice.Write(_outputBuffer, dwNumBytesToSend, ref dwNumBytesSent);
                //_logger.Info("sent.");
                dwNumBytesToSend = 0;

                do
                {
                    ftStatus = _ftdiDevice.GetRxBytesAvailable(ref dwNumBytesRead);
                    // Get the number of bytes in the device input buffer
                } while (dwNumBytesRead == 0 && ftStatus == FTDI.FT_STATUS.FT_OK);

                // Or timeout
                var bCommandEchod = 0;

                ftStatus = _ftdiDevice.Read(_inputBuffer, dwNumBytesRead, ref dwNumBytesRead);
                // Read out the input buffer

                // Check if bad command and echo command are received
                uint count;
                for (count = 0; count < dwNumBytesRead - 1; count++)
                    if (_inputBuffer[count] == 0xFA && _inputBuffer[count + 1] == 0xAA)
                    {
                        //_logger.Info("Success. Input buffer contained 0x%X and 0x%X", InputBuffer[dwCount], InputBuffer[dwCount+1]);
                        bCommandEchod = 1;
                        break;
                    }

                if (bCommandEchod == 0)
                {
                    _logger.Info("failed to synchronize MPSSE with command 0xAA ");
                    _logger.Info($"{_inputBuffer[count]}, {_inputBuffer[count + 1]}");
                    _ftdiDevice.Close();
                    return false;
                }

                // #########################################################################################
                // Synchronise the MPSSE by sending bad command AB to it
                // #########################################################################################

                dwNumBytesToSend = 0;
                //_logger.Info("");
                //_logger.Info("Sending bogus command 0xAB...");
                _outputBuffer[dwNumBytesToSend++] = 0xAB;
                // Bogus command added to queue
                ftStatus = _ftdiDevice.Write(_outputBuffer, dwNumBytesToSend, ref dwNumBytesSent);
                dwNumBytesToSend = 0;

                do
                {
                    ftStatus = _ftdiDevice.GetRxBytesAvailable(ref dwNumBytesRead);
                    // Get the number of bytes in the device input buffer
                } while (dwNumBytesRead == 0 && ftStatus == FTDI.FT_STATUS.FT_OK);
                // Or timeout


                bCommandEchod = 0;
                ftStatus = _ftdiDevice.Read(_inputBuffer, dwNumBytesRead, ref dwNumBytesRead);
                // Read out the input buffer

                for (count = 0; count < dwNumBytesRead - 1; count++)
                    // Check if bad command and echo command are received
                    if (_inputBuffer[count] == 0xFA && _inputBuffer[count + 1] == 0xAB)
                    {
                        bCommandEchod = 1;
                        //_logger.Info("Success. Input buffer contained 0x%X and 0x%X", InputBuffer[dwCount], InputBuffer[dwCount+1]);
                        break;
                    }

                if (bCommandEchod == 0)
                {
                    _logger.Info("failed to synchronize MPSSE with command 0xAB ");
                    _logger.Info($"{_inputBuffer[count]}, {_inputBuffer[count + 1]}");
                    _ftdiDevice.Close();
                    return false;
                }

                dwNumBytesToSend = 0;
                //_logger.Info("Disabling internal loopback...");
                _outputBuffer[dwNumBytesToSend++] = 0x85;
                // Disable loopback
                ftStatus = _ftdiDevice.Write(_outputBuffer, dwNumBytesToSend, ref dwNumBytesSent);
                if (ftStatus != FTDI.FT_STATUS.FT_OK) _logger.Info("command failed");

                // #########################################################################################
                // Configure the MPSSE settings
                // #########################################################################################

                dwNumBytesToSend = 0; // Clear index to zero
                _outputBuffer[dwNumBytesToSend++] = 0x8A; // Disable clock divide-by-5 for 60Mhz master clock
                _outputBuffer[dwNumBytesToSend++] = 0x97; // Ensure adaptive clocking is off
                _outputBuffer[dwNumBytesToSend++] =
                    0x8C; // Enable 3 phase data clocking, data valid on both clock edges for I2C

                _outputBuffer[dwNumBytesToSend++] =
                    0x9E; // Enable the FT232H's drive-zero mode on the lines used for I2C ...
                _outputBuffer[dwNumBytesToSend++] =
                    0x07; // ... on the bits 0, 1 and 2 of the lower port (AD0, AD1, AD2)...
                _outputBuffer[dwNumBytesToSend++] = 0x00; // ...not required on the upper port AC 0-7

                _outputBuffer[dwNumBytesToSend++] = 0x85; // Ensure internal loopback is off

                ftStatus = _ftdiDevice.Write(_outputBuffer, dwNumBytesToSend,
                    ref dwNumBytesSent); // Send off the commands

                // Now configure the dividers to set the SCLK frequency which we will use
                // The SCLK clock frequency can be worked out by the algorithm (when divide-by-5 is off)
                // SCLK frequency  = 60MHz /((1 +  [(1 +0xValueH*256) OR 0xValueL])*2)
                dwNumBytesToSend = 0; // Clear index to zero
                _outputBuffer[dwNumBytesToSend++] = 0x86; // Command to set clock divisor
                _outputBuffer[dwNumBytesToSend++] = (byte) (dwClockDivisor & 0xFF); // Set 0xValueL of clock divisor
                _outputBuffer[dwNumBytesToSend++] =
                    (byte) ((dwClockDivisor >> 8) & 0xFF); // Set 0xValueH of clock divisor
                ftStatus = _ftdiDevice.Write(_outputBuffer, dwNumBytesToSend,
                    ref dwNumBytesSent); // Send off the commands
                if (ftStatus == FTDI.FT_STATUS.FT_OK)
                {
                    //_logger.Info("Clock set to three-phase, drive-zero mode set, loopback off.");
                }
                else
                {
                    _logger.Info("Clock and pin mode set failed.");
                }


                // #########################################################################################
                // Configure the I/O pins of the MPSSE
                // #########################################################################################

                // Call the I2C function to set the lines of port AD to their required states
                SetI2CLinesIdle();

                // Also set the required states of port AC0-7. Bit 6 is used as an active-low LED, the others are unused
                // After this instruction, bit 6 will drive out high (LED off)
                //dwNumBytesToSend = 0;             // Clear index to zero
                //OutputBuffer[dwNumBytesToSend++] = 0x82;  // Command to set directions of upper 8 pins and force value on bits set as output
                //OutputBuffer[dwNumBytesToSend++] = 0xFF;      // Write 1's to all bits, only affects those set as output
                //OutputBuffer[dwNumBytesToSend++] = 0x40;  // Set bit 6 as an output
                //ftStatus = _ftdiDevice.Write(OutputBuffer, dwNumBytesToSend, ref dwNumBytesSent); // Send off the commands

                return true;
            }
        }

        public bool ShutdownFtdi()
        {
            lock (_functionLock)
            {
                _logger.Info("Shutting Down: Starting");
                var result = _ftdiDevice.SetBitMode(0x0, 0x00);
                _logger.Info($"Shutting Down: Set BitMode: {result}");
                result = _ftdiDevice.Close();
                _logger.Info($"Shutting Down: Close: {result}");
                Ready = false;
                return result == FTDI.FT_STATUS.FT_OK || result == FTDI.FT_STATUS.FT_INVALID_HANDLE;
            }
        }

        public void ScanDevicesAndQuit()
        {
            lock (_functionLock)
            {
                _logger.Info("Scanning I2C bus:");
                bool success;
                byte address;
                int numDevices;

                numDevices = 0;
                for (address = 0; address < 127; address++)
                {
                    SetI2CLinesIdle();
                    SetI2CStart();
                    success = SendAddressAndCheckACK(address, false);
                    SetI2CStop();
                    if (success)
                    {
                        //_logger.Info("I2C device found at address 0x");
                        _logger.Info("0x");
                        if (address < 16) _logger.Info("0");
                        _logger.Info($"{address}");
                        //_logger.Info(".");
                        numDevices++;
                    }
                    else
                    {
                        _logger.Info(". ");
                    }
                }

                _logger.Info("");

                if (numDevices == 0)
                {
                    _logger.Info("No I2C devices found.");
                }
                else
                {
                    if (numDevices == 1)
                    {
                        _logger.Info("One device found.");
                    }
                    else
                    {
                        if (numDevices > 1) _logger.Info($"{numDevices} devices found.");
                    }
                }

                _logger.Info("Shutting Down");
                _ftdiDevice.SetBitMode(0x0, 0x00);
                _ftdiDevice.Close();
            }
        }

        public bool GetPinStatus(byte mask)
        {
            lock (_functionLock)
            {
                byte value = 0;
                SetI2CLinesIdle();
                if (_ftdiDevice.GetPinStates(ref value) == FTDI.FT_STATUS.FT_OK)
                {
                    Debug.WriteLine($"value: {value}");
                    var result = value & mask;
                    if (result > 0) return true;
                }

                return false;
            }
        }

        public void Dispose()
        {
            _autoReconnectTimer.Dispose();
        }

        private void InitReconnectImpl()
        {
            if (_initCheckExecuting) return;

            try
            {
                _initCheckExecuting = true;
                
                    if (!GetDeviceByIndex(DeviceIndex, out var device))
                    {
                        // Failed talking to the driver. This indicates something in software is wrong
                        _logger.Info($"FTDI Core - DeviceIndex {DeviceIndex} unavailable.");
                        return;
                    }

                    // If previous state was not connected and the device count is high enough to meet
                    // the index (note, this is not great, we should use another method instead of an index
                    // to determine which device we are controlling. this will need to be adjusted
                    // TODO: Instead of Device Index, use another method of identification
                    if (!Ready && device != null)
                    {
                        _logger.Info(
                            $"Detected Device: {device.Description}; Location: {device.LocationId}; Id: {device.Id}; Serial Number: {device.SerialNumber}");

                        if (device.LocationId == 0)
                        {
                            var cycle = _ftdiDevice.CyclePort();
                            _logger.Info($"FTDI CyclePort: {cycle}");
                            CreateDevice();
                            return;
                        }

                        _logger.Info("FTDI Core - Device initializing");

                        // Execute Init Commands
                        var collection = InitCommands.GetOrAdd(DeviceIndex, x => new List<Func<bool>>());

                        // Execute the init functions
                        for (var index = 0; index < collection.Count; index++)
                        {
                            var action = collection[index];
                            _logger.Info($"FTDI Core - Init Action {index}");
                            var result = action();
                            if (result == false)
                            {
                                _logger.Info($"FTDI Core - Init Action {index} returned false!");
                                return;
                            }
                        }

                        Ready = true;
                    }
                    else if (Ready && device == null)
                    {
                        _logger.Info("FTDI Core Disconnected? No device found.");

                        Ready = false;
                    }
                
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error in _autoReconnectTimer_Elapsed. {ex.Message}");
            }
            finally
            {
                _initCheckExecuting = false;
            }
        }


        private void _autoReconnectTimer_Elapsed(object sender, ElapsedEventArgs e)
        {
            InitReconnectImpl();
        }

        public bool GetDeviceByLocationId(uint locationId, out FtdiDevice? device)
        {
            lock (_functionLock)
            {
                // Init object
                device = null;

                if (_ftdiDevice?.GetDeviceList(_nodeBuffer) == FTDI.FT_STATUS.FT_OK)
                {
                    foreach (var deviceInfoNode in _nodeBuffer)
                    {
                        if (deviceInfoNode == null!) continue;
                        if (deviceInfoNode.LocId == locationId) device = new FtdiDevice(deviceInfoNode);
                    }

                    // FTDI responded
                    return true;
                }

                //FTDI did not respond
                return false;
            }
        }


        public bool GetDeviceByIndex(uint deviceIndex, out FtdiDevice? device)
        {
            lock (_functionLock)
            {
                // Init object
                device = null;

                if (_ftdiDevice?.GetDeviceList(_nodeBuffer) == FTDI.FT_STATUS.FT_OK)
                {
                    var i = _nodeBuffer[deviceIndex];
                    if (i == null!) return false;
                    device = new FtdiDevice(i);

                    // FTDI responded
                    return true;
                }

                //FTDI did not respond
                return false;
            }
        }

        protected virtual void OnFtdiInitializeStateChanged(bool e)
        {
            _logger.Info($"FTDI Core - Changing Ready State to {e}");
            FtdiInitializeStateChanged?.Invoke(this, e);
        }
    }
}