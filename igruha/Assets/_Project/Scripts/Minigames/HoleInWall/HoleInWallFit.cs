using System.Text;
using UnityEngine;

namespace Igruha.Minigames.HoleInWall
{
    /// <summary>
    /// Один участник против одного выреза: три условия спеки 5.3 по отдельности —
    /// поза, горизонталь, опора.
    ///
    /// <b>Одна запись правила на вердикт и на лог.</b> Сервер судит по
    /// <see cref="Fits"/>, а строка «почему упал» печатается из тех же чисел
    /// (<see cref="Describe"/>). Раньше лога не было вовсе, и «прошёл, а упал»
    /// с живого прогона 20.09 нечем было проверить: у каждой машины своя
    /// правдоподобная картинка, а сервер молчал, по каким числам решил.
    ///
    /// Габариты капсулы в проверке не участвуют (спека 5.3): сравниваются номер
    /// позы и одно число по горизонтали, поэтому толстый и тонкий проходят
    /// одинаково.
    /// </summary>
    public readonly struct HoleInWallFit
    {
        /// <summary>Поза, в которой участник стоит.</summary>
        public HoleInWallPose Have { get; }

        /// <summary>Поза выреза. <see cref="HoleInWallPose.None"/> — выреза под этим номером нет.</summary>
        public HoleInWallPose Need { get; }

        /// <summary>Смещение от центра выреза по горизонтали со знаком, м.</summary>
        public float OffsetX { get; }

        /// <summary>Допуск по горизонтали для этой позы этого персонажа, м.</summary>
        public float Tolerance { get; }

        /// <summary>Высота над полом платформы, м. Большая — в прыжке, отрицательная — в воде.</summary>
        public float Height { get; }

        /// <summary>Допуск по высоте, м.</summary>
        public float GroundedTolerance { get; }

        private HoleInWallFit(HoleInWallPose have, HoleInWallPose need, float offsetX, float tolerance,
            float height, float groundedTolerance)
        {
            Have = have;
            Need = need;
            OffsetX = offsetX;
            Tolerance = tolerance;
            Height = height;
            GroundedTolerance = groundedTolerance;
        }

        public bool HasCutout => Need != HoleInWallPose.None;
        public bool PoseOk => HasCutout && Have == Need;
        public bool PlaceOk => HasCutout && Mathf.Abs(OffsetX) <= Tolerance;
        public bool GroundOk => Mathf.Abs(Height) <= GroundedTolerance;

        /// <summary>Влез: все три условия разом.</summary>
        public bool Fits => PoseOk && PlaceOk && GroundOk;

        /// <summary>
        /// Замерить участника против выреза под этим номером — таким, какой
        /// стоит на стене прямо сейчас, с учётом подвоха.
        ///
        /// Позиция берётся у мотора, а не у трансформа: тот отстаёт на кадр
        /// после телепорта, и вернувшийся из воды на этом кадре считался бы
        /// всё ещё в воде.
        /// </summary>
        public static HoleInWallFit Measure(HoleInWallConfig config, HoleInWallTrack track,
            HoleInWallTrack.Member member, int cutoutIndex) =>
            Measure(config, track, member, cutoutIndex,
                member != null && member.Avatar != null ? member.Avatar.Position : Vector3.zero);

        /// <summary>
        /// То же, но тело стоит там, где сказано, а не там, где его видит эта
        /// машина. Так сервер разбирает отчёт владельца: место — из отчёта,
        /// поза и вырез — свои (см. <see cref="HoleInWallCheck"/>).
        /// </summary>
        public static HoleInWallFit Measure(HoleInWallConfig config, HoleInWallTrack track,
            HoleInWallTrack.Member member, int cutoutIndex, Vector3 position)
        {
            HoleInWallPose need = HoleInWallPose.None;
            float offset = 0f;
            if (track != null && track.Wall != null)
            {
                track.Wall.TryGetCutout(cutoutIndex, out need, out offset);
            }

            if (member == null || member.Avatar == null || member.Pose == null || config == null)
            {
                return new HoleInWallFit(HoleInWallPose.None, need, float.PositiveInfinity, 0f,
                    float.PositiveInfinity, 0f);
            }

            float cutoutX = track.transform.position.x + offset;
            float tolerance = need != HoleInWallPose.None ? member.Shapes.Tolerance(need) : 0f;

            return new HoleInWallFit(member.Pose.CurrentPose, need, position.x - cutoutX, tolerance,
                position.y - config.PlatformSurfaceY, config.GroundedTolerance);
        }

        /// <summary>
        /// Строка разбора для лога: что совпало, что нет и на сколько.
        /// Зовётся раз на стену, не в каждом такте, — строки здесь позволены.
        /// </summary>
        public void Describe(StringBuilder into)
        {
            into.Append(Fits ? "✓" : "✗")
                .Append(" поза ").Append(Have)
                .Append(PoseOk ? "=" : "≠").Append(Need)
                .Append(", dx ").Append(OffsetX.ToString("+0.00;-0.00"))
                .Append(PlaceOk ? "≤" : ">").Append(Tolerance.ToString("0.00"))
                .Append(", dy ").Append(Height.ToString("+0.00;-0.00"))
                .Append(GroundOk ? "≤" : ">").Append(GroundedTolerance.ToString("0.00"));
        }
    }
}
