using System;

namespace NAPS2.Scan
{
    public static partial class BarcodeLocator
    {
        private sealed class Candidate
        {
            public double CenterX, CenterY, Theta, Length, Height, Score;
        }

        /// <summary>Sommes accumulées sur une composante connexe (unités : cellules).</summary>
        private sealed class ComponentStats
        {
            public int Count, ActiveCells;
            public double Sx, Sy, Sxx, Syy, Sxy;
            public double Txx, Tyy, Txy;
        }

        private static byte[] BuildActiveMask(int[] jxx, int[] jyy, int[] jxy, int cellCount, out int activeTotal)
        {
            byte[] active = new byte[cellCount];
            activeTotal = 0;
            for (int i = 0; i < cellCount; i++)
            {
                long energy = (long)jxx[i] + jyy[i];
                if (energy < MinCellEnergy)
                {
                    continue;
                }
                double diff = (double)jxx[i] - jyy[i];
                double coherence = Math.Sqrt(diff * diff + 4.0 * jxy[i] * (double)jxy[i]) / energy;
                if (coherence >= MinCellCoherence)
                {
                    active[i] = 1;
                    activeTotal++;
                }
            }
            return active;
        }

        /// <summary>Ramène un angle dans ]-pi/2, pi/2] (un axe n'a pas de sens).</summary>
        private static double NormalizeHalfTurn(double angle)
        {
            while (angle > Math.PI / 2) angle -= Math.PI;
            while (angle <= -Math.PI / 2) angle += Math.PI;
            return angle;
        }
    }
}
