using Microsoft.OpenApi.Models;
using RabbitMQ.Client;
using System.Text;
using System.Text.Json;
using Api.Contracts.Requests;using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using Microsoft.IdentityModel.JsonWebTokens;
using System.Security.Claims;
using System.Text;
using Serilog;

// Logs JSON compactos (listos para ELK/DataDog)
Log.Logger = new LoggerConfiguration()
    .Enrich.FromLogContext()
    .WriteTo.Console(new Serilog.Formatting.Compact.CompactJsonFormatter())
    .CreateLogger();
    
var builder = WebApplication.CreateBuilder(args);
builder.Host.UseSerilog();

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

var jwtKey = builder.Configuration["JWT__KEY"] ?? "dev-local-change-me";

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
  .AddJwtBearer(o =>
  {
      o.RequireHttpsMetadata = false;
      o.TokenValidationParameters = new TokenValidationParameters
      {
          ValidateIssuer = false,
          ValidateAudience = false,
          ValidateIssuerSigningKey = true,
          IssuerSigningKey = new SymmetricSecurityKey(
              Encoding.UTF8.GetBytes(jwtKey.Length >= 32 ? jwtKey : jwtKey.PadRight(32, '_')))
      };
  });

builder.Services.AddAuthorization();

var app = builder.Build();

app.UseSwagger();
app.UseSwaggerUI();
app.UseAuthentication();
app.UseAuthorization();

// Health
app.MapGet("/health", () => Results.Ok(new { status = "ok" }));

app.MapMethods("/token", new[] { "GET", "POST" }, () =>
{
    var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(
        jwtKey.Length >= 32 ? jwtKey : jwtKey.PadRight(32, '_')));
    var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

    var claims = new[]
    {
        new Claim(JwtRegisteredClaimNames.Sub, "demo-user"),
        new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString("N"))
    };

    var desc = new SecurityTokenDescriptor
    {
        Subject = new ClaimsIdentity(claims),
        Expires = DateTime.UtcNow.AddHours(1),
        SigningCredentials = creds
    };

    var handler = new JsonWebTokenHandler();
    var jwt = handler.CreateToken(desc);
    return Results.Json(new { token = jwt });
});

// POST /orders (sin auth todavía)
app.MapPost("/orders", [Microsoft.AspNetCore.Authorization.Authorize](CreateOrderRequest req, IModel ch) =>
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
    Serilog.Log.Information("order_received {OrderId} {Amount}", req.OrderId, req.Amount);
    var body = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(envelope));

    var props = ch.CreateBasicProperties();
    props.ContentType = "application/json";
    props.DeliveryMode = 2; // persistente
    props.MessageId = envelope.messageId.ToString();

    ch.BasicPublish("orders.exchange", "orders.created", props, body);
    Serilog.Log.Information("order_published {OrderId}", req.OrderId);
    return Results.Accepted($"/orders/{req.OrderId}", new { status = "queued", req.OrderId });
});

app.Run();
