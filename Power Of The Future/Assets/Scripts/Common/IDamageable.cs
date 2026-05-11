/// <summary>
/// Interfaz para cualquier entidad que pueda recibir daño.
/// Implementar esta interfaz en el jugador, enemigos, NPCs, objetos destructibles, etc.
/// </summary>
public interface IDamageable
{
    /// <summary>
    /// Aplica daño a la entidad.
    /// </summary>
    /// <param name="amount">Cantidad de daño a recibir.</param>
    void TakeDamage(int amount);

    /// <summary>
    /// Indica si la entidad está muerta.
    /// </summary>
    bool IsDead { get; }
}