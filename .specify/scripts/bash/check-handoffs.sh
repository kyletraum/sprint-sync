#!/bin/sh
# Lists OPEN session handoffs under .specify/handoffs — the load-time check for
# the handoff mechanism (see .specify/handoffs/README.md and CLAUDE.md). Run at
# session start and before any /speckit-* command. Always exits 0: it reports, it
# does not gate.

set -eu

HANDOFF_DIR="${1:-$(dirname "$0")/../../handoffs}"

if [ ! -d "$HANDOFF_DIR" ]; then
  echo 'No handoffs directory — nothing to consume.'
  exit 0
fi

count=0
lines=""
for file in "$HANDOFF_DIR"/*.md; do
  [ -e "$file" ] || continue
  case "$(basename "$file")" in README.md) continue ;; esac
  if grep -Eq '^[[:space:]]*status:[[:space:]]*open\b' "$file"; then
    feature=$(grep -E '^[[:space:]]*feature:' "$file" | head -1 | sed 's/.*feature:[[:space:]]*//' || echo '-')
    items=$(grep -Ec '^\|[[:space:]]*[0-9]+[[:space:]]*\|' "$file" || echo 0)
    count=$((count + 1))
    lines="$lines
  - .specify/handoffs/$(basename "$file")  [feature: ${feature:--}]  ~${items} item(s)"
  fi
done

if [ "$count" -eq 0 ]; then
  echo 'No OPEN handoffs.'
  exit 0
fi

echo "OPEN handoffs: $count — read and consume before proceeding (CLAUDE.md).$lines"
exit 0
