using UnityEngine;
using UnityEngine.InputSystem;

public class FrisbeeThrower : MonoBehaviour
{
    // ーーー参照ーーー
    [Header("プレイヤーコントローラー(向き取得用)")]
    [SerializeField] private PlayerController player;

    [Header("カーソルコントローラー(投擲目標取得用)")]
    [SerializeField] private CursorController cursor;

    [Header("フリスビーのPrefab")]
    [SerializeField] private GameObject frisbeePrefab;

    // ーーー投擲位置ーーー
    [Header("プレイヤーの中心からのフリスビー発射位置オフセット")]
    [SerializeField] private Vector2 spawnOffset = new Vector2(0f, 0.3f);

    [Header("壁として扱うレイヤー(生成位置がこのレイヤーの中ならプレイヤーX位置に補正)")]
    [SerializeField] private LayerMask groundLayer;

    // ーーー入力システム関連ーーー
    private PlayerInputActions inputActions;
    private InputAction throwAction;

    // ーーー状態管理ーーー
    // 現在飛んでいる/落ちているフリスビーを保持する
    // 1個しか存在しない仕様のため、参照を保持して連投を防ぐ
    private FrisbeeController currentFrisbee;


    // ーーーUnityイベントーーー

    private void Awake()
    {
        inputActions = new PlayerInputActions();
        throwAction = inputActions.Player.Throw;
    }

    private void OnEnable()
    {
        inputActions.Player.Enable();
    }

    private void OnDisable()
    {
        inputActions.Player.Disable();
    }

    private void Update()
    {
        // Throwボタンが押された瞬間にフリスビーを生成する
        if (throwAction.WasPressedThisFrame())
        {
            TryThrowFrisbee();
        }
    }


    // ーーー投擲試行ーーー
    // 連投不可仕様：既にフリスビーが存在する場合は何もしない

    private void TryThrowFrisbee()
    {
        // 既にフリスビーが存在しているなら投擲しない
        // currentFrisbeeがnullでない＝まだ前回のフリスビーが残っている
        if (currentFrisbee != null)
        {
            return;
        }

        // 必要な参照が揃っていなければ投擲しない
        if (player == null || cursor == null || frisbeePrefab == null)
        {
            return;
        }

        ThrowFrisbee();
    }


    // ーーーフリスビー生成と発射ーーー

    private void ThrowFrisbee()
    {
        // プレイヤーの向きに応じてspawnOffsetのX方向を反転させる
        // 右向き(+1)ならXはそのまま、左向き(-1)なら反転
        int direction = player.FacingDirection;
        Vector2 actualOffset = new Vector2(spawnOffset.x * direction, spawnOffset.y);
        Vector2 spawnPos = (Vector2)player.transform.position + actualOffset;

        // 生成位置が壁の中の場合、X座標だけプレイヤーの位置に補正(Yはオフセット維持)
        // (壁にめり込んだ状態で生成されないようにする)
        Collider2D wallHit = Physics2D.OverlapPoint(spawnPos, groundLayer);
        Debug.Log($"[FrisbeeThrower] spawnPos={spawnPos}, wallHit={wallHit}, playerPos={(Vector2)player.transform.position}");

        if (wallHit != null)
        {
            spawnPos = new Vector2(player.transform.position.x, spawnPos.y);
            Debug.Log($"[FrisbeeThrower] 補正後 spawnPos={spawnPos}, 補正後wallHit={Physics2D.OverlapPoint(spawnPos, groundLayer)}");
        }

        GameObject frisbeeObj = Instantiate(frisbeePrefab, spawnPos, Quaternion.identity);
        currentFrisbee = frisbeeObj.GetComponent<FrisbeeController>();

        // 投擲方向はプレイヤーの向き、目標X座標はカーソルの手前側面
        float targetX = cursor.NearSideX;

        // フリスビーを初期化して飛行開始
        currentFrisbee.Initialize(direction, targetX);
    }


    // ーーー外部公開プロパティーーー
    // 犬側のスクリプトがフリスビーの状態を参照するために使う(後の段階で使用)
    public FrisbeeController CurrentFrisbee => currentFrisbee;


    // ーーーGizmos(発射位置の可視化)ーーー
    // SceneビューでPlayerを選択すると、フリスビーの発射位置が球で表示される

    private void OnDrawGizmos()
    {
        // playerの参照が無いと位置を計算できないのでガード
        if (player == null)
        {
            return;
        }

        // プレイヤーの向きに応じてspawnOffsetのX方向を反転させる
        int direction = player.FacingDirection;
        Vector2 actualOffset = new Vector2(spawnOffset.x * direction, spawnOffset.y);
        Vector2 spawnPos = (Vector2)player.transform.position + actualOffset;

        // 生成位置が壁の中の場合、X座標だけプレイヤーの位置に補正(実際の生成と同じ挙動を可視化)
        if (Application.isPlaying && Physics2D.OverlapPoint(spawnPos, groundLayer) != null)
        {
            spawnPos = new Vector2(player.transform.position.x, spawnPos.y);
        }

        // マゼンタ色の小さな球で発射位置を表示
        Gizmos.color = Color.magenta;
        Gizmos.DrawWireSphere(spawnPos, 0.1f);

        // 発射方向を矢印っぽく線で表示
        if (Application.isPlaying)
        {
            Vector2 lineEnd = spawnPos + new Vector2(0.5f * direction, 0f);
            Gizmos.DrawLine(spawnPos, lineEnd);
        }
    }
}