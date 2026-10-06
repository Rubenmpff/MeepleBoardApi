#!/bin/bash
set -euo pipefail
root="$(cd "$(dirname "$0")/.." && pwd)"
name=meepleboard-device-tests-sql
[[ -f "$root/.device-tests/docker.env" && -f "$root/.device-tests/sql-image.txt" ]] || { echo 'Local test credentials and pinned image required; do not reuse normal SQL configuration.' >&2; exit 1; }
identity="$(docker inspect --format '{{index .Config.Labels "meepleboard.device-tests"}} {{range .Mounts}}{{.Name}}:{{.Destination}} {{end}}{{range (index .HostConfig.PortBindings "1433/tcp")}}{{.HostIp}}:{{.HostPort}}{{end}}' "$name")"
[[ "$identity" == 'true meepleboard-device-tests-data:/var/opt/mssql 127.0.0.1:14339' ]] || { echo 'Refusing an unexpected container, volume or port.' >&2; exit 1; }
docker start "$name"
echo 'SQL test container started; verify readiness with start-device-test-api.sh --sql-probe.'
