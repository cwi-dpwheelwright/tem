using Cwi.Common.Events;
using Cwi.Common.Journey;
using Cwi.PackService.Api.Configuration;
using Cwi.PackService.Api.Infrastructure;
using Cwi.PackService.DataAccess;
using Cwi.PackService.Domain.Models;
using Cwi.PackService.Domain.Services;
using Cwi.PackService.Domain.Storage;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.FileProviders;
using Microsoft.OpenApi.Models;

var builder = WebApplication.CreateBuilder(args);

builder.Logging.ClearProviders();
builder.Logging.AddJsonConsole();

var uploadConfig = UploadConfig.FromEnvironment();
builder.Services.AddSingleton(uploadConfig);
builder.Services.Configure<FormOptions>(options =>
{
    options.MultipartBodyLengthLimit = uploadConfig.MaxBytes;
});

builder.Services.AddHttpClient();
builder.Services.AddEventBusClient();
builder.Services.AddSingleton<JourneyEventBusPublisher>();
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "CWI Pack Service",
        Version = "v1"
    });
});

var httpPort = GetIntFromEnv("PACK_SERVICE_PORT", 8082);
builder.WebHost.UseUrls($"http://0.0.0.0:{httpPort}");

var packsDir = Environment.GetEnvironmentVariable("PACKS_DIR");
if (string.IsNullOrWhiteSpace(packsDir))
{
    throw new InvalidOperationException("PACKS_DIR is required.");
}
if (!Directory.Exists(packsDir))
{
    throw new InvalidOperationException($"PACKS_DIR does not exist: {packsDir}");
}

var packsRoot = Path.GetFullPath(packsDir)
    .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;

var authConfig = AuthConfig.FromEnvironment();
var auditConfig = AuditConfig.FromEnvironment();

builder.Services.AddSingleton(authConfig);
builder.Services.AddSingleton(auditConfig);
builder.Services.AddSingleton<IPackRepository>(new FilePackRepository(packsRoot));
builder.Services.AddSingleton<PackDomainService>(sp =>
    new PackDomainService(sp.GetRequiredService<IPackRepository>(), sp.GetRequiredService<UploadConfig>()));
builder.Services.AddSingleton<PackDiagnosticsService>();
builder.Services.AddSingleton<PackAuthService>();
builder.Services.AddSingleton<PackUploadReader>();
builder.Services.AddSingleton<PackAuditService>();

var app = builder.Build();
PackEventBus.AttachEventBus(app);

app.Logger.LogInformation("Pack service starting. PACKS_DIR={PacksDir}", packsRoot);

app.UseSwagger();
app.UseSwaggerUI(options =>
{
    options.SwaggerEndpoint("/swagger/v1/swagger.json", "CWI Pack Service v1");
    options.RoutePrefix = "swagger";
});

var artifactsRoot = Path.Combine(packsRoot, "artifacts");
Directory.CreateDirectory(artifactsRoot);
var reportsRoot = Path.Combine(artifactsRoot, "reports");
var docsRoot = Path.Combine(artifactsRoot, "docs");
Directory.CreateDirectory(reportsRoot);
Directory.CreateDirectory(docsRoot);
app.UseStaticFiles(new StaticFileOptions
{
    FileProvider = new PhysicalFileProvider(artifactsRoot),
    RequestPath = "/artifacts"
});
app.UseStaticFiles(new StaticFileOptions
{
    FileProvider = new PhysicalFileProvider(reportsRoot),
    RequestPath = "/reports"
});
app.UseStaticFiles(new StaticFileOptions
{
    FileProvider = new PhysicalFileProvider(docsRoot),
    RequestPath = "/docs"
});

app.MapControllers();

app.Run();

static int GetIntFromEnv(string name, int fallback)
{
    var raw = Environment.GetEnvironmentVariable(name);
    return !string.IsNullOrWhiteSpace(raw) && int.TryParse(raw, out var value) ? value : fallback;
}

public partial class Program { }

