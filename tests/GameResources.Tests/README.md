# GameResources.Tests

Run from the backend repository root:

```text
dotnet run --project tests/GameResources.Tests --no-launch-profile
```

Nine isolated HTTP checks of the game resource catalogue: authentication, administrator-only routes, pending suggestions, approval and rejection, validation, language filters with per-language counts, reports and administration edits/removal. Production controllers, service and entities with an in-memory repository and fixture authentication (role header); no configuration or SQL. SQL persistence is covered by `tools/DeviceTestApi/verify-game-resources.cjs`; see [docs/recursos-jogos.md](../../docs/recursos-jogos.md).
