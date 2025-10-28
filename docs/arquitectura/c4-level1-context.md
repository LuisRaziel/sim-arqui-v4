# C4 Model - Nivel 1: Contexto del Sistema

## Propósito
Mostrar el sistema **sim-arqui-v4** en su entorno, identificando usuarios y sistemas externos con los que interactúa.

## Audiencia
- Stakeholders de negocio
- Product Managers
- Ejecutivos técnicos
- Nuevos miembros del equipo

---

## Diagrama de Contexto

```
                    ┌─────────────────┐
                    │     Cliente     │
                    │   (Persona)     │
                    │                 │
                    │  Envía órdenes  │
                    │  de compra vía  │
                    │   API REST      │
                    └────────┬────────┘
                             │
                             │ HTTPS/HTTP
                             │ POST /orders
                             │ GET /token
                             │
                    ┌────────▼────────────────────────┐
                    │                                 │
                    │   Sistema sim-arqui-v4         │
                    │                                 │
                    │  Procesa órdenes de forma      │
                    │  asíncrona con arquitectura    │
                    │  distribuida (API + Worker)    │
                    │                                 │
                    │  • Autenticación JWT           │
                    │  • Rate limiting               │
                    │  • Métricas Prometheus         │
                    │  • Logs estructurados          │
                    │                                 │
                    └────────┬────────────────────────┘
                             │
                             │ AMQP Protocol
                             │ (Mensajes de órdenes)
                             │
                    ┌────────▼────────┐
                    │   RabbitMQ      │
                    │  (Sistema       │
                    │   Externo)      │
                    │                 │
                    │  Message Broker │
                    └─────────────────┘
```

---

## Elementos del Diagrama

### 👤 **Cliente (Usuario/Sistema)**
- **Tipo:** Persona o Sistema Cliente
- **Descripción:** Usuario o aplicación que consume la API para enviar órdenes de compra
- **Responsabilidades:**
  - Autenticarse obteniendo token JWT
  - Enviar órdenes con `orderId` y `amount`
  - Recibir confirmación de recepción (HTTP 202 Accepted)

**Interacciones:**
```bash
# 1. Obtener token
curl -X POST http://localhost:8080/token

# 2. Enviar orden
curl -X POST http://localhost:8080/orders \
  -H "Authorization: Bearer <token>" \
  -H "Content-Type: application/json" \
  -d '{"orderId":"uuid","amount":123.45}'
```

---

### 🏗️ **Sistema sim-arqui-v4**
- **Tipo:** Sistema de Software
- **Descripción:** Sistema distribuido que recibe órdenes vía API REST y las procesa asíncronamente mediante workers
- **Tecnologías Principales:**
  - .NET 8
  - Docker
  - Prometheus
  - Serilog

**Capacidades Clave:**
- ✅ Recepción de órdenes vía REST API
- ✅ Procesamiento asíncrono desacoplado
- ✅ Autenticación y autorización
- ✅ Rate limiting (50 req/min por defecto)
- ✅ Observabilidad (métricas + logs + health checks)
- ✅ Resiliencia (retry + Dead Letter Queue)

**Endpoints Públicos:**
- `POST /orders` - Crear orden (requiere JWT)
- `POST /token` - Obtener token JWT
- `GET /health` - Health check
- `GET /metrics` - Métricas Prometheus

---

### 📬 **RabbitMQ (Sistema Externo)**
- **Tipo:** Sistema de Mensajería Externo
- **Descripción:** Message broker AMQP que desacopla la API del procesamiento
- **Responsabilidades:**
  - Almacenar mensajes de órdenes
  - Garantizar entrega (durable queues)
  - Gestionar reintentos y DLQ

**Topología:**
```
orders.exchange (topic)
    ↓
orders.queue → [procesamiento] → ACK
    ↓ (fallo)
orders.dlx (fanout)
    ↓
orders.dlq (manual review)
```

---

## Flujo de Datos Principal

### Flujo Exitoso (Happy Path)

```
1. Cliente solicita token
   Cliente → API: POST /token
   API → Cliente: { "token": "eyJ..." }

2. Cliente envía orden
   Cliente → API: POST /orders + JWT
   API → RabbitMQ: PublishMessage(order)
   API → Cliente: HTTP 202 Accepted

3. Procesamiento asíncrono
   RabbitMQ → Worker: DeliverMessage(order)
   Worker: ProcessOrder()
   Worker → RabbitMQ: ACK
```

### Flujo con Fallo y Retry

```
1. Mensaje falla en Worker
   Worker: ProcessOrder() → Exception
   Worker → RabbitMQ: NACK + requeue

2. RabbitMQ reintenta (max 3 veces)
   RabbitMQ → Worker: RedeliverMessage(order)

3. Si falla después de 3 reintentos
   RabbitMQ → orders.dlx
   orders.dlx → orders.dlq (revisión manual)
```

---

## Características Arquitectónicas

| Característica | Implementación | Beneficio |
|----------------|----------------|-----------|
| **Escalabilidad** | Arquitectura desacoplada | Workers pueden escalar independientemente |
| **Resiliencia** | DLQ + retry + health checks | Sistema tolera fallos transitorios |
| **Observabilidad** | Prometheus + Serilog + CorrelationId | Troubleshooting rápido |
| **Seguridad** | JWT + Rate limiting | Previene accesos no autorizados y abusos |
| **Mantenibilidad** | Logs estructurados + documentación | Facilita debugging y onboarding |

---

## Decisiones Arquitectónicas Relacionadas

- [ADR-001: Selección de RabbitMQ](../adr/ADR-001-rabbitmq-selection.md) - Por qué RabbitMQ vs Kafka
- [ADR-002: JWT Simplificado](../adr/ADR-002-jwt-authentication.md) - Estrategia de autenticación
- [ADR-003: Observabilidad](../adr/ADR-003-observability-prometheus-serilog.md) - Stack de monitoreo

---

## Limitaciones del Sistema (Scope)

### ✅ Dentro del Scope
- Recepción de órdenes
- Validación básica (JWT, rate limiting)
- Procesamiento asíncrono
- Monitoreo y observabilidad

### ❌ Fuera del Scope (por ahora)
- Persistencia de órdenes (no hay base de datos)
- Notificaciones a clientes sobre estado de orden
- Integración con sistemas de pago
- API de consulta de órdenes históricas
- Multi-tenancy

---

## Volumetría y SLA Esperados

| Métrica | Valor Objetivo | Notas |
|---------|----------------|-------|
| Throughput | < 10,000 órdenes/día | RabbitMQ puede manejar mucho más |
| Latencia API | < 200ms (p95) | Solo publica a RabbitMQ |
| Disponibilidad | > 99% | Sin SLA formal en MVP |
| Procesamiento Worker | < 5s por orden | Depende de lógica de negocio |

---

## Próximos Pasos (Roadmap)

### v1.1 - Mejoras Operacionales
- Dashboard Grafana con métricas clave
- Alertas en AlertManager
- Clustering RabbitMQ para HA

### v2.0 - Features de Negocio
- Base de datos para persistencia de órdenes
- API de consulta (`GET /orders/{id}`)
- Webhooks para notificar clientes
- Migración a IdP externo (Auth0/Keycloak)

---

## Referencias
- [C4 Model - Context Diagram](https://c4model.com/#SystemContextDiagram)
- [Repositorio del proyecto](https://github.com/LuisRaziel/sim-arqui-v4)
- [README del proyecto](../../README.md)

---
**Última actualización:** 2025-10-28  
**Autor:** @LuisRaziel  
**Versión del diagrama:** 1.0