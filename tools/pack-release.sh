#!/usr/bin/env bash
#
# Раздаточные архивы для друзей: Mac и Windows одним запуском.
#
# Отдельно от стенда (autorun-*.sh) намеренно: стенд собирается в режиме
# Development — со счётчиком кадров, профайлером и открытым портом отладчика.
# Раздать такой билд значит раздать не игру, а стенд.
#
# Запускать с закрытым редактором Unity: два инстанса на один проект
# одновременно не поднимаются.
#
# Использование:
#   tools/pack-release.sh            # собрать обе платформы и упаковать
#   tools/pack-release.sh --no-build # только упаковать уже собранное
#
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"

# Редактор лежит по-разному на двух машинах команды, а раздачу собирают с обеих.
# Прежде здесь был жёстко прописан маковый путь, и на Windows скрипт падал на
# первой же строке сборки — притом молча, потому что несуществующий бинарник
# набегает на ту же ошибку, что и занятый редактор.
case "$(uname -s)" in
    MINGW*|MSYS*|CYGWIN*)
        UNITY_DEFAULT="/c/Program Files/Unity/Hub/Editor/6000.3.11f1-x86_64/Editor/Unity.exe"
        UNITY_RUNNING="Unity.exe"
        ;;
    *)
        UNITY_DEFAULT="$HOME/Unity/Hub/Editor/6000.3.11f1/Unity.app/Contents/MacOS/Unity"
        UNITY_RUNNING="Unity.app/Contents/MacOS/Unity"
        ;;
esac
UNITY="${UNITY:-$UNITY_DEFAULT}"
RELEASE="$ROOT/igruha/Builds/Release"
STAGE="$RELEASE/dist"
README="$ROOT/tools/dist/README-RU.txt"
STAMP="$(date +%Y%m%d)"

if [[ "${1:-}" != "--no-build" ]]; then
    [[ -x "$UNITY" ]] || { echo "Редактор не найден: $UNITY (задай путь через UNITY=...)" >&2; exit 1; }

    if pgrep -f "$UNITY_RUNNING" >/dev/null 2>&1        || tasklist 2>/dev/null | grep -qi "^Unity\.exe"; then
        echo "Unity открыт — закрой редактор, иначе сборка не стартует." >&2
        exit 1
    fi

    # Две отдельные сборки, а не одна на два вызова: платформу Unity выбирает
    # при запуске аргументом -buildTarget. Собирать вторую платформу внутри
    # того же процесса значит переключать активную платформу на ходу — так
    # первая попытка молча собрала Windows против движка macOS и оставила
    # папку пустой, без ошибки в логе.
    build_one() {
        local target="$1" method="$2" label="$3" log="$RELEASE/build-$3.log"

        echo "▶ Собираю $label (несколько минут, при смене платформы — дольше)…"
        "$UNITY" -batchmode -nographics -quit -projectPath "$ROOT/igruha" \
                 -buildTarget "$target" \
                 -executeMethod "Igruha.EditorTools.ReleaseBuild.$method" \
                 -logFile "$log" || {
            echo "Сборка $label не прошла, разбор в $log" >&2
            grep -aE "\[Билд\]|error CS" "$log" | tail -20 >&2 || true
            exit 1
        }
        grep -aE "^\[Билд\]" "$log" || true
    }

    mkdir -p "$RELEASE"

    # Собрать под чужую платформу можно только с её модулем поддержки. На ПК
    # ASRock стоит один windowsstandalonesupport, и попытка собрать Mac там
    # раньше оставляла пустую папку без внятной ошибки в логе. Проверяем явно
    # и говорим, чего не хватает, — молчаливого провала быть не должно.
    UNITY_DIR="$(dirname "$UNITY")"
    if [[ -d "$UNITY_DIR/Data/PlaybackEngines/MacStandaloneSupport"        || -d "$UNITY_DIR/../PlaybackEngines/MacStandaloneSupport"        || "$(uname -s)" == "Darwin" ]]; then
        build_one OSXUniversal BuildMac mac
    else
        MAC_SKIPPED=1
        echo "⚠ Модуля Mac Build Support нет — сборка под macOS пропущена."
        echo "  Ставится в Unity Hub: Installs → 6000.3.11f1 → Add modules → Mac Build Support (Mono)."
    fi

    build_one Win64 BuildWindows windows
fi

[[ -f "$RELEASE/Windows/Komnata.exe" ]] || { echo "Нет $RELEASE/Windows/Komnata.exe" >&2; exit 1; }
if [[ ! -d "$RELEASE/Mac/Komnata.app" ]]; then
    [[ -n "${MAC_SKIPPED:-}" ]] || echo "⚠ Нет $RELEASE/Mac/Komnata.app — macOS не упакуем."
    MAC_SKIPPED=1
fi


# Архиватор. `zip` есть на macOS из коробки, а в Git Bash на Windows его нет
# вовсе — и упаковка падала там уже после часовой сборки, что особенно обидно.
# Питон есть на обеих машинах, его zipfile умеет zip64, а раздача под полтора
# гигабайта в обычный zip просто не поместилась бы.
make_zip() {
    local src="$1" dest="$2"

    if command -v zip >/dev/null 2>&1; then
        ( cd "$src" && zip -qry "$dest" . )
        return
    fi

    python - "$src" "$dest" <<'PYZIP'
import os, sys, zipfile
src, dest = sys.argv[1], sys.argv[2]
with zipfile.ZipFile(dest, "w", zipfile.ZIP_DEFLATED, allowZip64=True) as z:
    for root, _, files in os.walk(src):
        for name in files:
            full = os.path.join(root, name)
            z.write(full, os.path.relpath(full, src))
PYZIP
}

rm -rf "$STAGE"
mkdir -p "$STAGE"

if [[ -z "${MAC_SKIPPED:-}" ]]; then
    echo "▶ Упаковываю macOS…"
    mkdir -p "$STAGE/mac"
    cp -R "$RELEASE/Mac/Komnata.app" "$STAGE/mac/"
    cp "$README" "$STAGE/mac/ЧИТАТЬ-ПЕРВЫМ.txt"
    # Карантин снимается здесь, а не у друга: иначе macOS встречает его
    # окном «программа повреждена», и дальше этого окна вечер не идёт.
    xattr -cr "$STAGE/mac/Komnata.app" 2>/dev/null || true
    make_zip "$STAGE/mac" "$RELEASE/Komnata-mac-$STAMP.zip"
fi

echo "▶ Упаковываю Windows…"
mkdir -p "$STAGE/windows"
cp -R "$RELEASE/Windows/." "$STAGE/windows/"
cp "$README" "$STAGE/windows/ЧИТАТЬ-ПЕРВЫМ.txt"
make_zip "$STAGE/windows" "$RELEASE/Komnata-windows-$STAMP.zip"

rm -rf "$STAGE"

echo
echo "Готово:"
ls -lh "$RELEASE"/Komnata-*-"$STAMP".zip | awk '{print "  " $9 "  " $5}'
[[ -n "${MAC_SKIPPED:-}" ]] && echo "  (архива под macOS нет — не тем компьютером собрано)"
echo
echo "Адрес для друзей (Tailscale):"
TS="$(command -v tailscale || echo "/c/Program Files/Tailscale/tailscale.exe")"
if [[ -x "$TS" ]]; then
    "$TS" ip -4 2>/dev/null | sed 's/^/  /' || echo "  Tailscale не отвечает — проверь, что он запущен."
else
    echo "  Tailscale не установлен на этой машине — друзья до неё не достучатся."
fi
