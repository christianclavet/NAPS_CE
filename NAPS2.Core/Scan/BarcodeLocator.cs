using System;
using System.Collections.Generic;
using System.Drawing;

namespace NAPS2.Scan
{
    /// <summary>
    /// Localise les codes-barres 1D (même inclinés) dans une page numérisée, sans bibliothèque de vision externe.
    /// Étapes : niveaux de gris -> sous-échantillonnage -> tenseur de gradient par cellule ->
    /// fermeture morphologique 1D -> composantes connexes + moments (mu20, mu02, mu11) ->
    /// extraction redressée par interpolation bilinéaire inverse avec zone calme.
    /// Thread-safe : seuls des tampons [ThreadStatic] sont réutilisés.
    /// </summary>
    public static partial class BarcodeLocator
    {
        // Dimension maximale visée (px) pour l'image d'analyse ; le facteur de réduction est entier
        private const int AnalysisMaxDimension = 1600;
        // Taille (px de l'image réduite) d'une cellule du tenseur de gradient
        private const int CellSize = 8;
        // Gradient minimal au carré pour un pixel (écarte le bruit du papier)
        private const int MinGradientSq = 400;
        // Énergie minimale (somme gx²+gy²) d'une cellule pour être « barres »
        private const long MinCellEnergy = 150000;
        // Cohérence d'orientation minimale d'une cellule (1 = une seule direction)
        private const double MinCellCoherence = 0.6;
        // Rayon de l'ouverture 3x3 (érosion puis dilatation) appliquée avant la fermeture : elle supprime les
        // structures de moins de 2r+1 cellules d'épaisseur (traits de formulaire, cadres) qui, une fois fermées,
        // colleraient le code-barres au reste de la page. Un code-barres est un bloc épais et survit.
        private const int OpenRadius = 1;
        // Rayons de la fermeture 1D, en cellules (fenêtre = 2r+1)
        private const int CloseRadiusAlong = 2;
        private const int CloseRadiusAcross = 1;
        // Nombre minimal de cellules d'une composante
        private const int MinComponentCells = 6;
        // Rapport minimal longueur/hauteur de la boîte orientée
        private const double MinAspect = 1.5;
        // Dimensions minimales de la boîte (px source)
        private const double MinBoxLength = 30;
        private const double MinBoxHeight = 8;
        // Élongation (sqrt(l1/l2)) sous laquelle l'angle des moments est jugé peu fiable
        private const double MinReliableElongation = 1.3;
        // Écart max (degrés) entre angle des moments et angle du gradient pour retenir celui du gradient
        private const double MaxAngleDisagreementDeg = 15;
        // Taille max (px) d'un côté de la sous-image produite
        private const int MaxOutputSide = 3000;

        [ThreadStatic] private static byte[] _grayBuffer;
        [ThreadStatic] private static byte[] _smallBuffer;

        /// <summary>
        /// Retourne les sous-images redressées (meilleur candidat en premier). L'appelant doit les libérer.
        /// </summary>
        /// <param name="source">Bitmap quelconque (converti à la volée par LockBits).</param>
        /// <param name="maxResults">Nombre maximal de candidats retournés.</param>
        /// <param name="quietZone">Marge ajoutée de chaque côté, en proportion de la boîte (0.15 = 15 %).</param>
        public static List<Bitmap> Locate(Bitmap source, int maxResults = 8, double quietZone = 0.15)
        {
            var images = new List<Bitmap>();
            foreach (LocatedBarcode located in LocateDetailed(source, maxResults, quietZone))
            {
                images.Add(located.Image);
                located.Image = null; // la propriété passe à l'appelant
            }
            return images;
        }
    }
}
