using System;
using UnityEngine;

namespace Igruha.Minigames.Exam
{
    /// <summary>
    /// Готовые вопросы. Закрывают сразу две дыры: на геймпаде восемьдесят
    /// символов за тридцать секунд не набрать, и отдельно — человек может
    /// просто не придумать вопрос.
    ///
    /// <b>Верного варианта заготовка не содержит.</b> Его Ведущий отмечает
    /// сам, как и в своём вопросе: иначе игра перестала бы быть про угадывание
    /// конкретного человека.
    ///
    /// Вопросы обязаны быть <b>субъективными</b>. Фактический вопрос ломает
    /// механику: «Экзамен» про то, как думает Ведущий, а не про то, что он
    /// знает.
    /// </summary>
    [CreateAssetMenu(menuName = "Igruha/Exam Question Presets", fileName = "ExamQuestionPresets")]
    public sealed class ExamQuestionPresets : ScriptableObject
    {
        [Serializable]
        public struct Preset
        {
            [TextArea(1, 2)] public string Question;
            public string OptionA;
            public string OptionB;
        }

        [SerializeField]
        private Preset[] presets =
        {
            new Preset { Question = "Что хуже — опоздать на час или не прийти вообще?", OptionA = "Опоздать", OptionB = "Не прийти" },
            new Preset { Question = "Пельмени со сметаной или с майонезом?", OptionA = "Сметана", OptionB = "Майонез" },
            new Preset { Question = "Кто из нас первым женится?", OptionA = "Точно не я", OptionB = "Сто процентов я" },
            new Preset { Question = "Ананас на пицце — это преступление?", OptionA = "Преступление", OptionB = "Вкусно" },
            new Preset { Question = "Душ утром или вечером?", OptionA = "Утром", OptionB = "Вечером" },
            new Preset { Question = "Что страшнее — выступать перед залом или идти к стоматологу?", OptionA = "Зал", OptionB = "Стоматолог" },
            new Preset { Question = "Кофе или чай, если выбирать навсегда?", OptionA = "Кофе", OptionB = "Чай" },
            new Preset { Question = "Читать книгу или смотреть экранизацию?", OptionA = "Книга", OptionB = "Фильм" },
            new Preset { Question = "Кто в этой комнате хуже всех водит?", OptionA = "Сидящий слева", OptionB = "Сидящий справа" },
            new Preset { Question = "Отпуск в горах или на море?", OptionA = "Горы", OptionB = "Море" },
            new Preset { Question = "Что важнее в друге — честность или доброта?", OptionA = "Честность", OptionB = "Доброта" },
            new Preset { Question = "Сова или жаворонок?", OptionA = "Сова", OptionB = "Жаворонок" },
            new Preset { Question = "Позвонить или написать?", OptionA = "Позвонить", OptionB = "Написать" },
            new Preset { Question = "Что бесит сильнее — чавканье или опоздания?", OptionA = "Чавканье", OptionB = "Опоздания" },
            new Preset { Question = "Кошки или собаки?", OptionA = "Кошки", OptionB = "Собаки" },
            new Preset { Question = "Сериал залпом или по серии в неделю?", OptionA = "Залпом", OptionB = "По серии" },
            new Preset { Question = "Лучше переплатить или стоять в очереди?", OptionA = "Переплатить", OptionB = "Стоять" },
            new Preset { Question = "Что хуже — потерять телефон или потерять ключи?", OptionA = "Телефон", OptionB = "Ключи" },
            new Preset { Question = "Готовить самому или заказывать?", OptionA = "Готовить", OptionB = "Заказывать" },
            new Preset { Question = "Кто первым сдастся в споре?", OptionA = "Тот, кто громче", OptionB = "Тот, кто молчит" },
            new Preset { Question = "Сладкое или солёное?", OptionA = "Сладкое", OptionB = "Солёное" },
            new Preset { Question = "Что лучше — знать правду или спать спокойно?", OptionA = "Правда", OptionB = "Спать спокойно" },
            new Preset { Question = "Отвечать сразу или обдумать сутки?", OptionA = "Сразу", OptionB = "Через сутки" },
            new Preset { Question = "Зима или лето?", OptionA = "Зима", OptionB = "Лето" },
            new Preset { Question = "Что раздражает больше — спойлеры или реклама?", OptionA = "Спойлеры", OptionB = "Реклама" },
            new Preset { Question = "Сидеть у окна или у прохода?", OptionA = "У окна", OptionB = "У прохода" },
            new Preset { Question = "Признаться в ошибке сразу или тихо исправить?", OptionA = "Признаться", OptionB = "Исправить" },
            new Preset { Question = "Что важнее — деньги или свободное время?", OptionA = "Деньги", OptionB = "Время" },
            new Preset { Question = "Кто в компании самый жадный?", OptionA = "Он это отрицает", OptionB = "Он этим гордится" },
            new Preset { Question = "Играть, чтобы победить, или чтобы посмеяться?", OptionA = "Победить", OptionB = "Посмеяться" },
            new Preset { Question = "Что хуже — громкий сосед или холодная батарея?", OptionA = "Сосед", OptionB = "Батарея" },
            new Preset { Question = "Планировать поездку или ехать наугад?", OptionA = "Планировать", OptionB = "Наугад" }
        };

        public int Count => presets != null ? presets.Length : 0;

        public Preset Get(int index)
        {
            if (presets == null || presets.Length == 0)
            {
                return new Preset { Question = "Вопрос не задан", OptionA = "А", OptionB = "Б" };
            }

            return presets[Mathf.Clamp(index, 0, presets.Length - 1)];
        }

        /// <summary>
        /// Случайная заготовка. Зовётся у авторитета: в сетевой фазе выбор
        /// болванки обязан быть серверным, как и любой другой рандом.
        /// </summary>
        public Preset GetRandom() => Get(UnityEngine.Random.Range(0, Count));
    }
}
