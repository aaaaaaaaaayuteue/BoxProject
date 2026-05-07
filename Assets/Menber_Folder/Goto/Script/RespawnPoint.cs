using UnityEngine;

// リスポーン地点として動作するスクリプト
// プレイヤー(白箱)が触れたら、GameManager に「ここを現在のリスポーン地点として登録して」と通知する
//
// 新仕様(入れ替えゲーム)対応: 触れたオブジェクトが BoxController で IsPlayerBody=true なら通知
// 旧仕様(犬連れフリスビーゲーム)対応: PlayerController でも通知
[RequireComponent(typeof(Collider2D))]
public class RespawnPoint : MonoBehaviour
{
    [Header("通知先 GameManager")]
    [SerializeField] private GameManager gameManager;


    // ーーープレイヤーが触れた時の処理ーーー

    private void OnTriggerEnter2D(Collider2D other)
    {
        if (gameManager == null)
        {
            return;
        }

        // 新仕様: 現在のプレイヤー本体(BoxController.IsPlayerBody=true)なら通知
        BoxController box = other.GetComponentInParent<BoxController>();
        if (box != null && box.IsPlayerBody)
        {
            gameManager.UpdateCurrentRespawnPoint(transform);
            return;
        }

        // 旧仕様: PlayerController(有効な状態)なら通知
        PlayerController player = other.GetComponentInParent<PlayerController>();
        if (player != null && player.enabled)
        {
            gameManager.UpdateCurrentRespawnPoint(transform);
        }
    }
}
