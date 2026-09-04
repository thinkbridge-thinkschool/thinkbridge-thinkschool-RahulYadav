namespace QuotesApi.Modules.Collections.Application.Commands;

public sealed record CreateCollectionCommand(string Name, int OwnerId);
