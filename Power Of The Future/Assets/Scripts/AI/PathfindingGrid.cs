using UnityEngine;
using UnityEngine.Tilemaps;
using System.Collections.Generic;

/// <summary>
/// Genera un grid de navegación a partir de los Tilemaps de la escena.
/// Detecta tiles de agua, colliders y límites del mapa para marcar celdas como caminables o no.
/// </summary>
public class PathfindingGrid : MonoBehaviour
{
    [Header("Referencias")]
    [Tooltip("Objeto Grid que contiene los Tilemaps hijos")]
    public Grid grid;

    [Tooltip("Tilemap cuyas celdas son transitables (ej: suelo). Si es null, se detecta automáticamente.")]
    public Tilemap walkableTilemap;

    [Tooltip("Tilemap(s) que representan agua (no caminable). Asigna todos los tilemaps de agua.")]
    public Tilemap[] waterTilemaps;

    [Tooltip("Tilemap(s) con TilemapCollider2D (paredes, obstáculos). Si es null, se detectan automáticamente.")]
    public Tilemap[] obstacleTilemaps;

    [Header("Configuración")]
    [Tooltip("Offset para posicionar el grid en el mundo (opcional)")]
    public Vector2 gridWorldOffset;

    [Tooltip("Tamaño del grid en celdas (ancho y alto). 0 = auto-detectar desde los bounds del tilemap.")]
    public int gridWidth = 0;
    public int gridHeight = 0;

    [Tooltip("Layer para los tiles transitables. Dejar en default si no se usa.")]
    public int walkableLayer = 0;

    [Header("Debug")]
    public bool showDebugGizmos = true;
    public Color walkableColor = new Color(0f, 1f, 0f, 0.3f);
    public Color unwalkableColor = new Color(1f, 0f, 0f, 0.3f);
    public Color waterColor = new Color(0f, 0.3f, 1f, 0.4f);

    // --- Datos internos ---
    // true = caminable, false = no caminable
    public bool[,] walkableGrid { get; private set; }
    public int Width { get; private set; }
    public int Height { get; private set; }
    public Vector3 cellSize { get; private set; }

    // Conjunto rápido de celdas de agua (para renderizado)
    private HashSet<Vector2Int> waterCells = new HashSet<Vector2Int>();
    private HashSet<Vector2Int> obstacleCells = new HashSet<Vector2Int>();

    private static PathfindingGrid _instance;
    public static PathfindingGrid Instance
    {
        get
        {
            if (_instance == null)
            {
                _instance = FindFirstObjectByType<PathfindingGrid>();
                if (_instance != null) _instance.Initialize();
            }
            return _instance;
        }
    }

    private bool initialized = false;

    private void Awake()
    {
        if (_instance != null && _instance != this)
        {
            Destroy(gameObject);
            return;
        }
        _instance = this;
        Initialize();
    }

    /// <summary>
    /// Inicializa el grid de navegación. Se puede llamar manualmente si se necesitan re-calcular.
    /// </summary>
    public void Initialize()
    {
        if (initialized) return;

        // Auto-detectar Grid si no se asignó
        if (grid == null)
            grid = FindFirstObjectByType<Grid>();

        if (grid == null)
        {
            Debug.LogError("[PathfindingGrid] No se encontró un componente Grid en la escena.");
            return;
        }

        var cs = grid.cellSize;
        cs.z = 0;
        cellSize = cs;

        // Auto-detectar Tilemaps
        FindTilemaps(ref walkableTilemap, ref waterTilemaps, ref obstacleTilemaps);

        // Calcular tamaño del grid
        CalculateGridSize();

        // Construir grid de caminabilidad
        BuildWalkableGrid();

        initialized = true;
        Debug.Log($"[PathfindingGrid] Grid inicializado: {Width}x{Height} celdas.");
    }

    /// <summary>
    /// Auto-detecta los tilemaps relevantes en hijos del Grid.
    /// </summary>
    private void FindTilemaps(ref Tilemap walkable, ref Tilemap[] waters, ref Tilemap[] obstacles)
    {
        Tilemap[] allTilemaps = GetComponentsInChildren<Tilemap>();

        // Detectar TilemapCollider2D
        HashSet<Tilemap> colliderTilemaps = new HashSet<Tilemap>();
        foreach (var tm in allTilemaps)
        {
            var collider = tm.GetComponent<TilemapCollider2D>();
            if (collider != null && collider.enabled)
                colliderTilemaps.Add(tm);
        }

        // Si no se asignaron manualmente, usar heurística
        if (walkable == null)
        {
            // El primer tilemap sin colisionador es suelo (caminable)
            foreach (var tm in allTilemaps)
            {
                if (!colliderTilemaps.Contains(tm) && tm.gameObject.activeInHierarchy)
                {
                    walkable = tm;
                    break;
                }
            }
        }

        if (waters == null || waters.Length == 0)
        {
            // Buscar tilemaps con "Water" en el nombre
            var waterList = new List<Tilemap>();
            foreach (var tm in allTilemaps)
            {
                if (tm.name.Contains("Water") || tm.name.Contains("water"))
                    waterList.Add(tm);
            }
            waters = waterList.ToArray();
        }

        if (obstacles == null || obstacles.Length == 0)
        {
            obstacles = new List<Tilemap>(colliderTilemaps).ToArray();
        }
    }

    /// <summary>
    /// Calcula las dimensiones del grid basándose en los bounds de todos los tilemaps.
    /// </summary>
    private void CalculateGridSize()
    {
        BoundsInt totalBounds = new BoundsInt();
        bool first = true;

        foreach (var tm in GetComponentsInChildren<Tilemap>())
        {
            if (!tm.gameObject.activeInHierarchy) continue;
            BoundsInt bounds = tm.cellBounds;
            if (first)
            {
                totalBounds = bounds;
                first = false;
            }
            else
            {
                totalBounds.xMin = Mathf.Min(totalBounds.xMin, bounds.xMin);
                totalBounds.yMin = Mathf.Min(totalBounds.yMin, bounds.yMin);
                totalBounds.xMax = Mathf.Max(totalBounds.xMax, bounds.xMax);
                totalBounds.yMax = Mathf.Max(totalBounds.yMax, bounds.yMax);
            }
        }

        if (gridWidth <= 0) gridWidth = totalBounds.size.x + 2; // +2 margen
        if (gridHeight <= 0) gridHeight = totalBounds.size.y + 2;

        Width = gridWidth;
        Height = gridHeight;
    }

    /// <summary>
    /// Construye la matriz de caminabilidad.
    /// </summary>
    private void BuildWalkableGrid()
    {
        walkableGrid = new bool[Width, Height];
        waterCells.Clear();
        obstacleCells.Clear();

        // Primero: marcar todo como caminable
        for (int x = 0; x < Width; x++)
            for (int y = 0; y < Height; y++)
                walkableGrid[x, y] = true;

        // Segundo: marcar tiles del tilemap de colisión como NO caminables
        foreach (var tm in obstacleTilemaps)
        {
            if (tm == null) continue;
            BoundsInt bounds = tm.cellBounds;
            foreach (var pos in bounds.allPositionsWithin)
            {
                if (tm.HasTile(pos))
                {
                    Vector2Int gridPos = WorldToGrid(pos);
                    if (IsInBounds(gridPos))
                    {
                        walkableGrid[gridPos.x, gridPos.y] = false;
                        obstacleCells.Add(gridPos);
                    }
                }
            }
        }

        // Tercero: marcar tiles de agua como NO caminables
        for (int i = 0; i < waterTilemaps.Length; i++)
        {
            var tm = waterTilemaps[i];
            if (tm == null) continue;
            BoundsInt bounds = tm.cellBounds;
            foreach (var pos in bounds.allPositionsWithin)
            {
                if (tm.HasTile(pos))
                {
                    Vector2Int gridPos = WorldToGrid(pos);
                    if (IsInBounds(gridPos))
                    {
                        walkableGrid[gridPos.x, gridPos.y] = false;
                        waterCells.Add(gridPos);
                    }
                }
            }
        }
    }

    /// <summary>
    /// Convierte una posición de tile (local al tilemap) a coordenadas del grid.
    /// </summary>
    public Vector2Int TileToGrid(Vector3Int tilePos)
    {
        // Las posiciones de tile son en espacio local del grid
        // Offset para centrar el grid
        int offsetX = Width / 2;
        int offsetY = Height / 2;

        return new Vector2Int(tilePos.x + offsetX, tilePos.y + offsetY);
    }

    /// <summary>
    /// Convierte una posición de tile a coordenadas del grid (usando bounds calculados).
    /// </summary>
    public Vector2Int WorldToGrid(Vector3Int tilePos)
    {
        // Calcular usando los bounds del mapa
        BoundsInt totalBounds = GetTotalTilemapBounds();
        int originX = totalBounds.xMin;
        int originY = totalBounds.yMin;

        return new Vector2Int(tilePos.x - originX, tilePos.y - originY);
    }

    /// <summary>
    /// Convierte una coordenada del grid a posición de tile.
    /// </summary>
    public Vector3Int GridToTile(Vector2Int gridPos)
    {
        BoundsInt totalBounds = GetTotalTilemapBounds();
        return new Vector3Int(gridPos.x + totalBounds.xMin, gridPos.y + totalBounds.yMin, 0);
    }

    /// <summary>
    /// Convierte coordenadas del grid a posición en el mundo.
    /// </summary>
    public Vector3 GridToWorld(Vector2Int gridPos)
    {
        Vector3Int tilePos = GridToTile(gridPos);
        return grid.CellToWorld(tilePos) + (Vector3)(grid.cellSize * 0.5f);
    }

    /// <summary>
    /// Convierte posición en el mundo a coordenadas del grid.
    /// </summary>
    public Vector2Int WorldToGrid(Vector3 worldPos)
    {
        Vector3Int cellPos = grid.WorldToCell(worldPos);
        return WorldToGrid(cellPos);
    }

    private BoundsInt GetTotalTilemapBounds()
    {
        BoundsInt total = new BoundsInt();
        bool first = true;
        foreach (var tm in GetComponentsInChildren<Tilemap>())
        {
            if (!tm.gameObject.activeInHierarchy) continue;
            if (first) { total = tm.cellBounds; first = false; }
            else
            {
                total.xMin = Mathf.Min(total.xMin, tm.cellBounds.xMin);
                total.yMin = Mathf.Min(total.yMin, tm.cellBounds.yMin);
                total.xMax = Mathf.Max(total.xMax, tm.cellBounds.xMax);
                total.yMax = Mathf.Max(total.yMax, tm.cellBounds.yMax);
            }
        }
        return total;
    }

    /// <summary>
    /// Verifica si una celda del grid está dentro de los límites y es caminable.
    /// </summary>
    public bool IsWalkable(Vector2Int gridPos)
    {
        if (!IsInBounds(gridPos)) return false;
        return walkableGrid[gridPos.x, gridPos.y];
    }

    /// <summary>
    /// Verifica si una celda está dentro de los límites del grid.
    /// </summary>
    public bool IsInBounds(Vector2Int gridPos)
    {
        return gridPos.x >= 0 && gridPos.x < Width &&
               gridPos.y >= 0 && gridPos.y < Height;
    }

    /// <summary>
    /// Verifica si una posición en el mundo es caminable.
    /// </summary>
    public bool IsWalkableWorld(Vector3 worldPos)
    {
        var gridPos = WorldToGrid(worldPos);
        // Verificar también colisión con colliders del escenario
        if (Physics2D.OverlapCircle(worldPos, 0.2f, LayerMask.GetMask("Default")) != null)
        {
            // Hacer más preciso si hay colliders extra
        }
        return IsWalkable(gridPos);
    }

    /// <summary>
    /// Obtiene las celdas adyacentes (8-direccional) de una celda.
    /// </summary>
    public List<Vector2Int> GetNeighbors(Vector2Int gridPos)
    {
        var neighbors = new List<Vector2Int>(8);
        for (int dx = -1; dx <= 1; dx++)
        {
            for (int dy = -1; dy <= 1; dy++)
            {
                if (dx == 0 && dy == 0) continue;
                var neighbor = new Vector2Int(gridPos.x + dx, gridPos.y + dy);
                if (IsWalkable(neighbor))
                    neighbors.Add(neighbor);
            }
        }
        return neighbors;
    }

    /// <summary>
    /// Obtiene las celdas adyacentes (4-direccional: arriba, abajo, izquierda, derecha).
    /// Útil para movimientos con restricción de diagonales.
    /// </summary>
    public List<Vector2Int> GetCardinalNeighbors(Vector2Int gridPos)
    {
        var neighbors = new List<Vector2Int>(4);
        Vector2Int[] directions = { Vector2Int.up, Vector2Int.down, Vector2Int.left, Vector2Int.right };
        foreach (var dir in directions)
        {
            var neighbor = gridPos + dir;
            if (IsWalkable(neighbor))
                neighbors.Add(neighbor);
        }
        return neighbors;
    }

    /// <summary>
    /// Obtiene vecinos para pathfinding con costo diagonal correcto.
    /// Retorna tuplas (posición, esDiagonal).
    /// </summary>
    public List<(Vector2Int pos, bool isDiagonal)> GetNeighborsWithDiagonalInfo(Vector2Int gridPos)
    {
        var result = new List<(Vector2Int, bool)>(8);
        for (int dx = -1; dx <= 1; dx++)
        {
            for (int dy = -1; dy <= 1; dy++)
            {
                if (dx == 0 && dy == 0) continue;
                bool isDiagonal = dx != 0 && dy != 0;
                var neighbor = new Vector2Int(gridPos.x + dx, gridPos.y + dy);

                if (IsWalkable(neighbor))
                {
                    // Para diagonales, verificar que las celdas cardinales también sean transitables
                    // para evitar "atalajos" entre paredes diagonales
                    if (isDiagonal)
                    {
                        bool horizontalBlocked = !IsWalkable(new Vector2Int(gridPos.x + dx, gridPos.y));
                        bool verticalBlocked = !IsWalkable(new Vector2Int(gridPos.x, gridPos.y + dy));
                        if (horizontalBlocked && verticalBlocked)
                            continue;
                    }
                    result.Add((neighbor, isDiagonal));
                }
            }
        }
        return result;
    }

    private void OnDrawGizmos()
    {
        if (!showDebugGizmos || walkableGrid == null) return;

        if (grid == null && _instance != null)
            grid = _instance.grid;
        if (grid == null) return;

        for (int x = 0; x < Width; x++)
        {
            for (int y = 0; y < Height; y++)
            {
                Vector3 worldPos = GridToWorld(new Vector2Int(x, y));
                worldPos.z = 0;

                if (waterCells.Contains(new Vector2Int(x, y)))
                {
                    Gizmos.color = waterColor;
                    Gizmos.DrawCube(worldPos, new Vector3(cellSize.x * 0.9f, cellSize.y * 0.9f, 0.01f));
                }
                else if (!walkableGrid[x, y])
                {
                    Gizmos.color = unwalkableColor;
                    Gizmos.DrawCube(worldPos, new Vector3(cellSize.x * 0.9f, cellSize.y * 0.9f, 0.01f));
                }
                else
                {
                    Gizmos.color = walkableColor;
                    Gizmos.DrawWireCube(worldPos, new Vector3(cellSize.x * 0.9f, cellSize.y * 0.9f, 0.01f));
                }
            }
        }
    }
}