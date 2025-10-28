# C4 Model - Nivel 2: Contenedores

## Propósito
Mostrar los contenedores (aplicaciones, servicios, data stores) que componen el sistema **sim-arqui-v4** y cómo se comunican entre sí.

## Audiencia
- Arquitectos de Software
- Tech Leads
- Ingenieros DevOps/SRE
- Desarrolladores senior

---

## Diagrama de Contenedores

```
┌─────────────────────────────────────────────────────────────────┐
│                         Cliente (Browser/App)                    │
└────────────────┬────────────────────────────────────────────────┘
                 │
                 │ HTTP/HTTPS
                 │ POST /orders (JWT)
                 │ GET /token
                 │
┌────────────────▼──────────────────────────────────────────────────┐
│                           API Container                           │
│  ┌─────────────────────────────────────────────────────────┐     │
│  │  ASP.NET Core Minimal API (.NET 8)                      │     │
│  │  • Puerto: 8080                                         │     │
│  │  • Endpoints: /orders, /token, /health, /metrics       │     │
│  │  • Auth: JWT Bearer                                     │     │
│  │  • Rate Limiting: 50 req/min                           │     │
│  │  • Middleware: CorrelationId, Logging                  │     │
│  └─────────────────────────────────────────────────────────┘     │
└────────────────┬──────────────────────────────────────────────────┘
                 │
                 │ AMQP (RabbitMQ.Client)
                 │ PublishMessage(order)
                 │ Exchange: orders.exchange
                 │ RoutingKey: orders.created
                 │
┌────────────────▼──────────────────────────────────────────────────┐
│                      RabbitMQ Container                           │
│  ┌─────────────────────────────────────────────────────────┐     │
│  │  RabbitMQ 3.x (Message Broker)                          │     │
│  │  • Puerto AMQP: 5672                                    │     │
│  │  • Puerto Management: 15672                             │     │
│  │  • Exchanges: orders.exchange, orders.dlx              │     │
│  │  • Queues: orders.queue, orders.dlq                    │     │
│  │  • Features: DLQ, durable messages, auto-recovery      │     │
│  └─────────────────────────────────────────────────────────┘     │
└────────────────┬──────────────────────────────────────────────────┘
                 │
                 │ AMQP (AsyncEventingBasicConsumer)
                 │ ConsumeMessage(order)
                 │ Queue: orders.queue
                 │ Prefetch: 10 messages
                 │
┌────────────────▼──────────────────────────────────────────────────┐
│                         Worker Container                          │
│  ┌─────────────────────────────────────────────────────────┐     │
│  │  .NET 8 Background Service                              │     │
│  │  • Puerto (health): 8081                                │     │
│  │  • Procesa mensajes de RabbitMQ                         │     │
│  │  • Retry: 3 intentos máximo                             │     │
│  │  • Idempotencia: MemoryCache (TTL 5min)                │     │
│  │  • Endpoints: /live, /health, /metrics                 │     │
│  └─────────────────────────────────────────────────────────┘     │
└───────────────────────────────────────────────────────────────────┘

┌───────────────────────────────────────────────────────────────────┐
│                    Prometheus (Externo)                           │
│  Scrapes metrics from:                                            │
│  • http://api:8080/metrics                                        │
│  • http://worker:8081/metrics                                     │
└───────────────────────────────────────────────────────────────────┘
```

---

## Descripción de Contenedores

### 📦 **API Container**

**Tecnología:** ASP.NET Core 8 (Minimal APIs)  
**Responsabilidad:** Frontend del sistema, expone API REST para clientes  
**Lenguaje:** C# (.NET 8)  
**Puerto:** 8080  

#### Dependencias:
```xml
<PackageReference Include="RabbitMQ.Client" Version="6.x" />
<PackageReference Include="Microsoft.AspNetCore.Authentication.JwtBearer" />
<PackageReference Include="Serilog.AspNetCore" />
<PackageReference Include="prometheus-net.AspNetCore" />
```

#### Endpoints:

| Método | Ruta | Autenticación | Descripción |
|--------|------|---------------|-------------|
| POST | `/orders` | ✅ JWT | Recibe orden y publica a RabbitMQ |
| POST/GET | `/token` | ❌ | Genera token JWT |
| GET | `/health` | ❌ | Health check básico |
| GET | `/metrics` | ❌ | Métricas Prometheus |
| GET | `/swagger` | ❌ | Documentación OpenAPI |

#### Variables de Entorno:
```yaml
RABBITMQ__HOST: rabbitmq
RABBITMQ__USER: guest
RABBITMQ__PASS: guest
JWT__KEY: "dev-local-change-me"
RATELIMIT__PERMIT_LIMIT: "50"
RATELIMIT__WINDOW_SECONDS: "60"
ASPNETCORE_URLS: "http://+:8080"
```

#### Características:
- ✅ **Stateless**: No mantiene estado entre requests
- ✅ **Rate Limiting**: Fixed window (50 req/min)
- ✅ **CorrelationId**: Middleware que propaga ID end-to-end
- ✅ **Metrics**: Contador `orders_published_total`
- ✅ **Auto-recovery**: Conexión RabbitMQ con reconexión automática

---

### 📦 **Worker Container**

**Tecnología:** .NET 8 Background Service  
**Responsabilidad:** Consumir y procesar mensajes de RabbitMQ  
**Lenguaje:** C# (.NET 8)  
**Puerto:** 8081 (solo health endpoints)  

#### Dependencias:
```xml
<PackageReference Include="RabbitMQ.Client" Version="6.x" />
<PackageReference Include="Serilog.Sinks.Console" />
<PackageReference Include="prometheus-net" />
<PackageReference Include="Microsoft.Extensions.Caching.Memory" />
```

#### Endpoints:

| Método | Ruta | Descripción |
|--------|------|-------------|
| GET | `/live` | Liveness probe (proceso vivo) |
| GET | `/health` | Readiness probe (conectado a RabbitMQ) |
| GET | `/metrics` | Métricas Prometheus |

#### Variables de Entorno:
```yaml
RABBITMQ__HOST: rabbitmq
RABBITMQ__USER: guest
RABBITMQ__PASS: guest
WORKER__PREFETCH: "10"
WORKER__RETRYCOUNT: "3"
```

#### Características:
- ✅ **Prefetch**: Procesa hasta 10 mensajes concurrentemente
- ✅ **Retry Logic**: 3 intentos antes de enviar a DLQ
- ✅ **Idempotencia**: MemoryCache con TTL de 5 minutos
- ✅ **Metrics**: 
  - `orders_processed_total`
  - `orders_failed_total`
- ✅ **Graceful Shutdown**: Espera a completar mensajes en proceso

#### Flujo de Procesamiento:
```csharp
1. Recibe mensaje de orders.queue
2. Extrae CorrelationId (payload → props → headers)
3. Verifica idempotencia (MemoryCache)
   ├─ Si ya procesado: ACK y termina
   └─ Si nuevo: continúa
4. Valida JSON (orderId, amount)
5. Procesa orden (lógica de negocio)
6. En éxito: ACK
7. En error: NACK + requeue (max 3 veces)
8. Después de 3 fallos: mensaje va a DLQ
```

---

### 📦 **RabbitMQ Container**

**Tecnología:** RabbitMQ 3.x (Management)  
**Responsabilidad:** Message broker para comunicación asíncrona  
**Imagen Docker:** `rabbitmq:3-management`  
**Puertos:**
- 5672 (AMQP)
- 15672 (Management UI)

#### Topología de Mensajería:

```
┌─────────────────────┐
│  orders.exchange    │  (type: topic)
│  (durable: true)    │
└──────────┬──────────┘
           │ binding: "orders.created"
           │
┌──────────▼──────────┐
│   orders.queue      │  (durable: true)
│   (x-dead-letter-   │
│    exchange: dlx)   │
└──────────┬──────────┘
           │
           ├─ ACK (éxito) → mensaje eliminado
           │
           └─ NACK después de 3 reintentos
                      ↓
           ┌──────────▼──────────┐
           │    orders.dlx       │  (type: fanout)
           └──────────┬──────────┘
                      │
           ┌──────────▼──────────┐
           │    orders.dlq       │  (revisión manual)
           └─────────────────────┘
```

#### Health Check:
```yaml
healthcheck:
  test: ["CMD-SHELL", "rabbitmq-diagnostics -q ping"]
  interval: 10s
  timeout: 5s
  retries: 5
```

---

## Comunicación entre Contenedores

### 1️⃣ **API → RabbitMQ**
- **Protocolo:** AMQP 0-9-1
- **Librería:** RabbitMQ.Client 6.x
- **Patrón:** Publisher con confirmaciones
- **Propiedades del mensaje:**
  ```csharp
  props.ContentType = "application/json";
  props.DeliveryMode = 2;  // Persistent
  props.MessageId = Guid.NewGuid().ToString();
  props.CorrelationId = correlationId;
  props.Headers["X-Correlation-Id"] = correlationId;
  ```

### 2️⃣ **RabbitMQ → Worker**
- **Protocolo:** AMQP 0-9-1
- **Patrón:** Async Consumer
- **Configuración:**
  ```csharp
  ch.BasicQos(0, prefetch: 10, global: false);
  var consumer = new AsyncEventingBasicConsumer(ch);
  ch.BasicConsume(queue, autoAck: false, consumer);
  ```

---

## Deployment (Docker Compose)

```yaml
services:
  rabbitmq:
    image: rabbitmq:3-management
    ports: 
      - "5672:5672"
      - "15672:15672"
    healthcheck:
      test: ["CMD-SHELL", "rabbitmq-diagnostics -q ping"]
      interval: 10s

  api:
    build: ./api
    ports: 
      - "8080:8080"
    depends_on:
      rabbitmq: 
        condition: service_healthy
    environment:
      RABBITMQ__HOST: rabbitmq
      JWT__KEY: "dev-local-change-me"

  worker:
    build: ./worker
    ports: 
      - "8081:8081"
    depends_on:
      rabbitmq: 
        condition: service_healthy
    environment:
      RABBITMQ__HOST: rabbitmq
      WORKER__PREFETCH: "10"
```

---

## Escalado

### Escalado Horizontal

```yaml
# API: Stateless, puede escalar libremente
docker-compose up --scale api=3

# Worker: Puede escalar, RabbitMQ distribuye mensajes
docker-compose up --scale worker=5
```

### Consideraciones:
- **API**: Load balancer externo requerido (nginx, Traefik)
- **Worker**: RabbitMQ distribuye mensajes round-robin
- **RabbitMQ**: Requiere clustering para HA (no implementado en MVP)

---

## Observabilidad

### Métricas Expuestas

**API:**
```
http://localhost:8080/metrics
```
- `orders_published_total` - Órdenes publicadas
- `http_request_duration_seconds` - Latencia HTTP
- `http_requests_in_progress` - Requests activos

**Worker:**
```
http://localhost:8081/metrics
```
- `orders_processed_total` - Órdenes procesadas exitosamente
- `orders_failed_total` - Órdenes enviadas a DLQ
- `process_cpu_seconds_total` - Uso de CPU

### Logs Estructurados

Ambos contenedores emiten logs en formato JSON compacto:
```json
{
  "@t": "2025-10-28T19:32:57.1234567Z",
  "@mt": "order_received {OrderId} {Amount}",
  "OrderId": "a1b2c3d4-5678-...",
  "Amount": 123.45,
  "CorrelationId": "7f8e9d0c-...",
  "@l": "Information"
}
```

---

## Decisiones Arquitectónicas

- [ADR-001: RabbitMQ Selection](../adr/ADR-001-rabbitmq-selection.md)
- [ADR-002: JWT Authentication](../adr/ADR-002-jwt-authentication.md)
- [ADR-003: Observability Stack](../adr/ADR-003-observability-prometheus-serilog.md)

---

## Referencias
- [C4 Model - Container Diagram](https://c4model.com/#ContainerDiagram)
- [Docker Compose File](../../docker-compose.yml)
- [API Dockerfile](../../api/Dockerfile)
- [Worker Dockerfile](../../worker/Dockerfile)

---
**Última actualización:** 2025-10-28  
**Autor:** @LuisRaziel  
**Versión del diagrama:** 1.0