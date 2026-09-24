using UnityEngine;

namespace Igruha.Minigames.HoleInWall
{
    /// <summary>
    /// Разбор одного участника на одной стене у сервера: влез ли он и чем это
    /// установлено. Живёт у участника и сбрасывается с каждой новой стеной.
    /// </summary>
    /// <remarks>
    /// <b>Зачем отчёт владельца.</b> Опозданий два, и они складываются.
    /// Часы клиента — это <c>ServerTime</c> NGO, время уже полученных пакетов:
    /// стена у клиента доходит до линии позже, чем у сервера. А копию клиента
    /// сервер видит ещё и с опозданием сетевого пути и буфера сглаживания.
    /// На стенде 22.09 отчёт клиента приходил через 0.09–0.18 с после линии
    /// у сервера, а импульс сноса долетал до клиента <b>раньше</b>, чем его
    /// собственная стена доезжала до линии. Воронка выреза
    /// (<see cref="WallFunnel"/>) доводит игрока как раз в последние
    /// полсекунды, а допуск попадания всего 0.15 м: на своём экране человек
    /// уже в дырке, а сервер судил его ещё на подходе. Это и было «визуально
    /// прошёл, а всё равно упал» с плейтеста 20.09 (IGR-594): «человеческая»
    /// болванка на клиенте не проходила ни одной стены, на хосте — проходила.
    ///
    /// Поэтому в момент проверки машина владельца сама замеряет своё тело
    /// и шлёт серверу, где оно стоит. <b>Решает по-прежнему сервер</b>:
    /// позу он берёт свою, подтверждённую (она приходит тем же надёжным
    /// каналом и раньше отчёта), вырез — свой, а присланное место принимает,
    /// только если оно не дальше <see cref="ReportSlack"/> от того, что видит
    /// сам (<see cref="Plausible"/>). Иначе, как и без отчёта вовсе, судит
    /// по своему виду.
    /// </remarks>
    public sealed class HoleInWallCheck
    {
        /// <summary>Чем установлен исход участника.</summary>
        public enum Source : byte
        {
            /// <summary>Ещё не решено.</summary>
            None = 0,

            /// <summary>Тело ведёт сам сервер — его вид и есть правда.</summary>
            Server,

            /// <summary>По отчёту машины владельца, проверенному сервером.</summary>
            Owner,

            /// <summary>Отчёт не пришёл вовремя — по виду сервера на линии проверки.</summary>
            Timeout,

            /// <summary>Отчёт не прошёл проверку правдоподобия — по виду сервера на линии проверки.</summary>
            Rejected,

            /// <summary>Отчёта и не ждали — удар случился раньше линии; по виду сервера в этот миг.</summary>
            Seen
        }

        /// <summary>
        /// Насколько присланное место может расходиться с тем, что видит сервер, м.
        ///
        /// Расхождение честного клиента — это его ход за время опоздания копии:
        /// воронка на подходе сдвигает человека на 0.1–0.3 м за 0.1–0.2 с,
        /// бегущий боком проходит до полуметра. Больше — это уже не опоздание,
        /// а другое место, и такому отчёту сервер не верит.
        /// </summary>
        public const float ReportSlack = 0.6f;

        /// <summary>
        /// Сколько сервер ждёт отчёт после линии проверки, с. Отчёт в пути
        /// дольше круга связи: часы клиента отстают на полпути, и ещё
        /// полпути он едет обратно. Обычно это 0.1–0.3 с; секунда — запас
        /// на плохую связь. До следующей стены после удара больше трёх
        /// секунд, а до итогов после последней — полторы, так что ожидание
        /// ни на что не наезжает.
        /// </summary>
        public const float ReportTimeout = 1f;

        public HoleInWallFit Fit { get; private set; }
        public Source DecidedBy { get; private set; }

        /// <summary>Когда решено, в общих часах. Для строки вердикта: насколько отчёт отстал от линии.</summary>
        public double DecidedAt { get; private set; }
        public bool Decided => DecidedBy != Source.None;

        /// <summary>Ждём отчёт владельца: линия проверки пройдена, а тело ведёт не сервер.</summary>
        public bool AwaitingReport { get; private set; }

        /// <summary>Вид сервера на линии проверки — на случай, если отчёт не придёт или не пройдёт проверку.</summary>
        public HoleInWallFit Fallback { get; private set; }

        /// <summary>Отчёт пришёл раньше, чем сервер сам дошёл до линии: часы сходятся не до миллисекунды.</summary>
        public bool HasEarlyReport { get; private set; }
        public Vector3 EarlyReport { get; private set; }

        /// <summary>Новая стена: всё, что было решено по прошлой, недействительно.</summary>
        public void Reset()
        {
            Fit = default;
            DecidedBy = Source.None;
            DecidedAt = 0d;
            AwaitingReport = false;
            Fallback = default;
            HasEarlyReport = false;
            EarlyReport = default;
        }

        public void Decide(HoleInWallFit fit, Source by, double at)
        {
            Fit = fit;
            DecidedBy = by;
            DecidedAt = at;
            AwaitingReport = false;
            HasEarlyReport = false;
        }

        /// <summary>Линия пройдена, судит владелец: запомнить свой вид на случай, если отчёта не будет.</summary>
        public void Await(HoleInWallFit fallback)
        {
            Fallback = fallback;
            AwaitingReport = true;
        }

        public void StashEarlyReport(Vector3 position)
        {
            EarlyReport = position;
            HasEarlyReport = true;
        }

        /// <summary>Не дождались или не поверили — решить по виду сервера на линии.</summary>
        public void DecideByFallback(Source why, double at) => Decide(Fallback, why, at);

        /// <summary>
        /// Правдоподобен ли отчёт: присланное место не дальше <see cref="ReportSlack"/>
        /// от того, что сервер видит сам, — по горизонтали и по высоте. Глубина
        /// в вердикте не участвует и здесь не сверяется.
        /// </summary>
        public static bool Plausible(Vector3 reported, Vector3 seen) =>
            Mathf.Abs(reported.x - seen.x) <= ReportSlack &&
            Mathf.Abs(reported.y - seen.y) <= ReportSlack;

        /// <summary>Подпись источника для строки вердикта.</summary>
        public static string Label(Source source)
        {
            switch (source)
            {
                case Source.Server: return "сам";
                case Source.Owner: return "отчёт";
                case Source.Timeout: return "вид сервера, отчёт не пришёл";
                case Source.Rejected: return "вид сервера, отчёт отклонён";
                case Source.Seen: return "вид сервера";
                default: return "вид сервера";
            }
        }
    }
}
