# Perfiles predeterminados de IA

El servicio provisiona automáticamente las plantillas y perfiles requeridos por Web y Pricing:

| Clave | Uso | Enrutamiento |
|---|---|---|
| `assistant` | Asistente conversacional de Dhole Web | `LocalFirst` |
| `pricing-email-analysis` | Análisis asíncrono de correos y extracciones de Pricing | `PriorityFallback` |
| `pricing-dashboard-analysis` | Comparación y recomendación de tarifas del panel de Pricing | `PriorityFallback` |

## Funcionamiento

1. Al iniciar la API se crean las tres plantillas y los tres perfiles si no existen.
2. Si ya existen modelos activos con capacidad `Chat`, se asignan automáticamente y los perfiles se activan.
3. Si todavía no hay modelos, los perfiles quedan creados e inactivos. El servicio vuelve a comprobar cada 30 segundos y los configura cuando se registre o descubra el primer modelo compatible.
4. Los perfiles de Pricing priorizan modelos disponibles, con salida estructurada y mayor ventana de contexto. El asistente general prioriza modelos locales y utiliza los demás como fallback.
5. Las configuraciones existentes se conservan mientras tengan al menos un modelo activo compatible. Si todos los modelos configurados dejan de estar disponibles, el provisionador asigna modelos compatibles para mantener operativo el perfil. El administrador puede cambiar prioridades y fallbacks desde la consola de IA.

## Configuración

```json
"AI": {
  "DefaultProfiles": {
    "Enabled": true,
    "RetrySeconds": 30,
    "MaximumModelsPerProfile": 5
  }
}
```

Para desactivar el provisionamiento automático use `AI__DefaultProfiles__Enabled=false`.
