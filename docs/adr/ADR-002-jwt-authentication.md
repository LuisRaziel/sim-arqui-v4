# ADR-002: Autenticación JWT Simplificada

## Estado
✅ **Aceptado** (con limitaciones documentadas)

## Fecha
2025-01-28

## Contexto
La API expone endpoints que requieren autenticación para prevenir acceso no autorizado. Se necesita un mecanismo de autenticación que:

- Sea simple de implementar para el MVP
- Permita testing local sin dependencias externas
- Sea evolutivo hacia un sistema de identidad robusto
- Funcione sin bases de datos inicialmente

## Decisión
Implementar **JWT (JSON Web Token) con firma HMAC-SHA256** generado localmente por la API, sin validación contra un sistema de identidad externo.

⚠️ **NOTA IMPORTANTE:** Esta es una solución temporal para MVP. NO es apta para producción sin evolucionar hacia un IdP (Identity Provider) externo.

## Alternativas Consideradas

### 1. OAuth 2.0 + OpenID Connect (Auth0, Azure AD, Keycloak)
**Pros:**
- ✅ Estándar de industria
- ✅ Multi-tenancy
- ✅ Refresh tokens
- ✅ Scopes granulares

**Contras:**
- ❌ Requiere infraestructura adicional (IdP)
- ❌ Complejidad de configuración para MVP
- ❌ Dependencia de servicio externo

**Veredicto:** Rechazado para MVP, candidato para v2.0

---

### 2. API Keys
**Pros:**
- ✅ Simple de implementar
- ✅ Sin necesidad de tokens

**Contras:**
- ❌ No contiene claims/metadata
- ❌ Difícil de revocar
- ❌ No expira automáticamente

**Veredicto:** Rechazado por limitaciones

---

### 3. JWT Simplificado ✅ (Seleccionado)
**Pros:**
- ✅ Sin dependencias externas
- ✅ Stateless (no requiere DB)
- ✅ Contiene claims extensibles
- ✅ Expiración automática
- ✅ Estándar ampliamente adoptado
- ✅ Fácil evolución hacia OAuth2/OIDC

**Contras:**
- ⚠️ Sin revocación (requiere esperar expiración)
- ⚠️ Secret key en variables de entorno (gestión manual)
- ⚠️ Sin refresh tokens
- ⚠️ NO apto para producción sin IdP

**Veredicto:** ✅ Seleccionado para MVP

## Implementación

### Generación del Token
```csharp
// api/src/Program.cs
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
        Expires = DateTime.UtcNow.AddHours(1),  // ⏱️ TTL: 1 hora
        SigningCredentials = creds
    };

    var handler = new JsonWebTokenHandler();
    var token = handler.CreateToken(desc);
    return Results.Ok(new { token });
});
```

### Validación
```csharp
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
  .AddJwtBearer(o =>
  {
      o.RequireHttpsMetadata = false;  // ⚠️ Solo para desarrollo
      o.TokenValidationParameters = new TokenValidationParameters
      {
          ValidateIssuer = false,
          ValidateAudience = false,
          ValidateIssuerSigningKey = true,
          IssuerSigningKey = new SymmetricSecurityKey(
              Encoding.UTF8.GetBytes(jwtKey))
      };
  });
```

## Consecuencias

### Positivas ✅
- Desarrollo rápido sin dependencias externas
- Testing local simplificado
- Base para evolución hacia OAuth2/OIDC
- Stateless (no requiere almacenamiento de sesiones)

### Negativas ⚠️
- **CRÍTICO:** Sin revocación de tokens (si un token se compromete, es válido hasta expirar)
- Secret key debe gestionarse manualmente
- No hay concepto de usuarios/roles reales
- No apto para multi-tenancy
- `RequireHttpsMetadata = false` es inseguro (solo para desarrollo)

### Limitaciones Conocidas

| Limitación | Impacto | Cuándo Resolver |
|------------|---------|-----------------|
| Sin revocación | Alto | Antes de producción |
| Secret key estático | Medio | Migrar a Key Vault en producción |
| Sin refresh tokens | Medio | v2.0 con IdP |
| HTTP permitido | Crítico | Configurar HTTPS en producción |
| Usuario hardcoded | Bajo | v2.0 con base de usuarios |

## Plan de Evolución

### Fase 1: MVP (Actual) ✅
```
Cliente → API → JWT local (firma HMAC)
```

### Fase 2: Producción Mínima
```
Cliente → API → JWT local + HTTPS + Key Vault
```

### Fase 3: IdP Externo (v2.0)
```
Cliente → Auth0/Azure AD → API (valida token)
```

## Configuración

### Variables de Entorno
```yaml
JWT__KEY: "dev-local-change-me"  # ⚠️ Cambiar en producción
```

### Uso
```bash
# 1. Obtener token
TOKEN=$(curl -s http://localhost:8080/token | jq -r '.token')

# 2. Usar token
curl -H "Authorization: Bearer $TOKEN" http://localhost:8080/orders
```

## Criterios de Migración a IdP

Migrar cuando se cumpla cualquiera de:
- [ ] Se requiera revocación inmediata de tokens
- [ ] Múltiples clientes/aplicaciones consuman la API
- [ ] Se necesiten roles/permisos granulares
- [ ] Deployment a producción con usuarios reales
- [ ] Cumplimiento de normativas (GDPR, SOC2, etc.)

## Referencias
- [JWT.io](https://jwt.io/)
- [RFC 7519 - JSON Web Token](https://tools.ietf.org/html/rfc7519)
- [OWASP JWT Cheat Sheet](https://cheatsheetseries.owasp.org/cheatsheets/JSON_Web_Token_for_Java_Cheat_Sheet.html)
- Microsoft Identity Platform: [Tokens Overview](https://learn.microsoft.com/en-us/azure/active-directory/develop/security-tokens)

## Revisión
- **Fecha de Revisión:** 2025-04-28 (3 meses)
- **Criterio de Revisión:** Antes de cualquier deployment a producción
- **Responsable:** Arquitecto de Solución + Security Lead

---
**Última actualización:** 2025-01-28  
**Autor:** @LuisRaziel  
**⚠️ Estado:** MVP - Requiere evolución para producción