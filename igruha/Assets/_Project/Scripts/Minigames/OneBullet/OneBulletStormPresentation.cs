using Igruha.Core.Minigame;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Igruha.Minigames.OneBullet
{
    /// <summary>All presentation follows the replicated stage and deadlines.</summary>
    public sealed class OneBulletStormPresentation : MonoBehaviour
    {
        [SerializeField] private OneBulletMinigame game;
        [SerializeField] private OneBulletStorm storm;
        [SerializeField] private ParticleSystem dustPrefab;
        private ParticleSystem[] dust;
        [SerializeField] private Transform[] arrows;
        [SerializeField] private Renderer[] veils;
        [SerializeField] private CanvasGroup canPanel, warningPanel, routePanel;
        [SerializeField] private TMP_Text canCount, title, detail, seconds, route;
        [SerializeField] private Image progress, tint;
        [SerializeField] private Material warningVeil, dangerVeil;
        private int[] dustModes;
        private int stage = -1, safeStage = -1, shownCans = -1, shownSecond = -1, shownMode = -1, shownRoute = -1;
        private bool wasActive, practiceLayout;
        private RectTransform canRect;
        private const float WarningRate = 2f, DangerRate = 6f;
        private static readonly Color Sand = new Color(.9f, .63f, .28f), Danger = new Color(1f, .34f, .19f);
        private void Awake()
        {
            canRect = (RectTransform)canPanel.transform;
            var layout = storm.Layout;
            dust = new ParticleSystem[layout.NodeCount + layout.Edges.Count]; dustModes = new int[dust.Length];
            for (int i = 0; i < dust.Length; i++)
            {
                Vector3 point;
                if (i < layout.NodeCount) point = layout.Position(i);
                else { var e = layout.Edges[i - layout.NodeCount]; point = (layout.Position(e.A) + layout.Position(e.B)) * .5f; }
                dust[i] = Instantiate(dustPrefab, point + Vector3.up * 1.4f, Quaternion.identity, transform);
            }
        }
        private void Update()
        {
            if (practiceLayout != game.IsPractice)
            {
                practiceLayout = game.IsPractice;
                canRect.anchorMin = canRect.anchorMax = canRect.pivot = practiceLayout ? Vector2.one : Vector2.zero;
                canRect.anchoredPosition = practiceLayout ? new Vector2(-44, -376) : new Vector2(28, 28);
            }
            bool active = game.GameplayActive && !game.Round.Finished;
            bool alive = active && game.LocalParticipant != null && !game.LocalParticipant.Dead;
            canPanel.alpha = alive ? 1 : 0;
            if (!active)
            {
                warningPanel.alpha = routePanel.alpha = 0; tint.color = Color.clear;
                if (wasActive) ClearWorld(); wasActive = false; return;
            }
            wasActive = true;
            if (stage != storm.State.Stage || safeStage != storm.State.SafeStage)
            { stage = storm.State.Stage; safeStage = storm.State.SafeStage; RefreshWorld(); }
            var record = game.Round.Find(game.LocalId);
            if (record != null && shownCans != record.Cans)
            { shownCans = record.Cans; canCount.SetText("Q  ·  БАНКА   {0}", shownCans); }
            double now = NetworkClock.Now;
            bool danger = alive && record != null && record.DangerSince >= 0;
            bool closing = alive && !storm.Layout.Safe(game.LocalParticipant.Motor.Position, safeStage);
            int mode = danger ? 2 : storm.State.Warning ? 1 : 0;
            float remaining = mode == 2 ? Mathf.Max(0, game.Config.StormExposure - (float)(now - record.DangerSince)) :
                Mathf.Max(0, (float)(storm.State.ClosesAt - now));
            warningPanel.alpha = mode > 0 ? 1 : 0;
            tint.color = new Color(.62f, .28f, .08f, danger ? .1f + .07f * Mathf.Sin(Time.unscaledTime * 4f) : 0);
            progress.color = mode == 2 ? Danger : Sand;
            progress.fillAmount = Mathf.Clamp01(remaining / (mode == 2 ? game.Config.StormExposure : game.Config.StormWarning));
            int second = Mathf.CeilToInt(remaining), detailMode = closing ? mode + 3 : mode;
            if (shownMode != detailMode)
            {
                shownMode = detailMode;
                title.text = mode == 2 ? "ТЫ В БУРЕ" : "БУРЯ ПРИБЛИЖАЕТСЯ";
                detail.text = mode == 2 ? "Выберись в чистый двор, пока не поздно" : closing ?
                    "Этот проход закроется. Следуй по стрелкам" : "Пыль забирает дальние дворы";
            }
            if (shownSecond != second) { shownSecond = second; seconds.SetText("{0:00}", second); }
            routePanel.alpha = closing ? 1 : 0;
            if (!closing) return;
            int node = storm.Layout.NodeAt(game.LocalParticipant.Motor.Position);
            int next = storm.Layout.NextEscapeNode(node, safeStage);
            if (next < 0 || next == node) return;
            Vector3 direction = storm.Layout.StandingPoint(next) - game.LocalParticipant.Motor.Position;
            float angle = Vector3.SignedAngle(Vector3.ProjectOnPlane(game.LocalAim, Vector3.up), Vector3.ProjectOnPlane(direction, Vector3.up), Vector3.up);
            int routeMode = Mathf.Abs(angle) > 135 ? 3 : angle > 35 ? 1 : angle < -35 ? 2 : 0;
            if (shownRoute == routeMode) return;
            shownRoute = routeMode;
            route.text = routeMode == 3 ? "РАЗВЕРНИСЬ · К ЧИСТОМУ ДВОРУ" : routeMode == 1 ? "К ЧИСТОМУ ДВОРУ  →" :
                routeMode == 2 ? "←  К ЧИСТОМУ ДВОРУ" : "↑  ВПЕРЁД · К ЧИСТОМУ ДВОРУ";
        }
        private void RefreshWorld()
        {
            var layout = storm.Layout;
            for (int i = 0; i < dust.Length; i++)
            {
                int mode = !layout.Safe(dust[i].transform.position, stage) ? 2 : !layout.Safe(dust[i].transform.position, safeStage) ? 1 : 0;
                if (dustModes[i] == mode) continue; dustModes[i] = mode;
                var emission = dust[i].emission; emission.rateOverTime = mode == 2 ? DangerRate : WarningRate;
                var main = dust[i].main; main.startColor = new Color(.77f, .58f, .33f, mode == 2 ? .32f : .12f);
                if (mode > 0) dust[i].Play(); else dust[i].Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            }
            for (int i = 0; i < arrows.Length; i++)
            {
                bool show = !layout.Safe(i, safeStage); arrows[i].gameObject.SetActive(show);
                if (!show) continue;
                int next = layout.NextEscapeNode(i, safeStage);
                Vector3 d = layout.Position(next) - layout.Position(i); d.y = 0;
                if (d.sqrMagnitude > .01f) arrows[i].rotation = Quaternion.LookRotation(d, Vector3.up);
            }
            for (int i = 0; i < veils.Length; i++)
            {
                var e = layout.Edges[i];
                bool boundary = layout.Safe(e.A, stage) != layout.Safe(e.B, stage);
                bool warning = layout.Safe(e.A, safeStage) != layout.Safe(e.B, safeStage);
                veils[i].enabled = boundary || warning;
                veils[i].sharedMaterial = boundary ? dangerVeil : warningVeil;
            }
        }
        private void ClearWorld()
        {
            stage = safeStage = -1;
            for (int i = 0; i < dust.Length; i++) { dustModes[i] = 0; dust[i].Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear); }
            foreach (var arrow in arrows) arrow.gameObject.SetActive(false);
            foreach (var veil in veils) veil.enabled = false;
        }
        private void OnDisable() { if (dustModes != null) ClearWorld(); }
    }
}
