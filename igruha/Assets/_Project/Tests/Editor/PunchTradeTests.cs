using System.Collections.Generic;
using Igruha.Core.Player;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Igruha.Tests
{
    /// <summary>
    /// IGR-582: встречный удар не выдёргивает сбитого из падения обратно в удар.
    ///
    /// Второе нажатие ЛКМ, пока первый удар ещё идёт, ждёт триггером: удар в
    /// себя не переходит. Если в этот момент сбили, ждущий удар срабатывал
    /// сразу за началом падения — лента аниматора Boss 21.09: 0.80 с падение,
    /// 0.88 с снова удар, до 2.37 с удар, потом стойка. Тело всё это время
    /// лежит без управления, а на экране удар и стойка.
    ///
    /// Проверка на настоящих контроллерах всех восьми: у каждого свой клип
    /// удара и своя длина, и замороженные контроллеры не правятся.
    /// </summary>
    public sealed class PunchTradeTests
    {
        private static readonly string[] Characters = { "Aza", "Boss", "Fat", "Girl", "Milez", "MyBoy", "Shlanga", "Player" };

        private static readonly KnockdownType[] Knockdowns = { KnockdownType.FlyBack, KnockdownType.FallForward };

        private const string PunchTrigger = "Punch";
        private const string PunchState = "Punch";

        /// <summary>Шаг аниматора — кадр при 60 к/с.</summary>
        private const float Step = 1f / 60f;

        /// <summary>Сколько стоим до удара: переход в покой после Rebind.</summary>
        private const float SettleTime = 0.5f;

        /// <summary>Сколько ждём, пока удар войдёт в своё состояние.</summary>
        private const float PunchEntryLimit = 1f;

        /// <summary>
        /// Сколько кадров второе нажатие висит до удара соперника. Мало: окно
        /// между кулдауном 0.6 с и концом клипа у большинства — десятые доли.
        /// </summary>
        private const int PendingFrames = 2;

        /// <summary>Сколько смотрим после падения: цепочка «падение → подъём» короче четырёх секунд.</summary>
        private const float WatchTime = 4f;

        private readonly List<Object> spawned = new List<Object>();

        [TearDown]
        public void Clean()
        {
            foreach (Object item in spawned)
            {
                if (item != null)
                {
                    Object.DestroyImmediate(item);
                }
            }

            spawned.Clear();
        }

        [Test]
        public void PendingPunchDoesNotHijackKnockdown(
            [ValueSource(nameof(Characters))] string character,
            [ValueSource(nameof(Knockdowns))] KnockdownType knockdown)
        {
            Animator animator = Build(character);

            animator.SetTrigger(PunchTrigger);
            Assert.That(AdvanceUntilIn(animator, PunchState, PunchEntryLimit), Is.True,
                $"{character}: удар не начался — проверять нечего");

            // Второе нажатие посреди удара: в себя удар не переходит, триггер ждёт.
            animator.SetTrigger(PunchTrigger);
            Advance(animator, PendingFrames);

            CharacterAnimatorDriver.PlayKnockdown(animator, knockdown);

            string fallState = knockdown == KnockdownType.FlyBack ? "FlyBack" : "FallForward";
            string standState = knockdown == KnockdownType.FlyBack ? "StandUpFromBack" : "StandUpFromForward";

            bool fell = false;
            bool stoodUp = false;
            int steps = Mathf.CeilToInt(WatchTime / Step);

            for (int i = 0; i < steps && !stoodUp; i++)
            {
                Advance(animator, 1);
                fell |= IsIn(animator, fallState);
                stoodUp |= IsIn(animator, standState);

                if (fell && Enters(animator, PunchState))
                {
                    Assert.Fail($"{character}, {knockdown}: через {i * Step:F2} с после падения аниматор вернулся в удар");
                }
            }

            Assert.That(fell, Is.True, $"{character}, {knockdown}: падение не началось");
            Assert.That(stoodUp, Is.True, $"{character}, {knockdown}: подъём не начался — цепочку падения что-то перебило");
        }

        /// <summary>
        /// Настоящий префаб в стойке. Аниматор — в «анимировать всегда»: без
        /// камеры кости и состояния иначе не обновляются, и тест шёл бы впустую.
        /// </summary>
        private Animator Build(string character)
        {
            string path = $"Assets/_Project/Prefabs/Player/{character}.prefab";
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            Assert.That(prefab, Is.Not.Null, $"нет префаба {path}");

            var player = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            spawned.Add(player);

            var driver = player.GetComponent<CharacterAnimatorDriver>();
            Assert.That(driver, Is.Not.Null, $"{character}: нет CharacterAnimatorDriver");

            var animator = new SerializedObject(driver).FindProperty("animator").objectReferenceValue as Animator;
            Assert.That(animator, Is.Not.Null, $"{character}: у драйвера не назначен аниматор");
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;

            animator.Rebind();
            animator.Update(0f);
            Advance(animator, Mathf.RoundToInt(SettleTime / Step));
            return animator;
        }

        private static void Advance(Animator animator, int frames)
        {
            for (int i = 0; i < frames; i++)
            {
                animator.Update(Step);
            }
        }

        private static bool AdvanceUntilIn(Animator animator, string state, float limit)
        {
            int steps = Mathf.CeilToInt(limit / Step);
            for (int i = 0; i < steps; i++)
            {
                Advance(animator, 1);
                if (!animator.IsInTransition(0) && animator.GetCurrentAnimatorStateInfo(0).IsName(state))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Аниматор идёт в это состояние: стоит в нём вне перехода или
        /// переходит в него. Переход ИЗ него — не считается: в первые кадры
        /// падения текущим ещё числится удар.
        /// </summary>
        private static bool Enters(Animator animator, string state)
        {
            return animator.IsInTransition(0)
                ? animator.GetNextAnimatorStateInfo(0).IsName(state)
                : animator.GetCurrentAnimatorStateInfo(0).IsName(state);
        }

        /// <summary>Аниматор в этом состоянии или уже переходит в него.</summary>
        private static bool IsIn(Animator animator, string state)
        {
            if (animator.GetCurrentAnimatorStateInfo(0).IsName(state))
            {
                return true;
            }

            return animator.IsInTransition(0) && animator.GetNextAnimatorStateInfo(0).IsName(state);
        }
    }
}
