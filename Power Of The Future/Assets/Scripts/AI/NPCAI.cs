using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// Comportamiento de IA para NPCs (Personajes No Jugadores).
/// Los NPCs pueden patrullar, quedarse quietos, hablar con el jugador,
/// o seguir rutas predefinidas. Hereda la base de AIEntity para
/// reutilizar pathfinding y movimiento.
/// </summary>
[RequireComponent(typeof(Rigidbody2D))]
public class NPCAI : MonoBehaviour
{
    [Header("Tipo de NPC")]
    public NPCBehaviorType npcType = NPCBehaviorType.Static;

    public enum NPCBehaviorType
    {
        Static,       // Se queda quieto (ej: vendedor, NPC de quest)
        Patrol,       // Patrulla entre waypoints
        Wander,       // Camina aleatoriamente por el mapa
        FollowPath,   // Sigue una ruta fija en bucle
        Guard         // Permanece en posición pero vigila un área
    }

    [Header("Movimiento")]
    [Tooltip("Velocidad de movimiento del NPC")]
    public float moveSpeed = 2f;

    [Tooltip("Distancia mínima para considerar llegada a un punto")]
    public float waypointTolerance = 0.2f;

    [Tooltip("Tiempo mínimo de pausa en cada waypoint (segundos)")]
    public float minPauseTime = 1f;

    [Tooltip("Tiempo máximo de pausa en cada waypoint (segundos)")]
    public float maxPauseTime = 3f;

    [Header("Waypoints (para tipos Patrol y FollowPath)")]
    [Tooltip("Puntos de ruta del NPC. Se recorren en orden.")]
    public Transform[] waypoints;

    [Tooltip("Si true, vuelve al inicio tras llegar al último waypoint")]
    public bool loopPath = true;

    [Tooltip("Si true, va y viene por los waypoints (ida y vuelta)")]
    public bool pingPong = false;

    [Header("Radio de Wander (para tipo Wander)")]
    [Tooltip("Radio máximo desde la posición inicial para caminar aleatorio")]
    public float wanderRadius = 8f;

    [Tooltip("Tiempo mínimo entre cada cambio de dirección aleatoria")]
    public float wanderIntervalMin = 2f;

    [Tooltip("Tiempo máximo entre cada cambio de dirección aleatoria")]
    public float wanderIntervalMax = 6f;

    [Header("Interacción")]
    [Tooltip("Si el NPC puede interactuar con el jugador")]
    public bool interactable = false;

    [Tooltip("Distancia máxima de interacción")]
    public float interactionRange = 2f;

    [Tooltip("Dialogo a mostrar (opcional, se conecta con un sistema de diálogo)")]
    [TextArea(3, 5)]
    public string dialogueText = "";

    [Header("Detección del Jugador")]
    [Tooltip("Rango a partir del cual el NPC detecta al jugador")]
    public float awarenessRange = 5f;

    [Tooltip("Si el NPC mira al jugador cuando está cerca")]
    public bool lookAtPlayer = true;

    [Header("Debug & Feedback")]
    public bool showGizmos = true;
    public Color waypointColor = Color.green;
    public Color interactionColor = Color.blue;

    // --- Estado interno ---
    private Rigidbody2D rb;
    private SpriteRenderer spriteRenderer;
    private Animator animator;

    // Estado del NPC
    private enum NPCState
    {
        Idle,
        Walking,
        Paused,
        Interacting
    }

    private NPCState npcState;
    private int currentWaypointIndex;
    private float pauseTimer;
    private float wanderIntervalTimer;
    private Vector3 wanderTarget;
    private Vector3 homePosition;
    private bool goingForward = true; // Para patrulla bidireccional

    // Pathfinding
    private AStarPathfinder pathfinder;
    private List<Vector3> currentPath;
    private int currentPathIndex;
    private float pathRecalcTimer;
    private float currentSpeed;

    public bool IsInteractable => interactable;
    public NPCBehaviorType NpcType => npcType;

    public System.Action<string> OnDialogueRequested; // nombre del NPC, texto
    public System.Action OnInteraction;

    #region Lifecycle

    private void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
        spriteRenderer = GetComponent<SpriteRenderer>();
        animator = GetComponent<Animator>();

        homePosition = transform.position;
        currentSpeed = moveSpeed;

        npcState = NPCState.Idle;
    }

    private void Start()
    {
        if (PathfindingGrid.Instance != null)
        {
            pathfinder = new AStarPathfinder(PathfindingGrid.Instance);
        }

        wanderIntervalTimer = Random.Range(wanderIntervalMin, wanderIntervalMax);

        // Inicializar según tipo
        if (npcType == NPCBehaviorType.Static || npcType == NPCBehaviorType.Guard)
        {
            // No necesita ruta
            npcState = NPCState.Idle;
        }
        else if (npcType == NPCBehaviorType.Patrol || npcType == NPCBehaviorType.FollowPath)
        {
            if (waypoints != null && waypoints.Length > 0)
            {
                MoveToNextWaypoint();
            }
            else
            {
                Debug.LogWarning($"[NPCAI] {gameObject.name}: No hay waypoints asignados para patrulla.");
                npcType = NPCBehaviorType.Wander; // Fallback a wander
            }
        }
        else if (npcType == NPCBehaviorType.Wander)
        {
            SetNewWanderTarget();
        }
    }

    private void Update()
    {
        pathRecalcTimer -= Time.deltaTime;
        wanderIntervalTimer -= Time.deltaTime;

        HandlePauseState();
        UpdateFacing();
    }

    private void FixedUpdate()
    {
        switch (npcState)
        {
            case NPCState.Walking:
                MoveAlongPath();
                break;
            case NPCState.Idle:
            case NPCState.Interacting:
                rb.linearVelocity = Vector2.zero;
                break;
        }
    }

    #endregion

    #region Movement Logic

    private void HandlePauseState()
    {
        if (npcState == NPCState.Paused)
        {
            pauseTimer -= Time.deltaTime;

            if (pauseTimer <= 0f)
            {
                npcState = NPCState.Walking;

                // Para wander, generar nuevo target
                if (npcType == NPCBehaviorType.Wander)
                {
                    SetNewWanderTarget();
                }
            }
        }
    }

    /// <summary>
    /// Ejecuta el movimiento a lo largo de la ruta actual.
    /// </summary>
    private void MoveAlongPath()
    {
        if (currentPath == null || currentPath.Count == 0 || currentPathIndex >= currentPath.Count)
        {
            // Ruta completada
            OnRouteCompleted();
            return;
        }

        Vector3 targetWaypoint = currentPath[currentPathIndex];
        Vector2 direction = ((Vector2)(targetWaypoint - transform.position)).normalized;

        rb.linearVelocity = direction * currentSpeed;

        float dist = Vector2.Distance(transform.position, targetWaypoint);
        if (dist < waypointTolerance)
        {
            currentPathIndex++;

            if (currentPathIndex >= currentPath.Count)
            {
                OnRouteCompleted();
            }
        }
    }

    /// <summary>
    /// Llamado cuando se completa una ruta (llegó al último waypoint).
    /// </summary>
    private void OnRouteCompleted()
    {
        rb.linearVelocity = Vector2.zero;

        switch (npcType)
        {
            case NPCBehaviorType.Patrol:
                PauseAtWaypoint();
                break;

            case NPCBehaviorType.FollowPath:
                PauseAtWaypoint();
                break;

            case NPCBehaviorType.Wander:
                PauseAtWaypoint();
                break;

            default:
                npcState = NPCState.Idle;
                break;
        }
    }

    /// <summary>
    /// Realiza una pausa en el waypoint y luego continúa.
    /// </summary>
    private void PauseAtWaypoint()
    {
        npcState = NPCState.Paused;
        pauseTimer = Random.Range(minPauseTime, maxPauseTime);
    }

    #endregion

    #region Waypoint Navigation

    /// <summary>
    /// Mueve el NPC al siguiente waypoint en la ruta.
    /// </summary>
    private void MoveToNextWaypoint()
    {
        if (waypoints == null || waypoints.Length == 0) return;
        if (pathfinder == null)
        {
            Debug.LogWarning($"[NPCAI] {gameObject.name}: Pathfinder no disponible.");
            FallbackDirectMove(waypoints[currentWaypointIndex].position);
            return;
        }

        Vector3 targetPos = waypoints[currentWaypointIndex].position;
        var path = pathfinder.FindPath(transform.position, targetPos);

        if (path != null && path.Count > 0)
        {
            currentPath = path;
            currentPathIndex = 0;
            npcState = NPCState.Walking;
        }
        else
        {
            FallbackDirectMove(targetPos);
        }
    }

    /// <summary>
    /// Movimiento directo como respaldo cuando no hay pathfinding disponible.
    /// </summary>
    private void FallbackDirectMove(Vector3 target)
    {
        // Mover directamente sin pathfinding
        Vector2 direction = ((Vector2)(target - transform.position)).normalized;
        rb.linearVelocity = direction * currentSpeed;
        npcState = NPCState.Walking;
    }

    /// <summary>
    /// Cambia al siguiente waypoint (maneja bucle y ping-pong).
    /// </summary>
    private void AdvanceToNextWaypoint()
    {
        if (waypoints == null || waypoints.Length == 0) return;

        if (loopPath && !pingPong)
        {
            currentWaypointIndex = (currentWaypointIndex + 1) % waypoints.Length;
        }
        else if (pingPong)
        {
            if (goingForward)
            {
                if (currentWaypointIndex >= waypoints.Length - 1)
                {
                    goingForward = false;
                    currentWaypointIndex--;
                }
                else
                {
                    currentWaypointIndex++;
                }
            }
            else
            {
                if (currentWaypointIndex <= 0)
                {
                    goingForward = true;
                    currentWaypointIndex++;
                }
                else
                {
                    currentWaypointIndex--;
                }
            }
        }
        else
        {
            // Solo ir hacia adelante, pararse al final
            if (currentWaypointIndex < waypoints.Length - 1)
            {
                currentWaypointIndex++;
            }
            else
            {
                // Ruta completada, volver al inicio o quedarse quieto
                if (loopPath)
                {
                    currentWaypointIndex = 0;
                }
                else
                {
                    npcState = NPCState.Idle;
                    return;
                }
            }
        }

        MoveToNextWaypoint();
    }

    /// <summary>
    /// Genera un nuevo destino aleatorio para wander.
    /// </summary>
    private void SetNewWanderTarget()
    {
        Vector2 randomPos = Random.insideUnitCircle * wanderRadius;
        wanderTarget = homePosition + new Vector3(randomPos.x, randomPos.y, 0f);

        if (pathfinder != null)
        {
            var path = pathfinder.FindPath(transform.position, wanderTarget);
            if (path != null && path.Count > 0)
            {
                currentPath = path;
                currentPathIndex = 0;
                npcState = NPCState.Walking;
            }
            else
            {
                FallbackDirectMove(wanderTarget);
            }
        }
        else
        {
            FallbackDirectMove(wanderTarget);
        }

        wanderIntervalTimer = Random.Range(wanderIntervalMin, wanderIntervalMax);
    }

    #endregion

    #region Facing

    private void UpdateFacing()
    {
        if (spriteRenderer == null) return;

        if (npcState == NPCState.Walking && rb.linearVelocity.x != 0)
        {
            facingRight = rb.linearVelocity.x > 0;
            spriteRenderer.flipX = !facingRight;
        }

        // Look at player si está activo
        if (lookAtPlayer)
        {
            GameObject player = GameObject.FindGameObjectWithTag("Player");
            if (player != null)
            {
                float dist = Vector2.Distance(transform.position, player.transform.position);
                if (dist <= awarenessRange * 1.5f)
                {
                    spriteRenderer.flipX = player.transform.position.x < transform.position.x;
                }
            }
        }
    }

    private bool facingRight = true;

    #endregion

    #region Interaction

    /// <summary>
    /// Llamado cuando el jugador presiona el botón de interacción cerca del NPC.
    /// </summary>
    public void Interact(GameObject player)
    {
        if (!interactable) return;

        float dist = Vector2.Distance(transform.position, player.transform.position);
        if (dist > interactionRange) return;

        npcState = NPCState.Interacting;
        rb.linearVelocity = Vector2.zero;

        // Mirar al jugador
        if (player.transform.position.x < transform.position.x)
            spriteRenderer.flipX = true;
        else
            spriteRenderer.flipX = false;

        // Trigger diálogo
        if (!string.IsNullOrEmpty(dialogueText))
        {
            OnDialogueRequested?.Invoke(dialogueText);
        }

        OnInteraction?.Invoke();
    }

    /// <summary>
    /// Llama a este método para terminar la interacción.
    /// </summary>
    public void EndInteraction()
    {
        if (npcState == NPCState.Interacting)
        {
            npcState = NPCState.Idle;
        }
    }

    #endregion

    #region Gizmos

    private void OnDrawGizmosSelected()
    {
        if (!showGizmos) return;

        // Dibujar waypoints
        if (waypoints != null && waypoints.Length > 0)
        {
            Gizmos.color = waypointColor;
            for (int i = 0; i < waypoints.Length; i++)
            {
                if (waypoints[i] != null)
                {
                    Gizmos.DrawWireSphere(waypoints[i].position, 0.3f);
                    if (i > 0 && waypoints[i - 1] != null)
                    {
                        Gizmos.DrawLine(waypoints[i - 1].position, waypoints[i].position);
                    }
                    if (loopPath && i == waypoints.Length - 1 && waypoints[0] != null)
                    {
                        Gizmos.DrawLine(waypoints[i].position, waypoints[0].position);
                    }
                }
            }
        }

        // Rango de interacción
        if (interactable)
        {
            Gizmos.color = interactionColor;
            Gizmos.DrawWireSphere(transform.position, interactionRange);
        }

        // Rango de detección
        Gizmos.color = new Color(1f, 1f, 0f, 0.15f);
        Gizmos.DrawWireSphere(transform.position, awarenessRange);

        // Radio de wander
        if (npcType == NPCBehaviorType.Wander)
        {
            Gizmos.color = new Color(0.5f, 0.8f, 0.5f, 0.15f);
            Gizmos.DrawWireSphere(homePosition, wanderRadius);
        }
    }

    #endregion
}