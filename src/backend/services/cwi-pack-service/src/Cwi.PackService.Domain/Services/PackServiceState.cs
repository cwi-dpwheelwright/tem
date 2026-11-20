using System.Text.Json;
using Cwi.PackService.Domain.Validation;

namespace Cwi.PackService.Domain.Services;

internal static class PackServiceState
{
    public static readonly SemaphoreSlim IndexLock = new(1, 1);
    public static readonly JsonSerializerOptions IndexJsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };
    public static readonly PackSchemaValidator SchemaValidator = new();
}
