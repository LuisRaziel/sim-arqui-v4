# Architecture Decision Records (ADRs)

Este directorio contiene los registros de decisiones arquitectónicas (ADRs) del proyecto **sim-arqui-v4**.

## ¿Qué es un ADR?

Un ADR (Architecture Decision Record) documenta una decisión arquitectónica significativa, incluyendo:
- **Contexto:** Por qué se necesita tomar una decisión
- **Alternativas consideradas:** Qué opciones se evaluaron
- **Decisión:** Qué se eligió y por qué
- **Consecuencias:** Impactos positivos y negativos

## Índice de ADRs

| # | Título | Estado | Fecha |
|---|--------|--------|-------|
| [ADR-001](./ADR-001-rabbitmq-selection.md) | Selección de RabbitMQ como Message Broker | ✅ Aceptado | 2025-01-28 |
| [ADR-002](./ADR-002-jwt-authentication.md) | Autenticación JWT Simplificada | ✅ Aceptado (con limitaciones) | 2025-01-28 |
| [ADR-003](./ADR-003-observability-prometheus-serilog.md) | Observabilidad con Prometheus + Serilog | ✅ Aceptado | 2025-01-28 |

## Estados Posibles

- ✅ **Aceptado:** Decisión implementada y en uso
- 🚧 **Propuesto:** En discusión
- ⚠️ **Deprecated:** Reemplazada por otra decisión
- ❌ **Rechazado:** No se implementó

## Formato

Usamos el formato [MADR](https://adr.github.io/madr/) (Markdown Architectural Decision Records) simplificado con las siguientes secciones:

```markdown
# ADR-XXX: Título

## Estado
[Aceptado/Propuesto/Deprecated/Rechazado]

## Fecha
YYYY-MM-DD

## Contexto
[Por qué necesitamos decidir]

## Decisión
[Qué decidimos]

## Alternativas Consideradas
[Qué más evaluamos y por qué no lo elegimos]

## Consecuencias
[Impactos positivos y negativos]

## Referencias
[Links relevantes]
```

## Contribuir

Para proponer un nuevo ADR:

1. Copia la plantilla desde `ADR-template.md`
2. Numera secuencialmente (ADR-004, ADR-005, etc.)
3. Completa todas las secciones
4. Crea un PR para revisión

## Referencias

- [ADR GitHub Organization](https://adr.github.io/)
- [Documenting Architecture Decisions](https://cognitect.com/blog/2011/11/15/documenting-architecture-decisions)
- [MADR Template](https://github.com/adr/madr)

---
**Proyecto:** sim-arqui-v4  
**Mantenedor:** @LuisRaziel