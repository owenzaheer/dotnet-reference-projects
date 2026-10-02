# .NET workflow demos

.NET SDK 10. From this folder: `dotnet run --project ecommerce-order-and-inventory-service-dotnet --urls http://127.0.0.1:8000`. Substitute either assessment or Azure toolkit folder name for the other applications. Open the root URL for the JSON workflow console.

Run `dotnet test Tests/Tests.csproj`. Tests check replay conflicts, concurrent reservations, retained outbox messages, instructor permissions, stale assessment edits and ingestion retries.

Implemented: C#, ASP.NET Core minimal APIs, native JSON validation, in-memory state protected by a lock, outbox/replay fixtures and xUnit. The Azure ingestion toolkit models validated file rows and an operator replay queue locally. Azure Functions, Blob Storage, Service Bus, managed identity, SQL Server and EF Core are deployment extensions, not configured cloud integrations. The local console uses HTML/JavaScript; Angular views are provided separately in `frontends/angular` and can operate the assessment API through its proxy.

Explicit `Bearer local-learner`, `local-operator` and `local-instructor` tokens select local fixture roles. No external authentication is configured. `/api/state` requires the operator token. In-memory state resets on service restart.
