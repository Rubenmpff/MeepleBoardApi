#!/bin/bash
set -euo pipefail
umask 077
root="$(cd "$(dirname "$0")/.." && pwd)"
node_dir="${MEEPLE_TEST_NODE_DIR:-$HOME/.nvm/versions/node/v22.14.0/bin}"
export PATH="$node_dir:$PATH"
[[ "$(node --version)" == v22.14.0 && "$(npm --version)" == 10.9.2 ]] || { echo 'Required: Node 22.14.0 / npm 10.9.2.' >&2; exit 1; }
cd "$root"
curl --fail --silent --show-error --max-time 5 http://127.0.0.1:5099/device-test/health | node -e 'let s="";process.stdin.on("data",c=>s+=c);process.stdin.on("end",()=>{const h=JSON.parse(s);if(h.environment!=="DeviceTests"||h.database!=="MeepleBoard_DeviceTests"||h.externalDelivery!==false)process.exit(1);});'
for check in verify-catalog-search verify-local-photos verify-session-writes verify-session-dates verify-session-required-friend verify-session-invite-friends; do
  node "tools/DeviceTestApi/$check.cjs"
done
for mode in writes dates rule invites; do
  "$root/scripts/start-device-test-api.sh" "--verify-sql=$mode"
done
node tools/DeviceTestApi/verify-session-campaign-reading.cjs
