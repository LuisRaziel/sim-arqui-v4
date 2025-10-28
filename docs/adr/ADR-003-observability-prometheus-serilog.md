# ADR-003: Observabilidad con Prometheus + Serilog

## Estado
✅ **Aceptado**

## Fecha
2025-01-28

## Contexto
Un sistema distribuido (API + Worker + RabbitMQ) requiere observabilidad para:

- Monitorear métricas de rendimiento (throughput, latencia)
- Troubleshooting de errores en producción
- Correlación de logs entre servicios
- Alertas proactivas ante anomalías
- Health checks para orquestadores (Kubernetes)

Se necesita una solución que sea:
- Compatible con .NET 8
- Apta para entornos containerizados
- Estándares de industria (vendor-agnostic)

## Decisión
Implementar una estrategia de observabilidad basada en:

1. **Métricas:** Prometheus (formato exposition)
2. **Logs:** Serilog con formato JSON compacto
3. **Trazabilidad:** CorrelationId propagado end-to-end
4. **Health Checks:** Endpoints `/health`, `/live` (Kubernetes-ready)

## Pilares de Observabilidad

### 1. Métricas (Prometheus)

**Justificación:**
- ✅ Estándar de facto en cloud-native
- ✅ Integración nativa con Kubernetes
- ✅ Ecosistema maduro (Grafana, AlertManager)
- ✅ Modelo pull (no requiere agents)

**Implementación:**
```csharp
// Métricas custom
var ordersPublishedTotal = Metrics.CreateCounter(
    "orders_published_total", 
    "Órdenes publicadas por la API"
);

var ordersProcessedTotal = Metrics.CreateCounter(
    "orders_processed_total", 
    "Órdenes procesadas por el worker"
);

// Endpoint de scraping
app.MapMetrics("/metrics");
```

**Métricas Expuestas:**
- `orders_published_total` - API
- `orders_processed_total` - Worker
- `orders_failed_total` - Worker
- HTTP metrics (latencia, status codes) - API
- Process metrics (CPU, memoria) - Ambos

---

### 2. Logs Estructurados (Serilog JSON)

**Justificación:**
- ✅ Formato JSON facilita parsing por agregadores (ELK, DataDog, Loki)
- ✅ Campos estructurados (no texto libre)
- ✅ Enriquecimiento contextual (CorrelationId)
- ✅ Sinks flexibles (Console, File, Elasticsearch, etc.)

**Formato CompactJsonFormatter:**
```json
{
  "@t": "2025-01-28T17:25:53.1234567Z",
  "@mt": "order_received {OrderId} {Amount}",
  "OrderId": "a1b2c3d4-...",
  "Amount": 123.45,
  "CorrelationId": "7f8e9d0c-...",
  "@l": "Information"
}
```

**Ventajas sobre logs de texto:**
- Búsqueda eficiente por campos estructurados
- Aggregación automática en dashboards
- No requiere regex para parsing

---

### 3. Trazabilidad Distribuida (CorrelationId)

**Problema:**
En sistemas distribuidos, una solicitud atraviesa múltiples servicios. Sin trazabilidad, es imposible correlacionar logs.

**Solución:**
```
Cliente → API (genera CorrelationId) → RabbitMQ → Worker
  |          |                           |         |
  └──────────┴───────── mismo ID ────────┴─────────┘
```

**Implementación:**
```csharp
// API: Middleware
public class CorrelationIdMiddleware
{
    public async Task Invoke(HttpContext context)
    {
        var cid = context.Request.Headers["X-Correlation-Id"].FirstOrDefault()
                  ?? Guid.NewGuid().ToString("N");
        
        context.Items["CorrelationId"] = cid;
        context.Response.Headers["X-Correlation-Id"] = cid;
        
        using (LogContext.PushProperty("CorrelationId", cid))
        {
            await _next(context);
        }
    }
}

// RabbitMQ: Headers
props.Headers["X-Correlation-Id"] = correlationId;

// Worker: Extracción
var correlationId = ea.BasicProperties?.Headers?["X-Correlation-Id"];
using (LogContext.PushProperty("CorrelationId", correlationId))
{
    // Procesar mensaje
}
```

**Beneficio:**
```bash
# Buscar todos los logs de una transacción específica
kubectl logs -l app=api | jq 'select(.CorrelationId == "7f8e9d0c")'
kubectl logs -l app=worker | jq 'select(.CorrelationId == "7f8e9d0c")'
```

---

### 4. Health Checks

**Liveness:** ¿El proceso está vivo?
```csharp
app.MapGet("/live", () => Results.Ok(new { status = "alive" }));
```

**Readiness:** ¿El servicio puede procesar tráfico?
```csharp
app.MapGet("/health", () =>
{
    // Verifica conectividad a RabbitMQ
    using var conn = factory.CreateConnection();
    return Results.Ok(new { status = "ready", broker = "connected" });
});
```

**Uso en Kubernetes:**
```yaml
livenessProbe:
  httpGet:
    path: /live
    port: 8081
  initialDelaySeconds: 10
  periodSeconds: 5

readinessProbe:
  httpGet:
    path: /health
    port: 8081
  initialDelaySeconds: 5
  periodSeconds: 10
```

## Alternativas Consideradas

### 1. Application Insights (Azure)
**Pros:** Integración nativa, APM completo
**Contras:** ❌ Vendor lock-in, ❌ Costos
**Veredicto:** Rechazado por dependencia de Azure

### 2. OpenTelemetry
**Pros:** Estándar vendor-agnostic, traces + metrics + logs
**Contras:** ⚠️ Mayor complejidad de configuración
**Veredicto:** Candidato para v2.0 (cuando se requieran distributed traces)

### 3. ELK Stack (Elasticsearch + Logstash + Kibana)
**Pros:** Suite completa para logs
**Contras:** ❌ Alto consumo de recursos, ❌ Complejidad operacional
**Veredicto:** Prometheus + Serilog + Loki es más ligero

## Consecuencias

### Positivas ✅
- Stack vendor-agnostic (portabilidad multi-cloud)
- Bajo overhead de recursos
- Troubleshooting rápido con CorrelationId
- Integración nativa con Kubernetes
- Formato estándar (Prometheus exposition format)

### Negativas ⚠️
- No incluye distributed tracing (spans)
- Requiere Grafana por separado para visualización
- Métricas almacenadas short-term (Prometheus no es TSDB largo plazo)

## Roadmap de Observabilidad

### Fase 1: Actual ✅
```
API/Worker → Prometheus /metrics
API/Worker → Serilog → stdout (JSON)
```

### Fase 2: Agregación
```
Prometheus → Grafana (dashboards)
Logs → Loki/ELK → Grafana
```

### Fase 3: APM Completo (v2.0)
```
OpenTelemetry → Jaeger (traces) + Prometheus + Loki
```

## Configuración

### Serilog
```csharp
Log.Logger = new LoggerConfiguration()
    .Enrich.FromLogContext()
    .WriteTo.Console(new Serilog.Formatting.Compact.CompactJsonFormatter())
    .CreateLogger();
```

### Prometheus Scrape Config
```yaml
scrape_configs:
  - job_name: 'sim-arqui-api'
    static_configs:
      - targets: ['api:8080']
  - job_name: 'sim-arqui-worker'
    static_configs:
      - targets: ['worker:8081']
```

## Métricas Clave a Monitorear

| Métrica | Umbral Alerta | Acción |
|---------|---------------|--------|
| `orders_published_total` (rate) | < 10/min durante 5min | Revisar API health |
| `orders_failed_total` (rate) | > 10% del total | Revisar logs DLQ |
| HTTP 5xx rate | > 1% | Escalar API pods |
| RabbitMQ queue depth | > 1000 msgs | Escalar Workers |

## Referencias
- [Prometheus Best Practices](https://prometheus.io/docs/practices/naming/)
- [Serilog Structured Logging](https://github.com/serilog/serilog/wiki/Structured-Data)
- [Kubernetes Health Checks](https://kubernetes.io/docs/tasks/configure-pod-container/configure-liveness-readiness-startup-probes/)
- [The Three Pillars of Observability](https://www.oreilly.com/library/view/distributed-systems-observability/9781492033431/ch04.html)

## Revisión
- **Fecha de Revisión:** 2025-07-28 (6 meses)
- **Criterio de Revisión:** Evaluar migración a OpenTelemetry
- **Responsable:** Arquitecto de Solución + SRE

---
**Última actualización:** 2025-01-28  
**Autor:** @LuisRaziel