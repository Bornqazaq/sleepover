using UnityEngine;
using TMPro;
using Igruha.Core.Minigame;

namespace Igruha.Minigames.OneBullet
{
    public sealed class OneBulletPresentation : MonoBehaviour
    {
        [SerializeField] private OneBulletMinigame game;
        [SerializeField] private Transform gun;
        [SerializeField] private Light pickupLight;
        [SerializeField] private CanvasGroup ammunition;
        [SerializeField] private LineRenderer tracer;
        [SerializeField] private ParticleSystem smoke;
        [SerializeField] private ParticleSystem impact;
        [SerializeField] private Light muzzleFlash;
        [SerializeField] private LineRenderer pickupRing;
        [SerializeField] private TMP_Text weaponStatus;
        private int holder = -1, pickup = -1;
        private int shownWeaponState = int.MinValue;
        private float tracerUntil;
        private const float HeldScale = 1.1f, PickupScale = 2f, PalmDrop = .06f;
        private static readonly Vector3 GripCenter = new Vector3(0, -.075f, -.078f);
        private const float SpinSpeed = 35f, BobAmplitude = .06f, BobSpeed = 2f;
        private void OnEnable() { game.Changed += Refresh; game.Shot += OnShot; }
        private void OnDisable() { game.Changed -= Refresh; game.Shot -= OnShot; }
        private void Refresh()
        {
            holder = game.Round.Holder; pickup = game.Round.Pickup;
            bool visible = game.GameplayActive && (holder >= 0 || pickup >= 0);
            gun.gameObject.SetActive(visible && !game.LocalArmed);
            pickupLight.enabled = visible && pickup >= 0;
            if (pickupRing != null) pickupRing.enabled = visible && pickup >= 0;
            ammunition.alpha = game.LocalArmed ? 1f : 0f;
            shownWeaponState = int.MinValue;
        }
        private void LateUpdate()
        {
            tracer.enabled = Time.time < tracerUntil;
            if (muzzleFlash != null) muzzleFlash.enabled = Time.time < tracerUntil;
            UpdateStatus();
            if (!game.GameplayActive) return;
            if (pickup >= 0)
            {
                gun.localScale = Vector3.one * PickupScale;
                gun.position = game.PickupPosition + Vector3.up * (.48f + Mathf.Sin(Time.time * BobSpeed) * BobAmplitude);
                gun.rotation = Quaternion.Euler(0, Time.time * SpinSpeed, -12);
                pickupLight.transform.position = gun.position + Vector3.up * .3f;
                if (pickupRing != null) pickupRing.transform.position = game.PickupPosition - Vector3.up * .08f;
            }
            else if (holder >= 0)
            {
                var p = game.Find(holder);
                if (p?.Motor != null)
                {
                    gun.localScale = Vector3.one * HeldScale;
                    gun.rotation = p.Hand != null ? p.Hand.rotation * Quaternion.Euler(0, -90, 90) : p.Motor.transform.rotation;
                    Vector3 grip = p.Hand != null ? p.Hand.position - Vector3.up * PalmDrop : p.Motor.Position + Vector3.up;
                    gun.position = grip - gun.rotation * (GripCenter * HeldScale);
                }
            }
        }
        private void UpdateStatus()
        {
            if (weaponStatus == null) return;
            int state = !game.GameplayActive ? -4 : game.LocalArmed ? -3 : game.Round.Holder >= 0 ? -2 :
                game.Round.Pickup >= 0 ? -1 : Mathf.Max(0, Mathf.CeilToInt((float)(game.Round.SpawnAt - NetworkClock.Now)));
            if (state == shownWeaponState) return;
            shownWeaponState = state;
            weaponStatus.text = state == -4 ? "" : state == -3 ? "ОДИН ПАТРОН. ВЫБИРАЙ МОМЕНТ." :
                state == -2 ? "ОРУЖИЕ ПОДОБРАНО • СЛУШАЙ ШАГИ" : state == -1 ? "РЕВОЛЬВЕР В ЛАБИРИНТЕ • ИЩИ ЗОЛОТОЕ СВЕЧЕНИЕ" :
                $"РЕВОЛЬВЕР ПОЯВИТСЯ ЧЕРЕЗ {state} С";
        }
        private void OnShot(Vector3 origin, Vector3 end, bool hit)
        {
            tracer.SetPosition(0, origin); tracer.SetPosition(1, end);
            tracerUntil = Time.time + .065f;
            if (muzzleFlash != null) muzzleFlash.transform.position = origin;
            if (smoke != null)
            {
                smoke.transform.position = origin; smoke.transform.rotation = Quaternion.LookRotation(end - origin); smoke.Emit(9);
            }
            if (impact != null && Vector3.Distance(origin, end) < game.Config.ShotRange - .1f)
            {
                impact.transform.position = end; impact.transform.rotation = Quaternion.LookRotation(origin - end); impact.Emit(12);
            }
        }
    }
}
