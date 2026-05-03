using UnityEngine;

[RequireComponent(typeof(Rigidbody2D))]
[RequireComponent(typeof(Collider2D))]
public class FrisbeeController : MonoBehaviour
{
    // ーーー飛行パラメーターーーー
    [Header("カーソルへの到達時間(秒)")]
    [SerializeField] private float flightDuration = 0.6f;

    [Header("飛行中の最大上昇高さ(ユニット、ふわっと感)")]
    [SerializeField] private float peakRiseHeight = 0.3f;

    // ーーー減速・落下パラメーターーーー
    [Header("カーソル到達後の急減速の強さ(値が大きいほど急に止まる)")]
    [SerializeField] private float decelerationStrength = 8f;

    [Header("カーソル到達後に残す横方向の速度割合(0〜1、低いほど真下に落ちる)")]
    [SerializeField] private float horizontalSpeedAfterReach = 0.2f;

    // ーーー着地判定ーーー
    [Header("地面として扱うレイヤー")]
    [SerializeField] private LayerMask groundLayer;

    [Header("着地判定ボックスのサイズ")]
    [SerializeField] private Vector2 landCheckSize = new Vector2(0.4f, 0.1f);

    [Header("着地判定ボックスのY方向オフセット")]
    [SerializeField] private float landCheckOffsetY = -0.25f;

    // ーーー壁衝突ーーー
    [Header("壁とみなす法線のX成分の閾値(衝突面の法線X成分の絶対値がこの値以上なら壁とみなす、0=どんな向きでも壁、1=完全に縦の壁のみ)")]
    [SerializeField] private float wallNormalThreshold = 0.5f;

    // ーーー内部参照ーーー
    private Rigidbody2D rb;
    private Collider2D frisbeeCollider;

    // ーーー状態管理ーーー
    private enum FrisbeeState
    {
        Flying,        // カーソルに向かって飛行中(イーズアウト曲線)
        Decelerating,  // カーソル到達後、減速して落下中
        Landed,        // 着地して停止(犬がキャッチできる状態)
        Caught         // 犬にキャッチされた(口に固定されて追従)
    }

    private FrisbeeState currentState;

    // ーーー飛行軌道情報ーーー
    private Vector2 flightStartPosition;
    private float flightTargetX;
    private int flightDirection;
    private float flightElapsedTime;

    // ーーー壁ヒット済みフラグ(壁にぶつかって落下中かどうか、犬の救済キャッチ判定に使う)ーーー
    private bool didHitWall;

    // ーーー外部公開プロパティーーー
    public bool IsLanded => currentState == FrisbeeState.Landed;
    public bool IsCaught => currentState == FrisbeeState.Caught;
    public bool IsDecelerating => currentState == FrisbeeState.Decelerating;
    public bool DidHitWall => didHitWall;
    public Vector2 Position => transform.position;


    // ーーーUnityイベントーーー

    private void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
        frisbeeCollider = GetComponent<Collider2D>();
        currentState = FrisbeeState.Flying;
    }

    private void FixedUpdate()
    {
        switch (currentState)
        {
            case FrisbeeState.Flying:
                FlyingUpdate();
                break;

            case FrisbeeState.Decelerating:
                DeceleratingUpdate();
                break;

            case FrisbeeState.Landed:
                // 着地後は何もしない(犬の検知待ち)
                break;

            case FrisbeeState.Caught:
                // キャッチ後は何もしない(位置はDogControllerから更新される)
                break;
        }
    }


    // ーーー初期化ーーー

    public void Initialize(int direction, float targetX)
    {
        flightDirection = direction;
        flightTargetX = targetX;
        flightStartPosition = transform.position;
        flightElapsedTime = 0f;
        currentState = FrisbeeState.Flying;
        didHitWall = false;

        rb.gravityScale = 0f;
        rb.linearVelocity = Vector2.zero;
    }


    // ーーー飛行中の処理ーーー

    private void FlyingUpdate()
    {
        flightElapsedTime += Time.fixedDeltaTime;

        float t = flightElapsedTime / flightDuration;

        if (t >= 1f)
        {
            t = 1f;
            ApplyFlightPosition(t);
            EnterDeceleratingState();
            return;
        }

        ApplyFlightPosition(t);
    }


    // ーーー進捗tに基づいて飛行位置を計算・適用ーーー

    private void ApplyFlightPosition(float t)
    {
        float easedT = 1f - (1f - t) * (1f - t);

        float currentX = Mathf.Lerp(flightStartPosition.x, flightTargetX, easedT);
        float currentY = flightStartPosition.y + (peakRiseHeight * easedT);

        rb.MovePosition(new Vector2(currentX, currentY));
    }


    // ーーー減速状態への移行ーーー

    private void EnterDeceleratingState()
    {
        currentState = FrisbeeState.Decelerating;

        float arrivalSlope = 2f * 0.05f;
        float totalDistance = flightTargetX - flightStartPosition.x;
        float arrivalSpeedX = (totalDistance / flightDuration) * arrivalSlope;

        Vector2 v = new Vector2(arrivalSpeedX * horizontalSpeedAfterReach, 0f);
        rb.linearVelocity = v;

        rb.gravityScale = 1f;
    }


    // ーーー減速・落下中の処理ーーー

    private void DeceleratingUpdate()
    {
        Vector2 v = rb.linearVelocity;
        v.x = Mathf.Lerp(v.x, 0f, decelerationStrength * Time.fixedDeltaTime);
        rb.linearVelocity = v;

        if (CheckLanded())
        {
            EnterLandedState();
        }
    }


    // ーーー着地状態への移行ーーー

    private void EnterLandedState()
    {
        currentState = FrisbeeState.Landed;
        rb.linearVelocity = Vector2.zero;
        rb.angularVelocity = 0f;
        rb.gravityScale = 0f;
    }


    // ーーー着地判定ーーー
    // フリスビー下部にOverlapBoxを置いて、地面レイヤーに触れているか確認する

    private bool CheckLanded()
    {
        Vector2 origin = (Vector2)transform.position + new Vector2(0f, landCheckOffsetY);
        Collider2D hit = Physics2D.OverlapBox(origin, landCheckSize, 0f, groundLayer);
        return hit != null;
    }


    // ーーー物理衝突発生時の処理(地面・壁との衝突)ーーー
    // 法線X成分の絶対値が閾値以上なら壁(縦の壁面)とみなして横移動を停止
    // 地面の上面など縦向きの法線は無視(着地はCheckLandedで処理)

    private void OnCollisionEnter2D(Collision2D collision)
    {
        // 着地・キャッチ済みなら何もしない
        if (currentState == FrisbeeState.Landed || currentState == FrisbeeState.Caught)
        {
            return;
        }

        Vector2 normal = collision.GetContact(0).normal;

        // 法線X成分の絶対値が閾値以上なら壁とみなす
        if (Mathf.Abs(normal.x) < wallNormalThreshold)
        {
            return;
        }

        // 壁にぶつかった：横移動を停止
        Vector2 v = rb.linearVelocity;
        v.x = 0f;
        rb.linearVelocity = v;

        // 飛行方向もクリア
        flightDirection = 0;

        // 壁ヒット済みフラグを立てる(犬の救済キャッチ用)
        didHitWall = true;

        // 飛行中だった場合は減速状態へ移行(重力を有効化、地面着地を待つ)
        if (currentState == FrisbeeState.Flying)
        {
            EnterDeceleratingState();
        }
    }


    // ーーーキャッチされた時の処理ーーー

    public void OnCaught()
    {
        currentState = FrisbeeState.Caught;

        rb.bodyType = RigidbodyType2D.Kinematic;
        rb.linearVelocity = Vector2.zero;
        rb.angularVelocity = 0f;

        // コライダーを無効化(キャッチ後は当たり判定不要)
        frisbeeCollider.enabled = false;

        transform.rotation = Quaternion.identity;
    }


    // ーーーキャッチ中の位置を外部から設定ーーー

    public void SetCaughtPosition(Vector2 position)
    {
        if (currentState != FrisbeeState.Caught)
        {
            return;
        }

        Vector3 currentPos = transform.position;
        transform.position = new Vector3(position.x, position.y, currentPos.z);
    }


    // ーーーGizmos(着地判定ボックスの可視化)ーーー

    private void OnDrawGizmos()
    {
        Vector2 origin = (Vector2)transform.position + new Vector2(0f, landCheckOffsetY);

        if (Application.isPlaying)
        {
            if (currentState == FrisbeeState.Caught)
            {
                Gizmos.color = Color.magenta;
            }
            else if (currentState == FrisbeeState.Landed)
            {
                Gizmos.color = Color.green;
            }
            else if (currentState == FrisbeeState.Decelerating)
            {
                Gizmos.color = Color.cyan;
            }
            else
            {
                Gizmos.color = Color.red;
            }
        }
        else
        {
            Gizmos.color = Color.yellow;
        }

        Gizmos.DrawWireCube(origin, landCheckSize);
    }
}