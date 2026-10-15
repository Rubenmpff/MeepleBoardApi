#!/bin/bash
# Fetch a BGG XML sample with the existing token mechanism (environment variable Bgg__Token, as the API reads Bgg:Token)
# and keep ONLY the XML in .device-tests/ (not versioned). The token is never printed, never put on the command line
# (the Authorization header goes to curl through stdin) and never written to disk.
#   tools/DeviceTestApi/fetch-bgg-sample.sh                      # Wingspan, Codenames?, Ticket to Ride? (confirm in the output)
#   tools/DeviceTestApi/fetch-bgg-sample.sh 266192               # one game
# Same request as BGGService: {Bgg__BaseUrl or https://boardgamegeek.com/xmlapi2/}thing?id=…&stats=1&type=boardgame,boardgameexpansion
set -euo pipefail
set +x
ids="${1:-266192,178900,9209}"
out_dir="${2:-$(cd "$(dirname "$0")/../.." && pwd)/.device-tests}"
[[ "$ids" =~ ^[0-9]+(,[0-9]+)*$ ]] || { echo "IDs inválidos: use números separados por vírgulas." >&2; exit 64; }
if [ -z "${Bgg__Token:-}" ]; then
  echo "Bgg__Token não está definida nesta shell: nada foi pedido. A amostra fica pendente." >&2
  exit 2
fi
base="${Bgg__BaseUrl:-https://boardgamegeek.com/xmlapi2/}"
url="${base%/}/thing?id=${ids}&stats=1&type=boardgame,boardgameexpansion"
mkdir -p "$out_dir"
file="$out_dir/bgg-sample-$(date +%Y%m%d-%H%M%S).xml"
tmp="$(mktemp)"; trap 'rm -f "$tmp"' EXIT
echo "Pedido: $url"
status=""
for attempt in 1 2 3 4 5; do
  # printf is a shell builtin: the token does not appear in the process list.
  status="$(printf 'Authorization: Bearer %s\n' "$Bgg__Token" | curl -sS -H @- -A "MeepleBoard/1.0" -o "$tmp" -w '%{http_code}' "$url")" || status="000"
  [ "$status" = "202" ] || break
  echo "BGG pôs o pedido em fila (202); nova tentativa em 5 s ($attempt/5)."; sleep 5
done
if [ "$status" != "200" ]; then
  echo "Resposta HTTP $status: nada foi guardado." >&2
  exit 1
fi
if ! xmllint --noout "$tmp" 2>/dev/null; then
  echo "A resposta não é XML válido: nada foi guardado." >&2
  exit 1
fi
mv "$tmp" "$file"; trap - EXIT
echo "Guardado: $file"
# What the sample contains, read from the response itself (names and IDs, poll and rank names).
python3 -I - "$file" <<'PY'
import sys, xml.etree.ElementTree as ET
root = ET.parse(sys.argv[1]).getroot()
items = root.findall("item")
print(f"{len(items)} item(s)")
for item in items:
    name = next((n.get("value") for n in item.findall("name") if n.get("type") == "primary"), "?")
    y = item.find("yearpublished"); year = y.get("value", "?") if y is not None else "?"
    print(f"- id {item.get('id')} · {item.get('type')} · {name} ({year})")
    polls = [f"{p.get('name')} ({p.get('totalvotes')} votos)" for p in item.findall("poll")]
    summaries = [p.get("name") for p in item.findall("poll-summary")]
    ranks = [f"{r.get('name')}={r.get('value')}" for r in item.iter("rank")]
    links = {}
    for l in item.findall("link"): links[l.get("type")] = links.get(l.get("type"), 0) + 1
    print("    votações:", ", ".join(polls) or "nenhuma")
    print("    resumos de votação:", ", ".join(summaries) or "nenhum")
    print("    posições:", ", ".join(ranks) or "nenhuma")
    print("    ligações:", ", ".join(f"{k} {v}" for k, v in sorted(links.items())) or "nenhuma")
PY
