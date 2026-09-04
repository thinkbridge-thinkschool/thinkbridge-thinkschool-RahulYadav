namespace QuotesApi.Options;

// Day 19: configuration for the Azure Service Bus topic/subscription
// pub-sub flow. Deliberately holds no secret — only the namespace hostname
// (public, not sensitive) and entity names. Authentication is handled by
// an Azure identity credential (see Program.cs), never a connection string.
//
// Left unset (empty FullyQualifiedNamespace) means "Service Bus is not
// configured" — Program.cs registers a no-op publisher and skips starting
// the subscription workers in that case, so local/test environments never
// need Azure connectivity (see appsettings.Testing.json, which has no
// ServiceBus section).
public sealed class ServiceBusOptions
{
    // e.g. "sb-day19-quotedemo.servicebus.windows.net" — no protocol,
    // no port, no key.
    public string? FullyQualifiedNamespace { get; set; }

    public string TopicName { get; set; } = "quote-events";

    public string SubscriptionA { get; set; } = "sub-a";

    public string SubscriptionB { get; set; } = "sub-b";

    // Number of competing consumer workers started against Subscription A,
    // to make the "multiple workers race for messages on the same
    // subscription" behavior visible. Subscription B always gets exactly
    // one worker — the competing-consumers demo only needs to happen once.
    public int SubscriptionAWorkerCount { get; set; } = 2;
}
