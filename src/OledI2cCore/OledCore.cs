﻿using System;
using System.Buffers;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
// ReSharper disable InconsistentNaming

namespace OledI2cCore
{
    /// <summary>
    ///     OledCore class to manage a display.  Inspired by many source pages on github, converted from js and C++ to
    ///     accomplish many needs.
    ///     https://github.com/baltazorr/oled-i2c-bus/blob/master/oled.js
    ///     https://github.com/SuperHouse/esp-open-rtos/blob/master/extras/ssd1306/ssd1306.c
    /// </summary>
    public class OledCore
    {
        // create command buffers
        private const byte DISPLAY_OFF = 0xAE;
        private const byte DISPLAY_ON = 0xAF;
        private const byte SET_DISPLAY_CLOCK_DIV = 0xD5;
        private const byte SET_MULTIPLEX = 0xA8;
        private const byte SET_DISPLAY_OFFSET = 0xD3;
        private const byte CHARGE_PUMP = 0x8D;
        private const bool EXTERNAL_VCC = true;
        private const byte VCC_EXTERNAL = 0x10;
        private const byte VCC_INTERNAL = 0x14;
        private const byte MEMORY_MODE = 0x20;
        private const byte SEG_REMAP = 0xA1; // using 0xA0 will flip screen
        private const byte COM_SCAN_DEC = 0xC8;
        private const byte COM_SCAN_INC = 0xC0;
        private const byte SET_COM_PINS = 0xDA;
        private const byte SET_CONTRAST = 0x81;
        private const byte SET_PRECHARGE = 0xd9;
        private const byte SET_VCOM_DETECT = 0xDB;
        private const byte DISPLAY_ALL_ON_RESUME = 0xA4;
        private const byte NORMAL_DISPLAY = 0xA6;
        private const byte INVERSE_DISPLAY = 0xA7;
        private const byte COLUMN_ADDR = 0x21;
        private const byte ACTIVATE_SCROLL = 0x2F;
        private const byte DEACTIVATE_SCROLL = 0x2E;
        private const byte SET_VERTICAL_SCROLL_AREA = 0xA3;
        private const byte RIGHT_HORIZONTAL_SCROLL = 0x26;
        private const byte LEFT_HORIZONTAL_SCROLL = 0x27;
        private const byte VERTICAL_AND_RIGHT_HORIZONTAL_SCROLL = 0x29;
        private const byte VERTICAL_AND_LEFT_HORIZONTAL_SCROLL = 0x2A;
        private const byte LOW_COL_ADDR = 0x00;
        private const byte HIGH_COL_ADDR = 0x10;
        private const byte SET_PAGE_ADDRESS = 0xB0;
        private const byte SET_START_LINE = 0x40;

        // Command or Data Values
        private const byte MODE_COMMAND = 0x00;
        private const byte MODE_DATA = 0x40;

        /// <summary>
        ///     Default font to be used with simple string writes
        /// </summary>
        private static readonly Oled_Font_5x7 DefaultFont = new();

        private readonly List<Func<bool>> _initActions = new();

        /// <summary>
        /// As much as I hate locks, there is a potential issue with updating the hashset, while another operation is writing to it.
        /// </summary>
        private readonly object _screenUpdateLock = new();

        /// <summary>
        ///     Command lookup for Debugging
        /// </summary>
        // ReSharper disable once UnusedMember.Local
        private static readonly Dictionary<byte, string> CommandLookup = new(
            new List<KeyValuePair<byte, string>>
            {
                new(DISPLAY_OFF, nameof(DISPLAY_OFF)),
                new(DISPLAY_ON, nameof(DISPLAY_ON)),
                new(SET_DISPLAY_CLOCK_DIV, nameof(SET_DISPLAY_CLOCK_DIV)),
                new(SET_MULTIPLEX, nameof(SET_MULTIPLEX)),
                new(SET_DISPLAY_OFFSET, nameof(SET_DISPLAY_OFFSET)),
                new(CHARGE_PUMP, nameof(CHARGE_PUMP)),
                new(MEMORY_MODE, nameof(MEMORY_MODE)),
                new(SEG_REMAP, nameof(SEG_REMAP)),
                new(COM_SCAN_DEC, nameof(COM_SCAN_DEC)),
                new(COM_SCAN_INC, nameof(COM_SCAN_INC)),
                new(SET_COM_PINS, nameof(SET_COM_PINS)),
                new(SET_CONTRAST, nameof(SET_CONTRAST)),
                new(SET_PRECHARGE, nameof(SET_PRECHARGE)),
                new(SET_VCOM_DETECT, nameof(SET_VCOM_DETECT)),
                new(DISPLAY_ALL_ON_RESUME, nameof(DISPLAY_ALL_ON_RESUME)),
                new(NORMAL_DISPLAY, nameof(NORMAL_DISPLAY)),
                new(COLUMN_ADDR, nameof(COLUMN_ADDR)),
                new(INVERSE_DISPLAY, nameof(INVERSE_DISPLAY)),
                new(ACTIVATE_SCROLL, nameof(ACTIVATE_SCROLL)),
                new(DEACTIVATE_SCROLL, nameof(DEACTIVATE_SCROLL)),
                new(SET_VERTICAL_SCROLL_AREA, nameof(SET_VERTICAL_SCROLL_AREA)),
                new(RIGHT_HORIZONTAL_SCROLL, nameof(RIGHT_HORIZONTAL_SCROLL)),
                new(LEFT_HORIZONTAL_SCROLL, nameof(LEFT_HORIZONTAL_SCROLL)),
                new(VERTICAL_AND_RIGHT_HORIZONTAL_SCROLL,
                    nameof(VERTICAL_AND_RIGHT_HORIZONTAL_SCROLL)),
                new(VERTICAL_AND_LEFT_HORIZONTAL_SCROLL,
                    nameof(VERTICAL_AND_LEFT_HORIZONTAL_SCROLL)),
                new(LOW_COL_ADDR, nameof(LOW_COL_ADDR)),
                new(HIGH_COL_ADDR, nameof(HIGH_COL_ADDR)),
                new(SET_PAGE_ADDRESS, nameof(SET_PAGE_ADDRESS))
            });

        private static readonly Dictionary<string, ScreenConfig> ScreenConfigs = new();

        /// <summary>
        ///     The I2C screen index.
        /// </summary>
        private readonly byte _address;

        /// <summary>
        /// To reduce allocations, we will re-use this command array
        /// </summary>
        private readonly byte[] _commandArray = new byte[3];

        /// <summary>
        /// While using the _commandArray buffer to send to the wire, we will
        /// lock to prevent other entries from modifying it before the previous
        /// send has completed
        /// </summary>
        private readonly object _commandArrayLock = new();

        /// <summary>
        ///     For performance, we will track each index of which byte needs to be updated.
        ///     This is a set of byte indexes in the _screenBuffer object.
        /// </summary>
        private readonly HashSet<int> _dirtyBytes = new();

        /// <summary>
        ///     Logger Reference
        /// </summary>
        private readonly IOledLogger? _logger;

        /// <summary>
        ///     The buffer that holds the bytes that match the OLED.
        /// </summary>
        private readonly byte[] _screenBuffer;

        /// <summary>
        ///     Screen Configuration used with the display
        /// </summary>
        private readonly ScreenConfig _screenConfig;

        /// <summary>
        ///     Reference to which screen driver is being used.
        /// </summary>
        private readonly ScreenDriver _screenDriver;

        /// <summary>
        ///     Reference to the wire interface.
        /// </summary>
        private readonly II2C _wire;

        /// <summary>
        ///     Static ctor - Populate the command screen resolutions with their config overrides per screen
        /// </summary>
        static OledCore()
        {
            ScreenConfigs.Add("128x32", new ScreenConfig(0x1f, 0x02, 0));
            ScreenConfigs.Add("128x64", new ScreenConfig(0x3F, 0x12, 0));
            ScreenConfigs.Add("132x64", new ScreenConfig(0x3F, 0x12, 0));
            ScreenConfigs.Add("96x16", new ScreenConfig(0x0F, 0x2, 0));
        }

        public OledCore(II2C wire, byte width = 128, byte height = 32, byte address = 0x3C, byte lineSpacing = 1,
            byte letterSpacing = 1, IOledLogger? logger = null, ScreenDriver screenDriver = ScreenDriver.SSD1306)
        {
            _wire = wire;
            Height = height;
            Width = width;
            _address = address;
            
            // To reduce allocations, populate the command array first two elements
            // with what will be sent on each command
            _commandArray[0] = address;
            _commandArray[1] = MODE_COMMAND;

            LineSpacing = lineSpacing;
            LetterSpacing = letterSpacing;
            _logger = logger;
            _screenDriver = screenDriver;

            // Build the screen buffer with the passed in screen resolution
            _screenBuffer = new byte[Width * Height / 8];
            _logger?.Info($"Screen Buffer Size: {_screenBuffer.Length}");

            var screenSize = $"{Width}x{Height}";

            // Default Value
            _screenConfig = ScreenConfigs.First().Value;

            if (ScreenConfigs.TryGetValue(screenSize, out var screenConfig))
            {
                _logger?.Info($"Found matching screen config {screenSize}");
                _screenConfig = screenConfig;
            }
            else
            {
                _logger?.Info($"No available screen config for screen size {screenSize}");
            }

            if (_wire.ReadyState)
            {
                InitCommandStart();
            }

            _wire.ReadyStateChanged += _wire_ReadyStateChanged;

        }

        private void _wire_ReadyStateChanged(object? sender, bool e)
        {
            if (e)
            {
                InitCommandStart();
            }
        }

        /// <summary>
        /// Executions to run when the I2C is ready
        /// </summary>
        /// <param name="action"></param>
        public void InitCommandRegister(Func<bool> action)
        {
            _initActions.Add(action);
        }

        /// <summary>
        /// Clear the Init commands expected to run when the I2C is read
        /// </summary>
        public void InitCommandReset()
        {
            _initActions.Clear();
        }

        /// <summary>
        /// Init the Commands
        /// </summary>
        public void InitCommandStart()
        {
            foreach (var initAction in _initActions)
            {
                if (!initAction())
                {
                    Debug.WriteLine("Oled Failed Init Action");
                    return;
                }
            }
        }

        public byte Height { get; }
        public byte Width { get; }

        public byte LineSpacing { get; }
        public byte LetterSpacing { get; }

        public bool Initialise()
        {
            // sequence of bytes to initialise with
            var initSeq = new byte[]
            {
                DISPLAY_OFF,
                SET_DISPLAY_CLOCK_DIV, 0x80,
                SET_MULTIPLEX,
                _screenConfig.Multiplex, // set the last value dynamically based on screen size requirement
                SET_DISPLAY_OFFSET, 0x00,
                SET_START_LINE | 0x0,
                // ReSharper disable once HeuristicUnreachableCode
                CHARGE_PUMP, EXTERNAL_VCC ? VCC_EXTERNAL : VCC_INTERNAL,
                MEMORY_MODE, 0x00,
                SEG_REMAP | 0x1, // screen orientation
                COM_SCAN_DEC, // screen orientation change to INC to flip
                SET_COM_PINS,
                _screenConfig.ComPins, // com pins val sets dynamically to match each screen size requirement
                // ReSharper disable once HeuristicUnreachableCode
                SET_CONTRAST, EXTERNAL_VCC ? 0x9F : 0xCF, // contrast val
                // ReSharper disable once HeuristicUnreachableCode
                SET_PRECHARGE, EXTERNAL_VCC ? 0x22 : 0xF1, // precharge val
                SET_VCOM_DETECT, 0x40, // vcom detect
                DISPLAY_ALL_ON_RESUME,
                NORMAL_DISPLAY,
                DISPLAY_ON
            };

            var result = true;

            // write init seq commands
            foreach (var byteToTransfer in initSeq)
                if (!TransferCommand(byteToTransfer))
                    result = false;

            ClearDisplay(true);

            _logger?.Info($"Display on: {result}");

            return result;
        }

        /// <summary>
        /// Data will be transferred with this command. This is used for things like drawing the screen
        /// </summary>
        /// <param name="data">data, without the address of data command.</param>
        /// <returns></returns>
        private bool TransferData(byte[] data)
        {
            if (!_wire.ReadyState) return false;

            var shared = ArrayPool<byte>.Shared;
            // Rent a buffer from the shared pool
            var desiredLength = data.Length + 2;
            var rentedBytes = shared.Rent(desiredLength);

            try
            {
                rentedBytes[0] = _address;
                rentedBytes[1] = MODE_DATA;
                Buffer.BlockCopy(data, 0, rentedBytes, 2, data.Length);

                return _wire.SendBytes(rentedBytes, desiredLength);
            }
            finally
            {
                // Return the buffer back to the pool
                shared.Return(rentedBytes);
            }
        }

        /// <summary>
        /// Commands to be sent to the Oled. These commands are listed as constants above. Each screen
        /// has different values for starting them up and turning them on. See reference guide for more info
        /// </summary>
        /// <param name="command"></param>
        /// <returns></returns>
        private bool TransferCommand(byte command)
        {
            if (!_wire.ReadyState) return false;

            // Lock to prevent other entries from modifying the array
            // before the method has completed. 
            // This is just an allocation optimization instead of
            // creating a new array each send
            lock (_commandArrayLock)
            {
                // Update the command to be sent
                _commandArray[2] = command;
                return _wire.SendBytes(_commandArray, _commandArray.Length);
            }
        }

        /// <summary>
        /// Generic String writer
        /// </summary>
        /// <param name="positionX">X Position on the screen</param>
        /// <param name="positionY">Y Position on the screen</param>
        /// <param name="message">Desired Message</param>
        /// <param name="size">Override of the size.</param>
        /// <param name="writeWidth">Clear x pixels in width before writing new chars</param>
        /// <param name="wrap">Enable word wrapping</param>
        /// <param name="inverseColor">Inverse the color so text is black and the background is white</param>
        /// <param name="charMax">If supplied, will clear out the background area so when smaller text is written to this area, it wont leave behind old text</param>
        public void WriteString(byte positionX, byte positionY, string message, decimal size = 1, int writeWidth = -1, bool wrap = false, bool inverseColor = false, int charMax = -1)
        {
            // If text is being overwritten in the same place, you can optionally clear it 
            // bye providing the write with area. Otherwise, it will not be overwritten and will
            // look funky if you write smaller text.
            if (writeWidth > 0)
            {
                var height = DefaultFont.Height * size + LineSpacing;
                DrawFilledRectangle(positionX, positionY, writeWidth, (int)height, inverseColor ? ScreenColor.White : ScreenColor.Black);
            }

            // If the code calling this is tracking the max chars
            // this will clear out the area for smaller text based on the current font properties being used
            if (charMax > 0)
            {
                var height = DefaultFont.Height * size + LineSpacing;
                var width = DefaultFont.Width * charMax + LetterSpacing * charMax;
                DrawFilledRectangle(positionX, positionY, width, (int)height, inverseColor ? ScreenColor.White : ScreenColor.Black);
            }

            // push out the text to the screen buffer
            WriteString(positionX, positionY, DefaultFont, size, message, wrap: wrap, inverseColor: inverseColor);
            
        }

        /// <summary>
        /// Inspired by oled-i2c-bus node js project.  Will write the text string to the screen.
        /// </summary>
        /// <param name="cursorX">starting X position to start writing</param>
        /// <param name="cursorY">starting Y position to start writing</param>
        /// <param name="font"></param>
        /// <param name="size"></param>
        /// <param name="message"></param>
        /// <param name="inverseColor">Invert the color so text is black, and around the char is white</param>
        /// <param name="wrap"></param>
        /// <param name="sync"></param>
        public void WriteString(byte cursorX, byte cursorY, IFont font, decimal size, string message,
            bool inverseColor = false, bool wrap = true,
            bool sync = false)
        {
            var wordArr = message.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            var len = wordArr.Length;
            // start x offset at cursor pos

            // loop through words
            for (var w = 0; w < len; w += 1)
            {
                // put the word space back in for all in between words or empty words
                if (w < len - 1 || wordArr[w].Length != 0) wordArr[w] += ' ';

                var stringArr = wordArr[w].ToCharArray();
                var slen = stringArr.Length;
                var compare = font.Width * size * slen + size * (len - 1);

                // wrap words if necessary
                if (wrap && len > 1 && cursorX >= Width - compare)
                {
                    cursorX = 0;

                    cursorY += (byte)(font.Height * size + LineSpacing);
                    //SetCursor(offset, _cursorY);
                }

                // loop through the array of each char to draw
                for (var i = 0; i < slen; i += 1)
                    if (stringArr[i] == '\n')
                    {
                        cursorX = 0;
                        cursorY += (byte)(font.Height * size + LineSpacing);
                        //SetCursor(offset, _cursorY);
                    }
                    else
                    {
                        // look up the position of the char, pull out the _screenBuffer slice

                        if (!font.FontData.TryGetValue(stringArr[i], out var charBuf)) continue;
                        // read the bits in the bytes that make up the char

                        var charBytes = ReadCharBytes(charBuf);
                        // draw the entire character
                        DrawChar(cursorX, cursorY, charBytes, size, false, inverseColor: inverseColor);

                        // calc new x position for the next char, add a touch of padding too if it's a non space char
                        //padding = (stringArr[i] === ' ') ? 0 : this.LetterSpacing;
                        cursorX += (byte)(font.Width * size + LetterSpacing); // padding;

                        // wrap letters if necessary
                        if (wrap && cursorX >= Width - font.Width - LetterSpacing)
                        {
                            cursorX = 0;
                            cursorY += (byte)(font.Height * size + LineSpacing);
                        }

                        // set the 'cursor' for the next char to be drawn, then loop again for next char
                        //SetCursor(offset, _cursorY);
                    }
            }

            if (sync) UpdateDirtyBytes();
        }


        /// <summary>
        /// Get character bytes from the supplied font object in order to send to frame buffer
        /// </summary>
        /// <param name="byteArray"></param>
        /// <returns></returns>
        public byte[][] ReadCharBytes(byte[] byteArray)
        {
            var bitArr = new List<byte>();
            var bitCharArr = new List<byte[]>();

            // loop through each byte supplied for a char
            for (var i = 0; i < byteArray.Length; i += 1)
            {
                // set current byte
                var b = byteArray[i];
                // read each byte
                for (var j = 0; j < 8; j += 1)
                {
                    // shift bits right until all are read
                    var bit = (b >> j) & 1;
                    bitArr.Add((byte) bit);
                }

                // push to array containing flattened bit sequence
                bitCharArr.Add(bitArr.ToArray());
                // clear bits for next byte
                bitArr.Clear();
            }

            return bitCharArr.ToArray();
        }

        /// <summary>
        /// Draw a char
        /// </summary>
        /// <param name="cursorX"></param>
        /// <param name="cursorY"></param>
        /// <param name="byteArray"></param>
        /// <param name="size"></param>
        /// <param name="sync"></param>
        /// <param name="inverseColor">Invert the color</param>
        public void DrawChar(byte cursorX, byte cursorY, byte[][] byteArray, decimal size, bool sync, bool inverseColor)
        {
            // take your positions...
            //var x = _cursorX;
            //var y = _cursorY;

            // loop through the byte array containing the hexes for the char
            for (byte i = 0; i < byteArray.Length; i += 1)
            for (byte j = 0; j < 8; j += 1)
            {
                // pull color out
                var color = byteArray[i][j];

                ScreenColor screenColor;
                if (inverseColor)
                {
                    screenColor = color > 0 ? ScreenColor.Black : ScreenColor.White;
                }
                else
                {
                    screenColor = color == 0 ? ScreenColor.Black : ScreenColor.White;
                }

                byte xpos;
                byte ypos;
                // standard font size
                if (size == 1.0m)
                {
                    xpos = (byte) (cursorX + i);
                    ypos = (byte) (cursorY + j);
                    DrawPixel(new ScreenPixel(xpos, ypos, screenColor));
                }
                else
                {
                    // MATH! Calculating pixel size multiplier to primitively scale the font
                    xpos = (byte) (cursorX + i * size);
                    ypos = (byte) (cursorY + j * size);
                    DrawFilledRectangle(xpos, ypos, (int)size, (int)size, screenColor);
                }
            }

            // Sync if requested
            if(sync) UpdateDirtyBytes();
        }

        /// <summary>
        /// Draw a Pixel
        /// </summary>
        /// <param name="pixel"></param>
        /// <param name="sync"></param>
        public void DrawPixel(ScreenPixel pixel, bool sync = false)
        {
            DrawPixel(new[] {pixel}, sync);
        }

        private short Shift(int n)
        {
            return (short) (1 << n);
        }
        
        /// <summary>
        /// Draw Pixel at specific X/Y with specific color. 
        /// </summary>
        /// <param name="x"></param>
        /// <param name="y"></param>
        /// <param name="color"></param>
        /// <param name="sync"></param>
        public void DrawPixel(int x, int y, ScreenColor color, bool sync = false)
        {
            if (x < 0 || x >= Width || y < 0 || y >= Height) return;

            var index = x + y / 8 * Width;
            var orig = _screenBuffer[index];

            if (color == ScreenColor.White)
                _screenBuffer[index] |= (byte) Shift(y % 8);
            else
                _screenBuffer[index] &= (byte) ~Shift(y % 8);

            var changeDetected = orig != _screenBuffer[index];

            if (changeDetected)
            {
                lock (_screenUpdateLock)
                {
                    if (!_dirtyBytes.Contains(index)) _dirtyBytes.Add(index);
                }

                if (sync) UpdateDirtyBytes();
            }
        }

        /// <summary>
        /// Draw a pixel array
        /// </summary>
        /// <param name="pixels"></param>
        /// <param name="sync"></param>
        public void DrawPixel(ScreenPixel[] pixels, bool sync = false)
        {
            pixels.ToList().ForEach(el =>
            {
                // return if the pixel is out of range
                var x = el.X;
                var y = el.Y;
                var color = el.Color;

                if (x >= Width || y >= Height) return;

                var page = (byte) Math.Floor(y / 8.0);
                var pageShift = (byte) (0x01 << (y - 8 * page));

                // is the pixel on the first row of the page?
                var pixelIndex = page == 0 ? x : x + Width * page;

                // colors! Well, monochrome.
                //color == "BLACK" || 
                if (color == 0) _screenBuffer[pixelIndex] = (byte) (_screenBuffer[pixelIndex] & ~pageShift);

                // color == "WHITE"
                if (color > 0) _screenBuffer[pixelIndex] |= pageShift;

                // push byte to dirty if not already there
                lock (_screenUpdateLock)
                {
                    if (!_dirtyBytes.Contains(pixelIndex)) _dirtyBytes.Add(pixelIndex);
                }
            });

            if (sync) UpdateDirtyBytes();
        }

        /// <summary>
        /// Simple function to update only the pixels that have changed instead of the entire
        /// screen. If the system determines more than 1/7 of the screen is dirty
        /// it will force a full update
        /// </summary>
        public bool UpdateDirtyBytes()
        {
            bool success = true;

            lock (_screenUpdateLock)
            {
                if (_dirtyBytes.Count == 0) return success;

                var byteArray = _dirtyBytes.ToArray();
                var blen = byteArray.Length;

                // check to see if this will even save time
                if (blen > _screenBuffer.Length / 7)
                {
                    // just call regular update at this stage, saves on bytes sent
                    Update();
                    // now that all bytes are synced, reset dirty state
                    _dirtyBytes.Clear();
                }
                else
                {
                    // iterate through dirty bytes
                    for (var i = 0; i < blen; i += 1)
                    {
                        var byteIndex = byteArray[i];
                        var page = (byte)Math.Floor((double)byteIndex / Width);
                        var col = (byte)Math.Floor((double)byteIndex % Width);

                        success = GoCoordinate(col, page);

                        if (success)
                        {
                            // send byte, then move on to next byte
                            //sent = TransferData(_screenBuffer[byte1]);
                            success = TransferData(new[] { _screenBuffer[byteIndex] });
                            if (!success) _logger?.Info($"Failed Sending Data {_screenBuffer[byteIndex]:X}");
                        }
                    }
                }

                // now that all bytes are synced, reset dirty state
                _dirtyBytes.Clear();
            }

            return success;
        }

        /// <summary>
        /// Fully updates the screen - This comes at a cost, but will ensure its fully written to match your current buffer
        /// </summary>
        public void Update()
        {
            var bufferToSend = new byte[64];

            lock (_screenUpdateLock)
            {
                for (var i = 0; i < _screenBuffer.Length;)
                    try
                    {
                        if (i % Width == 0)
                        {
                            var y = (byte)Math.Floor(i / (double)Width);
                            var success = GoCoordinate(0, y);
                            if (!success) continue;
                        }

                        Buffer.BlockCopy(_screenBuffer, i, bufferToSend, 0, bufferToSend.Length);
                        TransferData(bufferToSend);
                    }
                    finally
                    {
                        i += bufferToSend.Length;
                    }

                // Now that all bytes are synced, reset the dirty state
                _dirtyBytes.Clear();
            }
        }

        /// <summary>
        /// Different screens have different positions, so calling this will set the current write position of the data before
        /// it is sent with the TransferData command.
        /// </summary>
        /// <param name="x"></param>
        /// <param name="page"></param>
        /// <returns></returns>
        private bool GoCoordinate(int x, int page)
        {
            if (x >= Width || page >= Height / 8)
                return false;

            switch (_screenDriver)
            {
                case ScreenDriver.SH1106:
                    x += 2; //offset : panel is 128 ; RAM is 132 for sh1106
                    break;
            }

            var row = SET_PAGE_ADDRESS + page;
            var lowColumn = LOW_COL_ADDR | (x & 0xF);
            var highColumn = HIGH_COL_ADDR | (x >> 4);

            return TransferCommand((byte)row) // Set row
                   && TransferCommand((byte)lowColumn) // Set lower column address
                   && TransferCommand((byte)highColumn); //Set higher column address
        }

        /// <summary>
        /// Draw a Rectangle
        /// </summary>
        /// <param name="x"></param>
        /// <param name="y"></param>
        /// <param name="w"></param>
        /// <param name="h"></param>
        /// <param name="color"></param>
        /// <param name="sync"></param>
        public void DrawFilledRectangle(int x, int y, int w, int h, ScreenColor color, bool sync = false)
        {
            // one iteration for each column of the rectangle
            for (var i = x; i < x + w; i += 1)
                // draws a vert line
                DrawLine(i, y, i, y + h - 1, color);

            if (sync) UpdateDirtyBytes();
        }

        /// <summary>
        /// Draw a line using Bresenham's line algorithm.
        /// https://en.wikipedia.org/wiki/Bresenham%27s_line_algorithm
        /// </summary>
        /// <param name="x0"></param>
        /// <param name="y0"></param>
        /// <param name="x1"></param>
        /// <param name="y1"></param>
        /// <param name="color"></param>
        /// <param name="sync"></param>
        public void DrawLine(int x0, int y0, int x1, int y1, ScreenColor color, bool sync = false)
        {
            var dx = Math.Abs(x1 - x0);
            var sx = x0 < x1 ? 1 : -1;
            var dy = Math.Abs(y1 - y0);
            var sy = y0 < y1 ? 1 : -1;
            var err = (dx > dy ? dx : -dy) / 2;

            while (true)
            {
                DrawPixel(x0, y0, color);

                if (x0 == x1 && y0 == y1) break;

                var e2 = err;

                if (e2 > -dx)
                {
                    err -= dy;
                    x0 += sx;
                }

                if (e2 < dy)
                {
                    err += dx;
                    y0 += sy;
                }
            }

            if (sync) UpdateDirtyBytes();
        }

        /// <summary>
        /// Clear the internal screen buffer with all zero values. Optionally Update
        /// </summary>
        /// <param name="sync"></param>
        public void ClearDisplay(bool sync = false)
        {
            Array.Clear(_screenBuffer, 0, _screenBuffer.Length);
            if (sync) Update();
        }

        /// <summary>
        /// Draw a bitmap array to the screen
        /// </summary>
        /// <param name="x">x position offset</param>
        /// <param name="y">y position offset</param>
        /// <param name="bmp">bitmap byte data</param>
        /// <param name="w">width of the image</param>
        /// <param name="h">height of the image</param>
        public void DrawBitmap(int x, int y, byte[] bmp, int w, int h)
        {
            for (var i = 0; i < bmp.Length; i++)
            {
                var x1 = (int)Math.Floor(i % (decimal)w);
                var y1 = (int)Math.Floor(i / (decimal)w);

                DrawPixel(x1 + x, y1 + y, (bmp[i] > 0 ? ScreenColor.White : ScreenColor.Black));
            }
        }

        public void DrawBitmap(int x, int y, OledImageData image)
        {
            DrawBitmap(x, y, image.ImageData, image.Width, image.Height);
        }
    }


    /// <summary>
    /// Simple Screen Configuration Container
    /// </summary>
    internal struct ScreenConfig
    {
        public byte Multiplex { get; }

        public byte ComPins { get; }

        public byte ColOffset { get; }

        public ScreenConfig(byte multiplex, byte comPins, byte colOffset)
        {
            Multiplex = multiplex;
            ComPins = comPins;
            ColOffset = colOffset;
        }
    }

    /// <summary>
    /// Pixel Placement Container
    /// </summary>
    public struct ScreenPixel
    {
        public byte X { get; }
        public byte Y { get; }
        public ScreenColor Color { get; }

        public ScreenPixel(byte x, byte y, ScreenColor color)
        {
            X = x;
            Y = y;
            Color = color;
        }
    }

    /// <summary>
    /// Possible Screen Drivers
    /// </summary>
    public enum ScreenDriver
    {
        SH1106,
        SSD1306
    }

    public enum ScreenColor : byte
    {
        Black = 0,
        White = 255
    }
}