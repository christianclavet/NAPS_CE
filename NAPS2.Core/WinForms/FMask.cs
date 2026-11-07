using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using NAPS2.Scan.Images;
using NAPS2.Scan.Images.Transforms;
using NAPS2.Util;

namespace NAPS2.WinForms
{
    partial class FMask : ImageForm
    {
        private static MaskTransform _lastTransform;
        private static Size _lastSize;

        private static readonly Cursor PickerCursor = new Cursor(global::NAPS2.Icons.color_wheel.GetHicon());

        private Point dragStartCoords;
        private LayoutManager lm;
        private bool pickingColor;

        private int originalWidth, originalHeight;

        public FMask(ChangeTracker changeTracker, ScannedImageRenderer scannedImageRenderer)
            : base(changeTracker, scannedImageRenderer)
        {
            InitializeComponent();
            SaveFormState = true;

            lm = new LayoutManager(this)
                .Bind(tbLeft, tbRight)
                    .WidthTo(() => (int)(GetImageWidthRatio() * pictureBox.Width))
                    .LeftTo(() => (int)((1 - GetImageWidthRatio()) * pictureBox.Width / 2))
                .Bind(tbTop, tbBottom)
                    .HeightTo(() => (int)(GetImageHeightRatio() * pictureBox.Height))
                    .TopTo(() => (int)((1 - GetImageHeightRatio()) * pictureBox.Height / 2))
                .Activate();

            originalWidth = originalHeight = 1000;
        }

        public MaskTransform MaskTransform { get; private set; }

        protected override IEnumerable<Transform> Transforms => new[] { MaskTransform };

        protected override PictureBox PictureBox => pictureBox;

        /// <summary>
        /// The fill color, persisted in the user configuration (white by default).
        /// </summary>
        private Color FillColor => Color.FromArgb(UserConfigManager.Config.MaskColorArgb);

        protected override Bitmap RenderPreview()
        {
            var bitmap = new Bitmap(originalWidth, originalHeight);
            using (var g = Graphics.FromImage(bitmap))
            {
                g.DrawImage(workingImage2, new Rectangle(0, 0, originalWidth, originalHeight));
                if (!MaskTransform.IsNull)
                {
                    var maskRect = new Rectangle(MaskTransform.Left, MaskTransform.Top,
                        originalWidth - MaskTransform.Left - MaskTransform.Right,
                        originalHeight - MaskTransform.Top - MaskTransform.Bottom);
                    using (var brush = new SolidBrush(FillColor))
                    {
                        g.FillRectangle(brush, maskRect);
                    }
                    using (var pen = new Pen(Color.Black, 2.0f))
                    {
                        g.DrawRectangle(pen, maskRect);
                    }
                }
            }

            return bitmap;
        }

        protected override void InitTransform()
        {
            originalWidth = workingImage.Width;
            originalHeight = workingImage.Height;
            if (_lastTransform != null && _lastSize == workingImage.Size)
            {
                MaskTransform = _lastTransform;
            }
            else
            {
                ResetTransform();
            }
            MaskTransform.FillColor = FillColor;
            UpdateMaskBounds();
            UpdateColorBox();
            lm.UpdateLayout();
        }

        protected override void ResetTransform()
        {
            MaskTransform = new MaskTransform
            {
                OriginalHeight = originalHeight,
                OriginalWidth = originalWidth,
                FillColor = FillColor
            };
        }

        protected override void TransformSaved()
        {
            _lastTransform = MaskTransform;
            _lastSize = workingImage.Size;
        }

        private double GetImageWidthRatio()
        {
            if (workingImage == null)
            {
                return 1;
            }
            double imageAspect = originalWidth / (double)originalHeight;
            double pboxAspect = pictureBox.Width / (double)pictureBox.Height;
            if (imageAspect > pboxAspect)
            {
                return 1;
            }
            return imageAspect / pboxAspect;
        }

        private double GetImageHeightRatio()
        {
            if (workingImage == null)
            {
                return 1;
            }
            double imageAspect = originalWidth / (double)originalHeight;
            double pboxAspect = pictureBox.Width / (double)pictureBox.Height;
            if (pboxAspect > imageAspect)
            {
                return 1;
            }
            return pboxAspect / imageAspect;
        }

        private void UpdateMaskBounds()
        {
            tbLeft.Maximum = tbRight.Maximum = originalWidth;
            tbTop.Maximum = tbBottom.Maximum = originalHeight;

            tbLeft.Value = MaskTransform.Left;
            tbBottom.Value = MaskTransform.Bottom;
            tbRight.Value = originalWidth - MaskTransform.Right;
            tbTop.Value = originalHeight - MaskTransform.Top;
        }

        private void UpdateTransform()
        {
            MaskTransform = new MaskTransform
            {
                Left = Math.Min(tbLeft.Value, tbRight.Value),
                Right = originalWidth - Math.Max(tbLeft.Value, tbRight.Value),
                Bottom = Math.Min(tbTop.Value, tbBottom.Value),
                Top = originalHeight - Math.Max(tbTop.Value, tbBottom.Value),
                OriginalHeight = originalHeight,
                OriginalWidth = originalWidth,
                FillColor = FillColor
            };
            UpdatePreviewBox();
        }

        private void UpdateColorBox()
        {
            pnlColor.BackColor = FillColor;
        }

        private void pnlColor_Click(object sender, EventArgs e)
        {
            if (workingImage == null)
            {
                return;
            }
            // Clicking the color box toggles the color picker mode
            pickingColor = !pickingColor;
            pictureBox.Cursor = pickingColor ? PickerCursor : Cursors.Cross;
        }

        private void PickColor(Point location)
        {
            var coords = TranslatePboxCoords(location);
            int x = Math.Min(coords.X, workingImage.Width - 1);
            int y = Math.Min(coords.Y, workingImage.Height - 1);
            var color = workingImage.GetPixel(x, y);

            UserConfigManager.Config.MaskColorArgb = color.ToArgb();
            UserConfigManager.Save();

            pickingColor = false;
            pictureBox.Cursor = Cursors.Cross;
            UpdateColorBox();
            UpdateTransform();
        }

        private void tbLeft_Scroll(object sender, EventArgs e)
        {
            UpdateTransform();
        }

        private void tbRight_Scroll(object sender, EventArgs e)
        {
            UpdateTransform();
        }

        private void tbBottom_Scroll(object sender, EventArgs e)
        {
            UpdateTransform();
        }

        private void tbTop_Scroll(object sender, EventArgs e)
        {
            UpdateTransform();
        }

        private void pictureBox_MouseDown(object sender, MouseEventArgs e)
        {
            if (pickingColor)
            {
                if (workingImage != null)
                {
                    PickColor(e.Location);
                }
                return;
            }
            dragStartCoords = TranslatePboxCoords(e.Location);
        }

        private void pictureBox_MouseMove(object sender, MouseEventArgs e)
        {
            if (!pickingColor && e.Button == MouseButtons.Left)
            {
                var dragEndCoords = TranslatePboxCoords(e.Location);
                if (dragEndCoords.X > dragStartCoords.X)
                {
                    tbLeft.Value = dragStartCoords.X;
                    tbRight.Value = dragEndCoords.X;
                }
                else
                {
                    tbLeft.Value = dragEndCoords.X;
                    tbRight.Value = dragStartCoords.X;
                }
                if (dragEndCoords.Y > dragStartCoords.Y)
                {
                    tbTop.Value = originalHeight - dragStartCoords.Y;
                    tbBottom.Value = originalHeight - dragEndCoords.Y;
                }
                else
                {
                    tbTop.Value = originalHeight - dragEndCoords.Y;
                    tbBottom.Value = originalHeight - dragStartCoords.Y;
                }
                UpdateTransform();
            }
        }

        private Point TranslatePboxCoords(Point point)
        {
            double px = point.X - 1;
            double py = point.Y - 1;
            double imageAspect = originalWidth / (double)originalHeight;
            double pboxWidth = (pictureBox.Width - 2);
            double pboxHeight = (pictureBox.Height - 2);
            double pboxAspect = pboxWidth / pboxHeight;
            if (pboxAspect > imageAspect)
            {
                // Empty space on left/right
                var emptyWidth = ((1 - imageAspect / pboxAspect) / 2 * pboxWidth);
                px = (pboxAspect / imageAspect * (px - emptyWidth));
            }
            else
            {
                // Empty space on top/bottom
                var emptyHeight = ((1 - pboxAspect / imageAspect) / 2 * pboxHeight);
                py = (imageAspect / pboxAspect * (py - emptyHeight));
            }
            double x = px / pboxWidth * originalWidth;
            double y = py / pboxHeight * originalHeight;
            x = Math.Max(Math.Min(x, originalWidth), 0);
            y = Math.Max(Math.Min(y, originalHeight), 0);
            return new Point((int)Math.Round(x), (int)Math.Round(y));
        }
    }
}
