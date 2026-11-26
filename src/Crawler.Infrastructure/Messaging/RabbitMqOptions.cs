namespace Crawler.Infrastructure.Messaging;

public sealed class RabbitMqOptions
{
    public const string SectionName = "RabbitMq";

    public string HostName { get; set; } = "localhost";
    public int Port { get; set; } = 5672;
    public string UserName { get; set; } = "guest";
    public string Password { get; set; } = "guest";
    public string VirtualHost { get; set; } = "/";

    public string QueueName { get; set; } = "crawl.jobs";
    public string RetryQueueName { get; set; } = "crawl.jobs.retry";
    public string DeadLetterQueueName { get; set; } = "crawl.jobs.dlq";

    /// <summary>How long a failed message waits in the retry queue before it is redelivered.</summary>
    public int RetryDelayMilliseconds { get; set; } = 10_000;

    /// <summary>Total processing attempts (first delivery + retries) before a message goes to the DLQ.</summary>
    public int MaxDeliveryAttempts { get; set; } = 3;

    /// <summary>Jobs processed concurrently by one worker instance.</summary>
    public ushort PrefetchCount { get; set; } = 1;
}
