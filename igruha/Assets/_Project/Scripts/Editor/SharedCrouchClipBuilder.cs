using UnityEditor;
using UnityEngine;

namespace Igruha.EditorTools
{
    /// <summary>
    /// Делает из купленного клипа приседа пригодный для игры: распрямляет голову.
    ///
    /// В исходнике Mixamo персонаж крадётся, озираясь — голова и шея повёрнуты
    /// влево на весь клип (мышцы Turn Left-Right держат −0.51 и −0.44). В ролике
    /// это выразительно, а в игре читается как баг: корпус идёт на 12 часов,
    /// а смотрит персонаж на 10-11. Прицеливаться и понимать, куда он повёрнут,
    /// становится невозможно.
    ///
    /// Правится не поворотом корня — корпус как раз в порядке, — а самими
    /// кривыми головы. Клип внутри FBX только для чтения, поэтому рядом кладётся
    /// редактируемая копия .anim с обнулённым разворотом. Заодно она весит
    /// килобайты вместо 90 МБ и не тащит внутри модель.
    ///
    /// Наклон головы (Nod Down-Up) не трогаем: в приседе поднятая голова —
    /// это правильно, иначе персонаж уставится в пол.
    /// </summary>
    internal static class SharedCrouchClipBuilder
    {
        private const string SourceClipPath = "Assets/_Project/Art/Animations/Aza@Crouch Walk Forward.fbx";

        /// <summary>Куда кладётся исправленная копия. Именно её берут все контроллеры.</summary>
        public const string OutputClipPath = "Assets/_Project/Art/Animations/CrouchWalkForward.anim";

        /// <summary>
        /// Мышцы, уводящие взгляд в сторону. Поворот вокруг вертикали — очевидный
        /// вклад, но одного его мало: голова в клипе ещё и задрана (Nod), а на
        /// задранной голове наклон вбок (Tilt) тоже разворачивает лицо по
        /// горизонтали. Замер это подтвердил: в исходнике голова уведена на −62°,
        /// обнуление одних Turn оставляло −20°, вместе с Tilt остаётся −7°.
        /// Наклон вперёд-назад (Nod Down-Up) не трогаем: в приседе поднятая
        /// голова — это правильно, иначе персонаж уставится в пол.
        /// </summary>
        private static readonly string[] HeadTurnMuscles =
        {
            "Neck Turn Left-Right",
            "Head Turn Left-Right",
            "Neck Tilt Left-Right",
            "Head Tilt Left-Right"
        };

        [MenuItem("Igruha/Player/Rebuild Shared Crouch Clip")]
        internal static bool Build()
        {
            AnimationClip source = LoadSourceClip();
            if (source == null)
            {
                Debug.LogError($"SharedCrouchClipBuilder: не найден клип в {SourceClipPath}.");
                return false;
            }

            var fixedClip = new AnimationClip { name = System.IO.Path.GetFileNameWithoutExtension(OutputClipPath) };
            AnimationUtility.SetAnimationClipSettings(fixedClip, AnimationUtility.GetAnimationClipSettings(source));

            int flattened = 0;
            foreach (EditorCurveBinding binding in AnimationUtility.GetCurveBindings(source))
            {
                AnimationCurve curve = AnimationUtility.GetEditorCurve(source, binding);
                if (System.Array.IndexOf(HeadTurnMuscles, binding.propertyName) >= 0)
                {
                    curve = AnimationCurve.Constant(0f, source.length, 0f);
                    flattened++;
                }

                AnimationUtility.SetEditorCurve(fixedClip, binding, curve);
            }

            if (flattened != HeadTurnMuscles.Length)
            {
                Debug.LogWarning($"SharedCrouchClipBuilder: обнулено кривых {flattened} из {HeadTurnMuscles.Length} — имена мышц в исходнике другие, голову могло не выправить.");
            }

            AnimationClip existing = AssetDatabase.LoadAssetAtPath<AnimationClip>(OutputClipPath);
            if (existing == null)
            {
                AssetDatabase.CreateAsset(fixedClip, OutputClipPath);
            }
            else
            {
                // Перезаписываем содержимое существующего ассета, а не создаём новый:
                // на него уже ссылаются восемь Animator Controller'ов, и новый GUID
                // порвал бы все ссылки разом.
                EditorUtility.CopySerialized(fixedClip, existing);
                Object.DestroyImmediate(fixedClip);
            }

            AssetDatabase.SaveAssets();
            Debug.Log($"SharedCrouchClipBuilder: клип приседа собран, разворот головы обнулён ({flattened} кривых).");
            return true;
        }

        private static AnimationClip LoadSourceClip()
        {
            foreach (Object asset in AssetDatabase.LoadAllAssetsAtPath(SourceClipPath))
            {
                if (asset is AnimationClip clip && !clip.name.StartsWith("__preview__"))
                {
                    return clip;
                }
            }

            return null;
        }
    }
}
