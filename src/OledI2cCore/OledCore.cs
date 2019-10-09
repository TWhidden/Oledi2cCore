using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace OledI2cCore
{
    public class OledCore
    {
        private readonly II2C _wire;

        private static readonly Oled_Font_5x7 DefaultFont = new Oled_Font_5x7();
        public byte Height { get; }
        public byte Width { get; }

        private readonly byte _address;

        public byte LineSpacing { get; }
        public byte LetterSpacing { get; }

        private readonly IOledLogger _logger;
        private readonly ScreenDriver _screenDriver;

        private static readonly Dictionary<byte, string> CommandLookup = new Dictionary<byte, string>(new List<KeyValuePair<byte, string>>
        {
            new KeyValuePair<byte, string>(DISPLAY_OFF, nameof(DISPLAY_OFF)),
            new KeyValuePair<byte, string>(DISPLAY_ON, nameof(DISPLAY_ON)),
            new KeyValuePair<byte, string>(SET_DISPLAY_CLOCK_DIV, nameof(SET_DISPLAY_CLOCK_DIV)),
            new KeyValuePair<byte, string>(SET_MULTIPLEX, nameof(SET_MULTIPLEX)),
            new KeyValuePair<byte, string>(SET_DISPLAY_OFFSET, nameof(SET_DISPLAY_OFFSET)),
            new KeyValuePair<byte, string>(CHARGE_PUMP, nameof(CHARGE_PUMP)),
            new KeyValuePair<byte, string>(MEMORY_MODE, nameof(MEMORY_MODE)),
            new KeyValuePair<byte, string>(SEG_REMAP, nameof(SEG_REMAP)),
            new KeyValuePair<byte, string>(COM_SCAN_DEC, nameof(COM_SCAN_DEC)),
            new KeyValuePair<byte, string>(COM_SCAN_INC, nameof(COM_SCAN_INC)),
            new KeyValuePair<byte, string>(SET_COM_PINS, nameof(SET_COM_PINS)),
            new KeyValuePair<byte, string>(SET_CONTRAST, nameof(SET_CONTRAST)),
            new KeyValuePair<byte, string>(SET_PRECHARGE, nameof(SET_PRECHARGE)),
            new KeyValuePair<byte, string>(SET_VCOM_DETECT, nameof(SET_VCOM_DETECT)),
            new KeyValuePair<byte, string>(DISPLAY_ALL_ON_RESUME, nameof(DISPLAY_ALL_ON_RESUME)),
            new KeyValuePair<byte, string>(NORMAL_DISPLAY, nameof(NORMAL_DISPLAY)),
            new KeyValuePair<byte, string>(COLUMN_ADDR, nameof(COLUMN_ADDR)),
            new KeyValuePair<byte, string>(INVERSE_DISPLAY, nameof(INVERSE_DISPLAY)),
            new KeyValuePair<byte, string>(ACTIVATE_SCROLL, nameof(ACTIVATE_SCROLL)),
            new KeyValuePair<byte, string>(DEACTIVATE_SCROLL, nameof(DEACTIVATE_SCROLL)),
            new KeyValuePair<byte, string>(SET_VERTICAL_SCROLL_AREA, nameof(SET_VERTICAL_SCROLL_AREA)),
            new KeyValuePair<byte, string>(RIGHT_HORIZONTAL_SCROLL, nameof(RIGHT_HORIZONTAL_SCROLL)),
            new KeyValuePair<byte, string>(LEFT_HORIZONTAL_SCROLL, nameof(LEFT_HORIZONTAL_SCROLL)),
            new KeyValuePair<byte, string>(VERTICAL_AND_RIGHT_HORIZONTAL_SCROLL, nameof(VERTICAL_AND_RIGHT_HORIZONTAL_SCROLL)),
            new KeyValuePair<byte, string>(VERTICAL_AND_LEFT_HORIZONTAL_SCROLL, nameof(VERTICAL_AND_LEFT_HORIZONTAL_SCROLL)),
            new KeyValuePair<byte, string>(LOW_COL_ADDR, nameof(LOW_COL_ADDR)),
            new KeyValuePair<byte, string>(HIGH_COL_ADDR, nameof(HIGH_COL_ADDR)),
            new KeyValuePair<byte, string>(SET_PAGE_ADDRESS, nameof(SET_PAGE_ADDRESS))
        });
            

        // create command buffers
        const byte DISPLAY_OFF = 0xAE;
        const byte DISPLAY_ON = 0xAF;
        const byte SET_DISPLAY_CLOCK_DIV = 0xD5;
        const byte SET_MULTIPLEX = 0xA8;
        const byte SET_DISPLAY_OFFSET = 0xD3;
        const byte CHARGE_PUMP = 0x8D;
        const bool EXTERNAL_VCC = true;
        const byte VCC_EXTERNAL = 0x10;
        const byte VCC_INTERNAL = 0x14;
        const byte MEMORY_MODE = 0x20;
        const byte SEG_REMAP = 0xA1; // using 0xA0 will flip screen
        const byte COM_SCAN_DEC = 0xC8;
        const byte COM_SCAN_INC = 0xC0;
        const byte SET_COM_PINS = 0xDA;
        const byte SET_CONTRAST = 0x81;
        const byte SET_PRECHARGE = 0xd9;
        const byte SET_VCOM_DETECT = 0xDB;
        const byte DISPLAY_ALL_ON_RESUME = 0xA4;
        const byte NORMAL_DISPLAY = 0xA6;
        const byte INVERSE_DISPLAY = 0xA7;
        const byte COLUMN_ADDR = 0x21;
        const byte ACTIVATE_SCROLL = 0x2F;
        const byte DEACTIVATE_SCROLL = 0x2E;
        const byte SET_VERTICAL_SCROLL_AREA = 0xA3;
        const byte RIGHT_HORIZONTAL_SCROLL = 0x26;
        const byte LEFT_HORIZONTAL_SCROLL = 0x27;
        const byte VERTICAL_AND_RIGHT_HORIZONTAL_SCROLL = 0x29;
        const byte VERTICAL_AND_LEFT_HORIZONTAL_SCROLL = 0x2A;
        const byte SSD1306_DISPLAYALLON_RESUME = 0xA4;
        const byte LOW_COL_ADDR = 0x00;
        const byte HIGH_COL_ADDR = 0x10;
        const byte SET_PAGE_ADDRESS = 0xB0;
        const byte SET_START_LINE = 0x40;

        const byte MODE_COMMAND = 0x00;
        const byte MODE_DATA = 0x40;

        private byte _cursorX = 0;
        private byte _cursorY = 0;

        private readonly byte[] _screenBuffer;
        private readonly HashSet<int> _dirtyBytes = new HashSet<int>();

        private static readonly Dictionary<string, ScreenConfig> ScreenConfigs = new Dictionary<string, ScreenConfig>();
        private readonly ScreenConfig _screenConfig;

        static OledCore()
        {
            ScreenConfigs.Add("128x32", new ScreenConfig(0x1f, 0x02, 0));
            ScreenConfigs.Add("128x64", new ScreenConfig(0x3F, 0x12, 0));
            ScreenConfigs.Add("132x64", new ScreenConfig(0x3F, 0x12, 0));
            ScreenConfigs.Add("96x16", new ScreenConfig(0x0F, 0x2, 0));
        }

        public OledCore(II2C wire, byte width = 128, byte height = 32, byte address = 0x3C, byte lineSpacing = 1, byte letterSpacing = 1, IOledLogger logger = null, ScreenDriver screenDriver = ScreenDriver.SSD1306)
        {
            _wire = wire;

            Height = height;
            Width = width;
            _address = address;
            LineSpacing = lineSpacing;
            LetterSpacing = letterSpacing;
            _logger = logger;
            _screenDriver = screenDriver;

            _screenBuffer = new byte[this.Width * this.Height / 8];
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
        }

        public void Initialise()
        {
            // sequence of bytes to initialise with
            var initSeq = new byte[] {
                DISPLAY_OFF,
                SET_DISPLAY_CLOCK_DIV, 0x80,
                SET_MULTIPLEX, _screenConfig.Multiplex, // set the last value dynamically based on screen size requirement
                SET_DISPLAY_OFFSET, 0x00,
                SET_START_LINE | 0x0,
                CHARGE_PUMP, EXTERNAL_VCC ? VCC_EXTERNAL : VCC_INTERNAL,
                MEMORY_MODE, 0x00,
                SEG_REMAP | 0x1, // screen orientation
                COM_SCAN_DEC, // screen orientation change to INC to flip
                SET_COM_PINS, _screenConfig.ComPins, // com pins val sets dynamically to match each screen size requirement
                SET_CONTRAST, (EXTERNAL_VCC) ? 0x9F : 0xCF, // contrast val
                SET_PRECHARGE, (EXTERNAL_VCC) ? 0x22 : 0xF1, // precharge val
                SET_VCOM_DETECT, 0x40, // vcom detect
                SSD1306_DISPLAYALLON_RESUME,
                NORMAL_DISPLAY,
                DISPLAY_ON
            };

            // write init seq commands
            foreach (var byteToTransfer in initSeq)
            {
                TransferCommand(byteToTransfer);
            }

            ClearDisplay(true);

            _logger?.Info("Display on");
            
        }

        private bool TransferData(byte[] data)
        {
            _logger?.Info("\n\n****DATA");
            try
            {
                _wire.SetI2CStart();
                var success = _wire.SendAddressAndCheckAck(_address, false)
                              && _wire.SendByte(MODE_DATA)
                              && _wire.SendBytes(data);
            }
            finally
            {
                _wire.SetI2CStop();
            }
            
            return true;
        }

        private bool TransferData(byte data)
        {
            _logger?.Info("\n\n****DATA");
            _wire.SetI2CStart();
            var success = _wire.SendAddressAndCheckAck(_address, false);
            _wire.SendByte(data);
            _wire.SetI2CStop();
            return true;
        }

        private bool TransferCommand(byte command)
        {
            _logger?.Info("\n\n****Command");

            _logger?.Info(CommandLookup.TryGetValue(command, out var commandName)
                ? $"Sending Command {commandName}"
                : $"Sending Command {command:X}");
            try
            {
                _wire.SetI2CStart();
                return _wire.SendAddressAndCheckAck(_address, false)
                       && _wire.SendByteAndCheckAck(MODE_COMMAND)
                       && _wire.SendByteAndCheckAck(command);
            }
            finally
            {
                _wire.SetI2CStop();
            }
        }

        public void SetCursor(byte x, byte y)
        {
            _cursorX = x;
            _cursorY = y;
        }

        public void WriteString(byte positionX, byte positionY, string message, byte size)
        {
            SetCursor(positionX, positionY);
            WriteString(DefaultFont, size, message);
        }

        public void WriteString(IFont font, byte size, string message, byte color = 255, bool wrap = true, bool sync = false)
        {
            var wordArr = message.Split(new[] {' '}, StringSplitOptions.RemoveEmptyEntries);
            var len = wordArr.Length;
            // start x offset at cursor pos
            var offset = _cursorX;

            // loop through words
            for (var w = 0; w < len; w += 1)
            {
                // put the word space back in for all in between words or empty words
                if (w < len - 1 || wordArr[w].Length != 0)
                {
                    wordArr[w] += ' ';
                }


                var stringArr = wordArr[w].ToCharArray();
                var slen = stringArr.Length;
                var compare = (font.Width * size * slen) + (size * (len - 1));

                // wrap words if necessary
                if (wrap && len > 1 && (offset >= (this.Width - compare)))
                {
                    offset = 0;

                    _cursorY += (byte)((font.Height * size) + this.LineSpacing);
                    SetCursor(offset, _cursorY);
                }

                // loop through the array of each char to draw
                for (var i = 0; i < slen; i += 1)
                {
                    if (stringArr[i] == '\n')
                    {
                        offset = 0;
                        _cursorY += (byte)((font.Height * size) + this.LineSpacing);
                        SetCursor(offset, _cursorY);
                    }
                    else
                    {
                        // look up the position of the char, pull out the _screenBuffer slice

                        if(!font.FontData.TryGetValue(stringArr[i], out var charBuf)) continue;
                        // read the bits in the bytes that make up the char

                        var charBytes = ReadCharBytes(charBuf);
                        // draw the entire character
                        DrawChar(charBytes, size, false);

                        // calc new x position for the next char, add a touch of padding too if it's a non space char
                        //padding = (stringArr[i] === ' ') ? 0 : this.LetterSpacing;
                        offset += (byte)( (font.Width * size) + this.LetterSpacing );// padding;

                        // wrap letters if necessary
                        if (wrap && (offset >= (this.Width - font.Width - this.LetterSpacing)))
                        {
                            offset = 0;
                            _cursorY += (byte)( (font.Height * size) + this.LineSpacing);
                        }
                        // set the 'cursor' for the next char to be drawn, then loop again for next char
                        SetCursor(offset, _cursorY);
                    }
                }
            }

            if (sync)
            {
                UpdateDirtyBytes();
            }

        }



        /// <summary>
        /// Get character bytes from the supplied font object in order to send to framebuffer
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
                    var bit = b >> j & 1;
                    bitArr.Add((byte)bit);
                }
                // push to array containing flattened bit sequence
                bitCharArr.Add(bitArr.ToArray());
                // clear bits for next byte
                bitArr.Clear();
            }
            return bitCharArr.ToArray();
        }

        public void DrawChar(byte[][] byteArray, double size, bool sync)
        {
            // take your positions...
            var x = _cursorX;
            var y = _cursorY;

            // loop through the byte array containing the hexes for the char
            for (byte i = 0; i < byteArray.Length; i += 1)
            {
                for (byte j = 0; j < 8; j += 1)
                {
                    // pull color out
                    var color = byteArray[i][j];
                    byte xpos;
                    byte ypos;
                    // standard font size
                    if (size == 1.0)
                    {
                        xpos = (byte)(x + i);
                        ypos = (byte)(y + j);
                        DrawPixel(new ScreenPixel(xpos, ypos, color), false);
                    } else {
                        // MATH! Calculating pixel size multiplier to primitively scale the font
                        xpos = (byte)(x + (i* size));
                        ypos = (byte)(y + (j* size));
                        this.FillRect(xpos, ypos, (byte)size, (byte)size, color, false);
                    }
                }
            }
        }

        public void DrawPixel(ScreenPixel pixel, bool sync = false)
        {
            DrawPixel(new [] {pixel}, sync);
        }

        private short Shift(int n)
        {
            return (short)(1 << n);

        }

        public void DrawPixel(short x, short y, short color, bool sync = false)
        {
            if ((x < 0) || (x >= Width) || (y < 0) || (y >= Height))
            {
                return;
            }

            var index = x + (y / 8) * Width;
            var orig = _screenBuffer[index];

            if (color > 0)
            {
                _screenBuffer[index] |= (byte) Shift((y % 8));
                //_screenBuffer[index] = (byte)(_screenBuffer[index] | (1 << (y & 7)));
            }
            else
            {
                _screenBuffer[index] &= (byte) ~Shift((y % 8));

                //_screenBuffer[index] == (_screenBuffer[index] & ~(1 << (y & 7)));
            }

            bool changeDetected = orig != _screenBuffer[index];

            if (changeDetected)
            {
                if (!_dirtyBytes.Contains(index)) _dirtyBytes.Add(index);
                if(sync) UpdateDirtyBytes();
            }
        }

        public void DrawPixel(ScreenPixel[] pixels, bool sync = false)
        {
            
            pixels.ToList().ForEach(el => {
                // return if the pixel is out of range
                byte x = el.X;
                byte y = el.Y;
                byte color = el.Color;

                if (x >= this.Width || y >= this.Height) return;

                // thanks, Martin Richards.
                // I wanna can this, this tool is for devs who get 0 indexes
                //x -= 1; y -=1;
                var pixelIndex = 0;
                byte page = (byte)Math.Floor(y / 8.0);
                byte pageShift = (byte)(0x01 << (y - 8 * page));

                // is the pixel on the first row of the page?
                pixelIndex = (page == 0) ? x : x + (Width * page);

                // colors! Well, monochrome.
                //color == "BLACK" || 
                if (color == 0)
                {
                    _screenBuffer[pixelIndex] = (byte)(_screenBuffer[pixelIndex] & ~pageShift);
                }

                // color == "WHITE"
                if (color > 0)
                {
                    _screenBuffer[pixelIndex] |= pageShift;
                }

                // push byte to dirty if not already there
                if (!_dirtyBytes.Contains(pixelIndex))
                {
                    _dirtyBytes.Add(pixelIndex);
                }

            });

            if (sync)
            {
                UpdateDirtyBytes();
            }
        }

        public void UpdateDirtyBytes()
        {
            if (_dirtyBytes.Count == 0) return;

            var byteArray = _dirtyBytes.ToArray();
            var blen = byteArray.Length;

            // check to see if this will even save time
            if (blen > (this._screenBuffer.Length / 7))
            {
                // just call regular update at this stage, saves on bytes sent
                Update();
                // now that all bytes are synced, reset dirty state
                this._dirtyBytes.Clear();
            }
            else
            {
                _logger?.Info("Update Dirty");
                
                bool sent = false;

                // iterate through dirty bytes
                for (var i = 0; i < blen; i += 1)
                {

                    var byteIndex = byteArray[i];
                    byte page = (byte)Math.Floor(((double)byteIndex / Width));
                    byte col = (byte)Math.Floor(((double)byteIndex % Width));

                    sent = GoCoordinate(col, page);

                    if (sent)
                    {
                        // send byte, then move on to next byte
                        //sent = TransferData(_screenBuffer[byte1]);
                        sent = TransferData(new [] {_screenBuffer[byteIndex]});
                        if (!sent)
                        {
                            _logger?.Info($"Failed Sending Data {_screenBuffer[byteIndex]:X}");
                        }
                    }
                }
                
            }
            // now that all bytes are synced, reset dirty state
            this._dirtyBytes.Clear();
        }

        public void Update()
        {
            _logger?.Info("Update All");

            // currently a dirty, non-performant hack - need to get this back to do something like 16 bytes at a time. 
            // TODO: circle back and resolve this issue.
            var bufferToSend = new byte[1];

            for (var i = 0; i < _screenBuffer.Length;)
            {
                try
                {
                    if (i % Width == 0)
                    {
                        var y = (byte) Math.Floor((i / (double) Width));
                        var success = GoCoordinate(0, y);
                        if (!success)
                        {
                            continue;
                        }
                    }

                    Buffer.BlockCopy(_screenBuffer, i, bufferToSend, 0, bufferToSend.Length);
                    TransferData(bufferToSend);

                }
                finally
                {
                    i += bufferToSend.Length;
                }
            }
        }

        private bool GoCoordinate(byte x, byte page)
        {
            if (x >= Width || page >= (Height / 8))
                return false;

            switch (_screenDriver)
            {
                case ScreenDriver.SH1106:
                    x += 2; //offset : panel is 128 ; RAM is 132 for sh1106
                    break;
            }

            var row = (SET_PAGE_ADDRESS + page);
            var lowColumn = LOW_COL_ADDR | (x & 0xF);
            var highColumn = (HIGH_COL_ADDR | (x >> 4));

            return TransferCommand((byte)row) // Set row
                   && TransferCommand((byte)lowColumn)  // Set lower column address
                   && TransferCommand((byte)highColumn); //Set higher column address
        }

        public void FillRect(byte x, byte y, byte w, byte h, byte color, bool sync)
        {
            // one iteration for each column of the rectangle
            for (var i = x; i < x + w; i += 1)
            {
                // draws a vert line
                DrawLine(i, y, i, (byte)(y + h - 1), color, false);
            }
            if (sync)
            {
                UpdateDirtyBytes();
            }
        }

        // using Bresenham's line algorithm
        public void DrawLine(byte x0, byte y0, byte x1, byte y1, byte color, bool sync = false)
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

                if (e2 > -dx) { err -= dy; x0 += (byte)sx; }
                if (e2 < dy) { err += dx; y0 += (byte)sy; }
            }

            if (sync)
            {
                UpdateDirtyBytes();
            }
        }



        private static byte[] Combine(byte[] first, byte[] second)
        {
            byte[] ret = new byte[first.Length + second.Length];
            Buffer.BlockCopy(first, 0, ret, 0, first.Length);
            Buffer.BlockCopy(second, 0, ret, first.Length, second.Length);
            return ret;
        }

        public void ClearDisplay(bool sync = false)
        {
            Array.Clear(_screenBuffer, 0, _screenBuffer.Length);
            if (sync)
            {
                Update();
            }
        }

        public void DrawBitmap(short x, short y, byte[] bmp, short w, short h, bool color) 
        {
            short byteWidth = (short)((w + 7) / 8);

            for(var j=0; j<h; j++) {
                for(var i=0; i<w; i++ ) {
                    if((bmp[j* byteWidth + i / 8] & (128 >> (i & 7))) > 0 ) {
                        DrawPixel((short)(x+i), (short)(y +j), 1);
                    } else {
                        DrawPixel((short)(x +i), (short)(y +j), 0);
                    }
                }
            }
        }
    }
    

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

    public struct ScreenPixel
    {
        public byte X { get; }
        public byte Y { get; }
        public byte Color { get; }

        public ScreenPixel(byte x, byte y, byte color)
        {
            X = x;
            Y = y;
            Color = color;
        }
    }

    public enum ScreenDriver
    {
        SH1106,
        SSD1306
    }
}
