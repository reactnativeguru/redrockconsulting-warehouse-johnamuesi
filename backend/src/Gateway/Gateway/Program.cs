// ---------------------------------------------------------------------------
// API Gateway (YARP - Microsoft's reverse proxy library).
//
// Clients (handheld app, admin portal) talk to ONE origin. The gateway routes by
// path prefix to the owning service. Benefits:
//   - clients don't know/care how many services exist or where they run
//   - one place for cross-cutting concerns: CORS, auth, rate limiting, logging
//   - in k8s/Azure, only the gateway needs a public endpoint
// Routes and destinations live in appsettings.json (overridable by env vars).
// ---------------------------------------------------------------------------
var builder = WebApplication.CreateBuilder(args);

builder.Services.AddReverseProxy()
    .LoadFromConfig(builder.Configuration.GetSection("ReverseProxy"));

builder.Services.AddCors(o => o.AddDefaultPolicy(p => p
    .WithOrigins(builder.Configuration.GetSection("Cors:Origins").Get<string[]>() ?? ["http://localhost:3000"])
    .AllowAnyHeader()
    .AllowAnyMethod()));

builder.Services.AddHealthChecks();

// In a real deployment: builder.Services.AddAuthentication().AddJwtBearer(...) with
// Entra ID, then .RequireAuthorization() on the proxy below.

var app = builder.Build();

app.UseCors();
app.MapHealthChecks("/health");
app.MapReverseProxy();

app.Run();
