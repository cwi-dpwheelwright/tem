using System.Text;
using Cwi.Common;
using Cwi.Common.Errors;
using Cwi.PackService.Domain;
using Cwi.PackService.Domain.Models;
using Cwi.PackService.Domain.Storage;

namespace Cwi.PackService.Api.Infrastructure;

public sealed class PackUploadReader
{
    private readonly UploadConfig _uploadConfig;
    private readonly IPackRepository _repository;

    public PackUploadReader(UploadConfig uploadConfig, IPackRepository repository)
    {
        _uploadConfig = uploadConfig;
        _repository = repository;
    }

    public async Task<ServiceResponse<PackUploadRequest>> ReadUploadRequestAsync(
        HttpContext context,
        CancellationToken cancellationToken)
    {
        var contentType = context.Request.ContentType?.Split(';')[0].Trim().ToLowerInvariant() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(contentType))
        {
            return Fail("unsupported_media_type", "Content-Type is required.", StatusCodes.Status415UnsupportedMediaType);
        }

        string? tempPath = null;
        try
        {
            if (contentType.StartsWith("multipart/form-data", StringComparison.OrdinalIgnoreCase))
            {
                var form = await context.Request.ReadFormAsync(cancellationToken).ConfigureAwait(false);
                var file = form.Files.FirstOrDefault();
                if (file is null)
                {
                    return Fail("invalid_request", "Bundle file is required.", StatusCodes.Status400BadRequest);
                }

                if (file.Length > _uploadConfig.MaxBytes)
                {
                    return Fail("payload_too_large", "Upload exceeds allowed size.", StatusCodes.Status413PayloadTooLarge);
                }

                tempPath = Path.GetTempFileName();
                await using (var fileStream = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None, 8192, true))
                await using (var input = file.OpenReadStream())
                {
                    await CopyWithLimitAsync(input, fileStream, _uploadConfig.MaxBytes, cancellationToken).ConfigureAwait(false);
                }

                var isZip = await _repository.IsZipBundleAsync(tempPath, cancellationToken).ConfigureAwait(false);
                var packJson = isZip
                    ? await _repository.ReadPackJsonFromZipAsync(tempPath, cancellationToken).ConfigureAwait(false)
                    : await _repository.ReadAllTextAsync(tempPath, cancellationToken).ConfigureAwait(false);

                if (string.IsNullOrWhiteSpace(packJson))
                {
                    TryDeleteTempFile(tempPath);
                    return Fail("invalid_pack", "pack.json missing from bundle.", StatusCodes.Status400BadRequest);
                }

                var dryRun = ParseBool(form.TryGetValue("dry_run", out var dry)
                    ? dry.ToString()
                    : context.Request.Query["dry_run"].ToString());
                var packIdOverride = form.TryGetValue("pack_id", out var packId)
                    ? packId.ToString()
                    : context.Request.Query["pack_id"].ToString();
                var packVersionOverride = form.TryGetValue("pack_version", out var packVersion)
                    ? packVersion.ToString()
                    : context.Request.Query["pack_version"].ToString();
                var notes = form.TryGetValue("notes", out var notesValue)
                    ? notesValue.ToString()
                    : context.Request.Query["notes"].ToString();

                return ServiceResponse<PackUploadRequest>.Success(
                    new PackUploadRequest(packJson, tempPath, isZip, notes, dryRun, packIdOverride, packVersionOverride));
            }

            if (contentType is "application/json" or "text/json" or "application/x-json")
            {
                var packJson = await ReadBodyWithLimitAsync(context.Request.Body, _uploadConfig.MaxBytes, cancellationToken)
                    .ConfigureAwait(false);

                var dryRun = ParseBool(context.Request.Query["dry_run"].ToString());
                var packIdOverride = context.Request.Query["pack_id"].ToString();
                var packVersionOverride = context.Request.Query["pack_version"].ToString();
                var notes = context.Request.Query["notes"].ToString();

                return ServiceResponse<PackUploadRequest>.Success(
                    new PackUploadRequest(packJson, null, false, notes, dryRun, packIdOverride, packVersionOverride));
            }

            if (contentType is "application/zip" or "application/x-zip-compressed" or "application/octet-stream")
            {
                tempPath = Path.GetTempFileName();
                await using (var fileStream = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None, 8192, true))
                {
                    await CopyWithLimitAsync(context.Request.Body, fileStream, _uploadConfig.MaxBytes, cancellationToken)
                        .ConfigureAwait(false);
                }

                var isZip = await _repository.IsZipBundleAsync(tempPath, cancellationToken).ConfigureAwait(false);
                var packJson = isZip
                    ? await _repository.ReadPackJsonFromZipAsync(tempPath, cancellationToken).ConfigureAwait(false)
                    : await _repository.ReadAllTextAsync(tempPath, cancellationToken).ConfigureAwait(false);

                if (string.IsNullOrWhiteSpace(packJson))
                {
                    TryDeleteTempFile(tempPath);
                    return Fail("invalid_pack", "pack.json missing from bundle.", StatusCodes.Status400BadRequest);
                }

                var dryRun = ParseBool(context.Request.Query["dry_run"].ToString());
                var packIdOverride = context.Request.Query["pack_id"].ToString();
                var packVersionOverride = context.Request.Query["pack_version"].ToString();
                var notes = context.Request.Query["notes"].ToString();

                return ServiceResponse<PackUploadRequest>.Success(
                    new PackUploadRequest(packJson, tempPath, isZip, notes, dryRun, packIdOverride, packVersionOverride));
            }
        }
        catch (PayloadTooLargeException)
        {
            if (!string.IsNullOrWhiteSpace(tempPath))
            {
                TryDeleteTempFile(tempPath);
            }

            return Fail("payload_too_large", "Upload exceeds allowed size.", StatusCodes.Status413PayloadTooLarge);
        }
        catch (InvalidDataException)
        {
            if (!string.IsNullOrWhiteSpace(tempPath))
            {
                TryDeleteTempFile(tempPath);
            }

            return Fail("invalid_pack", "Pack bundle is invalid.", StatusCodes.Status400BadRequest);
        }

        return Fail("unsupported_media_type", "Unsupported Content-Type.", StatusCodes.Status415UnsupportedMediaType);
    }

    public static void TryDeleteTempFile(string path, ILogger? logger = null)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (Exception ex)
        {
            logger?.LogWarning(ex, "Failed to clean up temp file {Path}", path);
        }
    }

    private static async Task<string> ReadBodyWithLimitAsync(Stream body, long maxBytes, CancellationToken cancellationToken)
    {
        using var memory = new MemoryStream();
        await CopyWithLimitAsync(body, memory, maxBytes, cancellationToken).ConfigureAwait(false);
        memory.Position = 0;
        using var reader = new StreamReader(memory, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, leaveOpen: false);
        return await reader.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task CopyWithLimitAsync(Stream input, Stream output, long maxBytes, CancellationToken cancellationToken)
    {
        var buffer = new byte[8192];
        long total = 0;
        int read;
        while ((read = await input.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken).ConfigureAwait(false)) > 0)
        {
            total += read;
            if (total > maxBytes)
            {
                throw new PayloadTooLargeException();
            }
            await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
        }
    }

    private static bool ParseBool(string? value)
    {
        return !string.IsNullOrWhiteSpace(value) && bool.TryParse(value, out var parsed) && parsed;
    }

    private static ServiceResponse<PackUploadRequest> Fail(string code, string message, int statusCode)
    {
        return ServiceResponse<PackUploadRequest>.Fail(new DomainError(code, message), statusCode, message);
    }
}
