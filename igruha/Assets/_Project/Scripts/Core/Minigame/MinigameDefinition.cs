using UnityEngine;

namespace Igruha.Core.Minigame
{
    /// <summary>
    /// Конфигурация мини-игры: одна на игру, ноль нового кода для новой игры.
    /// Содержит и данные обучающей заставки (4.5), и параметры раунда (4.3).
    /// </summary>
    [CreateAssetMenu(fileName = "MinigameDefinition", menuName = "Igruha/Minigame Definition")]
    public sealed class MinigameDefinition : ScriptableObject
    {
        [Header("Общее")]
        [SerializeField] private string displayName = "Мини-игра";
        [SerializeField] private MinigameCategory category = MinigameCategory.FreeForAll;
        [Tooltip("Addressables-ключ сцены мини-игры (одиночный запуск и редактор)")]
        [SerializeField] private string sceneAddress = "";
        [Tooltip("Имя сцены в Build Settings для NGO SceneManager. Пусто — берётся хвост Scene Address")]
        [SerializeField] private string sceneName = "";

        [Header("Раунд")]
        [Tooltip("Лимит времени раунда, с. По истечении игра завершается автоматически")]
        [SerializeField] private float roundDuration = 120f;
        [SerializeField] private int minPlayers = 2;
        [SerializeField] private int maxPlayers = 8;

        [Header("Камера")]
        [SerializeField] private CameraMode cameraMode = CameraMode.ThirdPerson;

        [Header("Обучающая заставка")]
        [TextArea]
        [SerializeField] private string objective = "Цель игры";
        [Tooltip("Строки подсказок управления, например «WASD — бег»")]
        [SerializeField] private string[] controlHints = System.Array.Empty<string>();
        [Tooltip("Сколько секунд висит заставка, если её не закрыли вводом")]
        [SerializeField] private float tutorialDuration = 6f;

        public string DisplayName => displayName;
        public MinigameCategory Category => category;
        public string SceneAddress => sceneAddress;
        public string SceneName => !string.IsNullOrEmpty(sceneName) ? sceneName : ExtractSceneName(sceneAddress);
        public float RoundDuration => roundDuration;
        public int MinPlayers => minPlayers;
        public int MaxPlayers => maxPlayers;
        public CameraMode CameraMode => cameraMode;
        public string Objective => objective;
        public string[] ControlHints => controlHints;
        public float TutorialDuration => tutorialDuration;

        private static string ExtractSceneName(string address)
        {
            if (string.IsNullOrEmpty(address))
            {
                return string.Empty;
            }

            int slash = address.LastIndexOf('/');
            return slash >= 0 ? address.Substring(slash + 1) : address;
        }
    }
}
