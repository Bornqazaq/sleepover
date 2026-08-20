using UnityEditor;
using UnityEngine;

namespace Igruha.EditorTools
{
    /// <summary>
    /// Делает из купленного клипа подкрадывания пригодный для игры: доворачивает
    /// его целиком на 12 часов и кладёт рядом лёгкую копию.
    ///
    /// Разворот у клипов Mixamo запечён в КОРЕНЬ: у прежнего клипа
    /// (Aza@Crouch Walk Forward) таз смотрел на 44.7°, плечи на 55.3°, и всё тело
    /// шло боком. Мышцы скрутки корпуса при этом почти нулевые — корпус
    /// относительно таза не скручен, развёрнут именно корень. Поэтому правится
    /// поворотом корня, и только им.
    ///
    /// Кривые шеи и головы не трогаем. Попытка выправить голову обнулением её
    /// мышц была ошибкой: в исходнике голова компенсировала разворот корня и
    /// смотрела туда, куда идут ноги, — стерев компенсацию, её довернули к
    /// кривому корпусу, а корпус остался кривым.
    ///
    /// Клип внутри FBX только для чтения, поэтому рядом кладётся редактируемая
    /// копия .anim. Заодно она весит килобайты вместо 90 МБ и не тащит модель.
    ///
    /// Замена клипа: положить новый FBX, указать его в SourceClipPath, замерить
    /// разворот таза и вписать его со знаком минус в RootYawFixDegrees.
    /// </summary>
    internal static class SharedCrouchClipBuilder
    {
        private const string SourceClipPath = "Assets/_Project/Art/Animations/Aza@Sneaking Forward.fbx";

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
        // Кривые шеи и головы больше не трогаем вовсе — см. комментарий к классу.
        /// <summary>
        /// На сколько градусов доворачивается корень клипа.
        ///
        /// Обнулить одну голову было половиной дела и давало обратный эффект.
        /// Замер клипа: мышцы скрутки корпуса (Spine/Chest/UpperChest Twist)
        /// в среднем 0.005…0.080, то есть корпус относительно таза почти не
        /// скручен. Развёрнут сам корень: таз смотрит на 44.8°, плечи на 55.3°,
        /// а голова в исходнике компенсирует это назад и смотрит на 7.2° — туда,
        /// куда шагают ноги. Стёрли компенсацию — голова уехала к кривому
        /// корпусу (52°), а корпус как был кривым, так и остался: персонаж шёл
        /// боком. Поэтому корень доворачивается на тот же угол обратно.
        ///
        /// Поле Offset в импорте здесь не годится: при Bake Into Pose оно не
        /// даёт ничего — проверено.
        /// </summary>
        private const float RootYawFixDegrees = -11.2f;

        /// <summary>Кривые поворота корня. Крутятся все четыре вместе, покомпонентно кватернион крутить нельзя.</summary>
        private static readonly string[] RootRotation = { "RootQ.x", "RootQ.y", "RootQ.z", "RootQ.w" };


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

            foreach (EditorCurveBinding binding in AnimationUtility.GetCurveBindings(source))
            {
                AnimationCurve curve = AnimationUtility.GetEditorCurve(source, binding);

                if (System.Array.IndexOf(RootRotation, binding.propertyName) >= 0)
                {
                    // Корень крутим отдельно, после цикла: здесь видна одна компонента,
                    // а поворот требует всех четырёх сразу.
                    continue;
                }

                AnimationUtility.SetEditorCurve(fixedClip, binding, curve);
            }

            RotateRootYaw(source, fixedClip, RootYawFixDegrees);


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
            Debug.Log($"SharedCrouchClipBuilder: клип приседа собран, корень развёрнут на {RootYawFixDegrees:F1}°, остальное — как в исходнике.");
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

        /// <summary>
        /// Довернуть корень клипа вокруг вертикали.
        ///
        /// Четыре кривые кватерниона читаются вместе и пересобираются по общему
        /// набору моментов времени: у компонент ключи стоят в разных местах,
        /// и крутить их по отдельности — значит получить мусор.
        /// </summary>
        private static void RotateRootYaw(AnimationClip source, AnimationClip target, float degrees)
        {
            var curves = new AnimationCurve[RootRotation.Length];
            var bindings = new EditorCurveBinding[RootRotation.Length];
            var times = new System.Collections.Generic.SortedSet<float>();

            foreach (EditorCurveBinding binding in AnimationUtility.GetCurveBindings(source))
            {
                int index = System.Array.IndexOf(RootRotation, binding.propertyName);
                if (index < 0)
                {
                    continue;
                }

                bindings[index] = binding;
                curves[index] = AnimationUtility.GetEditorCurve(source, binding);
                foreach (Keyframe key in curves[index].keys)
                {
                    times.Add(key.time);
                }
            }

            for (int i = 0; i < curves.Length; i++)
            {
                if (curves[i] == null)
                {
                    Debug.LogWarning($"SharedCrouchClipBuilder: в клипе нет {RootRotation[i]} — корпус развернуть не удалось.");
                    return;
                }
            }

            Quaternion fix = Quaternion.Euler(0f, degrees, 0f);
            var rebuilt = new AnimationCurve[] { new AnimationCurve(), new AnimationCurve(), new AnimationCurve(), new AnimationCurve() };

            foreach (float time in times)
            {
                var q = new Quaternion(
                    curves[0].Evaluate(time), curves[1].Evaluate(time),
                    curves[2].Evaluate(time), curves[3].Evaluate(time));
                Quaternion turned = fix * q;
                rebuilt[0].AddKey(time, turned.x);
                rebuilt[1].AddKey(time, turned.y);
                rebuilt[2].AddKey(time, turned.z);
                rebuilt[3].AddKey(time, turned.w);
            }

            for (int i = 0; i < RootRotation.Length; i++)
            {
                AnimationUtility.SetEditorCurve(target, bindings[i], rebuilt[i]);
            }
        }

    }
}
