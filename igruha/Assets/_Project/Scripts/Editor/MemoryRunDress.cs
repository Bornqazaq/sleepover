using UnityEngine;

namespace Igruha.EditorTools
{
    internal static class MemoryRunDress
    {
        internal enum Kind { None, Deck, ExitDoor }
        private static int plates;
        internal static void Begin() { plates = 0; }
        internal static GameObject Apply(GameObject box, Kind kind)
        {
            if (kind == Kind.None) return null;
            var root = new GameObject("Dress");
            root.transform.SetParent(box.transform, false);
            Vector3 scale = box.transform.lossyScale;
            root.transform.localScale = new Vector3(1 / scale.x, 1 / scale.y, 1 / scale.z);
            box.GetComponent<Renderer>().enabled = false;
            if (kind == Kind.Deck)
            {
                root.transform.position = box.transform.position + Vector3.up * scale.y * .5f;
                int nx = Mathf.CeilToInt(scale.x / 3), nz = Mathf.CeilToInt(scale.z / 3);
                float dx = scale.x / nx, dz = scale.z / nz;
                for (int x = 0; x < nx; x++)
                for (int z = 0; z < nz; z++)
                {
                    var tile = MemoryFoundryAssets.Place(root.transform, "Deck",
                        new Vector3(-scale.x / 2 + (x + .5f) * dx, 0, -scale.z / 2 + (z + .5f) * dz));
                    tile.localScale = new Vector3(dx / 3, 1, dz / 3);
                }
            }
            else
            {
                root.transform.position = box.transform.position - Vector3.up * scale.y * .5f;
                MemoryFoundryAssets.Place(root.transform, "ExitPortal", Vector3.zero);
            }
            return root;
        }
        internal static void ApplyPlate(GameObject box)
        {
            box.GetComponent<Renderer>().enabled = false;
            Vector3 size = box.transform.lossyScale;
            var dress = MemoryFoundryAssets.Place(box.transform, "Plate", Vector3.zero);
            dress.name = "Dress";
            dress.localScale = new Vector3(1 / size.x, 1 / size.y, 1 / size.z);
            dress.position = box.transform.position + Vector3.up * size.y * .5f;
            plates++;
        }
        internal static string Report() => "Original foundry: " + plates + " identical Blender plates; original decks and portal.";
    }
}
