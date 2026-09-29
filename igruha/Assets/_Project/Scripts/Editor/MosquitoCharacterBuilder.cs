using UnityEditor;
using UnityEngine;
using Igruha.Core.Player;

namespace Igruha.EditorTools
{
    // Visual-only copies: never instantiate a player motor or a nested NetworkObject.
    public static class MosquitoCharacterBuilder
    {
        public const string Folder = "Assets/_Project/Prefabs/Minigames/Mosquitoes/Characters";
        public static GameObject[] Build()
        {
            MosquitoesCoreSetup.EnsureFolder(Folder);
            var roster = AssetDatabase.LoadAssetAtPath<CharacterRoster>("Assets/_Project/Settings/Gameplay/CharacterRoster.asset");
            var wingModel = AssetDatabase.LoadAssetAtPath<GameObject>(MosquitoesArenaBuilder.Art + "/Models/Wings.fbx");
            if (wingModel == null) throw new System.InvalidOperationException("Export Wings.fbx first.");
            var result = new GameObject[roster.Characters.Count];
            for (int i = 0; i < result.Length; i++)
            {
                var character = roster.Characters[i];
                var source = character.Prefab.GetComponentInChildren<Animator>(true);
                var root = new GameObject(character.DisplayName + "_Mosquito");
                var model = Object.Instantiate(source.gameObject, root.transform, false);
                model.name = character.DisplayName;
                var animator = model.GetComponent<Animator>();
                animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                animator.applyRootMotion = false;
                animator.Rebind(); animator.Update(0);
                // Pose only this copy. The shared controllers/clips remain untouched.
                animator.enabled = false;
                PoseArm(animator, HumanBodyBones.LeftUpperArm, HumanBodyBones.LeftLowerArm, HumanBodyBones.LeftHand);
                PoseArm(animator, HumanBodyBones.RightUpperArm, HumanBodyBones.RightLowerArm, HumanBodyBones.RightHand);
                Bend(animator, HumanBodyBones.LeftLowerLeg, 30);
                Bend(animator, HumanBodyBones.RightLowerLeg, 42);
                var skins = model.GetComponentsInChildren<SkinnedMeshRenderer>(true);
                var bounds = new Bounds(); bool first = true;
                foreach (var skin in skins)
                {
                    skin.updateWhenOffscreen = true;
                    if (first) { bounds = skin.bounds; first = false; }
                    else bounds.Encapsulate(skin.bounds);
                }
                float scale = .22f / Mathf.Max(.01f, bounds.size.y);
                model.transform.localPosition -= bounds.center;
                root.transform.localScale = Vector3.one * scale;
                var chest = animator.GetBoneTransform(HumanBodyBones.Chest);
                if (chest == null) chest = animator.GetBoneTransform(HumanBodyBones.Spine);
                var mount = new GameObject("WingMount").transform;
                mount.SetParent(chest != null ? chest : model.transform, false);
                // Keep wings in character-facing axes, independently of imported bone axes.
                mount.rotation = root.transform.rotation;
                mount.position = (chest != null ? chest.position : root.transform.position) - root.transform.forward * .035f;
                mount.localScale = Vector3.one;
                var wings = Object.Instantiate(wingModel, mount, false);
                wings.name = "MosquitoWings";
                wings.transform.localScale = new Vector3(1f / mount.lossyScale.x, 1f / mount.lossyScale.y, 1f / mount.lossyScale.z);
                MosquitoesArenaBuilder.RemapMaterials(wings);
                foreach (Transform t in root.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = 12;
                result[i] = PrefabUtility.SaveAsPrefabAsset(root, Folder + "/" + character.DisplayName + ".prefab");
                Object.DestroyImmediate(root);
            }
            return result;
        }
        private static void PoseArm(Animator animator, HumanBodyBones upper, HumanBodyBones lower, HumanBodyBones hand)
        {
            var a = animator.GetBoneTransform(upper); var b = animator.GetBoneTransform(lower); var c = animator.GetBoneTransform(hand);
            if (a == null || b == null || c == null) return;
            float side = Mathf.Sign(a.position.x - animator.transform.position.x);
            a.rotation = Quaternion.FromToRotation(b.position - a.position, new Vector3(side * .38f, -.85f, .10f)) * a.rotation;
            b.rotation = Quaternion.FromToRotation(c.position - b.position, new Vector3(side * .15f, -.85f, .35f)) * b.rotation;
        }
        private static void Bend(Animator animator, HumanBodyBones bone, float angle)
        {
            var t = animator.GetBoneTransform(bone);
            if (t != null) t.localRotation *= Quaternion.Euler(angle, 0, 0);
        }
    }
}
