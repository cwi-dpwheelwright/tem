using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Xunit;

namespace Cwi.Orchestrator.IntegrationTests;

public sealed class MtlsValidationTests
{
    [Fact]
    public void ValidateClientCertificate_ReturnsTrue_WhenSignedByCa()
    {
        using var ca = CreateCertificateAuthority("CN=TestCA");
        using var client = CreateClientCertificate("CN=TestClient", ca);

        var result = Program.ValidateClientCertificate(client, ca);

        Assert.True(result);
    }

    [Fact]
    public void ValidateClientCertificate_ReturnsFalse_WhenCaMismatch()
    {
        using var ca = CreateCertificateAuthority("CN=TestCA");
        using var otherCa = CreateCertificateAuthority("CN=OtherCA");
        using var client = CreateClientCertificate("CN=TestClient", ca);

        var result = Program.ValidateClientCertificate(client, otherCa);

        Assert.False(result);
    }

    private static X509Certificate2 CreateCertificateAuthority(string subjectName)
    {
        var key = RSA.Create(2048);
        var request = new CertificateRequest(
            subjectName,
            key,
            HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1);

        request.CertificateExtensions.Add(
            new X509BasicConstraintsExtension(true, false, 0, true));
        request.CertificateExtensions.Add(
            new X509KeyUsageExtension(
                X509KeyUsageFlags.KeyCertSign | X509KeyUsageFlags.CrlSign,
                true));
        request.CertificateExtensions.Add(
            new X509SubjectKeyIdentifierExtension(request.PublicKey, false));

        var notBefore = DateTimeOffset.UtcNow.AddDays(-1);
        var notAfter = DateTimeOffset.UtcNow.AddDays(30);
        var cert = request.CreateSelfSigned(notBefore, notAfter);

        return cert;
    }

    private static X509Certificate2 CreateClientCertificate(string subjectName, X509Certificate2 caCertificate)
    {
        var key = RSA.Create(2048);
        var request = new CertificateRequest(
            subjectName,
            key,
            HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1);

        request.CertificateExtensions.Add(
            new X509BasicConstraintsExtension(false, false, 0, false));
        request.CertificateExtensions.Add(
            new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature, true));
        request.CertificateExtensions.Add(
            new X509SubjectKeyIdentifierExtension(request.PublicKey, false));

        var serial = new byte[16];
        RandomNumberGenerator.Fill(serial);
        var notBefore = DateTimeOffset.UtcNow.AddDays(-1);
        var notAfter = DateTimeOffset.UtcNow.AddDays(7);

        var cert = request.Create(caCertificate, notBefore, notAfter, serial);
        return cert.CopyWithPrivateKey(key);
    }
}
