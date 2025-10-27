using Serilog.Context;

namespace Api.Middleware;

public class CorrelationIdMiddleware
{
    private const string HeaderName = "X-Correlation-Id";
    private readonly RequestDelegate _next;

    public CorrelationIdMiddleware(RequestDelegate next) => _next = next;

    public async Task Invoke(HttpContext context)
    {
        var cid = context.Request.Headers[HeaderName].FirstOrDefault();
        if (string.IsNullOrWhiteSpace(cid))
            cid = Guid.NewGuid().ToString("N");

        // Guarda para los handlers y agrega a la respuesta
        context.Items["CorrelationId"] = cid;
        context.Response.Headers[HeaderName] = cid;

        // Enriquecer logs
        using (LogContext.PushProperty("CorrelationId", cid))
        {
            await _next(context);
        }
    }
}