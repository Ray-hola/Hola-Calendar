#!/bin/bash
# End-to-end check against the real server.py: boots a throwaway server on a
# temp database, signs in with a seeded account through holactl (the same
# HolaCore client the app uses), lists today's to-dos, completes one, then
# confirms the server's refusals (409 on a repeat, 403 for the admin account)
# come back as readable messages. No Keychain is touched (tokens via $HOLA_TOKEN).
#
#   scripts/integration-test.sh   (expects scripts/build.sh to have produced build/holactl)
set -euo pipefail
# Loopback must not go through a proxy some shells inject.
unset HTTP_PROXY HTTPS_PROXY ALL_PROXY http_proxy https_proxy all_proxy
export NO_PROXY=127.0.0.1,localhost no_proxy=127.0.0.1,localhost
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
REPO="$(cd "$ROOT/../.." && pwd)"
CTL="$ROOT/build/holactl"
[ -x "$CTL" ] || { echo "build first: scripts/build.sh"; exit 1; }
PORT="${PORT:-48173}"
WORK="$(mktemp -d)"
trap 'kill $SERVER 2>/dev/null || true; rm -rf "$WORK"' EXIT

python3 "$REPO/server.py" --port "$PORT" --db "$WORK/db.sqlite3" >"$WORK/server.log" 2>&1 &
SERVER=$!
for i in $(seq 1 50); do curl -fs -m 3 "http://127.0.0.1:$PORT/api/health" >/dev/null 2>&1 && break; sleep 0.2; done
curl -fs -m 3 "http://127.0.0.1:$PORT/api/health" >/dev/null || { cat "$WORK/server.log"; echo "server did not start"; exit 1; }

pw() { awk -F'\t' -v u="$1" '$1==u{print $2}' "$WORK/initial-accounts.txt"; }
SERVER_URL="127.0.0.1:$PORT"
fails=0
expect() { if [[ "$2" == *"$3"* ]]; then echo "  ok   $1"; else echo "  FAIL $1: expected «$3» in:"; echo "$2" | sed 's/^/       /'; fails=$((fails+1)); fi; }

echo "→ login as test001 (seeded member)"
TOKEN="$(HOLA_PASSWORD="$(pw test001)" "$CTL" login "$SERVER_URL" test001 --print-token)"
[ -n "$TOKEN" ] && echo "  ok   got a session token (${#TOKEN} chars)"

echo "→ wrong password is refused with the server's text"
OUT="$(HOLA_PASSWORD=nope "$CTL" login "$SERVER_URL" test001 --print-token 2>&1 || true)"
expect "401 message" "$OUT" "账户或密码错误"

echo "→ today's list"
OUT="$(HOLA_TOKEN="$TOKEN" "$CTL" today "$SERVER_URL")"
echo "$OUT" | sed 's/^/       /'
expect "seeded P0 task listed" "$OUT" "核验周轴与悬停"
expect "open marker" "$OUT" "[ ]"
TASK_ID="$(echo "$OUT" | awk '/^\[ \]/{print $2; exit}')"

echo "→ complete $TASK_ID"
OUT="$(HOLA_TOKEN="$TOKEN" "$CTL" complete "$SERVER_URL" "$TASK_ID")"
expect "completed" "$OUT" "completed"
OUT="$(HOLA_TOKEN="$TOKEN" "$CTL" today "$SERVER_URL")"
expect "now ticked" "$OUT" "[x] $TASK_ID"

echo "→ completing it again is refused (409)"
OUT="$(HOLA_TOKEN="$TOKEN" "$CTL" complete "$SERVER_URL" "$TASK_ID" 2>&1 || true)"
expect "409 message" "$OUT" "本次任务已记录"

echo "→ admin account cannot tick (403)"
ADMIN="$(HOLA_PASSWORD="$(pw superadmin)" "$CTL" login "$SERVER_URL" superadmin --print-token)"
OUT="$(HOLA_TOKEN="$ADMIN" "$CTL" today "$SERVER_URL")"
expect "admin sees everyone's tasks" "$OUT" "测试任务完成与递延"
OTHER="$(echo "$OUT" | awk '/^\[ \]/{print $2; exit}')"
OUT="$(HOLA_TOKEN="$ADMIN" "$CTL" complete "$SERVER_URL" "$OTHER" 2>&1 || true)"
expect "403 message" "$OUT" "superadmin 账号仅用于"

echo "→ a stale token is unauthorized"
OUT="$(HOLA_TOKEN="stale" "$CTL" today "$SERVER_URL" 2>&1 || true)"
expect "401 on stale" "$OUT" "请登录工作台"

echo "→ logout invalidates the token"
HOLA_TOKEN="$TOKEN" "$CTL" logout "$SERVER_URL" >/dev/null
OUT="$(HOLA_TOKEN="$TOKEN" "$CTL" today "$SERVER_URL" 2>&1 || true)"
expect "token dead after logout" "$OUT" "请登录工作台"

echo "→ offline server"
OUT="$(HOLA_TOKEN="x" "$CTL" today "127.0.0.1:1" 2>&1 || true)"
expect "offline message" "$OUT" "error:"

if [ "$fails" = 0 ]; then echo "integration: all checks passed"; else echo "integration: $fails check(s) failed"; exit 1; fi
