using System.Text.Json;
using System.Text.Json.Serialization;
using Cwi.Normalization.Api;
using Cwi.Normalization.Core;
using Microsoft.AspNetCore.Http.Json;
using Microsoft.OpenApi.Models;

var builder = WebApplication.CreateBuilder(args);
var options = NormalizationApiOptions.LoadFromEnvironment();

builder.Services.AddSingleton(options);
builder.Services.AddSingleton<RuleBasedLanguageNormalizer>();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(swaggerOptions =>
{
    swaggerOptions.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "CWI Language Normalization API",
        Version = "v1"
    });
});
builder.Services.Configure<JsonOptions>(jsonOptions =>
{
    jsonOptions.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower;
    jsonOptions.SerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
});
builder.WebHost.UseUrls($"http://0.0.0.0:{options.HttpPort}");

var app = builder.Build();
if (app.Environment.IsDevelopment() || options.DevOpenApiEnabled)
{
    app.UseSwagger(swaggerOptions =>
    {
        swaggerOptions.RouteTemplate = "api/dev/openapi/{documentName}.json";
    });
    app.UseSwaggerUI(swaggerUiOptions =>
    {
        swaggerUiOptions.SwaggerEndpoint("/api/dev/openapi/v1.json", "CWI Language Normalization API v1");
        swaggerUiOptions.RoutePrefix = "api/dev/swagger";
    });
}

app.MapGet("/health", () => Results.Ok(new
{
    status = "healthy",
    service = "cwi-language-normalization",
    engine_version = RuleBasedLanguageNormalizer.EngineVersion
}))
.WithName("Health")
.WithTags("Health");

app.MapPost("/api/normalization/normalize", (
    NormalizationRequest request,
    RuleBasedLanguageNormalizer normalizer) =>
{
    var result = normalizer.Normalize(request.Prompt);
    return Results.Ok(result);
})
.WithName("NormalizePrompt")
.WithTags("Normalization");

app.Run();

public sealed record NormalizationRequest(string? Prompt);

public partial class Program;
