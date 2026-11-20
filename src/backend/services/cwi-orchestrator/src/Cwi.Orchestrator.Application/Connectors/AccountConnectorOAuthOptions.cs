#nullable enable
namespace Cwi.Orchestrator.Application.Connectors;

public sealed class AccountConnectorOAuthOptions
{
    public static AccountConnectorOAuthOptions Empty { get; } = new();

    public string? PublicBaseUrl { get; init; }
    public string? DefaultRedirectUri { get; init; }
    public string CallbackPath { get; init; } = "/api/account/connectors/oauth/callback";

    public string? GoogleClientId { get; init; }
    public string? GoogleRedirectUri { get; init; }

    public string? MicrosoftClientId { get; init; }
    public string MicrosoftTenant { get; init; } = "common";
    public string? MicrosoftRedirectUri { get; init; }

    public static AccountConnectorOAuthOptions LoadFromEnvironment()
    {
        return new AccountConnectorOAuthOptions
        {
            PublicBaseUrl = FirstEnv("CWI_CONNECTOR_OAUTH_PUBLIC_BASE_URL", "CWI_PUBLIC_BASE_URL", "PUBLIC_BASE_URL"),
            DefaultRedirectUri = FirstEnv("CWI_CONNECTOR_OAUTH_REDIRECT_URI", "CONNECTOR_OAUTH_REDIRECT_URI"),
            CallbackPath = FirstEnv("CWI_CONNECTOR_OAUTH_CALLBACK_PATH", "CONNECTOR_OAUTH_CALLBACK_PATH")
                ?? "/api/account/connectors/oauth/callback",
            GoogleClientId = FirstEnv(
                "CWI_CONNECTOR_OAUTH_GOOGLE_CLIENT_ID",
                "CWI_GOOGLE_OAUTH_CLIENT_ID",
                "GOOGLE_OAUTH_CLIENT_ID",
                "GOOGLE_CLIENT_ID"),
            GoogleRedirectUri = FirstEnv(
                "CWI_CONNECTOR_OAUTH_GOOGLE_REDIRECT_URI",
                "CWI_GOOGLE_OAUTH_REDIRECT_URI",
                "GOOGLE_OAUTH_REDIRECT_URI"),
            MicrosoftClientId = FirstEnv(
                "CWI_CONNECTOR_OAUTH_MICROSOFT_CLIENT_ID",
                "CWI_MICROSOFT_OAUTH_CLIENT_ID",
                "MICROSOFT_OAUTH_CLIENT_ID",
                "AZURE_CLIENT_ID",
                "MICROSOFT_APP_ID"),
            MicrosoftTenant = FirstEnv(
                    "CWI_CONNECTOR_OAUTH_MICROSOFT_TENANT",
                    "CWI_MICROSOFT_OAUTH_TENANT",
                    "MICROSOFT_OAUTH_TENANT")
                ?? "common",
            MicrosoftRedirectUri = FirstEnv(
                "CWI_CONNECTOR_OAUTH_MICROSOFT_REDIRECT_URI",
                "CWI_MICROSOFT_OAUTH_REDIRECT_URI",
                "MICROSOFT_OAUTH_REDIRECT_URI")
        };
    }

    private static string? FirstEnv(params string[] names)
    {
        foreach (var name in names)
        {
            var value = Environment.GetEnvironmentVariable(name);
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value.Trim();
            }
        }

        return null;
    }
}
#nullable disable
