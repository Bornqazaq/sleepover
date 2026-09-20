using System;
using NUnit.Framework;
using Igruha.Core.Voice;

namespace Igruha.Tests
{
    /// <summary>
    /// Голос проверяется тестами потому, что вживую его не отладить: слышно
    /// «булькает» — и непонятно, кодек это, буфер, частота устройства или сеть.
    /// Здесь проверено всё, что можно проверить без микрофона и без сети.
    /// </summary>
    public sealed class VoiceChatTests
    {
        /// <summary>Речевой сигнал: основной тон и обертон, как у голоса.</summary>
        private static float[] Speech(int samples, float startPhase = 0f)
        {
            var data = new float[samples];
            for (int i = 0; i < samples; i++)
            {
                double t = (i + startPhase) / VoiceFormat.SampleRate;
                data[i] = (float)(0.55d * Math.Sin(2d * Math.PI * 220d * t) +
                                  0.2d * Math.Sin(2d * Math.PI * 660d * t));
            }

            return data;
        }

        private static float Rms(float[] data, int count)
        {
            double sum = 0d;
            for (int i = 0; i < count; i++) sum += data[i] * data[i];
            return (float)Math.Sqrt(sum / count);
        }

        [Test]
        public void Пакет_помещается_в_один_сетевой_кадр()
        {
            // Больше MTU — и голос поехал бы фрагментами, которых ненадёжная
            // доставка не собирает.
            Assert.Less(VoiceFormat.DownstreamPacketBytes, 1200,
                "Голосовой пакет обязан влезать в MTU целиком");
            Assert.AreEqual(324, VoiceFormat.EncodedFrameBytes);
            Assert.AreEqual(25, VoiceFormat.FramesPerSecond);
        }

        [Test]
        public void Кодек_возвращает_узнаваемую_речь()
        {
            float[] source = Speech(VoiceFormat.FrameSamples);
            var encoded = new byte[VoiceFormat.EncodedFrameBytes];
            var decoded = new float[VoiceFormat.FrameSamples];

            var state = new VoiceCodecState();
            int written = VoiceCodec.Encode(source, 0, source.Length, ref state, encoded);
            int samples = VoiceCodec.Decode(encoded, 0, written, decoded, 0);

            Assert.AreEqual(VoiceFormat.EncodedFrameBytes, written);
            Assert.AreEqual(VoiceFormat.FrameSamples, samples);

            var error = new float[VoiceFormat.FrameSamples];
            for (int i = 0; i < samples; i++) error[i] = source[i] - decoded[i];

            float signalToNoise = 20f * (float)Math.Log10(Rms(source, samples) / Math.Max(1e-6f, Rms(error, samples)));
            Assert.Greater(signalToNoise, 18f, "ADPCM обязан давать разборчивую речь, а не шум");
        }

        [Test]
        public void Потерянный_кадр_не_ломает_следующие()
        {
            // Голос ходит ненадёжной доставкой: каждый кадр обязан
            // декодироваться сам по себе, иначе первая же потеря зашипит навсегда.
            var state = new VoiceCodecState();
            var first = new byte[VoiceFormat.EncodedFrameBytes];
            var second = new byte[VoiceFormat.EncodedFrameBytes];
            var third = new byte[VoiceFormat.EncodedFrameBytes];

            VoiceCodec.Encode(Speech(VoiceFormat.FrameSamples), 0, VoiceFormat.FrameSamples, ref state, first);
            VoiceCodec.Encode(Speech(VoiceFormat.FrameSamples, VoiceFormat.FrameSamples), 0, VoiceFormat.FrameSamples, ref state, second);
            float[] lastSource = Speech(VoiceFormat.FrameSamples, VoiceFormat.FrameSamples * 2);
            VoiceCodec.Encode(lastSource, 0, VoiceFormat.FrameSamples, ref state, third);

            var afterLoss = new float[VoiceFormat.FrameSamples];
            VoiceCodec.Decode(third, 0, VoiceFormat.EncodedFrameBytes, afterLoss, 0);

            var error = new float[VoiceFormat.FrameSamples];
            for (int i = 0; i < VoiceFormat.FrameSamples; i++) error[i] = lastSource[i] - afterLoss[i];

            float signalToNoise = 20f * (float)Math.Log10(Rms(lastSource, VoiceFormat.FrameSamples) /
                                                          Math.Max(1e-6f, Rms(error, VoiceFormat.FrameSamples)));
            Assert.Greater(signalToNoise, 18f, "Кадр после потерянных обязан звучать так же чисто");
        }

        [Test]
        public void Буфер_молчит_пока_не_накопит_запас()
        {
            var buffer = new VoiceJitterBuffer(16000, VoiceFormat.FrameSamples * 2, VoiceFormat.FrameSamples * 8);
            var frame = Speech(VoiceFormat.FrameSamples);
            var output = new float[VoiceFormat.FrameSamples];

            buffer.Write(frame, 0, frame.Length);
            Assert.AreEqual(0, buffer.Read(output, 0, output.Length), "С одним кадром играть рано");
            Assert.IsTrue(buffer.Priming);

            buffer.Write(frame, 0, frame.Length);
            Assert.AreEqual(VoiceFormat.FrameSamples, buffer.Read(output, 0, output.Length));
            Assert.IsFalse(buffer.Priming);
        }

        [Test]
        public void Буфер_не_копит_задержку()
        {
            // Пакетов пришло больше, чем успели съесть: лучше щелчок, чем
            // разговор с секундным опозданием до конца катки.
            var buffer = new VoiceJitterBuffer(16000, VoiceFormat.FrameSamples * 2, VoiceFormat.FrameSamples * 8);
            var frame = Speech(VoiceFormat.FrameSamples);

            for (int i = 0; i < 40; i++) buffer.Write(frame, 0, frame.Length);

            Assert.LessOrEqual(buffer.Available, VoiceFormat.FrameSamples * 8,
                "Задержка обязана сбрасываться, а не накапливаться");
        }

        [Test]
        public void Опустевший_буфер_снова_копит_запас()
        {
            var buffer = new VoiceJitterBuffer(16000, VoiceFormat.FrameSamples * 2, VoiceFormat.FrameSamples * 8);
            var frame = Speech(VoiceFormat.FrameSamples);
            var output = new float[VoiceFormat.FrameSamples];

            buffer.Write(frame, 0, frame.Length);
            buffer.Write(frame, 0, frame.Length);
            buffer.Read(output, 0, output.Length);
            buffer.Read(output, 0, output.Length);

            Assert.IsTrue(buffer.Priming, "Опустев, буфер обязан снова набрать запас");
        }

        [Test]
        public void Пересчёт_частоты_не_уплывает_на_длинной_записи()
        {
            // Уплывшая на доли процента частота за минуту разговора
            // превращается в заметное отставание звука от губ.
            var resampler = new VoiceResampler(48000, VoiceFormat.SampleRate);
            var block = Speech(960);
            var output = new float[resampler.EstimateOutput(960)];

            int total = 0;
            for (int i = 0; i < 300; i++) total += resampler.Process(block, 0, block.Length, output, 0);

            int expected = 960 * 300 / 3;
            Assert.LessOrEqual(Math.Abs(total - expected), 2, "Длина потока обязана сойтись с частотой");
        }

        [Test]
        public void Пересчёт_частоты_сохраняет_тон()
        {
            var resampler = new VoiceResampler(48000, VoiceFormat.SampleRate);
            var block = new float[480];
            var output = new float[resampler.EstimateOutput(480) + 8];

            // Синус 400 Гц на 48 кГц: после пересчёта период обязан остаться
            // тем же по времени, то есть стать втрое короче по отсчётам.
            int zeroCrossings = 0;
            float previous = 0f;

            for (int b = 0; b < 20; b++)
            {
                for (int i = 0; i < block.Length; i++)
                {
                    double t = (b * block.Length + i) / 48000d;
                    block[i] = (float)Math.Sin(2d * Math.PI * 400d * t);
                }

                int produced = resampler.Process(block, 0, block.Length, output, 0);
                for (int i = 0; i < produced; i++)
                {
                    if (previous < 0f && output[i] >= 0f) zeroCrossings++;
                    previous = output[i];
                }
            }

            // 20 блоков по 10 мс = 0.2 с, 400 Гц дают 80 периодов.
            Assert.AreEqual(80, zeroCrossings, 2, "После пересчёта тон обязан остаться прежним");
        }

        [Test]
        public void Порог_не_рубит_речь_на_паузах_между_словами()
        {
            var detector = new VoiceActivityDetector { Threshold = 0.02f, ReleaseSeconds = 0.4f };

            Assert.IsTrue(detector.Evaluate(0.05f, VoiceFormat.FrameSeconds), "Громкий звук открывает канал");

            // Пауза между словами — четверть секунды тишины.
            for (int i = 0; i < 6; i++) detector.Evaluate(0.001f, VoiceFormat.FrameSeconds);
            Assert.IsTrue(detector.IsOpen, "Короткая пауза не обязана обрывать фразу");

            for (int i = 0; i < 6; i++) detector.Evaluate(0.001f, VoiceFormat.FrameSeconds);
            Assert.IsFalse(detector.IsOpen, "После паузы длиннее удержания канал закрывается");
        }

        [Test]
        public void Тихий_фон_не_уходит_в_эфир()
        {
            var detector = new VoiceActivityDetector { Threshold = 0.02f };
            for (int i = 0; i < 50; i++)
            {
                Assert.IsFalse(detector.Evaluate(0.004f, VoiceFormat.FrameSeconds),
                    "Дыхание и гул комнаты обязаны оставаться при своём хозяине");
            }
        }
    }
}
