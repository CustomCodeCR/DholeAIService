# Corrección del caché de perfiles de IA

## Problema corregido

Entity Framework Core no podía traducir el filtro aplicado después de construir `ProfileHeaderProjection` en `AiProfileRepository.GetDtoByIdAsync` y `GetDtoByKeyAsync`.

## Cambios aplicados

- Los filtros por `Id` y `Key` ahora se aplican sobre `IQueryable<AiProfile>` antes de la proyección.
- `GetDtoByIdAsync` y `GetDtoByKeyAsync` ejecutan `SingleOrDefaultAsync` sin predicado sobre la consulta ya filtrada.
- Se agregó validación de `key` mediante `ArgumentException.ThrowIfNullOrWhiteSpace`.
- El warmup de perfiles ahora captura errores por perfil, registra cuántos se cargaron y evita que un perfil defectuoso detenga todo el proceso.
- La cancelación solicitada por el host sigue propagándose correctamente.

## Archivos modificados

- `src/Dhole.AI.Persistence/Repositories/AiProfileRepository.cs`
- `src/Dhole.AI.Workers/Workers/AiCacheWarmupWorker.cs`

## Ejecución sugerida

```bash
dotnet clean
dotnet build
dotnet run --project src/Dhole.AI.Api
```

En otra terminal:

```bash
dotnet run --project src/Dhole.AI.Workers
```
