#!/usr/bin/env python3
"""Каталог Synty Store — первый инструмент арт-конвейера (фаза 4, подфаза 4.0).

Зачем: подбор паков под мини-игру не должен начинаться с ручного листания
магазина. Каталог снимается машинно и лежит в репозитории, поэтому подбор
работает и тогда, когда самих паков на машине нет.

Команды:
    python tools/synty_catalog.py fetch            — обновить каталог из магазина
    python tools/synty_catalog.py find <слова>     — искать паки по теме
    python tools/synty_catalog.py preview <handle> — скачать превью в кэш (для просмотра глазами)

Выход:
    docs/art/synty-catalog.json — машинный каталог (коммитится)
    docs/art/synty-catalog.md   — человекочитаемая таблица (коммитится)
    docs/art/.cache/            — превью паков, в гитигноре
"""

from __future__ import annotations

import html
import json
import re
import sys
import urllib.request
from pathlib import Path

STORE = "https://syntystore.com"
PRODUCTS_URL = STORE + "/products.json?limit=250&page={page}"
MAX_PAGES = 10
USER_AGENT = "Mozilla/5.0 (compatible; sleepover-art-pipeline/1.0)"

# Консоль Windows по умолчанию в cp1251 — русский вывод и стрелки в ней падают.
if hasattr(sys.stdout, "reconfigure"):
    sys.stdout.reconfigure(encoding="utf-8", errors="replace")

ROOT = Path(__file__).resolve().parent.parent
ART_DIR = ROOT / "docs" / "art"
CATALOG_JSON = ART_DIR / "synty-catalog.json"
CATALOG_MD = ART_DIR / "synty-catalog.md"
CACHE_DIR = ART_DIR / ".cache"

# Паки, уже импортированные на машину геймдизайнера (STATE.md, разделы 3a и 3.29).
# PolygonGeneric отдельным продуктом не продаётся — приезжает внутри других паков.
IMPORTED = {
    "polygon-shops-pack",
    "polygon-town-pack",
    "polygon-farm-pack",
    "polygon-alpine-mountain-nature-biomes",
}

# Группировка по темам — только для читаемости таблицы, подбор идёт по тегам и описанию.
THEME_ORDER = [
    ("Современность и город", ("Modern", "City", "Urban")),
    ("Природа и биомы", ("Nature", "Biomes", "Farm")),
    ("Фэнтези и подземелья", ("Fantasy", "Dungeon", "Medieval")),
    ("Хоррор", ("Horror", "Zombie", "Apocalypse")),
    ("Sci-Fi", ("Sci-Fi", "SciFi", "Space")),
    ("История и война", ("History", "Military", "War", "Western", "Viking", "Samurai")),
    ("Персонажи", ("Characters", "Sidekick")),
    ("Интерфейс, FX, анимации", ("Interface", "FX", "Animation", "Icons")),
]


def _get(url: str) -> bytes:
    request = urllib.request.Request(url, headers={"User-Agent": USER_AGENT})
    with urllib.request.urlopen(request, timeout=60) as response:
        return response.read()


def _plain_text(body_html: str, limit: int = 6000) -> str:
    """HTML описания → короткая строка без разметки."""
    text = re.sub(r"<[^>]+>", " ", body_html or "")
    text = html.unescape(text)
    text = re.sub(r"\s+", " ", text).strip()
    return text[:limit]


def _theme_of(product: dict) -> str:
    tags = " ".join(product.get("tags", []))
    haystack = f"{product.get('title', '')} {tags}"
    for theme, keys in THEME_ORDER:
        if any(key.lower() in haystack.lower() for key in keys):
            return theme
    return "Прочее"


def fetch() -> dict:
    products: list[dict] = []
    for page in range(1, MAX_PAGES + 1):
        payload = json.loads(_get(PRODUCTS_URL.format(page=page)))
        batch = payload.get("products", [])
        if not batch:
            break
        products.extend(batch)
        print(f"  страница {page}: {len(batch)}")

    catalog = {"store": STORE, "packs": []}
    for product in products:
        variants = product.get("variants") or [{}]
        images = product.get("images") or []
        handle = product.get("handle", "")
        catalog["packs"].append(
            {
                "handle": handle,
                "title": product.get("title", ""),
                "type": product.get("product_type", ""),
                "tags": product.get("tags", []),
                "theme": _theme_of(product),
                "price_usd": variants[0].get("price"),
                "url": f"{STORE}/products/{handle}",
                "image": images[0]["src"].split("?")[0] if images else None,
                "images": [i["src"].split("?")[0] for i in images[:8]],
                "summary": _plain_text(product.get("body_html", "")),
                "imported": handle in IMPORTED,
            }
        )

    catalog["packs"].sort(key=lambda p: (p["theme"], p["title"]))
    ART_DIR.mkdir(parents=True, exist_ok=True)
    CATALOG_JSON.write_text(json.dumps(catalog, ensure_ascii=False, indent=1), encoding="utf-8")
    _write_markdown(catalog)
    print(f"Каталог: {len(catalog['packs'])} позиций → {CATALOG_JSON.relative_to(ROOT)}")
    return catalog


def _write_markdown(catalog: dict) -> None:
    lines = [
        "# Каталог Synty Store",
        "",
        "Снимается машинно: `python tools/synty_catalog.py fetch`. Не править руками.",
        "Подписка SyntyPass Standard даёт всю библиотеку, пока активна.",
        "**✅ — пак уже импортирован на машину** (`igruha/Assets/Synty/`, в репозиторий не кладётся).",
        "",
    ]
    by_theme: dict[str, list[dict]] = {}
    for pack in catalog["packs"]:
        by_theme.setdefault(pack["theme"], []).append(pack)

    for theme, packs in by_theme.items():
        lines += [f"## {theme}", "", "| Пак | Что внутри | Ссылка |", "|---|---|---|"]
        for pack in packs:
            mark = "✅ " if pack["imported"] else ""
            summary = pack["summary"][:160].replace("|", "/")
            lines.append(f"| {mark}{pack['title']} | {summary} | [{pack['handle']}]({pack['url']}) |")
        lines.append("")

    CATALOG_MD.write_text("\n".join(lines), encoding="utf-8")


def _load() -> dict:
    if not CATALOG_JSON.exists():
        sys.exit("Каталога нет. Сначала: python tools/synty_catalog.py fetch")
    return json.loads(CATALOG_JSON.read_text(encoding="utf-8"))


def find(words: list[str]) -> None:
    catalog = _load()
    needles = [w.lower() for w in words]
    for pack in catalog["packs"]:
        haystack = " ".join(
            [pack["title"], " ".join(pack["tags"]), pack["summary"]]
        ).lower()
        # По границам слов: иначе «tent» находится внутри «content», а «stage» внутри «stages».
        score = sum(len(re.findall(rf"\b{re.escape(n)}", haystack)) for n in needles)
        if score:
            mark = "✅" if pack["imported"] else "  "
            pack["_score"] = score
    hits = sorted(
        (p for p in catalog["packs"] if p.get("_score")),
        key=lambda p: -p["_score"],
    )[:15]
    for pack in hits:
        mark = "✅" if pack["imported"] else "  "
        print(f"{mark} [{pack['_score']:3}] {pack['title']}")
        print(f"       {pack['handle']} — {pack['summary'][:150]}")


def preview(handles: list[str]) -> None:
    """Качает картинки паков в кэш, чтобы их можно было посмотреть глазами."""
    catalog = _load()
    index = {p["handle"]: p for p in catalog["packs"]}
    CACHE_DIR.mkdir(parents=True, exist_ok=True)
    for handle in handles:
        pack = index.get(handle)
        if not pack:
            print(f"нет такого пака: {handle}")
            continue
        for number, source in enumerate(pack["images"][:4], start=1):
            target = CACHE_DIR / f"{handle}_{number}.png"
            if not target.exists():
                target.write_bytes(_get(source))
            print(target)


def main() -> None:
    if len(sys.argv) < 2:
        sys.exit(__doc__)
    command, args = sys.argv[1], sys.argv[2:]
    if command == "fetch":
        fetch()
    elif command == "find":
        find(args)
    elif command == "preview":
        preview(args)
    else:
        sys.exit(__doc__)


if __name__ == "__main__":
    main()
