using System;
using System.Drawing;

namespace NAPS2.Scan
{
    /// <summary>
    /// Un code-barres candidat localisé, redressé à 0 degré et prêt pour ZXing.
    /// L'appelant doit libérer <see cref="Image"/> (Dispose).
    /// </summary>
    public sealed class LocatedBarcode : IDisposable
    {
        /// <summary>Sous-image redressée (Format32bppArgb, niveaux de gris) avec zone calme.</summary>
        public Bitmap Image { get; internal set; }

        /// <summary>Angle (degrés) de l'axe long du code dans l'image source (sens horaire, y vers le bas).</summary>
        public double AngleDegrees { get; internal set; }

        /// <summary>Centre de la boîte dans l'image source (pixels).</summary>
        public PointF Center { get; internal set; }

        /// <summary>Longueur et hauteur de la boîte orientée (sans zone calme), en pixels source.</summary>
        public SizeF BoxSize { get; internal set; }

        /// <summary>Score relatif (plus grand = plus probable).</summary>
        public double Score { get; internal set; }

        public void Dispose()
        {
            if (Image != null)
            {
                Image.Dispose();
                Image = null;
            }
        }
    }
}
