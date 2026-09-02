using Azure.Messaging.ServiceBus;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using QuotesApi.Options;

namespace QuotesApi.Messaging;

// One BackgroundService instance per named worker. Program.cs registers
// three of these:
//
//   Worker-A1  -> subscription "sub-a"
//   Worker-A2  -> subscription "sub-a"   (competing consumer with A1)
//   Worker-B1  -> subscription "sub-b"
//
// Worker-A1 and Worker-A2 both attach a ServiceBusProcessor to the SAME
// subscription, so Service Bus hands each delivered message to whichever
// one is free — that is the competing-consumers behavior. Worker-B1 is on
// an independent subscription that gets its own copy of every message
// published to the topic; it never competes with A1/A2, it duplicates
// the whole stream for a different consumer group. Both mechanisms are on
// display at once: two subscriptions each seeing every message, and two
// workers racing for messages within one of those subscriptions.
//
// Message settlement uses the SDK's normal API: success completes the
// message, a processing failure abandons it. Service Bus itself tracks
// delivery count and moves a message to the subscription's real
// dead-letter queue once MaxDeliveryCount is exceeded (configured on
// sub-a/sub-b as 3) — this class never talks to a DLQ directly.
public sealed class ServiceBusSubscriptionWorker : BackgroundService
{
    private readonly string _subscriptionName;
    private readonly string _workerName;
    private readonly ServiceBusClient _client;
    private readonly ServiceBusOptions _options;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<ServiceBusSubscriptionWorker> _logger;

    private ServiceBusProcessor? _processor;

    public ServiceBusSubscriptionWorker(
        string subscriptionName,
        string workerName,
        ServiceBusClient client,
        IOptions<ServiceBusOptions> options,
        IServiceScopeFactory scopeFactory,
        ILogger<ServiceBusSubscriptionWorker> logger)
    {
        _subscriptionName = subscriptionName;
        _workerName = workerName;
        _client = client;
        _options = options.Value;
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    public override async Task StartAsync(CancellationToken cancellationToken)
    {
        _processor = _client.CreateProcessor(
            _options.TopicName,
            _subscriptionName,
            new ServiceBusProcessorOptions
            {
                // One in-flight message per worker instance. Competing
                // consumers on sub-a come from running two separate
                // worker instances (A1/A2), not from concurrency inside a
                // single processor — that keeps "which worker handled
                // this message" unambiguous in the logs.
                MaxConcurrentCalls = 1,
                AutoCompleteMessages = false
            });

        _processor.ProcessMessageAsync += ProcessMessageAsync;
        _processor.ProcessErrorAsync += ProcessErrorAsync;

        await _processor.StartProcessingAsync(cancellationToken);

        _logger.LogInformation(
            "{Worker} started, listening on subscription {Subscription}.",
            _workerName,
            _subscriptionName);

        await base.StartAsync(cancellationToken);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // All of the work happens in the processor's event handlers,
        // started above in StartAsync. This just holds the hosted service
        // alive until shutdown is requested.
        try
        {
            await Task.Delay(Timeout.Infinite, stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Expected on host shutdown.
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        if (_processor is not null)
        {
            await _processor.StopProcessingAsync(cancellationToken);
        }

        await base.StopAsync(cancellationToken);

        _logger.LogInformation(
            "{Worker} stopped (subscription {Subscription}).",
            _workerName,
            _subscriptionName);
    }

    public override void Dispose()
    {
        _processor?.DisposeAsync().AsTask().GetAwaiter().GetResult();
        base.Dispose();
    }

    private async Task ProcessMessageAsync(ProcessMessageEventArgs args)
    {
        var message = args.Message;

        try
        {
            // A new DI scope per message: QuoteEventMessageHandler depends
            // on IProcessedMessageStore, which holds a scoped DbContext —
            // this worker itself is a singleton hosted service and cannot
            // depend on scoped services directly. Same pattern as
            // QuoteProcessingBackgroundService (Day 18).
            using var scope = _scopeFactory.CreateScope();

            var handler = scope.ServiceProvider
                .GetRequiredService<QuoteEventMessageHandler>();

            await handler.HandleAsync(
                _subscriptionName,
                _workerName,
                message.MessageId,
                message.Body,
                args.CancellationToken);

            await args.CompleteMessageAsync(message, args.CancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "[{Worker}/{Subscription}] Failed to process {MessageId} " +
                "(delivery attempt {DeliveryCount}); abandoning for retry.",
                _workerName,
                _subscriptionName,
                message.MessageId,
                message.DeliveryCount);

            // Abandon, not dead-letter: Service Bus itself dead-letters
            // the message once MaxDeliveryCount is exceeded. Explicitly
            // dead-lettering here would short-circuit that broker-owned
            // retry policy instead of demonstrating it.
            await args.AbandonMessageAsync(message, cancellationToken: args.CancellationToken);
        }
    }

    private Task ProcessErrorAsync(ProcessErrorEventArgs args)
    {
        _logger.LogError(
            args.Exception,
            "{Worker} Service Bus processor error on {Subscription} (source: {ErrorSource}).",
            _workerName,
            _subscriptionName,
            args.ErrorSource);

        return Task.CompletedTask;
    }
}
