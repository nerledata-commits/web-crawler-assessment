using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using RabbitMQ.Client.Exceptions;

namespace Crawler.Infrastructure.Messaging;

/// <summary>
/// One long-lived connection per process (RabbitMQ best practice). Connects lazily and retries while
/// the broker is starting. Declares the queue topology on first connect.
/// </summary>
public sealed class RabbitMqConnection(IOptions<RabbitMqOptions> options, ILogger<RabbitMqConnection> logger) : IDisposable
{
    private readonly RabbitMqOptions _options = options.Value;
    private readonly SemaphoreSlim _lock = new(1, 1);
    private IConnection? _connection;

    public RabbitMqOptions Options => _options;

    public bool IsOpen => _connection?.IsOpen == true;

    public async Task<IConnection> GetConnectionAsync(CancellationToken ct)
    {
        if (_connection is { IsOpen: true })
        {
            return _connection;
        }

        await _lock.WaitAsync(ct);
        try
        {
            if (_connection is { IsOpen: true })
            {
                return _connection;
            }

            _connection?.Dispose();
            _connection = await ConnectWithRetryAsync(ct);
            using var channel = _connection.CreateModel();
            RabbitMqTopology.Declare(channel, _options);
            return _connection;
        }
        finally
        {
            _lock.Release();
        }
    }

    private async Task<IConnection> ConnectWithRetryAsync(CancellationToken ct)
    {
        var factory = new ConnectionFactory
        {
            HostName = _options.HostName,
            Port = _options.Port,
            UserName = _options.UserName,
            Password = _options.Password,
            VirtualHost = _options.VirtualHost,
            DispatchConsumersAsync = true,
            AutomaticRecoveryEnabled = true,
            ClientProvidedName = AppDomain.CurrentDomain.FriendlyName
        };

        const int maxAttempts = 15;
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                return factory.CreateConnection();
            }
            catch (BrokerUnreachableException ex) when (attempt < maxAttempts)
            {
                logger.LogWarning("RabbitMQ not reachable at {Host}:{Port} (attempt {Attempt}/{MaxAttempts}): {Error}",
                    _options.HostName, _options.Port, attempt, maxAttempts, ex.Message);
                await Task.Delay(TimeSpan.FromSeconds(2), ct);
            }
        }
    }

    public void Dispose()
    {
        _connection?.Dispose();
        _lock.Dispose();
    }
}
