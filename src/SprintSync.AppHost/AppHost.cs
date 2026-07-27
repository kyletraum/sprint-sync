// Aspire AppHost — the single source of truth for service topology (Principle X).
// This slice declares SQL + the API. The React app (Phase 7) and the Azure SQL
// free-serverless / scale-to-zero publish settings (Phase 8, T043) are added
// later. No Redis/broker or other idle-billable dependency (Principle 12).

var builder = DistributedApplication.CreateBuilder(args);

var db = builder.AddSqlServer("sql")
    .AddDatabase("sprintsync");

builder.AddProject<Projects.SprintSync_Api>("api")
    .WithReference(db)
    .WaitFor(db);

builder.Build().Run();
