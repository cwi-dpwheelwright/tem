using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Json;
using Cwi.Common;
using Cwi.Router.Application.Configuration;
using Cwi.Router.Application.Handlers;
using Cwi.Router.Application.Pipeline;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.IdentityModel.Tokens;
using Xunit;

namespace Cwi.Router.UnitTests;

[Collection("Env")]
public sealed class AuthHandlerTests
{
    [Fact]
    public async Task ValidToken_AllowsRequest()
    {
        var (handler, context) = CreateHandlerWithToken(expectedAudience: "api", expectedIssuer: "issuer");

        var response = await handler.HandleAsync(
            context,
            () => Task.FromResult(ServiceResponse<RouterResult>.Success(new RouterResult(200, "{}", "application/json"))),
            CancellationToken.None);

        response.IsSuccess.Should().BeTrue();
        context.ClientId.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task InvalidSignature_Fails()
    {
        var (handler, context) = CreateHandlerWithToken(expectedAudience: "api", expectedIssuer: "issuer", useWrongKey: true);

        var response = await handler.HandleAsync(
            context,
            () => Task.FromResult(ServiceResponse<RouterResult>.Success(new RouterResult(200, "{}", "application/json"))),
            CancellationToken.None);

        response.IsSuccess.Should().BeFalse();
        response.StatusCode.Should().Be(401);
    }

    [Fact]
    public async Task WrongAudience_Fails()
    {
        var (handler, context) = CreateHandlerWithToken(expectedAudience: "api", expectedIssuer: "issuer", tokenAudience: "other");

        var response = await handler.HandleAsync(
            context,
            () => Task.FromResult(ServiceResponse<RouterResult>.Success(new RouterResult(200, "{}", "application/json"))),
            CancellationToken.None);

        response.IsSuccess.Should().BeFalse();
        response.StatusCode.Should().Be(401);
    }

    [Fact]
    public async Task ExpiredToken_Fails()
    {
        var (handler, context) = CreateHandlerWithToken(expectedAudience: "api", expectedIssuer: "issuer", expiresInSeconds: -120);

        var response = await handler.HandleAsync(
            context,
            () => Task.FromResult(ServiceResponse<RouterResult>.Success(new RouterResult(200, "{}", "application/json"))),
            CancellationToken.None);

        response.IsSuccess.Should().BeFalse();
        response.StatusCode.Should().Be(401);
    }

    [Fact]
    public async Task TokenVersionMismatch_Fails()
    {
        var (handler, context) = CreateHandlerWithToken(expectedAudience: "api", expectedIssuer: "issuer", tokenVersion: 1, identityTokenVersion: 2, userId: "user-2");

        var response = await handler.HandleAsync(
            context,
            () => Task.FromResult(ServiceResponse<RouterResult>.Success(new RouterResult(200, "{}", "application/json"))),
            CancellationToken.None);

        response.IsSuccess.Should().BeFalse();
        response.StatusCode.Should().Be(401);
    }

    private static (AuthHandler handler, RouterRequestContext context) CreateHandlerWithToken(
        string expectedAudience,
        string expectedIssuer,
        string? tokenAudience = null,
        int expiresInSeconds = 300,
        bool useWrongKey = false,
        int tokenVersion = 1,
        int identityTokenVersion = 1,
        string userId = "user-1")
    {
        using var rsa = RSA.Create(2048);
        var key = new RsaSecurityKey(rsa) { KeyId = "kid1" };
        var jwk = JsonWebKeyConverter.ConvertFromRSASecurityKey(key);
        jwk.Kid = "kid1";
        jwk.Use = "sig";
        jwk.Alg = SecurityAlgorithms.RsaSha256;

        var jwks = new JsonWebKeySet();
        jwks.Keys.Add(jwk);
        var jwksJson = JsonSerializer.Serialize(new { keys = jwks.Keys });

        var signingKey = useWrongKey ? new RsaSecurityKey(RSA.Create(2048)) : key;
        var creds = new SigningCredentials(signingKey, SecurityAlgorithms.RsaSha256);

        var claims = new[]
        {
            new Claim("sub", userId),
            new Claim("tenant_id", "tenant-1"),
            new Claim("token_version", tokenVersion.ToString())
        };
        var tokenDescriptor = new SecurityTokenDescriptor
        {
            Audience = tokenAudience ?? expectedAudience,
            Issuer = expectedIssuer,
            Subject = new ClaimsIdentity(claims),
            NotBefore = DateTime.UtcNow.AddMinutes(-10),
            Expires = DateTime.UtcNow.AddSeconds(expiresInSeconds),
            SigningCredentials = creds
        };

        var tokenHandler = new JwtSecurityTokenHandler();
        var token = tokenHandler.CreateToken(tokenDescriptor);
        var jwt = tokenHandler.WriteToken(token);

        Environment.SetEnvironmentVariable("ORCHESTRATOR_URL", "http://localhost:8081/");
        Environment.SetEnvironmentVariable("JWKS_URL", "https://example.local/jwks");
        Environment.SetEnvironmentVariable("JWT_ISSUER", expectedIssuer);
        Environment.SetEnvironmentVariable("JWT_AUDIENCE", expectedAudience);
        Environment.SetEnvironmentVariable("IDENTITY_URL", "https://example.local");
        Environment.SetEnvironmentVariable("TOKEN_EPOCH_CACHE_SECONDS", "60");
        Environment.SetEnvironmentVariable("AUTH_REQUIRED", "true");

        var config = RouterConfig.LoadFromEnvironment();
        var httpClient = new HttpClient(new FakeAuthHandler(jwksJson, identityTokenVersion))
        {
            BaseAddress = new Uri("https://example.local/")
        };
        var factory = new FakeHttpClientFactory(httpClient);
        var handler = new AuthHandler(config, factory);

        var httpContext = new DefaultHttpContext();
        httpContext.Request.Headers.Authorization = "Bearer " + jwt;
        var context = new RouterRequestContext(httpContext, body: string.Empty);

        return (handler, context);
    }

    private sealed class FakeHttpClientFactory : IHttpClientFactory
    {
        private readonly HttpClient _client;

        public FakeHttpClientFactory(HttpClient client)
        {
            _client = client;
        }

        public HttpClient CreateClient(string name)
        {
            return _client;
        }
    }

    private sealed class FakeAuthHandler : HttpMessageHandler
    {
        private readonly string _jwksJson;
        private readonly int _tokenVersion;

        public FakeAuthHandler(string jwksJson, int tokenVersion)
        {
            _jwksJson = jwksJson;
            _tokenVersion = tokenVersion;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri?.AbsolutePath ?? "";
            HttpResponseMessage response;
            if (path.EndsWith("/jwks", StringComparison.OrdinalIgnoreCase))
            {
                response = new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(_jwksJson)
                };
            }
            else if (path.EndsWith("/auth/token-version", StringComparison.OrdinalIgnoreCase))
            {
                var payload = JsonSerializer.Serialize(new { token_version = _tokenVersion });
                response = new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(payload)
                };
            }
            else
            {
                response = new HttpResponseMessage(HttpStatusCode.NotFound);
            }

            return Task.FromResult(response);
        }
    }
}
