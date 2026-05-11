using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// Implementación de A* Pathfinding con Binary Heap para optimización.
/// Usa el grid de PathfindingGrid para encontrar rutas óptimas respetando
/// tiles de agua, colliders y límites del mapa.
/// </summary>
public class AStarPathfinder
{
    private PathfindingGrid grid;

    // Pool de listas para evitar allocaciones
    private static List<PathfindingNode> openList = new List<PathfindingNode>();
    private static HashSet<Vector2Int> closedSet = new HashSet<Vector2Int>();
    private static Dictionary<Vector2Int, PathfindingNode> allNodes = new Dictionary<Vector2Int, PathfindingNode>();

    // Reutilización de lista de resultados
    private static List<Vector3> pathResult = new List<Vector3>();

    private class PathfindingNode : System.IComparable<PathfindingNode>
    {
        public Vector2Int gridPos;
        public int gCost;  // Coste desde el inicio
        public int hCost;  // Heurística (estimado al destino)
        public int fCost;  // gCost + hCost
        public PathfindingNode parent;

        public PathfindingNode(Vector2Int pos)
        {
            gridPos = pos;
        }

        public int CompareTo(PathfindingNode other)
        {
            int compare = fCost.CompareTo(other.fCost);
            if (compare == 0)
                compare = hCost.CompareTo(other.hCost);
            return compare;
        }
    }

    public AStarPathfinder(PathfindingGrid pathfindingGrid)
    {
        grid = pathfindingGrid;
    }

    /// <summary>
    /// Encuentra un camino desde worldStart hasta worldEnd.
    /// Retorna lista de posiciones en el mundo (Vector3). Retorna null si no hay camino.
    /// </summary>
    public List<Vector3> FindPath(Vector3 worldStart, Vector3 worldEnd)
    {
        return FindPath(grid.WorldToGrid(worldStart), grid.WorldToGrid(worldEnd));
    }

    /// <summary>
    /// Encuentra un camino desde startGrid hasta endGrid (en coordenadas de grid).
    /// Retorna lista de posiciones en el mundo (Vector3). Retorna null si no hay camino.
    /// </summary>
    public List<Vector3> FindPath(Vector2Int startGrid, Vector2Int endGrid)
    {
        // Limpiar estructuras
        openList.Clear();
        closedSet.Clear();
        allNodes.Clear();

        // Validar inicio y destino
        if (!grid.IsWalkable(startGrid))
        {
            // Intentar encontrar la celda caminable más cercana al inicio
            startGrid = FindNearestWalkable(startGrid, 10);
            if (startGrid == new Vector2Int(int.MinValue, int.MinValue))
                return null;
        }

        if (!grid.IsWalkable(endGrid))
        {
            // Intentar encontrar la celda caminable más cercana al destino
            endGrid = FindNearestWalkable(endGrid, 10);
            if (endGrid == new Vector2Int(int.MinValue, int.MinValue))
                return null;
        }

        // Si inicio == destino, retornar ruta trivial
        if (startGrid == endGrid)
        {
            pathResult.Clear();
            pathResult.Add(grid.GridToWorld(startGrid));
            return pathResult;
        }

        // Crear nodo inicial
        PathfindingNode startNode = GetOrCreateNode(startGrid);
        startNode.gCost = 0;
        startNode.hCost = Heuristic(startGrid, endGrid);
        startNode.fCost = startNode.hCost;

        openList.Add(startNode);

        int maxIterations = grid.Width * grid.Height; // Seguridad contra bucles infinitos
        int iterations = 0;

        while (openList.Count > 0)
        {
            iterations++;
            if (iterations > maxIterations)
            {
                Debug.LogWarning("[AStarPathfinder] Excedido límite de iteraciones. Posible bucle infinito.");
                break;
            }

            // Obtener nodo con menor fCost del heap
            PathfindingNode currentNode = PopMinNode();

            // Si llegamos al destino, reconstruir ruta
            if (currentNode.gridPos == endGrid)
            {
                return RetracePath(startGrid, currentNode);
            }

            closedSet.Add(currentNode.gridPos);

            // Explorar vecinos
            List<(Vector2Int pos, bool isDiagonal)> neighbors = grid.GetNeighborsWithDiagonalInfo(currentNode.gridPos);

            for (int i = 0; i < neighbors.Count; i++)
            {
                var (neighborPos, isDiagonal) = neighbors[i];

                if (closedSet.Contains(neighborPos))
                    continue;

                // Coste de movimiento: 10 cardinal, 14 diagonal (aprox √2 * 10)
                int moveCost = isDiagonal ? 14 : 10;
                int tentativeGCost = currentNode.gCost + moveCost;

                PathfindingNode neighborNode = GetOrCreateNode(neighborPos);

                if (tentativeGCost < neighborNode.gCost)
                {
                    neighborNode.gCost = tentativeGCost;
                    neighborNode.hCost = Heuristic(neighborPos, endGrid);
                    neighborNode.fCost = neighborNode.gCost + neighborNode.hCost;
                    neighborNode.parent = currentNode;

                    if (!openList.Contains(neighborNode))
                    {
                        InsertNode(neighborNode);
                    }
                }
            }
        }

        // No se encontró camino
        return null;
    }

    /// <summary>
    /// Encuentra la celda caminable más cercana a la posición dada.
    /// BFS en espiral desde el punto.
    /// </summary>
    private Vector2Int FindNearestWalkable(Vector2Int start, int maxRadius)
    {
        if (grid.IsWalkable(start))
            return start;

        for (int radius = 1; radius <= maxRadius; radius++)
        {
            for (int dx = -radius; dx <= radius; dx++)
            {
                for (int dy = -radius; dy <= radius; dy++)
                {
                    // Solo bordes del cuadrado de radio actual
                    if (Mathf.Abs(dx) != radius && Mathf.Abs(dy) != radius)
                        continue;

                    Vector2Int check = new Vector2Int(start.x + dx, start.y + dy);
                    if (grid.IsWalkable(check))
                        return check;
                }
            }
        }

        return new Vector2Int(int.MinValue, int.MinValue); // No encontrado
    }

    /// <summary>
    /// Reconstruye la ruta desde el nodo final hasta el inicial.
    /// </summary>
    private List<Vector3> RetracePath(Vector2Int startGrid, PathfindingNode endNode)
    {
        pathResult.Clear();
        List<Vector2Int> gridPath = new List<Vector2Int>();

        PathfindingNode current = endNode;
        while (current != null)
        {
            gridPath.Add(current.gridPos);
            current = current.parent;
        }

        // Invertir (de inicio a fin)
        gridPath.Reverse();

        // Simplificar ruta: eliminar puntos intermedios en línea recta (path smoothing)
        // y convertir a posiciones del mundo
        SimplifyAndConvert(gridPath);

        return pathResult;
    }

    /// <summary>
    /// Simplifica la ruta eliminando puntos colineales y convierte a Vector3 del mundo.
    /// </summary>
    private void SimplifyAndConvert(List<Vector2Int> gridPath)
    {
        if (gridPath.Count == 0) return;

        // Siempre incluir el primer punto
        pathResult.Add(grid.GridToWorld(gridPath[0]));

        if (gridPath.Count <= 2)
        {
            if (gridPath.Count == 2)
                pathResult.Add(grid.GridToWorld(gridPath[1]));
            return;
        }

        // Simplificación: solo incluir puntos donde cambia la dirección
        Vector2Int prevDir = gridPath[1] - gridPath[0];

        for (int i = 1; i < gridPath.Count - 1; i++)
        {
            Vector2Int nextDir = gridPath[i + 1] - gridPath[i];
            if (nextDir != prevDir)
            {
                pathResult.Add(grid.GridToWorld(gridPath[i]));
                prevDir = nextDir;
            }
        }

        // Siempre incluir el último punto
        pathResult.Add(grid.GridToWorld(gridPath[gridPath.Count - 1]));
    }

    /// <summary>
    /// Heurística: distancia Manhattan optimizada con tie-breaker.
    /// </summary>
    private int Heuristic(Vector2Int a, Vector2Int b)
    {
        int dx = Mathf.Abs(a.x - b.x);
        int dy = Mathf.Abs(a.y - b.y);

        // Octile distance (para movimiento 8-direccional)
        int diagonal = Mathf.Min(dx, dy);
        int straight = dx + dy - 2 * diagonal;

        // Tie-breaker para preferir rutas más directas
        float tieBreaker = 1.0f + 1f / (Mathf.Max(dx, dy) + 1);

        return (int)((diagonal * 14 + straight * 10) * tieBreaker);
    }

    /// <summary>
    /// Obtiene o crea un nodo del pool.
    /// </summary>
    private PathfindingNode GetOrCreateNode(Vector2Int pos)
    {
        if (!allNodes.TryGetValue(pos, out PathfindingNode node))
        {
            node = new PathfindingNode(pos)
            {
                gCost = int.MaxValue,
                hCost = 0,
                fCost = int.MaxValue
            };
            allNodes[pos] = node;
        }
        return node;
    }

    #region Binary Heap Operations

    /// <summary>
    /// Extrae el nodo con menor fCost (operación O(log n)).
    /// </summary>
    private PathfindingNode PopMinNode()
    {
        PathfindingNode min = openList[0];
        int last = openList.Count - 1;
        openList[0] = openList[last];
        openList.RemoveAt(last);

        if (openList.Count > 0)
            SiftDown(0);

        return min;
    }

    /// <summary>
    /// Inserta un nodo en el heap (operación O(log n)).
    /// </summary>
    private void InsertNode(PathfindingNode node)
    {
        openList.Add(node);
        SiftUp(openList.Count - 1);
    }

    /// <summary>
    /// Sube un elemento en el heap para mantener propiedad de min-heap.
    /// </summary>
    private void SiftUp(int index)
    {
        while (index > 0)
        {
            int parent = (index - 1) / 2;
            if (openList[index].CompareTo(openList[parent]) < 0)
            {
                Swap(index, parent);
                index = parent;
            }
            else
            {
                break;
            }
        }
    }

    /// <summary>
    /// Baja un elemento en el heap para mantener propiedad de min-heap.
    /// </summary>
    private void SiftDown(int index)
    {
        int count = openList.Count;
        while (true)
        {
            int smallest = index;
            int left = 2 * index + 1;
            int right = 2 * index + 2;

            if (left < count && openList[left].CompareTo(openList[smallest]) < 0)
                smallest = left;
            if (right < count && openList[right].CompareTo(openList[smallest]) < 0)
                smallest = right;

            if (smallest != index)
            {
                Swap(index, smallest);
                index = smallest;
            }
            else
            {
                break;
            }
        }
    }

    private void Swap(int a, int b)
    {
        PathfindingNode temp = openList[a];
        openList[a] = openList[b];
        openList[b] = temp;
    }

    #endregion

    /// <summary>
    /// Debug: dibuja la ruta en el editor durante un breve tiempo.
    /// </summary>
    public static void DebugDrawPath(List<Vector3> path, Color color, float duration = 2f)
    {
        if (path == null || path.Count < 2) return;
        for (int i = 0; i < path.Count - 1; i++)
        {
            Debug.DrawLine(path[i], path[i + 1], color, duration);
        }
    }
}