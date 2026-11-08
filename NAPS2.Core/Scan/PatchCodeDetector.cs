using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Linq;
using System.Numerics;
using System.Runtime.InteropServices;
using ZXing;
using ZXing.Multi;

namespace NAPS2.Scan
{
    /// <summary>
    /// A wrapper around the ZXing library that detects patch codes.
    /// http://www.alliancegroup.co.uk/patch-codes.htm
    /// </summary>
    public class PatchCodeDetector
    {
        public static string DataBarcode { get; set; }

        /// <summary>
        /// Expression régulière appliquée au texte lu. Une lecture qui ne correspond pas est ignorée.
        /// Null (défaut) = aucun filtre. Exemple pour les codes SDOC : ^SDOC\d{8}$
        /// </summary>
        public static string RequiredFormatPattern { get; set; }

        public static string DetectBarcode(Bitmap bitmap)
        {
            if (bitmap == null)
            {
                DataBarcode = "";
                return DataBarcode;
            }

            Bitmap clone = null;

            // Convertir en 32 bits ARGB si nécessaire
            if (bitmap.PixelFormat != PixelFormat.Format32bppArgb)
            {
                // Cloner l'image dans le format requis
                clone = bitmap.Clone(new Rectangle(0, 0, bitmap.Width, bitmap.Height), PixelFormat.Format32bppArgb);
                bitmap = clone;
            }

            var reader  = new BarcodeReader
            {
                    AutoRotate = true,
                    // Lit aussi les codes inversés (clair sur sombre). Tous les formats restent actifs.
                    Options = new ZXing.Common.DecodingOptions { TryHarder = true, TryInverted = true }
            };

            Result[] barcodeResult;
            try
            {
                // Localiser la zone du code-barres et décoder seulement cette zone (pas de décodage de la page entière)
                barcodeResult = DecodeLocatedRegions(bitmap, reader);
            }
            finally
            {
                // Libère le clone créé localement (le bitmap d'origine appartient à l'appelant)
                if (clone != null)
                {
                    clone.Dispose();
                }
            }
            // Filtre de format : on ne garde que les lectures qui correspondent au motif demandé
            if (barcodeResult != null && !string.IsNullOrEmpty(RequiredFormatPattern))
            {
                barcodeResult = barcodeResult
                    .Where(r => System.Text.RegularExpressions.Regex.IsMatch(r.Text ?? "", RequiredFormatPattern))
                    .ToArray();
            }

            // Valeur locale : DataBarcode est statique et partagé entre les threads du traitement parallèle
            string data = "";
            if (barcodeResult != null && barcodeResult.Length > 0) //Something is detected
            {
                data = barcodeResult[0].Text;
                for (int i = 0; i < barcodeResult.Length; i++) 
                {

                    if (i > 0)
                    {
                        data += "^" + barcodeResult[i].Text;
                    }
                    Debug.WriteLine("Barcode " + i + ": " + barcodeResult[i].Text);
                }
                //barcodeResult[0].ResultPoints[0].ToString();
            }
            else
                DataBarcode = "";

            DataBarcode = data;
            return data;
        }


        // Taille (px) d'une cellule d'analyse utilisée pour localiser le code-barres
        private const int RegionCellSize = 16;
        // Différence de luminance (0-255) à partir de laquelle un pixel est considéré comme un bord
        private const int RegionEdgeThreshold = 48;
        // Nombre de zones candidates décodées avant le repli sur l'image complète
        private const int MaxRegionCandidates = 8;
        // Écart horizontal maximal (en cellules) entre deux zones pour les fusionner
        private const int MaxMergeGapCells = 24;

        private class BarcodeCandidate
        {
            // Proportion de cellules actives dans la zone (un code-barres est dense et régulier)
            public double Density;
            public Rectangle Rect;
        }

        // Énergie minimale des gradients d'une cellule pour qu'elle soit considérée comme un bord de barre
        private const int MinCellEnergy = 1000000;
        // Cohérence minimale des gradients d'une zone (1 = une seule direction, 0 = aucune direction)
        private const double MinOrientationCoherence = 0.7;
        // Nombre minimal de cellules pour qu'une zone serve à estimer l'inclinaison
        private const int MinTiltCells = 8;
        // Croissance de zone : énergie minimale d'une cellule voisine et écart d'orientation toléré (degrés)
        private const int MinGrowEnergy = 250000;
        private const double GrowAngleTolerance = 12.0;
        // Une zone agrandie ne peut pas dépasser ce facteur par rapport à la zone de départ
        private const int MaxGrowFactor = 4;
        // Inclinaison minimale (degrés) pour redresser l'image ; en dessous, la localisation à 0° suffit
        private const double MinTiltDegrees = 2.0;
        // Nombre maximal d'inclinaisons testées (chacune coûte une rotation de l'image)
        private const int MaxRotatedAttempts = 3;
        // Écarts d'angle (degrés) utilisés pour confirmer une lecture : le code doit se relire près de son angle
        private static readonly int[] ConfirmationAngleOffsets = { -1, 1, -3, 3 };
        // Nombre minimal d'écarts d'angle où la lecture doit se retrouver
        private const int MinConfirmations = 2;

        private static readonly int[] NeighborRowOffsets = { 0, 0, -1, 1 };
        private static readonly int[] NeighborColOffsets = { -1, 1, 0, 0 };

        /// <summary>
        /// Décode les zones candidates (les plus probables en premier) et retourne le premier résultat trouvé.
        /// </summary>
        private static Result[] DecodeLocatedRegions(Bitmap bitmap, BarcodeReader reader)
        {
            Rectangle foundRect;
            Result[] results = DecodeRegionsOf(bitmap, reader, out foundRect);
            if (results != null)
            {
                Result[] confirmed = KeepConfirmedReadings(bitmap, foundRect, results, reader);
                if (confirmed != null)
                {
                    return confirmed;
                }
                Debug.WriteLine("Lecture rejetée (non confirmée) à 0 degré : " + results[0].Text);
            }

            // Codes inclinés : on ne redresse que la zone candidate (et non la page entière).
            // La zone est découpée dans l'image d'origine, puis tournée de son inclinaison et confirmée.
            foreach (TiltedRegion region in FindBarTilts(bitmap).Take(MaxRotatedAttempts))
            {
                using (Bitmap crop = bitmap.Clone(region.Rect, PixelFormat.Format32bppArgb))
                {
                    // Orientation donnée par la forme : plus haute que large = barres horizontales = base de 90 degrés.
                    // La seule rotation fine est le résidu de l'inclinaison (entre -45 et 45), ce qui limite l'aliasing.
                    float baseTurn = region.Rect.Height > region.Rect.Width ? 90f : 0f;
                    float fineTurn = (float)ResidualTilt(region.Tilt);
                    using (Bitmap oriented = RotateWithWhiteBackground(crop, baseTurn - fineTurn))
                    {
                        Result[] zoneResults = reader.DecodeMultiple(oriented);
                        if (zoneResults != null && zoneResults.Length > 0)
                        {
                            Rectangle whole = new Rectangle(0, 0, oriented.Width, oriented.Height);
                            Result[] confirmed = KeepConfirmedReadings(oriented, whole, zoneResults, reader);
                            if (confirmed != null)
                            {
                                Debug.WriteLine("Code confirmé dans la zone (inclinaison " + region.Tilt.ToString("F1") + ", base " + baseTurn + ") : " + confirmed[0].Text);
                                return confirmed;
                            }
                        }
                    }
                }
            }
            return null;
        }

        /// <summary>
        /// Localise les zones de code-barres dans l'image et les décode. Retourne null si aucun code n'est lu.
        /// </summary>
        private static Result[] DecodeRegionsOf(Bitmap bitmap, BarcodeReader reader, out Rectangle foundRect)
        {
            foreach (BarcodeCandidate candidate in FindBarcodeRegions(bitmap).Take(MaxRegionCandidates))
            {
                using (Bitmap crop = bitmap.Clone(candidate.Rect, PixelFormat.Format32bppArgb))
                {
                    Result[] results = reader.DecodeMultiple(crop);
                    if (results != null && results.Length > 0)
                    {
                        Debug.WriteLine("Code lu dans la zone " + candidate.Rect + " : " + results[0].Text);
                        foundRect = candidate.Rect;
                        return results;
                    }
                    Debug.WriteLine("Aucun code lu dans la zone " + candidate.Rect);
                }
            }
            foundRect = Rectangle.Empty;
            return null;
        }

        /// <summary>
        /// Confirme une lecture : la zone doit donner le même texte après une petite rotation,
        /// dans au moins un des deux sens. Un vrai code-barres se lit de façon stable, un faux positif sur du texte non.
        /// </summary>
        private static Result[] KeepConfirmedReadings(Bitmap source, Rectangle rect, Result[] readings, BarcodeReader reader)
        {
            // Un résultat est conservé s'il est relu à au moins MinConfirmations écarts d'angle.
            // Un vrai code se relit près de son angle ; un faux positif sur du texte se relit rarement.
            var hits = new Dictionary<string, int>();
            foreach (Result r in readings)
            {
                hits[r.Text] = 0;
            }
            using (Bitmap crop = source.Clone(rect, PixelFormat.Format32bppArgb))
            {
                int offsetsDone = 0;
                foreach (int offset in ConfirmationAngleOffsets)
                {
                    // Arrêt anticipé : tous les résultats ont déjà atteint le seuil
                    if (hits.Values.All(h => h >= MinConfirmations))
                    {
                        break;
                    }
                    // Arrêt anticipé : même si tous les écarts restants réussissaient, le seuil ne serait plus atteint
                    if (hits.Values.All(h => h + (ConfirmationAngleOffsets.Length - offsetsDone) < MinConfirmations))
                    {
                        break;
                    }
                    using (Bitmap rotatedCrop = RotateWithWhiteBackground(crop, offset))
                    {
                        Result[] results = reader.DecodeMultiple(rotatedCrop);
                        var seenAtOffset = new HashSet<string>(results == null ? new string[0] : results.Select(r => r.Text));
                        foreach (string text in hits.Keys.ToList())
                        {
                            if (seenAtOffset.Contains(text))
                            {
                                hits[text]++;
                            }
                        }
                    }
                    offsetsDone++;
                }
            }
            Result[] confirmed = readings.Where(r => hits[r.Text] >= MinConfirmations).ToArray();
            return confirmed.Length > 0 ? confirmed : null;
        }

        /// <summary>
        /// Estime l'inclinaison (en degrés) des barres des zones où les gradients suivent une seule direction.
        /// Utilise un tenseur de structure par cellule : un code-barres incliné a des gradients orientés
        /// perpendiculairement à ses barres, alors que le texte a des gradients dans plusieurs directions.
        /// Retourne les inclinaisons, la zone la plus grande en premier.
        /// </summary>
        private static List<TiltedRegion> FindBarTilts(Bitmap bitmap)
        {
            int width = bitmap.Width;
            int height = bitmap.Height;
            byte[] gray = ToGrayscale(bitmap);

            int cols = (width + RegionCellSize - 1) / RegionCellSize;
            int rows = (height + RegionCellSize - 1) / RegionCellSize;
            int cellCount = cols * rows;
            double[] jxx = new double[cellCount];
            double[] jyy = new double[cellCount];
            double[] jxy = new double[cellCount];

            for (int y = 1; y < height - 1; y++)
            {
                for (int x = 1; x < width - 1; x++)
                {
                    int p = y * width + x;
                    double gx = gray[p + 1] - gray[p - 1];
                    double gy = gray[p + width] - gray[p - width];
                    int cell = (y / RegionCellSize) * cols + x / RegionCellSize;
                    jxx[cell] += gx * gx;
                    jyy[cell] += gy * gy;
                    jxy[cell] += gx * gy;
                }
            }

            bool[] active = new bool[cellCount];
            for (int i = 0; i < cellCount; i++)
            {
                active[i] = jxx[i] + jyy[i] >= MinCellEnergy;
            }

            var found = new List<TiltedRegion>();
            bool[] visited = new bool[cellCount];
            var queue = new Queue<int>();
            for (int start = 0; start < cellCount; start++)
            {
                if (!active[start] || visited[start])
                {
                    continue;
                }

                visited[start] = true;
                queue.Enqueue(start);
                int count = 0;
                var component = new List<int>();
                int minCol = cols, maxCol = -1, minRow = rows, maxRow = -1;
                double sxx = 0, syy = 0, sxy = 0;
                while (queue.Count > 0)
                {
                    int index = queue.Dequeue();
                    int row = index / cols;
                    int col = index % cols;
                    count++;
                    minCol = Math.Min(minCol, col);
                    maxCol = Math.Max(maxCol, col);
                    minRow = Math.Min(minRow, row);
                    maxRow = Math.Max(maxRow, row);
                    sxx += jxx[index];
                    syy += jyy[index];
                    sxy += jxy[index];

                    for (int n = 0; n < 4; n++)
                    {
                        int nRow = row + NeighborRowOffsets[n];
                        int nCol = col + NeighborColOffsets[n];
                        if (nRow < 0 || nRow >= rows || nCol < 0 || nCol >= cols)
                        {
                            continue;
                        }
                        int neighbor = nRow * cols + nCol;
                        if (active[neighbor] && !visited[neighbor])
                        {
                            visited[neighbor] = true;
                            queue.Enqueue(neighbor);
                        }
                    }
                }

                if (count < MinTiltCells)
                {
                    continue;
                }
                double trace = sxx + syy;
                if (trace <= 0)
                {
                    continue;
                }
                double coherence = Math.Sqrt((sxx - syy) * (sxx - syy) + 4 * sxy * sxy) / trace;
                if (coherence < MinOrientationCoherence)
                {
                    continue;
                }
                // Direction dominante des gradients = direction perpendiculaire aux barres
                // Angle brut dans (-90, 90] : proche de 0 pour des barres verticales, proche de 90 pour des barres horizontales
                double tilt = 0.5 * Math.Atan2(2 * sxy, sxx - syy) * 180.0 / Math.PI;
                if (Math.Abs(tilt) >= MinTiltDegrees)
                {
                    found.Add(new TiltedRegion
                    {
                        Cells = count,
                        Tilt = tilt,
                        Rect = CellsToRectangle(minCol, maxCol, minRow, maxRow, width, height)
                    });
                }
            }

            var result = new List<TiltedRegion>();
            foreach (var candidate in found.OrderByDescending(p => p.Cells))
            {
                // Ignore les zones dont l'inclinaison est déjà retenue (écart de moins de 2 degrés)
                if (result.All(r => AngleDistance(r.Tilt, candidate.Tilt) >= 2.0))
                {
                    result.Add(candidate);
                }
            }
            return result;
        }

        /// <summary>
        /// Agrandit une zone inclinée : ajoute les cellules voisines (8 directions) dont l'énergie et
        /// l'orientation des bords correspondent à l'inclinaison trouvée. Le code entier est ainsi couvert,
        /// y compris les barres plus faibles aux extrémités. La croissance est bornée autour de la zone de départ.
        /// </summary>
        private static Rectangle GrowTiltedRegion(List<int> seed, double seedTilt, double[] jxx, double[] jyy, double[] jxy,
            int cols, int rows, int width, int height)
        {
            int seedMinCol = cols, seedMaxCol = -1, seedMinRow = rows, seedMaxRow = -1;
            foreach (int index in seed)
            {
                int row = index / cols;
                int col = index % cols;
                seedMinCol = Math.Min(seedMinCol, col);
                seedMaxCol = Math.Max(seedMaxCol, col);
                seedMinRow = Math.Min(seedMinRow, row);
                seedMaxRow = Math.Max(seedMaxRow, row);
            }

            // Limites de croissance : la moitié de la largeur/hauteur de la zone de départ, au minimum quelques cellules
            int capX = Math.Max(8, (seedMaxCol - seedMinCol + 1) / 2);
            int capY = Math.Max(4, (seedMaxRow - seedMinRow + 1) / 2);

            var inRegion = new bool[cols * rows];
            var queue = new Queue<int>();
            int minCol = seedMinCol, maxCol = seedMaxCol, minRow = seedMinRow, maxRow = seedMaxRow;
            foreach (int index in seed)
            {
                inRegion[index] = true;
                queue.Enqueue(index);
            }

            while (queue.Count > 0)
            {
                int index = queue.Dequeue();
                int row = index / cols;
                int col = index % cols;
                for (int dr = -1; dr <= 1; dr++)
                {
                    for (int dc = -1; dc <= 1; dc++)
                    {
                        int nRow = row + dr;
                        int nCol = col + dc;
                        if ((dr == 0 && dc == 0) || nRow < 0 || nRow >= rows || nCol < 0 || nCol >= cols)
                        {
                            continue;
                        }
                        if (nRow < seedMinRow - capY || nRow > seedMaxRow + capY || nCol < seedMinCol - capX || nCol > seedMaxCol + capX)
                        {
                            continue;
                        }
                        int n = nRow * cols + nCol;
                        if (inRegion[n] || jxx[n] + jyy[n] < MinGrowEnergy)
                        {
                            continue;
                        }
                        double cellTilt = 0.5 * Math.Atan2(2 * jxy[n], jxx[n] - jyy[n]) * 180.0 / Math.PI;
                        if (AngleDistance(cellTilt, seedTilt) > GrowAngleTolerance)
                        {
                            continue;
                        }
                        inRegion[n] = true;
                        queue.Enqueue(n);
                        minCol = Math.Min(minCol, nCol);
                        maxCol = Math.Max(maxCol, nCol);
                        minRow = Math.Min(minRow, nRow);
                        maxRow = Math.Max(maxRow, nRow);
                    }
                }
            }
            // Zone trop grande = probablement du texte ou une ligne : on garde la zone de départ
            int grownCells = (maxCol - minCol + 1) * (maxRow - minRow + 1);
            int seedCells = (seedMaxCol - seedMinCol + 1) * (seedMaxRow - seedMinRow + 1);
            if (grownCells > seedCells * MaxGrowFactor)
            {
                minCol = seedMinCol; maxCol = seedMaxCol; minRow = seedMinRow; maxRow = seedMaxRow;
            }
            return CellsToRectangle(minCol, maxCol, minRow, maxRow, width, height);
        }

        /// <summary>
        /// Zone inclinée trouvée par FindBarTilts : son inclinaison et son emplacement dans l'image d'origine.
        /// </summary>
        private class TiltedRegion
        {
            public int Cells;
            public double Tilt;
            public Rectangle Rect;
        }

        private static Rectangle CellsToRectangle(int minCol, int maxCol, int minRow, int maxRow, int width, int height)
        {
            // Marge proportionnelle à la taille de la zone : une zone inclinée perd ses extrémités si la marge est trop petite
            int regionWidth = (maxCol - minCol + 1) * RegionCellSize;
            int regionHeight = (maxRow - minRow + 1) * RegionCellSize;
            int padX = Math.Max(RegionCellSize * 3, (int)(regionWidth * 0.3));
            int padY = Math.Max(RegionCellSize * 2, (int)(regionHeight * 0.3));
            int left = Math.Max(0, minCol * RegionCellSize - padX);
            int top = Math.Max(0, minRow * RegionCellSize - padY);
            int right = Math.Min(width, (maxCol + 1) * RegionCellSize + padX);
            int bottom = Math.Min(height, (maxRow + 1) * RegionCellSize + padY);
            return Rectangle.FromLTRB(left, top, right, bottom);
        }

        /// <summary>
        /// Ramène un angle de barres dans [-45, 45] degrés : l'écart à la verticale (0) ou à l'horizontale (90).
        /// </summary>
        private static double ResidualTilt(double degrees)
        {
            return degrees - 90.0 * Math.Round(degrees / 90.0);
        }

        /// <summary>
        /// Écart entre deux angles de barres. Un angle et son opposé modulo 180 désignent les mêmes barres.
        /// </summary>
        private static double AngleDistance(double a, double b)
        {
            double d = Math.Abs(a - b) % 180.0;
            return Math.Min(d, 180.0 - d);
        }

        /// <summary>
        /// Fait pivoter l'image (fond blanc, format Format32bppArgb). L'image agrandie conserve le contenu entier.
        /// </summary>
        private static Bitmap RotateWithWhiteBackground(Bitmap source, float angle)
        {
            double radians = angle * Math.PI / 180.0;
            double cos = Math.Abs(Math.Cos(radians));
            double sin = Math.Abs(Math.Sin(radians));
            int newWidth = (int)Math.Ceiling(source.Width * cos + source.Height * sin);
            int newHeight = (int)Math.Ceiling(source.Width * sin + source.Height * cos);

            var result = new Bitmap(newWidth, newHeight, PixelFormat.Format32bppArgb);
            using (Graphics g = Graphics.FromImage(result))
            {
                g.Clear(Color.White);
                // Interpolation de haute qualité : limite l'aliasing sur les barres fines
                g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                g.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.HighQuality;
                g.TranslateTransform(newWidth / 2f, newHeight / 2f);
                g.RotateTransform(angle);
                g.TranslateTransform(-source.Width / 2f, -source.Height / 2f);
                g.DrawImage(source, 0, 0, source.Width, source.Height);
            }
            return result;
        }

        /// <summary>
        /// Trouve les zones qui ressemblent à un code-barres 1D : beaucoup de transitions verticales
        /// (barres) et peu de transitions horizontales. Retourne les zones, la plus probable en premier.
        /// Le bitmap doit être au format Format32bppArgb.
        /// </summary>
        private static List<BarcodeCandidate> FindBarcodeRegions(Bitmap bitmap)
        {
            int width = bitmap.Width;
            int height = bitmap.Height;
            byte[] gray = ToGrayscale(bitmap);

            int cols = (width + RegionCellSize - 1) / RegionCellSize;
            int rows = (height + RegionCellSize - 1) / RegionCellSize;
            int cellCount = cols * rows;
            int[] verticalEdges = new int[cellCount];   // bords verticaux (barres)
            int[] horizontalEdges = new int[cellCount]; // bords horizontaux (texte, lignes)

            for (int y = 0; y < height; y++)
            {
                int rowStart = y * width;
                int cellRowStart = (y / RegionCellSize) * cols;
                for (int x = 0; x < width; x++)
                {
                    int cell = cellRowStart + x / RegionCellSize;
                    int value = gray[rowStart + x];
                    if (x + 1 < width && Math.Abs(value - gray[rowStart + x + 1]) > RegionEdgeThreshold)
                    {
                        verticalEdges[cell]++;
                    }
                    if (y + 1 < height && Math.Abs(value - gray[rowStart + width + x]) > RegionEdgeThreshold)
                    {
                        horizontalEdges[cell]++;
                    }
                }
            }

            // Une cellule est active si elle contient assez de bords verticaux et peu de bords horizontaux
            int minVerticalEdges = RegionCellSize * RegionCellSize / 32;
            bool[] active = new bool[cellCount];
            for (int i = 0; i < cellCount; i++)
            {
                // Les bords haut/bas des barres ajoutent des bords horizontaux : on tolère davantage de bruit
                active[i] = verticalEdges[i] >= minVerticalEdges && horizontalEdges[i] <= verticalEdges[i] * 2;
            }

            var candidates = new List<BarcodeCandidate>();
            bool[] visited = new bool[cellCount];
            var queue = new Queue<int>();
            for (int start = 0; start < cellCount; start++)
            {
                if (!active[start] || visited[start])
                {
                    continue;
                }

                // Regroupe les cellules actives connexes (parcours en largeur)
                visited[start] = true;
                queue.Enqueue(start);
                int minCol = cols, maxCol = -1, minRow = rows, maxRow = -1, score = 0, count = 0;
                while (queue.Count > 0)
                {
                    int index = queue.Dequeue();
                    int row = index / cols;
                    int col = index % cols;
                    score += verticalEdges[index];
                    count++;
                    minCol = Math.Min(minCol, col);
                    maxCol = Math.Max(maxCol, col);
                    minRow = Math.Min(minRow, row);
                    maxRow = Math.Max(maxRow, row);

                    for (int n = 0; n < 4; n++)
                    {
                        int nRow = row + NeighborRowOffsets[n];
                        int nCol = col + NeighborColOffsets[n];
                        if (nRow < 0 || nRow >= rows || nCol < 0 || nCol >= cols)
                        {
                            continue;
                        }
                        int neighbor = nRow * cols + nCol;
                        if (active[neighbor] && !visited[neighbor])
                        {
                            visited[neighbor] = true;
                            queue.Enqueue(neighbor);
                        }
                    }
                }

                // On ne garde que les zones nettement plus larges que hautes
                int regionWidth = (maxCol - minCol + 1) * RegionCellSize;
                int regionHeight = (maxRow - minRow + 1) * RegionCellSize;
                if (count < 4 || regionWidth < 2 * regionHeight)
                {
                    continue;
                }

                // Marge autour du code (zone calme) pour aider ZXing
                int padX = RegionCellSize * 2;
                int padY = RegionCellSize;
                int left = Math.Max(0, minCol * RegionCellSize - padX);
                int top = Math.Max(0, minRow * RegionCellSize - padY);
                int right = Math.Min(width, (maxCol + 1) * RegionCellSize + padX);
                int bottom = Math.Min(height, (maxRow + 1) * RegionCellSize + padY);

                candidates.Add(new BarcodeCandidate
                {
                    Density = (double)count / ((maxCol - minCol + 1) * (maxRow - minRow + 1)),
                    Rect = Rectangle.FromLTRB(left, top, right, bottom)
                });
            }

            candidates = MergeNearbyCandidates(candidates);
            foreach (BarcodeCandidate candidate in candidates)
            {
                Debug.WriteLine("Zone candidate: " + candidate.Rect + " densité=" + candidate.Density.ToString("F2"));
            }
            Debug.WriteLine("Zones candidates trouvées : " + candidates.Count + " (image " + width + "x" + height + ")");
            return candidates.OrderByDescending(c => c.Density).ToList();
        }

        /// <summary>
        /// Fusionne les zones qui se chevauchent verticalement et sont proches horizontalement.
        /// Un même code-barres peut être découpé en plusieurs segments par les bords internes.
        /// </summary>
        private static List<BarcodeCandidate> MergeNearbyCandidates(List<BarcodeCandidate> input)
        {
            var merged = new List<BarcodeCandidate>(input);
            bool changed = true;
            while (changed)
            {
                changed = false;
                for (int i = 0; i < merged.Count && !changed; i++)
                {
                    for (int j = i + 1; j < merged.Count && !changed; j++)
                    {
                        Rectangle a = merged[i].Rect;
                        Rectangle b = merged[j].Rect;
                        int verticalOverlap = Math.Min(a.Bottom, b.Bottom) - Math.Max(a.Top, b.Top);
                        int horizontalGap = Math.Max(a.Left, b.Left) - Math.Min(a.Right, b.Right);
                        if (verticalOverlap > Math.Min(a.Height, b.Height) / 2 && horizontalGap <= MaxMergeGapCells * RegionCellSize)
                        {
                            merged[i] = new BarcodeCandidate
                            {
                                Density = Math.Max(merged[i].Density, merged[j].Density),
                                Rect = Rectangle.Union(a, b)
                            };
                            merged.RemoveAt(j);
                            changed = true;
                        }
                    }
                }
            }
            return merged;
        }

        /// <summary>
        /// Convertit un bitmap Format32bppArgb en tableau de luminance (0-255).
        /// </summary>
        private static byte[] ToGrayscale(Bitmap bitmap)
        {
            int width = bitmap.Width;
            int height = bitmap.Height;
            byte[] gray = new byte[width * height];

            BitmapData data = bitmap.LockBits(new Rectangle(0, 0, width, height), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            try
            {
                byte[] pixels = new byte[data.Stride * height];
                Marshal.Copy(data.Scan0, pixels, 0, pixels.Length);
                for (int y = 0; y < height; y++)
                {
                    int src = y * data.Stride;
                    int dst = y * width;
                    for (int x = 0; x < width; x++, src += 4, dst++)
                    {
                        // Format BGRA : luminance approximative (R*0.299 + G*0.587 + B*0.114)
                        gray[dst] = (byte)((pixels[src + 2] * 77 + pixels[src + 1] * 150 + pixels[src] * 29) >> 8);
                    }
                }
            }
            finally
            {
                bitmap.UnlockBits(data);
            }
            return gray;
        }

        public static PatchCode Detect(Bitmap bitmap)
        {
            if (bitmap == null)
            {
                DataBarcode = "";
                return PatchCode.None;
            }

            IBarcodeReader reader = new BarcodeReader();
            var barcodeResult = reader.Decode(bitmap);
            if (barcodeResult != null) //Something is detected
            {
                DataBarcode = barcodeResult.Text;
                switch (barcodeResult.Text)
                {
                    case "PATCH1":
                        return PatchCode.Patch1;
                    case "PATCH2":
                        return PatchCode.Patch2;
                    case "PATCH3":
                        return PatchCode.Patch3;
                    case "PATCH4":
                        return PatchCode.Patch4;
                    case "PATCH6":
                        return PatchCode.Patch6;
                    case "PATCHT":
                        return PatchCode.PatchT;
                    default:
                        return PatchCode.Unknown;
                }

            }
            else
                DataBarcode = "";

            return PatchCode.None;
        }

        void DetectBarcodeDeep(Bitmap bitmap)
        {
        

        }
    }
}