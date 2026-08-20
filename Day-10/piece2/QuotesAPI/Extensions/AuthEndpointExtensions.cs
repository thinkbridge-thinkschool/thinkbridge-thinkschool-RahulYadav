using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using QuotesApi.Data;
using QuotesApi.Models;
using QuotesApi.Options;
using QuotesApi.Services;

namespace QuotesApi.Extensions;

public static class AuthEndpointExtensions
{
    private sealed record RefreshRequest(string RefreshToken);

    public static IEndpointRouteBuilder MapAuthEndpoints(
        this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/auth");

        // ============================================================
        // POST /api/auth/login
        // ============================================================

        group.MapPost("/login", async (
            LoginRequest request,
            QuotesDbContext db,
            IOptions<JwtOptions> jwtOptions,
            CancellationToken cancellationToken) =>
        {
            var user = await db.Users
                .SingleOrDefaultAsync(
                    x => x.Email == request.Email,
                    cancellationToken);

            if (user is null ||
                !BCrypt.Net.BCrypt.Verify(
                    request.Password,
                    user.PasswordHash))
            {
                return Results.Unauthorized();
            }

            var accessToken = CreateAccessToken(
                user,
                jwtOptions.Value);

            var refreshToken =
                RefreshTokenService.Generate();

            var refreshTokenHash =
                RefreshTokenService.Hash(refreshToken);

            var refreshTokenEntity = new RefreshToken
            {
                Token = refreshTokenHash,
                UserId = user.Id,
                ExpiresAt = DateTime.UtcNow.AddDays(7)
            };

            db.RefreshTokens.Add(refreshTokenEntity);

            await db.SaveChangesAsync(cancellationToken);

            return Results.Ok(
                new LoginResponse(
                    accessToken,
                    refreshToken,
                    (int)jwtOptions.Value.AccessTokenLifetime.TotalSeconds));
        });


        // ============================================================
        // POST /api/auth/refresh
        // ============================================================

        group.MapPost("/refresh", async (
            RefreshRequest request,
            QuotesDbContext db,
            IOptions<JwtOptions> jwtOptions,
            RefreshTokenManager refreshTokenManager,
            CancellationToken cancellationToken) =>
        {
            var tokenHash =
                RefreshTokenService.Hash(request.RefreshToken);

            var storedToken = await db.RefreshTokens
                .SingleOrDefaultAsync(
                    x => x.Token == tokenHash,
                    cancellationToken);

            // Token does not exist
            if (storedToken is null)
                return Results.Unauthorized();


            // ========================================================
            // Refresh-token reuse detection
            // ========================================================

            if (refreshTokenManager.IsReuseDetected(storedToken))
            {
                Console.WriteLine(
                    $"SECURITY EVENT: Refresh token reuse detected. UserId={storedToken.UserId}");

                var familyTokens = await db.RefreshTokens
                    .Where(x => x.UserId == storedToken.UserId)
                    .ToListAsync(cancellationToken);

                refreshTokenManager.RevokeTokenFamily(
                    familyTokens);

                await db.SaveChangesAsync(
                    cancellationToken);

                return Results.Unauthorized();
            }


            // ========================================================
            // Token already revoked
            // ========================================================

            if (storedToken.RevokedAt is not null)
                return Results.Unauthorized();


            // ========================================================
            // Token expired
            // ========================================================

            if (refreshTokenManager.IsExpired(storedToken))
                return Results.Unauthorized();


            // ========================================================
            // Find user
            // ========================================================

            var user = await db.Users
                .SingleOrDefaultAsync(
                    x => x.Id == storedToken.UserId,
                    cancellationToken);

            if (user is null)
                return Results.Unauthorized();


            // ========================================================
            // Create new access token
            // ========================================================

            var accessToken = CreateAccessToken(
                user,
                jwtOptions.Value);


            // ========================================================
            // Rotate refresh token
            // ========================================================

            var newRefreshToken =
                RefreshTokenService.Generate();

            var newRefreshTokenHash =
                RefreshTokenService.Hash(newRefreshToken);

            var newRefreshTokenEntity = new RefreshToken
            {
                Token = newRefreshTokenHash,
                UserId = user.Id,
                ExpiresAt =
                    refreshTokenManager.UtcNow.AddDays(7)
            };


            // ========================================================
            // Revoke old refresh token
            // ========================================================

            storedToken.RevokedAt =
                refreshTokenManager.UtcNow;

            storedToken.ReplacedByToken =
                newRefreshTokenHash;

            db.RefreshTokens.Add(
                newRefreshTokenEntity);

            await db.SaveChangesAsync(
                cancellationToken);

            return Results.Ok(
                new LoginResponse(
                    accessToken,
                    newRefreshToken,
                    (int)jwtOptions.Value.AccessTokenLifetime.TotalSeconds));
        });


        // ============================================================
        // POST /api/auth/logout
        // ============================================================

        group.MapPost("/logout", async (
            RefreshRequest request,
            QuotesDbContext db,
            CancellationToken cancellationToken) =>
        {
            var tokenHash =
                RefreshTokenService.Hash(
                    request.RefreshToken);

            var storedToken = await db.RefreshTokens
                .SingleOrDefaultAsync(
                    x => x.Token == tokenHash,
                    cancellationToken);

            if (storedToken is null)
                return Results.NoContent();

            if (storedToken.RevokedAt is null)
            {
                storedToken.RevokedAt =
                    DateTime.UtcNow;

                await db.SaveChangesAsync(
                    cancellationToken);
            }

            return Results.NoContent();
        });

        return app;
    }


    // ================================================================
    // Create Access Token
    // ================================================================

    private static string CreateAccessToken(
        User user,
        JwtOptions jwtOptions)
    {
        if (string.IsNullOrWhiteSpace(jwtOptions.Key))
        {
            throw new InvalidOperationException(
                "JWT signing key is not configured.");
        }

        if (string.IsNullOrWhiteSpace(jwtOptions.Issuer))
        {
            throw new InvalidOperationException(
                "JWT issuer is not configured.");
        }

        if (string.IsNullOrWhiteSpace(jwtOptions.Audience))
        {
            throw new InvalidOperationException(
                "JWT audience is not configured.");
        }

        var claims = new[]
        {
            new Claim(
                JwtRegisteredClaimNames.Sub,
                user.Id.ToString()),

            new Claim(
                JwtRegisteredClaimNames.Email,
                user.Email),

            new Claim(
                "scope",
                "quotes.write")
        };

        var key = new SymmetricSecurityKey(
            Encoding.UTF8.GetBytes(jwtOptions.Key));

        var credentials = new SigningCredentials(
            key,
            SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(
            issuer: jwtOptions.Issuer,
            audience: jwtOptions.Audience,
            claims: claims,
            expires:
                DateTime.UtcNow.Add(
                    jwtOptions.AccessTokenLifetime),
            signingCredentials: credentials);

        return new JwtSecurityTokenHandler()
            .WriteToken(token);
    }
}