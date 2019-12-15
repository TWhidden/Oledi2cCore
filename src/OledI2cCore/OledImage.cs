using SkiaSharp;

namespace OledI2cCore
{
    public class OledImage
    {
        private SKImage _image;

        public OledImage(byte[] image)
        {
            _image = SKImage.FromEncodedData(image);
        }

        public int ImageWidth => _image.Width;

        public int ImageHeight => _image.Height;

        public OledImageData GetOledBytes(int newHeight)
        {
            var aspectRatio = ImageWidth / ImageHeight;

            var newWidth = (newHeight * aspectRatio);

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
