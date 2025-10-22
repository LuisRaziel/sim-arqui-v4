using Microsoft.OpenApi.Models;
using RabbitMQ.Client;
using System.Text;
using System.Text.Json;
using Api.Contracts.Requests;

var builder = WebApplication.CreateBuilder(args);

// Swagger
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c => c.SwaggerDoc("v1", new OpenApiInfo { Title = "Orders API", Version = "v1" }));

// RabbitMQ: IConnection = singleton, IModel = scoped
builder.Services.AddSingleton<IConnection>(sp =>
{
    var host = Environment.GetEnvironmentVariable("RABBITMQ__HOST") ?? "localhost";
    var user = Environment.GetEnvironmentVariable("RABBITMQ__USER") ?? "guest";
    var pass = Environment.GetEnvironmentVariable("RABBITMQ__PASS") ?? "guest";

    var factory = new ConnectionFactory
    {
        HostName = host,
        UserName = user,
        Password = pass,
        AutomaticRecoveryEnabled = true
    };
    return factory.CreateConnection();
});
builder.Services.AddScoped<IModel>(sp =>
{
    var conn = sp.GetRequiredService<IConnection>();
    var ch = conn.CreateModel();
    ch.ExchangeDeclare("orders.exchange", "topic", durable: true);
    return ch;
});

var app = builder.Build();

app.UseSwagger();
app.UseSwaggerUI();

// Health
app.MapGet("/health", () => Results.Ok(new { status = "ok" }));

// POST /orders (sin auth todavía)
app.MapPost("/orders", (CreateOrderRequest req, IModel ch) =>
{
    if (req.OrderId == Guid.Empty || req.Amount <= 0)
        return Results.BadRequest(new { message = "Invalid payload" });

    var envelope = new
    {
        messageId = Guid.NewGuid(),
        orderId = req.OrderId,
        amount = req.Amount,
        createdAt = DateTime.UtcNow
    };
    var body = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(envelope));

    var props = ch.CreateBasicProperties();
    props.ContentType = "application/json";
    props.DeliveryMode = 2; // persistente
    props.MessageId = envelope.messageId.ToString();

    ch.BasicPublish("orders.exchange", "orders.created", props, body);
    return Results.Accepted($"/orders/{req.OrderId}", new { status = "queued", req.OrderId });
});

app.Run();
