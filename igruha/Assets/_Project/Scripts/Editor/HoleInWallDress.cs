using UnityEngine;

namespace Igruha.EditorTools
{
    /// <summary>Original bevelled modules fitted to the gameplay collision volumes.</summary>
    internal static class HoleInWallDress
    {
        internal enum Kind { None, PlatformDeck, Support, PoolRim, Ladder }
        internal static void Begin()
        {
            if (HoleInWallStudioAssets.Mesh("Panel") == null) HoleInWallStudioAssets.Import();
            HoleInWallPaletteAssets.Begin(1, "original studio materials");
        }
        internal static Material PaintOf(Kind kind) => HoleInWallStudioAssets.Mat(
            kind == Kind.PlatformDeck ? "Ivory" : kind == Kind.Ladder ? "Steel" : kind == Kind.PoolRim ? "Mint" : "Ivory");
        internal static GameObject Apply(GameObject box, Kind kind, System.Random rng)
        {
            if (kind == Kind.None) return null;
            string model = kind == Kind.Ladder ? "Ladder" : "Panel";
            Transform art = HoleInWallStudioAssets.Place(box.transform, model, Vector3.zero);
            Bounds bounds = HoleInWallStudioAssets.Mesh(model).bounds;
            Vector3 scale = new Vector3(1 / bounds.size.x, 1 / bounds.size.y, 1 / bounds.size.z);
            art.localScale = scale;
            art.localPosition = -Vector3.Scale(bounds.center, scale);
            if (kind != Kind.Ladder) art.GetComponent<Renderer>().sharedMaterial = PaintOf(kind);
            box.GetComponent<Renderer>().enabled = false;
            return art.gameObject;
        }
        internal static string MeasurementReport() => "Original HoleInWall studio modules: metre scale, preserved gameplay collision volumes.";
    }
}
