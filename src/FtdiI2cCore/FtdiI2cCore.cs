﻿using System;
using System.Collections;
using System.Diagnostics;
using System.Threading;
using FtdiCore._3rdParty;

namespace FtdiCore
{
    public class FtdiI2cCore : IFtdiI2cCore
    {
        private readonly ILogger _logger;
        private readonly byte _gpioDMask;

        public uint DeviceIndex { get; }


        /// <summary>
        /// The I2C can be used with GPIO, but the GPIO pins need to bet setup with their
        /// directions (in/out).  Use this mask durint Init to setup.
        /// Note, Pins 0-2 are reserved on the FT232 for I2C
        /// </summary>
        /// <param name="deviceIndex"></param>
        /// <param name="logger"></param>
        /// <param name="gpioMask"></param>
        public FtdiI2cCore(uint deviceIndex, ILogger logger, byte gpioDMask = 0)
        {
            _logger = logger;

            var maskExcludingReservedDPins = 0x1F;
            var maskIncludingDefaultDPins = 0xC0;

            gpioDMask = (byte)(gpioDMask & maskExcludingReservedDPins);
            gpioDMask = (byte) (gpioDMask | maskIncludingDefaultDPins);

            _gpioDMask = gpioDMask;

            DeviceIndex = deviceIndex;
        }

        FTDI _ftdiDevice = new FTDI();

        // #########################################################################################
        // FT232H I2C BUS SCAN
        //
        // Scan the I2C bus of an FT232H and display the address of any devices found
        // It simply calls the address and either gets an ACK or doesn't. 
        // Requires FTDI D2XX drivers
        // ######################################################################################### 

        // #include <stdio.h>
        // #include "ftd2xx.h"

        int bCommandEchod = 0;

        //        // ###### Driver defines ######
        FTDI.FT_STATUS ftStatus;         // Status defined in D2XX to indicate operation result

        byte[] OutputBuffer = new byte[2048];        // Buffer to hold MPSSE commands and data to be sent to FT232H
        byte[] InputBuffer = new byte[2048];         // Buffer to hold Data bytes read from FT232H

        //uint dwClockDivisor = 0x32;
        //faster one crashes device or something
        uint dwClockDivisor = 0xC8;      // 100khz -- 60mhz / ((1+dwClockDivisor)*2) = clock speed in mhz
                                          //uint dwClockDivisor = 0x012B;

        uint dwNumBytesToSend = 0;         // Counter used to hold number of bytes to be sent
        uint dwNumBytesSent = 0;       // Holds number of bytes actually sent (returned by the read function)

        uint dwNumInputBuffer = 0;     // Number of bytes which we want to read
        uint dwNumBytesRead = 0;       // Number of bytes actually read
        uint ReadTimeoutCounter = 0;       // Used as a software timeout counter when the code checks the Queue Status
        //uint dwCount = 0;

        byte[] ByteDataRead = new byte[15];          // Array for storing the data which was read from the I2C Slave
        //bool DataInBuffer = false;         // Flag which code sets when the GetNumBytesAvailable returned is > 0 
        //byte DataByte = 0;          // Used to store data bytes read from and written to the I2C Slave

        // I2C FUNCTIONS


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
            dwNumBytesToSend = 0;                           // Clear output buffer

            // Clock one byte of data in...
            OutputBuffer[dwNumBytesToSend++] = 0x20;        // Command to clock data byte in on the clock rising edge
            OutputBuffer[dwNumBytesToSend++] = 0x00;        // Length (low)
            OutputBuffer[dwNumBytesToSend++] = 0x00;        // Length (high)   Length 0x0000 means clock ONE byte in 

            // Now clock out one bit (the ACK/NAK bit). This bit has value '1' to send a NAK to the I2C Slave
            OutputBuffer[dwNumBytesToSend++] = 0x13;        // Command to clock data bits out on clock falling edge
            OutputBuffer[dwNumBytesToSend++] = 0x00;        // Length of 0x00 means clock out ONE bit
            OutputBuffer[dwNumBytesToSend++] = 0xFF;        // Command will send bit 7 of this byte, we send a '1' here

            // Put I2C line back to idle (during transfer) state... Clock line driven low, Data line high (open drain)
            OutputBuffer[dwNumBytesToSend++] = 0x80;        // Command to set lower 8 bits of port (ADbus 0-7 on the FT232H)
            OutputBuffer[dwNumBytesToSend++] = 0x1A;        // gpo1, rst, and data high 00111011 
            OutputBuffer[dwNumBytesToSend++] = 0x3B;        // 00111011

            // AD0 (SCL) is output driven low
            // AD1 (DATA OUT) is output high (open drain)
            // AD2 (DATA IN) is input (therefore the output value specified is ignored)
            // AD3 to AD7 are inputs driven high (not used in this application)

            // This command then tells the MPSSE to send any results gathered back immediately
            OutputBuffer[dwNumBytesToSend++] = 0x87;        // Send answer back immediate command

            ftStatus = _ftdiDevice.Write(OutputBuffer, dwNumBytesToSend, ref dwNumBytesSent);     // Send off the commands to the FT232H

            // ===============================================================
            // Now wait for the byte which we read to come back to the host PC
            // ===============================================================

            dwNumInputBuffer = 0;
            ReadTimeoutCounter = 0;

            ftStatus = _ftdiDevice.GetRxBytesAvailable(ref dwNumInputBuffer);  // Get number of bytes in the input buffer

            while ((dwNumInputBuffer < 1) && (ftStatus == FTDI.FT_STATUS.FT_OK) && (ReadTimeoutCounter < 500))
            {
                // Sit in this loop until
                // (1) we receive the one byte expected
                // or (2) a hardware error occurs causing the GetQueueStatus to return an error code
                // or (3) we have checked 500 times and the expected byte is not coming 
                ftStatus = _ftdiDevice.GetRxBytesAvailable(ref dwNumInputBuffer);  // Get number of bytes in the input buffer
                ReadTimeoutCounter++;
            }

            // If the loop above exited due to the byte coming back (not an error code and not a timeout)
            // then read the byte available and return True to indicate success
            if ((ftStatus == FTDI.FT_STATUS.FT_OK) && (ReadTimeoutCounter < 500))
            {
                ftStatus = _ftdiDevice.Read(InputBuffer, dwNumInputBuffer, ref dwNumBytesRead); // Now read the data
                ByteDataRead[0] = InputBuffer[0];               // return the data read in the global array ByteDataRead
                return true;                            // Indicate success
            }
            else
            {
                return false;                           // Failed to get any data back or got an error code back
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
            dwNumBytesToSend = 0;           // Clear output buffer
            FTDI.FT_STATUS ftStatus = FTDI.FT_STATUS.FT_OK;

            OutputBuffer[dwNumBytesToSend++] = 0x11;        // command to clock data bytes out MSB first on clock falling edge
            OutputBuffer[dwNumBytesToSend++] = 0x00;        // 
            OutputBuffer[dwNumBytesToSend++] = 0x00;        // Data length of 0x0000 means 1 byte data to clock out
            OutputBuffer[dwNumBytesToSend++] = dwDataSend;  // Actual byte to clock out

            // Put I2C line back to idle (during transfer) state... Clock line driven low, Data line high (open drain)
            OutputBuffer[dwNumBytesToSend++] = 0x80;        // Command to set lower 8 bits of port (ADbus 0-7 on the FT232H)
            OutputBuffer[dwNumBytesToSend++] = 0x1A;        // Set the value of the pins (only affects those set as output)
            OutputBuffer[dwNumBytesToSend++] = 0x3B;        // Set the directions - all pins as output except Bit2(data_in)

            // AD0 (SCL) is output driven low
            // AD1 (DATA OUT) is output high (open drain)
            // AD2 (DATA IN) is input (therefore the output value specified is ignored)
            // AD3 to AD7 are inputs driven high (not used in this application)

            OutputBuffer[dwNumBytesToSend++] = 0x22;    // Command to clock in bits MSB first on clock rising edge
            OutputBuffer[dwNumBytesToSend++] = 0x00;    // Length of 0x00 means to scan in 1 bit

            // This command then tells the MPSSE to send any results gathered back immediately
            OutputBuffer[dwNumBytesToSend++] = 0x87;    //Send answer back immediate command

            ftStatus = _ftdiDevice.Write(OutputBuffer, dwNumBytesToSend, ref dwNumBytesSent);     //Send off the commands
                                                                                                //_logger.Info("Byte Sent, waiting for ACK...");    
                                                                                                // ===============================================================
                                                                                                // Now wait for the byte which we read to come back to the host PC
                                                                                                // ===============================================================

            dwNumInputBuffer = 0;
            ReadTimeoutCounter = 0;

            ftStatus = _ftdiDevice.GetRxBytesAvailable(ref dwNumInputBuffer);  // Get number of bytes in the input buffer

            while ((dwNumInputBuffer < 1) && (ftStatus == FTDI.FT_STATUS.FT_OK) && (ReadTimeoutCounter < 5500))
            {
                // Sit in this loop until
                // (1) we receive the one byte expected
                // or (2) a hardware error occurs causing the GetQueueStatus to return an error code
                // or (3) we have checked 500 times and the expected byte is not coming 
                ftStatus = _ftdiDevice.GetRxBytesAvailable(ref dwNumInputBuffer);  // Get number of bytes in the input buffer
                                                                            //_logger.Info("counter: %d, bytes: %d, ftStatus: %d", ReadTimeoutCounter, dwNumInputBuffer, ftStatus);
                ReadTimeoutCounter++;
            }

            // If the loop above exited due to the byte coming back (not an error code and not a timeout)

            if ((ftStatus == FTDI.FT_STATUS.FT_OK) && (ReadTimeoutCounter < 2500))
            {
                ftStatus = _ftdiDevice.Read(InputBuffer, dwNumInputBuffer, ref dwNumBytesRead); // Now read the data
                                                                                               //_logger.Info("status was %d, input was 0x%X", ftStatus, InputBuffer[0]);  
                if (((InputBuffer[0] & 0x01) == 0x0))       //Check ACK bit 0 on data byte read out
                {
                    //_logger.Info("received ACK.");
                    return true;    // Return True if the ACK was received
                }
                else
                    _logger.Info($"Failed to get ACK from I2C Slave when sending {dwDataSend} First Byte: {InputBuffer[0]:X}");
                return false; //Error, can't get the ACK bit 
            }
            else
            {
                _logger.Info($"Error: {ftStatus} status");
                return false;   // Failed to get any data back or got an error code back
            }

        }

        //my function
        public bool SendByte(byte dwDataSend)
        {
            dwNumBytesToSend = 0;           // Clear output buffer
            FTDI.FT_STATUS ftStatus = FTDI.FT_STATUS.FT_OK;

            OutputBuffer[dwNumBytesToSend++] = 0x11;        // command to clock data bytes out MSB first on clock falling edge
            OutputBuffer[dwNumBytesToSend++] = 0x00;        // 
            OutputBuffer[dwNumBytesToSend++] = 0x00;        // Data length of 0x0000 means 1 byte data to clock out
            OutputBuffer[dwNumBytesToSend++] = dwDataSend;  // Actual byte to clock out

            // Put I2C line back to idle (during transfer) state... Clock line driven low, Data line high (open drain)
            OutputBuffer[dwNumBytesToSend++] = 0x80;        // Command to set lower 8 bits of port (ADbus 0-7 on the FT232H)
            OutputBuffer[dwNumBytesToSend++] = 0x1A;        // Set the value of the pins (only affects those set as output)
            OutputBuffer[dwNumBytesToSend++] = 0x3B;        // Set the directions - all pins as output except Bit2(data_in)

            // AD0 (SCL) is output driven low
            // AD1 (DATA OUT) is output high (open drain)
            // AD2 (DATA IN) is input (therefore the output value specified is ignored)
            // AD3 to AD7 are inputs driven high (not used in this application)

            OutputBuffer[dwNumBytesToSend++] = 0x22;    // Command to clock in bits MSB first on clock rising edge
            OutputBuffer[dwNumBytesToSend++] = 0x00;    // Length of 0x00 means to scan in 1 bit

            // This command then tells the MPSSE to send any results gathered back immediately
            OutputBuffer[dwNumBytesToSend++] = 0x87;    //Send answer back immediate command

            ftStatus = _ftdiDevice.Write(OutputBuffer, dwNumBytesToSend, ref dwNumBytesSent);     //Send off the commands
            _logger.Info($"Status: {ftStatus};Sent: {dwDataSend:X}; Send {dwNumBytesToSend}; Sent: {dwNumBytesSent}");

            return true;
        }

        /// <summary>
        /// Fully configured send. First byte should be address
        /// Send Idea from https://raw.githubusercontent.com/gurvindrasingh/AnyI2C/master/AnyI2cLib/FT232HI2C.cs
        /// </summary>
        /// <param name="data"></param>
        /// <returns></returns>
        public bool SendBytes(params byte[] data)
        {
            var succeeded = false;
            SetI2CLinesIdle();							// Set idle line condition
            try
            {
                SetI2CStart(); // Send the start condition
                if (data != null)
                {
                    for (int i = 0; i < data.Length; i++)
                    {
                        succeeded = i == 0
                            ? SendAddressAndCheckACK((byte) data[0], false)
                            : SendByteAndCheckACK(data[i]);

                        if (!succeeded)
                        {
                            _logger.Info($"Send Failed");
                            return false;
                        }
                    }
                }
            }
            finally
            {
                SetI2CStop();								// Send the stop condition	
            }
            
            return succeeded;
        }

        public bool SendByteRaw1(byte[] bytes)
        {
            dwNumBytesToSend = 0;           // Clear output buffer
            FTDI.FT_STATUS ftStatus = FTDI.FT_STATUS.FT_OK;

            OutputBuffer[dwNumBytesToSend++] = 0x11;        // command to clock data bytes out MSB first on clock falling edge

            var length = BitConverter.GetBytes((ushort)bytes.Length);
            foreach (var b in length)
            {
                OutputBuffer[dwNumBytesToSend++] = b;
            }

            foreach (var b in bytes)
            {
                OutputBuffer[dwNumBytesToSend++] = b;  // Actual byte to clock out    
            }

            // Put I2C line back to idle (during transfer) state... Clock line driven low, Data line high (open drain)
            OutputBuffer[dwNumBytesToSend++] = 0x80;        // Command to set lower 8 bits of port (ADbus 0-7 on the FT232H)
            OutputBuffer[dwNumBytesToSend++] = 0x1A;        // Set the value of the pins (only affects those set as output)
            OutputBuffer[dwNumBytesToSend++] = 0x3B;        // Set the directions - all pins as output except Bit2(data_in)

            // AD0 (SCL) is output driven low
            // AD1 (DATA OUT) is output high (open drain)
            // AD2 (DATA IN) is input (therefore the output value specified is ignored)
            // AD3 to AD7 are inputs driven high (not used in this application)

            OutputBuffer[dwNumBytesToSend++] = 0x22;    // Command to clock in bits MSB first on clock rising edge
            OutputBuffer[dwNumBytesToSend++] = 0x00;    // Length of 0x00 means to scan in 1 bit

            // This command then tells the MPSSE to send any results gathered back immediately
            OutputBuffer[dwNumBytesToSend++] = 0x87;    //Send answer back immediate command

            ftStatus = _ftdiDevice.Write(OutputBuffer, dwNumBytesToSend, ref dwNumBytesSent);     //Send off the commands

            _logger.Info($"Status: {ftStatus};Sent: {bytes.Length}; Send {dwNumBytesToSend}; Total Sent: {dwNumBytesSent}");

            dwNumInputBuffer = 0;
            ReadTimeoutCounter = 0;

            ftStatus = _ftdiDevice.GetRxBytesAvailable(ref dwNumInputBuffer);  // Get number of bytes in the input buffer

            while ((dwNumInputBuffer < 1) && (ftStatus == FTDI.FT_STATUS.FT_OK) && (ReadTimeoutCounter < 5500))
            {
                // Sit in this loop until
                // (1) we receive the one byte expected
                // or (2) a hardware error occurs causing the GetQueueStatus to return an error code
                // or (3) we have checked 500 times and the expected byte is not coming 
                ftStatus = _ftdiDevice.GetRxBytesAvailable(ref dwNumInputBuffer);  // Get number of bytes in the input buffer
                                                                                   //_logger.Info("counter: %d, bytes: %d, ftStatus: %d", ReadTimeoutCounter, dwNumInputBuffer, ftStatus);
                ReadTimeoutCounter++;
            }

            // If the loop above exited due to the byte coming back (not an error code and not a timeout)

            if ((ftStatus == FTDI.FT_STATUS.FT_OK) && (ReadTimeoutCounter < 2500))
            {
                ftStatus = _ftdiDevice.Read(InputBuffer, dwNumInputBuffer, ref dwNumBytesRead); // Now read the data
                                                                                                //_logger.Info("status was %d, input was 0x%X", ftStatus, InputBuffer[0]);  
                if (((InputBuffer[0] & 0x01) == 0x0))       //Check ACK bit 0 on data byte read out
                {
                    _logger.Info("received ACK.");
                    return true;    // Return True if the ACK was received
                }
                else
                    _logger.Info($"Failed to get ACK from I2C Slave when sending {bytes:X} First Index: {InputBuffer[0]:X}");
                return false; //Error, can't get the ACK bit 
            }
            else
            {
                _logger.Info($"Error: {ftStatus} status");
                return false;   // Failed to get any data back or got an error code back
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
            dwNumBytesToSend = 0;           // Clear output buffer
            FTDI.FT_STATUS ftStatus = FTDI.FT_STATUS.FT_OK;

            // Combine the Read/Write bit and the actual data to make a single byte with 7 data bits and the R/W in the LSB
            if (read)
            {
                address = (byte)((address << 1) | 0x01);
            }
            else
            {
                address = (byte)((address << 1) & 0xFE);
            }

            OutputBuffer[dwNumBytesToSend++] = 0x11;        // command to clock data bytes out MSB first on clock falling edge
            OutputBuffer[dwNumBytesToSend++] = 0x00;        // 
            OutputBuffer[dwNumBytesToSend++] = 0x00;        // Data length of 0x0000 means 1 byte data to clock out
            OutputBuffer[dwNumBytesToSend++] = address;  // Actual byte to clock out

            // Put I2C line back to idle (during transfer) state... Clock line driven low, Data line high (open drain)
            OutputBuffer[dwNumBytesToSend++] = 0x80;        // Command to set lower 8 bits of port (ADbus 0-7 on the FT232H)
            OutputBuffer[dwNumBytesToSend++] = 0x1A;        // 00011010
            OutputBuffer[dwNumBytesToSend++] = 0x3B;        // 00111011

            // AD0 (SCL) is output driven low
            // AD1 (DATA OUT) is output high (open drain)
            // AD2 (DATA IN) is input (therefore the output value specified is ignored)
            // AD3 to AD7 are inputs driven high (not used in this application)

            OutputBuffer[dwNumBytesToSend++] = 0x22;    // Command to clock in bits MSB first on clock rising edge
            OutputBuffer[dwNumBytesToSend++] = 0x00;    // Length of 0x00 means to scan in 1 bit

            // This command then tells the MPSSE to send any results gathered back immediately
            OutputBuffer[dwNumBytesToSend++] = 0x87;    //Send answer back immediate command

            ftStatus = _ftdiDevice.Write(OutputBuffer, dwNumBytesToSend, ref dwNumBytesSent);     //Send off the commands

            //Check if ACK bit received by reading the byte sent back from the FT232H containing the ACK bit
            ftStatus = _ftdiDevice.Read(InputBuffer, 1, ref dwNumBytesRead);      //Read one byte from device receive buffer

            if ((ftStatus != FTDI.FT_STATUS.FT_OK) || (dwNumBytesRead == 0))
            {
                _logger.Info("Failed to get ACK from I2C Slave - 0 bytes read");
                return false; //Error, can't get the ACK bit
            }
            else
            {
                var v = InputBuffer[0];
                var ack = (v & 0x01);
                if (ack != 0x00)     //Check ACK bit 0 on data byte read out
                {
                    //_logger.Info("Failed to get ACK from I2C Slave - Response was 0x%X", InputBuffer[0]);
                    return false; //Error, can't get the ACK bit 
                }

            }
            //_logger.Info("Received ACK bit from Address 0x%X - 0x%X", dwDataSend, InputBuffer[0]);
            return true;       // Return True if the ACK was received
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
            dwNumBytesToSend = 0;           //Clear output buffer

            // Set the idle states for the AD lines
            OutputBuffer[dwNumBytesToSend++] = 0x80;    // Command to set directions of ADbus and data values for pins set as o/p
            OutputBuffer[dwNumBytesToSend++] = 0x1B;        // 00011011
            OutputBuffer[dwNumBytesToSend++] = _gpioDMask; //0xD4; //0x3B;    // 00111011

            // IDLE line states are ...
            // AD0 (SCL) is output high (open drain, pulled up externally)
            // AD1 (DATA OUT) is output high (open drain, pulled up externally)
            // AD2 (DATA IN) is input (therefore the output value specified is ignored)

            // Set the idle states for the AC lines
            OutputBuffer[dwNumBytesToSend++] = 0x82;    // Command to set directions of ACbus and data values for pins set as o/p
            OutputBuffer[dwNumBytesToSend++] = 0xFF;    // 11111111
            OutputBuffer[dwNumBytesToSend++] = 0x40;    // 01000000
                                                        //_logger.Info("i2c lines set to idle");

            // IDLE line states are ...
            // AC6 (LED) is output driving high
            // AC0/1/2/3/4/5/7 are inputs (not used in this application)

            ftStatus = _ftdiDevice.Write(OutputBuffer, dwNumBytesToSend, ref dwNumBytesSent);     //Send off the commands
        }


        // ##############################################################################################################
        // Function to set the I2C Start state on the I2C clock and data lines
        // It pulls the data line low and then pulls the clock line low to produce the start condition
        // It also sends a GPIO command to set bit 6 of ACbus low to turn on the LED. This acts as an activity indicator
        // Turns on (low) during the I2C Start and off (high) during the I2C stop condition, giving a short blink.  
        // ##############################################################################################################
        public void SetI2CStart()
        {
            dwNumBytesToSend = 0;           //Clear output buffer
            uint dwCount;

            // Pull Data line low, leaving clock high (open-drain)
            for (dwCount = 0; dwCount < 4; dwCount++)  // Repeat commands to ensure the minimum period of the start hold time is achieved
            {
                OutputBuffer[dwNumBytesToSend++] = 0x80;    // Command to set directions of ADbus and data values for pins set as o/p
                OutputBuffer[dwNumBytesToSend++] = 0xFD;    // Bring data out low (bit 1)
                OutputBuffer[dwNumBytesToSend++] = 0xFB;    // Set all pins as output except bit 2 which is the data_in
            }

            // Pull Clock line low now, making both clock and data low
            for (dwCount = 0; dwCount < 4; dwCount++)  // Repeat commands to ensure the minimum period of the start setup time is achieved
            {
                OutputBuffer[dwNumBytesToSend++] = 0x80;    // Command to set directions of ADbus and data values for pins set as o/p
                OutputBuffer[dwNumBytesToSend++] = 0xFC;    // Bring clock line low too to make clock and data low
                OutputBuffer[dwNumBytesToSend++] = 0xFB;    // Set all pins as output except bit 2 which is the data_in
            }

            // Turn the LED on by setting port AC6 low.
            OutputBuffer[dwNumBytesToSend++] = 0x82;    // Command to set directions of upper 8 pins and force value on bits set as output
            OutputBuffer[dwNumBytesToSend++] = 0xBF;    // 10111111
            OutputBuffer[dwNumBytesToSend++] = 0x40;    // 01000000

            ftStatus = _ftdiDevice.Write(OutputBuffer, dwNumBytesToSend, ref dwNumBytesSent);     //Send off the commands
                                                                                                //_logger.Info("i2c lines set to start");
        }



        // ##############################################################################################################
        // Function to set the I2C Stop state on the I2C clock and data lines
        // It takes the clock line high whilst keeping data low, and then takes both lines high
        // It also sends a GPIO command to set bit 6 of ACbus high to turn off the LED. This acts as an activity indicator
        // Turns on (low) during the I2C Start and off (high) during the I2C stop condition, giving a short blink.  
        // ##############################################################################################################

        public void SetI2CStop()
        {
            dwNumBytesToSend = 0;           //Clear output buffer
            uint dwCount;

            // Initial condition for the I2C Stop - Pull data low (Clock will already be low and is kept low)
            for (dwCount = 0; dwCount < 4; dwCount++)        // Repeat commands to ensure the minimum period of the stop setup time is achieved
            {
                OutputBuffer[dwNumBytesToSend++] = 0x80;    // Command to set directions of ADbus and data values for pins set as o/p
                OutputBuffer[dwNumBytesToSend++] = 0xFC;    // put data and clock low
                OutputBuffer[dwNumBytesToSend++] = 0xFB;    // Set all pins as output except bit 2 which is the data_in
            }

            // Clock now goes high (open drain)
            for (dwCount = 0; dwCount < 4; dwCount++)        // Repeat commands to ensure the minimum period of the stop setup time is achieved
            {
                OutputBuffer[dwNumBytesToSend++] = 0x80;    // Command to set directions of ADbus and data values for pins set as o/p
                OutputBuffer[dwNumBytesToSend++] = 0xFD;    // put data low, clock remains high (open drain, pulled up externally)
                OutputBuffer[dwNumBytesToSend++] = 0xFB;    // Set all pins as output except bit 2 which is the data_in
            }

            // Data now goes high too (both clock and data now high / open drain)
            for (dwCount = 0; dwCount < 4; dwCount++)    // Repeat commands to ensure the minimum period of the stop hold time is achieved
            {
                OutputBuffer[dwNumBytesToSend++] = 0x80;    // Command to set directions of ADbus and data values for pins set as o/p
                OutputBuffer[dwNumBytesToSend++] = 0xFF;    // both clock and data now high (open drain, pulled up externally)
                OutputBuffer[dwNumBytesToSend++] = 0xFB;    // Set all pins as output except bit 2 which is the data_in
            }

            // Turn the LED off by setting port AC6 high.
            OutputBuffer[dwNumBytesToSend++] = 0x82;    // Command to set directions of upper 8 pins and force value on bits set as output
            OutputBuffer[dwNumBytesToSend++] = 0xFF;    // 11111111
            OutputBuffer[dwNumBytesToSend++] = 0x40;    // 01000000

            ftStatus = _ftdiDevice.Write(OutputBuffer, dwNumBytesToSend, ref dwNumBytesSent);     //Send off the commands
                                                                                                //_logger.Info("i2c stop");
        }

        // OPENING DEVICE AND MPSSE CONFIGURATION

        public bool SetupMpsse()
        {
            // Open the FT232H module by it's description in the EEPROM
            // Note: See FT_OpenEX in the D2xx Programmers Guide for other options available
            ftStatus = _ftdiDevice.OpenByIndex(DeviceIndex);

            // Check if Open was successful
            if (ftStatus != FTDI.FT_STATUS.FT_OK)
            {
                _logger.Info($"Can't open {DeviceIndex} device!");
                return false;
            }
            else
            {
                // #########################################################################################
                // After opening the device, Put it into MPSSE mode
                // #########################################################################################

                // Print message to show port opened successfully
                _logger.Info("Successfully opened FT232H device.");

                // Reset the FT232H
                ftStatus |= _ftdiDevice.ResetDevice();

                // Purge USB receive buffer ... Get the number of bytes in the FT232H receive buffer and then read them
                ftStatus |= _ftdiDevice.GetRxBytesAvailable(ref dwNumInputBuffer);
                if ((ftStatus == FTDI.FT_STATUS.FT_OK) && (dwNumInputBuffer > 0))
                {
                    _ftdiDevice .Read(InputBuffer, dwNumInputBuffer, ref dwNumBytesRead);
                }
                _logger.Info("Purged receive buffer.");

                var pinSetupBuffer = new byte[]
                {
                    0x80,
                    0,
                    0xBB
                };

                //ftStatus |= _ftdiDevice.SetBaudRate(9600);
                ftStatus |= _ftdiDevice.InTransferSize(65536);        // Set USB request transfer sizes
                ftStatus |= _ftdiDevice.SetCharacters(0, false, 0, false);          // Disable event and error characters
                ftStatus |= _ftdiDevice.SetTimeouts(5000, 5000);           // Set the read and write timeouts to 5 seconds
                ftStatus |= _ftdiDevice.SetLatency(16);               // Keep the latency timer at default of 16ms
                ftStatus |= _ftdiDevice.SetBitMode(0x0, FTDI.FT_BIT_MODES.FT_BIT_MODE_RESET);             // Reset the mode to whatever is set in EEPROM
                ftStatus |= _ftdiDevice.SetBitMode(0x0, FTDI.FT_BIT_MODES.FT_BIT_MODE_MPSSE);             // Enable MPSSE mode
                ftStatus |= _ftdiDevice.Write(pinSetupBuffer, pinSetupBuffer.Length, ref dwNumBytesSent);

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

                dwNumBytesToSend = 0;

                OutputBuffer[dwNumBytesToSend++] = 0x84;
                // Enable internal loopback
                ftStatus = _ftdiDevice.Write(OutputBuffer, dwNumBytesToSend, ref dwNumBytesSent);
                if (ftStatus != FTDI.FT_STATUS.FT_OK) { _logger.Info("failed"); }

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
                OutputBuffer[dwNumBytesToSend++] = 0xAA;
                // Bogus command added to queue
                ftStatus = _ftdiDevice.Write(OutputBuffer, dwNumBytesToSend, ref dwNumBytesSent);
                //_logger.Info("sent.");
                dwNumBytesToSend = 0;

                do
                {
                    ftStatus = _ftdiDevice.GetRxBytesAvailable(ref dwNumBytesRead);
                    // Get the number of bytes in the device input buffer
                } while ((dwNumBytesRead == 0) && (ftStatus == FTDI.FT_STATUS.FT_OK));

                // Or timeout
                bCommandEchod = 0;
                ftStatus = _ftdiDevice.Read(InputBuffer, dwNumBytesRead, ref dwNumBytesRead);
                // Read out the input buffer

                // Check if bad command and echo command are received
                uint count;
                for (count = 0; count < dwNumBytesRead - 1; count++)
                {
                    if ((InputBuffer[count] == 0xFA) && (InputBuffer[count + 1] == 0xAA))
                    {
                        //_logger.Info("Success. Input buffer contained 0x%X and 0x%X", InputBuffer[dwCount], InputBuffer[dwCount+1]);
                        bCommandEchod = 1;
                        break;
                    }
                }

                if (bCommandEchod == 0)
                {
                    _logger.Info("failed to synchronize MPSSE with command 0xAA ");
                    _logger.Info($"{InputBuffer[count]}, {InputBuffer[count + 1]}");
                    _ftdiDevice.Close();
                    return false;
                }

                // #########################################################################################
                // Synchronise the MPSSE by sending bad command AB to it
                // #########################################################################################

                dwNumBytesToSend = 0;
                //_logger.Info("");
                //_logger.Info("Sending bogus command 0xAB...");
                OutputBuffer[dwNumBytesToSend++] = 0xAB;
                // Bogus command added to queue
                ftStatus = _ftdiDevice.Write(OutputBuffer, dwNumBytesToSend, ref dwNumBytesSent);
                dwNumBytesToSend = 0;

                do
                {
                    ftStatus = _ftdiDevice.GetRxBytesAvailable(ref dwNumBytesRead);
                    // Get the number of bytes in the device input buffer
                } while ((dwNumBytesRead == 0) && (ftStatus == FTDI.FT_STATUS.FT_OK));
                // Or timeout


                bCommandEchod = 0;
                ftStatus = _ftdiDevice.Read(InputBuffer, dwNumBytesRead, ref dwNumBytesRead);
                // Read out the input buffer

                for (count = 0; count < dwNumBytesRead - 1; count++)
                // Check if bad command and echo command are received
                {
                    if ((InputBuffer[count] == 0xFA) && (InputBuffer[count + 1] == 0xAB))
                    {
                        bCommandEchod = 1;
                        //_logger.Info("Success. Input buffer contained 0x%X and 0x%X", InputBuffer[dwCount], InputBuffer[dwCount+1]);
                        break;
                    }
                }
                if (bCommandEchod == 0)
                {
                    _logger.Info("failed to synchronize MPSSE with command 0xAB ");
                    _logger.Info($"{InputBuffer[count]}, {InputBuffer[count + 1]}");
                    _ftdiDevice.Close();
                    return false;
                }

                dwNumBytesToSend = 0;
                //_logger.Info("Disabling internal loopback...");
                OutputBuffer[dwNumBytesToSend++] = 0x85;
                // Disable loopback
                ftStatus = _ftdiDevice.Write(OutputBuffer, dwNumBytesToSend, ref dwNumBytesSent);
                if (ftStatus != FTDI.FT_STATUS.FT_OK)
                {
                    _logger.Info("command failed");
                }
                else
                {
                    //_logger.Info("disabled.");
                }

                // #########################################################################################
                // Configure the MPSSE settings
                // #########################################################################################

                dwNumBytesToSend = 0;                   // Clear index to zero
                OutputBuffer[dwNumBytesToSend++] = 0x8A;        // Disable clock divide-by-5 for 60Mhz master clock
                OutputBuffer[dwNumBytesToSend++] = 0x97;        // Ensure adaptive clocking is off
                OutputBuffer[dwNumBytesToSend++] = 0x8C;        // Enable 3 phase data clocking, data valid on both clock edges for I2C

                OutputBuffer[dwNumBytesToSend++] = 0x9E;        // Enable the FT232H's drive-zero mode on the lines used for I2C ...
                OutputBuffer[dwNumBytesToSend++] = 0x07;        // ... on the bits 0, 1 and 2 of the lower port (AD0, AD1, AD2)...
                OutputBuffer[dwNumBytesToSend++] = 0x00;        // ...not required on the upper port AC 0-7

                OutputBuffer[dwNumBytesToSend++] = 0x85;        // Ensure internal loopback is off

                ftStatus = _ftdiDevice.Write(OutputBuffer, dwNumBytesToSend, ref dwNumBytesSent); // Send off the commands

                // Now configure the dividers to set the SCLK frequency which we will use
                // The SCLK clock frequency can be worked out by the algorithm (when divide-by-5 is off)
                // SCLK frequency  = 60MHz /((1 +  [(1 +0xValueH*256) OR 0xValueL])*2)
                dwNumBytesToSend = 0;                               // Clear index to zero
                OutputBuffer[dwNumBytesToSend++] = 0x86;                    // Command to set clock divisor
                OutputBuffer[dwNumBytesToSend++] = (byte)(dwClockDivisor & 0xFF);           // Set 0xValueL of clock divisor
                OutputBuffer[dwNumBytesToSend++] = (byte)((dwClockDivisor >> 8) & 0xFF);        // Set 0xValueH of clock divisor
                ftStatus = _ftdiDevice.Write(OutputBuffer, dwNumBytesToSend, ref dwNumBytesSent); // Send off the commands
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
            }

            return true;
        }

        public void ShutdownFtdi()
        {
            _logger.Info("Shutting Down");
            _ftdiDevice.SetBitMode(0x0, 0x00);
            _ftdiDevice.Close();
        }

        public void ScanDevicesAndQuit()
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
                    if (address < 16)
                    {
                        _logger.Info("0");
                    }
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
                    if (numDevices > 1)
                    {
                        _logger.Info($"{numDevices} devices found.");
                    }
                }
            }

            _logger.Info("Shutting Down");
            _ftdiDevice.SetBitMode(0x0, 0x00);
            _ftdiDevice.Close();
        }

        public bool GetPinStatus(byte mask)
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
}
