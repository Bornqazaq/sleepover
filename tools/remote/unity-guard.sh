#!/usr/bin/env bash
# unity-guard.sh — macOS-двойник tools/remote/unity-guard.ps1.
#
# Зачем: 03.09 редактор встал на старте, окно ждало нажатия, а агент этого
# окна не видел и не мог нажать — ждал десять минут, пока геймдизайнер не
# сделал руками. На Windows для этого давно есть unity-guard.ps1, на маке
# не было ничего. Это оно.
#
# Что важно знать про Unity на маке, иначе инструмент бесполезен:
#
#   1. Редактор НЕ отдаёт свои окна в Accessibility — он рисует UI сам.
#      `windows` покажет у процесса Unity ноль окон, и это норма, а не сбой.
#      Ловятся только НАТИВНЫЕ диалоги (NSAlert): лицензия, Safe Mode,
#      «project is already open», крэш-репорт. Ровно они и блокируют старт.
#
#   2. Признак «завис» — НЕ отсутствие окна и не низкий CPU сам по себе.
#      Замер 03.09: редактор молчал на licensing десять минут при 0 % CPU
#      и поднялся сам. Признак — лог, который не растёт, И низкий CPU
#      одновременно, замеренные за интервал. Это делает `status`.
#
#   3. Клик работает через System Events и требует Accessibility. Проверка
#      прав — `status`, строка «права на управление».
#
# Три грабли, на которых этот скрипт уже сломался — не наступать заново:
#
#   * Фильтр `every process whose background only is false` отсекает osascript,
#     а половина системных диалогов принадлежит именно фоновым процессам.
#     Перебирать надо ВСЕ процессы, обернув каждый в try: к части из них
#     Accessibility не пускает и роняет весь скрипт.
#   * `named` как имя переменной AppleScript не компилируется — слово занято
#     под свойства. Здесь переменная зовётся blist.
#   * В heredoc без кавычек `«$btn»` bash забирает ёлочку в имя переменной
#     и падает с unbound variable. Только `${btn}`.
#
# Команды:
#   status              состояние: процессы, CPU, рост лога, права, вердикт
#   windows             окна всех процессов Unity (обычно пусто — см. п.1)
#   dialogs             нативные диалоги где угодно, с их кнопками
#   read                текст блокирующего диалога целиком
#   click <кнопка>      нажать кнопку по имени в блокирующем диалоге
#   key <return|escape> послать клавишу активному диалогу
#   shot [путь]         скриншот экрана
#   start               поднять редактор на этом проекте
#   stop                закрыть редактор (TERM, потом KILL)
#
# Диалоги ищутся у ЛЮБОГО процесса, а не только у Unity: лицензионное окно
# принадлежит Unity Hub, крэш-репорт — отдельному процессу, а сам osascript
# фоновый, и фильтр «только не фоновые» отсекал бы половину нужного.
#
# Диалогом считается окно с ИМЕНОВАННЫМИ кнопками. Без этого условия в выдачу
# лезет меню-бар Tailscale: у него три кнопки, все без имени.

set -uo pipefail

REPO="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"

# Сколько секунд считаем стартом. Дольше — редактор уже не стартует, а стоит:
# холодный старт этого проекта укладывается в минуту с небольшим даже после
# полного импорта.
STARTUP_GRACE=${STARTUP_GRACE:-180}
PROJECT="$REPO/igruha"
EDITOR_APP="${UNITY_EDITOR_APP:-$HOME/Unity/Hub/Editor/6000.3.11f1/Unity.app}"
LOG="$HOME/Library/Logs/Unity/Editor.log"
SAMPLE_SECONDS="${UNITY_GUARD_SAMPLE:-6}"

die() { echo "$*" >&2; exit 1; }

editor_pid() {
    # Только сам редактор: не Hub, не лицензионный клиент, не UnityPackageManager,
    # и не наши собственные шеллы, у которых путь редактора попал в командную строку.
    pgrep -f "Unity\.app/Contents/MacOS/Unity" 2>/dev/null | while read -r p; do
        cmd=$(ps -o command= -p "$p" 2>/dev/null)
        case "$cmd" in
            *"/bin/"*|*zsh*|*bash*|*pgrep*) continue ;;
            *"Contents/MacOS/Unity "*|*"Contents/MacOS/Unity") echo "$p"; return ;;
        esac
    done
}

# Нативные диалоги: окна, у которых есть кнопки. У AXWindow обычного окна
# кнопок в этом смысле нет, поэтому фильтр отсекает рабочие окна приложений.
dialog_script() {
    cat <<'AS'
tell application "System Events"
	set out to ""
	repeat with p in (every process)
		try
			set pn to name of p
			repeat with w in (every window of p)
				try
					set names to ""
					repeat with b in (every button of w)
						try
							set bn to name of b
							if bn is not missing value then set names to names & "[" & bn & "]"
						end try
					end repeat
					if names is not "" then
						set out to out & pn & " | окно «" & (name of w) & "» | кнопки " & names & linefeed
					end if
				end try
			end repeat
		end try
	end repeat
	if out is "" then return "блокирующих диалогов не видно"
	return out
end tell
AS
}

cmd_status() {
    local pid before after cpu grew trusted
    pid="$(editor_pid)"
    trusted=$(osascript -e 'tell application "System Events" to return (UI elements enabled)' 2>/dev/null)

    echo "проект:              $PROJECT"
    echo "права на управление: ${trusted:-НЕТ (включить Accessibility для терминала)}"

    if [ -z "$pid" ]; then
        echo "редактор:            не запущен"
        [ -f "$PROJECT/Temp/UnityLockfile" ] && echo "лок:                 остался от прошлого запуска"
        return 0
    fi

    before=$(wc -l < "$LOG" 2>/dev/null || echo 0)
    sleep "$SAMPLE_SECONDS"
    after=$(wc -l < "$LOG" 2>/dev/null || echo 0)
    cpu=$(ps -o %cpu= -p "$pid" 2>/dev/null | tr -d ' ')
    grew=$(( after - before ))

    echo "редактор:            pid $pid, CPU ${cpu:-?} %"
    echo "лог:                 $after строк, за ${SAMPLE_SECONDS} с прибавилось $grew"

    local socks dlg
    socks=$(listening_count "$pid")
    dlg=$(dialog_script | osascript 2>/dev/null)
    echo "сокетов слушает:     $socks"

    # Вердикт по трём независимым признакам, а не по одному CPU: простаивающий
    # редактор и заблокированный выглядят одинаково, если смотреть только
    # на загрузку и лог.
    if [ "${dlg:-}" != "блокирующих диалогов не видно" ] && [ -n "${dlg:-}" ]; then
        echo "вердикт:             БЛОКИРОВАН ДИАЛОГОМ — нажать кнопку:"
        echo "$dlg" | sed 's/^/                     /'
        echo "                     жать так: tools/remote/unity-guard.sh click «имя кнопки»"
    elif [ "$socks" -eq 0 ] && [ "$(uptime_seconds "$pid")" -gt "$STARTUP_GRACE" ]; then
        # ⚠️ Сокетов нет, но редактор живёт дольше любого разумного старта —
        # значит он не стартует, а СТОИТ. Почти всегда это собственный
        # модальный диалог Unity (EditorUtility.DisplayDialog).
        #
        # Его не видит ни dialogs, ни click: Unity рисует такие окна своим
        # IMGUI, и для accessibility-API их кнопок не существует. Больше того,
        # заблокированный редактор не отвечает и на activate, и список его
        # окон пуст — прокачивать события ему нечем. Снять окно можно только
        # рукой.
        #
        # Стоило десяти минут 03.09: агент увидел «ЕЩЁ СТАРТУЕТ», поверил
        # и ждал, пока геймдизайнер не показал скриншот с диалогом запекания.
        echo "вердикт:             СТОИТ, А НЕ СТАРТУЕТ — живёт $(uptime_seconds "$pid") с без сокетов"
        echo "                     Почти наверняка собственный модальный диалог Unity."
        echo "                     Его НЕ видно через dialogs/click: это IMGUI, а не окно macOS,"
        echo "                     и заблокированный редактор не отвечает даже на activate."
        echo "                     Посмотреть глазами и нажать рукой — снять программно нельзя."
    elif [ "$socks" -eq 0 ]; then
        echo "вердикт:             ЕЩЁ СТАРТУЕТ — сокеты не открыты, моста нет"
        echo "                     Ждать, а не перезапускать: повторный запуск ничего не ускоряет."
        echo "                     Если через $STARTUP_GRACE с не поднимется — вердикт сменится"
        echo "                     на «СТОИТ»: это уже диалог, а не старт"
    elif [ "$grew" -gt 0 ] || [ "${cpu%%.*}" -ge 20 ] 2>/dev/null; then
        echo "вердикт:             РАБОТАЕТ"
    else
        echo "вердикт:             ПРОСТАИВАЕТ — это норма, редактор поднят и ждёт команд"
    fi
    echo "--- хвост лога ---"
    tail -3 "$LOG" 2>/dev/null | cut -c1-160
}

cmd_windows() {
    osascript <<'AS' 2>&1
tell application "System Events"
  set out to ""
  repeat with p in (every process whose name contains "Unity")
    set out to out & (name of p) & ": окон " & (count of windows of p) & linefeed
    repeat with w in (every window of p)
      set out to out & "    «" & (name of w) & "»" & linefeed
    end repeat
  end repeat
  return out & "(ноль окон у процесса Unity — норма: редактор не отдаёт их в Accessibility)"
end tell
AS
}

cmd_dialogs() { dialog_script | osascript 2>&1; }

# Слушающие сокеты редактора. Признак «старт закончился»: застрявший на
# лицензии Unity их ещё не открыл, а поднявшийся держит мост MCP и прочее.
listening_count() {
    local pid="$1"
    lsof -nP -iTCP -sTCP:LISTEN -a -p "$pid" 2>/dev/null | grep -c LISTEN
}

cmd_read() {
    osascript <<'AS' 2>&1
tell application "System Events"
	repeat with p in (every process)
		try
			set pn to name of p
			repeat with w in (every window of p)
				try
					set blist to ""
					repeat with b in (every button of w)
						try
							set bn to name of b
							if bn is not missing value then set blist to blist & "[" & bn & "]"
						end try
					end repeat
					if blist is not "" then
						set txt to ""
						repeat with t in (every static text of w)
							try
								set tv to value of t
								if tv is not missing value then set txt to txt & tv & linefeed
							end try
						end repeat
						return "процесс: " & pn & linefeed & "кнопки: " & blist & linefeed & "--- текст ---" & linefeed & txt
					end if
				end try
			end repeat
		end try
	end repeat
	return "блокирующих диалогов не видно"
end tell
AS
}

cmd_click() {
    local btn="${1:-}"
    [ -n "$btn" ] || die "нужно имя кнопки: unity-guard.sh click ОК"
    osascript <<AS 2>&1
tell application "System Events"
	repeat with p in (every process)
		try
			repeat with w in (every window of p)
				try
					set target to (first button of w whose name is "${btn}")
					click target
					return "нажал ${btn} у процесса " & (name of p)
				end try
			end repeat
		end try
	end repeat
	return "кнопка ${btn} не найдена ни в одном окне"
end tell
AS
}

# Сколько секунд живёт процесс. Нужно, чтобы отличить долгий старт от
# редактора, вставшего на диалоге: снаружи они выглядят одинаково.
uptime_seconds() {
    ps -o etime= -p "$1" 2>/dev/null | tr -d ' ' | awk -F'[-:]' '{
        if (NF == 4) print $1*86400 + $2*3600 + $3*60 + $4;
        else if (NF == 3) print $1*3600 + $2*60 + $3;
        else if (NF == 2) print $1*60 + $2;
        else print 0
    }'
}

cmd_key() {
    local k="${1:-return}" code
    case "$k" in
        return|enter) code=36 ;;
        escape|esc)   code=53 ;;
        space)        code=49 ;;
        *) die "поддерживаются return, escape, space" ;;
    esac
    osascript -e "tell application \"System Events\" to key code $code" 2>&1 \
        && echo "послал $k"
}

cmd_shot() {
    local out="${1:-$REPO/../unity-guard-shot.png}"
    screencapture -x "$out" && echo "скриншот: $out"

    # Снимок берёт весь экран, а Unity может быть закрыт другими окнами —
    # тогда на картинке будет редактор кода, а не диалог, который ищут.
    local front
    front=$(osascript -e 'tell application "System Events" to get name of first process whose frontmost is true' 2>/dev/null)
    case "$front" in
        Unity|"") ;;
        *) echo "ВНИМАНИЕ: впереди «${front}», Unity закрыт им — на снимке диалога может не быть" ;;
    esac
}

cmd_start() {
    [ -d "$EDITOR_APP" ] || die "редактор не найден: $EDITOR_APP (переопредели UNITY_EDITOR_APP)"
    [ -n "$(editor_pid)" ] && { echo "редактор уже запущен, pid $(editor_pid)"; return 0; }
    rm -f "$PROJECT/Temp/UnityLockfile"
    open -a "$EDITOR_APP" --args -projectPath "$PROJECT"
    echo "запущен. Старт занимает до десяти минут — следи через status, не перезапускай"
}

cmd_stop() {
    local pid; pid="$(editor_pid)"
    [ -n "$pid" ] || { echo "редактор не запущен"; return 0; }
    kill -TERM "$pid"
    for _ in $(seq 1 30); do
        sleep 1
        [ -z "$(editor_pid)" ] && { echo "закрылся"; return 0; }
    done
    kill -KILL "$pid" 2>/dev/null && echo "не ответил на TERM, закрыт KILL"
}

case "${1:-status}" in
    status)  cmd_status ;;
    windows) cmd_windows ;;
    dialogs) cmd_dialogs ;;
    read)    cmd_read ;;
    click)   shift; cmd_click "$@" ;;
    key)     shift; cmd_key "$@" ;;
    shot)    shift; cmd_shot "$@" ;;
    start)   cmd_start ;;
    stop)    cmd_stop ;;
    *) sed -n '2,40p' "${BASH_SOURCE[0]}" | sed 's/^# \{0,1\}//' ;;
esac
