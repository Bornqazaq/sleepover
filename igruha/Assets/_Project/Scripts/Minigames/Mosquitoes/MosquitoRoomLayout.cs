using UnityEngine;

namespace Igruha.Minigames.Mosquitoes
{
    // Shared by the room builder and the temporary sleeping pose.
    public static class MosquitoRoomLayout
    {
        public const float MattressHeight = .665f;
        public const float HeadSkinX = -2.60f;
        public const float BedZ = -1.05f;
        public static readonly Vector3 BedCenter = new Vector3(-1.55f, .3325f, BedZ);
        public static readonly Vector3 BedSize = new Vector3(2.45f, .665f, 1.45f);
        public static readonly Vector3 BedPose = new Vector3(-2.20f, .68f, BedZ);
        public static readonly Vector3 BedExit = new Vector3(-1.70f, .05f, .08f);
    }
}
