# «Комары»: IGR-627, IGR-630, IGR-631, IGR-632

Проверки 27–28 сентября 2026, ветка `codex/mosquitoes-implementation`.
Unity 6000.3.11f1, Windows Development Build. Изменения локальные.
Билд: `igruha/Builds/Autotest/IGR-Network/sleepover.exe`.
Последняя сборка через Unity MCP: `build-cf308aee31`, Succeeded,
12,8 с, errors=0, warnings=0.

## Что исправлено

- IGR-630: серверная проверка стен/потолка на FixedUpdate, возврат к дальней
  стене через owner NetworkTransform.Teleport, свободные от мебели recovery
  точки для семи комаров. CharacterIndex записывается после сетевого spawn.
- IGR-631: ушедший комар получает место своей команды; отключённые тела
  удаляются из реестра на сервере и клиентах. Giant disconnect завершает
  раунд победой комаров; последний mosquito disconnect — победой Гиганта.
- IGR-631: устранено истощение UDP receive pool после аварийного закрытия
  клиента на Windows. Встроена прежняя версия Unity Transport 2.6.0 с одной
  правкой: освобождение буфера неуспешного receive completion. Обоснование,
  подтверждение Unity и порядок снятия патча —
  [UPSTREAM-PATCH.md](../../../igruha/Packages/com.unity.transport/UPSTREAM-PATCH.md).
- IGR-632: клиент применяет заключительный snapshot после очистки раунда;
  Sleep теперь совпадает и в результатах. Сервер публикует состояние после
  окончания и очищает Living.

## Проверки редактора

12 EditMode правил — Passed. Исходные 8 PlayMode тестов сцены — Passed;
добавленные три варианта ролей — 3/3 Passed. В совокупности покрыты обе
роли при 3, 4 и 8 игроках, оба исхода для каждого состава, один/ноль
живых комаров, настоящие ResultsReported и загрузка Hub с управлением.
После правок отдельно повторены границы (1/1) и отключения (2/2).

Ошибок компиляции и сообщений ошибок от кода «Комаров» нет. В редакторе
есть предупреждения тестового AudioListener и URP shadow atlas. Один запуск
Unity test bridge не инициализировался после domain reload; повторный запуск
по group filter прошёл. Это не падение теста игрового кода.

## Сетевые процессы

JSON рядом содержит выборку сообщений без стеков Unity, состояние каждые
две секунды, движение/видимость/жужжание и сравнение результатов.
Полные логи находятся в указанном в JSON каталоге `Builds/.../logs`.

| Доказательство | Результат |
|---|---|
| [flight-boundaries.json](flight-boundaries.json) | Host + Client: клиент обошёл локальный ограничитель и отправил X=-5, Y=2,76, Z=5. Все три позиции исправлены сервером. Одинаковые места/очки, Sleep=60, bodies=0, Hub и управление на обоих процессах. |
| [round-sleep.json](round-sleep.json) | Host + 3 Client: обычный раунд до 60 с сна, результаты примерно на 69,3 с после Round с учётом countdown/пробуждений. Места/очки совпали, Hub и управление восстановлены у всех. |
| [round-timeout.json](round-timeout.json) | Host + 3 Client: настоящий таймер 120 с, без сокращения; после 3 с countdown итог примерно на elapsed=123. Комары победили, места/очки совпали, Hub и управление у всех. |
| [giant-crash.json](giant-crash.json) | Giant client принудительно закрыт на elapsed≈6. Через штатный timeout≈30 с сервер завершил раунд; оба оставшихся комара получили первое место, связь/Hub/управление сохранились. |
| [mosquito-crash-continues.json](mosquito-crash-continues.json) | Первый mosquito client принудительно закрыт. На сервере и оставшемся клиенте alive=1, bodies=1; раунд продолжился до обычного исхода. |
| [mosquitoes-crash-sequential.json](mosquitoes-crash-sequential.json) | Оба mosquito client последовательно принудительно закрыты. После первого обрыва оставшийся получил alive=1, bodies=1; после второго сервер завершил раунд при alive=0. Места 0:1, 1:3, 2:3; Hub и управление у хоста восстановлены. |
| [host-crash.json](host-crash.json) | Host принудительно закрыт в Round. Общий DisconnectionHandler клиента получил ClientStopped и вызвал загрузку Hub; исключений нет. Вход в новую сессию отдельно не проверялся. |

В полном прогоне по таймеру максимальная разница выборок Sleep — 0,12 с.
У третьего клиента три выборки переходов фаз отличаются при разнице
времени выборок 0,10–0,15 с; устойчивого расхождения нет. В прогоне сна
все 35 сопоставленных выборок ролей/фазы/лампы/числа живых совпали, разница
Sleep не превышает 0,10 с. Финальные состояния и строки мест/очков совпадают
точно. Выборки не являются замером плавности рендера или слуховой приёмкой.

На всех удалённых телах Rigidbody kinematic=True, interpolation=None.
Погибшие тела имеют visible=0; живые тела комары видят по своей роли,
Гигант — по кругу лампы. Клиенты-комары отправляли чужие bed/swat ServerRpc
в прогоне по таймеру; это не изменило авторитетный исход.

## Повтор

```powershell
.\tools\autorun-mosquitoes.ps1 -Players 2 -Giant 0 -Scenario flight-boundaries -BuildDirectory 'igruha\Builds\Autotest\IGR-Network'
.\tools\autorun-mosquitoes.ps1 -Players 4 -Giant 1 -Scenario observe -BuildDirectory 'igruha\Builds\Autotest\IGR-Network' -Port 17887
.\tools\autorun-mosquitoes.ps1 -Players 4 -Giant 0 -Scenario round-timeout -BuildDirectory 'igruha\Builds\Autotest\IGR-Network' -Port 17885
python tools/report-mosquitoes-network.py <каталог-логов> --output <отчёт.json>
```

Сценарии sleep/timeout — сокращённые fixtures, их нельзя выдавать за полный
таймер. Для полного таймера предназначен round-timeout: отключён только
Giant bot, пробуждение проходит через проверенное серверное действие у кровати.
Логи пишутся только при явном --mosquito-check в development build.

## Оставшаяся приёмка IGR-632

Локальные процессы запущены с `-batchmode -nographics`. Требуются живой
прогон на 3+ разных машинах и визуальная/слуховая оценка темноты, круга
лампы, плавности и жужжания. Это прямо указано в IGR-632 и в
[mg-net](../../../.claude/skills/mg-net/SKILL.md): «Стенд не заменяет живой прогон».
Состояние, флаги рендера и очистка на локальном стенде проверены; живую
приёмку стендом не подменяем. IGR-632 остаётся In Progress.
