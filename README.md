# 🧠 Sim-Arqui V4 — Arquitectura base (API + Worker + RabbitMQ)

Proyecto base funcional para practicar arquitectura distribuida en .NET 8 con API + Worker, comunicación asíncrona vía RabbitMQ, métricas Prometheus, health checks y seguridad JWT.

---

## 🚀 **Componentes**

| Servicio | Puerto | Descripción |
|-----------|--------|-------------|
| **API** | 8080 | Expone endpoints REST (`/orders`, `/token`, `/health`, `/metrics`) |
| **Worker** | 8081 | Procesa mensajes asíncronos de RabbitMQ (`orders.exchange`) |
| **RabbitMQ** | 5672 / 15672 | Broker AMQP (cola principal + DLQ) |

---

## 🧩 **Arquitectura**
[Client] → [API → RabbitMQ] → [Worker]
↘ logs (Serilog JSON + CorrelationId)
↘ métricas (Prometheus)
↘ health checks (K8s-ready)

- **CorrelationId e2e:** se propaga desde la API hasta el Worker.  
- **JWT mínimo viable:** `/token` genera un token local firmado (HMAC SHA256).  
- **Rate limiting:** configurable por entorno con `RATELIMIT__PERMIT_LIMIT` y `RATELIMIT__WINDOW_SECONDS`.  
- **Health y métricas:** ambos servicios exponen endpoints listos para monitoreo.  

---

## ⚙️ **Configuración por entorno**

Variables relevantes en `docker-compose.yaml`:

```yaml
environment:
  RABBITMQ__HOST: rabbitmq
  RABBITMQ__USER: guest
  RABBITMQ__PASS: guest
  JWT__KEY: dev-local-change-me
  RATELIMIT__PERMIT_LIMIT: "50"
  RATELIMIT__WINDOW_SECONDS: "60"

🧪 Ejecución rápida

1️⃣ Levantar todo
docker compose up -d --build

2️⃣ Verificar salud
curl -sf http://localhost:8080/health && echo "API OK"
curl -sf http://localhost:8081/live && echo "Worker vivo"

3️⃣ Obtener token JWT
TOKEN=$(curl -s http://localhost:8080/token | sed -n 's/.*"token"[[:space:]]*:[[:space:]]*"$begin:math:text$[^"]*$end:math:text$".*/\1/p')
echo $TOKEN

4️⃣ Enviar orden
OID=$(uuidgen 2>/dev/null || echo 11111111-1111-1111-1111-111111111111)
curl -i -X POST http://localhost:8080/orders \
  -H "Content-Type: application/json" \
  -H "Authorization: Bearer $TOKEN" \
  -d "{\"orderId\":\"$OID\",\"amount\":123.45}"

  docker compose logs worker --tail=50

6️⃣ Métricas
	•	API → http://localhost:8080/metrics
	•	Worker → http://localhost:8081/metrics

7️⃣ Health checks
	•	Worker liveness: http://localhost:8081/live
	•	Worker readiness: http://localhost:8081/health

🧱 Stack técnico
	•	.NET 8
	•	ASP.NET Minimal API
	•	RabbitMQ
	•	Serilog (Compact JSON)
	•	Prometheus + HealthChecks
	•	Docker Compose

Próximos pasos (v1.1+)
	•	Agregar dashboards de Grafana.
	•	Unificar métricas API+Worker en un solo scrape job.
	•	Endpoint /metrics consolidado en gateway opcional.