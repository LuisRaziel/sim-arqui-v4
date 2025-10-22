using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Caching.Memory;

var host = Environment.GetEnvironmentVariable("RABBITMQ__HOST") ?? "localhost";
var user = Environment.GetEnvironmentVariable("RABBITMQ__USER") ?? "guest";
var pass = Environment.GetEnvironmentVariable("RABBITMQ__PASS") ?? "guest";
var prefetch = int.TryParse(Environment.GetEnvironmentVariable("WORKER__PREFETCH"), out var p) ? p : 10;
var maxRetries = int.TryParse(Environment.GetEnvironmentVariable("WORKER__RETRYCOUNT"), out var r) ? r : 3;

const string exchange = "orders.exchange";
const string routingKey = "orders.created";
const string queue = "orders.queue";
const string dlx = "orders.dlx";
const string dlq = "orders.dlq";

// idempotencia simple con TTL
var cache = new MemoryCache(new MemoryCacheOptions { SizeLimit = 10_000 });
bool TryMark(string k)
{
    if (cache.TryGetValue(k, out _)) return false;
    cache.Set(k, true, new MemoryCacheEntryOptions
    {
        AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(10),
        Size = 1
    });
    return true;
}

while (true)
{
    try
    {
        var f = new ConnectionFactory
        {
            HostName = host,
            UserName = user,
            Password = pass,
            DispatchConsumersAsync = true,
            AutomaticRecoveryEnabled = true
        };
        using var conn = f.CreateConnection();
        using var ch = conn.CreateModel();

        ch.ExchangeDeclare(exchange, "topic", durable: true);
        ch.ExchangeDeclare(dlx, "fanout", durable: true);
        ch.QueueDeclare(dlq, durable: true, exclusive: false, autoDelete: false);
        ch.QueueBind(dlq, dlx, "");

        var qArgs = new Dictionary<string, object> { ["x-dead-letter-exchange"] = dlx };
        ch.QueueDeclare(queue, durable: true, exclusive: false, autoDelete: false, arguments: qArgs);
        ch.QueueBind(queue, exchange, routingKey);
        ch.BasicQos(0, (ushort)prefetch, false);

        var consumer = new AsyncEventingBasicConsumer(ch);
        consumer.Received += async (_, ea) =>
        {
            var json = Encoding.UTF8.GetString(ea.Body.ToArray());
            try
            {
                var root = JsonDocument.Parse(json).RootElement;
                var orderId = root.GetProperty("orderId").GetGuid().ToString();
                var amount  = root.GetProperty("amount").GetDecimal();

                var key = ea.BasicProperties?.MessageId ?? orderId;
                if (!TryMark(key)) { ch.BasicAck(ea.DeliveryTag, false); return; }

                await Task.Delay(20); // simula trabajo
                ch.BasicAck(ea.DeliveryTag, false);
            }
            catch
            {
                var retries = 0;
                if (ea.BasicProperties?.Headers != null &&
                    ea.BasicProperties.Headers.TryGetValue("x-retry", out var rv) &&
                    rv is byte[] b && int.TryParse(Encoding.UTF8.GetString(b), out var parsed))
                {
                    retries = parsed;
                }

                if (retries + 1 >= maxRetries)
                {
                    ch.BasicReject(ea.DeliveryTag, requeue: false); // a DLQ
                }
                else
                {
                    var props = ch.CreateBasicProperties();
                    props.Headers = new Dictionary<string, object> { ["x-retry"] = retries + 1 };
                    props.DeliveryMode = 2;
                    props.ContentType  = "application/json";

                    ch.BasicPublish(exchange, routingKey, props, ea.Body);
                    ch.BasicAck(ea.DeliveryTag, false);
                }
            }
        };

        ch.BasicConsume(queue, autoAck: false, consumer);
        await Task.Delay(Timeout.Infinite);
    }
    catch
    {
        await Task.Delay(3000); // reconexión
    }
}