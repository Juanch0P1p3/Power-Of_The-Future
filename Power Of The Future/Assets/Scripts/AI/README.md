# 🦾 Sistema de IA para "Power Of The Future"

## Descripción General

Este sistema proporciona **pathfinding A*** sobre el Tilemap de Unity, con soporte completo para:

- ✅ Navegación respetando **límites del Tilemap**
- ✅ Detección y evasión de **colliders** del mapa (paredes, plataformas)
- ✅ Exclusión de **tiles de agua** (no caminables)
- ✅ **Pathfinding 8-direccional** con detección de atajos diagonales entre paredes
- ✅ **Heap binario** para rendimiento óptimo
- ✅ **Suavizado de ruta** (elimina waypoints colineales)
- ✅ Base extensible para **enemigos y NPCs**

---

## 📁 Estructura de Archivos

```
Assets/Scripts/AI/
├── PathfindingGrid.cs     # Gestor del grid de navegación (Singleton)
├── AStarPathfinder.cs     # Implementación A* con heap binario
├── AIEntity.cs            # Clase base abstracta para entidades con IA
├── EnemyAI.cs             # Comportamiento de enemigo (persecución, patrulla)
└── NPCAI.cs               # Comportamiento de NPC (patrulla, wander, interacción)

Assets/Scripts/Enemy/
└── Enemy.cs               # [DEPRECATED] Reemplazar por EnemyAI
```

---

## 🛠️ Guía de Configuración en Unity

### Paso 1: Asignar el Tilemap de Agua

1. Selecciona el objeto **"Water"** en la jerarquía (bajo Grid → Water)
2. En el Inspector, arrastra ese objeto al campo `Water Tilemaps` de `PathfindingGrid`

### Paso 2: Auto-detección de Obstáculos

Los Tilemaps con `TilemapCollider2D` se detectan automáticamente:
- **Map_Collisions**
- **Decoration**
- El resto de colliders del nivel

### Paso 3: Configurar el EnemyAI

1. En el objeto **"Enemy"** (bajo Level 1):
   - Remover el componente `Enemy` (obsoleto)
   - Añadir componente **`EnemyAI`**
   - Configurar `Player Layer` al layer del jugador
   - Asignar `Sight Blocking Layers` (los colliders del mapa)

2. Configurar parámetros:
   ```
   Detection Range: 6
   Detection Angle: 120
   Attack Range: 1.2
   Chase Speed: 4
   Patrol Speed: 2
   Max Health: 50
   ```

### Paso 4: Crear NPCs (opcional)

1. Crear un nuevo GameObject con SpriteRenderer, Rigidbody2D, Animator
2. Añadir componente **`NPCAI`**
3. Seleccionar tipo: `Patrol`, `Wander`, `Static`, etc.
4. Crear GameObjects vacíos como **Waypoints** y asignarlos al arreglo

### Paso 5: Verificar el Grid

Activar `Show Debug Gizmos` en `PathfindingGrid` para visualizar en Scene view:
- 🟢 Verde claro: celdas transitables
- 🔴 Rojo: obstáculos (paredes, colliders)
- 🔵 Azul: agua (no caminable)

---

## 📚 API de Referencia

### PathfindingGrid
| Método | Descripción |
|--------|-------------|
| `Instance` | Singleton global |
| `Initialize()` | Inicializa/reinicializa el grid |
| `IsWalkable(worldPos)` | ¿Es caminable una posición del mundo? |
| `WorldToGrid(worldPos)` | Posición mundo → coordenada grid |
| `GridToWorld(gridPos)` | Coordenada grid → posición mundo |
| `GetNeighbors(pos)` | Celdas adyacentes transitables |

### AStarPathfinder
| Método | Descripción |
|--------|-------------|
| `FindPath(from, to)` | Encuentra ruta → List\<Vector3\> posiciones mundo |

### EnemyAI
| Propiedad | Descripción |
|-----------|-------------|
| `CurrentState` | Estado actual (Patrolling/Chasing/Attacking/etc.) |
| `HomePosition` | Posición de origen para retorno |

| Método | Descripción |
|--------|-------------|
| `TakeDamage(dmg)` | Aplica daño |
| `SetHomePosition(pos)` | Cambia posición home |
| `SetPatrolWaypoints(wps)` | Asigna waypoints de patrulla |

### NPCAI
| Método | Descripción |
|--------|-------------|
| `Interact(player)` | Activar interacción con el jugador |
| `EndInteraction()` | Terminar interacción |

---

## 🔧 Notas Técnicas

- **Heap binario**: A* usa un min-heap implementado con `List<T>` y operaciones SiftUp/SiftDown (O(log n))
- **Caché de nodos**: Los nodos se reusan entre búsquedas con un `Dictionary`
- **Caminabilidad diagonal**: Se verifica que las celdas cardinales no estén ambas bloqueadas para evitar "atajos" entre paredes diagonales
- **Pool-free**: Las listas se limpian y reusan (lista abierta, set cerrado) para minimizar GC

---

## 🎮 Controles del Jugador

El jugador actual usa `Input.GetAxisRaw` (sistema legacy). El nuevo Input System con acciones (`Move`, `Attack`, `Jump`, `Sprint`, `Interact`) está configurado en `Assets/InputSystem_Actions.inputactions` pero **aún no está conectado** a los scripts.

---

## ⚠️ Deuda Técnica Pendiente

1. **Player.cs** usa input legacy en vez del nuevo Input System
2. El enemigo en la escena usa el componente obsoleto `Enemy` → migrar a `EnemyAI`
3. No hay Assembly Definitions → todo compila en un solo assembly
4. Falta sistema de vida del jugador (`PlayerHealth`)