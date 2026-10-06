#!/bin/bash
set -euo pipefail
umask 077
root="$(cd "$(dirname "$0")/.." && pwd)"
export MEEPLE_DEVICE_TEST_SQL_FILE="$root/.device-tests/sql-connection.txt"
export MEEPLE_DEVICE_TEST_SQL_SERVER='127.0.0.1,14339'
if [[ "${1:-}" == '--audit-only' && ! -f "$MEEPLE_DEVICE_TEST_SQL_FILE" ]]; then
  unset MEEPLE_DEVICE_TEST_SQL_FILE MEEPLE_DEVICE_TEST_SQL_SERVER
fi
cd "$root"
# Runs in the foreground; Ctrl+C stops only this test API.
exec dotnet run --project tools/DeviceTestApi/DeviceTestApi.csproj --configuration DeviceTests --no-restore --no-launch-profile -- --data="$root/.device-tests" "$@"
