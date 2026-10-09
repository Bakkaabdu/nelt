using Nelt.Application;
using Nelt.Infrastructure;
using Nelt.Infrastructure.Persistence;
using Nelt.Web.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

if (!builder.Environment.IsDevelopment())
{
    builder.Logging.AddJsonConsole();
}

builder.Services
    .AddApplication()
    .AddInfrastructure(builder.Configuration)
    .AddWeb(builder.Configuration, builder.Environment);

var app = builder.Build();

await app.Services.InitializeDatabaseAsync();

app.UseWebPipeline();
await app.RunAsync();

/// <summary>Entry point marker (also used by integration tests).</summary>
public partial class Program;
