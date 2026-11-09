using System;
using System.Collections.Generic;
using System.Drawing;

namespace NAPS2.Scan
{
    public static partial class BarcodeLocator
    {
        /// <summary>
        /// Comme <see cref="Locate"/>, avec angle, centre et score de chaque candidat.
        /// </summary>
        public static List<LocatedBarcode> LocateDetailed(Bitmap source, int maxResults = 8, double quietZone = 0.15)
        {
            var results = new List<LocatedBarcode>();
            if (source == null || maxResults <= 0)
            {
                return results;
            }

            int width = source.Width;
            int height = source.Height;
            int factor = Math.Max(1, Math.Max(width, height) / AnalysisMaxDimension);
            int sw = width / factor;
            int sh = height / factor;
            if (sw < CellSize * 3 || sh < CellSize * 3)
            {
                return results;
            }

            // 1. Niveaux de gris pleine résolution (tampon réutilisé)
            int grayLength = width * height;
            if (_grayBuffer == null || _grayBuffer.Length < grayLength)
            {
                _grayBuffer = new byte[grayLength];
            }
            byte[] gray = _grayBuffer;
            ToGray(source, width, height, gray);

            // 2. Sous-échantillonnage par moyenne de blocs
            int smallLength = sw * sh;
            if (_smallBuffer == null || _smallBuffer.Length < smallLength)
            {
                _smallBuffer = new byte[smallLength];
            }
            byte[] small = _smallBuffer;
            Downsample(gray, width, small, sw, sh, factor);

            // 3. Tenseur de gradient par cellule + masque des cellules « barres »
            int cols = (sw + CellSize - 1) / CellSize;
            int rows = (sh + CellSize - 1) / CellSize;
            int cellCount = cols * rows;
            int[] jxx = new int[cellCount];
            int[] jyy = new int[cellCount];
            int[] jxy = new int[cellCount];
            AccumulateGradientTensor(small, sw, sh, cols, jxx, jyy, jxy);

            int activeTotal;
            byte[] active = BuildActiveMask(jxx, jyy, jxy, cellCount, out activeTotal);
            if (activeTotal < MinComponentCells)
            {
                return results;
            }

            // 4. Ouverture 3x3 (retire les traits fins) puis fermeture morphologique 1D
            //    (dilatation puis érosion) : le long de l'axe puis en travers
            byte[] a = (byte[])active.Clone();
            byte[] b = new byte[cellCount];
            int[] prefix = new int[Math.Max(cols, rows) + 1];
            Morph1D(a, b, cols, rows, OpenRadius, true, false, prefix);
            Morph1D(b, a, cols, rows, OpenRadius, false, false, prefix);
            Morph1D(a, b, cols, rows, OpenRadius, true, true, prefix);
            Morph1D(b, a, cols, rows, OpenRadius, false, true, prefix);
            Morph1D(a, b, cols, rows, CloseRadiusAlong, true, true, prefix);
            Morph1D(b, a, cols, rows, CloseRadiusAlong, true, false, prefix);
            Morph1D(a, b, cols, rows, CloseRadiusAcross, false, true, prefix);
            Morph1D(b, a, cols, rows, CloseRadiusAcross, false, false, prefix);

            // 5. Composantes connexes + moments
            double cellToSource = (double)CellSize * factor;
            List<Candidate> found = FindCandidates(a, active, jxx, jyy, jxy, cols, rows, cellToSource);
            found.Sort((x, y) => y.Score.CompareTo(x.Score));

            // 6. Redressement de chaque candidat
            for (int i = 0; i < found.Count && results.Count < maxResults; i++)
            {
                Candidate c = found[i];
                results.Add(new LocatedBarcode
                {
                    Image = ExtractRectified(gray, width, height, c, quietZone),
                    AngleDegrees = c.Theta * 180 / Math.PI,
                    Center = new PointF((float)c.CenterX, (float)c.CenterY),
                    BoxSize = new SizeF((float)c.Length, (float)c.Height),
                    Score = c.Score
                });
            }
            return results;
        }
    }
}
