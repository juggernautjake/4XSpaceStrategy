using UnityEngine;

public class SystemTester : MonoBehaviour
{
    [Header("References")]
    public GameManager gameManager;        // Assign in Inspector

    private void Update()
    {
        // R regenerates the galaxy — DEV MODE only (never in normal player mode).
        if (GameMode.DevMode && Input.GetKeyDown(KeyCode.R))
        {
            if (gameManager != null)
                gameManager.GenerateStartingSystem();
            else
                Debug.Log("Assign GameManager in SystemTester!");
        }
    }
}