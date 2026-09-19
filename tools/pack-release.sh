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
UNITY="${UNITY:-$HOME/Unity/Hub/Editor/6000.3.11f1/Unity.app/Contents/MacOS/Unity}"
RELEASE="$ROOT/igruha/Builds/Release"
STAGE="$RELEASE/dist"
README="$ROOT/tools/dist/README-RU.txt"
STAMP="$(date +%Y%m%d)"

if [[ "${1:-}" != "--no-build" ]]; then
    if pgrep -f "Unity.app/Contents/MacOS/Unity" >/dev/null 2>&1; then
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
    build_one OSXUniversal BuildMac mac
    build_one Win64 BuildWindows windows
fi

[[ -d "$RELEASE/Mac/Komnata.app" ]] || { echo "Нет $RELEASE/Mac/Komnata.app" >&2; exit 1; }
[[ -f "$RELEASE/Windows/Komnata.exe" ]] || { echo "Нет $RELEASE/Windows/Komnata.exe" >&2; exit 1; }

rm -rf "$STAGE"
mkdir -p "$STAGE"

echo "▶ Упаковываю macOS…"
mkdir -p "$STAGE/mac"
cp -R "$RELEASE/Mac/Komnata.app" "$STAGE/mac/"
cp "$README" "$STAGE/mac/ЧИТАТЬ-ПЕРВЫМ.txt"
# Карантин снимается здесь, а не у друга: иначе macOS встречает его
# окном «программа повреждена», и дальше этого окна вечер не идёт.
xattr -cr "$STAGE/mac/Komnata.app" 2>/dev/null || true
( cd "$STAGE/mac" && zip -qry "$RELEASE/Komnata-mac-$STAMP.zip" . )

echo "▶ Упаковываю Windows…"
mkdir -p "$STAGE/windows"
cp -R "$RELEASE/Windows/." "$STAGE/windows/"
cp "$README" "$STAGE/windows/ЧИТАТЬ-ПЕРВЫМ.txt"
( cd "$STAGE/windows" && zip -qry "$RELEASE/Komnata-windows-$STAMP.zip" . )

rm -rf "$STAGE"

echo
echo "Готово:"
ls -lh "$RELEASE"/Komnata-*-"$STAMP".zip | awk '{print "  " $9 "  " $5}'
echo
echo "Адрес для друзей (Tailscale):"
tailscale ip -4 2>/dev/null | sed 's/^/  /' || echo "  Tailscale не отвечает — проверь, что он запущен."
