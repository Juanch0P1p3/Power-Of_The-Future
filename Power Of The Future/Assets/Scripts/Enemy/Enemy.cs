using UnityEngine;

public class Enemy : MonoBehaviour
{

    public float speed = 5;

    private Rigidbody2D rb2D;

    public Transform targetTransform; //se asigna un punto al enemigo para que este vaya hacia él.

    void Start()
    {
       rb2D = GetComponent<Rigidbody2D>(); 
    }

    
    void Update()
    {
        rb2D.MovePosition(Vector2.MoveTowards(transform.position, targetTransform.position, speed*Time.deltaTime)); //Movemos al enemigo creando un vector2 el cual va a moverse cuando haya un target asignado; toma la posición del enemigo en el plano, luego esa posición debe transformarse a la posición del target y se toma la velocidad con la quel enemigo irá al target.
    }
}
