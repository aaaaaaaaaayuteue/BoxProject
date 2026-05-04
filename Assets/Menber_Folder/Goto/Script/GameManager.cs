using System.Collections.Generic;
using UnityEngine;

// ゲーム全体のリスタート管理
// シーン内のリスポーン地点を管理し、プレイヤー死亡時に犬・プレイヤー・フリスビーをリセットする
public class GameManager : MonoBehaviour
{
    [Header("プレイヤー")]
    [SerializeField] private PlayerController player;

    [Header("犬")]
    [SerializeField] private DogController dog;

    [Header("リスポーン地点の親オブジェクト(子オブジェクトを上から順に取得)")]
    [SerializeField] private Transform respawnsParent;

    [Header("リスポーン時、犬がプレイヤーから離れる距離(マス、プレイヤーの後ろ側に配置)")]
    [SerializeField] private float dogRespawnOffset = 1f;

    [Header("リスポーン時のプレイヤーの向き(+1=右、-1=左、犬は反対側に配置される)")]
    [SerializeField] private int respawnFacingDirection = 1;

    // ーーーリスポーン地点リストーーー
    private List<Transform> respawnPoints = new List<Transform>();

    // ーーー現在のリスポーン地点ーーー
    private Transform currentRespawnPoint;


    // ーーーUnityイベントーーー

    private void Awake()
    {
        // リスポーン地点の親オブジェクトの子をすべて取得してリスト化
        // Hierarchy上の上から順番に格納される
        CollectRespawnPoints();

        // 初期リスポーン地点を設定(リストの一番上=最初の子オブジェクト)
        if (respawnPoints.Count > 0)
        {
            currentRespawnPoint = respawnPoints[0];
        }
    }


    // ーーーリスポーン地点の収集ーーー

    private void CollectRespawnPoints()
    {
        respawnPoints.Clear();

        if (respawnsParent == null)
        {
            return;
        }

        for (int i = 0; i < respawnsParent.childCount; i++)
        {
            respawnPoints.Add(respawnsParent.GetChild(i));
        }
    }


    // ーーー現在のリスポーン地点を更新(RespawnPointから呼ばれる)ーーー
    // プレイヤーがリスポーン地点に触れた時に通知される

    public void UpdateCurrentRespawnPoint(Transform newRespawnPoint)
    {
        currentRespawnPoint = newRespawnPoint;
    }


    // ーーーリスタート実行(PlayerControllerから呼ばれる)ーーー
    // プレイヤーが死亡から復帰するタイミングで呼ばれる
    // フリスビーを消し、プレイヤーと犬を現在のリスポーン地点に戻す

    public void ExecuteRestart()
    {
        if (currentRespawnPoint == null)
        {
            return;
        }

        // シーン内の全フリスビーを消す(将来複数本対応)
        DestroyAllFrisbees();

        // プレイヤーと犬の位置を計算
        Vector2 respawnPos = currentRespawnPoint.position;
        Vector2 dogRespawnPos = CalculateDogRespawnPosition(respawnPos);

        // プレイヤーをリスポーン
        if (player != null)
        {
            player.Respawn(respawnPos, respawnFacingDirection);
        }

        // 犬をリスポーン(プレイヤーの後ろ側)
        if (dog != null)
        {
            dog.Respawn(dogRespawnPos);
        }
    }


    // ーーー犬のリスポーン位置を計算ーーー
    // プレイヤーの向きの反対側に、dogRespawnOffsetだけ離れた位置

    private Vector2 CalculateDogRespawnPosition(Vector2 playerPos)
    {
        // プレイヤーが右向き(+1)なら犬は左(-1)、プレイヤーが左向き(-1)なら犬は右(+1)
        int dogDirection;
        if (respawnFacingDirection > 0)
        {
            dogDirection = -1;
        }
        else
        {
            dogDirection = 1;
        }

        return new Vector2(playerPos.x + (dogDirection * dogRespawnOffset), playerPos.y);
    }


    // ーーーシーン内の全フリスビーを破壊ーーー

    private void DestroyAllFrisbees()
    {
        FrisbeeController[] frisbees = FindObjectsByType<FrisbeeController>(FindObjectsSortMode.None);
        for (int i = 0; i < frisbees.Length; i++)
        {
            if (frisbees[i] != null)
            {
                Destroy(frisbees[i].gameObject);
            }
        }
    }
}