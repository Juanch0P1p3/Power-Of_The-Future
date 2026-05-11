using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// Base para toda entidad con IA en el juego.
/// Provee pathfinding, movimiento suave por waypoints, estados de IA,
/// y detección de obstáculos. Funciona tanto para enemigos como NPCs.
/// </summary>
[RequireComponent(typeof(Rigidbody2D))]
public abstract class AIEntity : MonoBehaviour
{
    [Header("Movimiento")]
    [Tooltip("Velocidad base de movimiento")]
    public float moveSpeed = 3f;

    [Tooltip("Velocidad al rotar/interpolar dirección (grados/segundo)")]
    public float rotationSpeed = 720f;

    [Tooltip("Distancia para considerar que se llegó a un waypoint")]
    public float waypointTolerance = 0.15f;

    [Tooltip("Frecuencia de recalculación de ruta (segundos). 0 = cada frame.")]
    public float pathRecalculationRate = 0.3f;

    [Header("Colisión y Navegación")]
    [Tooltip("Radio para detección de obstáculos (usa Physics2D para detectar colisiones fuera del grid)")]
    public float obstacleCheckRadius = 0.4f;

    [Tooltip("Capas a considerar como obstáculos en detección de colisión")]
    public LayerMask obstacleLayers = ~0; // Todo por defecto

    [Header("Debug")]
    public bool showPathGizmos = true;
    public Color pathColor = Color.yellow;

    // --- Estado interno ---
    protected Rigidbody2D rb;
    protected Animator animator;
    protected SpriteRenderer spriteRenderer;

    protected List<Vector3> currentPath;
    protected int currentWaypointIndex;
    protected bool hasPath;

    protected Vector2 movementDirection;
    protected float currentSpeed;

    private float pathRecalcTimer;
    private Vector2Int lastTargetGridPos;
    private float stuckTime;
    private Vector2 lastPosition;

    // Propiedades accesibles por subclases
    public bool IsMoving => movementDirection.sqrMagnitude > 0.01f;
    public bool HasPath => hasPath && currentPath != null && currentPath.Count > 0;
    public Vector2 MovementDirection => movementDirection;
    public float CurrentSpeed => currentSpeed;

    // Estados de IA
    public enum AIState
    {
        Idle,
        Moving,
        Chasing,
        Attacking,
        Patrolling,
        Fleeing,
        Interacting,
        Stunned,
        Dead
    }

    public AIState CurrentState { get; protected set; } = AIState.Idle;
    public AIState PreviousState { get; protected set; }

    // Eventos para subclases
    public System.Action<AIState, AIState> OnStateChanged;

    #region Lifecycle

    protected virtual void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
        animator = GetComponent<Animator>();
        spriteRenderer = GetComponent<SpriteRenderer>();

        if (rb == null)
        {
            Debug.LogError($"[AIEntity] {gameObject.name} necesita un Rigidbody2D.");
        }

        currentPath = new List<Vector3>();
        currentSpeed = moveSpeed;
    }

    protected virtual void Update()
    {
        HandlePathRecalculation();
        UpdateMovementAnimation();
    }

    protected virtual void FixedUpdate()
    {
        MoveAlongPath();
    }

    #endregion

    #region Pathfinding

    /// <summary>
    /// Solicita una ruta al pathfinder. Se puede llamar desde subclases.
    /// </summary>
    protected bool RequestPath(Vector2 targetWorldPos)
    {
        if (PathfindingGrid.Instance == null)
        {
            Debug.LogWarning($"[AIEntity] No hay PathfindingGrid en la escena.");
            return false;
        }

        var pathfinder = new AStarPathfinder(PathfindingGrid.Instance);
        var path = pathfinder.FindPath(transform.position, targetWorldPos);

        if (path != null && path.Count > 0)
        {
            SetPath(path);
            lastTargetGridPos = PathfindingGrid.Instance.WorldToGrid(targetWorldPos);
            return true;
        }

        ClearPath();
        return false;
    }

    /// <summary>
    /// Establece una nueva ruta para seguirla.
    /// </summary>
    protected void SetPath(List<Vector3> path)
    {
        currentPath = path;
        currentWaypointIndex = 0;
        hasPath = path != null && path.Count > 0;
        stuckTime = 0f;

        if (hasPath)
        {
            ChangeState(AIState.Moving);
        }
    }

    /// <summary>
    /// Limpia la ruta actual.
    /// </summary>
    protected void ClearPath()
    {
        currentPath.Clear();
        currentWaypointIndex = 0;
        hasPath = false;
        movementDirection = Vector2.zero;
    }

    /// <summary>
    /// Recalcula la ruta si el target se ha movido sustancialmente.
    /// </summary>
    private void HandlePathRecalculation()
    {
        pathRecalcTimer -= Time.deltaTime;

        if (pathRecalcTimer <= 0f && hasPath && currentTarget != null)
        {
            pathRecalcTimer = pathRecalculationRate;
            var targetGridPos = PathfindingGrid.Instance.WorldToGrid(currentTarget.position);

            // Recalcular si el target se movió más de 1 celda
            if (Vector2Int.Distance(targetGridPos, lastTargetGridPos) >= 1)
            {
                RequestPath(currentTarget.position);
            }
        }
    }

    #endregion

    #region Movement

    /// <summary>
    /// Mueve la entidad a lo largo de la ruta actual.
    /// </summary>
    protected virtual void MoveAlongPath()
    {
        if (!hasPath || currentPath.Count == 0 || currentWaypointIndex >= currentPath.Count)
            return;

        Vector3 targetWaypoint = currentPath[currentWaypointIndex];
        Vector2 direction = ((Vector2)(targetWaypoint - transform.position)).normalized;

        // Verificar si estamos atascados
        float moved = Vector2.Distance(lastPosition, (Vector2)transform.position);
        if (moved < 0.001f)
        {
            stuckTime += Time.deltaTime;
            if (stuckTime > 1.0f)
            {
                // Posiblemente atascado, recalcular ruta
                stuckTime = 0f;
                if (currentTarget != null)
                {
                    RequestPath(currentTarget.position);
                }
            }
        }
        else
        {
            stuckTime = 0f;
        }
        lastPosition = transform.position;

        // Mover
        movementDirection = direction;
        rb.linearVelocity = direction * currentSpeed;

        // Verificar si llegamos al waypoint actual
        float distanceToWaypoint = Vector2.Distance(transform.position, targetWaypoint);
        if (distanceToWaypoint <= waypointTolerance)
        {
            currentWaypointIndex++;

            if (currentWaypointIndex >= currentPath.Count)
            {
                OnPathComplete();
            }
        }

        // Flip sprite según dirección horizontal
        if (spriteRenderer != null && direction.x != 0)
        {
            spriteRenderer.flipX = direction.x < 0;
        }
    }

    /// <summary>
    /// Llamado cuando la entidad llega al último waypoint de su ruta.
    /// </summary>
    protected virtual void OnPathComplete()
    {
        hasPath = false;
        movementDirection = Vector2.zero;
        rb.linearVelocity = Vector2.zero;
    }

    #endregion

    #region Obstacle Avoidance

    /// <summary>
    /// Detecta obstáculos cercanos usando raycasts y desvía el movimiento.
    /// </summary>
    protected Vector2 GetObstacleAvoidance(Vector2 desiredDirection)
    {
        if (desiredDirection.sqrMagnitude < 0.01f) return desiredDirection;

        float angle = Mathf.Atan2(desiredDirection.y, desiredDirection.x) * Mathf.Rad2Deg;

        // Castear rayos en abanico
        int numRays = 5;
        float halfAngle = 45f;
        float bestWeight = 0f;
        Vector2 bestDirection = desiredDirection;

        for (int i = 0; i < numRays; i++)
        {
            float rayAngle = angle - halfAngle + (halfAngle * 2f * i / (numRays - 1));
            Vector2 rayDir = new Vector2(Mathf.Cos(rayAngle * Mathf.Deg2Rad), Mathf.Sin(rayAngle * Mathf.Deg2Rad));

            RaycastHit2D hit = Physics2D.Raycast(transform.position, rayDir, obstacleCheckRadius, obstacleLayers);

            float distance = hit.collider != null ? hit.distance : obstacleCheckRadius;
            float weight = distance / obstacleCheckRadius;

            if (weight > bestWeight)
            {
                bestWeight = weight;
                bestDirection = rayDir;
            }
        }

        return Vector2.Lerp(desiredDirection, bestDirection.normalized, 1f - bestWeight);
    }

    #endregion

    #region State Machine

    /// <summary>
    /// Cambia el estado de IA actual.
    /// </summary>
    protected void ChangeState(AIState newState)
    {
        if (CurrentState == newState) return;

        PreviousState = CurrentState;
        AIState oldState = CurrentState;
        CurrentState = newState;

        OnExitState(oldState);
        OnEnterState(newState);

        OnStateChanged?.Invoke(oldState, newState);
    }

    /// <summary>
    /// Llamado al entrar en un nuevo estado.
    /// </summary>
    protected virtual void OnEnterState(AIState state)
    {
        switch (state)
        {
            case AIState.Idle:
                movementDirection = Vector2.zero;
                if (rb != null) rb.linearVelocity = Vector2.zero;
                break;
            case AIState.Moving:
                break;
            case AIState.Chasing:
                break;
            case AIState.Attacking:
                movementDirection = Vector2.zero;
                if (rb != null) rb.linearVelocity = Vector2.zero;
                break;
            case AIState.Patrolling:
                break;
            case AIState.Fleeing:
                break;
            case AIState.Interacting:
                movementDirection = Vector2.zero;
                if (rb != null) rb.linearVelocity = Vector2.zero;
                break;
        }
    }

    /// <summary>
    /// Llamado al salir de un estado.
    /// </summary>
    protected virtual void OnExitState(AIState state) { }

    #endregion

    #region Animation

    protected virtual void UpdateMovementAnimation()
    {
        if (animator == null) return;

        animator.SetFloat("Speed", movementDirection.magnitude * (currentSpeed / moveSpeed));
        animator.SetFloat("Horizontal", movementDirection.x);
        animator.SetFloat("Vertical", movementDirection.y);
    }

    #endregion

    #region Target

    // Target actual para pathfinding (enemigo, jugador, waypoint, etc.)
    protected Transform currentTarget;

    public void SetTarget(Transform target)
    {
        currentTarget = target;
    }

    public Transform GetTarget()
    {
        return currentTarget;
    }

    #endregion

    #region Gizmos

    protected virtual void OnDrawGizmos()
    {
        if (!showPathGizmos || currentPath == null || currentPath.Count < 2) return;

        Gizmos.color = pathColor;
        for (int i = 0; i < currentPath.Count - 1; i++)
        {
            Gizmos.DrawLine(currentPath[i], currentPath[i + 1]);
        }

        // Dibujar waypoints
        Gizmos.color = Color.cyan;
        for (int i = 0; i < currentPath.Count; i++)
        {
            Gizmos.DrawWireSphere(currentPath[i], 0.15f);
            if (i == currentWaypointIndex)
            {
                Gizmos.color = Color.green;
                Gizmos.DrawSphere(currentPath[i], 0.15f);
                Gizmos.color = Color.cyan;
            }
        }

        // Obstacle avoidance range
        Gizmos.color = new Color(1f, 0.5f, 0f, 0.2f);
        Gizmos.DrawWireSphere(transform.position, obstacleCheckRadius);
    }

    #endregion
}