using System;
using SkiaSharp;

namespace OledI2cCore
{
    public class OledImage
    {
        private SKImage _image;

        /// <summary>
        /// Input Image (bmp, jpg, png, etc) that can be decoded by a byte array.
        /// </summary>
        /// <param name="image"></param>
        public OledImage(byte[] image)
        {
            _image = SKImage.FromEncodedData(image);
        }

        /// <summary>
        /// Original input image width
        /// </summary>
        public int ImageWidth => _image.Width;

        /// <summary>
        /// Original input image height
        /// </summary>
        public int ImageHeight => _image.Height;

        /// <summary>
        /// Keep an image in aspect ratio, but within a certain size
        /// </summary>
        /// <param name="width"></param>
        /// <param name="height"></param>
        /// <returns></returns>
        public OledImageData GetOledBytesMaxSize(int width, int height)
        {
            var aspectRatio = ImageWidth / (double)ImageHeight;

            var heightForWidth = (int)Math.Round(width / aspectRatio);
            if (heightForWidth <= height)
            {
                // within the boundaries
                return GetOledBytesMaxHeight(heightForWidth);
            }
            else
            {
                var widthForHeight = (int)Math.Round(height * aspectRatio);
                return GetOledBytesMaxWidth(widthForHeight);
            }
        }

        /// <summary>
        /// Resize Image with new width, keeping aspect ratio
        /// </summary>
        /// <param name="newWidth"></param>
        /// <returns></returns>
        public OledImageData GetOledBytesMaxWidth(int newWidth)
        {
            var aspectRatio = (int)Math.Round(ImageWidth / (double)ImageHeight);

            var newHeight = (newWidth / aspectRatio);

            return GetOledBytes(newWidth, newHeight);
        }

        /// <summary>
        /// Resize image with new height keeping aspect ratio
        /// </summary>
        /// <param name="newHeight"></param>
        /// <returns></returns>
        public OledImageData GetOledBytesMaxHeight(int newHeight)
        {
            var aspectRatio = (int)Math.Round(ImageWidth / (double)ImageHeight);

            var newWidth = (newHeight * aspectRatio);

            return GetOledBytes(newWidth, newHeight);
        }

        /// <summary>
        /// Get the Oled Bytes needed to render. Does not keep aspect ratio, consider using GetOledBytesMaxSize(x,y);
        /// </summary>
        /// <param name="newWidth"></param>
        /// <param name="newHeight"></param>
        /// <returns></returns>
        public OledImageData GetOledBytes(int newWidth, int newHeight)
        {
            var skImageInfo = new SKImageInfo(newWidth, newHeight, SKColorType.Gray8, SKAlphaType.Unpremul);
            using var bmp = SKBitmap.FromImage(_image);
            using var bmp1 = bmp.Resize(skImageInfo, SKFilterQuality.High);

            return new OledImageData(newWidth, newHeight, bmp1.GetPixelSpan().ToArray());
        }
    }

    public struct OledImageData
    {
        public OledImageData(int width, int height, byte[] data)
        {
            Width = width;
            Height = height;
            ImageData = data;
        }

        public int Width { get; set; }

        public int Height { get; set; }

        public byte[] ImageData { get; set; }
    }
}
