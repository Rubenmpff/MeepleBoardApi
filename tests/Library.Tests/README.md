# Library.Tests

Run from the backend repository root:

```text
dotnet run --project tests/Library.Tests --no-launch-profile
```

Fifteen isolated checks: fourteen HTTP/service scenarios for the production library controller/service and one offline EF query/deletion check. Kestrel uses an ephemeral localhost port, fixture authentication and strict in-memory repositories. The production Program, application configuration, providers and database connections are never used.

Coverage: absent/null/zero/decimal prices, old requests, edit/clear/reload, existing statuses, negative rejection, owner write authorization, removal retaining catalogue/history. SQL persistence, JWT and native UI remain unverified. Production backend code is unchanged in this branch.

The current match model requires Matches.CreatorId; nullable does not make the database column optional. The prepared migration has not been applied. The existing catalogue snapshot index divergence and legacy public Cloudinary photo protection remain pending; see [authorization documentation](../../docs/autorizacao-partidas-diarios.md). Use only a separate disposable schema for device/integration testing.
