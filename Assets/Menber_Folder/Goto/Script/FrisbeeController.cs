using UnityEngine;

[RequireComponent(typeof(Rigidbody2D))]
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

    // ーーー内部参照ーーー
    private Rigidbody2D rb;

    // ーーー状態管理ーーー
    private enum FrisbeeState
    {
        Flying,        // カーソルに向かって飛行中(イーズアウト曲線)
        Decelerating,  // カーソル到達後、減速して落下中
        Landed         // 着地して停止
    }

    private FrisbeeState currentState;

    // ーーー飛行軌道情報ーーー
    private Vector2 flightStartPosition;  // 投擲開始時のフリスビー位置
    private float flightTargetX;          // カーソル手前側面のX座標(到達点)
    private int flightDirection;          // 飛行方向(+1=右、-1=左)
    private float flightElapsedTime;      // 飛行開始からの経過時間

    // ーーー外部公開プロパティーーー
    public bool IsLanded => currentState == FrisbeeState.Landed;
    public Vector2 Position => transform.position;


    // ーーーUnityイベントーーー

    private void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
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
                // 着地後は何もしない
                break;
        }
    }


    // ーーー初期化ーーー
    // FrisbeeThrowerから呼ばれる。投擲方向と目標X座標を受け取って飛行を開始する

    public void Initialize(int direction, float targetX)
    {
        flightDirection = direction;
        flightTargetX = targetX;
        flightStartPosition = transform.position;
        flightElapsedTime = 0f;
        currentState = FrisbeeState.Flying;

        // 飛行中は重力の影響を受けないようにする
        // (横と縦の動きは経過時間ベースで計算するため、物理重力に任せない)
        rb.gravityScale = 0f;
        rb.linearVelocity = Vector2.zero;
    }


    // ーーー飛行中の処理ーーー
    // 経過時間から進捗(0〜1)を計算し、イーズアウト曲線に沿って位置を直接更新する
    // 横方向：開始X→終点Xへ、二次関数のイーズアウト
    // 縦方向：開始Y→開始Y+peakRiseHeightへ、同じイーズアウト

    private void FlyingUpdate()
    {
        // 経過時間を加算
        flightElapsedTime += Time.fixedDeltaTime;

        // 進捗0〜1を計算(flightDurationで1.0に到達)
        float t = flightElapsedTime / flightDuration;

        // 進捗が1を超えたらカーソル到達とみなして減速状態へ移行する
        if (t >= 1f)
        {
            t = 1f;
            ApplyFlightPosition(t);  // 最終位置に補正
            EnterDeceleratingState();
            return;
        }

        // イーズアウトのカーブを適用して位置を更新
        ApplyFlightPosition(t);
    }


    // ーーー進捗tに基づいて飛行位置を計算・適用ーーー
    // 二次関数イーズアウト：1 - (1 - t)² を使う
    // この曲線は「最初に大きく動いて、終盤はゆっくり」になる

    private void ApplyFlightPosition(float t)
    {
        // イーズアウト係数(0〜1の範囲)
        float easedT = 1f - (1f - t) * (1f - t);

        // 横方向の位置：開始Xから終点Xへ補間
        float currentX = Mathf.Lerp(flightStartPosition.x, flightTargetX, easedT);

        // 縦方向の位置：開始Yから(開始Y + peakRiseHeight)へ補間
        // 飛行中はずっと上昇していき、終点で最大高さになる
        float currentY = flightStartPosition.y + (peakRiseHeight * easedT);

        // 位置を直接セット(Rigidbody2DはKinematicに近い動きをする)
        // velocityではなくpositionを直接いじる理由：
        // - イーズ曲線で位置を厳密に制御したい
        // - 速度ベースだと曲線の形が崩れやすい
        rb.MovePosition(new Vector2(currentX, currentY));
    }


    // ーーー減速状態への移行ーーー
    // カーソル到達時点の進行方向の速度を推定し、その値に減速倍率をかけて初期速度とする
    // これによりカーソル到達時にも横方向にもう少し進んで、自然な減速→落下の流れになる

    private void EnterDeceleratingState()
    {
        currentState = FrisbeeState.Decelerating;

        // カーソル到達時点での「進行方向の瞬間速度」を推定する
        // 二次関数イーズアウト 1 - (1-t)² の微分は 2(1-t) なので、t=1のときの傾きは0になる
        // ただしそれだと完全停止になってしまうので、t=直前(0.95あたり)の傾きを採用する
        // 二次関数の微分： d/dt [1 - (1-t)²] = 2(1-t)
        // t=0.95のとき接線の傾きは 2 * 0.05 = 0.1
        float arrivalSlope = 2f * 0.05f;
        float totalDistance = flightTargetX - flightStartPosition.x;
        float arrivalSpeedX = (totalDistance / flightDuration) * arrivalSlope;

        // 推定速度に「減速後の残存割合」を掛けて、減速状態の初期速度とする
        Vector2 v = new Vector2(arrivalSpeedX * horizontalSpeedAfterReach, 0f);
        rb.linearVelocity = v;

        // 重力を有効化して落下させる
        rb.gravityScale = 1f;
    }


    // ーーー減速・落下中の処理ーーー
    // 残った横速度を毎フレーム減衰させながら、重力で落下する
    // 着地判定して地面に触れたらLanded状態へ

    private void DeceleratingUpdate()
    {
        Vector2 v = rb.linearVelocity;
        // 横速度を毎フレーム減衰させる(0に近づいていく)
        v.x = Mathf.Lerp(v.x, 0f, decelerationStrength * Time.fixedDeltaTime);
        rb.linearVelocity = v;

        // 着地判定
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
        rb.gravityScale = 0f;
    }


    // ーーー着地判定ーーー
    // フリスビー下部に判定ボックスを置いて、地面レイヤーに触れているか確認する

    private bool CheckLanded()
    {
        Vector2 origin = (Vector2)transform.position + new Vector2(0f, landCheckOffsetY);
        Collider2D hit = Physics2D.OverlapBox(origin, landCheckSize, 0f, groundLayer);
        return hit != null;
    }


    // ーーーGizmos(着地判定ボックスの可視化)ーーー

    private void OnDrawGizmos()
    {
        Vector2 origin = (Vector2)transform.position + new Vector2(0f, landCheckOffsetY);

        if (Application.isPlaying)
        {
            if (currentState == FrisbeeState.Landed)
            {
                Gizmos.color = Color.green;  // 着地済み
            }
            else if (currentState == FrisbeeState.Decelerating)
            {
                Gizmos.color = Color.cyan;   // 減速中
            }
            else
            {
                Gizmos.color = Color.red;    // 飛行中
            }
        }
        else
        {
            Gizmos.color = Color.yellow;
        }

        Gizmos.DrawWireCube(origin, landCheckSize);
    }
}