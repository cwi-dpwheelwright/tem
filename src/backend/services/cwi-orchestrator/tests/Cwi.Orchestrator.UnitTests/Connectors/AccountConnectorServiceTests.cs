using Cwi.Orchestrator.Application.Connectors;
using Cwi.Orchestrator.Application.Models;
using Cwi.Orchestrator.Domain.Entities;
using Cwi.Orchestrator.Domain.Repositories;
using FluentAssertions;
using Xunit;

namespace Cwi.Orchestrator.UnitTests.Connectors;

public sealed class AccountConnectorServiceTests
{
    [Fact]
    public async Task ListAsyncReturnsOnlyPlatformSignInConnectors()
    {
        var service = new AccountConnectorService(new InMemoryConnectorRepository());

        var result = await service.ListAsync("tenant-1", "user-1", CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Data!.Available.Select(item => item.ConnectorType)
            .Should()
            .Equal("google_drive", "onedrive", "outlook");
        result.Data.Available.Should().OnlyContain(item => item.AuthTypes.SequenceEqual(new[] { "oauth2" }));
        result.Data.Available.Should().OnlyContain(item => item.ConfigurableFields.Count == 0);
        result.Data.Available.Should().OnlyContain(item => !item.AuthorizationConfigured);
    }

    [Theory]
    [InlineData("json")]
    [InlineData("structured_csv")]
    [InlineData("smartsheet")]
    public async Task CreateAsyncRejectsFileAndAdvancedSourceConnectorTypes(string connectorType)
    {
        var service = new AccountConnectorService(new InMemoryConnectorRepository());

        var result = await service.CreateAsync(
            "tenant-1",
            "user-1",
            new AccountConnectorCreateRequestDto { ConnectorType = connectorType },
            CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(400);
    }

    [Fact]
    public async Task CreateAsyncCreatesOneDriveAsPendingOAuthSignIn()
    {
        var service = new AccountConnectorService(new InMemoryConnectorRepository());

        var result = await service.CreateAsync(
            "tenant-1",
            "user-1",
            new AccountConnectorCreateRequestDto { ConnectorType = "onedrive" },
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.StatusCode.Should().Be(201);
        result.Data!.ConnectorType.Should().Be("onedrive");
        result.Data.DisplayName.Should().Be("OneDrive");
        result.Data.AuthType.Should().Be("oauth2");
        result.Data.Status.Should().Be("pending_auth");
        result.Data.CredentialConfigured.Should().BeFalse();
        result.Data.AuthorizationUrl.Should().BeNull();
        result.Data.AuthorizationConfigured.Should().BeFalse();
        result.Data.Scopes.Should().Equal("files.read");
        result.Data.Config.Should().BeEmpty();
    }

    [Fact]
    public async Task CreateAsyncReturnsProviderAuthorizationUrlWhenOAuthIsConfigured()
    {
        var service = new AccountConnectorService(
            new InMemoryConnectorRepository(),
            new AccountConnectorOAuthOptions
            {
                PublicBaseUrl = "https://cwigpt.com",
                MicrosoftClientId = "microsoft-client-id",
                MicrosoftTenant = "common"
            });

        var result = await service.CreateAsync(
            "tenant-1",
            "user-1",
            new AccountConnectorCreateRequestDto { ConnectorType = "onedrive" },
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Data!.AuthorizationConfigured.Should().BeTrue();
        result.Data.AuthorizationUrl.Should().StartWith("https://login.microsoftonline.com/common/oauth2/v2.0/authorize?");
        result.Data.AuthorizationUrl.Should().Contain("client_id=microsoft-client-id");
        result.Data.AuthorizationUrl.Should().Contain("redirect_uri=https%3A%2F%2Fcwigpt.com%2Fapi%2Faccount%2Fconnectors%2Foauth%2Fcallback");
        result.Data.AuthorizationUrl.Should().Contain("scope=offline_access%20Files.Read%20User.Read");
        result.Data.AuthorizationUrl.Should().Contain("state=");
    }

    private sealed class InMemoryConnectorRepository : IUserConnectorConnectionRepository
    {
        private readonly List<UserConnectorConnection> _records = new();

        public Task<IReadOnlyList<UserConnectorConnection>> ListForUserAsync(
            string tenantId,
            string userId,
            CancellationToken cancellationToken)
        {
            IReadOnlyList<UserConnectorConnection> records = _records
                .Where(record =>
                    string.Equals(record.TenantId, tenantId, StringComparison.OrdinalIgnoreCase)
                    && string.Equals(record.UserId, userId, StringComparison.OrdinalIgnoreCase))
                .ToList();
            return Task.FromResult(records);
        }

        public Task<UserConnectorConnection?> GetForUserAsync(
            string tenantId,
            string userId,
            string connectionId,
            CancellationToken cancellationToken)
        {
            var record = _records.FirstOrDefault(item =>
                string.Equals(item.TenantId, tenantId, StringComparison.OrdinalIgnoreCase)
                && string.Equals(item.UserId, userId, StringComparison.OrdinalIgnoreCase)
                && string.Equals(item.ConnectionId, connectionId, StringComparison.OrdinalIgnoreCase));
            return Task.FromResult(record);
        }

        public Task UpsertAsync(UserConnectorConnection connection, CancellationToken cancellationToken)
        {
            _records.RemoveAll(item => string.Equals(item.ConnectionId, connection.ConnectionId, StringComparison.OrdinalIgnoreCase));
            _records.Add(connection);
            return Task.CompletedTask;
        }
    }
}
