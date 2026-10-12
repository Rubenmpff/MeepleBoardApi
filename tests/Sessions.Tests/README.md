# Sessions.Tests

Run from the backend repository root:

```text
dotnet run --project tests/Sessions.Tests --no-launch-profile
```

Thirteen isolated checks of session start, automatic end and organizer end time: domain rules with past/future times, the idempotent `SessionAutoCloseJob`, and `PUT /MeepleBoard/session/{id}/close-time` (organizer, guest, anonymous, limits, not started, unknown). In-memory repository and fixture authentication; no configuration, SQL or Hangfire. SQL persistence is covered by `tools/DeviceTestApi/verify-session-auto-close.cjs`; see [docs/sessoes-encerramento-automatico.md](../../docs/sessoes-encerramento-automatico.md).
