using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Caching.Memory;
using Serilog;
using Serilog.Context;
using System.Globalization;
using Microsoft.AspNetCore.Builder;  // <-- para WebApplication, MapGet, MapMetrics
using Microsoft.AspNetCore.Http;     // <-- para Results
using Prometheus;  

// ===== Logs JSON compactos =====
Log.Logger = new LoggerConfiguration()
    .Enrich.FromLogContext()
    .WriteTo.Console(new Serilog.Formatting.Compact.CompactJsonFormatter())
    .CreateLogger();
    
Serilog.Log.Information("worker_starting");

var ordersProcessedTotal = Metrics.CreateCounter("orders_processed_total", "Órdenes procesadas por el worker");
var ordersFailedTotal    = Metrics.CreateCounter("orders_failed_total", "Órdenes enviadas a DLQ");

var metricsBuilder = WebApplication.CreateBuilder();
var metricsApp = metricsBuilder.Build();
metricsApp.MapMetrics(); // expondrá /metrics
_ = Task.Run(() => metricsApp.Run("http://0.0.0.0:8081"));
Serilog.Log.Information("worker_metrics_endpoint_started port=8081");


// ===== Config por entorno =====
var host       = Environment.GetEnvironmentVariable("RABBITMQ__HOST") ?? "localhost";
var user       = Environment.GetEnvironmentVariable("RABBITMQ__USER") ?? "guest";
var pass       = Environment.GetEnvironmentVariable("RABBITMQ__PASS") ?? "guest";
var prefetch   = int.TryParse(Environment.GetEnvironmentVariable("WORKER__PREFETCH"), out var p) ? p : 10;
var maxRetries = int.TryParse(Environment.GetEnvironmentVariable("WORKER__RETRYCOUNT"), out var r) ? r : 3;

const string exchange    = "orders.exchange";
const string routingKey  = "orders.created";
const string queue       = "orders.queue";
const string dlx         = "orders.dlx";
const string dlq         = "orders.dlq";

// ===== Idempotencia simple con TTL =====
var cache = new MemoryCache(new MemoryCacheOptions { SizeLimit = 10_000 });
bool TryMark(string key)
{
    if (cache.TryGetValue(key, out _)) return false;
    cache.Set(key, true, new MemoryCacheEntryOptions
    {
        AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(10),
        Size = 1
    });
    return true;
}

// ===== Helper: lectura robusta de x-retry =====
static int GetRetryCount(IBasicProperties? props)
{
    try
    {
        if (props?.Headers is null) return 0;
        if (!props.Headers.TryGetValue("x-retry", out var raw) || raw is null) return 0;

        return raw switch
        {
            byte[] b                 => int.TryParse(Encoding.UTF8.GetString(b), out var i1) ? i1 : 0,
            ReadOnlyMemory<byte> mem => int.TryParse(Encoding.UTF8.GetString(mem.ToArray()), out var i2) ? i2 : 0,
            sbyte sb                 => (int)sb,
            byte bb                  => (int)bb,
            short s                  => (int)s,
            ushort us                => (int)us,
            int ii                   => ii,
            uint ui                  => (int)ui,
            long l                   => (int)l,
            ulong ul                 => (int)ul,
            string str               => int.TryParse(str, out var i3) ? i3 : 0,
            _                        => 0
        };
    }
    catch { return 0; }
}

while (true)
{
    try
    {
        var factory = new ConnectionFactory
        {
            HostName = host,
            UserName = user,
            Password = pass,
            DispatchConsumersAsync = true,
            AutomaticRecoveryEnabled = true
        };

        using var conn = factory.CreateConnection();
        using var ch   = conn.CreateModel();

        // Topología
        ch.ExchangeDeclare(exchange, "topic", durable: true);
        ch.ExchangeDeclare(dlx,      "fanout", durable: true);

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
                using var doc  = JsonDocument.Parse(json);
                var root       = doc.RootElement;

                // Envelope v1.0 o legacy
                var payload = root.TryGetProperty("data", out var dataEl) ? dataEl : root;

                // --- CorrelationId: payload -> props -> headers ---
                string? correlationId = null;
                if (root.TryGetProperty("correlationId", out var cidEl))
                    correlationId = cidEl.GetString();
                if (string.IsNullOrWhiteSpace(correlationId))
                    correlationId = ea.BasicProperties?.CorrelationId;
                if (string.IsNullOrWhiteSpace(correlationId)
                    && ea.BasicProperties?.Headers != null
                    && ea.BasicProperties.Headers.TryGetValue("X-Correlation-Id", out var rawCid))
                {
                    correlationId = rawCid switch
                    {
                        byte[] b                 => Encoding.UTF8.GetString(b),
                        ReadOnlyMemory<byte> mem => Encoding.UTF8.GetString(mem.ToArray()),
                        string s                 => s,
                        _                        => null
                    };
                }

                using (LogContext.PushProperty("CorrelationId", correlationId ?? "n/a"))
                {
                    // --- orderId (camelCase o PascalCase) ---
                    Guid orderId;
                    if (payload.TryGetProperty("orderId", out var orderIdEl) ||
                        payload.TryGetProperty("OrderId", out orderIdEl))
                    {
                        var raw = orderIdEl.ValueKind == JsonValueKind.String ? orderIdEl.GetString() : orderIdEl.ToString();
                        if (!Guid.TryParse(raw, out orderId))
                        {
                            Serilog.Log.Warning("invalid_payload_orderId_parse_error {Raw}", raw);
                            ch.BasicAck(ea.DeliveryTag, false);
                            
                            return;
                        }
                    }
                    else
                    {
                        Serilog.Log.Warning("invalid_payload_missing_orderId {Body}", json);
                        ch.BasicAck(ea.DeliveryTag, false);
                        
                        return;
                    }

                    // --- amount (camelCase o PascalCase) ---
                    decimal amount;
                    if (payload.TryGetProperty("amount", out var amountEl) ||
                        payload.TryGetProperty("Amount", out amountEl))
                    {
                        try
                        {
                            amount = amountEl.ValueKind == JsonValueKind.Number
                                ? amountEl.GetDecimal()
                                : decimal.Parse(amountEl.ToString() ?? "0", CultureInfo.InvariantCulture);
                        }
                        catch
                        {
                            Serilog.Log.Warning("invalid_payload_amount_parse_error {OrderId} {Raw}", orderId, amountEl.ToString());
                            ch.BasicAck(ea.DeliveryTag, false);
                            
                            return;
                        }
                    }
                    else
                    {
                        Serilog.Log.Warning("invalid_payload_missing_amount {OrderId}", orderId);
                        ch.BasicAck(ea.DeliveryTag, false);
                        
                        return;
                    }

                    // Log y idempotencia
                    Serilog.Log.Information("order_processing {OrderId} {Amount}", orderId, amount);

                    var key = ea.BasicProperties?.MessageId ?? orderId.ToString();
                    if (!TryMark(key))
                    {
                        Serilog.Log.Information("duplicate_ignored {Key}", key);
                        ch.BasicAck(ea.DeliveryTag, false);
                        
                        return;
                    }

                    // Simula trabajo
                    await Task.Delay(20);

                    ch.BasicAck(ea.DeliveryTag, false);
                    ordersProcessedTotal.Inc();
                    Serilog.Log.Information("order_processed {OrderId}", orderId);
                }
            }
            catch (Exception ex)
            {
                var retries = GetRetryCount(ea.BasicProperties);

                if (retries + 1 >= maxRetries)
                {
                    Serilog.Log.Error(ex, "worker_error_to_dlq {Retry}", retries + 1);
                    ch.BasicReject(ea.DeliveryTag, requeue: false); // a DLQ
                    ordersFailedTotal.Inc();
                }
                else
                {
                    var props = ch.CreateBasicProperties();
                    props.Headers ??= new Dictionary<string, object>();
                    props.Headers["x-retry"] = retries + 1;
                    props.DeliveryMode = 2;
                    props.ContentType  = "application/json";

                    // Re-publica y ACKea el original
                    ch.BasicPublish(exchange, routingKey, props, ea.Body);
                    ch.BasicAck(ea.DeliveryTag, false);                    
                    Serilog.Log.Error(ex, "worker_error_retrying {Retry}", retries + 1);
                }
            }
        };

        ch.BasicConsume(queue, autoAck: false, consumer);

        // Mantener vivo
        await Task.Delay(Timeout.Infinite);
    }
    catch (Exception ex)
    {
        Serilog.Log.Warning(ex, "worker_broker_connection_failed host={Host}", host);
        await Task.Delay(3000); // reconexión
    }
}