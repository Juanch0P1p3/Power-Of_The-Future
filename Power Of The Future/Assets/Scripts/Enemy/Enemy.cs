////////////////////////////////////////////////////////////////////////////////
// OBSOLETO: Este script se mantiene solo por compatibilidad.
// Reemplazar el componente Enemy de este GameObject con "EnemyAI" (en Assets/Scripts/AI/).
// EnemyAI incluye pathfinding A*, detección de jugador, persecución, patrulla y más.
////////////////////////////////////////////////////////////////////////////////

using UnityEngine;

/// <summary>
/// [DEPRECATED] Use EnemyAI en su lugar.
/// EnemyIA proporciona:
/// - Pathfinding A* respetando tiles de agua y colliders
/// - Detección del jugador (rango + ángulo de visión + raycast)
/// - Patrulla por waypoints o radio
/// - Ataque con knockback
/// - Retorno a posición inicial
/// </summary>
[System.Obsolete("Usar EnemyAI en su lugar. Véase Assets/Scripts/AI/EnemyAI.cs")]
public class Enemy : MonoBehaviour
{
    // Se mantiene vacío para no romper referencias en la escena durante la migración.
    // Simplemente remueva este componente y añada EnemyAI.
    void Start()
    {
        Debug.LogWarning("[Enemy DEPRECATED] Este script está obsoleto. " +
            "Remueva este componente y añada 'EnemyAI' (Assets/Scripts/AI/EnemyAI.cs) al GameObject.",
            this.gameObject);
    }

    void Update()
    {
        // Dejar vacío — la funcionalidad ahora está en EnemyAI.
    }
}
