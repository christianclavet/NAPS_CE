using System;
using System.Collections.Generic;

namespace NAPS2.Scan
{
    public static partial class BarcodeLocator
    {
        /// <summary>
        /// Composantes connexes (8 voisins) du masque fermé ; chacune est convertie en candidat
        /// (orientation par moments + gradient, boîte orientée) par <see cref="BuildCandidate"/>.
        /// </summary>
        private static List<Candidate> FindCandidates(byte[] closed, byte[] active, int[] jxx, int[] jyy, int[] jxy,
            int cols, int rows, double cellToSource)
        {
            var found = new List<Candidate>();
            int cellCount = cols * rows;
            int[] queue = new int[cellCount]; // file BFS et liste des cellules de la composante

            for (int seed = 0; seed < cellCount; seed++)
            {
                if (closed[seed] != 1)
                {
                    continue;
                }

                int head = 0, tail = 0;
                queue[tail++] = seed;
                closed[seed] = 2;
                var s = new ComponentStats();

                while (head < tail)
                {
                    int idx = queue[head++];
                    int row = idx / cols;
                    int col = idx - row * cols;
                    double cx = col + 0.5;
                    double cy = row + 0.5;
                    s.Sx += cx; s.Sy += cy;
                    s.Sxx += cx * cx; s.Syy += cy * cy; s.Sxy += cx * cy;
                    if (active[idx] == 1)
                    {
                        s.Txx += jxx[idx]; s.Tyy += jyy[idx]; s.Txy += jxy[idx];
                        s.ActiveCells++;
                    }

                    for (int dr = -1; dr <= 1; dr++)
                    {
                        int nr = row + dr;
                        if (nr < 0 || nr >= rows) continue;
                        for (int dc = -1; dc <= 1; dc++)
                        {
                            int nc = col + dc;
                            if (nc < 0 || nc >= cols) continue;
                            int n = nr * cols + nc;
                            if (closed[n] == 1)
                            {
                                closed[n] = 2;
                                queue[tail++] = n;
                            }
                        }
                    }
                }

                s.Count = tail;
                if (s.Count < MinComponentCells || s.ActiveCells == 0)
                {
                    continue;
                }

                Candidate candidate = BuildCandidate(s, queue, cols, cellToSource);
                if (candidate != null)
                {
                    found.Add(candidate);
                }
            }
            return found;
        }
    }
}
