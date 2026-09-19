using UnityEngine;
using Igruha.Core.Traps;

namespace Igruha.Core.Audio
{
    /// <summary>
    /// Звук ловушки — общий слой фазы 5. Висит на самой ловушке и озвучивает
    /// её события: рывок рычага, хлопок двери, провал пола.
    ///
    /// <b>Привязка идёт к событиям ловушки, а не к нажатию кнопки.</b> Кнопку жмёт
    /// один игрок и на своей машине, а <see cref="TrapBase.Fired"/> и смена состояния
    /// поднимаются у всех — в том числе на машинах, которые исход не считали
    /// (<see cref="TrapBase.PlayFired"/>). Звук, повешенный на нажатие, услышал бы
    /// только нажавший.
    ///
    /// Слоты вынесены в поля не ради настройки, а ради подмены: у цирка свой
    /// хлопок люка, у Duck Hunt — своя дверь. Пустое поле означает «этого звука
    /// у ловушки нет», а не «играй общий».
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class TrapAudio : MonoBehaviour
    {
        [Tooltip("Проигрыватель звука арены")]
        [SerializeField] private MinigameAudioPlayer audioPlayer;

        [Tooltip("Ловушка. Не задана — берётся с этого же объекта")]
        [SerializeField] private TrapBase trap;

        [Tooltip("Рычаг ловушки. Задан — на срабатывании звучит рывок рычага")]
        [SerializeField] private TrapLever lever;

        [Tooltip("Слот рывка рычага. Пусто — рычаг молчит")]
        [SerializeField] private string leverSlot = CoreSfx.TrapLeverPull;

        [Tooltip("Слот хлопка двери. Пусто — дверь молчит")]
        [SerializeField] private string doorSlot = CoreSfx.TrapDoorSlam;

        [Tooltip("Слот провала пола. Пусто — провал молчит")]
        [SerializeField] private string floorSlot = CoreSfx.TrapFloorCollapse;

        private DoorTrap door;
        private CollapsingFloorTrap floor;

        private void Awake()
        {
            if (trap == null) trap = GetComponent<TrapBase>();
            door = trap as DoorTrap;
            floor = trap as CollapsingFloorTrap;
        }

        private void OnEnable()
        {
            if (trap == null) return;

            if (lever != null) trap.Fired += OnFired;
            if (door != null) door.ClosedChanged += OnDoorClosedChanged;
            if (floor != null) floor.OpenChanged += OnFloorOpenChanged;
        }

        private void OnDisable()
        {
            if (trap == null) return;

            if (lever != null) trap.Fired -= OnFired;
            if (door != null) door.ClosedChanged -= OnDoorClosedChanged;
            if (floor != null) floor.OpenChanged -= OnFloorOpenChanged;
        }

        private void OnFired() => Play(leverSlot);

        /// <summary>Хлопает только створка, которая закрылась: открывается дверь тихо.</summary>
        private void OnDoorClosedChanged(bool closed)
        {
            if (closed) Play(doorSlot);
        }

        /// <summary>
        /// Провал звучит и когда открывается, и когда возвращается: это одна и та же
        /// плита, ходящая туда-обратно, и паспорт поставки прямо велит играть на
        /// возврат тот же звук.
        /// </summary>
        private void OnFloorOpenChanged(bool open) => Play(floorSlot);

        private void Play(string slot)
        {
            if (audioPlayer == null || string.IsNullOrEmpty(slot)) return;
            audioPlayer.PlayAt(slot, transform.position);
        }
    }
}
