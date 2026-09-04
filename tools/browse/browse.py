#!/usr/bin/env python3
"""Пульт браузера для удалённой сессии — Playwright поверх живого Chrome по CDP.

Chrome поднимается отдельно с --remote-debugging-port, скрипт подключается к нему
на каждый вызов и отключается. Так вкладка, куки и вход переживают между командами,
а сам скрипт остаётся однократным — это единственный способ водить браузер
из Bash, когда браузерный MCP в сессии подключить нельзя.

    python tools/browse/browse.py goto https://syntystore.com/account/login
    python tools/browse/browse.py shot
    python tools/browse/browse.py text
    python tools/browse/browse.py fill "input[type=email]" me@example.com
    python tools/browse/browse.py click "Continue"
    python tools/browse/browse.py download "POLYGON Town" out/dir
"""

from __future__ import annotations

import os
import sys
import time
from pathlib import Path

from playwright.sync_api import sync_playwright

if hasattr(sys.stdout, "reconfigure"):
    sys.stdout.reconfigure(encoding="utf-8", errors="replace")

CDP = os.environ.get("BROWSE_CDP", "http://127.0.0.1:9222")
SHOT_DIR = Path(os.environ.get("BROWSE_SHOTS", "shots"))
SETTLE_MS = 1200


def page_of(browser):
    ctx = browser.contexts[0]
    pages = [p for p in ctx.pages if not p.url.startswith("devtools://")]
    return pages[-1] if pages else ctx.new_page()


def settle(page):
    try:
        page.wait_for_load_state("networkidle", timeout=15000)
    except Exception:
        page.wait_for_timeout(SETTLE_MS)


def locate(page, what):
    """Селектор, если похож на селектор; иначе — поиск по видимому тексту."""
    if what.startswith(("#", ".", "[")) or "=" in what.split(" ")[0]:
        return page.locator(what).first
    by_role = page.get_by_role("button", name=what).or_(page.get_by_role("link", name=what))
    if by_role.count():
        return by_role.first
    return page.get_by_text(what, exact=False).first


def main():
    if len(sys.argv) < 2:
        print(__doc__)
        return 2

    cmd, args = sys.argv[1], sys.argv[2:]

    with sync_playwright() as pw:
        browser = pw.chromium.connect_over_cdp(CDP)
        page = page_of(browser)

        if cmd == "goto":
            page.goto(args[0], wait_until="domcontentloaded", timeout=60000)
            settle(page)
            print(f"{page.url}\n{page.title()}")

        elif cmd == "shot":
            SHOT_DIR.mkdir(parents=True, exist_ok=True)
            out = Path(args[0]) if args else SHOT_DIR / f"shot-{int(time.time())}.png"
            out.parent.mkdir(parents=True, exist_ok=True)
            page.screenshot(path=str(out), full_page="--full" in args)
            print(out)

        elif cmd == "text":
            body = page.inner_text("body")
            limit = int(args[0]) if args else 6000
            print(f"URL: {page.url}\n---")
            print(body[:limit])

        elif cmd == "links":
            needle = args[0].lower() if args else ""
            seen = set()
            for a in page.locator("a").all():
                href = a.get_attribute("href") or ""
                label = (a.inner_text() or "").strip().replace("\n", " ")[:70]
                key = (label, href)
                if not href or key in seen:
                    continue
                if needle and needle not in (label + href).lower():
                    continue
                seen.add(key)
                print(f"{label}  ->  {href}")

        elif cmd == "fill":
            locate(page, args[0]).fill(args[1])
            print(f"filled {args[0]}")

        elif cmd == "click":
            locate(page, args[0]).click(timeout=15000)
            settle(page)
            print(f"clicked {args[0]} -> {page.url}")

        elif cmd == "press":
            page.keyboard.press(args[0])
            settle(page)
            print(f"pressed {args[0]}")

        elif cmd == "download":
            out_dir = Path(args[1] if len(args) > 1 else "downloads")
            out_dir.mkdir(parents=True, exist_ok=True)
            with page.expect_download(timeout=1800000) as info:
                locate(page, args[0]).click()
            dl = info.value
            dest = out_dir / dl.suggested_filename
            dl.save_as(str(dest))
            print(f"{dest}  {dest.stat().st_size} bytes")

        else:
            print(f"неизвестная команда: {cmd}")
            return 2

    return 0


if __name__ == "__main__":
    sys.exit(main())
