using System;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Igruha.Core.Audio;

namespace Igruha.EditorTools
{
    public static class HubSpeakerMusicBuilder
    {
        private static readonly Vector3 ConePosition = new(0, .58f, .21f);

        [MenuItem("Igruha/Хаб/Привязать музыку к колонкам телевизора")]
        public static void Apply()
        {
            var scene = EditorSceneManager.GetActiveScene();
            if (EditorApplication.isPlaying || scene.path != "Assets/_Project/Scenes/Hub.unity")
                throw new InvalidOperationException("Open Hub outside Play Mode.");
            Build(HubCompactPass.Require(HubCompactPass.RootName).transform.Find("Lounge"));
            EditorSceneManager.MarkSceneDirty(scene);
        }

        internal static void Build(Transform lounge)
        {
            if (lounge == null) throw new InvalidOperationException("Missing Hub lounge.");
            var speakers = lounge.Cast<Transform>().Where(t => t.name == "Speaker").OrderBy(t => t.position.x).ToArray();
            if (speakers.Length != 2) throw new InvalidOperationException("Hub needs two TV speakers.");
            var origins = new Transform[2];
            for (int i = 0; i < speakers.Length; i++)
            {
                origins[i] = speakers[i].Find("MusicOrigin");
                if (origins[i] == null) origins[i] = HubCompactPass.Group(speakers[i], "MusicOrigin");
                origins[i].localPosition = ConePosition;
            }
            var holder = lounge.Find("SpeakerMusic");
            if (holder == null) holder = HubCompactPass.Group(lounge, "SpeakerMusic");
            var emitter = holder.GetComponent<SceneMusicEmitter>();
            if (emitter == null) emitter = holder.gameObject.AddComponent<SceneMusicEmitter>();
            var serialized = new SerializedObject(emitter);
            serialized.FindProperty("leftSpeaker").objectReferenceValue = origins[0];
            serialized.FindProperty("rightSpeaker").objectReferenceValue = origins[1];
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(emitter);
        }
    }
}
