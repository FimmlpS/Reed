#!/bin/bash
# 临时桥：直接对编辑器自己起的 godot-ai 服务（默认 8000）发 MCP 调用。
# 用法: .tmp_godot_mcp.sh <tool> '<json args>'
PORT="${GODOT_AI_PORT:-8000}"
SIDFILE="$(dirname "$0")/.tmp_godot_mcp_sid"
URL="http://127.0.0.1:${PORT}/mcp"

init_session() {
	local raw
	raw=$(curl -s -D - -o /dev/null -X POST "$URL" \
		-H 'Content-Type: application/json' -H 'Accept: application/json, text/event-stream' \
		-d '{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2025-06-18","capabilities":{},"clientInfo":{"name":"claude-probe","version":"1"}}}' \
		--max-time 10)
	printf '%s' "$raw" | grep -i '^mcp-session-id:' | tr -d '\r' | awk '{print $2}'
}

tool="${1:?tool name required}"
args="${2:-{\}}"
sid=""
[ -f "$SIDFILE" ] && sid=$(cat "$SIDFILE")

call() {
	curl -s -X POST "$URL" \
		-H 'Content-Type: application/json' -H 'Accept: application/json, text/event-stream' \
		-H "mcp-session-id: $1" \
		-d "{\"jsonrpc\":\"2.0\",\"id\":2,\"method\":\"tools/call\",\"params\":{\"name\":\"$tool\",\"arguments\":$args}}" \
		--max-time 60
}

out=""
if [ -n "$sid" ]; then
	out=$(call "$sid")
fi
if [ -z "$out" ] || printf '%s' "$out" | grep -q 'TRANSPORT_AUTH_REQUIRED\|session'; then
	sid=$(init_session)
	printf '%s' "$sid" > "$SIDFILE"
	out=$(call "$sid")
fi
printf '%s\n' "$out"
