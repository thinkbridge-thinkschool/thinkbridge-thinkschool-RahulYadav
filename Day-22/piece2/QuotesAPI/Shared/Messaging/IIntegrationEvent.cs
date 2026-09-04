namespace QuotesApi.Shared.Messaging;

// Marker for an event a module publishes across its own boundary for other
// modules to react to (e.g. QuotesApi.Modules.Collections.Contracts.Events).
// Distinct from a module's internal domain events: an integration event is
// the public contract, safe for any other module to depend on.
public interface IIntegrationEvent
{
}
