#!/usr/bin/env python3
"""Unity MCP напрямую через HTTP-мост, в обход клиента Claude Code.

Зачем. Клиент опрашивает MCP-серверы **один раз, на старте сессии**. Если чат
открыли раньше Unity, мост на 8080 ещё никто не слушает, клиент получает
ConnectionRefused и инструментов `mcp__unity__*` в сессии не будет вовсе —
даже когда редактор поднимется минутой позже. Проверено 19.09.2026: сессия
10:26:28, Unity 10:29:34, мост 10:30:44.

Лечится либо порядком запуска (сперва Unity, потом чат), либо этим скриптом:
он говорит с мостом сам и умеет всё то же самое.

    python tools/unity_bridge.py tools                      список инструментов
    python tools/unity_bridge.py call <tool> '<json-args>'  вызов инструмента
    python tools/unity_bridge.py exec <файл.cs>             execute_code из файла

Три грабли, каждая стоит одного впустую потраченного вызова:

1. **Рукопожатие обязательно.** Мост говорит по streamable HTTP: сперва
   `initialize`, из заголовков ответа забрать `mcp-session-id`, послать
   `notifications/initialized`, и дальше каждый вызов с этим идентификатором.
   Голый `tools/call` отбивается без подсказки. Скрипт делает это сам и кладёт
   идентификатор рядом с собой.
2. **У `execute_code` обязателен `action: "execute"`**, директивы `using`
   запрещены (тело оборачивается в метод), и метод обязан вернуть значение.
   Писать полными именами: `UnityEditor.AssetDatabase`, `System.IO.Directory`.
3. **Кириллица в теле запроса рвёт вызов молча** — ответ приходит пустым, без
   ошибки и без записи в консоли Unity. Диагностические строки внутри
   `execute_code` писать латиницей. В самих файлах проекта кириллица работает.

Приватные методы редакторных инструментов дёргаются рефлексией:
`System.Type.GetType("Igruha.EditorTools.X, Assembly-CSharp-Editor")` — так
запускаются пункты меню, которые иначе открыли бы модальное окно.

Долгая операция рвёт соединение по таймауту, но в редакторе доходит до конца.
Проверять по файловой системе, а не перезапускать: иначе получишь два прогона.
"""
import json, os, sys, urllib.request

URL = "http://127.0.0.1:8080/mcp"
SESSION_FILE = os.path.join(os.path.dirname(os.path.abspath(__file__)), ".unity-mcp-session")
HEADERS = {"Content-Type": "application/json",
           "Accept": "application/json, text/event-stream"}


def post(body, session=None, want_headers=False, timeout=600):
    headers = dict(HEADERS)
    if session:
        headers["Mcp-Session-Id"] = session
    req = urllib.request.Request(URL, data=json.dumps(body).encode("utf-8"),
                                 headers=headers, method="POST")
    with urllib.request.urlopen(req, timeout=timeout) as resp:
        raw = resp.read().decode("utf-8", "replace")
        sid = resp.headers.get("mcp-session-id")
    payload = None
    for line in raw.splitlines():
        if line.startswith("data:"):
            payload = json.loads(line[5:].strip())
    if payload is None and raw.strip():
        try:
            payload = json.loads(raw)
        except ValueError:
            payload = {"raw": raw}
    return (payload, sid) if want_headers else payload


def handshake():
    body = {"jsonrpc": "2.0", "id": 1, "method": "initialize",
            "params": {"protocolVersion": "2024-11-05", "capabilities": {},
                       "clientInfo": {"name": "curl-bridge", "version": "1"}}}
    _, sid = post(body, want_headers=True)
    post({"jsonrpc": "2.0", "method": "notifications/initialized"}, session=sid)
    with open(SESSION_FILE, "w") as f:
        f.write(sid or "")
    return sid


def session():
    """Живой идентификатор сессии: сохранённый, если мост его ещё помнит, иначе новый.

    Сохранённый проверяется, а не берётся на веру: перезапуск редактора поднимает
    мост заново, и прежний идентификатор он встречает `404 Not Found` — причём
    исключением, а не полем `error` в ответе. Без этой проверки скрипт падает
    трассировкой там, где достаточно переподключиться.
    """
    if os.path.exists(SESSION_FILE):
        sid = open(SESSION_FILE).read().strip()
        if sid:
            try:
                probe = post({"jsonrpc": "2.0", "id": 2, "method": "tools/list"},
                             session=sid, timeout=15)
                if probe and "error" not in probe:
                    return sid
            except Exception:
                pass
    return handshake()


def main():
    cmd = sys.argv[1] if len(sys.argv) > 1 else "tools"
    sid = session()
    if cmd == "tools":
        res = post({"jsonrpc": "2.0", "id": 3, "method": "tools/list"}, session=sid)
        for t in res["result"]["tools"]:
            print(t["name"], "-", (t.get("description") or "").split("\n")[0][:110])
        return
    if cmd == "call":
        tool, args = sys.argv[2], json.loads(sys.argv[3]) if len(sys.argv) > 3 else {}
    elif cmd == "exec":
        tool = "execute_code"
        args = {"action": "execute", "code": open(sys.argv[2], encoding="utf-8").read()}
    else:
        print("неизвестная команда", cmd); sys.exit(2)
    res = post({"jsonrpc": "2.0", "id": 4, "method": "tools/call",
                "params": {"name": tool, "arguments": args}}, session=sid)
    if not res:
        print("ПУСТОЙ ОТВЕТ — вероятно кириллица в теле запроса"); sys.exit(1)
    if "error" in res:
        print(json.dumps(res["error"], ensure_ascii=False, indent=1)); sys.exit(1)
    for item in res.get("result", {}).get("content", []):
        print(item.get("text", json.dumps(item, ensure_ascii=False)))


main()
