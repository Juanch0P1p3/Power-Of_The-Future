using UnityEngine;

public class NewMonoBehaviourScript : MonoBehaviour
{

    public float speed = 5; // velocidad del personaje al correr. Como es un public float, el valor puede cambiarse desde el apartado de Unity.

    Rigidbody2D rb2D; // Variable del RigidBody
    Vector2 movementInput; // Variable de x/y

    private Animator animator; //Variable del componente animator

    void Start()
    {
        rb2D = GetComponent<Rigidbody2D>(); // Se asigna el componente Rigidbody2D al script
        animator = GetComponent<Animator>(); // Se asigna el componente animator del jugador al script
    }

  
    void Update()
    {

        movementInput.x = Input.GetAxisRaw("Horizontal"); //Toma el vector 2 de x y x tiene que ser igual al valor que genera el Input de las teclas A y D. Si el valor es -1, es porque se pulsa la tecla A y el personaje irá a la izquierda; si el valor es 1, se esta pulsando la tecla D y el personaje va hacia la derecha.
        movementInput.y = Input.GetAxisRaw("Vertical"); //Lo mismo del eje x, solo que ahora en el eje Y. Este comando responde a las teclas W y S.

        movementInput = movementInput.normalized; //Esto permite que la velocidad del jugador a todas las direcciones sea la misma.

        animator.SetFloat("Horizontal", Mathf.Abs(movementInput.x)); //Se toma el parámetro "Horizontal" del animator controller para relacionarlo con el movimiento en el vector x, cuando x pasa de -1 a 1, etc.
        animator.SetFloat("Vertical", Mathf.Abs(movementInput.y)); //Lo mismo pero con el vector Y.

        CheckFlip(); //Se llama el método al UpDate para que la comprobación se haga en cada frame.
    }

    private void FixedUpdate() //Es un método que se ejecuta en tiempos de intérvalos fijos.
    {
        rb2D.linearVelocity = movementInput * speed; // Tomamos la velocidad del componente rb2D e indicamos que debe ser igual al vector 2 por la velocidad que establecimos.
    }

    void CheckFlip() // Método para comprobar que el personaje se da la vuelta
    {
        if (movementInput.x > 0 && transform.localScale.x < 0 || movementInput.x < 0 && transform.localScale.x > 0) //se hace comprobación de que el movementInput sea mayor a 0 y que la escala sea menor a 0
        {
            transform.localScale = new Vector3(transform.localScale.x * -1, transform.localScale.y, transform.localScale.z); //Se transforma el valor de x, pero los demás vectores se dejan intactos.
        }
    }
}
