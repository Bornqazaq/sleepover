using UnityEditor;
using UnityEditor.SceneManagement;

namespace Igruha.EditorTools
{
    public static class MosquitoesPlayMenu
    {
        [MenuItem("Igruha/Minigames/Комары/Начать за человека")]
        public static void Human() => Start(0);

        [MenuItem("Igruha/Minigames/Комары/Начать за комара")]
        public static void Mosquito() => Start(1);

        [MenuItem("Igruha/Minigames/Комары/Начать за человека", true)]
        [MenuItem("Igruha/Minigames/Комары/Начать за комара", true)]
        private static bool CanStart() => !EditorApplication.isPlayingOrWillChangePlaymode;

        private static void Start(int role)
        {
            if (!CanStart()) return;
            if (EditorSceneManager.GetActiveScene().path != MosquitoesArenaBuilder.ScenePath)
            {
                if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
                EditorSceneManager.OpenScene(MosquitoesArenaBuilder.ScenePath);
            }
            // Survives the Play domain reload and is consumed once by the local round.
            SessionState.SetInt("Mosquitoes.StartRole", role);
            EditorApplication.isPlaying = true;
        }
    }
}
