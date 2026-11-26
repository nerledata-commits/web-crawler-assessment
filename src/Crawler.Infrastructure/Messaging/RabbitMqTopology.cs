using RabbitMQ.Client;

namespace Crawler.Infrastructure.Messaging;

/// <summary>
/// crawl.jobs        main work queue. Rejected messages (requeue=false) dead-letter to crawl.jobs.dlq.
/// crawl.jobs.retry  delay queue with a TTL; expired messages dead-letter back to crawl.jobs.
/// crawl.jobs.dlq    poison messages and messages that exhausted their retries. Inspected manually.
/// All queues are durable and messages are published as persistent.
/// </summary>
public static class RabbitMqTopology
{
    public const string AttemptHeader = "x-attempt";
    public const string MessageType = "CrawlJobRequested.v1";

    public static void Declare(IModel channel, RabbitMqOptions options)
    {
        channel.QueueDeclare(options.DeadLetterQueueName, durable: true, exclusive: false, autoDelete: false);

        channel.QueueDeclare(options.QueueName, durable: true, exclusive: false, autoDelete: false,
            arguments: new Dictionary<string, object>
            {
                ["x-dead-letter-exchange"] = "",
                ["x-dead-letter-routing-key"] = options.DeadLetterQueueName
            });

        channel.QueueDeclare(options.RetryQueueName, durable: true, exclusive: false, autoDelete: false,
            arguments: new Dictionary<string, object>
            {
                ["x-message-ttl"] = options.RetryDelayMilliseconds,
                ["x-dead-letter-exchange"] = "",
                ["x-dead-letter-routing-key"] = options.QueueName
            });
    }
}
