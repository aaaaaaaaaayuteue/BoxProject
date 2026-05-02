using System.Collections.Generic;
using UnityEngine;

[RequireComponent(typeof(Rigidbody2D))]
[RequireComponent(typeof(BoxCollider2D))]
[RequireComponent(typeof(SpriteRenderer))]
public class DogController : MonoBehaviour
{
    // ーーー追従対象ーーー
    [Header("追従対象のプレイヤー")]
    [SerializeField] private PlayerController player;

    // ーーーフリスビー連携ーーー
    [Header("フリスビーを投げるFrisbeeThrower")]
    [SerializeField] private FrisbeeThrower frisbeeThrower;

    // ーーー追従パラメーターーーー
    [Header("プレイヤーから追従する距離(軌跡上の距離、ユニット)")]
    [SerializeField] private float followDistance = 1.5f;

    [Header("通常モードの最大移動速度(ユニット/秒)")]
    [SerializeField] private float normalModeMoveSpeed = 6f;

    // ーーー追従ディレイーーー
    [Header("プレイヤーが動き始めてから追従開始までの遅延(秒)")]
    [SerializeField] private float followStartDelay = 0.1f;

    [Header("プレイヤーが止まってから追従停止までの遅延(秒)")]
    [SerializeField] private float followStopDelay = 0.1f;

    // ーーープレイヤー停止判定ーーー
    [Header("プレイヤーの速度がこの値未満なら「停止中」とみなす")]
    [SerializeField] private float playerStoppedSpeedThreshold = 0.1f;

    // ーーー軌跡記録ーーー
    [Header("軌跡記録の最小間隔(ユニット、これ以上動いたら新しい点を記録)")]
    [SerializeField] private float trailRecordMinDistance = 0.05f;

    [Header("軌跡記録の最大保持数(古いものから捨てる)")]
    [SerializeField] private int trailMaxCount = 200;

    // ーーー行ってこいモードーーー
    [Header("行ってこいモードの移動速度(ユニット/秒)")]
    [SerializeField] private float fetchModeMoveSpeed = 7f;

    [Header("フリスビー投擲を検知してから犬が動き出すまでのディレイ(秒)")]
    [SerializeField] private float fetchStartDelay = 0.2f;

    [Header("フリスビーへ到達したと判定する距離(ユニット)")]
    [SerializeField] private float fetchArriveThreshold = 0.3f;

    // ーーージャンプーーー
    [Header("犬のジャンプの最高到達点の高さ(マス数)")]
    [SerializeField] private float jumpMaxHeight = 1f;

    [Header("プレイヤーのジャンプを検知してから犬がジャンプするまでの遅延(秒)")]
    [SerializeField] private float jumpSyncDelay = 0.3f;

    // ※ 重力はRigidbody 2DのGravity ScaleとUnityの重力設定から決まる
    // ※ ジャンプ初速度はそれらの値から自動で逆算される

    // ーーー接地判定ーーー
    [Header("犬の地面として扱うレイヤー(Groundのみ)")]
    [SerializeField] private LayerMask groundLayer;

    [Header("接地判定ボックスのサイズ(X=幅、Y=厚み)")]
    [SerializeField] private Vector2 groundCheckSize = new Vector2(0.9f, 0.1f);

    [Header("接地判定ボックスのY方向オフセット(コライダー下端からの相対位置、負の値でさらに下)")]
    [SerializeField] private float groundCheckOffsetY = 0f;

    // ーーー壁越え自動ジャンプーーー
    [Header("壁検知ボックスのサイズ(進行方向に壁があるかチェックするセンサー)")]
    [SerializeField] private Vector2 wallCheckSize = new Vector2(0.1f, 0.6f);

    [Header("壁検知ボックスの犬の中心からの水平オフセット(向きで自動反転)")]
    [SerializeField] private float wallCheckOffsetX = 0.7f;

    [Header("壁検知ボックスのY方向オフセット(犬の中心からの相対位置)")]
    [SerializeField] private float wallCheckOffsetY = 0f;

    // ーーー衝突制御ーーー
    [Header("衝突を「上から」と判定する法線Y成分の閾値(0.5なら法線Yが-0.5以下を上方向とみなす)")]
    [SerializeField] private float upwardCollisionThreshold = 0.5f;

    [Header("プレイヤーと犬の距離がこの値を超えたら、すり抜け状態を解除する")]
    [SerializeField] private float collisionResetDistance = 1.7f;

    // ーーー向きーーー
    [Header("元のスプライト画像が右向きならtrue、左向きならfalse")]
    [SerializeField] private bool spriteOriginallyFacesRight = true;

    // ーーー内部参照ーーー
    private Rigidbody2D rb;
    private BoxCollider2D boxCollider;
    private SpriteRenderer spriteRenderer;
    private Rigidbody2D playerRb;

    // ーーーモード管理ーーー
    // 犬の動きの状態
    private enum DogMode
    {
        Normal,    // 通常モード(プレイヤー追従)
        Fetch,     // 行ってこいモード(フリスビーを追いかける)
        Waiting    // 待機モード(フリスビー位置で停止)
    }

    private DogMode currentMode = DogMode.Normal;

    // ーーーフリスビー連携ーーー
    private FrisbeeController trackedFrisbee;       // 追いかけ中のフリスビー
    private float fetchStartDelayTimer = -1f;        // 行ってこいモード開始ディレイのタイマー

    // ーーー軌跡データーーー
    // プレイヤーの過去位置を記録するリスト
    // 先頭が最新、末尾が最古
    // 犬は先頭から軌跡をたどって、followDistance分の距離になる位置を目指す
    private List<Vector2> trail = new List<Vector2>();
    private Vector2 lastRecordedPosition;  // 最後に軌跡に記録したプレイヤー位置(間引き判定用)

    // ーーー追従状態ーーー
    private bool playerIsMoving;            // プレイヤーが現在動いているかの判定
    private float playerStateChangeTimer;   // プレイヤーの状態が変わってからの経過時間
    private bool dogShouldFollow;           // 犬が追従すべきかどうか(ディレイ反映後)

    // ーーー向き状態ーーー
    private int facingDirection;  // 現在の犬の向き(+1=右、-1=左)

    // ーーージャンプ計算結果ーーー
    private float calculatedJumpVelocity;  // ジャンプ初速度(プレイヤーと同じく逆算で計算)

    // ーーージャンプ予約状態ーーー
    private float pendingJumpTimer = -1f;

    // ーーーすり抜け状態管理ーーー
    private List<Collider2D> ignoredColliders = new List<Collider2D>();

    // ーーー外部公開プロパティーーー
    public bool IsGrounded => CheckGrounded();


    // ーーーUnityイベントーーー

    private void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
        boxCollider = GetComponent<BoxCollider2D>();
        spriteRenderer = GetComponent<SpriteRenderer>();

        rb.freezeRotation = true;

        if (player != null)
        {
            playerRb = player.GetComponent<Rigidbody2D>();
            // 初期位置を軌跡の起点として記録しておく
            lastRecordedPosition = player.transform.position;
            trail.Add(lastRecordedPosition);
        }

        facingDirection = 1;
        ApplySpriteFlip();
    }

    private void Update()
    {
        // フリスビー投擲を検知してモード遷移をチェック
        CheckFrisbeeThrown();

        // 行ってこいモードのディレイタイマーを更新
        UpdateFetchStartDelay();

        // プレイヤーの動き状態を毎フレーム更新する
        UpdatePlayerMovingState();

        // ディレイを考慮して、犬が追従すべきかを決定する
        UpdateFollowDecision();

        // プレイヤーの軌跡を記録する
        RecordPlayerTrail();

        // 進行方向に応じて見た目を反転する
        ApplySpriteFlip();

        // すり抜け中のコライダーが十分離れたら衝突を再有効化する
        UpdateIgnoredColliders();
    }

    private void FixedUpdate()
    {
        // ジャンプ初速度は重力に依存するので毎FixedUpdateで再計算する
        CalculateJumpVelocity();

        // モードに応じて挙動を切り替える
        switch (currentMode)
        {
            case DogMode.Normal:
                // 通常モード：プレイヤーのジャンプに連動、軌跡追従、壁越えジャンプ
                HandleJumpSync();
                UpdateDogMovement();
                HandleStuckJump();
                break;

            case DogMode.Fetch:
                // 行ってこいモード：フリスビーへ向かう、壁越えジャンプは流用
                UpdateFetchMovement();
                HandleStuckJump();
                break;

            case DogMode.Waiting:
                // 待機モード：その場で停止
                StopHorizontalMovement();
                break;
        }
    }


    // ーーーフリスビー投擲の検知ーーー
    // FrisbeeThrowerに新しいフリスビーが生成されたら、行ってこいモード開始の準備をする

    private void CheckFrisbeeThrown()
    {
        if (frisbeeThrower == null)
        {
            return;
        }

        FrisbeeController currentFrisbee = frisbeeThrower.CurrentFrisbee;

        // 通常モードのときに新しいフリスビーが現れたら、行ってこいモードへの移行を予約する
        // (trackedFrisbeeと別物のフリスビーが現れたら新規投擲とみなす)
        if (currentMode == DogMode.Normal && currentFrisbee != null && currentFrisbee != trackedFrisbee)
        {
            trackedFrisbee = currentFrisbee;
            fetchStartDelayTimer = fetchStartDelay;
        }
    }


    // ーーー行ってこいモード開始のディレイタイマー更新ーーー
    // ディレイ時間が経過したら、行ってこいモードに突入する

    private void UpdateFetchStartDelay()
    {
        if (fetchStartDelayTimer < 0f)
        {
            return;
        }

        fetchStartDelayTimer -= Time.deltaTime;

        if (fetchStartDelayTimer <= 0f)
        {
            fetchStartDelayTimer = -1f;
            currentMode = DogMode.Fetch;
        }
    }


    // ーーー行ってこいモードの移動処理ーーー
    // 追いかけ中のフリスビーへ向かって走る
    // フリスビーが地面に着いてかつ十分近づいたら待機モードへ移行する

    private void UpdateFetchMovement()
    {
        // フリスビーが消えていれば通常モードに戻す
        if (trackedFrisbee == null)
        {
            currentMode = DogMode.Normal;
            return;
        }

        Vector2 frisbeePos = trackedFrisbee.Position;
        float dx = frisbeePos.x - transform.position.x;
        float distanceX = Mathf.Abs(dx);

        // フリスビーが着地済みで、犬が十分近づいたら待機モードへ
        if (trackedFrisbee.IsLanded && distanceX < fetchArriveThreshold)
        {
            currentMode = DogMode.Waiting;
            StopHorizontalMovement();
            return;
        }

        // フリスビーへ向かう方向を決定
        int directionToFrisbee;
        if (dx > 0f)
        {
            directionToFrisbee = 1;
        }
        else
        {
            directionToFrisbee = -1;
        }

        // 通り過ぎを防ぐ：目標が近い場合は速度を抑える
        float maxStepDistance = fetchModeMoveSpeed * Time.fixedDeltaTime;

        float currentSpeed;
        if (distanceX < maxStepDistance)
        {
            currentSpeed = distanceX / Time.fixedDeltaTime;
        }
        else
        {
            currentSpeed = fetchModeMoveSpeed;
        }

        Vector2 v = rb.linearVelocity;
        v.x = directionToFrisbee * currentSpeed;
        rb.linearVelocity = v;

        // 進行方向で見た目を更新
        facingDirection = directionToFrisbee;
    }


    // ーーー水平移動を停止ーーー
    // 待機モードなど、横方向の動きを止めたい時に使う

    private void StopHorizontalMovement()
    {
        Vector2 v = rb.linearVelocity;
        v.x = 0f;
        rb.linearVelocity = v;
    }


    // ーーー衝突発生時の処理ーーー
    // 衝突した瞬間、接触点の法線を見て「上から」か「横から」かを判定する
    // 横や下からの衝突なら、その特定の相手との衝突を無効化してすり抜けさせる

    private void OnCollisionEnter2D(Collision2D collision)
    {
        // プレイヤーじゃなければ何もしない
        if (collision.gameObject != player.gameObject)
        {
            return;
        }

        Vector2 normal = collision.GetContact(0).normal;

        if (normal.y <= -upwardCollisionThreshold)
        {
            // 上から乗られた → 衝突を維持する
            return;
        }

        // 横や下からの衝突 → 衝突を無効化してすり抜けさせる
        Physics2D.IgnoreCollision(collision.collider, boxCollider, true);

        if (!ignoredColliders.Contains(collision.collider))
        {
            ignoredColliders.Add(collision.collider);
        }
    }


    // ーーーすり抜け中コライダーの再有効化チェックーーー

    private void UpdateIgnoredColliders()
    {
        for (int i = ignoredColliders.Count - 1; i >= 0; i--)
        {
            Collider2D ignored = ignoredColliders[i];

            if (ignored == null)
            {
                ignoredColliders.RemoveAt(i);
                continue;
            }

            float distance = Vector2.Distance(transform.position, ignored.transform.position);

            if (distance > collisionResetDistance)
            {
                Physics2D.IgnoreCollision(ignored, boxCollider, false);
                ignoredColliders.RemoveAt(i);
            }
        }
    }


    // ーーープレイヤーの動き状態を判定ーーー

    private void UpdatePlayerMovingState()
    {
        if (playerRb == null)
        {
            return;
        }

        bool currentlyMoving = Mathf.Abs(playerRb.linearVelocity.x) >= playerStoppedSpeedThreshold;

        if (currentlyMoving != playerIsMoving)
        {
            playerIsMoving = currentlyMoving;
            playerStateChangeTimer = 0f;
        }
        else
        {
            playerStateChangeTimer += Time.deltaTime;
        }
    }


    // ーーー追従するかどうかの最終判定ーーー

    private void UpdateFollowDecision()
    {
        if (playerIsMoving)
        {
            if (playerStateChangeTimer >= followStartDelay)
            {
                dogShouldFollow = true;
            }
        }
        else
        {
            if (playerStateChangeTimer >= followStopDelay)
            {
                dogShouldFollow = false;
            }
        }
    }


    // ーーープレイヤーの軌跡を記録ーーー

    private void RecordPlayerTrail()
    {
        if (player == null)
        {
            return;
        }

        Vector2 currentPlayerPos = player.transform.position;
        float distFromLast = Vector2.Distance(currentPlayerPos, lastRecordedPosition);

        if (distFromLast >= trailRecordMinDistance)
        {
            trail.Insert(0, currentPlayerPos);
            lastRecordedPosition = currentPlayerPos;

            if (trail.Count > trailMaxCount)
            {
                trail.RemoveAt(trail.Count - 1);
            }
        }
    }


    // ーーージャンプ初速度の逆算ーーー

    private void CalculateJumpVelocity()
    {
        float effectiveGravity = Mathf.Abs(Physics2D.gravity.y) * rb.gravityScale;

        if (effectiveGravity <= 0f)
        {
            calculatedJumpVelocity = 0f;
            return;
        }

        // エネルギー保存則：v0 = √(2 * g * h)
        calculatedJumpVelocity = Mathf.Sqrt(2f * effectiveGravity * jumpMaxHeight);
    }


    // ーーープレイヤーのジャンプ検知と犬のジャンプ実行(通常モードのみ)ーーー

    private void HandleJumpSync()
    {
        if (player == null)
        {
            return;
        }

        if (player.JustJumped && !player.IsRidingDog)
        {
            pendingJumpTimer = jumpSyncDelay;
        }

        if (pendingJumpTimer < 0f)
        {
            return;
        }

        pendingJumpTimer -= Time.fixedDeltaTime;

        if (pendingJumpTimer <= 0f)
        {
            pendingJumpTimer = -1f;

            if (CheckGrounded())
            {
                Vector2 v = rb.linearVelocity;
                v.y = calculatedJumpVelocity;
                rb.linearVelocity = v;
            }
        }
    }


    // ーーー壁に引っかかった時の自動ジャンプーーー
    // 通常モードでは追従中、行ってこいモードでは常に発動する

    private void HandleStuckJump()
    {
        // 通常モード時、追従中じゃないなら何もしない
        if (currentMode == DogMode.Normal && !dogShouldFollow)
        {
            return;
        }

        // 接地していないならジャンプできない
        if (!CheckGrounded())
        {
            return;
        }

        // 進行方向に壁がなければ何もしない
        if (!CheckWallAhead())
        {
            return;
        }

        // 全条件を満たしたのでジャンプ発動
        Vector2 v = rb.linearVelocity;
        v.y = calculatedJumpVelocity;
        rb.linearVelocity = v;
    }


    // ーーー前方の壁検知ーーー

    private bool CheckWallAhead()
    {
        Vector2 origin = GetWallCheckOrigin();
        Collider2D hit = Physics2D.OverlapBox(origin, wallCheckSize, 0f, groundLayer);
        return hit != null;
    }


    // ーーー壁検知ボックスの中心位置を計算ーーー

    private Vector2 GetWallCheckOrigin()
    {
        Vector2 center = (Vector2)transform.position;
        return new Vector2(
            center.x + (wallCheckOffsetX * facingDirection),
            center.y + wallCheckOffsetY
        );
    }


    // ーーー接地判定ーーー

    private bool CheckGrounded()
    {
        Vector2 origin = GetGroundCheckOrigin();
        Collider2D hit = Physics2D.OverlapBox(origin, groundCheckSize, 0f, groundLayer);
        return hit != null;
    }


    // ーーー接地判定ボックスの中心位置を計算ーーー

    private Vector2 GetGroundCheckOrigin()
    {
        Vector2 colliderCenter = (Vector2)transform.position + boxCollider.offset * (Vector2)transform.lossyScale;
        float colliderBottomY = colliderCenter.y - (boxCollider.size.y * Mathf.Abs(transform.lossyScale.y) * 0.5f);
        return new Vector2(colliderCenter.x, colliderBottomY + groundCheckOffsetY);
    }


    // ーーー犬の通常モード移動処理(軌跡追従)ーーー

    private void UpdateDogMovement()
    {
        if (player == null || trail.Count == 0)
        {
            return;
        }

        Vector2 v = rb.linearVelocity;

        if (!dogShouldFollow)
        {
            v.x = 0f;
            rb.linearVelocity = v;
            return;
        }

        Vector2 targetPosition = GetTrailPositionAtDistance(followDistance);

        float dx = targetPosition.x - transform.position.x;
        float distanceToTarget = Mathf.Abs(dx);

        if (distanceToTarget < trailRecordMinDistance)
        {
            v.x = 0f;
            rb.linearVelocity = v;
            return;
        }

        // 目標位置へ向かう方向を求める
        int directionToTarget;
        if (dx > 0f)
        {
            directionToTarget = 1;
        }
        else
        {
            directionToTarget = -1;
        }

        // 通り過ぎを防ぐために、目標が近い場合は速度を抑える
        float maxStepDistance = normalModeMoveSpeed * Time.fixedDeltaTime;

        float currentSpeed;
        if (distanceToTarget < maxStepDistance)
        {
            currentSpeed = distanceToTarget / Time.fixedDeltaTime;
        }
        else
        {
            currentSpeed = normalModeMoveSpeed;
        }

        v.x = directionToTarget * currentSpeed;
        rb.linearVelocity = v;

        facingDirection = directionToTarget;
    }


    // ーーー軌跡上の指定距離の位置を取得ーーー

    private Vector2 GetTrailPositionAtDistance(float distance)
    {
        if (trail.Count <= 1)
        {
            return trail[0];
        }

        float accumulatedDistance = 0f;

        for (int i = 0; i < trail.Count - 1; i++)
        {
            Vector2 pointA = trail[i];
            Vector2 pointB = trail[i + 1];
            float segmentLength = Vector2.Distance(pointA, pointB);

            if (accumulatedDistance + segmentLength >= distance)
            {
                float remaining = distance - accumulatedDistance;
                float t = remaining / segmentLength;
                return Vector2.Lerp(pointA, pointB, t);
            }

            accumulatedDistance += segmentLength;
        }

        return trail[trail.Count - 1];
    }


    // ーーースプライトの左右反転を適用ーーー

    private void ApplySpriteFlip()
    {
        if (spriteOriginallyFacesRight)
        {
            spriteRenderer.flipX = (facingDirection == -1);
        }
        else
        {
            spriteRenderer.flipX = (facingDirection == 1);
        }
    }


    // ーーーGizmos(各種可視化)ーーー

    private void OnDrawGizmos()
    {
        // boxColliderがまだ取得されていない場合は自前で取得
        if (boxCollider == null)
        {
            boxCollider = GetComponent<BoxCollider2D>();
        }

        // 接地判定ボックスの可視化
        if (boxCollider != null)
        {
            Vector2 origin = GetGroundCheckOrigin();

            if (Application.isPlaying)
            {
                if (CheckGrounded())
                {
                    Gizmos.color = Color.green;
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

            Gizmos.DrawWireCube(origin, groundCheckSize);
        }

        // 壁検知ボックスの可視化
        Vector2 wallOrigin = GetWallCheckOrigin();
        if (Application.isPlaying)
        {
            if (CheckWallAhead())
            {
                Gizmos.color = Color.red;
            }
            else
            {
                Gizmos.color = Color.white;
            }
        }
        else
        {
            Gizmos.color = Color.white;
        }
        Gizmos.DrawWireCube(wallOrigin, wallCheckSize);

        // 軌跡関連は実行中のみ表示
        if (!Application.isPlaying || trail == null || trail.Count == 0)
        {
            return;
        }

        // プレイヤーの軌跡を白い線で表示
        Gizmos.color = Color.white;
        for (int i = 0; i < trail.Count - 1; i++)
        {
            Gizmos.DrawLine(trail[i], trail[i + 1]);
        }

        // 軌跡の各点を小さな点で表示
        Gizmos.color = Color.gray;
        for (int i = 0; i < trail.Count; i++)
        {
            Gizmos.DrawWireSphere(trail[i], 0.05f);
        }

        // 犬の目標位置を緑の球で表示
        Gizmos.color = Color.green;
        Vector2 target = GetTrailPositionAtDistance(followDistance);
        Gizmos.DrawWireSphere(target, 0.2f);

        // すり抜け中の状態をシアンの円で表示
        Gizmos.color = Color.cyan;
        Gizmos.DrawWireSphere(transform.position, collisionResetDistance);
    }
}