using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.IdentityModel.Tokens.Jwt;
using System.Text.Json;
using System.Text.Json.Serialization;
using Cwi.Identity.Api.Data;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Cwi.Identity.Api.IntegrationTests;

public sealed class AuthFlowTests : IClassFixture<IdentityApiFactory>
{
    private readonly IdentityApiFactory _factory;

    public AuthFlowTests(IdentityApiFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task PasswordResetFlowCompletes()
    {
        var client = _factory.CreateClient();

        var tenantId = "tenant-1";
        var email = "user@example.com";
        var password = "ValidPassw0rd!";

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
            var user = new AppUser
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                Email = email,
                DisplayName = "User",
                Status = "active",
                PasswordHash = BCrypt.Net.BCrypt.HashPassword(password),
                CreatedUtc = DateTime.UtcNow,
                UpdatedUtc = DateTime.UtcNow
            };
            db.Users.Add(user);
            await db.SaveChangesAsync();
        }

        var resetResp = await client.PostAsJsonAsync("/auth/password/reset", new
        {
            tenant_id = tenantId,
            email = email
        });
        Assert.Equal(HttpStatusCode.OK, resetResp.StatusCode);

        var resetBody = await resetResp.Content.ReadFromJsonAsync<PasswordResetResponse>();
        Assert.NotNull(resetBody);

        var completeResp = await client.PostAsJsonAsync("/auth/password/reset/complete", new
        {
            token = resetBody!.ResetToken,
            new_password = "AnotherVal1d!"
        });

        Assert.Equal(HttpStatusCode.OK, completeResp.StatusCode);

        var loginResp = await client.PostAsJsonAsync("/auth/login", new
        {
            tenant_id = tenantId,
            email = email,
            password = "AnotherVal1d!"
        });

        Assert.Equal(HttpStatusCode.OK, loginResp.StatusCode);
    }

    [Fact]
    public async Task RegisterWithEmailCreatesActiveAccountAndIssuesToken()
    {
        var client = _factory.CreateClient();

        var tenantId = $"register-email-{Guid.NewGuid():N}";
        var email = $"new-{Guid.NewGuid():N}@example.com";

        await CreateTenantAsync(tenantId);

        var startResp = await client.PostAsJsonAsync("/auth/register/start", new
        {
            tenant_id = tenantId,
            email,
            display_name = "New Email User"
        });

        Assert.Equal(HttpStatusCode.OK, startResp.StatusCode);
        var start = await startResp.Content.ReadFromJsonAsync<RegisterStartResponse>();
        Assert.NotNull(start);
        Assert.Equal("email", start!.IdentifierType);

        var confirmResp = await client.PostAsJsonAsync("/auth/register/confirm", new
        {
            verification_id = start.VerificationId,
            code = "123456",
            password = "ValidPassw0rd!"
        });

        Assert.Equal(HttpStatusCode.OK, confirmResp.StatusCode);
        var register = await confirmResp.Content.ReadFromJsonAsync<RegisterResponse>();
        Assert.NotNull(register);
        Assert.False(string.IsNullOrWhiteSpace(register!.AccessToken));
        Assert.Equal(email.ToLowerInvariant(), register.Email);
        Assert.Null(register.Phone);

        var loginResp = await client.PostAsJsonAsync("/auth/login", new
        {
            tenant_id = tenantId,
            email,
            password = "ValidPassw0rd!"
        });

        Assert.Equal(HttpStatusCode.OK, loginResp.StatusCode);
    }

    [Fact]
    public async Task RegisterWithPhoneCreatesPhoneUsername()
    {
        var client = _factory.CreateClient();

        var tenantId = $"register-phone-{Guid.NewGuid():N}";
        var phone = "+15551234567";

        await CreateTenantAsync(tenantId);

        var startResp = await client.PostAsJsonAsync("/auth/register/start", new
        {
            tenant_id = tenantId,
            phone,
            display_name = "New Phone User"
        });

        Assert.Equal(HttpStatusCode.OK, startResp.StatusCode);
        var start = await startResp.Content.ReadFromJsonAsync<RegisterStartResponse>();
        Assert.NotNull(start);
        Assert.Equal("phone", start!.IdentifierType);

        var confirmResp = await client.PostAsJsonAsync("/auth/register/confirm", new
        {
            verification_id = start.VerificationId,
            code = "123456",
            password = "ValidPassw0rd!"
        });

        Assert.Equal(HttpStatusCode.OK, confirmResp.StatusCode);
        var register = await confirmResp.Content.ReadFromJsonAsync<RegisterResponse>();
        Assert.NotNull(register);
        Assert.False(string.IsNullOrWhiteSpace(register!.AccessToken));
        Assert.Equal(phone, register.Phone);
        Assert.Equal(phone, register.Username);
        Assert.Null(register.Email);

        var loginResp = await client.PostAsJsonAsync("/auth/login", new
        {
            tenant_id = tenantId,
            username = phone,
            password = "ValidPassw0rd!"
        });

        Assert.Equal(HttpStatusCode.OK, loginResp.StatusCode);
    }

    [Fact]
    public async Task LoginRespectsExplicitTenantWhenDefaultTenantConfigured()
    {
        var client = _factory.CreateClient();

        var tenantId = $"tenant-{Guid.NewGuid():N}";
        var email = $"explicit-{Guid.NewGuid():N}@example.com";
        var password = "ValidPassw0rd!";

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
            db.Users.Add(new AppUser
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                Email = email,
                DisplayName = "Explicit Tenant User",
                Status = "active",
                PasswordHash = BCrypt.Net.BCrypt.HashPassword(password),
                CreatedUtc = DateTime.UtcNow,
                UpdatedUtc = DateTime.UtcNow
            });
            await db.SaveChangesAsync();
        }

        var loginResp = await client.PostAsJsonAsync("/auth/login", new
        {
            tenant_id = tenantId,
            email,
            password
        });

        Assert.Equal(HttpStatusCode.OK, loginResp.StatusCode);
    }

    [Fact]
    public async Task LoginMatchesEmailCaseInsensitively()
    {
        var client = _factory.CreateClient();

        var tenantId = "tenant-1";
        var email = $"MixedCase-{Guid.NewGuid():N}@example.com";
        var password = "ValidPassw0rd!";

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
            db.Users.Add(new AppUser
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                Email = email,
                DisplayName = "Case Insensitive User",
                Status = "active",
                PasswordHash = BCrypt.Net.BCrypt.HashPassword(password),
                CreatedUtc = DateTime.UtcNow,
                UpdatedUtc = DateTime.UtcNow
            });
            await db.SaveChangesAsync();
        }

        var loginResp = await client.PostAsJsonAsync("/auth/login", new
        {
            tenant_id = tenantId,
            email = email.ToLowerInvariant(),
            password
        });

        Assert.Equal(HttpStatusCode.OK, loginResp.StatusCode);
    }

    [Fact]
    public async Task LoginWithoutTenantResolvesActiveMembershipByPassword()
    {
        var client = _factory.CreateClient();

        var tenantA = $"tenant-a-{Guid.NewGuid():N}";
        var tenantB = $"tenant-b-{Guid.NewGuid():N}";
        var username = $"shared-{Guid.NewGuid():N}";
        var passwordA = "FirstValid1!";
        var passwordB = "SecondValid1!";

        await CreateTenantAsync(tenantA);
        await CreateTenantAsync(tenantB);

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
            AddUserWithMembership(db, tenantA, username, $"{username}@a.example.com", passwordA, "active");
            AddUserWithMembership(db, tenantB, username, $"{username}@b.example.com", passwordB, "active");
            await db.SaveChangesAsync();
        }

        var loginResp = await client.PostAsJsonAsync("/auth/login", new
        {
            username,
            password = passwordB
        });

        Assert.Equal(HttpStatusCode.OK, loginResp.StatusCode);
        var login = await loginResp.Content.ReadFromJsonAsync<TokenResponse>();
        Assert.NotNull(login);
        Assert.Equal(tenantB, ReadTenantId(login!.AccessToken));
    }

    [Fact]
    public async Task LoginWithoutTenantIgnoresRevokedMembership()
    {
        var client = _factory.CreateClient();

        var revokedTenant = $"revoked-{Guid.NewGuid():N}";
        var activeTenant = $"active-{Guid.NewGuid():N}";
        var username = $"revoked-shared-{Guid.NewGuid():N}";
        var password = "SharedValid1!";

        await CreateTenantAsync(revokedTenant);
        await CreateTenantAsync(activeTenant);

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
            AddUserWithMembership(db, revokedTenant, username, $"{username}@revoked.example.com", password, "revoked");
            AddUserWithMembership(db, activeTenant, username, $"{username}@active.example.com", password, "active");
            await db.SaveChangesAsync();
        }

        var loginResp = await client.PostAsJsonAsync("/auth/login", new
        {
            username,
            password
        });

        Assert.Equal(HttpStatusCode.OK, loginResp.StatusCode);
        var login = await loginResp.Content.ReadFromJsonAsync<TokenResponse>();
        Assert.NotNull(login);
        Assert.Equal(activeTenant, ReadTenantId(login!.AccessToken));
    }

    [Fact]
    public async Task JwksReturnsKeys()
    {
        var client = _factory.CreateClient();
        var resp = await client.GetAsync("/jwks");
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var text = await resp.Content.ReadAsStringAsync();
        Assert.Contains("keys", text);
    }

    [Fact]
    public async Task JwksRotateReturnsNewKey()
    {
        var client = _factory.CreateClient();
        await SignInAsSuperAdminAsync(client);

        var initial = await client.GetStringAsync("/jwks");
        var initialKid = ReadFirstKid(initial);

        var rotated = await client.PostAsync("/jwks/rotate", null);
        Assert.Equal(HttpStatusCode.OK, rotated.StatusCode);
        var rotatedBody = await rotated.Content.ReadAsStringAsync();
        var rotatedKid = ReadFirstKid(rotatedBody);

        Assert.NotEqual(initialKid, rotatedKid);

        var after = await client.GetStringAsync("/jwks");
        var afterKid = ReadFirstKid(after);
        Assert.Equal(rotatedKid, afterKid);
    }

    [Fact]
    public async Task AdminCanSwitchAndRemoveUserFromTenants()
    {
        var client = _factory.CreateClient();
        await SignInAsSuperAdminAsync(client);

        var sourceTenantId = $"source-{Guid.NewGuid():N}";
        var targetTenantId = $"target-{Guid.NewGuid():N}";
        var userId = Guid.NewGuid();
        var roleId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
            db.Tenants.AddRange(
                new Tenant
                {
                    Id = Guid.NewGuid(),
                    TenantId = sourceTenantId,
                    Name = sourceTenantId,
                    Status = "active",
                    CreatedUtc = DateTime.UtcNow,
                    UpdatedUtc = DateTime.UtcNow
                },
                new Tenant
                {
                    Id = Guid.NewGuid(),
                    TenantId = targetTenantId,
                    Name = targetTenantId,
                    Status = "active",
                    CreatedUtc = DateTime.UtcNow,
                    UpdatedUtc = DateTime.UtcNow
                });
            db.Users.Add(new AppUser
            {
                Id = userId,
                TenantId = sourceTenantId,
                Email = $"move-{Guid.NewGuid():N}@example.com",
                Username = $"move-{Guid.NewGuid():N}",
                DisplayName = "Move User",
                Status = "active",
                PasswordHash = BCrypt.Net.BCrypt.HashPassword("ValidPassw0rd!"),
                CreatedUtc = DateTime.UtcNow,
                UpdatedUtc = DateTime.UtcNow
            });
            db.Memberships.Add(new Membership
            {
                Id = Guid.NewGuid(),
                TenantId = sourceTenantId,
                UserId = userId,
                Status = "active",
                CreatedUtc = DateTime.UtcNow,
                UpdatedUtc = DateTime.UtcNow
            });
            db.Roles.Add(new Role
            {
                Id = roleId,
                TenantId = sourceTenantId,
                Name = "operator",
                CreatedUtc = DateTime.UtcNow
            });
            db.UserRoles.Add(new UserRole
            {
                Id = Guid.NewGuid(),
                TenantId = sourceTenantId,
                UserId = userId,
                RoleId = roleId,
                CreatedUtc = DateTime.UtcNow
            });
            db.Sessions.Add(new UserSession
            {
                Id = sessionId,
                TenantId = sourceTenantId,
                UserId = userId,
                Status = "active",
                CreatedUtc = DateTime.UtcNow,
                ExpiresUtc = DateTime.UtcNow.AddHours(1)
            });
            db.RefreshTokens.Add(new RefreshToken
            {
                Id = Guid.NewGuid(),
                TenantId = sourceTenantId,
                UserId = userId,
                SessionId = sessionId,
                TokenHash = $"hash-{Guid.NewGuid():N}",
                IssuedUtc = DateTime.UtcNow,
                ExpiresUtc = DateTime.UtcNow.AddDays(1)
            });
            await db.SaveChangesAsync();
        }

        var switchResponse = await client.PostAsJsonAsync($"/admin/users/{userId}/tenant", new
        {
            tenant_id = sourceTenantId,
            target_tenant_id = targetTenantId,
            roles = new[] { "admin" }
        });

        Assert.Equal(HttpStatusCode.OK, switchResponse.StatusCode);

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
            var user = await db.Users.FindAsync(userId);
            Assert.NotNull(user);
            Assert.Equal(targetTenantId, user!.TenantId);
            Assert.Equal("revoked", db.Memberships.Single(x => x.UserId == userId && x.TenantId == sourceTenantId).Status);
            Assert.Equal("active", db.Memberships.Single(x => x.UserId == userId && x.TenantId == targetTenantId).Status);
            Assert.Empty(db.UserRoles.Where(x => x.UserId == userId && x.TenantId == sourceTenantId));
            Assert.Single(db.UserRoles.Where(x => x.UserId == userId && x.TenantId == targetTenantId));
            Assert.Equal("revoked", db.Sessions.Single(x => x.Id == sessionId).Status);
            Assert.NotNull(db.RefreshTokens.Single(x => x.UserId == userId && x.TenantId == sourceTenantId).RevokedUtc);
        }

        var removeResponse = await client.PostAsJsonAsync($"/admin/users/{userId}/remove", new
        {
            tenant_id = targetTenantId,
            reason = "integration test"
        });

        Assert.Equal(HttpStatusCode.OK, removeResponse.StatusCode);
        var listed = await client.GetFromJsonAsync<List<UserSummaryResponse>>($"/admin/users?tenant_id={targetTenantId}");
        Assert.NotNull(listed);
        Assert.DoesNotContain(listed!, user => user.UserId == userId);
    }

    [Fact]
    public async Task AdminCanSeeAndUnlockLockedUser()
    {
        var client = _factory.CreateClient();
        await SignInAsSuperAdminAsync(client);

        var tenantId = $"locked-{Guid.NewGuid():N}";
        var userId = Guid.NewGuid();
        var lockoutUntilUtc = DateTime.UtcNow.AddMinutes(10);

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
            db.Tenants.Add(new Tenant
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                Name = tenantId,
                Status = "active",
                CreatedUtc = DateTime.UtcNow,
                UpdatedUtc = DateTime.UtcNow
            });
            db.Users.Add(new AppUser
            {
                Id = userId,
                TenantId = tenantId,
                Email = $"locked-{Guid.NewGuid():N}@example.com",
                Username = $"locked-{Guid.NewGuid():N}",
                DisplayName = "Locked User",
                Status = "active",
                PasswordHash = BCrypt.Net.BCrypt.HashPassword("ValidPassw0rd!"),
                FailedLoginCount = 5,
                LockoutUntilUtc = lockoutUntilUtc,
                CreatedUtc = DateTime.UtcNow,
                UpdatedUtc = DateTime.UtcNow
            });
            await db.SaveChangesAsync();
        }

        var listed = await client.GetFromJsonAsync<List<UserSummaryResponse>>($"/admin/users?tenant_id={tenantId}");
        Assert.NotNull(listed);

        var lockedUser = Assert.Single(listed!, user => user.UserId == userId);
        Assert.Equal("locked", lockedUser.AccountState);
        Assert.True(lockedUser.IsLocked);
        Assert.Equal(5, lockedUser.FailedLoginCount);
        Assert.True(lockedUser.CanUnlock);
        Assert.NotNull(lockedUser.LockoutUntilUtc);

        var unlockResponse = await client.PostAsJsonAsync($"/admin/users/{userId}/unlock", new
        {
            tenant_id = tenantId,
            reason = "integration test"
        });
        Assert.Equal(HttpStatusCode.OK, unlockResponse.StatusCode);

        var unlockedUser = await unlockResponse.Content.ReadFromJsonAsync<UserSummaryResponse>();
        Assert.NotNull(unlockedUser);
        Assert.Equal("active", unlockedUser!.AccountState);
        Assert.False(unlockedUser.IsLocked);
        Assert.Equal(0, unlockedUser.FailedLoginCount);
        Assert.False(unlockedUser.CanUnlock);
        Assert.Null(unlockedUser.LockoutUntilUtc);

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
            var user = await db.Users.FindAsync(userId);
            Assert.NotNull(user);
            Assert.Equal(0, user!.FailedLoginCount);
            Assert.Null(user.LockoutUntilUtc);
        }
    }

    [Fact]
    public async Task LegacyAdminTenantRoleCannotUseAdminEndpointsOrRotateKeys()
    {
        var client = _factory.CreateClient();

        var userId = Guid.NewGuid();
        var username = $"tenant-admin-{Guid.NewGuid():N}";
        var password = "TenantAdmin1!";

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
            var role = db.Roles.FirstOrDefault(x => x.TenantId == "admin" && x.Name == "admin");
            if (role is null)
            {
                role = new Role
                {
                    Id = Guid.NewGuid(),
                    TenantId = "admin",
                    Name = "admin",
                    CreatedUtc = DateTime.UtcNow
                };
                db.Roles.Add(role);
            }

            db.Users.Add(new AppUser
            {
                Id = userId,
                TenantId = "admin",
                Username = username,
                Email = $"{username}@example.com",
                DisplayName = "Tenant Admin",
                Status = "active",
                PasswordHash = BCrypt.Net.BCrypt.HashPassword(password),
                CreatedUtc = DateTime.UtcNow,
                UpdatedUtc = DateTime.UtcNow
            });
            db.Memberships.Add(new Membership
            {
                Id = Guid.NewGuid(),
                TenantId = "admin",
                UserId = userId,
                Status = "active",
                CreatedUtc = DateTime.UtcNow,
                UpdatedUtc = DateTime.UtcNow
            });
            db.UserRoles.Add(new UserRole
            {
                Id = Guid.NewGuid(),
                TenantId = "admin",
                UserId = userId,
                RoleId = role.Id,
                CreatedUtc = DateTime.UtcNow
            });
            await db.SaveChangesAsync();
        }

        var loginResp = await client.PostAsJsonAsync("/auth/login", new
        {
            tenant_id = "admin",
            username,
            password
        });
        Assert.Equal(HttpStatusCode.OK, loginResp.StatusCode);
        var login = await loginResp.Content.ReadFromJsonAsync<TokenResponse>();
        Assert.NotNull(login);
        var token = new JwtSecurityTokenHandler().ReadJwtToken(login!.AccessToken);
        Assert.Contains(token.Claims, claim => claim.Type == "role" && claim.Value == "admin");
        Assert.Contains(token.Claims, claim => claim.Type == "cwi_admin" && claim.Value == "false");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", login!.AccessToken);

        var adminUsersResp = await client.GetAsync("/admin/users?tenant_id=admin");
        Assert.Equal(HttpStatusCode.Forbidden, adminUsersResp.StatusCode);

        var rotateResp = await client.PostAsync("/jwks/rotate", null);
        Assert.Equal(HttpStatusCode.Forbidden, rotateResp.StatusCode);
    }

    [Fact]
    public async Task SuperAdminWithoutTenantUsesConfiguredWorkspaceTenantAndKeepsAdministrationAccess()
    {
        var client = _factory.CreateClient();

        var loginResp = await client.PostAsJsonAsync("/auth/login", new
        {
            username = "superadmin",
            password = "ValidPassw0rd!"
        });

        Assert.Equal(HttpStatusCode.OK, loginResp.StatusCode);
        var login = await loginResp.Content.ReadFromJsonAsync<TokenResponse>();
        Assert.NotNull(login);
        Assert.Equal("CWI-ops", ReadTenantId(login!.AccessToken));

        var token = new JwtSecurityTokenHandler().ReadJwtToken(login.AccessToken);
        Assert.Contains(token.Claims, claim => claim.Type == "role" && claim.Value == "platform_admin");
        Assert.Contains(token.Claims, claim => claim.Type == "cwi_admin" && claim.Value == "true");
        Assert.Contains(token.Claims, claim => claim.Type == "is_superadmin" && claim.Value == "true");

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", login.AccessToken);
        var adminUsersResp = await client.GetAsync("/admin/users?tenant_id=CWI-ops");
        Assert.Equal(HttpStatusCode.OK, adminUsersResp.StatusCode);

        var rotateResp = await client.PostAsync("/jwks/rotate", null);
        Assert.Equal(HttpStatusCode.OK, rotateResp.StatusCode);
    }

    [Fact]
    public async Task TenantAdminOutsideLegacyAdminTenantRequiresPlatformAdminEntitlement()
    {
        var client = _factory.CreateClient();

        var tenantId = $"workspace-{Guid.NewGuid():N}";
        var userId = Guid.NewGuid();
        var username = $"workspace-admin-{Guid.NewGuid():N}";
        var password = "WorkspaceAdmin1!";

        await CreateTenantAsync(tenantId);
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
            db.Users.Add(new AppUser
            {
                Id = userId,
                TenantId = tenantId,
                Username = username,
                Email = $"{username}@example.com",
                DisplayName = "Workspace Admin",
                Status = "active",
                PasswordHash = BCrypt.Net.BCrypt.HashPassword(password),
                CreatedUtc = DateTime.UtcNow,
                UpdatedUtc = DateTime.UtcNow
            });
            db.Memberships.Add(new Membership
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                UserId = userId,
                Status = "active",
                CreatedUtc = DateTime.UtcNow,
                UpdatedUtc = DateTime.UtcNow
            });
            var adminRole = new Role
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                Name = "admin",
                CreatedUtc = DateTime.UtcNow
            };
            db.Roles.Add(adminRole);
            db.UserRoles.Add(new UserRole
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                UserId = userId,
                RoleId = adminRole.Id,
                CreatedUtc = DateTime.UtcNow
            });
            await db.SaveChangesAsync();
        }

        var loginResp = await client.PostAsJsonAsync("/auth/login", new
        {
            tenant_id = tenantId,
            username,
            password
        });
        Assert.Equal(HttpStatusCode.OK, loginResp.StatusCode);
        var login = await loginResp.Content.ReadFromJsonAsync<TokenResponse>();
        Assert.NotNull(login);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", login!.AccessToken);

        var denied = await client.GetAsync($"/admin/users?tenant_id={tenantId}");
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
            var platformRole = new Role
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                Name = "platform_admin",
                CreatedUtc = DateTime.UtcNow
            };
            db.Roles.Add(platformRole);
            db.UserRoles.Add(new UserRole
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                UserId = userId,
                RoleId = platformRole.Id,
                CreatedUtc = DateTime.UtcNow
            });
            await db.SaveChangesAsync();
        }

        client.DefaultRequestHeaders.Authorization = null;
        var platformLoginResp = await client.PostAsJsonAsync("/auth/login", new
        {
            tenant_id = tenantId,
            username,
            password
        });
        Assert.Equal(HttpStatusCode.OK, platformLoginResp.StatusCode);
        var platformLogin = await platformLoginResp.Content.ReadFromJsonAsync<TokenResponse>();
        Assert.NotNull(platformLogin);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", platformLogin!.AccessToken);

        var allowed = await client.GetAsync($"/admin/users?tenant_id={tenantId}");
        Assert.Equal(HttpStatusCode.OK, allowed.StatusCode);
    }

    private static async Task SignInAsSuperAdminAsync(HttpClient client)
    {
        var loginResp = await client.PostAsJsonAsync("/auth/login", new
        {
            tenant_id = "admin",
            username = "superadmin",
            password = "ValidPassw0rd!"
        });
        loginResp.EnsureSuccessStatusCode();
        var login = await loginResp.Content.ReadFromJsonAsync<TokenResponse>();
        Assert.NotNull(login);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", login!.AccessToken);
    }

    private async Task CreateTenantAsync(string tenantId)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        db.Tenants.Add(new Tenant
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            Name = tenantId,
            Status = "active",
            CreatedUtc = DateTime.UtcNow,
            UpdatedUtc = DateTime.UtcNow
        });
        await db.SaveChangesAsync();
    }

    private static void AddUserWithMembership(
        IdentityDbContext db,
        string tenantId,
        string username,
        string email,
        string password,
        string membershipStatus)
    {
        var user = new AppUser
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            Username = username,
            Email = email,
            DisplayName = username,
            Status = "active",
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(password),
            CreatedUtc = DateTime.UtcNow,
            UpdatedUtc = DateTime.UtcNow
        };
        db.Users.Add(user);
        db.Memberships.Add(new Membership
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            UserId = user.Id,
            Status = membershipStatus,
            CreatedUtc = DateTime.UtcNow,
            UpdatedUtc = DateTime.UtcNow
        });
    }

    private sealed record PasswordResetResponse(
        [property: JsonPropertyName("reset_token")] string ResetToken,
        [property: JsonPropertyName("expires_utc")] DateTime ExpiresUtc);

    private sealed record TokenResponse(
        [property: JsonPropertyName("access_token")] string AccessToken);

    private sealed record RegisterResponse(
        [property: JsonPropertyName("access_token")] string AccessToken,
        [property: JsonPropertyName("username")] string? Username,
        [property: JsonPropertyName("email")] string? Email,
        [property: JsonPropertyName("phone")] string? Phone);

    private sealed record RegisterStartResponse(
        [property: JsonPropertyName("verification_id")] Guid VerificationId,
        [property: JsonPropertyName("identifier_type")] string IdentifierType);

    private sealed record UserSummaryResponse(
        [property: JsonPropertyName("user_id")] Guid UserId,
        [property: JsonPropertyName("account_state")] string AccountState,
        [property: JsonPropertyName("is_locked")] bool IsLocked,
        [property: JsonPropertyName("lockout_until_utc")] DateTime? LockoutUntilUtc,
        [property: JsonPropertyName("failed_login_count")] int FailedLoginCount,
        [property: JsonPropertyName("can_unlock")] bool CanUnlock);

    private static string ReadFirstKid(string jwksJson)
    {
        using var doc = JsonDocument.Parse(jwksJson);
        return doc.RootElement.GetProperty("keys")[0].GetProperty("kid").GetString() ?? "";
    }

    private static string ReadTenantId(string accessToken)
    {
        var token = new JwtSecurityTokenHandler().ReadJwtToken(accessToken);
        return token.Claims.First(claim => claim.Type == "tenant_id").Value;
    }
}
