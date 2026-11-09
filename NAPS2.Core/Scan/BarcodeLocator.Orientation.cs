using System;

namespace NAPS2.Scan
{
    public static partial class BarcodeLocator
    {
        /// <summary>
        /// Moments géométriques (mu20, mu02, mu11) -> axe principal et élongation ; l'angle est raffiné
        /// par la direction dominante du gradient (perpendiculaire aux barres, plus précise) quand les deux
        /// concordent. Retourne la boîte orientée englobante, ou null si elle n'a pas la forme d'un code-barres.
        /// </summary>
        private static Candidate BuildCandidate(ComponentStats s, int[] cells, int cols, double cellToSource)
        {
            int count = s.Count;
            double mx = s.Sx / count;
            double my = s.Sy / count;
            // 1/12 = variance propre d'une cellule unité
            double mu20 = s.Sxx / count - mx * mx + 1.0 / 12;
            double mu02 = s.Syy / count - my * my + 1.0 / 12;
            double mu11 = s.Sxy / count - mx * my;

            double half = (mu20 - mu02) / 2;
            double root = Math.Sqrt(half * half + mu11 * mu11);
            double lambdaMax = (mu20 + mu02) / 2 + root;
            double lambdaMin = Math.Max(1e-9, (mu20 + mu02) / 2 - root);
            double elongation = Math.Sqrt(lambdaMax / lambdaMin);

            // Axe principal du bloc = axe long du code
            double theta = 0.5 * Math.Atan2(2 * mu11, mu20 - mu02);

            // Direction dominante du gradient = axe long du code (perpendiculaire aux barres)
            double tensorEnergy = s.Txx + s.Tyy;
            if (tensorEnergy > 0)
            {
                double tDiff = s.Txx - s.Tyy;
                double tensorCoherence = Math.Sqrt(tDiff * tDiff + 4 * s.Txy * s.Txy) / tensorEnergy;
                double phi = 0.5 * Math.Atan2(2 * s.Txy, tDiff);
                if (tensorCoherence >= 0.5)
                {
                    double disagreement = Math.Abs(NormalizeHalfTurn(phi - theta)) * 180 / Math.PI;
                    if (elongation < MinReliableElongation || disagreement <= MaxAngleDisagreementDeg)
                    {
                        theta = phi;
                    }
                }
            }
            theta = NormalizeHalfTurn(theta);

            double ux = Math.Cos(theta), uy = Math.Sin(theta);
            double vx = -uy, vy = ux;
            double halfCell = 0.5 * (Math.Abs(ux) + Math.Abs(uy));

            // Étendue de la boîte orientée (en unités de cellules)
            double minU = double.MaxValue, maxU = double.MinValue;
            double minV = double.MaxValue, maxV = double.MinValue;
            for (int q = 0; q < count; q++)
            {
                int idx = cells[q];
                int row = idx / cols;
                int col = idx - row * cols;
                double dx = col + 0.5 - mx;
                double dy = row + 0.5 - my;
                double pu = dx * ux + dy * uy;
                double pv = dx * vx + dy * vy;
                if (pu - halfCell < minU) minU = pu - halfCell;
                if (pu + halfCell > maxU) maxU = pu + halfCell;
                if (pv - halfCell < minV) minV = pv - halfCell;
                if (pv + halfCell > maxV) maxV = pv + halfCell;
            }

            double boxLength = (maxU - minU) * cellToSource;
            double boxHeight = (maxV - minV) * cellToSource;
            if (boxLength < MinBoxLength || boxHeight < MinBoxHeight || boxLength < MinAspect * boxHeight)
            {
                return null;
            }

            double centerU = (minU + maxU) / 2;
            double centerV = (minV + maxV) / 2;
            return new Candidate
            {
                CenterX = (mx + centerU * ux + centerV * vx) * cellToSource,
                CenterY = (my + centerU * uy + centerV * vy) * cellToSource,
                Theta = theta,
                Length = boxLength,
                Height = boxHeight,
                Score = s.ActiveCells * ((double)s.ActiveCells / count)
            };
        }
    }
}
