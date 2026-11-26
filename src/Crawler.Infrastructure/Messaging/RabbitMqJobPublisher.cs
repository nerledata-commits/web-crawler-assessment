using System.Text.Json;
using Crawler.Core.Application;
using Crawler.Core.Messaging;
using RabbitMQ.Client;

namespace Crawler.Infrastructure.Messaging;

/// <summary>Publishes job messages with publisher confirms, so a successful return means the broker has the message.</summary>
public sealed class RabbitMqJobPublisher(RabbitMqConnection connection) : IJobPublisher
{
    private static readonly TimeSpan ConfirmTimeout = TimeSpan.FromSeconds(5);

    public async Task PublishAsync(CrawlJobRequested message, CancellationToken ct)
    {
        var conn = await connection.GetConnectionAsync(ct);
        using var channel = conn.CreateModel();
        Publish(channel, connection.Options.QueueName, message, attempt: 1);
    }

    /// <summary>Publishes to a queue via the default exchange and waits for the broker confirm.</summary>
    public static void Publish(IModel channel, string queueName, CrawlJobRequested message, int attempt)
    {
        channel.ConfirmSelect();

        var properties = channel.CreateBasicProperties();
        properties.Persistent = true;
        properties.ContentType = "application/json";
        properties.MessageId = message.MessageId.ToString();
        properties.CorrelationId = message.CorrelationId;
        properties.Type = RabbitMqTopology.MessageType;
        properties.Headers = new Dictionary<string, object> { [RabbitMqTopology.AttemptHeader] = attempt };

        var body = JsonSerializer.SerializeToUtf8Bytes(message, MessageJson.Options);
        channel.BasicPublish(exchange: "", routingKey: queueName, mandatory: false, basicProperties: properties, body: body);
        channel.WaitForConfirmsOrDie(ConfirmTimeout);
    }
}

public static class MessageJson
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);
}
