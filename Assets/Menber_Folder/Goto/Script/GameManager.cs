using System.Collections.Generic;
using UnityEngine;

// ゲーム全体のリスタート管理
// シーン内のリスポーン地点を管理し、プレイヤー死亡時に現在のリスポーン地点へ復帰させる
// 旧仕様: PlayerController を直接 Respawn する
// 新仕様: PossessionController がいる場合はそちらに委譲して、初期本体に戻す
public class GameManager : MonoBehaviour
{
    [Header("プレイヤー(旧仕様用、PossessionController使用時は無視される)")]
    [SerializeField] private PlayerController player;

    [Header("犬(旧仕様用、PossessionController使用時は無視される)")]
    [SerializeField] private DogController dog;

    [Header("憑依管理(指定するとこちらが優先される、入れ替えゲーム用)")]
    [SerializeField] private PossessionController possessionController;

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


    // ーーーリスタート実行(BoxController/PlayerController から呼ばれる)ーーー
    // プレイヤーが死亡から復帰するタイミングで呼ばれる
    // PossessionController があればそちらに委譲、なければ旧仕様の Player/Dog を Respawn する

    public void ExecuteRestart()
    {
        if (currentRespawnPoint == null)
        {
            // リスポーン地点未設定時の警告
            // BoxController 側にも hasRequestedRestart フラグがあるので、無限ループにはならないが
            // 「リスタートが発生しないまま箱が死亡状態のまま残る」状態になるので Inspector 設定漏れを通知する
            Debug.LogWarning("[GameManager] currentRespawnPoint が null です。リスタートできません。respawnsParent と子オブジェクトを Inspector で設定してください。", this);
            return;
        }

        // フリスビーは旧仕様用、シーンにあれば破棄(なければ何もしない)
        DestroyAllFrisbees();

        Vector2 respawnPos = currentRespawnPoint.position;

        // 新仕様: PossessionController があれば委譲
        if (possessionController != null)
        {
            possessionController.HandleRestart(respawnPos, respawnFacingDirection);
            return;
        }

        // 旧仕様: Player/Dog を直接 Respawn
        Vector2 dogRespawnPos = CalculateDogRespawnPosition(respawnPos);

        if (player != null)
        {
            player.Respawn(respawnPos, respawnFacingDirection);
        }

        if (dog != null)
        {
            dog.Respawn(dogRespawnPos);
        }
    }


    // ーーー犬のリスポーン位置を計算(旧仕様用)ーーー
    // プレイヤーの向きの反対側に、dogRespawnOffset だけ離れた位置

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


    // ーーーシーン内の全フリスビーを破棄(旧仕様用)ーーー

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
