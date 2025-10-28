# ADR-001: Selección de RabbitMQ como Message Broker

## Estado
✅ **Aceptado**

## Fecha
2025-01-28

## Contexto
El sistema sim-arqui-v4 requiere comunicación asíncrona entre la API y el Worker para procesar órdenes de manera desacoplada. Se necesita un message broker que permita:

- Comunicación asíncrona confiable
- Manejo de mensajes fallidos (Dead Letter Queue)
- Soporte para retry policies
- Configuración simple para entornos de desarrollo y pruebas
- Bajo overhead operacional para un sistema de escala media

## Decisión
Seleccionamos **RabbitMQ** como message broker para la comunicación entre API y Worker.

## Alternativas Consideradas

### 1. Apache Kafka
**Pros:**
- Alta throughput (millones de mensajes/seg)
- Excelente para event streaming
- Retención de mensajes prolongada
- Ideal para arquitecturas event-sourcing

**Contras:**
- ❌ Complejidad operacional alta (requiere ZooKeeper/KRaft)
- ❌ Overkill para el volumen esperado (<10k msg/día)
- ❌ Curva de aprendizaje más pronunciada
- ❌ Recursos computacionales mayores (mínimo 3 brokers en producción)
- ❌ No tiene concepto nativo de DLQ (requiere implementación custom)

**Veredicto:** Rechazado por complejidad vs beneficio

---

### 2. Azure Service Bus
**Pros:**
- Totalmente administrado (PaaS)
- Integración nativa con Azure
- DLQ nativo
- Bajo overhead operacional

**Contras:**
- ❌ Vendor lock-in con Azure
- ❌ Costos recurrentes por mensajes procesados
- ❌ Dependencia de conectividad a Azure
- ❌ Menor flexibilidad para entornos locales/on-premise

**Veredicto:** Rechazado por dependencia de cloud provider

---

### 3. AWS SQS
**Pros:**
- Totalmente administrado
- Alta disponibilidad
- Modelo de pago por uso

**Contras:**
- ❌ Vendor lock-in con AWS
- ❌ Sin soporte nativo para routing patterns complejos
- ❌ Latencia mayor que brokers auto-hospedados
- ❌ Limitaciones en orden de mensajes (requiere FIFO queues con limitaciones)

**Veredicto:** Rechazado por vendor lock-in

---

### 4. RabbitMQ ✅ (Seleccionado)
**Pros:**
- ✅ Complejidad operacional baja-media
- ✅ Excelente soporte para .NET (RabbitMQ.Client)
- ✅ DLQ nativo con configuración simple
- ✅ Patrones de routing flexibles (exchange types: direct, topic, fanout)
- ✅ UI de administración incluida (Management Plugin)
- ✅ Fácil de dockerizar para desarrollo local
- ✅ Auto-recovery de conexiones
- ✅ Confirmaciones de publicación (publisher confirms)
- ✅ Ideal para throughput medio (<100k msg/día)

**Contras:**
- ⚠️ Menor throughput que Kafka (pero suficiente para nuestro caso)
- ⚠️ No diseñado para event streaming largo plazo
- ⚠️ Requiere gestión de clustering para HA (pero no requerido en MVP)

**Veredicto:** ✅ Seleccionado

## Justificación de la Decisión

### Análisis Cuantitativo

| Criterio | Peso | Kafka | Azure SB | AWS SQS | RabbitMQ |
|----------|------|-------|----------|---------|----------|
| Complejidad Operacional | 30% | 2/10 | 9/10 | 9/10 | **8/10** |
| Costo | 20% | 7/10 | 5/10 | 6/10 | **9/10** |
| Flexibilidad Deployment | 20% | 8/10 | 4/10 | 4/10 | **10/10** |
| Features Requeridos | 15% | 10/10 | 8/10 | 6/10 | **9/10** |
| Ecosistema .NET | 15% | 7/10 | 9/10 | 7/10 | **10/10** |
| **TOTAL PONDERADO** | | **6.3** | **6.9** | **6.5** | **✅ 9.0** |

### Razones Principales:

1. **Complejidad vs Valor:** RabbitMQ ofrece el mejor balance entre features y complejidad operacional
2. **Portabilidad:** Funciona igual en desarrollo local, on-premise y cualquier cloud
3. **Ecosistema .NET:** Librería oficial madura y ampliamente adoptada
4. **DLQ Pattern:** Implementación nativa y simple del patrón Dead Letter Queue
5. **Time-to-Market:** Permite desarrollo y deployment rápido sin configuraciones complejas

## Consecuencias

### Positivas ✅
- Desarrollo rápido con Docker Compose para entornos locales
- Bajo costo de infraestructura (self-hosted)
- Fácil troubleshooting con Management UI
- Comunidad activa y documentación extensa
- Patterns bien establecidos para .NET

### Negativas ⚠️
- Si el throughput crece >100k msg/día, podría requerir migración a Kafka
- Clustering para HA requiere configuración adicional (no crítico en MVP)
- No apto para event sourcing con retención prolongada (>7 días)

### Riesgos y Mitigaciones

| Riesgo | Probabilidad | Impacto | Mitigación |
|--------|--------------|---------|------------|
| Throughput insuficiente | Baja | Alto | Monitorear métricas; plan de migración a Kafka si se exceden 50k msg/día |
| Single point of failure | Media | Alto | Implementar clustering en fase de producción |
| Pérdida de mensajes | Baja | Crítico | Publisher confirms + mensaje durable + DLQ |

## Notas de Implementación

### Configuración Actual
```csharp
// api/src/Program.cs
var factory = new ConnectionFactory
{
    HostName = host,
    UserName = user,
    Password = pass,
    AutomaticRecoveryEnabled = true  // ✅ Reconexión automática
};
```

### Topología de Mensajería
```
orders.exchange (topic) 
    ↓ [routing: orders.created]
orders.queue
    ↓ [si falla después de 3 reintentos]
orders.dlx (fanout)
    ↓
orders.dlq
```

## Referencias
- [RabbitMQ Documentation](https://www.rabbitmq.com/documentation.html)
- [RabbitMQ .NET Client Guide](https://www.rabbitmq.com/dotnet-api-guide.html)
- [Reliability Guide](https://www.rabbitmq.com/reliability.html)
- [Issue de implementación](https://github.com/LuisRaziel/sim-arqui-v4/issues/1)

## Revisión
- **Fecha de Revisión:** 2025-07-28 (6 meses)
- **Criterio de Revisión:** Si el throughput supera 50k msg/día o se requiere event sourcing
- **Responsable:** Arquitecto de Solución

---
**Última actualización:** 2025-01-28  
**Autor:** @LuisRaziel  
**Revisores:** [Pendiente]