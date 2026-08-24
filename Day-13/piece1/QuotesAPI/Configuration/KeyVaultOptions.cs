namespace QuotesApi.Configuration;

public sealed record KeyVaultOptions
{
    public string? VaultUri { get; init; }
}
