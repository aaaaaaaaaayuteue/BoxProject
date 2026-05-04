using UnityEngine;

// リスポーン地点として動作するスクリプト
// プレイヤーが触れたら、GameManagerに「ここを現在のリスポーン地点として登録して」と通知する
[RequireComponent(typeof(Collider2D))]
public class RespawnPoint : MonoBehaviour
{
    [Header("通知先のGameManager")]
    [SerializeField] private GameManager gameManager;


    // ーーープレイヤーが触れた時の処理ーーー

    private void OnTriggerEnter2D(Collider2D other)
    {
        if (gameManager == null)
        {
            return;
        }

        // 触れたのがプレイヤーかチェック
        PlayerController player = other.GetComponent<PlayerController>();
        if (player == null)
        {
            return;
        }

        // GameManagerに「ここが現在のリスポーン地点」と通知
        gameManager.UpdateCurrentRespawnPoint(transform);
    }
}