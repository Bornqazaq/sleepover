namespace Igruha.Core.Voice
{
    /// <summary>
    /// Формат голосового пакета — один на всю игру, потому что кодировать и
    /// раскодировать его обязаны все машины одинаково.
    ///
    /// Цифры выбраны под речь и под сетевой кадр, а не под музыку:
    /// 16 кГц моно — это верхняя граница разборчивости голоса, а 40 мс —
    /// компромисс между задержкой (человек слышит собеседника «сразу») и
    /// числом пакетов в секунду. Сжатие ADPCM даёт 4 бита на отсчёт, то есть
    /// 324 байта на кадр вместе с заголовком — вчетверо меньше MTU, поэтому
    /// голос никогда не фрагментируется и уходит одним ненадёжным датаграммом.
    /// </summary>
    public static class VoiceFormat
    {
        /// <summary>Частота дискретизации голосового потока.</summary>
        public const int SampleRate = 16000;

        /// <summary>Отсчётов в одном сетевом кадре (40 мс).</summary>
        public const int FrameSamples = 640;

        /// <summary>Заголовок кадра: предиктор (2 байта) + шаг таблицы + резерв.</summary>
        public const int HeaderBytes = 4;

        /// <summary>Размер закодированного кадра целиком.</summary>
        public const int EncodedFrameBytes = HeaderBytes + FrameSamples / 2;

        /// <summary>Длительность кадра в секундах.</summary>
        public const float FrameSeconds = FrameSamples / (float)SampleRate;

        /// <summary>Сколько кадров в секунду уходит с говорящей машины.</summary>
        public const int FramesPerSecond = SampleRate / FrameSamples;

        /// <summary>Порядковый номер кадра, которым получатель отсеивает опоздавшие пакеты.</summary>
        public const int SequenceBytes = 2;

        /// <summary>Идентификатор говорящего в пакете «сервер → клиент».</summary>
        public const int SpeakerBytes = 8;

        /// <summary>Пакет клиент → сервер: номер кадра и звук.</summary>
        public const int UpstreamPacketBytes = SequenceBytes + EncodedFrameBytes;

        /// <summary>Пакет сервер → клиент: кто говорит, номер кадра и звук.</summary>
        public const int DownstreamPacketBytes = SpeakerBytes + UpstreamPacketBytes;
    }
}
