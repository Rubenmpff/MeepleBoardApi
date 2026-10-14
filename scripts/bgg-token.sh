#!/bin/bash
# BGG XML API token for local runs on macOS, kept in the login Keychain (never in files, code or the repository).
#   scripts/bgg-token.sh set              prompts for the token (hidden input) and stores it
#   scripts/bgg-token.sh status           says whether it is stored and whether this shell has Bgg__Token; never prints it
#   scripts/bgg-token.sh run -- <command> runs <command> with Bgg__Token taken from the Keychain, for that process only
#   scripts/bgg-token.sh delete           removes it
# The API reads it as configuration key Bgg:Token. DeviceTests does not use it: its host never contacts BGG.
set -euo pipefail
service=meepleboard-bgg-token
case "${1:-}" in
  set)
    # -w as the last option makes `security` ask for the value itself (hidden, typed twice): it never reaches
    # the command line, the shell history or a file. -U replaces a previous value.
    echo 'Paste the BGG token at the prompt (input hidden), then again to confirm.'
    security add-generic-password -U -a "$USER" -s "$service" -l 'MeepleBoard BGG token' -w
    echo 'Stored in the login Keychain.' ;;
  status)
    if value="$(security find-generic-password -a "$USER" -s "$service" -w 2>/dev/null)" && [[ -n "$value" ]]; then
      echo "Keychain: stored (${#value} characters)"
    else
      echo 'Keychain: not stored'
    fi
    unset value
    [[ -n "${Bgg__Token:-}" ]] && echo 'This shell: Bgg__Token defined' || echo 'This shell: Bgg__Token not defined' ;;
  run)
    shift; [[ "${1:-}" == '--' ]] && shift
    (( $# )) || { echo 'Usage: scripts/bgg-token.sh run -- <command>' >&2; exit 1; }
    Bgg__Token="$(security find-generic-password -a "$USER" -s "$service" -w 2>/dev/null)" || { echo 'No BGG token in the Keychain; run: scripts/bgg-token.sh set' >&2; exit 1; }
    export Bgg__Token
    exec "$@" ;;
  delete)
    security delete-generic-password -a "$USER" -s "$service" >/dev/null 2>&1 && echo 'Removed from the Keychain.' ;;
  *)
    sed -n '2,7p' "$0" | sed 's/^# \{0,1\}//'; exit 1 ;;
esac
