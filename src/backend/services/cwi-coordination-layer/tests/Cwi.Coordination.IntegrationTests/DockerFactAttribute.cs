using System.ComponentModel;
using System.Diagnostics;

namespace Cwi.Coordination.IntegrationTests;

[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
internal sealed class DockerFactAttribute : FactAttribute
{
    private static readonly Lazy<(bool Available, string? Reason)> Availability =
        new(DockerAvailabilityProbe.Check);

    public DockerFactAttribute()
    {
        var (available, reason) = Availability.Value;
        if (!available)
        {
            Skip = reason ?? "Docker is unavailable in this environment.";
        }
    }
}

internal static class DockerAvailabilityProbe
{
    public static (bool Available, string? Reason) Check()
    {
        try
        {
            using var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "docker",
                    Arguments = "info --format \"{{.ServerVersion}}\"",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };

            if (!process.Start())
            {
                return (false, "Docker CLI could not be started.");
            }

            if (!process.WaitForExit(5000))
            {
                TryKill(process);
                return (false, "Docker probe timed out.");
            }

            var serverVersion = process.StandardOutput.ReadToEnd().Trim();
            var error = process.StandardError.ReadToEnd().Trim();

            if (process.ExitCode != 0)
            {
                return (false, string.IsNullOrWhiteSpace(error)
                    ? "Docker daemon is unavailable."
                    : $"Docker daemon is unavailable: {error}");
            }

            return string.IsNullOrWhiteSpace(serverVersion)
                ? (false, "Docker daemon did not report a server version.")
                : (true, null);
        }
        catch (Exception ex) when (ex is Win32Exception or FileNotFoundException or InvalidOperationException)
        {
            return (false, $"Docker is unavailable: {ex.Message}");
        }
    }

    private static void TryKill(Process process)
    {
        try
        {
            process.Kill(entireProcessTree: true);
        }
        catch (InvalidOperationException)
        {
        }
    }
}
