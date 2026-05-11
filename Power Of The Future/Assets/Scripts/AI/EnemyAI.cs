using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// Comportamiento de IA para enemigos. Implementa detección del jugador,
/// persecución con pathfinding A*, y patrulla básica.
/// </summary>
[RequireComponent(typeof(Rigidbody2D))]
public class EnemyAI : MonoBehaviour
{
    // Estados del Enemigo
    public enum EnemyState
    {
        Patrolling,
        Chasing,
        Returning,
        Attacking,
        Stunned,
        Dead
    }

    [Header("Detección")]
    [Tooltip("Rango de detección del jugador")]
    public float detectionRange = 6f;

    [Tooltip("Ángulo de visión del enemigo (grados)")]
    public float detectionAngle = 120f;

    [Tooltip("Layer del jugador para detección por raycast")]
    public LayerMask playerLayer;

    [Tooltip("Capas que bloquean la línea de visión")]
    public LayerMask sightBlockingLayers;

    [Header("Persecución")]
    [Tooltip("Distancia mínima de ataque")]
    public float attackRange = 1.2f;

    [Tooltip("Velocidad de persecución")]
    public float chaseSpeed = 4f;

    [Tooltip("Velocidad de patrulla")]
    public float patrolSpeed = 2f;

    [Tooltip("Velocidad de retorno a punto de patrulla")]
    public float returnSpeed = 3f;

    [Header("Patrulla")]
    [Tooltip("Si es true, patrulla en un radio definido. Si es false, sigue waypoints.")]
    public bool usePatrolRadius = true;

    [Tooltip("Radio de patrulla (si usePatrolRadius = true)")]
    public float patrolRadius = 5f;

    [Tooltip("Waypoints para patrulla (si usePatrolRadius = false)")]
    public Transform[] patrolWaypoints;

    [Tooltip("Tiempo de espera en cada waypoint (segundos)")]
    public float waypointWaitTime = 1.5f;

    [Header("Ataque")]
    [Tooltip("Daño del enemigo")]
    public int attackDamage = 10;

    [Tooltip("Cooldown entre ataques (segundos)")]
    public float attackCooldown = 1.5f;

    [Tooltip("Duración del ataque (segundos)")]
    public float attackDuration = 0.4f;

    [Tooltip("Fuerza de knockback al golpear")]
    public float knockbackForce = 5f;

    [Header("Vida")]
    public int maxHealth = 50;

    [Tooltip("Si el enemigo puede ser eliminado")]
    public bool canDie = true;

    [Tooltip("Tiempo en segundos antes de desaparecer tras morir")]
    public float destroyDelay = 2f;

    [Header("Debug & Feedback")]
    public bool showDetectionGizmos = true;
    public Color gizmoDetectionColor = Color.yellow;
    public Color gizmoAttackColor = Color.red;

    // --- Estado interno ---
    private EnemyState currentState;
    private float stateTimer;
    private float attackCooldownTimer;
    private float waypointWaitTimer;
    private int currentWaypointIndex;

    // Posición de inicio para retorno
    private Vector3 homePosition;

    // Detección del jugador
    private GameObject currentTarget;
    private bool playerInSight;
    private bool playerInRange;

    // Referencias
    private new Rigidbody2D rigidbody2D;
    private SpriteRenderer spriteRenderer;
    private Animator animator;

    // Pathfinding
    private AStarPathfinder pathfinder;
    private List<Vector3> currentPath;
    private int currentPathIndex;
    private float pathRecalcTimer;

    // Smoothing
    private Vector2 currentVelocity;
    private bool facingRight = true;

    public EnemyState CurrentState => currentState;
    public bool IsDead => currentState == EnemyState.Dead;
    public Vector3 HomePosition => homePosition;

    public System.Action<EnemyState, EnemyState> OnStateChanged;
    public System.Action<int> OnDamaged;
    public System.Action OnDeath;

    #region Lifecycle

    private void Awake()
    {
        rigidbody2D = GetComponent<Rigidbody2D>();
        spriteRenderer = GetComponent<SpriteRenderer>();
        animator = GetComponent<Animator>();

        homePosition = transform.position;
        attackCooldownTimer = attackCooldown;

        ChangeState(EnemyState.Patrolling);
    }

    private void Start()
    {
        if (PathfindingGrid.Instance != null)
        {
            pathfinder = new AStarPathfinder(PathfindingGrid.Instance);
        }
        else
        {
            Debug.LogWarning($"[EnemyAI] {gameObject.name}: No se encontró PathfindingGrid en la escena.");
        }
    }

    private void Update()
    {
        attackCooldownTimer -= Time.deltaTime;
        pathRecalcTimer -= Time.deltaTime;
        stateTimer -= Time.deltaTime;
        waypointWaitTimer -= Time.deltaTime;

        // Debug: tecla para matar enemigo
        if (Input.GetKeyDown(KeyCode.K))
        {
            TakeDamage(maxHealth);
        }
    }

    private void FixedUpdate()
    {
        switch (currentState)
        {
            case EnemyState.Patrolling:
                UpdatePatrol();
                break;
            case EnemyState.Chasing:
                UpdateChase();
                break;
            case EnemyState.Returning:
                UpdateReturn();
                break;
            case EnemyState.Attacking:
                UpdateAttack();
                break;
            case EnemyState.Stunned:
                UpdateStunned();
                break;
        }
    }

    #endregion

    #region State Machine

    private void ChangeState(EnemyState newState)
    {
        if (currentState == newState) return;

        EnemyState oldState = currentState;
        currentState = newState;
        stateTimer = 0f;

        OnExitState(oldState);
        OnEnterState(newState);

        OnStateChanged?.Invoke(oldState, newState);
    }

    private void OnEnterState(EnemyState state)
    {
        switch (state)
        {
            case EnemyState.Patrolling:
                currentPath?.Clear();
                break;

            case EnemyState.Chasing:
                currentSpeed = chaseSpeed;
                break;

            case EnemyState.Returning:
                currentSpeed = returnSpeed;
                if (pathfinder != null)
                {
                    var path = pathfinder.FindPath(transform.position, homePosition);
                    SetPath(path);
                }
                break;

            case EnemyState.Attacking:
                rigidbody2D.linearVelocity = Vector2.zero;
                stateTimer = attackDuration;
                // Aquí se dispararía una animación de ataque
                if (animator != null)
                    animator.SetTrigger("Attack");
                break;

            case EnemyState.Stunned:
                rigidbody2D.linearVelocity = Vector2.zero;
                break;
        }
    }

    private void OnExitState(EnemyState state)
    {
        switch (state)
        {
            case EnemyState.Attacking:
                break;
        }
    }

    #endregion

    #region Detection

    /// <summary>
    /// Detecta al jugador por rango y línea de visión.
    /// </summary>
    private bool DetectPlayer()
    {
        if (currentTarget == null)
        {
            // Buscar jugador en el rango
            Collider2D[] hits = Physics2D.OverlapCircleAll(transform.position, detectionRange, playerLayer);
            foreach (var hit in hits)
            {
                if (hit.CompareTag("Player"))
                {
                    currentTarget = hit.gameObject;
                    break;
                }
            }
        }

        if (currentTarget == null)
        {
            playerInSight = false;
            playerInRange = false;
            return false;
        }

        float dist = Vector2.Distance(transform.position, currentTarget.transform.position);
        playerInRange = dist <= detectionRange;

        if (!playerInRange)
        {
            currentTarget = null;
            playerInSight = false;
            return false;
        }

        // Verificar ángulo de visión
        Vector2 directionToTarget = ((Vector2)(currentTarget.transform.position - transform.position)).normalized;
        float angleToTarget = Vector2.Angle(transform.right, directionToTarget);
        if (Vector3.Cross(transform.right, directionToTarget).z < 0)
            angleToTarget = -angleToTarget;

        if (Mathf.Abs(angleToTarget) > detectionAngle / 2f)
        {
            playerInSight = false;
            return false;
        }

        // Verificar línea de visión con raycast
        RaycastHit2D sightHit = Physics2D.Raycast(
            transform.position,
            directionToTarget,
            dist,
            sightBlockingLayers
        );

        playerInSight = !sightHit;
        return playerInSight;
    }

    #endregion

    #region Pathfinding Movement

    private float currentSpeed;

    private void SetPath(List<Vector3> path)
    {
        currentPath = path;
        currentPathIndex = 0;
    }

    private void MoveAlongCurrentPath(float speed)
    {
        if (currentPath == null || currentPath.Count == 0 || currentPathIndex >= currentPath.Count)
            return;

        Vector3 targetWaypoint = currentPath[currentPathIndex];
        Vector2 direction = ((Vector2)(targetWaypoint - transform.position)).normalized;

        // Move towards waypoint
        Vector2 desiredVelocity = direction * speed;
        rigidbody2D.linearVelocity = desiredVelocity;

        // Check if reached waypoint
        float dist = Vector2.Distance(transform.position, targetWaypoint);
        if (dist < 0.15f)
        {
            currentPathIndex++;
        }

        // Update facing
        if (direction.x != 0)
        {
            facingRight = direction.x > 0;
            if (spriteRenderer != null)
                spriteRenderer.flipX = !facingRight;
        }
    }

    private bool RecalculatePathToTarget()
    {
        if (pathfinder == null || currentTarget == null) return false;

        if (pathRecalcTimer <= 0f)
        {
            pathRecalcTimer = 0.3f;
            var path = pathfinder.FindPath(transform.position, currentTarget.transform.position);
            if (path != null && path.Count > 0)
            {
                SetPath(path);
                return true;
            }
        }
        return HasValidPath();
    }

    private bool HasValidPath()
    {
        return currentPath != null && currentPath.Count > 0 && currentPathIndex < currentPath.Count;
    }

    #endregion

    #region State Updates

    private void UpdatePatrol()
    {
        if (DetectPlayer())
        {
            ChangeState(EnemyState.Chasing);
            return;
        }

        if (usePatrolRadius)
        {
            // Patrulla en radio: mover en círculo o aleatorio
            PatrolRadiusMovement();
        }
        else
        {
            // Patrulla por waypoints
            PatrolWaypointsMovement();
        }
    }

    private void PatrolRadiusMovement()
    {
        if (currentPath == null || currentPath.Count == 0 || currentPathIndex >= currentPath.Count)
        {
            // Generar punto aleatorio dentro del radio
            Vector2 randomDir = Random.insideUnitCircle.normalized;
            float randomDist = Random.Range(patrolRadius * 0.3f, patrolRadius);
            Vector3 targetPoint = homePosition + new Vector3(randomDir.x, randomDir.y, 0f) * randomDist;

            if (pathfinder != null)
            {
                var path = pathfinder.FindPath(transform.position, targetPoint);
                if (path != null && path.Count > 0)
                {
                    SetPath(path);
                    currentSpeed = patrolSpeed;
                }
            }

            stateTimer = patrolRadius; // Tiempo antes de nuevo punto
        }

        if (HasValidPath())
        {
            MoveAlongCurrentPath(patrolSpeed);
        }

        if (stateTimer <= 0f)
        {
            currentPath?.Clear();
        }
    }

    private void PatrolWaypointsMovement()
    {
        if (patrolWaypoints == null || patrolWaypoints.Length == 0)
            return;

        if (!HasValidPath())
        {
            // Ir al siguiente waypoint
            Transform targetWP = patrolWaypoints[currentWaypointIndex % patrolWaypoints.Length];
            if (pathfinder != null)
            {
                var path = pathfinder.FindPath(transform.position, targetWP.position);
                if (path != null)
                {
                    SetPath(path);
                    currentSpeed = patrolSpeed;
                }
            }
        }

        if (HasValidPath())
        {
            MoveAlongCurrentPath(patrolSpeed);

            // Verificar si llegó al waypoint
            float distToWP = Vector2.Distance(transform.position, patrolWaypoints[currentWaypointIndex % patrolWaypoints.Length].position);
            if (distToWP < 0.3f)
            {
                if (waypointWaitTimer <= 0f)
                {
                    currentWaypointIndex = (currentWaypointIndex + 1) % patrolWaypoints.Length;
                    currentPath?.Clear();
                    waypointWaitTimer = waypointWaitTime;
                }
            }
        }
    }

    private void UpdateChase()
    {
        // Verificar si el jugador ya no está en rango
        if (!DetectPlayer() || currentTarget == null)
        {
            ChangeState(EnemyState.Returning);
            return;
        }

        // Verificar si está en rango de ataque
        float distToTarget = Vector2.Distance(transform.position, currentTarget.transform.position);
        if (distToTarget <= attackRange && attackCooldownTimer <= 0f)
        {
            ChangeState(EnemyState.Attacking);
            return;
        }

        // Perseguir con pathfinding
        if (RecalculatePathToTarget())
        {
            MoveAlongCurrentPath(chaseSpeed);
        }
        else
        {
            // Fallback: mover directamente hacia el target (sin pathfinding)
            Vector2 direction = ((Vector2)(currentTarget.transform.position - transform.position)).normalized;
            rigidbody2D.linearVelocity = direction * chaseSpeed;
        }
    }

    private void UpdateReturn()
    {
        // Verificar si el jugador volvió a ser visible
        if (DetectPlayer())
        {
            ChangeState(EnemyState.Chasing);
            return;
        }

        // Seguir ruta de regreso
        if (HasValidPath())
        {
            MoveAlongCurrentPath(returnSpeed);

            // Verificar si llegó a casa
            float distToHome = Vector2.Distance(transform.position, homePosition);
            if (distToHome < 0.3f)
            {
                ChangeState(EnemyState.Patrolling);
            }
        }
        else
        {
            // Ya llegó o no hay ruta
            float distToHome = Vector2.Distance(transform.position, homePosition);
            if (distToHome < 0.5f)
            {
                ChangeState(EnemyState.Patrolling);
                rigidbody2D.linearVelocity = Vector2.zero;
            }
        }
    }

    private void UpdateAttack()
    {
        // Se ejecuta durante attackDuration
        if (stateTimer <= 0f)
        {
            attackCooldownTimer = attackCooldown;

            // Volver a perseguir o retornar
            if (DetectPlayer() && currentTarget != null)
            {
                ChangeState(EnemyState.Chasing);
            }
            else
            {
                ChangeState(EnemyState.Returning);
            }
        }
    }

    private void UpdateStunned()
    {
        rigidbody2D.linearVelocity = Vector2.zero;

        if (stateTimer <= 0f)
        {
            if (DetectPlayer())
                ChangeState(EnemyState.Chasing);
            else
                ChangeState(EnemyState.Patrolling);
        }
    }

    #endregion

    #region Combat

    public void TakeDamage(int damage)
    {
        if (currentState == EnemyState.Dead) return;

        maxHealth -= damage;
        OnDamaged?.Invoke(damage);

        // Efecto visual de daño (flash)
        StartCoroutine(FlashDamage());

        if (maxHealth <= 0 && canDie)
        {
            Die();
        }
        else
        {
            // Aturdir brevemente
            StartCoroutine(Stun(0.3f));
        }
    }

    private System.Collections.IEnumerator FlashDamage()
    {
        if (spriteRenderer != null)
        {
            spriteRenderer.color = Color.red;
            yield return new WaitForSeconds(0.15f);
            spriteRenderer.color = Color.white;
        }
    }

    private System.Collections.IEnumerator Stun(float duration)
    {
        ChangeState(EnemyState.Stunned);
        stateTimer = duration;
        yield return new WaitForSeconds(duration);
    }

    private void Die()
    {
        ChangeState(EnemyState.Dead);
        rigidbody2D.simulated = false;
        GetComponent<Collider2D>().enabled = false;

        // Desactivar visualmente
        if (spriteRenderer != null)
            spriteRenderer.enabled = false;

        OnDeath?.Invoke();

        Destroy(gameObject, destroyDelay);
    }

    /// <summary>
    /// Llamado por un trigger de ataque para aplicar daño al jugador.
    /// </summary>
    public void OnAttackHit(Collider2D other)
    {
        if (other.CompareTag("Player"))
        {
            // Buscar cualquier componente que implemente IDamageable
            var health = other.GetComponent<IDamageable>();

            if (health != null && !health.IsDead)
            {
                health.TakeDamage(attackDamage);

                // Aplicar knockback
                var playerRb = other.GetComponent<Rigidbody2D>();
                if (playerRb != null)
                {
                    Vector2 knockbackDir = ((Vector2)(other.transform.position - transform.position)).normalized;
                    playerRb.AddForce(knockbackDir * knockbackForce, ForceMode2D.Impulse);
                }
            }
        }
    }

    #endregion

    #region Setters

    public void SetHomePosition(Vector3 position)
    {
        homePosition = position;
    }

    public void SetPatrolWaypoints(Transform[] waypoints)
    {
        patrolWaypoints = waypoints;
        currentWaypointIndex = 0;
    }

    #endregion

    #region Gizmos

    private void OnDrawGizmosSelected()
    {
        if (!showDetectionGizmos) return;

        // Rango de detección
        Gizmos.color = gizmoDetectionColor;
        Gizmos.DrawWireSphere(transform.position, detectionRange);

        // Ángulo de visión
        Vector3 left = Quaternion.Euler(0, 0, detectionAngle / 2f) * transform.right * detectionRange;
        Vector3 right = Quaternion.Euler(0, 0, -detectionAngle / 2f) * transform.right * detectionRange;
        Gizmos.color = new Color(gizmoDetectionColor.r, gizmoDetectionColor.g, gizmoDetectionColor.b, 0.2f);
        Gizmos.DrawLine(transform.position, transform.position + left);
        Gizmos.DrawLine(transform.position, transform.position + right);

        // Rango de ataque
        Gizmos.color = gizmoAttackColor;
        Gizmos.DrawWireSphere(transform.position, attackRange);

        // Posición home
        Gizmos.color = Color.blue;
        Gizmos.DrawWireSphere(homePosition, 0.3f);
        Gizmos.DrawLine(homePosition, homePosition + Vector3.up * 0.5f);

        // Waypoints de patrulla
        if (!usePatrolRadius && patrolWaypoints != null)
        {
            Gizmos.color = Color.cyan;
            for (int i = 0; i < patrolWaypoints.Length; i++)
            {
                if (patrolWaypoints[i] != null)
                {
                    Gizmos.DrawWireSphere(patrolWaypoints[i].position, 0.3f);
                    if (i < patrolWaypoints.Length - 1)
                    {
                        Gizmos.DrawLine(patrolWaypoints[i].position, patrolWaypoints[i + 1].position);
                    }
                }
            }
        }
    }

    #endregion
}