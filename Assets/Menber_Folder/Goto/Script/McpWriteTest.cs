using UnityEngine;

public class McpWriteTest : MonoBehaviour
{
    [SerializeField] private string message = "MCP write test";

    private void Start()
    {
        Debug.Log(message);
    }
}
