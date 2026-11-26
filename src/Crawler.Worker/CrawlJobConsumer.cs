using System.Text;
using System.Text.Json;
using Crawler.Core.Application;
using Crawler.Core.Messaging;
using Crawler.Infrastructure.Messaging;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace Crawler.Worker;

/// <summary>
/// Consumes crawl.jobs with manual acks (at-least-once).
///   success                  -> ack
///   poison (bad JSON, unknown job) -> reject without requeue -> dead-lettered to crawl.jobs.dlq
///   other exception          -> republish to crawl.jobs.retry with x-attempt+1, then ack
///                               (after MaxDeliveryAttempts: mark job Failed, reject -> DLQ)
///   worker shutting down     -> nack with requeue, another delivery resumes the job idempotently
/// </summary>
public sealed class CrawlJobConsumer(
    RabbitMqConnection connection,
    IServiceScopeFactory scopeFactory,
    IOptions<RabbitMqOptions> options,
    TimeProvider clock,
    ILogger<CrawlJobConsumer> logger) : BackgroundService
{
    private readonly RabbitMqOptions _options = options.Value;
    private IModel? _consumeChannel;
    private IModel? _retryChannel;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var conn = await connection.GetConnectionAsync(stoppingToken);
        _consumeChannel = conn.CreateModel();
        _retryChannel = conn.CreateModel();
        _consumeChannel.BasicQos(prefetchSize: 0, prefetchCount: _options.PrefetchCount, global: false);

        var consumer = new AsyncEventingBasicConsumer(_consumeChannel);
        consumer.Received += (_, delivery) => HandleAsync(delivery, stoppingToken);
        _consumeChannel.BasicConsume(_options.QueueName, autoAck: false, consumer);

        logger.LogInformation("Consuming {Queue} (prefetch {Prefetch}, max attempts {MaxAttempts})",
            _options.QueueName, _options.PrefetchCount, _options.MaxDeliveryAttempts);

        try
        {
            await Task.Delay(Timeout.Infinite, stoppingToken);
        }
        catch (OperationCanceledException)
        {
            // normal shutdown
        }
    }

    private async Task HandleAsync(BasicDeliverEventArgs delivery, CancellationToken stoppingToken)
    {
        var channel = _consumeChannel!;
        var attempt = ReadAttempt(delivery.BasicProperties);
        var message = TryDeserialize(delivery.Body);
        if (message is null)
        {
            logger.LogError("Malformed message {MessageId}; routing to DLQ", delivery.BasicProperties.MessageId);
            channel.BasicReject(delivery.DeliveryTag, requeue: false);
            return;
        }

        using var _ = logger.BeginScope("JobId:{JobId} CorrelationId:{CorrelationId} MessageId:{MessageId} Attempt:{Attempt}",
            message.JobId, message.CorrelationId, message.MessageId, attempt);

        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            await scope.ServiceProvider.GetRequiredService<CrawlJobProcessor>().ProcessAsync(message.JobId, stoppingToken);
            channel.BasicAck(delivery.DeliveryTag, multiple: false);
        }
        catch (PoisonMessageException ex)
        {
            logger.LogError("Poison message: {Reason}; routing to DLQ", ex.Message);
            channel.BasicReject(delivery.DeliveryTag, requeue: false);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            logger.LogWarning("Worker stopping; returning message to the queue");
            channel.BasicNack(delivery.DeliveryTag, multiple: false, requeue: true);
        }
        catch (Exception ex)
        {
            await HandleTransientFailureAsync(delivery, message, attempt, ex);
        }
    }

    private async Task HandleTransientFailureAsync(BasicDeliverEventArgs delivery, CrawlJobRequested message, int attempt, Exception ex)
    {
        var channel = _consumeChannel!;
        if (attempt < _options.MaxDeliveryAttempts)
        {
            logger.LogWarning(ex, "Attempt {Attempt}/{MaxAttempts} failed; retrying in {DelayMs} ms",
                attempt, _options.MaxDeliveryAttempts, _options.RetryDelayMilliseconds);
            RabbitMqJobPublisher.Publish(_retryChannel!, _options.RetryQueueName, message, attempt + 1);
            channel.BasicAck(delivery.DeliveryTag, multiple: false);
            return;
        }

        logger.LogError(ex, "Attempt {Attempt}/{MaxAttempts} failed; giving up and routing to DLQ",
            attempt, _options.MaxDeliveryAttempts);
        await TryMarkFailedAsync(message.JobId, $"Processing failed after {attempt} attempts: {ex.Message}");
        channel.BasicReject(delivery.DeliveryTag, requeue: false);
    }

    private async Task TryMarkFailedAsync(Guid jobId, string reason)
    {
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var store = scope.ServiceProvider.GetRequiredService<ICrawlStore>();
            await store.MarkFailedAsync(jobId, reason, clock.GetUtcNow().UtcDateTime, CancellationToken.None);
        }
        catch (Exception ex)
        {
            // The DB is probably what is failing; the message is still preserved in the DLQ.
            logger.LogError(ex, "Could not mark job as Failed");
        }
    }

    private static int ReadAttempt(IBasicProperties properties)
    {
        if (properties.Headers is not null
            && properties.Headers.TryGetValue(RabbitMqTopology.AttemptHeader, out var value)
            && value is not null)
        {
            return value switch
            {
                int i => i,
                long l => (int)l,
                byte[] bytes when int.TryParse(Encoding.UTF8.GetString(bytes), out var parsed) => parsed,
                _ => 1
            };
        }

        return 1;
    }

    private static CrawlJobRequested? TryDeserialize(ReadOnlyMemory<byte> body)
    {
        try
        {
            var message = JsonSerializer.Deserialize<CrawlJobRequested>(body.Span, MessageJson.Options);
            return message is { JobId: var id } && id != Guid.Empty ? message : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public override void Dispose()
    {
        _consumeChannel?.Dispose();
        _retryChannel?.Dispose();
        base.Dispose();
    }
}
