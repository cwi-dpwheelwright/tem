using System.Security.Cryptography.X509Certificates;

namespace Cwi.Coordination.Api.Security;

public static class CertificateValidator
{
    public static bool Validate(X509Certificate2? certificate, string? trustBundlePath)
    {
        if (certificate is null)
        {
            return false;
        }
        if (string.IsNullOrWhiteSpace(trustBundlePath) || !File.Exists(trustBundlePath))
        {
            return true;
        }

        var bundle = new X509Certificate2Collection();
        bundle.ImportFromPem(File.ReadAllText(trustBundlePath));

        using var chain = new X509Chain();
        chain.ChainPolicy.TrustMode = X509ChainTrustMode.CustomRootTrust;
        chain.ChainPolicy.CustomTrustStore.AddRange(bundle);
        chain.ChainPolicy.VerificationFlags = X509VerificationFlags.NoFlag;

        return chain.Build(certificate);
    }
}
