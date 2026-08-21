using QuotesApi.Models;

namespace QuotesApi.Services;

public class RefreshTokenManager
{
    private readonly IClock _clock;

    public RefreshTokenManager(IClock clock)
    {
        _clock = clock;
    }

    public DateTime UtcNow => _clock.UtcNow.UtcDateTime;

    public bool IsReuseDetected(RefreshToken token)
    {
        return token.RevokedAt is not null &&
               token.ReplacedByToken is not null;
    }

    public void RevokeTokenFamily(
        IEnumerable<RefreshToken> tokens)
    {
        foreach (var token in tokens)
        {
            if (token.RevokedAt is null)
            {
                token.RevokedAt = UtcNow;
            }
        }
    }

    public bool IsExpired(RefreshToken token)
    {
        return token.ExpiresAt <= UtcNow;
    }
}