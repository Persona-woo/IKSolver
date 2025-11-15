using UnityEngine;

public class NewMonoBehaviourScript : MonoBehaviour
{
    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
       Debug.Log("Test, you should only see this at the start of the game");
       //create an empty game object named with "test, you should only see this at the start of the game"
       GameObject emptyObject = new GameObject("test, you should only see this at the start of the game");        
    }

    // Update is called once per frame
    void Update()
    {
        
    }
}
