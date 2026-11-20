using Cwi.Common.Events;
using Cwi.Identity.Api.Data;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Mail;
using System.Threading.RateLimiting;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddEventBusClient();

var httpPort = GetIntFromEnv("IDENTITY_HTTP_PORT", 8085);
builder.WebHost.UseUrls($"http://0.0.0.0:{httpPort}");

var corsOriginsRaw = Environment.GetEnvironmentVariable("IDENTITY_CORS_ORIGINS");
if (string.IsNullOrWhiteSpace(corsOriginsRaw))
{
    throw new InvalidOperationException("IDENTITY_CORS_ORIGINS is required.");
}
var corsOrigins = corsOriginsRaw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

var connectionString = Environment.GetEnvironmentVariable("IDENTITY_DB_CONNECTION");
if (string.IsNullOrWhiteSpace(connectionString))
{
    throw new InvalidOperationException("IDENTITY_DB_CONNECTION is required.");
}

var issuer = Environment.GetEnvironmentVariable("IDENTITY_JWT_ISSUER") ?? "cwi-identity";
var audience = Environment.GetEnvironmentVariable("IDENTITY_JWT_AUDIENCE") ?? "cwi";
var accessTokenMinutes = GetIntFromEnv("IDENTITY_ACCESS_TOKEN_MINUTES", 60);
var refreshTokenDays = GetIntFromEnv("IDENTITY_REFRESH_TOKEN_DAYS", 7);
var resetTokenMinutes = GetIntFromEnv("IDENTITY_RESET_TOKEN_MINUTES", 30);
var inviteTokenMinutes = GetIntFromEnv("IDENTITY_INVITE_TOKEN_MINUTES", 1440);
var lockoutMinutes = GetIntFromEnv("IDENTITY_LOCKOUT_MINUTES", 15);
var lockoutThreshold = GetIntFromEnv("IDENTITY_LOCKOUT_THRESHOLD", 5);
var rateLimitWindowSeconds = GetIntFromEnv("IDENTITY_RATE_LIMIT_WINDOW_SECONDS", 60);
var rateLimitPermitLimit = GetIntFromEnv("IDENTITY_RATE_LIMIT_PER_WINDOW", 30);
var failedLoginDelayMs = GetIntFromEnv("IDENTITY_FAILED_LOGIN_DELAY_MS", 150);
var bootstrapTenantId = Environment.GetEnvironmentVariable("IDENTITY_BOOTSTRAP_TENANT_ID");
var bootstrapAdminEmail = Environment.GetEnvironmentVariable("IDENTITY_BOOTSTRAP_ADMIN_EMAIL");
var bootstrapAdminUsername = Environment.GetEnvironmentVariable("IDENTITY_BOOTSTRAP_ADMIN_USERNAME");
var bootstrapAdminPassword = Environment.GetEnvironmentVariable("IDENTITY_BOOTSTRAP_ADMIN_PASSWORD");
var defaultTenantId = Environment.GetEnvironmentVariable("IDENTITY_DEFAULT_TENANT") ?? bootstrapTenantId;
var bootstrapAdminName = Environment.GetEnvironmentVariable("IDENTITY_BOOTSTRAP_ADMIN_NAME") ?? "Admin";
var superAdminTenantId = Environment.GetEnvironmentVariable("IDENTITY_SUPERADMIN_TENANT_ID");
var superAdminUsername = Environment.GetEnvironmentVariable("IDENTITY_SUPERADMIN_USERNAME");
var superAdminPassword = Environment.GetEnvironmentVariable("IDENTITY_SUPERADMIN_PASSWORD");
var superAdminEmail = Environment.GetEnvironmentVariable("IDENTITY_SUPERADMIN_EMAIL");
var superAdminName = Environment.GetEnvironmentVariable("IDENTITY_SUPERADMIN_NAME") ?? "Super Admin";
var superAdminLoginTenantId = Environment.GetEnvironmentVariable("IDENTITY_SUPERADMIN_LOGIN_TENANT_ID")
    ?? Environment.GetEnvironmentVariable("IDENTITY_SUPERADMIN_WORKSPACE_TENANT_ID");
var platformAdminRoleName = Environment.GetEnvironmentVariable("IDENTITY_PLATFORM_ADMIN_ROLE")?.Trim();
if (string.IsNullOrWhiteSpace(platformAdminRoleName))
{
    platformAdminRoleName = "platform_admin";
}
var superAdminAliases = BuildSuperAdminUsernames(superAdminUsername, Environment.GetEnvironmentVariable("IDENTITY_SUPERADMIN_ALIASES"));
var superAdminTenantIds = BuildTenantPriority(superAdminTenantId, superAdminLoginTenantId);
var loginTenantPriority = BuildTenantPriority(
    Environment.GetEnvironmentVariable("IDENTITY_LOGIN_TENANT_PRIORITY"),
    superAdminLoginTenantId,
    defaultTenantId,
    bootstrapTenantId,
    superAdminTenantId);
var keyEncryptionKey = Environment.GetEnvironmentVariable("IDENTITY_KEY_ENCRYPTION_KEY");
if (string.IsNullOrWhiteSpace(keyEncryptionKey))
{
    if (builder.Environment.IsDevelopment())
    {
        keyEncryptionKey = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
    }
    else
    {
        throw new InvalidOperationException("IDENTITY_KEY_ENCRYPTION_KEY is required.");
    }
}

builder.Services.AddDbContext<IdentityDbContext>(options =>
{
    options.UseNpgsql(connectionString);
});

builder.Services.AddSingleton<SigningKeyStore>(sp =>
    new SigningKeyStore(keyEncryptionKey, sp.GetRequiredService<IServiceScopeFactory>()));

builder.Services.AddCors(options =>
{
    options.AddPolicy("identity-admin", policy =>
    {
        policy.WithOrigins(corsOrigins)
            .AllowAnyHeader()
            .AllowAnyMethod();
    });
});

builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy("auth", httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            $"{httpContext.Connection.RemoteIpAddress}-{httpContext.Request.Path}",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = rateLimitPermitLimit,
                Window = TimeSpan.FromSeconds(rateLimitWindowSeconds),
                QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                QueueLimit = 0,
                AutoReplenishment = true
            }));
});

var app = builder.Build();
AttachEventBus(app);

app.UseCors("identity-admin");

app.UseRateLimiter();

app.Use(async (httpContext, next) =>
{
    if (!IsSuperAdminProtectedPath(httpContext.Request.Path))
    {
        await next();
        return;
    }

    var authFailure = await AuthorizeSuperAdminAsync(
        httpContext,
        httpContext.RequestServices.GetRequiredService<IdentityDbContext>(),
        app.Services.GetRequiredService<SigningKeyStore>(),
        issuer,
        audience,
        superAdminTenantIds,
        superAdminUsername,
        platformAdminRoleName,
        allowAdminRole: httpContext.Request.Path.StartsWithSegments("/admin", StringComparison.OrdinalIgnoreCase));

    if (authFailure is not null)
    {
        await authFailure.ExecuteAsync(httpContext);
        return;
    }

    await next();
});

if (args.Contains("--migrate", StringComparer.OrdinalIgnoreCase))
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
    db.Database.Migrate();
    return;
}

if (!string.IsNullOrWhiteSpace(bootstrapTenantId) &&
    !string.IsNullOrWhiteSpace(bootstrapAdminEmail) &&
    !string.IsNullOrWhiteSpace(bootstrapAdminPassword))
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
    EnsureBootstrapAdmin(db, bootstrapTenantId, bootstrapAdminEmail, bootstrapAdminUsername, bootstrapAdminPassword, bootstrapAdminName, app.Logger);
}

if (!string.IsNullOrWhiteSpace(superAdminTenantId) &&
    !string.IsNullOrWhiteSpace(superAdminUsername) &&
    !string.IsNullOrWhiteSpace(superAdminPassword))
{
    if (string.IsNullOrWhiteSpace(superAdminEmail))
    {
        throw new InvalidOperationException("IDENTITY_SUPERADMIN_EMAIL is required when configuring a super admin.");
    }
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
    foreach (var tenantId in superAdminTenantIds.Count > 0 ? superAdminTenantIds : new[] { superAdminTenantId })
    {
        EnsureSuperAdmin(
            db,
            tenantId,
            superAdminUsername,
            superAdminEmail,
            superAdminPassword,
            superAdminName,
            platformAdminRoleName,
            app.Logger);
    }
}

app.MapGet("/healthz", async (IdentityDbContext db) =>
{
    var canConnect = await db.Database.CanConnectAsync();
    return BuildHealthResult(canConnect, "ok");
});
app.MapGet("/readyz", async (IdentityDbContext db) =>
{
    var canConnect = await db.Database.CanConnectAsync();
    return BuildHealthResult(canConnect, "ready");
});

static IResult BuildHealthResult(bool canConnect, string okStatus)
{
    var payload = new
    {
        status = canConnect ? okStatus : "down",
        checks = new
        {
            database = canConnect ? "ok" : "down"
        }
    };

    return canConnect ? Results.Ok(payload) : Results.Json(payload, statusCode: 503);
}

app.MapPost("/auth/login", async (LoginRequest request, IdentityDbContext db, HttpContext httpContext) =>
{
    var errors = ValidateLogin(request, superAdminAliases, superAdminTenantId, defaultTenantId);
    if (errors.Count > 0)
    {
        return Results.BadRequest(new ErrorResponse("invalid_request", "Request validation failed.", errors));
    }

    var normalizedUsername = string.IsNullOrWhiteSpace(request.Username) ? null : request.Username.Trim();
    var normalizedEmail = string.IsNullOrWhiteSpace(request.Email) ? null : request.Email.Trim();
    var usingUsername = !string.IsNullOrWhiteSpace(normalizedUsername);
    var resolvedTenantId = string.IsNullOrWhiteSpace(request.TenantId) ? null : request.TenantId.Trim();
    var isSuperAdminLogin = normalizedUsername is not null &&
        !string.IsNullOrWhiteSpace(superAdminTenantId) &&
        superAdminAliases.Contains(normalizedUsername);
    var lookupUsername = isSuperAdminLogin && !string.IsNullOrWhiteSpace(superAdminUsername)
        ? superAdminUsername
        : normalizedUsername;
    if (isSuperAdminLogin && string.IsNullOrWhiteSpace(resolvedTenantId))
    {
        resolvedTenantId = string.IsNullOrWhiteSpace(superAdminLoginTenantId)
            ? superAdminTenantId
            : superAdminLoginTenantId.Trim();
    }
    var lookupUsernameKey = lookupUsername?.ToLowerInvariant();
    var normalizedEmailKey = normalizedEmail?.ToLowerInvariant();
    AppUser? user;
    var passwordVerifiedByResolver = false;
    TenantlessLoginResolution? tenantlessResolution = null;
    if (string.IsNullOrWhiteSpace(resolvedTenantId))
    {
        tenantlessResolution = await ResolveTenantlessLoginAsync(
            db,
            usingUsername,
            lookupUsernameKey,
            normalizedEmailKey,
            request.Password,
            loginTenantPriority);
        user = tenantlessResolution.User;
        passwordVerifiedByResolver = user is not null;
        resolvedTenantId = user?.TenantId;
    }
    else
    {
        user = usingUsername
            ? await db.Users.FirstOrDefaultAsync(x =>
                x.TenantId == resolvedTenantId &&
                x.Username != null &&
                x.Username.ToLower() == lookupUsernameKey &&
                x.Status == "active")
            : await db.Users.FirstOrDefaultAsync(x =>
                x.TenantId == resolvedTenantId &&
                x.Email.ToLower() == normalizedEmailKey &&
                x.Status == "active");
    }

    if (user is null)
    {
        if (tenantlessResolution is not null)
        {
            foreach (var candidate in tenantlessResolution.Candidates.Where(candidate =>
                         candidate.LockoutUntilUtc is null || candidate.LockoutUntilUtc <= DateTime.UtcNow))
            {
                candidate.FailedLoginCount += 1;
                if (candidate.FailedLoginCount >= lockoutThreshold)
                {
                    candidate.LockoutUntilUtc = DateTime.UtcNow.AddMinutes(lockoutMinutes);
                }
            }
        }

        AddAuditEvent(db, resolvedTenantId ?? request.TenantId ?? "tenantless", null, "auth.login_failed", new
        {
            reason = "invalid_credentials",
            identifier = usingUsername ? normalizedUsername : normalizedEmail,
            identifier_type = usingUsername ? "username" : "email",
            candidate_count = tenantlessResolution?.Candidates.Count,
            ip = GetClientIp(httpContext),
            user_agent = GetUserAgent(httpContext)
        });
        await db.SaveChangesAsync();
        await Task.Delay(failedLoginDelayMs);
        return Results.Unauthorized();
    }

    if (user.LockoutUntilUtc is not null && user.LockoutUntilUtc > DateTime.UtcNow)
    {
        AddAuditEvent(db, user.TenantId, user.Id, "auth.login_failed", new
        {
            reason = "locked_out",
            ip = GetClientIp(httpContext),
            user_agent = GetUserAgent(httpContext)
        });
        await db.SaveChangesAsync();
        await Task.Delay(failedLoginDelayMs);
        return Results.Unauthorized();
    }

    if (!passwordVerifiedByResolver && !VerifyPassword(request.Password, user.PasswordHash))
    {
        user.FailedLoginCount += 1;
        if (user.FailedLoginCount >= lockoutThreshold)
        {
            user.LockoutUntilUtc = DateTime.UtcNow.AddMinutes(lockoutMinutes);
        }
        AddAuditEvent(db, user.TenantId, user.Id, "auth.login_failed", new
        {
            reason = "invalid_credentials",
            identifier = usingUsername ? normalizedUsername : normalizedEmail,
            identifier_type = usingUsername ? "username" : "email",
            ip = GetClientIp(httpContext),
            user_agent = GetUserAgent(httpContext)
        });
        await db.SaveChangesAsync();
        await Task.Delay(failedLoginDelayMs);
        return Results.Unauthorized();
    }

    user.FailedLoginCount = 0;
    user.LockoutUntilUtc = null;

    var sessionId = Guid.NewGuid();
    var session = new UserSession
    {
        Id = sessionId,
        TenantId = resolvedTenantId ?? request.TenantId ?? user.TenantId,
        UserId = user.Id,
        Status = "active",
        CreatedUtc = DateTime.UtcNow,
        ExpiresUtc = DateTime.UtcNow.AddMinutes(accessTokenMinutes)
    };

    var refreshToken = GenerateToken();
    var refreshTokenHash = HashToken(refreshToken);
    var refresh = new RefreshToken
    {
        Id = Guid.NewGuid(),
        TenantId = resolvedTenantId ?? request.TenantId ?? user.TenantId,
        UserId = user.Id,
        SessionId = sessionId,
        TokenHash = refreshTokenHash,
        IssuedUtc = DateTime.UtcNow,
        ExpiresUtc = DateTime.UtcNow.AddDays(refreshTokenDays),
        RevokedUtc = null
    };

    db.Sessions.Add(session);
    db.RefreshTokens.Add(refresh);
    AddAuditEvent(db, user.TenantId, user.Id, "auth.login", new
    {
        session_id = sessionId,
        identifier = usingUsername ? normalizedUsername : normalizedEmail,
        identifier_type = usingUsername ? "username" : "email",
        ip = GetClientIp(httpContext),
        user_agent = GetUserAgent(httpContext)
    });
    await db.SaveChangesAsync();

    var isSuperAdmin = IsConfiguredSuperAdmin(user, superAdminTenantIds, superAdminUsername);
    var roleNames = await GetRoleNamesAsync(db, user.TenantId, user.Id);
    var canAccessAdministration = CanAccessAdministration(
        user,
        roleNames,
        isSuperAdmin,
        platformAdminRoleName);
    var accessToken = await IssueAccessToken(
        issuer,
        audience,
        user,
        sessionId,
        user.TokenVersion,
        DateTime.UtcNow.AddMinutes(accessTokenMinutes),
        app.Services.GetRequiredService<SigningKeyStore>(),
        roleNames,
        isSuperAdmin,
        canAccessAdministration);

    var response = new TokenResponse(
        AccessToken: accessToken,
        RefreshToken: refreshToken,
        ExpiresUtc: DateTime.UtcNow.AddMinutes(accessTokenMinutes));

    return Results.Ok(response);
}).RequireRateLimiting("auth");

app.MapPost("/auth/register", () =>
    Results.Json(
        new ErrorResponse("verification_required", "Use /auth/register/start and /auth/register/confirm.", null),
        statusCode: StatusCodes.Status409Conflict)).RequireRateLimiting("auth");

app.MapPost("/auth/register/start", async (RegisterStartRequest request, IdentityDbContext db, HttpContext httpContext) =>
{
    var errors = ValidateRegisterStart(request, defaultTenantId);
    if (errors.Count > 0)
    {
        return Results.BadRequest(new ErrorResponse("invalid_request", "Request validation failed.", errors));
    }

    var registration = ResolveRegistrationIdentity(request.Email, request.Phone, request.Username);
    if (registration is null)
    {
        return Results.BadRequest(new ErrorResponse("invalid_request", "Valid email or phone is required.", new List<string> { "valid email or phone is required" }));
    }

    var resolvedTenantId = string.IsNullOrWhiteSpace(request.TenantId)
        ? defaultTenantId?.Trim()
        : request.TenantId.Trim();
    if (string.IsNullOrWhiteSpace(resolvedTenantId))
    {
        return Results.Json(
            new ErrorResponse("default_tenant_missing", "Default tenant is not configured.", null),
            statusCode: StatusCodes.Status503ServiceUnavailable);
    }

    var tenant = await db.Tenants.FirstOrDefaultAsync(x => x.TenantId == resolvedTenantId);
    if (tenant is null || tenant.Status != "active")
    {
        return Results.NotFound(new ErrorResponse("tenant_not_found", "Tenant is not available for registration.", null));
    }

    var userEmailKey = registration.UserEmail.ToLowerInvariant();
    var existingEmailUser = await db.Users.FirstOrDefaultAsync(x =>
        x.TenantId == resolvedTenantId &&
        x.Email.ToLower() == userEmailKey);
    if (existingEmailUser is not null &&
        !string.Equals(existingEmailUser.Status, "registration_pending", StringComparison.OrdinalIgnoreCase))
    {
        return Results.Conflict(new ErrorResponse("user_exists", "An account already exists for this email or phone.", null));
    }

    AppUser? user = existingEmailUser;
    if (registration.Username is not null)
    {
        var usernameKey = registration.Username.ToLowerInvariant();
        var existingUsernameUser = await db.Users.FirstOrDefaultAsync(x =>
            x.TenantId == resolvedTenantId &&
            x.Username != null &&
            x.Username.ToLower() == usernameKey);
        if (existingUsernameUser is not null &&
            !string.Equals(existingUsernameUser.Status, "registration_pending", StringComparison.OrdinalIgnoreCase))
        {
            return Results.Conflict(new ErrorResponse("username_exists", "Username or phone number is already in use.", null));
        }

        if (existingUsernameUser is not null && user is not null && user.Id != existingUsernameUser.Id)
        {
            return Results.Conflict(new ErrorResponse("username_exists", "Username or phone number is already in use.", null));
        }
        user ??= existingUsernameUser;
    }

    var now = DateTime.UtcNow;
    if (user is null)
    {
        user = new AppUser
        {
            Id = Guid.NewGuid(),
            TenantId = resolvedTenantId,
            Username = registration.Username,
            Email = registration.UserEmail,
            DisplayName = NormalizeDisplayName(request.DisplayName) ?? registration.PublicIdentifier,
            Status = "registration_pending",
            PasswordHash = "",
            CreatedUtc = now,
            UpdatedUtc = now
        };
        db.Users.Add(user);
    }
    else
    {
        user.Username = registration.Username ?? user.Username;
        user.DisplayName = NormalizeDisplayName(request.DisplayName) ?? user.DisplayName;
        user.Status = "registration_pending";
        user.PasswordHash = "";
        user.FailedLoginCount = 0;
        user.LockoutUntilUtc = null;
        user.UpdatedUtc = now;
    }

    var membership = await db.Memberships.FirstOrDefaultAsync(x => x.TenantId == resolvedTenantId && x.UserId == user.Id);
    if (membership is null)
    {
        db.Memberships.Add(new Membership
        {
            Id = Guid.NewGuid(),
            TenantId = resolvedTenantId,
            UserId = user.Id,
            Status = "active",
            CreatedUtc = now,
            UpdatedUtc = now
        });
    }

    var previousInvites = await db.InviteTokens
        .Where(x => x.TenantId == resolvedTenantId && x.UserId == user.Id && x.UsedUtc == null)
        .ToListAsync();
    foreach (var previous in previousInvites)
    {
        previous.UsedUtc = now;
    }

    var code = GenerateVerificationCode(app.Environment);
    var inviteId = Guid.NewGuid();
    var invite = new InviteToken
    {
        Id = inviteId,
        TenantId = resolvedTenantId,
        UserId = user.Id,
        Email = registration.PublicIdentifier,
        TokenHash = HashRegistrationVerificationToken(inviteId, code),
        IssuedUtc = now,
        ExpiresUtc = now.AddMinutes(inviteTokenMinutes),
        ApprovedUtc = now,
        UsedUtc = null
    };
    db.InviteTokens.Add(invite);
    await db.SaveChangesAsync();

    var delivery = await SendRegistrationVerificationAsync(registration.IdentifierType, registration.DeliveryTarget, code, app.Environment, app.Logger);
    AddAuditEvent(db, resolvedTenantId, user.Id, delivery.Sent ? "auth.registration_verification_sent" : "auth.registration_verification_failed", new
    {
        identifier_type = registration.IdentifierType,
        identifier = registration.PublicIdentifier,
        delivery_status = delivery.Status,
        delivery_method = delivery.Method,
        verification_id = invite.Id,
        ip = GetClientIp(httpContext),
        user_agent = GetUserAgent(httpContext)
    });
    await db.SaveChangesAsync();

    if (!delivery.Sent)
    {
        return Results.Json(
            new ErrorResponse("verification_delivery_failed", delivery.Message, new List<string> { delivery.Status }),
            statusCode: StatusCodes.Status503ServiceUnavailable);
    }

    return Results.Ok(new RegisterStartResponse(
        VerificationId: invite.Id,
        IdentifierType: registration.IdentifierType,
        MaskedTarget: MaskIdentifier(registration.IdentifierType, registration.DeliveryTarget),
        ExpiresUtc: invite.ExpiresUtc,
        DeliveryMethod: delivery.Method));
}).RequireRateLimiting("auth");

app.MapPost("/auth/register/confirm", async (RegisterConfirmRequest request, IdentityDbContext db, HttpContext httpContext) =>
{
    var errors = ValidateRegisterConfirm(request);
    if (errors.Count > 0)
    {
        return Results.BadRequest(new ErrorResponse("invalid_request", "Request validation failed.", errors));
    }

    var passwordErrors = ValidatePasswordPolicy(request.Password);
    if (passwordErrors.Count > 0)
    {
        return Results.BadRequest(new ErrorResponse("weak_password", "Password does not meet policy.", passwordErrors));
    }

    var invite = await db.InviteTokens.FirstOrDefaultAsync(x => x.Id == request.VerificationId);
    if (invite is null || invite.UsedUtc is not null || invite.ExpiresUtc <= DateTime.UtcNow)
    {
        return Results.Unauthorized();
    }

    var user = await db.Users.FirstOrDefaultAsync(x => x.Id == invite.UserId && x.TenantId == invite.TenantId);
    if (user is null || !string.Equals(user.Status, "registration_pending", StringComparison.OrdinalIgnoreCase))
    {
        return Results.Unauthorized();
    }

    var scopedVerificationHash = HashRegistrationVerificationToken(invite.Id, request.Code.Trim());
    var legacyVerificationHash = HashToken(request.Code.Trim());
    if (!string.Equals(invite.TokenHash, scopedVerificationHash, StringComparison.Ordinal) &&
        !string.Equals(invite.TokenHash, legacyVerificationHash, StringComparison.Ordinal))
    {
        user.FailedLoginCount += 1;
        if (user.FailedLoginCount >= 5)
        {
            invite.UsedUtc = DateTime.UtcNow;
        }
        AddAuditEvent(db, invite.TenantId, user.Id, "auth.registration_verification_failed", new
        {
            reason = "invalid_code",
            verification_id = invite.Id,
            attempt_count = user.FailedLoginCount,
            ip = GetClientIp(httpContext),
            user_agent = GetUserAgent(httpContext)
        });
        await db.SaveChangesAsync();
        return Results.Unauthorized();
    }

    var now = DateTime.UtcNow;
    user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(request.Password);
    user.Status = "active";
    user.TokenVersion += 1;
    user.FailedLoginCount = 0;
    user.LockoutUntilUtc = null;
    user.UpdatedUtc = now;
    invite.UsedUtc = now;

    var sessionId = Guid.NewGuid();
    db.Sessions.Add(new UserSession
    {
        Id = sessionId,
        TenantId = invite.TenantId,
        UserId = user.Id,
        Status = "active",
        CreatedUtc = now,
        ExpiresUtc = now.AddMinutes(accessTokenMinutes)
    });

    var refreshToken = GenerateToken();
    db.RefreshTokens.Add(new RefreshToken
    {
        Id = Guid.NewGuid(),
        TenantId = invite.TenantId,
        UserId = user.Id,
        SessionId = sessionId,
        TokenHash = HashToken(refreshToken),
        IssuedUtc = now,
        ExpiresUtc = now.AddDays(refreshTokenDays),
        RevokedUtc = null
    });

    AddAuditEvent(db, invite.TenantId, user.Id, "auth.register", new
    {
        verification_id = invite.Id,
        identifier = invite.Email,
        ip = GetClientIp(httpContext),
        user_agent = GetUserAgent(httpContext)
    });
    await db.SaveChangesAsync();

    var roleNames = await GetRoleNamesAsync(db, user.TenantId, user.Id);
    var accessToken = await IssueAccessToken(
        issuer,
        audience,
        user,
        sessionId,
        user.TokenVersion,
        now.AddMinutes(accessTokenMinutes),
        app.Services.GetRequiredService<SigningKeyStore>(),
        roleNames,
        isSuperAdmin: false,
        canAccessAdministration: CanAccessAdministration(
            user,
            roleNames,
            isSuperAdmin: false,
            platformAdminRoleName: platformAdminRoleName));

    return Results.Ok(new RegisterResponse(
        AccessToken: accessToken,
        RefreshToken: refreshToken,
        ExpiresUtc: now.AddMinutes(accessTokenMinutes),
        TenantId: invite.TenantId,
        UserId: user.Id,
        Username: user.Username,
        Email: IsPhoneBackedEmail(user.Email) ? null : user.Email,
        Phone: IsPhoneBackedEmail(user.Email) ? user.Username : null,
        DisplayName: user.DisplayName));
}).RequireRateLimiting("auth");

app.MapPost("/auth/refresh", async (RefreshRequest request, IdentityDbContext db, HttpContext httpContext) =>
{
    var errors = ValidateRefresh(request);
    if (errors.Count > 0)
    {
        return Results.BadRequest(new ErrorResponse("invalid_request", "Request validation failed.", errors));
    }

    var tokenHash = HashToken(request.RefreshToken);
    var existing = await db.RefreshTokens.FirstOrDefaultAsync(x => x.TokenHash == tokenHash);
    if (existing is null || existing.RevokedUtc is not null || existing.ExpiresUtc <= DateTime.UtcNow)
    {
        AddAuditEvent(db, "unknown", null, "auth.refresh_failed", new
        {
            reason = "invalid_refresh_token",
            token_hash_prefix = tokenHash[..8],
            ip = GetClientIp(httpContext),
            user_agent = GetUserAgent(httpContext)
        });
        await db.SaveChangesAsync();
        return Results.Unauthorized();
    }

    existing.RevokedUtc = DateTime.UtcNow;
    var user = await db.Users.FirstOrDefaultAsync(x => x.Id == existing.UserId);
    if (user is null || user.Status != "active")
    {
        AddAuditEvent(db, existing.TenantId, existing.UserId, "auth.refresh_failed", new
        {
            reason = "user_inactive",
            token_hash_prefix = tokenHash[..8],
            ip = GetClientIp(httpContext),
            user_agent = GetUserAgent(httpContext)
        });
        await db.SaveChangesAsync();
        return Results.Unauthorized();
    }

    var newRefreshToken = GenerateToken();
    var newRefresh = new RefreshToken
    {
        Id = Guid.NewGuid(),
        TenantId = existing.TenantId,
        UserId = existing.UserId,
        SessionId = existing.SessionId,
        TokenHash = HashToken(newRefreshToken),
        IssuedUtc = DateTime.UtcNow,
        ExpiresUtc = DateTime.UtcNow.AddDays(refreshTokenDays),
        RevokedUtc = null
    };

    db.RefreshTokens.Add(newRefresh);
    AddAuditEvent(db, existing.TenantId, existing.UserId, "auth.refresh", new
    {
        session_id = existing.SessionId,
        token_hash_prefix = tokenHash[..8],
        ip = GetClientIp(httpContext),
        user_agent = GetUserAgent(httpContext)
    });
    await db.SaveChangesAsync();

    var isSuperAdmin = IsConfiguredSuperAdmin(user, superAdminTenantIds, superAdminUsername);
    var roleNames = await GetRoleNamesAsync(db, user.TenantId, user.Id);
    var canAccessAdministration = CanAccessAdministration(
        user,
        roleNames,
        isSuperAdmin,
        platformAdminRoleName);
    var accessToken = await IssueAccessToken(
        issuer,
        audience,
        user,
        existing.SessionId,
        user.TokenVersion,
        DateTime.UtcNow.AddMinutes(accessTokenMinutes),
        app.Services.GetRequiredService<SigningKeyStore>(),
        roleNames,
        isSuperAdmin,
        canAccessAdministration);

    var response = new TokenResponse(
        AccessToken: accessToken,
        RefreshToken: newRefreshToken,
        ExpiresUtc: DateTime.UtcNow.AddMinutes(accessTokenMinutes));

    return Results.Ok(response);
}).RequireRateLimiting("auth");

app.MapPost("/auth/logout", async (LogoutRequest request, IdentityDbContext db, HttpContext httpContext) =>
{
    var errors = ValidateLogout(request);
    if (errors.Count > 0)
    {
        return Results.BadRequest(new ErrorResponse("invalid_request", "Request validation failed.", errors));
    }

    var tokenHash = HashToken(request.RefreshToken);
    var existing = await db.RefreshTokens.FirstOrDefaultAsync(x => x.TokenHash == tokenHash);
    if (existing is null)
    {
        AddAuditEvent(db, "unknown", null, "auth.logout", new
        {
            token_found = false,
            token_hash_prefix = tokenHash[..8],
            ip = GetClientIp(httpContext),
            user_agent = GetUserAgent(httpContext)
        });
        await db.SaveChangesAsync();
        return Results.Ok();
    }

    existing.RevokedUtc = DateTime.UtcNow;
    var session = await db.Sessions.FirstOrDefaultAsync(x => x.Id == existing.SessionId);
    if (session is not null)
    {
        session.Status = "revoked";
    }

    AddAuditEvent(db, existing.TenantId, existing.UserId, "auth.logout", new
    {
        token_found = true,
        session_id = existing.SessionId,
        token_hash_prefix = tokenHash[..8],
        ip = GetClientIp(httpContext),
        user_agent = GetUserAgent(httpContext)
    });
    await db.SaveChangesAsync();
    return Results.Ok();
}).RequireRateLimiting("auth");

app.MapPost("/auth/password/set", async (PasswordSetRequest request, IdentityDbContext db) =>
{
    var errors = ValidatePasswordSet(request);
    if (errors.Count > 0)
    {
        return Results.BadRequest(new ErrorResponse("invalid_request", "Request validation failed.", errors));
    }

    var user = await db.Users.FirstOrDefaultAsync(x =>
        x.TenantId == request.TenantId &&
        x.Email == request.Email);

    if (user is null)
    {
        return Results.NotFound();
    }

    var passwordErrors = ValidatePasswordPolicy(request.Password);
    if (passwordErrors.Count > 0)
    {
        return Results.BadRequest(new ErrorResponse("weak_password", "Password does not meet policy.", passwordErrors));
    }

    var hashed = BCrypt.Net.BCrypt.HashPassword(request.Password);
    user.PasswordHash = hashed;
    user.TokenVersion += 1;
    user.FailedLoginCount = 0;
    user.LockoutUntilUtc = null;
    await db.SaveChangesAsync();

    return Results.Ok();
}).RequireRateLimiting("auth");

app.MapPost("/auth/password/reset", async (PasswordResetRequest request, IdentityDbContext db) =>
{
    var errors = ValidatePasswordReset(request);
    if (errors.Count > 0)
    {
        return Results.BadRequest(new ErrorResponse("invalid_request", "Request validation failed.", errors));
    }

    var user = await db.Users.FirstOrDefaultAsync(x =>
        x.TenantId == request.TenantId &&
        x.Email == request.Email &&
        x.Status == "active");

    if (user is null)
    {
        return Results.Ok();
    }

    var token = GenerateToken();
    var reset = new PasswordResetToken
    {
        Id = Guid.NewGuid(),
        TenantId = user.TenantId,
        UserId = user.Id,
        TokenHash = HashToken(token),
        IssuedUtc = DateTime.UtcNow,
        ExpiresUtc = DateTime.UtcNow.AddMinutes(resetTokenMinutes),
        UsedUtc = null
    };
    db.PasswordResetTokens.Add(reset);
    await db.SaveChangesAsync();

    return Results.Ok(new PasswordResetResponse(token, reset.ExpiresUtc));
}).RequireRateLimiting("auth");

app.MapPost("/auth/password/reset/complete", async (PasswordResetCompleteRequest request, IdentityDbContext db) =>
{
    var errors = ValidatePasswordResetComplete(request);
    if (errors.Count > 0)
    {
        return Results.BadRequest(new ErrorResponse("invalid_request", "Request validation failed.", errors));
    }

    var passwordErrors = ValidatePasswordPolicy(request.NewPassword);
    if (passwordErrors.Count > 0)
    {
        return Results.BadRequest(new ErrorResponse("weak_password", "Password does not meet policy.", passwordErrors));
    }

    var tokenHash = HashToken(request.Token);
    var token = await db.PasswordResetTokens.FirstOrDefaultAsync(x => x.TokenHash == tokenHash);
    if (token is null || token.UsedUtc is not null || token.ExpiresUtc <= DateTime.UtcNow)
    {
        return Results.Unauthorized();
    }

    var user = await db.Users.FirstOrDefaultAsync(x => x.Id == token.UserId);
    if (user is null)
    {
        return Results.Unauthorized();
    }

    user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(request.NewPassword);
    user.TokenVersion += 1;
    user.FailedLoginCount = 0;
    user.LockoutUntilUtc = null;
    token.UsedUtc = DateTime.UtcNow;
    await db.SaveChangesAsync();

    return Results.Ok();
}).RequireRateLimiting("auth");

app.MapPost("/auth/activate", async (InviteActivationRequest request, IdentityDbContext db, HttpContext httpContext) =>
{
    var errors = ValidateInviteActivation(request);
    if (errors.Count > 0)
    {
        return Results.BadRequest(new ErrorResponse("invalid_request", "Request validation failed.", errors));
    }

    var passwordErrors = ValidatePasswordPolicy(request.Password);
    if (passwordErrors.Count > 0)
    {
        return Results.BadRequest(new ErrorResponse("weak_password", "Password does not meet policy.", passwordErrors));
    }

    var tokenHash = HashToken(request.Token);
    var token = await db.InviteTokens.FirstOrDefaultAsync(x => x.TokenHash == tokenHash);
    if (token is null || token.UsedUtc is not null || token.ExpiresUtc <= DateTime.UtcNow)
    {
        return Results.Unauthorized();
    }

    if (token.ApprovedUtc is null)
    {
        return Results.StatusCode(403);
    }

    var user = await db.Users.FirstOrDefaultAsync(x => x.Id == token.UserId);
    if (user is null)
    {
        return Results.Unauthorized();
    }

    user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(request.Password);
    user.Status = "active";
    user.TokenVersion += 1;
    user.UpdatedUtc = DateTime.UtcNow;
    token.UsedUtc = DateTime.UtcNow;
    await db.SaveChangesAsync();

    AddAuditEvent(db, token.TenantId, token.UserId, "admin.user_activated", new
    {
        ip = GetClientIp(httpContext),
        user_agent = GetUserAgent(httpContext)
    });
    await db.SaveChangesAsync();

    return Results.Ok();
}).RequireRateLimiting("auth");

app.MapGet("/auth/token-version", async (string tenant_id, string user_id, IdentityDbContext db) =>
{
    if (string.IsNullOrWhiteSpace(tenant_id) || string.IsNullOrWhiteSpace(user_id))
    {
        return Results.BadRequest(new ErrorResponse("invalid_request", "tenant_id and user_id are required.", new List<string>
        {
            "tenant_id is required",
            "user_id is required"
        }));
    }

    if (!Guid.TryParse(user_id, out var userGuid))
    {
        return Results.BadRequest(new ErrorResponse("invalid_request", "user_id must be a uuid.", new List<string> { "user_id must be a uuid" }));
    }

    var user = await db.Users.FirstOrDefaultAsync(x => x.Id == userGuid && x.TenantId == tenant_id);
    if (user is null)
    {
        return Results.NotFound();
    }

    return Results.Ok(new TokenVersionResponse(user.TokenVersion));
});

app.MapGet("/jwks", async (SigningKeyStore keys) =>
{
    return Results.Ok(await keys.GetJwksAsync());
});

app.MapPost("/jwks/rotate", async (SigningKeyStore keys) =>
{
    var jwks = await keys.RotateAsync();
    return Results.Ok(jwks);
});

app.MapPost("/admin/tenants", async (TenantCreateRequest request, IdentityDbContext db, HttpContext httpContext) =>
{
    var errors = ValidateTenantCreate(request);
    if (errors.Count > 0)
    {
        return Results.BadRequest(new ErrorResponse("invalid_request", "Request validation failed.", errors));
    }

    var existing = await db.Tenants.FirstOrDefaultAsync(x => x.TenantId == request.TenantId);
    if (existing is not null)
    {
        return Results.Conflict(new ErrorResponse("tenant_exists", "Tenant already exists.", null));
    }

    var tenant = new Tenant
    {
        Id = Guid.NewGuid(),
        TenantId = request.TenantId,
        Name = request.Name,
        Status = "active",
        CreatedUtc = DateTime.UtcNow,
        UpdatedUtc = DateTime.UtcNow
    };
    db.Tenants.Add(tenant);
    AddAuditEvent(db, request.TenantId, null, "admin.tenant_created", new
    {
        name = request.Name,
        ip = GetClientIp(httpContext),
        user_agent = GetUserAgent(httpContext)
    });
    await db.SaveChangesAsync();
    return Results.Ok(new TenantResponse(tenant.TenantId, tenant.Name, tenant.Status));
});

app.MapGet("/admin/tenants", async (IdentityDbContext db) =>
{
    var tenants = await db.Tenants.OrderBy(x => x.TenantId).ToListAsync();
    return Results.Ok(tenants.Select(tenant => new TenantResponse(tenant.TenantId, tenant.Name, tenant.Status)));
});

app.MapPatch("/admin/tenants/{tenantId}", async (string tenantId, TenantUpdateRequest request, IdentityDbContext db, HttpContext httpContext) =>
{
    var errors = ValidateTenantUpdate(request);
    if (errors.Count > 0)
    {
        return Results.BadRequest(new ErrorResponse("invalid_request", "Request validation failed.", errors));
    }

    var tenant = await db.Tenants.FirstOrDefaultAsync(x => x.TenantId == tenantId);
    if (tenant is null)
    {
        return Results.NotFound();
    }

    if (!string.IsNullOrWhiteSpace(request.Name))
    {
        tenant.Name = request.Name;
    }

    if (!string.IsNullOrWhiteSpace(request.Status))
    {
        tenant.Status = request.Status;
    }

    tenant.UpdatedUtc = DateTime.UtcNow;
    AddAuditEvent(db, tenantId, null, "admin.tenant_updated", new
    {
        name = tenant.Name,
        status = tenant.Status,
        ip = GetClientIp(httpContext),
        user_agent = GetUserAgent(httpContext)
    });
    await db.SaveChangesAsync();
    return Results.Ok(new TenantResponse(tenant.TenantId, tenant.Name, tenant.Status));
});

app.MapPost("/admin/tenants/{tenantId}/disable", async (string tenantId, IdentityDbContext db, HttpContext httpContext) =>
{
    var tenant = await db.Tenants.FirstOrDefaultAsync(x => x.TenantId == tenantId);
    if (tenant is null)
    {
        return Results.NotFound();
    }

    tenant.Status = "disabled";
    tenant.UpdatedUtc = DateTime.UtcNow;
    AddAuditEvent(db, tenantId, null, "admin.tenant_disabled", new
    {
        ip = GetClientIp(httpContext),
        user_agent = GetUserAgent(httpContext)
    });
    await db.SaveChangesAsync();
    return Results.Ok(new TenantResponse(tenant.TenantId, tenant.Name, tenant.Status));
});

app.MapPost("/admin/users/invite", async (UserInviteRequest request, IdentityDbContext db, HttpContext httpContext) =>
{
    var errors = ValidateUserInvite(request);
    if (errors.Count > 0)
    {
        return Results.BadRequest(new ErrorResponse("invalid_request", "Request validation failed.", errors));
    }

    var normalizedUsername = string.IsNullOrWhiteSpace(request.Username) ? null : request.Username.Trim();
    var tenant = await db.Tenants.FirstOrDefaultAsync(x => x.TenantId == request.TenantId);
    if (tenant is null)
    {
        return Results.NotFound();
    }

    var user = await db.Users.FirstOrDefaultAsync(x => x.TenantId == request.TenantId && x.Email == request.Email);
    if (user is not null && user.Status == "active")
    {
        return Results.Conflict(new ErrorResponse("user_exists", "User already active.", null));
    }

    if (!string.IsNullOrWhiteSpace(normalizedUsername))
    {
        var usernameOwner = await db.Users.FirstOrDefaultAsync(x => x.TenantId == request.TenantId && x.Username == normalizedUsername);
        if (usernameOwner is not null && (user is null || usernameOwner.Id != user.Id))
        {
            return Results.Conflict(new ErrorResponse("username_exists", "Username already in use.", null));
        }
    }

    if (user is null)
    {
        user = new AppUser
        {
            Id = Guid.NewGuid(),
            TenantId = request.TenantId,
            Username = normalizedUsername,
            Email = request.Email,
            DisplayName = request.DisplayName ?? "",
            Status = "invited",
            PasswordHash = "",
            CreatedUtc = DateTime.UtcNow,
            UpdatedUtc = DateTime.UtcNow
        };
        db.Users.Add(user);
    }
    else
    {
        user.Status = "invited";
        user.DisplayName = request.DisplayName ?? user.DisplayName;
        if (!string.IsNullOrWhiteSpace(normalizedUsername))
        {
            user.Username = normalizedUsername;
        }
        user.UpdatedUtc = DateTime.UtcNow;
    }

    var membership = await db.Memberships.FirstOrDefaultAsync(x => x.TenantId == request.TenantId && x.UserId == user.Id);
    if (membership is null)
    {
        db.Memberships.Add(new Membership
        {
            Id = Guid.NewGuid(),
            TenantId = request.TenantId,
            UserId = user.Id,
            Status = "active",
            CreatedUtc = DateTime.UtcNow,
            UpdatedUtc = DateTime.UtcNow
        });
    }

    var token = GenerateToken();
    var invite = new InviteToken
    {
        Id = Guid.NewGuid(),
        TenantId = request.TenantId,
        UserId = user.Id,
        Email = request.Email,
        TokenHash = HashToken(token),
        IssuedUtc = DateTime.UtcNow,
        ExpiresUtc = DateTime.UtcNow.AddMinutes(inviteTokenMinutes),
        ApprovedUtc = null,
        UsedUtc = null
    };
    db.InviteTokens.Add(invite);

    AddAuditEvent(db, request.TenantId, user.Id, "admin.user_invited", new
    {
        email = request.Email,
        username = normalizedUsername,
        invite_expires_utc = invite.ExpiresUtc,
        ip = GetClientIp(httpContext),
        user_agent = GetUserAgent(httpContext)
    });

    await db.SaveChangesAsync();
    return Results.Ok(new InviteResponse(token, invite.ExpiresUtc));
});

app.MapPost("/admin/users/{userId}/approve", async (Guid userId, UserApproveRequest request, IdentityDbContext db, HttpContext httpContext) =>
{
    var errors = ValidateUserApprove(request);
    if (errors.Count > 0)
    {
        return Results.BadRequest(new ErrorResponse("invalid_request", "Request validation failed.", errors));
    }

    var user = await db.Users.FirstOrDefaultAsync(x => x.Id == userId && x.TenantId == request.TenantId);
    if (user is null)
    {
        return Results.NotFound();
    }

    var invite = await db.InviteTokens
        .Where(x => x.UserId == userId && x.TenantId == request.TenantId && x.UsedUtc == null && x.ExpiresUtc > DateTime.UtcNow)
        .OrderByDescending(x => x.IssuedUtc)
        .FirstOrDefaultAsync();

    if (invite is null)
    {
        return Results.NotFound();
    }

    invite.ApprovedUtc = DateTime.UtcNow;
    user.Status = "approved";
    user.UpdatedUtc = DateTime.UtcNow;

    AddAuditEvent(db, request.TenantId, userId, "admin.user_approved", new
    {
        ip = GetClientIp(httpContext),
        user_agent = GetUserAgent(httpContext)
    });

    await db.SaveChangesAsync();
    return Results.Ok();
});

app.MapPost("/admin/users/{userId}/password", async (Guid userId, AdminPasswordSetRequest request, IdentityDbContext db, HttpContext httpContext) =>
{
    var errors = ValidateAdminPasswordSet(request);
    if (errors.Count > 0)
    {
        return Results.BadRequest(new ErrorResponse("invalid_request", "Request validation failed.", errors));
    }

    var user = await db.Users.FirstOrDefaultAsync(x => x.Id == userId && x.TenantId == request.TenantId);
    if (user is null)
    {
        return Results.NotFound();
    }

    var passwordErrors = ValidatePasswordPolicy(request.Password);
    if (passwordErrors.Count > 0)
    {
        return Results.BadRequest(new ErrorResponse("weak_password", "Password does not meet policy.", passwordErrors));
    }

    user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(request.Password);
    user.Status = "active";
    user.TokenVersion += 1;
    user.FailedLoginCount = 0;
    user.LockoutUntilUtc = null;
    user.UpdatedUtc = DateTime.UtcNow;

    AddAuditEvent(db, request.TenantId, userId, "admin.user_password_set", new
    {
        ip = GetClientIp(httpContext),
        user_agent = GetUserAgent(httpContext)
    });

    await db.SaveChangesAsync();
    return Results.Ok();
});

app.MapGet("/admin/users", async (string tenant_id, IdentityDbContext db) =>
{
    if (string.IsNullOrWhiteSpace(tenant_id))
    {
        return Results.BadRequest(new ErrorResponse("invalid_request", "tenant_id is required.", new List<string> { "tenant_id is required" }));
    }

    var users = await db.Users
        .Where(x => x.TenantId == tenant_id)
        .Where(x => x.Status != "removed")
        .OrderBy(x => x.Email)
        .ToListAsync();

    var roleMap = await db.Roles
        .Where(x => x.TenantId == tenant_id)
        .ToDictionaryAsync(x => x.Id, x => x.Name);

    var userIds = users.Select(x => x.Id).ToArray();
    var userRoles = await db.UserRoles
        .Where(x => x.TenantId == tenant_id && userIds.Contains(x.UserId))
        .ToListAsync();

    var rolesByUser = userRoles
        .GroupBy(x => x.UserId)
        .ToDictionary(
            group => group.Key,
            group => group.Select(role => roleMap.TryGetValue(role.RoleId, out var name) ? name : "").Where(name => name.Length > 0).ToList());

    var response = users.Select(user =>
        BuildUserSummary(user, rolesByUser.TryGetValue(user.Id, out var roles) ? roles : new List<string>()));

    return Results.Ok(response);
});

app.MapPost("/admin/users/{userId}/unlock", async (Guid userId, UserUnlockRequest request, IdentityDbContext db, HttpContext httpContext) =>
{
    var errors = ValidateUserUnlock(request);
    if (errors.Count > 0)
    {
        return Results.BadRequest(new ErrorResponse("invalid_request", "Request validation failed.", errors));
    }

    var user = await db.Users.FirstOrDefaultAsync(x => x.Id == userId && x.TenantId == request.TenantId && x.Status != "removed");
    if (user is null)
    {
        return Results.NotFound();
    }

    var wasLocked = IsUserCurrentlyLocked(user);
    var priorLockoutUntilUtc = user.LockoutUntilUtc;
    var priorFailedLoginCount = user.FailedLoginCount;

    user.FailedLoginCount = 0;
    user.LockoutUntilUtc = null;
    user.UpdatedUtc = DateTime.UtcNow;

    AddAuditEvent(db, request.TenantId, userId, "admin.user_unlocked", new
    {
        was_locked = wasLocked,
        prior_lockout_until_utc = priorLockoutUntilUtc,
        prior_failed_login_count = priorFailedLoginCount,
        reason = request.Reason,
        ip = GetClientIp(httpContext),
        user_agent = GetUserAgent(httpContext)
    });

    await db.SaveChangesAsync();
    return Results.Ok(await BuildUserSummaryAsync(db, user, request.TenantId));
});

app.MapPost("/admin/users/{userId}/revoke", async (Guid userId, UserRevokeRequest request, IdentityDbContext db, HttpContext httpContext) =>
{
    var errors = ValidateUserRevoke(request);
    if (errors.Count > 0)
    {
        return Results.BadRequest(new ErrorResponse("invalid_request", "Request validation failed.", errors));
    }

    var user = await db.Users.FirstOrDefaultAsync(x => x.Id == userId && x.TenantId == request.TenantId);
    if (user is null)
    {
        return Results.NotFound();
    }

    user.Status = "revoked";
    user.TokenVersion += 1;
    user.UpdatedUtc = DateTime.UtcNow;

    var memberships = await db.Memberships
        .Where(x => x.UserId == userId && x.TenantId == request.TenantId && x.Status == "active")
        .ToListAsync();
    foreach (var membership in memberships)
    {
        membership.Status = "revoked";
        membership.UpdatedUtc = DateTime.UtcNow;
    }

    var sessions = await db.Sessions
        .Where(x => x.UserId == userId && x.TenantId == request.TenantId && x.Status == "active")
        .ToListAsync();
    foreach (var session in sessions)
    {
        session.Status = "revoked";
    }

    var refreshTokens = await db.RefreshTokens
        .Where(x => x.UserId == userId && x.TenantId == request.TenantId && x.RevokedUtc == null)
        .ToListAsync();
    foreach (var refresh in refreshTokens)
    {
        refresh.RevokedUtc = DateTime.UtcNow;
    }

    AddAuditEvent(db, request.TenantId, userId, "admin.user_revoked", new
    {
        reason = request.Reason,
        token_version = user.TokenVersion,
        ip = GetClientIp(httpContext),
        user_agent = GetUserAgent(httpContext)
    });

    await db.SaveChangesAsync();
    return Results.Ok(new TokenVersionResponse(user.TokenVersion));
});

app.MapPost("/admin/users/{userId}/remove", async (Guid userId, UserRemoveRequest request, IdentityDbContext db, HttpContext httpContext) =>
{
    var errors = ValidateUserRemove(request);
    if (errors.Count > 0)
    {
        return Results.BadRequest(new ErrorResponse("invalid_request", "Request validation failed.", errors));
    }

    var user = await db.Users.FirstOrDefaultAsync(x => x.Id == userId && x.TenantId == request.TenantId && x.Status != "removed");
    if (user is null)
    {
        return Results.NotFound();
    }

    user.Status = "removed";
    user.TokenVersion += 1;
    user.UpdatedUtc = DateTime.UtcNow;

    await RevokeUserAccessAsync(db, userId, request.TenantId, DateTime.UtcNow);
    await RemoveUserRolesAsync(db, userId, request.TenantId);

    AddAuditEvent(db, request.TenantId, userId, "admin.user_removed_from_tenant", new
    {
        reason = request.Reason,
        token_version = user.TokenVersion,
        ip = GetClientIp(httpContext),
        user_agent = GetUserAgent(httpContext)
    });

    await db.SaveChangesAsync();
    return Results.Ok(new TokenVersionResponse(user.TokenVersion));
});

app.MapPost("/admin/users/{userId}/tenant", async (Guid userId, UserTenantSwitchRequest request, IdentityDbContext db, HttpContext httpContext) =>
{
    var errors = ValidateUserTenantSwitch(request);
    if (errors.Count > 0)
    {
        return Results.BadRequest(new ErrorResponse("invalid_request", "Request validation failed.", errors));
    }

    var sourceTenantId = request.TenantId.Trim();
    var targetTenantId = request.TargetTenantId.Trim();
    if (string.Equals(sourceTenantId, targetTenantId, StringComparison.Ordinal))
    {
        return Results.BadRequest(new ErrorResponse("invalid_request", "target_tenant_id must be different from tenant_id.", new List<string> { "target_tenant_id must be different from tenant_id" }));
    }

    var targetTenant = await db.Tenants.FirstOrDefaultAsync(x => x.TenantId == targetTenantId);
    if (targetTenant is null)
    {
        return Results.NotFound();
    }

    if (!string.Equals(targetTenant.Status, "active", StringComparison.OrdinalIgnoreCase))
    {
        return Results.BadRequest(new ErrorResponse("tenant_disabled", "Target tenant is not active.", new List<string> { "target tenant is not active" }));
    }

    var user = await db.Users.FirstOrDefaultAsync(x => x.Id == userId && x.TenantId == sourceTenantId && x.Status != "removed");
    if (user is null)
    {
        return Results.NotFound();
    }

    var emailConflict = await db.Users.AnyAsync(x => x.TenantId == targetTenantId && x.Email == user.Email && x.Id != userId);
    if (emailConflict)
    {
        return Results.Conflict(new ErrorResponse("user_exists", "A user with this email already exists in the target tenant.", null));
    }

    if (!string.IsNullOrWhiteSpace(user.Username))
    {
        var usernameConflict = await db.Users.AnyAsync(x => x.TenantId == targetTenantId && x.Username == user.Username && x.Id != userId);
        if (usernameConflict)
        {
            return Results.Conflict(new ErrorResponse("username_exists", "A user with this username already exists in the target tenant.", null));
        }
    }

    user.TenantId = targetTenantId;
    user.TokenVersion += 1;
    user.UpdatedUtc = DateTime.UtcNow;

    await RevokeUserAccessAsync(db, userId, sourceTenantId, DateTime.UtcNow);
    await RemoveUserRolesAsync(db, userId, sourceTenantId);

    var targetMembership = await db.Memberships.FirstOrDefaultAsync(x => x.UserId == userId && x.TenantId == targetTenantId);
    if (targetMembership is null)
    {
        db.Memberships.Add(new Membership
        {
            Id = Guid.NewGuid(),
            TenantId = targetTenantId,
            UserId = userId,
            Status = "active",
            CreatedUtc = DateTime.UtcNow,
            UpdatedUtc = DateTime.UtcNow
        });
    }
    else
    {
        targetMembership.Status = "active";
        targetMembership.UpdatedUtc = DateTime.UtcNow;
    }

    await RemoveUserRolesAsync(db, userId, targetTenantId);
    var targetRoles = await EnsureRolesAsync(db, targetTenantId, request.Roles ?? new List<string>());
    foreach (var role in targetRoles)
    {
        db.UserRoles.Add(new UserRole
        {
            Id = Guid.NewGuid(),
            TenantId = targetTenantId,
            UserId = userId,
            RoleId = role.Id,
            CreatedUtc = DateTime.UtcNow
        });
    }

    AddAuditEvent(db, sourceTenantId, userId, "admin.user_switched_from_tenant", new
    {
        target_tenant_id = targetTenantId,
        token_version = user.TokenVersion,
        ip = GetClientIp(httpContext),
        user_agent = GetUserAgent(httpContext)
    });
    AddAuditEvent(db, targetTenantId, userId, "admin.user_switched_to_tenant", new
    {
        source_tenant_id = sourceTenantId,
        roles = targetRoles.Select(role => role.Name).ToArray(),
        token_version = user.TokenVersion,
        ip = GetClientIp(httpContext),
        user_agent = GetUserAgent(httpContext)
    });

    await db.SaveChangesAsync();
    return Results.Ok(await BuildUserSummaryAsync(db, user, targetTenantId));
});

app.MapGet("/admin/roles", async (string tenant_id, IdentityDbContext db) =>
{
    if (string.IsNullOrWhiteSpace(tenant_id))
    {
        return Results.BadRequest(new ErrorResponse("invalid_request", "tenant_id is required.", new List<string> { "tenant_id is required" }));
    }

    var roles = await db.Roles.Where(x => x.TenantId == tenant_id).OrderBy(x => x.Name).ToListAsync();
    return Results.Ok(roles.Select(role => new RoleResponse(role.Id, role.Name)));
});

app.MapPost("/admin/roles", async (RoleCreateRequest request, IdentityDbContext db, HttpContext httpContext) =>
{
    var errors = ValidateRoleCreate(request);
    if (errors.Count > 0)
    {
        return Results.BadRequest(new ErrorResponse("invalid_request", "Request validation failed.", errors));
    }

    var existing = await db.Roles.FirstOrDefaultAsync(x => x.TenantId == request.TenantId && x.Name == request.Name);
    if (existing is not null)
    {
        return Results.Conflict(new ErrorResponse("role_exists", "Role already exists.", null));
    }

    var role = new Role
    {
        Id = Guid.NewGuid(),
        TenantId = request.TenantId,
        Name = request.Name,
        CreatedUtc = DateTime.UtcNow
    };
    db.Roles.Add(role);
    AddAuditEvent(db, request.TenantId, null, "admin.role_created", new
    {
        role = request.Name,
        ip = GetClientIp(httpContext),
        user_agent = GetUserAgent(httpContext)
    });
    await db.SaveChangesAsync();
    return Results.Ok(new RoleResponse(role.Id, role.Name));
});

app.MapPut("/admin/users/{userId}/roles", async (Guid userId, UserRoleAssignRequest request, IdentityDbContext db, HttpContext httpContext) =>
{
    var errors = ValidateRoleAssign(request);
    if (errors.Count > 0)
    {
        return Results.BadRequest(new ErrorResponse("invalid_request", "Request validation failed.", errors));
    }

    var user = await db.Users.FirstOrDefaultAsync(x => x.Id == userId && x.TenantId == request.TenantId);
    if (user is null)
    {
        return Results.NotFound();
    }

    var roles = await db.Roles.Where(x => x.TenantId == request.TenantId && request.Roles.Contains(x.Name)).ToListAsync();
    var missing = request.Roles.Except(roles.Select(x => x.Name)).ToList();
    if (missing.Count > 0)
    {
        foreach (var roleName in missing)
        {
            var role = new Role
            {
                Id = Guid.NewGuid(),
                TenantId = request.TenantId,
                Name = roleName,
                CreatedUtc = DateTime.UtcNow
            };
            db.Roles.Add(role);
            roles.Add(role);
        }
    }

    var existing = await db.UserRoles.Where(x => x.TenantId == request.TenantId && x.UserId == userId).ToListAsync();
    if (existing.Count > 0)
    {
        db.UserRoles.RemoveRange(existing);
    }

    foreach (var role in roles)
    {
        db.UserRoles.Add(new UserRole
        {
            Id = Guid.NewGuid(),
            TenantId = request.TenantId,
            UserId = userId,
            RoleId = role.Id,
            CreatedUtc = DateTime.UtcNow
        });
    }

    AddAuditEvent(db, request.TenantId, userId, "admin.role_assigned", new
    {
        roles = request.Roles,
        ip = GetClientIp(httpContext),
        user_agent = GetUserAgent(httpContext)
    });

    await db.SaveChangesAsync();
    return Results.Ok(new RoleAssignResponse(userId, request.Roles));
});

app.Run();

void AttachEventBus(WebApplication app)
{
    var options = app.Services.GetRequiredService<EventBusClientOptions>();
    if (!options.Enabled)
    {
        return;
    }

    var client = app.Services.GetRequiredService<EventBusClient>();
    app.Lifetime.ApplicationStarted.Register(() =>
        _ = client.PublishAsync(new EventEnvelope
        {
            Type = "service.start",
            Polarity = "positive",
            Severity = "info",
            Message = "Identity server started."
        }));

    app.Lifetime.ApplicationStopping.Register(() =>
        _ = client.PublishAsync(new EventEnvelope
        {
            Type = "service.stop",
            Polarity = "negative",
            Severity = "warn",
            Message = "Identity server stopping."
        }));

    AppDomain.CurrentDomain.UnhandledException += (_, args) =>
    {
        if (args.ExceptionObject is Exception ex)
        {
            _ = client.PublishAsync(new EventEnvelope
            {
                Type = "service.error",
                Polarity = "negative",
                Severity = "error",
                Message = ex.Message,
                Data = new Dictionary<string, object?>
                {
                    ["stack"] = ex.StackTrace
                }
            });
        }
    };
}

static int GetIntFromEnv(string name, int defaultValue)
{
    var raw = Environment.GetEnvironmentVariable(name);
    return int.TryParse(raw, out var value) ? value : defaultValue;
}

static string GenerateToken()
{
    return Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
}

static string GenerateVerificationCode(IHostEnvironment environment)
{
    var configuredCode = Environment.GetEnvironmentVariable("IDENTITY_DEV_VERIFICATION_CODE");
    if (environment.IsDevelopment() &&
        configuredCode is { Length: 6 } &&
        configuredCode.All(char.IsDigit))
    {
        return configuredCode;
    }

    return RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6");
}

static string HashToken(string token)
{
    var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(token));
    return Convert.ToHexString(bytes).ToLowerInvariant();
}

static string HashRegistrationVerificationToken(Guid verificationId, string code)
{
    return HashToken($"{verificationId:N}:{code}");
}

static void AddAuditEvent(IdentityDbContext db, string tenantId, Guid? userId, string eventType, object payload)
{
    var payloadJson = JsonSerializer.Serialize(payload, new JsonSerializerOptions
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    });
    db.AuditEvents.Add(new AuditEvent
    {
        Id = Guid.NewGuid(),
        TenantId = tenantId,
        UserId = userId,
        EventType = eventType,
        PayloadJson = payloadJson,
        OccurredUtc = DateTime.UtcNow
    });
}

static HashSet<string> BuildSuperAdminUsernames(string? primaryUsername, string? aliasesRaw)
{
    var usernames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    if (!string.IsNullOrWhiteSpace(primaryUsername))
    {
        usernames.Add(primaryUsername.Trim());
    }

    if (!string.IsNullOrWhiteSpace(aliasesRaw))
    {
        foreach (var alias in aliasesRaw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (!string.IsNullOrWhiteSpace(alias))
            {
                usernames.Add(alias);
            }
        }
    }

    return usernames;
}

static List<string> ValidateLogin(LoginRequest request, IReadOnlySet<string> superAdminUsernames, string? superAdminTenantId, string? defaultTenantId)
{
    var errors = new List<string>();
    if (string.IsNullOrWhiteSpace(request.Email) && string.IsNullOrWhiteSpace(request.Username))
    {
        errors.Add("email or username is required");
    }
    if (string.IsNullOrWhiteSpace(request.Password)) errors.Add("password is required");
    return errors;
}

static async Task<TenantlessLoginResolution> ResolveTenantlessLoginAsync(
    IdentityDbContext db,
    bool usingUsername,
    string? lookupUsernameKey,
    string? normalizedEmailKey,
    string password,
    IReadOnlyList<string> tenantPriority)
{
    var candidates = usingUsername
        ? await (
            from user in db.Users
            join tenant in db.Tenants on user.TenantId equals tenant.TenantId
            join membership in db.Memberships on new { user.TenantId, UserId = user.Id } equals new { membership.TenantId, membership.UserId }
            where user.Username != null
                  && user.Username.ToLower() == lookupUsernameKey
                  && user.Status == "active"
                  && tenant.Status == "active"
                  && membership.Status == "active"
            select user)
        .ToListAsync()
        : await (
            from user in db.Users
            join tenant in db.Tenants on user.TenantId equals tenant.TenantId
            join membership in db.Memberships on new { user.TenantId, UserId = user.Id } equals new { membership.TenantId, membership.UserId }
            where user.Email.ToLower() == normalizedEmailKey
                  && user.Status == "active"
                  && tenant.Status == "active"
                  && membership.Status == "active"
            select user)
        .ToListAsync();

    var validCandidates = candidates
        .Where(user => user.LockoutUntilUtc is null || user.LockoutUntilUtc <= DateTime.UtcNow)
        .Where(user => VerifyPassword(password, user.PasswordHash))
        .OrderBy(user => TenantPriorityRank(user.TenantId, tenantPriority))
        .ThenBy(user => user.TenantId, StringComparer.OrdinalIgnoreCase)
        .ThenBy(user => user.CreatedUtc)
        .ToArray();

    return new TenantlessLoginResolution(validCandidates.FirstOrDefault(), candidates);
}

static IReadOnlyList<string> BuildTenantPriority(params string?[] sources)
{
    var priority = new List<string>();
    foreach (var source in sources)
    {
        if (string.IsNullOrWhiteSpace(source))
        {
            continue;
        }

        foreach (var tenantId in source.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (!priority.Contains(tenantId, StringComparer.OrdinalIgnoreCase))
            {
                priority.Add(tenantId);
            }
        }
    }

    return priority;
}

static int TenantPriorityRank(string tenantId, IReadOnlyList<string> tenantPriority)
{
    for (var index = 0; index < tenantPriority.Count; index++)
    {
        if (string.Equals(tenantId, tenantPriority[index], StringComparison.OrdinalIgnoreCase))
        {
            return index;
        }
    }

    return int.MaxValue;
}

static List<string> ValidateRegisterStart(RegisterStartRequest request, string? defaultTenantId)
{
    var errors = new List<string>();
    if (request is null)
    {
        return new List<string> { "body is required" };
    }

    var email = NormalizeEmail(request.Email);
    var phone = NormalizePhoneNumber(request.Phone);
    if (email is null && phone is null)
    {
        errors.Add("valid email or phone is required");
    }
    if (email is not null && phone is not null)
    {
        errors.Add("provide either email or phone, not both");
    }
    if (request.Email is not null && email is null)
    {
        errors.Add("email is invalid");
    }
    if (request.Phone is not null && phone is null)
    {
        errors.Add("phone is invalid");
    }
    if (request.Username is not null && NormalizeUsername(request.Username) is null)
    {
        errors.Add("username cannot be empty");
    }
    if (string.IsNullOrWhiteSpace(request.TenantId) && string.IsNullOrWhiteSpace(defaultTenantId))
    {
        errors.Add("tenant_id is required when no default tenant is configured");
    }

    return errors;
}

static List<string> ValidateRegisterConfirm(RegisterConfirmRequest request)
{
    var errors = new List<string>();
    if (request.VerificationId == Guid.Empty) errors.Add("verification_id is required");
    if (string.IsNullOrWhiteSpace(request.Code)) errors.Add("code is required");
    if (string.IsNullOrWhiteSpace(request.Password)) errors.Add("password is required");
    return errors;
}

static List<string> ValidateRefresh(RefreshRequest request)
{
    var errors = new List<string>();
    if (string.IsNullOrWhiteSpace(request.RefreshToken)) errors.Add("refresh_token is required");
    return errors;
}

static List<string> ValidateLogout(LogoutRequest request)
{
    var errors = new List<string>();
    if (string.IsNullOrWhiteSpace(request.RefreshToken)) errors.Add("refresh_token is required");
    return errors;
}

static bool VerifyPassword(string password, string passwordHash)
{
    if (string.IsNullOrWhiteSpace(passwordHash))
    {
        return false;
    }
    return BCrypt.Net.BCrypt.Verify(password, passwordHash);
}

static async Task<string> IssueAccessToken(
    string issuer,
    string audience,
    AppUser user,
    Guid sessionId,
    int tokenVersion,
    DateTime expiresUtc,
    SigningKeyStore keyStore,
    IReadOnlyCollection<string> roles,
    bool isSuperAdmin,
    bool canAccessAdministration)
{
    var handler = new JwtSecurityTokenHandler();
    var key = await keyStore.GetPrivateKeyAsync();
    var creds = new SigningCredentials(new ECDsaSecurityKey(key) { KeyId = keyStore.KeyId }, SecurityAlgorithms.EcdsaSha256);

    var claims = new List<System.Security.Claims.Claim>
    {
        new("sub", user.Id.ToString()),
        new("tenant_id", user.TenantId),
        new("session_id", sessionId.ToString()),
        new("token_version", tokenVersion.ToString()),
        new("preferred_username", user.Username ?? string.Empty),
        new("email", user.Email),
        new("name", user.DisplayName),
        new("is_superadmin", isSuperAdmin ? "true" : "false"),
        new("cwi_admin", canAccessAdministration ? "true" : "false")
    };

    foreach (var role in roles.Select(role => role.Trim()).Where(role => role.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase))
    {
        claims.Add(new System.Security.Claims.Claim("role", role));
        claims.Add(new System.Security.Claims.Claim(ClaimTypes.Role, role));
    }

    var token = new JwtSecurityToken(
        issuer: issuer,
        audience: audience,
        claims: claims,
        notBefore: DateTime.UtcNow.AddMinutes(-1),
        expires: expiresUtc,
        signingCredentials: creds);

    return handler.WriteToken(token);
}

static async Task<List<string>> GetRoleNamesAsync(IdentityDbContext db, string tenantId, Guid userId)
{
    return await (
        from userRole in db.UserRoles
        join role in db.Roles on new { userRole.TenantId, RoleId = userRole.RoleId } equals new { role.TenantId, RoleId = role.Id }
        where userRole.TenantId == tenantId &&
              userRole.UserId == userId
        orderby role.Name
        select role.Name)
        .ToListAsync();
}

static bool IsConfiguredSuperAdmin(
    AppUser user,
    IReadOnlyList<string> superAdminTenantIds,
    string? superAdminUsername)
{
    if (superAdminTenantIds.Count == 0 || string.IsNullOrWhiteSpace(superAdminUsername))
    {
        return false;
    }

    return superAdminTenantIds.Any(tenantId => string.Equals(user.TenantId, tenantId, StringComparison.Ordinal)) &&
        string.Equals(user.Username, superAdminUsername, StringComparison.Ordinal);
}

static bool CanAccessAdministration(
    AppUser user,
    IReadOnlyCollection<string> roles,
    bool isSuperAdmin,
    string platformAdminRoleName)
{
    if (isSuperAdmin)
    {
        return true;
    }

    if (HasRole(roles, platformAdminRoleName))
    {
        return true;
    }

    return false;
}

static bool HasRole(IEnumerable<string> roles, string roleName)
{
    return roles.Any(role => string.Equals(role, roleName, StringComparison.OrdinalIgnoreCase));
}

static bool IsSuperAdminProtectedPath(PathString path)
{
    return path.StartsWithSegments("/admin", StringComparison.OrdinalIgnoreCase) ||
        path.StartsWithSegments("/jwks/rotate", StringComparison.OrdinalIgnoreCase);
}

static async Task<IResult?> AuthorizeSuperAdminAsync(
    HttpContext httpContext,
    IdentityDbContext db,
    SigningKeyStore keyStore,
    string issuer,
    string audience,
    IReadOnlyList<string> superAdminTenantIds,
    string? superAdminUsername,
    string platformAdminRoleName,
    bool allowAdminRole)
{
    if (superAdminTenantIds.Count == 0 || string.IsNullOrWhiteSpace(superAdminUsername))
    {
        return Results.Json(
            new ErrorResponse("superadmin_not_configured", "Superadmin access is not configured.", null),
            statusCode: StatusCodes.Status503ServiceUnavailable);
    }

    var authorization = httpContext.Request.Headers.Authorization.ToString();
    if (string.IsNullOrWhiteSpace(authorization) || !authorization.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
    {
        return Results.Unauthorized();
    }

    var token = authorization["Bearer ".Length..].Trim();
    if (string.IsNullOrWhiteSpace(token))
    {
        return Results.Unauthorized();
    }

    ClaimsPrincipal principal;
    try
    {
        principal = await ValidateAccessTokenAsync(token, keyStore, issuer, audience);
    }
    catch
    {
        return Results.Unauthorized();
    }

    httpContext.User = principal;

    var isSuperAdminClaim = principal.FindFirstValue("is_superadmin");
    if (string.Equals(isSuperAdminClaim, "true", StringComparison.OrdinalIgnoreCase))
    {
        return null;
    }

    var tenantId = principal.FindFirstValue("tenant_id");
    var username = principal.FindFirstValue("preferred_username");
    if (tenantId is not null &&
        superAdminTenantIds.Any(configuredTenantId => string.Equals(tenantId, configuredTenantId, StringComparison.Ordinal)) &&
        string.Equals(username, superAdminUsername, StringComparison.Ordinal))
    {
        return null;
    }

    if (allowAdminRole && await HasActiveAdminRoleAsync(db, principal, platformAdminRoleName))
    {
        return null;
    }

    return Results.Json(
        new ErrorResponse("forbidden", allowAdminRole ? "Administrative access is required." : "Superadmin access is required.", null),
        statusCode: StatusCodes.Status403Forbidden);
}

static async Task<bool> HasActiveAdminRoleAsync(
    IdentityDbContext db,
    ClaimsPrincipal principal,
    string platformAdminRoleName)
{
    var tenantId = principal.FindFirstValue("tenant_id");
    if (string.IsNullOrWhiteSpace(tenantId))
    {
        return false;
    }

    var userIdClaim = principal.FindFirstValue("sub") ??
        principal.FindFirstValue(ClaimTypes.NameIdentifier);
    if (!Guid.TryParse(userIdClaim, out var userId))
    {
        return false;
    }

    var tokenVersionClaim = principal.FindFirstValue("token_version");
    if (!int.TryParse(tokenVersionClaim, out var tokenVersion))
    {
        return false;
    }

    var user = await db.Users.FirstOrDefaultAsync(x =>
        x.Id == userId &&
        x.TenantId == tenantId &&
        x.Status == "active");
    if (user is null || user.TokenVersion != tokenVersion)
    {
        return false;
    }

    var hasActiveMembership = await db.Memberships.AnyAsync(x =>
        x.TenantId == tenantId &&
        x.UserId == userId &&
        x.Status == "active");
    if (!hasActiveMembership)
    {
        return false;
    }

    var roles = await (
        from userRole in db.UserRoles
        join role in db.Roles on new { userRole.TenantId, RoleId = userRole.RoleId } equals new { role.TenantId, RoleId = role.Id }
        where userRole.TenantId == tenantId &&
              userRole.UserId == userId &&
              role.Name == platformAdminRoleName
        select role.Name)
        .ToListAsync();

    return HasRole(roles, platformAdminRoleName);
}

static async Task<ClaimsPrincipal> ValidateAccessTokenAsync(
    string token,
    SigningKeyStore keyStore,
    string issuer,
    string audience)
{
    var handler = new JwtSecurityTokenHandler();
    var validationParameters = new TokenValidationParameters
    {
        ValidateIssuer = true,
        ValidIssuer = issuer,
        ValidateAudience = true,
        ValidAudience = audience,
        ValidateIssuerSigningKey = true,
        IssuerSigningKey = await keyStore.GetValidationKeyAsync(),
        ValidateLifetime = true,
        ClockSkew = TimeSpan.FromMinutes(1),
        NameClaimType = "preferred_username"
    };

    return handler.ValidateToken(token, validationParameters, out _);
}

static List<string> ValidatePasswordPolicy(string password)
{
    var errors = new List<string>();
    if (password.Length < 12) errors.Add("password must be at least 12 characters");
    if (!password.Any(char.IsUpper)) errors.Add("password must include an uppercase letter");
    if (!password.Any(char.IsLower)) errors.Add("password must include a lowercase letter");
    if (!password.Any(char.IsDigit)) errors.Add("password must include a digit");
    if (!password.Any(ch => !char.IsLetterOrDigit(ch))) errors.Add("password must include a symbol");
    return errors;
}

static List<string> ValidatePasswordSet(PasswordSetRequest request)
{
    var errors = new List<string>();
    if (string.IsNullOrWhiteSpace(request.TenantId)) errors.Add("tenant_id is required");
    if (string.IsNullOrWhiteSpace(request.Email)) errors.Add("email is required");
    if (string.IsNullOrWhiteSpace(request.Password)) errors.Add("password is required");
    return errors;
}

static List<string> ValidatePasswordReset(PasswordResetRequest request)
{
    var errors = new List<string>();
    if (string.IsNullOrWhiteSpace(request.TenantId)) errors.Add("tenant_id is required");
    if (string.IsNullOrWhiteSpace(request.Email)) errors.Add("email is required");
    return errors;
}

static List<string> ValidatePasswordResetComplete(PasswordResetCompleteRequest request)
{
    var errors = new List<string>();
    if (string.IsNullOrWhiteSpace(request.Token)) errors.Add("token is required");
    if (string.IsNullOrWhiteSpace(request.NewPassword)) errors.Add("new_password is required");
    return errors;
}

static List<string> ValidateInviteActivation(InviteActivationRequest request)
{
    var errors = new List<string>();
    if (string.IsNullOrWhiteSpace(request.Token)) errors.Add("token is required");
    if (string.IsNullOrWhiteSpace(request.Password)) errors.Add("password is required");
    return errors;
}

static List<string> ValidateTenantCreate(TenantCreateRequest request)
{
    var errors = new List<string>();
    if (string.IsNullOrWhiteSpace(request.TenantId)) errors.Add("tenant_id is required");
    if (string.IsNullOrWhiteSpace(request.Name)) errors.Add("name is required");
    return errors;
}

static List<string> ValidateTenantUpdate(TenantUpdateRequest request)
{
    var errors = new List<string>();
    if (request is null) errors.Add("body is required");
    if (request is not null && string.IsNullOrWhiteSpace(request.Name) && string.IsNullOrWhiteSpace(request.Status))
    {
        errors.Add("name or status is required");
    }
    if (request is not null && !string.IsNullOrWhiteSpace(request.Status) && request.Status is not ("active" or "disabled"))
    {
        errors.Add("status must be active or disabled");
    }
    return errors;
}

static List<string> ValidateUserInvite(UserInviteRequest request)
{
    var errors = new List<string>();
    if (string.IsNullOrWhiteSpace(request.TenantId)) errors.Add("tenant_id is required");
    if (string.IsNullOrWhiteSpace(request.Email)) errors.Add("email is required");
    if (request.Username is not null && string.IsNullOrWhiteSpace(request.Username))
    {
        errors.Add("username cannot be empty");
    }
    return errors;
}

static List<string> ValidateRoleCreate(RoleCreateRequest request)
{
    var errors = new List<string>();
    if (string.IsNullOrWhiteSpace(request.TenantId)) errors.Add("tenant_id is required");
    if (string.IsNullOrWhiteSpace(request.Name)) errors.Add("name is required");
    return errors;
}

static List<string> ValidateRoleAssign(UserRoleAssignRequest request)
{
    var errors = new List<string>();
    if (string.IsNullOrWhiteSpace(request.TenantId)) errors.Add("tenant_id is required");
    if (request.Roles is null || request.Roles.Count == 0) errors.Add("roles is required");
    return errors;
}

static List<string> ValidateUserApprove(UserApproveRequest request)
{
    var errors = new List<string>();
    if (string.IsNullOrWhiteSpace(request.TenantId)) errors.Add("tenant_id is required");
    return errors;
}

static List<string> ValidateAdminPasswordSet(AdminPasswordSetRequest request)
{
    var errors = new List<string>();
    if (string.IsNullOrWhiteSpace(request.TenantId)) errors.Add("tenant_id is required");
    if (string.IsNullOrWhiteSpace(request.Password)) errors.Add("password is required");
    return errors;
}

static List<string> ValidateUserRevoke(UserRevokeRequest request)
{
    var errors = new List<string>();
    if (string.IsNullOrWhiteSpace(request.TenantId)) errors.Add("tenant_id is required");
    return errors;
}

static List<string> ValidateUserUnlock(UserUnlockRequest request)
{
    var errors = new List<string>();
    if (string.IsNullOrWhiteSpace(request.TenantId)) errors.Add("tenant_id is required");
    return errors;
}

static List<string> ValidateUserRemove(UserRemoveRequest request)
{
    var errors = new List<string>();
    if (string.IsNullOrWhiteSpace(request.TenantId)) errors.Add("tenant_id is required");
    return errors;
}

static List<string> ValidateUserTenantSwitch(UserTenantSwitchRequest request)
{
    var errors = new List<string>();
    if (string.IsNullOrWhiteSpace(request.TenantId)) errors.Add("tenant_id is required");
    if (string.IsNullOrWhiteSpace(request.TargetTenantId)) errors.Add("target_tenant_id is required");
    if (request.Roles is not null && request.Roles.Any(string.IsNullOrWhiteSpace))
    {
        errors.Add("roles cannot contain empty values");
    }
    return errors;
}

static async Task RevokeUserAccessAsync(IdentityDbContext db, Guid userId, string tenantId, DateTime timestampUtc)
{
    var memberships = await db.Memberships
        .Where(x => x.UserId == userId && x.TenantId == tenantId && x.Status == "active")
        .ToListAsync();
    foreach (var membership in memberships)
    {
        membership.Status = "revoked";
        membership.UpdatedUtc = timestampUtc;
    }

    var sessions = await db.Sessions
        .Where(x => x.UserId == userId && x.TenantId == tenantId && x.Status == "active")
        .ToListAsync();
    foreach (var session in sessions)
    {
        session.Status = "revoked";
    }

    var refreshTokens = await db.RefreshTokens
        .Where(x => x.UserId == userId && x.TenantId == tenantId && x.RevokedUtc == null)
        .ToListAsync();
    foreach (var refresh in refreshTokens)
    {
        refresh.RevokedUtc = timestampUtc;
    }

    var inviteTokens = await db.InviteTokens
        .Where(x => x.UserId == userId && x.TenantId == tenantId && x.UsedUtc == null)
        .ToListAsync();
    foreach (var invite in inviteTokens)
    {
        invite.UsedUtc = timestampUtc;
    }
}

static async Task RemoveUserRolesAsync(IdentityDbContext db, Guid userId, string tenantId)
{
    var existing = await db.UserRoles
        .Where(x => x.TenantId == tenantId && x.UserId == userId)
        .ToListAsync();
    if (existing.Count > 0)
    {
        db.UserRoles.RemoveRange(existing);
    }
}

static async Task<List<Role>> EnsureRolesAsync(IdentityDbContext db, string tenantId, IReadOnlyCollection<string> roleNames)
{
    var normalizedRoleNames = roleNames
        .Select(role => role.Trim())
        .Where(role => role.Length > 0)
        .Distinct(StringComparer.Ordinal)
        .ToList();
    if (normalizedRoleNames.Count == 0)
    {
        return new List<Role>();
    }

    var roles = await db.Roles
        .Where(x => x.TenantId == tenantId && normalizedRoleNames.Contains(x.Name))
        .ToListAsync();
    var missing = normalizedRoleNames.Except(roles.Select(role => role.Name)).ToList();
    foreach (var roleName in missing)
    {
        var role = new Role
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            Name = roleName,
            CreatedUtc = DateTime.UtcNow
        };
        db.Roles.Add(role);
        roles.Add(role);
    }

    return roles;
}

static async Task<UserSummaryResponse> BuildUserSummaryAsync(IdentityDbContext db, AppUser user, string tenantId)
{
    var roleMap = await db.Roles
        .Where(x => x.TenantId == tenantId)
        .ToDictionaryAsync(x => x.Id, x => x.Name);
    var userRoles = await db.UserRoles
        .Where(x => x.TenantId == tenantId && x.UserId == user.Id)
        .ToListAsync();
    var roles = userRoles
        .Select(role => roleMap.TryGetValue(role.RoleId, out var name) ? name : "")
        .Where(name => name.Length > 0)
        .ToList();

    return BuildUserSummary(user, roles);
}

static UserSummaryResponse BuildUserSummary(AppUser user, List<string> roles)
{
    var isLocked = IsUserCurrentlyLocked(user);

    return new UserSummaryResponse(
        user.Id,
        user.Email,
        user.Username,
        user.DisplayName,
        user.Status,
        ResolveAccountState(user, isLocked),
        isLocked,
        user.LockoutUntilUtc,
        user.FailedLoginCount,
        user.LockoutUntilUtc is not null || user.FailedLoginCount > 0,
        user.TokenVersion,
        roles);
}

static bool IsUserCurrentlyLocked(AppUser user)
{
    return user.LockoutUntilUtc is not null && user.LockoutUntilUtc > DateTime.UtcNow;
}

static string ResolveAccountState(AppUser user, bool isLocked)
{
    if (isLocked && user.Status is "active" or "approved" or "enabled")
    {
        return "locked";
    }

    return string.IsNullOrWhiteSpace(user.Status) ? "unknown" : user.Status;
}

static string? GetClientIp(HttpContext httpContext)
{
    return httpContext.Connection.RemoteIpAddress?.ToString();
}

static string? GetUserAgent(HttpContext httpContext)
{
    return httpContext.Request.Headers.UserAgent.ToString();
}

static string? NormalizeEmail(string? value)
{
    if (string.IsNullOrWhiteSpace(value))
    {
        return null;
    }

    var trimmed = value.Trim();
    try
    {
        var address = new MailAddress(trimmed);
        return string.Equals(address.Address, trimmed, StringComparison.OrdinalIgnoreCase)
            ? address.Address.ToLowerInvariant()
            : null;
    }
    catch
    {
        return null;
    }
}

static string? NormalizePhoneNumber(string? value)
{
    if (string.IsNullOrWhiteSpace(value))
    {
        return null;
    }

    var trimmed = value.Trim();
    var digits = new string(trimmed.Where(char.IsDigit).ToArray());
    if (trimmed.StartsWith("+", StringComparison.Ordinal) && digits.Length is >= 10 and <= 15)
    {
        return $"+{digits}";
    }
    if (digits.Length == 10)
    {
        return $"+1{digits}";
    }
    if (digits.Length is >= 11 and <= 15 && digits.StartsWith("1", StringComparison.Ordinal))
    {
        return $"+{digits}";
    }

    return null;
}

static string? NormalizeUsername(string? value)
{
    if (string.IsNullOrWhiteSpace(value))
    {
        return null;
    }

    return value.Trim();
}

static string? NormalizeDisplayName(string? value)
{
    if (string.IsNullOrWhiteSpace(value))
    {
        return null;
    }

    return value.Trim();
}

static string BuildPhoneBackedEmail(string phone)
{
    var digits = new string(phone.Where(char.IsDigit).ToArray());
    return $"phone.{digits}@phone.cwi.local";
}

static bool IsPhoneBackedEmail(string value)
{
    return value.StartsWith("phone.", StringComparison.OrdinalIgnoreCase) &&
        value.EndsWith("@phone.cwi.local", StringComparison.OrdinalIgnoreCase);
}

static RegistrationIdentity? ResolveRegistrationIdentity(string? email, string? phone, string? username)
{
    var normalizedEmail = NormalizeEmail(email);
    var normalizedPhone = NormalizePhoneNumber(phone);
    if ((normalizedEmail is null && normalizedPhone is null) ||
        (normalizedEmail is not null && normalizedPhone is not null))
    {
        return null;
    }

    if (normalizedPhone is not null)
    {
        return new RegistrationIdentity(
            IdentifierType: "phone",
            DeliveryTarget: normalizedPhone,
            PublicIdentifier: normalizedPhone,
            UserEmail: BuildPhoneBackedEmail(normalizedPhone),
            Username: normalizedPhone);
    }

    return new RegistrationIdentity(
        IdentifierType: "email",
        DeliveryTarget: normalizedEmail!,
        PublicIdentifier: normalizedEmail!,
        UserEmail: normalizedEmail!,
        Username: NormalizeUsername(username));
}

static string MaskIdentifier(string identifierType, string value)
{
    if (identifierType == "phone")
    {
        var digits = new string(value.Where(char.IsDigit).ToArray());
        var last4 = digits.Length >= 4 ? digits[^4..] : digits;
        return $"+***{last4}";
    }

    var at = value.IndexOf('@');
    if (at <= 0)
    {
        return "***";
    }

    var local = value[..at];
    var domain = value[at..];
    return $"{local[0]}***{domain}";
}

static async Task<VerificationDeliveryResult> SendRegistrationVerificationAsync(
    string identifierType,
    string target,
    string code,
    IHostEnvironment environment,
    ILogger logger)
{
    if (environment.IsDevelopment() &&
        string.Equals(Environment.GetEnvironmentVariable("IDENTITY_DEV_SKIP_VERIFICATION_DELIVERY"), "true", StringComparison.OrdinalIgnoreCase))
    {
        logger.LogInformation("Development registration verification code generated for {IdentifierType}.", identifierType);
        return new VerificationDeliveryResult(true, "development", "sent", "Development verification code accepted.");
    }

    return identifierType == "phone"
        ? await SendSmsVerificationAsync(target, code, logger).ConfigureAwait(false)
        : await SendEmailVerificationAsync(target, code, logger).ConfigureAwait(false);
}

static async Task<VerificationDeliveryResult> SendEmailVerificationAsync(string target, string code, ILogger logger)
{
    var smtpHost = Environment.GetEnvironmentVariable("IDENTITY_SMTP_HOST");
    var emailFrom = Environment.GetEnvironmentVariable("IDENTITY_EMAIL_FROM");
    if (string.IsNullOrWhiteSpace(smtpHost) || string.IsNullOrWhiteSpace(emailFrom))
    {
        return new VerificationDeliveryResult(false, "smtp", "not_configured", "Email verification is not configured.");
    }

    var smtpPort = GetIntFromEnv("IDENTITY_SMTP_PORT", 587);
    var smtpUser = Environment.GetEnvironmentVariable("IDENTITY_SMTP_USERNAME");
    var smtpPassword = Environment.GetEnvironmentVariable("IDENTITY_SMTP_PASSWORD");
    var enableSsl = !string.Equals(Environment.GetEnvironmentVariable("IDENTITY_SMTP_ENABLE_SSL"), "false", StringComparison.OrdinalIgnoreCase);

    try
    {
        using var message = new MailMessage(emailFrom, target)
        {
            Subject = "Your CWI verification code",
            Body = $"Your CWI verification code is {code}. It expires soon."
        };
        using var client = new SmtpClient(smtpHost, smtpPort)
        {
            EnableSsl = enableSsl
        };
        if (!string.IsNullOrWhiteSpace(smtpUser))
        {
            client.Credentials = new NetworkCredential(smtpUser, smtpPassword);
        }

        await client.SendMailAsync(message).ConfigureAwait(false);
        return new VerificationDeliveryResult(true, "smtp", "sent", "Verification email sent.");
    }
    catch (Exception ex)
    {
        logger.LogWarning(ex, "Failed to send registration verification email.");
        return new VerificationDeliveryResult(false, "smtp", "send_failed", "Unable to send verification email.");
    }
}

static async Task<VerificationDeliveryResult> SendSmsVerificationAsync(string target, string code, ILogger logger)
{
    var accountSid = Environment.GetEnvironmentVariable("IDENTITY_TWILIO_ACCOUNT_SID");
    var authToken = Environment.GetEnvironmentVariable("IDENTITY_TWILIO_AUTH_TOKEN");
    var fromNumber = Environment.GetEnvironmentVariable("IDENTITY_TWILIO_FROM_NUMBER");
    if (string.IsNullOrWhiteSpace(accountSid) ||
        string.IsNullOrWhiteSpace(authToken) ||
        string.IsNullOrWhiteSpace(fromNumber))
    {
        return new VerificationDeliveryResult(false, "twilio", "not_configured", "SMS verification is not configured.");
    }

    try
    {
        using var client = new HttpClient();
        var credentials = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{accountSid}:{authToken}"));
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"https://api.twilio.com/2010-04-01/Accounts/{Uri.EscapeDataString(accountSid)}/Messages.json");
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", credentials);
        request.Content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["From"] = fromNumber,
            ["To"] = target,
            ["Body"] = $"Your CWI verification code is {code}. It expires soon."
        });

        using var response = await client.SendAsync(request).ConfigureAwait(false);
        if (response.IsSuccessStatusCode)
        {
            return new VerificationDeliveryResult(true, "twilio", "sent", "Verification text sent.");
        }

        logger.LogWarning("Twilio registration verification failed with status {StatusCode}.", response.StatusCode);
        return new VerificationDeliveryResult(false, "twilio", "send_failed", "Unable to send verification text.");
    }
    catch (Exception ex)
    {
        logger.LogWarning(ex, "Failed to send registration verification text.");
        return new VerificationDeliveryResult(false, "twilio", "send_failed", "Unable to send verification text.");
    }
}

static void EnsureBootstrapAdmin(
    IdentityDbContext db,
    string tenantId,
    string email,
    string? username,
    string password,
    string displayName,
    ILogger logger)
{
    var errors = ValidatePasswordPolicy(password);
    if (errors.Count > 0)
    {
        throw new InvalidOperationException($"Bootstrap admin password does not meet policy: {string.Join(", ", errors)}");
    }

    var tenant = db.Tenants.FirstOrDefault(x => x.TenantId == tenantId);
    if (tenant is null)
    {
        tenant = new Tenant
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            Name = tenantId,
            Status = "active",
            CreatedUtc = DateTime.UtcNow,
            UpdatedUtc = DateTime.UtcNow
        };
        db.Tenants.Add(tenant);
    }

    var normalizedUsername = string.IsNullOrWhiteSpace(username) ? null : username.Trim();
    var user = db.Users.FirstOrDefault(x => x.TenantId == tenantId && x.Email == email);
    if (user is null)
    {
        user = new AppUser
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            Username = normalizedUsername,
            Email = email,
            DisplayName = displayName,
            Status = "active",
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(password),
            CreatedUtc = DateTime.UtcNow,
            UpdatedUtc = DateTime.UtcNow
        };
        db.Users.Add(user);
    }
    else
    {
        if (!string.IsNullOrWhiteSpace(normalizedUsername) && string.IsNullOrWhiteSpace(user.Username))
        {
            var usernameOwner = db.Users.FirstOrDefault(x => x.TenantId == tenantId && x.Username == normalizedUsername);
            if (usernameOwner is null || usernameOwner.Id == user.Id)
            {
                user.Username = normalizedUsername;
                user.UpdatedUtc = DateTime.UtcNow;
            }
            else
            {
                logger.LogWarning("Bootstrap admin username {Username} already assigned to another user in tenant {TenantId}.", normalizedUsername, tenantId);
            }
        }

        if (!VerifyPassword(password, user.PasswordHash))
        {
            user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(password);
            user.UpdatedUtc = DateTime.UtcNow;
        }

        if (!string.Equals(user.Status, "active", StringComparison.OrdinalIgnoreCase))
        {
            user.Status = "active";
            user.UpdatedUtc = DateTime.UtcNow;
        }
    }

    var membership = db.Memberships.FirstOrDefault(x => x.TenantId == tenantId && x.UserId == user.Id);
    if (membership is null)
    {
        db.Memberships.Add(new Membership
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            UserId = user.Id,
            Status = "active",
            CreatedUtc = DateTime.UtcNow,
            UpdatedUtc = DateTime.UtcNow
        });
    }

    var role = db.Roles.FirstOrDefault(x => x.TenantId == tenantId && x.Name == "admin");
    if (role is null)
    {
        role = new Role
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            Name = "admin",
            CreatedUtc = DateTime.UtcNow
        };
        db.Roles.Add(role);
    }

    var userRole = db.UserRoles.FirstOrDefault(x => x.TenantId == tenantId && x.UserId == user.Id && x.RoleId == role.Id);
    if (userRole is null)
    {
        db.UserRoles.Add(new UserRole
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            UserId = user.Id,
            RoleId = role.Id,
            CreatedUtc = DateTime.UtcNow
        });
    }

    db.SaveChanges();
    logger.LogInformation("Bootstrap admin ensured for tenant {TenantId} and user {Email}.", tenantId, email);
}

static void EnsureSuperAdmin(
    IdentityDbContext db,
    string tenantId,
    string username,
    string email,
    string password,
    string displayName,
    string platformAdminRoleName,
    ILogger logger)
{
    var errors = ValidatePasswordPolicy(password);
    if (errors.Count > 0)
    {
        throw new InvalidOperationException($"Super admin password does not meet policy: {string.Join(", ", errors)}");
    }

    var tenant = db.Tenants.FirstOrDefault(x => x.TenantId == tenantId);
    if (tenant is null)
    {
        tenant = new Tenant
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            Name = tenantId,
            Status = "active",
            CreatedUtc = DateTime.UtcNow,
            UpdatedUtc = DateTime.UtcNow
        };
        db.Tenants.Add(tenant);
    }

    var normalizedUsername = username.Trim();
    var user = db.Users.FirstOrDefault(x => x.TenantId == tenantId && x.Username == normalizedUsername);
    if (user is null)
    {
        user = new AppUser
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            Username = normalizedUsername,
            Email = email,
            DisplayName = displayName,
            Status = "active",
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(password),
            CreatedUtc = DateTime.UtcNow,
            UpdatedUtc = DateTime.UtcNow
        };
        db.Users.Add(user);
    }
    else
    {
        if (user.Email != email)
        {
            logger.LogWarning("Super admin email differs from configured value; keeping existing {Email}.", user.Email);
        }
        if (!VerifyPassword(password, user.PasswordHash))
        {
            user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(password);
        }
        user.Status = "active";
        user.UpdatedUtc = DateTime.UtcNow;
    }

    var membership = db.Memberships.FirstOrDefault(x => x.TenantId == tenantId && x.UserId == user.Id);
    if (membership is null)
    {
        db.Memberships.Add(new Membership
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            UserId = user.Id,
            Status = "active",
            CreatedUtc = DateTime.UtcNow,
            UpdatedUtc = DateTime.UtcNow
        });
    }

    EnsureUserRole(db, tenantId, user, "admin");
    if (!string.Equals(platformAdminRoleName, "admin", StringComparison.OrdinalIgnoreCase))
    {
        EnsureUserRole(db, tenantId, user, platformAdminRoleName);
    }

    db.SaveChanges();
    logger.LogInformation("Super admin ensured for tenant {TenantId} and username {Username}.", tenantId, normalizedUsername);
}

static void EnsureUserRole(IdentityDbContext db, string tenantId, AppUser user, string roleName)
{
    var normalizedRoleName = roleName.Trim();
    if (string.IsNullOrWhiteSpace(normalizedRoleName))
    {
        return;
    }

    var role = db.Roles.FirstOrDefault(x => x.TenantId == tenantId && x.Name == normalizedRoleName);
    if (role is null)
    {
        role = new Role
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            Name = normalizedRoleName,
            CreatedUtc = DateTime.UtcNow
        };
        db.Roles.Add(role);
    }

    var userRole = db.UserRoles.FirstOrDefault(x => x.TenantId == tenantId && x.UserId == user.Id && x.RoleId == role.Id);
    if (userRole is null)
    {
        db.UserRoles.Add(new UserRole
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            UserId = user.Id,
            RoleId = role.Id,
            CreatedUtc = DateTime.UtcNow
        });
    }
}

public sealed record LoginRequest(
    [property: JsonPropertyName("tenant_id")] string? TenantId,
    [property: JsonPropertyName("email")] string? Email,
    [property: JsonPropertyName("username")] string? Username,
    [property: JsonPropertyName("password")] string Password);

public sealed record TenantlessLoginResolution(
    AppUser? User,
    IReadOnlyList<AppUser> Candidates);

public sealed record RegisterStartRequest(
    [property: JsonPropertyName("tenant_id")] string? TenantId,
    [property: JsonPropertyName("email")] string? Email,
    [property: JsonPropertyName("phone")] string? Phone,
    [property: JsonPropertyName("username")] string? Username,
    [property: JsonPropertyName("display_name")] string? DisplayName);

public sealed record RegisterConfirmRequest(
    [property: JsonPropertyName("verification_id")] Guid VerificationId,
    [property: JsonPropertyName("code")] string Code,
    [property: JsonPropertyName("password")] string Password);

public sealed record RefreshRequest(
    [property: JsonPropertyName("refresh_token")] string RefreshToken);

public sealed record LogoutRequest(
    [property: JsonPropertyName("refresh_token")] string RefreshToken);

public sealed record TokenResponse(
    [property: JsonPropertyName("access_token")] string AccessToken,
    [property: JsonPropertyName("refresh_token")] string RefreshToken,
    [property: JsonPropertyName("token_type")] string TokenType = "Bearer",
    [property: JsonPropertyName("expires_utc")] DateTime ExpiresUtc = default);

public sealed record RegisterResponse(
    [property: JsonPropertyName("access_token")] string AccessToken,
    [property: JsonPropertyName("refresh_token")] string RefreshToken,
    [property: JsonPropertyName("expires_utc")] DateTime ExpiresUtc,
    [property: JsonPropertyName("tenant_id")] string TenantId,
    [property: JsonPropertyName("user_id")] Guid UserId,
    [property: JsonPropertyName("username")] string? Username,
    [property: JsonPropertyName("email")] string? Email,
    [property: JsonPropertyName("phone")] string? Phone,
    [property: JsonPropertyName("display_name")] string DisplayName,
    [property: JsonPropertyName("token_type")] string TokenType = "Bearer");

public sealed record RegisterStartResponse(
    [property: JsonPropertyName("verification_id")] Guid VerificationId,
    [property: JsonPropertyName("identifier_type")] string IdentifierType,
    [property: JsonPropertyName("masked_target")] string MaskedTarget,
    [property: JsonPropertyName("expires_utc")] DateTime ExpiresUtc,
    [property: JsonPropertyName("delivery_method")] string DeliveryMethod);

public sealed record RegistrationIdentity(
    string IdentifierType,
    string DeliveryTarget,
    string PublicIdentifier,
    string UserEmail,
    string? Username);

public sealed record VerificationDeliveryResult(
    bool Sent,
    string Method,
    string Status,
    string Message);

public sealed record PasswordSetRequest(
    [property: JsonPropertyName("tenant_id")] string TenantId,
    [property: JsonPropertyName("email")] string Email,
    [property: JsonPropertyName("password")] string Password);

public sealed record PasswordResetRequest(
    [property: JsonPropertyName("tenant_id")] string TenantId,
    [property: JsonPropertyName("email")] string Email);

public sealed record PasswordResetCompleteRequest(
    [property: JsonPropertyName("token")] string Token,
    [property: JsonPropertyName("new_password")] string NewPassword);

public sealed record PasswordResetResponse(
    [property: JsonPropertyName("reset_token")] string ResetToken,
    [property: JsonPropertyName("expires_utc")] DateTime ExpiresUtc);

public sealed record InviteActivationRequest(
    [property: JsonPropertyName("token")] string Token,
    [property: JsonPropertyName("password")] string Password);

public sealed record TenantCreateRequest(
    [property: JsonPropertyName("tenant_id")] string TenantId,
    [property: JsonPropertyName("name")] string Name);

public sealed record TenantUpdateRequest(
    [property: JsonPropertyName("name")] string? Name,
    [property: JsonPropertyName("status")] string? Status);

public sealed record TenantResponse(
    [property: JsonPropertyName("tenant_id")] string TenantId,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("status")] string Status);

public sealed record UserInviteRequest(
    [property: JsonPropertyName("tenant_id")] string TenantId,
    [property: JsonPropertyName("email")] string Email,
    [property: JsonPropertyName("username")] string? Username,
    [property: JsonPropertyName("display_name")] string? DisplayName);

public sealed record InviteResponse(
    [property: JsonPropertyName("invite_token")] string InviteToken,
    [property: JsonPropertyName("expires_utc")] DateTime ExpiresUtc);

public sealed record RoleCreateRequest(
    [property: JsonPropertyName("tenant_id")] string TenantId,
    [property: JsonPropertyName("name")] string Name);

public sealed record RoleResponse(
    [property: JsonPropertyName("role_id")] Guid RoleId,
    [property: JsonPropertyName("name")] string Name);

public sealed record UserRoleAssignRequest(
    [property: JsonPropertyName("tenant_id")] string TenantId,
    [property: JsonPropertyName("roles")] List<string> Roles);

public sealed record RoleAssignResponse(
    [property: JsonPropertyName("user_id")] Guid UserId,
    [property: JsonPropertyName("roles")] List<string> Roles);

public sealed record UserSummaryResponse(
    [property: JsonPropertyName("user_id")] Guid UserId,
    [property: JsonPropertyName("email")] string Email,
    [property: JsonPropertyName("username")] string? Username,
    [property: JsonPropertyName("display_name")] string DisplayName,
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("account_state")] string AccountState,
    [property: JsonPropertyName("is_locked")] bool IsLocked,
    [property: JsonPropertyName("lockout_until_utc")] DateTime? LockoutUntilUtc,
    [property: JsonPropertyName("failed_login_count")] int FailedLoginCount,
    [property: JsonPropertyName("can_unlock")] bool CanUnlock,
    [property: JsonPropertyName("token_version")] int TokenVersion,
    [property: JsonPropertyName("roles")] List<string> Roles);

public sealed record UserApproveRequest(
    [property: JsonPropertyName("tenant_id")] string TenantId);

public sealed record UserUnlockRequest(
    [property: JsonPropertyName("tenant_id")] string TenantId,
    [property: JsonPropertyName("reason")] string? Reason);

public sealed record AdminPasswordSetRequest(
    [property: JsonPropertyName("tenant_id")] string TenantId,
    [property: JsonPropertyName("password")] string Password);

public sealed record UserRevokeRequest(
    [property: JsonPropertyName("tenant_id")] string TenantId,
    [property: JsonPropertyName("reason")] string? Reason);

public sealed record UserRemoveRequest(
    [property: JsonPropertyName("tenant_id")] string TenantId,
    [property: JsonPropertyName("reason")] string? Reason);

public sealed record UserTenantSwitchRequest(
    [property: JsonPropertyName("tenant_id")] string TenantId,
    [property: JsonPropertyName("target_tenant_id")] string TargetTenantId,
    [property: JsonPropertyName("roles")] List<string>? Roles);

public sealed record TokenVersionResponse(
    [property: JsonPropertyName("token_version")] int TokenVersion);

public sealed record ErrorResponse(
    [property: JsonPropertyName("error")] string Error,
    [property: JsonPropertyName("message")] string Message,
    [property: JsonPropertyName("details")] List<string>? Details);

public sealed class SigningKeyStore
{
    private readonly byte[] _masterKey;
    private readonly IServiceScopeFactory _scopeFactory;
    public string KeyId { get; private set; } = "";

    public SigningKeyStore(string base64MasterKey, IServiceScopeFactory scopeFactory)
    {
        _masterKey = Convert.FromBase64String(base64MasterKey);
        _scopeFactory = scopeFactory;
    }

    public async Task<ECDsa> GetPrivateKeyAsync()
    {
        var key = await GetOrCreateActiveKeyAsync();
        return DecryptPrivateKey(key);
    }

    public async Task<SecurityKey> GetValidationKeyAsync()
    {
        var key = await GetPrivateKeyAsync();
        return new ECDsaSecurityKey(key) { KeyId = KeyId };
    }

    public async Task<object> GetJwksAsync()
    {
        var key = await GetOrCreateActiveKeyAsync();
        using var doc = JsonDocument.Parse(key.PublicJwkJson);
        return new { keys = new[] { doc.RootElement.Clone() } };
    }

    public async Task<object> RotateAsync()
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var active = await db.SigningKeys.FirstOrDefaultAsync(x => x.Status == "active");
        if (active is not null)
        {
            active.Status = "retired";
        }
        var created = CreateNewKey();
        db.SigningKeys.Add(created);
        await db.SaveChangesAsync();
        KeyId = created.KeyId;
        using var doc = JsonDocument.Parse(created.PublicJwkJson);
        return new { keys = new[] { doc.RootElement.Clone() } };
    }

    private async Task<SigningKey> GetOrCreateActiveKeyAsync()
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var existing = await db.SigningKeys.FirstOrDefaultAsync(x => x.Status == "active");
        if (existing is not null)
        {
            KeyId = existing.KeyId;
            return existing;
        }

        var created = CreateNewKey();
        db.SigningKeys.Add(created);
        await db.SaveChangesAsync();
        KeyId = created.KeyId;
        return created;
    }

    private SigningKey CreateNewKey()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var parameters = key.ExportParameters(false);
        var jwk = new
        {
            kty = "EC",
            crv = "P-256",
            x = Base64UrlEncoder.Encode(parameters.Q.X),
            y = Base64UrlEncoder.Encode(parameters.Q.Y),
            kid = Guid.NewGuid().ToString("n"),
            use = "sig",
            alg = "ES256"
        };
        var jwkJson = JsonSerializer.Serialize(jwk);

        var privateKey = key.ExportPkcs8PrivateKey();
        var encrypted = Encrypt(privateKey, out var nonce);

        return new SigningKey
        {
            Id = Guid.NewGuid(),
            KeyId = jwk.kid,
            Status = "active",
            EncryptedPrivateKey = encrypted,
            Nonce = nonce,
            PublicJwkJson = jwkJson,
            CreatedUtc = DateTime.UtcNow
        };
    }

    private ECDsa DecryptPrivateKey(SigningKey key)
    {
        var decrypted = Decrypt(key.EncryptedPrivateKey, key.Nonce);
        var ecdsa = ECDsa.Create();
        ecdsa.ImportPkcs8PrivateKey(decrypted, out _);
        return ecdsa;
    }

    private byte[] Encrypt(byte[] plaintext, out byte[] nonce)
    {
        nonce = RandomNumberGenerator.GetBytes(12);
        var cipher = new byte[plaintext.Length];
        var tag = new byte[16];
        using var aes = new AesGcm(_masterKey, 16);
        aes.Encrypt(nonce, plaintext, cipher, tag);
        var combined = new byte[cipher.Length + tag.Length];
        Buffer.BlockCopy(cipher, 0, combined, 0, cipher.Length);
        Buffer.BlockCopy(tag, 0, combined, cipher.Length, tag.Length);
        return combined;
    }

    private byte[] Decrypt(byte[] combined, byte[] nonce)
    {
        if (combined.Length < 16)
        {
            throw new InvalidOperationException("Invalid encrypted key payload.");
        }
        var cipherLength = combined.Length - 16;
        var cipher = new byte[cipherLength];
        var tag = new byte[16];
        Buffer.BlockCopy(combined, 0, cipher, 0, cipherLength);
        Buffer.BlockCopy(combined, cipherLength, tag, 0, 16);
        var plaintext = new byte[cipherLength];
        using var aes = new AesGcm(_masterKey, 16);
        aes.Decrypt(nonce, cipher, tag, plaintext);
        return plaintext;
    }
}

public partial class Program { }
