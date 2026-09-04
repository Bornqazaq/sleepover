#!/usr/bin/env python3
"""Качает паки Synty из библиотеки SyntyPass через уже открытый Chrome.

Работает поверх живой сессии браузера (см. tools/browse/browse.py): вход по
шестизначному коду делается один раз руками, дальше скрипт сам обходит страницы
паков и тянет нужный вариант файла. Вариант выбирается по подписи — для Unity
это `Unity_2022_3`; SourceFiles и Unreal не нужны и не качаются.

    python tools/browse/synty_fetch.py packs.json out_dir [--variant Unity_2022_3]

packs.json — карта «имя пака → ссылка в библиотеке», снимается тем же обходом.
Уже скачанные файлы пропускаются по имени, так что прогон можно повторять.
"""

from __future__ import annotations

import json
import os
import sys
from pathlib import Path

from playwright.sync_api import sync_playwright

if hasattr(sys.stdout, "reconfigure"):
    sys.stdout.reconfigure(encoding="utf-8", errors="replace")

CDP = os.environ.get("BROWSE_CDP", "http://127.0.0.1:9222")
STORE = "https://syntystore.com"
DOWNLOAD_MARK = "/apps/downloads/downloads/"
CARD_DEPTH = 4  # насколько высоко подниматься от ссылки в поисках подписи файла


def card_text(link):
    """Текст карточки файла: подпись варианта лежит не в самой ссылке, а рядом."""
    node = link
    for _ in range(CARD_DEPTH):
        node = node.locator("xpath=..")
        try:
            text = node.inner_text(timeout=5000)
        except Exception:
            return ""
        if len(text) > 12:
            return text
    return ""


def pick_link(page, variant):
    for link in page.locator(f"a[href*='{DOWNLOAD_MARK}']").all():
        if variant.lower() in card_text(link).lower():
            return link
    return None


def main():
    if len(sys.argv) < 3:
        print(__doc__)
        return 2

    packs = json.loads(Path(sys.argv[1]).read_text(encoding="utf-8"))
    out_dir = Path(sys.argv[2])
    out_dir.mkdir(parents=True, exist_ok=True)
    variant = sys.argv[4] if "--variant" in sys.argv else "Unity_2022_3"

    done, failed = [], []
    with sync_playwright() as pw:
        browser = pw.chromium.connect_over_cdp(CDP)
        page = [p for p in browser.contexts[0].pages
                if not p.url.startswith("devtools://")][-1]

        for i, (name, href) in enumerate(packs.items(), 1):
            print(f"\n[{i}/{len(packs)}] {name}", flush=True)
            url = STORE + href if href.startswith("/") else href
            page.goto(url, wait_until="domcontentloaded", timeout=90000)
            page.wait_for_timeout(1500)

            link = pick_link(page, variant)
            if link is None:
                print(f"    нет варианта {variant} — пропуск", flush=True)
                failed.append((name, f"нет {variant}"))
                continue

            try:
                with page.expect_download(timeout=3600000) as info:
                    link.click()
                dl = info.value
                dest = out_dir / dl.suggested_filename
                if dest.exists() and dest.stat().st_size > 0:
                    print(f"    уже есть: {dest.name}", flush=True)
                    dl.cancel()
                    done.append(dest)
                    continue
                dl.save_as(str(dest))
                mb = dest.stat().st_size / 1048576
                print(f"    ✔ {dest.name}  {mb:.1f} МБ", flush=True)
                done.append(dest)
            except Exception as exc:
                print(f"    ✖ {exc.__class__.__name__}: {exc}"[:300], flush=True)
                failed.append((name, exc.__class__.__name__))

    print(f"\nскачано {len(done)}, не вышло {len(failed)}")
    for name, why in failed:
        print(f"  ✖ {name}: {why}")
    return 1 if failed else 0


if __name__ == "__main__":
    sys.exit(main())
