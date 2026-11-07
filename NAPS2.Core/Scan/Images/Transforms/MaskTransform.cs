using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Xml.Serialization;
using NAPS2.Util;

namespace NAPS2.Scan.Images.Transforms
{
    [Serializable]
    public class MaskTransform : Transform
    {
        public int Left { get; set; }
        public int Right { get; set; }
        public int Top { get; set; }
        public int Bottom { get; set; }

        public int? OriginalWidth { get; set; }
        public int? OriginalHeight { get; set; }

        /// <summary>
        /// The fill color as an ARGB integer, so that it can be serialized.
        /// </summary>
        public int FillArgb { get; set; } = Color.White.ToArgb();

        [XmlIgnore]
        public Color FillColor
        {
            get => Color.FromArgb(FillArgb);
            set => FillArgb = value.ToArgb();
        }

        public override Bitmap Perform(Bitmap bitmap)
        {
            double xScale = bitmap.Width / (double)(OriginalWidth ?? bitmap.Width),
                yScale = bitmap.Height / (double)(OriginalHeight ?? bitmap.Height);

            int x = ((int)Math.Round(Left * xScale)).Clamp(0, bitmap.Width - 1);
            int y = ((int)Math.Round(Top * yScale)).Clamp(0, bitmap.Height - 1);
            int width = (bitmap.Width - (int)Math.Round((Left + Right) * xScale)).Clamp(1, bitmap.Width - x);
            int height = (bitmap.Height - (int)Math.Round((Top + Bottom) * yScale)).Clamp(1, bitmap.Height - y);

            bool isBlackWhite = bitmap.PixelFormat == PixelFormat.Format1bppIndexed;
            EnsurePixelFormat(ref bitmap);
            using (var g = Graphics.FromImage(bitmap))
            using (var brush = new SolidBrush(FillColor))
            {
                // The selected area is filled instead of being removed (unlike CropTransform)
                g.FillRectangle(brush, x, y, width, height);
            }

            if (isBlackWhite)
            {
                var monoBitmap = (Bitmap)BitmapHelper.CopyToBpp(bitmap, 1).Clone();
                bitmap.Dispose();
                return monoBitmap;
            }
            return bitmap;
        }

        public override bool IsNull => Left == 0 && Right == 0 && Top == 0 && Bottom == 0;
    }
}
