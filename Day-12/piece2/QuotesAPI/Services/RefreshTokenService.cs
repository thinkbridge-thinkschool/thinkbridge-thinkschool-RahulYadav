using System.Security.Cryptography;
using System.Text;

namespace QuotesApi.Services;

public static class RefreshTokenService
{
    public static string Generate()
    {
        return Convert.ToBase64String(
            RandomNumberGenerator.GetBytes(32));
    }

    public static string Hash(string token)
    {
        var bytes = SHA256.HashData(
            Encoding.UTF8.GetBytes(token));

        return Convert.ToHexString(bytes);
    }
}