using Igruha.Core.Audio;
using TMPro;
using UnityEngine;

namespace Igruha.Core.Hub.Activities
{
    public sealed class StarshipPresentation : MonoBehaviour
    {
        public const float PixelsPerUnit = 260;
        private const float SnapshotSeconds = 1f / 30f;
        [SerializeField] private StarshipStation station;
        [SerializeField] private RectTransform ship;
        [SerializeField] private RectTransform[] enemies, shots, bombs, stars;
        [SerializeField] private TMP_Text score, lives, wave, title, button, hint;
        [SerializeField] private GameObject panel;
        [SerializeField] private MinigameAudioPlayer audioPlayer;
        private StarshipState previous;
        private bool initialized, wasOccupied, wasPaused;
        private float receivedAt, visualTime;
        private static readonly string[] Slots = { null, "arcade.level", "arcade.launch", "arcade.brick",
            "arcade.life", "arcade.level", "arcade.over" };
        private void Update()
        {
            StarshipState s = station.State;
            bool paused = station.IsArcadePaused;
            bool occupied = station.Occupant != HubActivityStation.NoOccupant;
            if (!initialized || s.Tick != previous.Tick || s.Session != previous.Session) receivedAt = Time.unscaledTime;
            if (!initialized || s.Score != previous.Score) score.SetText("СЧЁТ {0:00000}", s.Score);
            if (!initialized || s.Lives != previous.Lives) lives.SetText("ЖИЗНИ {0}", s.Lives);
            if (!initialized || s.Wave != previous.Wave) wave.SetText("ВОЛНА {0}", s.Wave);
            if (!initialized || s.Phase != previous.Phase || occupied != wasOccupied || paused != wasPaused || s.Wave != previous.Wave)
            {
                panel.SetActive(paused || s.Phase != StarshipPhase.Playing);
                if (paused) { title.text = "ПАУЗА"; button.text = "ИГРОК В МЕНЮ"; hint.text = "ЖДЁМ ПРОДОЛЖЕНИЯ"; }
                else if (!occupied) { title.text = "КОРАБЛИКИ"; button.text = "ЗАЖМИ E"; hint.text = "ЗАЩИТИ СВОЮ ГАЛАКТИКУ"; }
                else if (s.Phase == StarshipPhase.GameOver)
                { title.text = "ИГРА ОКОНЧЕНА"; button.text = "ЕЩЁ РАЗ"; hint.text = "ЛКМ — НОВЫЙ ПОЛЁТ"; }
                else
                {
                    title.text = s.Event == StarshipEvent.LifeLost ? "ЕЩЁ ЕСТЬ ШАНС" : s.Wave > 1 ? "НОВАЯ ВОЛНА" : "КОРАБЛИКИ";
                    button.text = "ЛКМ — В БОЙ"; hint.text = "ДЕРЖИ ЛКМ И УВОРАЧИВАЙСЯ";
                }
            }
            if (!paused) visualTime += Time.unscaledDeltaTime;
            float predict = !paused && s.Phase == StarshipPhase.Playing ? Mathf.Min(Time.unscaledTime - receivedAt, SnapshotSeconds) : 0;
            ship.anchoredPosition = new Vector2(station.IsLocal ? station.LocalShip : s.Ship, StarshipRules.ShipY) * PixelsPerUnit;
            ship.gameObject.SetActive(s.Phase != StarshipPhase.GameOver && (s.Shield <= 0 || (int)(visualTime * 12) % 2 == 0));
            for (int i = 0; i < enemies.Length; i++)
            {
                bool active = (s.Enemies & (1U << i)) != 0;
                enemies[i].gameObject.SetActive(active);
                if (active) enemies[i].anchoredPosition = StarshipRules.EnemyPosition(s, i) * PixelsPerUnit;
            }
            for (int i = 0; i < StarshipRules.ProjectileCount; i++)
            {
                shots[i].gameObject.SetActive((s.Shots & (1 << i)) != 0);
                bombs[i].gameObject.SetActive((s.Bombs & (1 << i)) != 0);
                shots[i].anchoredPosition = (s.Projectile(false, i) + Vector2.up * (StarshipRules.ShotSpeed * predict)) * PixelsPerUnit;
                bombs[i].anchoredPosition = (s.Projectile(true, i) + Vector2.down * (StarshipRules.BombSpeed(s.Wave) * predict)) * PixelsPerUnit;
            }
            for (int i = 0; i < stars.Length; i++)
                stars[i].anchoredPosition = new Vector2((i * 137 % 510) - 255,
                    Mathf.Repeat(i * 73 - visualTime * (8 + i % 3 * 6), 320) - 160);
            if (initialized && s.Session == previous.Session && s.EventId != previous.EventId &&
                s.Event != StarshipEvent.None && audioPlayer != null) audioPlayer.PlayAt(Slots[(int)s.Event], transform.position);
            previous = s; wasOccupied = occupied; wasPaused = paused; initialized = true;
        }
    }
}
