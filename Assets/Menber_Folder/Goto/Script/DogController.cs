using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

[RequireComponent(typeof(Rigidbody2D))]
[RequireComponent(typeof(BoxCollider2D))]
[RequireComponent(typeof(SpriteRenderer))]
public class DogController : MonoBehaviour
{
    [Header("追従対象のプレイヤー")]
    [SerializeField] private PlayerController player;

    [Header("フリスビーを投げるFrisbeeThrower")]
    [SerializeField] private FrisbeeThrower frisbeeThrower;

    [Header("カーソル(ジャンプキャッチの着地点として使用)")]
    [SerializeField] private CursorController cursor;

    [Header("ーーーーーーー ここから下は通常モード(プレイヤー追従)関連 ーーーーーーー")]
    [Header("プレイヤーから追従する距離(軌跡上の距離、ユニット)")]
    [SerializeField] private float followDistance = 1.5f;

    [Header("通常モードの最大移動速度(ユニット/秒)")]
    [SerializeField] private float normalModeMoveSpeed = 6f;

    [Header("プレイヤーが動き始めてから追従開始までの遅延(秒)")]
    [SerializeField] private float followStartDelay = 0.1f;

    [Header("プレイヤーが止まってから追従停止までの遅延(秒)")]
    [SerializeField] private float followStopDelay = 0.1f;

    [Header("プレイヤーの速度がこの値未満なら「停止中」とみなす")]
    [SerializeField] private float playerStoppedSpeedThreshold = 0.1f;

    [Header("軌跡記録の最小間隔(ユニット、これ以上動いたら新しい点を記録)")]
    [SerializeField] private float trailRecordMinDistance = 0.05f;

    [Header("軌跡記録の最大保持数(古いものから捨てる)")]
    [SerializeField] private int trailMaxCount = 200;

    [Header("ーーーーーーー ここから下は行ってこいモード関連 ーーーーーーー")]
    [Header("行ってこいモードの移動速度(ユニット/秒)")]
    [SerializeField] private float fetchModeMoveSpeed = 7f;

    [Header("フリスビー投擲を検知してから犬が動き出すまでのディレイ(秒)")]
    [SerializeField] private float fetchStartDelay = 0.2f;

    [Header("フリスビーがこのY値以上犬より下にある時、段差から降りるために強制前進する")]
    [SerializeField] private float fetchDropDownThreshold = 0.5f;

    [Header("ーーーーーーー ここから下はジャンプキャッチ・キャッチ関連 ーーーーーーー")]
    [Header("ジャンプキャッチ判定距離(犬とフリスビーの距離がこの値以内なら発動)")]
    [SerializeField] private float jumpCatchDistance = 1.5f;

    [Header("ジャンプキャッチ最低高さ(フリスビーが犬よりこの値以上高くないと発動しない)")]
    [SerializeField] private float jumpCatchMinHeight = 0.1f;

    [Header("犬の口の位置(犬の中心からのオフセット、Xは向きで自動反転)")]
    [SerializeField] private Vector2 mouthOffset = new Vector2(0.4f, 0.1f);

    [Header("座標ベースのキャッチ判定：X座標の許容差(ユニット)")]
    [SerializeField] private float positionCatchXThreshold = 0.5f;

    [Header("座標ベースのキャッチ判定：Y座標の許容差(ユニット)")]
    [SerializeField] private float positionCatchYThreshold = 1.0f;

    [Header("ーーーーーーー ここから下は待機モード関連 ーーーーーーー")]
    [Header("キャッチ完了後、犬がプレイヤーの方を向くまでのディレイ(秒)")]
    [SerializeField] private float turnToPlayerDelay = 0.3f;

    [Header("待機・おかえりモード中、プレイヤーがこのY値以上犬より上にいたら衝突有効化(乗れる)")]
    [SerializeField] private float waitingPlayerAboveThreshold = 0.5f;

    [Header("ーーーーーーー ここから下はおかえりモード関連 ーーーーーーー")]
    [Header("おかえりモードの移動速度(ユニット/秒)")]
    [SerializeField] private float returnModeMoveSpeed = 7f;

    [Header("プレイヤーとこの距離以内に近づいたらフリスビー回収&通常モードに戻る(ユニット)")]
    [SerializeField] private float returnPickupDistance = 0.8f;

    [Header("ーーーーーーー ここから下はジャンプ(物理)関連 ーーーーーーー")]
    [Header("犬のジャンプの最高到達点の高さ(マス数、通常モードのジャンプ連動用)")]
    [SerializeField] private float jumpMaxHeight = 1f;

    [Header("プレイヤーのジャンプを検知してから犬がジャンプするまでの遅延(秒)")]
    [SerializeField] private float jumpSyncDelay = 0.3f;

    // ※ 重力はRigidbody 2DのGravity ScaleとUnityの重力設定から決まる
    // ※ ジャンプ初速度はそれらの値から自動で逆算される

    [Header("ーーーーーーー ここから下は接地判定関連 ーーーーーーー")]
    [Header("犬の地面として扱うレイヤー(Groundのみ)")]
    [SerializeField] private LayerMask groundLayer;

    [Header("接地判定ボックスのサイズ(X=幅、Y=厚み)")]
    [SerializeField] private Vector2 groundCheckSize = new Vector2(0.9f, 0.1f);

    [Header("接地判定ボックスのY方向オフセット(コライダー下端からの相対位置、負の値でさらに下)")]
    [SerializeField] private float groundCheckOffsetY = 0f;

    [Header("ーーーーーーー ここから下は壁越え自動ジャンプ関連 ーーーーーーー")]
    [Header("壁検知ボックスのサイズ(進行方向に壁があるかチェックするセンサー)")]
    [SerializeField] private Vector2 wallCheckSize = new Vector2(0.1f, 0.6f);

    [Header("壁検知ボックスの犬の中心からの水平オフセット(向きで自動反転)")]
    [SerializeField] private float wallCheckOffsetX = 0.7f;

    [Header("壁検知ボックスのY方向オフセット(犬の中心からの相対位置)")]
    [SerializeField] private float wallCheckOffsetY = 0f;

    [Header("壁越え自動ジャンプの方式(A案=事前計測、B案=段階的)")]
    [SerializeField] private WallJumpMode wallJumpMode = WallJumpMode.Predictive;

    [Header("壁越え自動ジャンプの最大マス数")]
    [SerializeField] private int maxAutoJumpHeight = 3;

    [Header("1マスのワールドサイズ(ユニット)")]
    [SerializeField] private float cellSize = 1f;

    [Header("壁の高さ計測位置の犬中心からの水平オフセット(進行方向で自動反転、壁の少し奥側を測る)")]
    [SerializeField] private float wallProbeOffsetX = 1f;

    [Header("壁の高さ計測ボックスのサイズ(1マス分より少し小さめがおすすめ)")]
    [SerializeField] private Vector2 wallProbeSize = new Vector2(0.8f, 0.8f);

    [Header("ーーーーーーー ここから下はプレイヤーとの衝突制御(通常モード等)関連 ーーーーーーー")]
    [Header("衝突を「上から」と判定する法線Y成分の閾値(0.5なら法線Yが-0.5以下を上方向とみなす)")]
    [SerializeField] private float upwardCollisionThreshold = 0.5f;

    [Header("プレイヤーと犬の距離がこの値を超えたら、すり抜け状態を解除する")]
    [SerializeField] private float collisionResetDistance = 1.7f;

    [Header("ーーーーーーー ここから下はスプライト関連 ーーーーーーー")]
    [Header("元のスプライト画像が右向きならtrue、左向きならfalse")]
    [SerializeField] private bool spriteOriginallyFacesRight = true;

    // ーーー内部参照ーーー
    private Rigidbody2D rb;
    private BoxCollider2D boxCollider;
    private SpriteRenderer spriteRenderer;
    private Rigidbody2D playerRb;

    // ーーー入力ーーー
    private PlayerInputActions inputActions;
    private InputAction throwAction;

    // ーーーモード管理ーーー
    private enum DogMode
    {
        Normal,        // 通常モード(プレイヤー追従)
        Fetch,         // 行ってこいモード(フリスビーを追いかける)
        JumpCatching,  // ジャンプキャッチ中(放物線軌道でカーソルX位置に着地)
        Waiting,       // 待機モード(フリスビーをくわえてその場で停止)
        Return         // おかえりモード(プレイヤーへ戻る)
    }

    private DogMode currentMode = DogMode.Normal;

    // ーーー壁越え自動ジャンプの方式ーーー
    private enum WallJumpMode
    {
        Predictive,  // A案：事前計測(壁の高さに応じてピッタリの高さでジャンプ)
        Stepwise     // B案：段階的(1マス→ダメ→2マス→ダメ→3マスと上げていく)
    }

    // ーーーフリスビー連携ーーー
    private FrisbeeController trackedFrisbee;
    private float fetchStartDelayTimer = -1f;
    private bool wasFrisbeeDecelerating;

    // ーーージャンプキャッチ軌道情報ーーー
    private Vector2 jumpCatchStartPos;
    private float jumpCatchTargetX;
    private float jumpCatchPeakY;
    private float jumpCatchDuration;
    private float jumpCatchElapsed;

    // ーーー待機モード状態ーーー
    private float waitingTurnTimer = -1f;
    private bool hasTurnedToPlayer;
    private bool waitingPlayerIsAbove;

    // ーーー通常モード復帰直後の衝突再有効化保留状態ーーー
    private bool needCollisionRestoreAfterFinish;

    // ーーー軌跡データーーー
    private List<Vector2> trail = new List<Vector2>();
    private Vector2 lastRecordedPosition;

    // ーーー追従状態ーーー
    private bool playerIsMoving;
    private float playerStateChangeTimer;
    private bool dogShouldFollow;

    // ーーー向き状態ーーー
    private int facingDirection;

    // ーーージャンプ計算結果ーーー
    private float calculatedJumpVelocity;

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
            lastRecordedPosition = player.transform.position;
            trail.Add(lastRecordedPosition);
        }

        facingDirection = 1;
        ApplySpriteFlip();

        // 入力アクションの初期化
        inputActions = new PlayerInputActions();
        throwAction = inputActions.Player.Throw;
    }

    private void OnEnable()
    {
        throwAction.Enable();
    }

    private void OnDisable()
    {
        throwAction.Disable();
    }

    // ーーーリスポーン処理(GameManagerから呼ばれる)ーーー
    // プレイヤーが死亡した時、犬を指定位置に戻して通常モードにリセットする

    public void Respawn(Vector2 position)
    {
        // 位置を設定(Z座標は維持)
        Vector3 newPos = new Vector3(position.x, position.y, transform.position.z);
        transform.position = newPos;

        // BodyTypeをDynamicに戻す(ジャンプキャッチ中だとKinematicになってる可能性)
        rb.bodyType = RigidbodyType2D.Dynamic;

        // 速度をゼロにリセット
        rb.linearVelocity = Vector2.zero;
        rb.angularVelocity = 0f;

        // モードを通常モードに戻す
        currentMode = DogMode.Normal;

        // フリスビー追跡をクリア(GameManager側でフリスビー本体は破壊済み)
        trackedFrisbee = null;
        wasFrisbeeDecelerating = false;
        fetchStartDelayTimer = -1f;

        // 軌跡をクリア(プレイヤーが瞬間移動するので、古い軌跡を引きずらない)
        trail.Clear();
        if (player != null)
        {
            lastRecordedPosition = player.transform.position;
            trail.Add(lastRecordedPosition);
        }

        // 追従状態をリセット
        playerIsMoving = false;
        playerStateChangeTimer = 0f;
        dogShouldFollow = false;

        // ジャンプ予約をクリア
        pendingJumpTimer = -1f;

        // 待機モード関連の状態をクリア
        waitingTurnTimer = -1f;
        hasTurnedToPlayer = false;
        waitingPlayerIsAbove = false;

        // おかえりモード復帰関連の状態をクリア
        needCollisionRestoreAfterFinish = false;

        // すり抜け中のコライダーをすべて再有効化
        ClearAllIgnoredColliders();

        // プレイヤーとの衝突を有効に戻す(待機・おかえりモードの設定が残らないように)
        SetPlayerCollisionIgnore(false);

        // 向きをリセット(犬はプレイヤーの左側にリスポーンする想定なので、右向きにしてプレイヤーを見る形)
        facingDirection = 1;
        ApplySpriteFlip();
    }

    private void Update()
    {
        CheckFrisbeeThrown();
        UpdateFetchStartDelay();
        UpdatePlayerMovingState();
        UpdateFollowDecision();
        RecordPlayerTrail();
        ApplySpriteFlip();
        UpdateIgnoredColliders();

        // おかえりモード終了後、プレイヤーと十分離れたら衝突を再有効化
        UpdateCollisionRestoreAfterFinish();

        // 待機モード中のZ押下でおかえりモードへ
        CheckReturnModeInput();
    }

    private void FixedUpdate()
    {
        CalculateJumpVelocity();

        // ジャンプキャッチの発動判定(全モード共通でフリスビーの状態変化を見る)
        CheckJumpCatchTrigger();

        switch (currentMode)
        {
            case DogMode.Normal:
                HandleJumpSync();
                UpdateDogMovement();
                HandleStuckJump();
                break;

            case DogMode.Fetch:
                UpdateFetchMovement();
                HandleStuckJump();
                CheckWallFallRescueCatch();
                CheckPositionBasedCatch();
                UpdateWaitingCollision();
                break;

            case DogMode.JumpCatching:
                UpdateJumpCatchMovement();
                UpdateCaughtFrisbeePosition();
                break;

            case DogMode.Waiting:
                StopHorizontalMovement();
                UpdateWaitingTurnTimer();
                UpdateWaitingCollision();
                UpdateCaughtFrisbeePosition();
                break;

            case DogMode.Return:
                UpdateReturnMovement();
                HandleStuckJump();
                UpdateWaitingCollision();
                UpdateCaughtFrisbeePosition();
                CheckPlayerNearby();
                break;
        }
    }


    // ーーー待機モード中のZ押下処理ーーー
    // プレイヤーが犬に乗ってたら即フリスビーを渡す
    // 乗ってなかったらおかえりモードに移行(犬がプレイヤーへ走る)

    private void CheckReturnModeInput()
    {
        if (currentMode != DogMode.Waiting)
        {
            return;
        }

        if (!throwAction.WasPressedThisFrame())
        {
            return;
        }

        if (player != null && player.IsRidingDog)
        {
            // 乗ってる場合は即回収
            FinishReturnMode();
        }
        else
        {
            // 乗ってない場合はおかえりモードへ
            EnterReturnMode();
        }
    }


    // ーーーおかえりモードへの遷移ーーー
    // 待機モードの衝突制御(横すり抜け、上は乗れる)をそのまま引き継ぐ
    // (UpdateWaitingCollisionが毎FixedUpdateで位置に応じて正しく切り替えてくれる)

    private void EnterReturnMode()
    {
        currentMode = DogMode.Return;
    }


    // ーーーおかえりモードの移動処理ーーー
    // プレイヤーへ向かって走る。フリスビーは犬の口に固定されたまま追従する

    private void UpdateReturnMovement()
    {
        if (player == null)
        {
            return;
        }

        Vector2 playerPos = player.transform.position;
        float dx = playerPos.x - transform.position.x;
        float distanceX = Mathf.Abs(dx);

        int directionToPlayer;
        if (dx > 0f)
        {
            directionToPlayer = 1;
        }
        else
        {
            directionToPlayer = -1;
        }

        // 通り過ぎを防ぐ：目標が近い場合は速度を抑える
        float maxStepDistance = returnModeMoveSpeed * Time.fixedDeltaTime;

        float currentSpeed;
        if (distanceX < maxStepDistance)
        {
            currentSpeed = distanceX / Time.fixedDeltaTime;
        }
        else
        {
            currentSpeed = returnModeMoveSpeed;
        }

        Vector2 v = rb.linearVelocity;
        v.x = directionToPlayer * currentSpeed;
        rb.linearVelocity = v;

        facingDirection = directionToPlayer;
    }


    // ーーープレイヤー接近の判定(おかえりモード専用)ーーー
    // 距離が近い、またはプレイヤーが犬に乗っていたら、フリスビーを回収して通常モードに戻る

    private void CheckPlayerNearby()
    {
        if (player == null)
        {
            return;
        }

        // プレイヤーが犬に乗ってたら距離関係なく即回収
        if (player.IsRidingDog)
        {
            FinishReturnMode();
            return;
        }

        // 距離が一定以内なら回収
        float distance = Vector2.Distance(transform.position, player.transform.position);

        if (distance <= returnPickupDistance)
        {
            FinishReturnMode();
        }
    }


    // ーーーおかえりモード終了処理ーーー
    // フリスビーを消して、通常モードに戻る
    // 衝突は無効化のまま維持(プレイヤーと重なってる状態で有効化すると押し出されるため)
    // 十分離れたらUpdateCollisionRestoreAfterFinishで自動的に再有効化される

    private void FinishReturnMode()
    {
        // フリスビーを消す
        if (trackedFrisbee != null)
        {
            Destroy(trackedFrisbee.gameObject);
            trackedFrisbee = null;
        }

        // 衝突再有効化を保留(プレイヤーと十分離れてから自動で有効化する)
        needCollisionRestoreAfterFinish = true;

        // 状態リセット
        wasFrisbeeDecelerating = false;
        waitingPlayerIsAbove = false;
        currentMode = DogMode.Normal;
    }


    // ーーーおかえりモード終了後の衝突再有効化チェックーーー
    // フリスビー回収直後はプレイヤーと重なっているので、十分離れるまで衝突無効を維持する
    // 離れたタイミングで通常モードの衝突制御に復帰させる

    private void UpdateCollisionRestoreAfterFinish()
    {
        if (!needCollisionRestoreAfterFinish)
        {
            return;
        }

        if (player == null)
        {
            return;
        }

        float distance = Vector2.Distance(transform.position, player.transform.position);

        if (distance > collisionResetDistance)
        {
            SetPlayerCollisionIgnore(false);
            needCollisionRestoreAfterFinish = false;
        }
    }


    // ーーー待機モードへの遷移ーーー

    private void EnterWaitingMode()
    {
        currentMode = DogMode.Waiting;
        waitingTurnTimer = turnToPlayerDelay;
        hasTurnedToPlayer = false;

        ClearAllIgnoredColliders();

        waitingPlayerIsAbove = false;
        SetPlayerCollisionIgnore(true);
    }


    // ーーー待機モード中のプレイヤー振り向きタイマー更新ーーー

    private void UpdateWaitingTurnTimer()
    {
        if (hasTurnedToPlayer)
        {
            return;
        }

        waitingTurnTimer -= Time.fixedDeltaTime;

        if (waitingTurnTimer <= 0f)
        {
            TurnToPlayer();
            hasTurnedToPlayer = true;
        }
    }


    // ーーープレイヤーの方を向く処理ーーー

    private void TurnToPlayer()
    {
        if (player == null)
        {
            return;
        }

        float dx = player.transform.position.x - transform.position.x;

        if (dx > 0f)
        {
            facingDirection = 1;
        }
        else
        {
            facingDirection = -1;
        }
    }


    // ーーー待機・おかえりモード中の衝突制御ーーー
    // プレイヤーが犬の上にいる時だけ衝突を有効化(マリオのすり抜け床と同等の挙動)
    // 横や下にいる時はすり抜け

    private void UpdateWaitingCollision()
    {
        if (player == null)
        {
            return;
        }

        Vector2 dogPos = transform.position;
        Vector2 playerPos = player.transform.position;

        bool playerIsAboveNow = (playerPos.y - dogPos.y) >= waitingPlayerAboveThreshold;

        if (playerIsAboveNow != waitingPlayerIsAbove)
        {
            SetPlayerCollisionIgnore(!playerIsAboveNow);
            waitingPlayerIsAbove = playerIsAboveNow;
        }
    }


    // ーーー全てのすり抜け状態を解除ーーー

    private void ClearAllIgnoredColliders()
    {
        for (int i = 0; i < ignoredColliders.Count; i++)
        {
            Collider2D ignored = ignoredColliders[i];
            if (ignored != null)
            {
                Physics2D.IgnoreCollision(ignored, boxCollider, false);
            }
        }
        ignoredColliders.Clear();
    }


    // ーーープレイヤーとの衝突を有効/無効に切り替えるヘルパーーーー

    private void SetPlayerCollisionIgnore(bool ignore)
    {
        if (player == null)
        {
            return;
        }

        Collider2D playerCollider = player.GetComponent<Collider2D>();
        if (playerCollider == null)
        {
            return;
        }

        Physics2D.IgnoreCollision(playerCollider, boxCollider, ignore);
    }


    // ーーージャンプキャッチ発動判定ーーー
    // フリスビーが減速状態に入った瞬間(=カーソル到達 or 壁衝突の瞬間)を検知

    private void CheckJumpCatchTrigger()
    {
        if (trackedFrisbee == null)
        {
            wasFrisbeeDecelerating = false;
            return;
        }

        bool isDecelNow = trackedFrisbee.IsDecelerating;

        if (!wasFrisbeeDecelerating && isDecelNow)
        {
            TryStartJumpCatch();
        }

        wasFrisbeeDecelerating = isDecelNow;
    }


    // ーーージャンプキャッチ発動判定の本体ーーー

    private void TryStartJumpCatch()
    {
        if (currentMode != DogMode.Fetch)
        {
            return;
        }

        if (!CheckGrounded())
        {
            return;
        }

        Vector2 dogPos = transform.position;
        Vector2 frisbeePos = trackedFrisbee.Position;
        float distance = Vector2.Distance(dogPos, frisbeePos);

        if (distance > jumpCatchDistance)
        {
            return;
        }

        float heightDiff = frisbeePos.y - dogPos.y;
        if (heightDiff < jumpCatchMinHeight)
        {
            return;
        }

        StartJumpCatch(false);  // 通常のジャンプキャッチ：カーソル位置目標
    }


    // ーーー壁ヒット後の落下フリスビーへの救済ジャンプキャッチーーー
    // フリスビーが壁にぶつかって落下中、犬がジャンプキャッチ範囲内に入ったら発動
    // (段差で詰まった時の救済策、Fetchモード中のみ毎フレーム判定)
    // フリスビーが犬より高い位置にある時だけ発動(低い位置なら普通に走って取る)

    private void CheckWallFallRescueCatch()
    {
        if (trackedFrisbee == null)
        {
            return;
        }

        if (!trackedFrisbee.DidHitWall)
        {
            return;
        }

        if (!trackedFrisbee.IsDecelerating)
        {
            return;
        }

        if (!CheckGrounded())
        {
            return;
        }

        Vector2 dogPos = transform.position;
        Vector2 frisbeePos = trackedFrisbee.Position;
        float distance = Vector2.Distance(dogPos, frisbeePos);

        if (distance > jumpCatchDistance)
        {
            return;
        }

        // フリスビーが犬より十分高い位置にある時だけ発動
        // (低い位置なら普通に走って取りに行ける、ジャンプキャッチで壁の向こうに飛ぶのを防ぐ)
        float heightDiff = frisbeePos.y - dogPos.y;
        if (heightDiff < jumpCatchMinHeight)
        {
            return;
        }

        // 範囲内に入った：ジャンプキャッチ発動(フリスビー位置を目標に、カーソル方向には飛ばない)
        StartJumpCatch(true);
    }

    // ーーー座標ベースのフリスビーキャッチ判定(保険)ーーー
    // 犬とフリスビーのX座標が近い + Y座標も大差なければキャッチ
    // フリスビーが減速or着地状態の時だけ発動(Flying中はジャンプキャッチを尊重、カーソル到達前のキャッチを防ぐ)
    // コライダー重なりや既存のキャッチで取りこぼしたケースの保険

    private void CheckPositionBasedCatch()
    {
        if (trackedFrisbee == null)
        {
            return;
        }

        // Flying中はNG(ジャンプキャッチを尊重、カーソル到達前のキャッチを防ぐ)
        if (!trackedFrisbee.IsDecelerating && !trackedFrisbee.IsLanded)
        {
            return;
        }

        Vector2 dogPos = transform.position;
        Vector2 frisbeePos = trackedFrisbee.Position;

        float dx = Mathf.Abs(frisbeePos.x - dogPos.x);
        float dy = Mathf.Abs(frisbeePos.y - dogPos.y);

        if (dx > positionCatchXThreshold)
        {
            return;
        }

        if (dy > positionCatchYThreshold)
        {
            return;
        }

        // 範囲内：キャッチ発動
        ExecuteCatch(trackedFrisbee);
    }


    // ーーージャンプキャッチ開始処理ーーー
    // useFrisbeePosAsTarget=trueの場合はフリスビーの現在位置を着地X目標とする
    // (壁ヒット救済キャッチ用、犬が壁を突き抜けるのを防ぐ)
    // useFrisbeePosAsTarget=falseの場合はカーソル位置を着地X目標とする
    // (通常のジャンプキャッチ用)

    private void StartJumpCatch(bool useFrisbeePosAsTarget)
    {
        currentMode = DogMode.JumpCatching;

        trackedFrisbee.OnCaught();

        jumpCatchStartPos = transform.position;
        jumpCatchPeakY = trackedFrisbee.Position.y;

        if (useFrisbeePosAsTarget)
        {
            jumpCatchTargetX = trackedFrisbee.Position.x;
        }
        else if (cursor != null)
        {
            jumpCatchTargetX = cursor.transform.position.x;
        }
        else
        {
            jumpCatchTargetX = trackedFrisbee.Position.x;
        }

        // 滞空時間を物理計算から逆算：T = 2 * sqrt(2h/g)
        float h = jumpCatchPeakY - jumpCatchStartPos.y;
        float g = Mathf.Abs(Physics2D.gravity.y) * rb.gravityScale;

        if (g > 0f && h > 0f)
        {
            jumpCatchDuration = 2f * Mathf.Sqrt(2f * h / g);
        }
        else
        {
            jumpCatchDuration = 0.5f;
        }

        jumpCatchElapsed = 0f;

        rb.bodyType = RigidbodyType2D.Kinematic;
        rb.linearVelocity = Vector2.zero;

        if (jumpCatchTargetX > jumpCatchStartPos.x)
        {
            facingDirection = 1;
        }
        else
        {
            facingDirection = -1;
        }
    }


    // ーーージャンプキャッチ中の移動処理ーーー

    private void UpdateJumpCatchMovement()
    {
        jumpCatchElapsed += Time.fixedDeltaTime;

        float t = jumpCatchElapsed / jumpCatchDuration;

        if (t >= 1f)
        {
            t = 1f;
            ApplyJumpCatchPosition(t);
            EndJumpCatch();
            return;
        }

        ApplyJumpCatchPosition(t);
    }


    // ーーージャンプキャッチ軌道の位置を計算・適用ーーー

    private void ApplyJumpCatchPosition(float t)
    {
        // 横方向：二次関数イーズアウト 1 - (1-t)^2
        float easedT = 1f - (1f - t) * (1f - t);
        float currentX = Mathf.Lerp(jumpCatchStartPos.x, jumpCatchTargetX, easedT);

        // 縦方向：4t(1-t)で放物線(t=0で0、t=0.5で1、t=1で0)
        float heightT = 4f * t * (1f - t);
        float currentY = jumpCatchStartPos.y + (jumpCatchPeakY - jumpCatchStartPos.y) * heightT;

        rb.MovePosition(new Vector2(currentX, currentY));
    }


    // ーーージャンプキャッチ終了処理ーーー

    private void EndJumpCatch()
    {
        rb.bodyType = RigidbodyType2D.Dynamic;
        rb.linearVelocity = Vector2.zero;

        EnterWaitingMode();
    }


    // ーーーフリスビーキャッチの検知(地上キャッチ)ーーー

    private void OnTriggerStay2D(Collider2D other)
    {
        if (currentMode != DogMode.Fetch)
        {
            return;
        }

        FrisbeeController frisbee = other.GetComponent<FrisbeeController>();

        if (frisbee == null)
        {
            return;
        }

        if (frisbee != trackedFrisbee)
        {
            return;
        }

        if (!frisbee.IsLanded)
        {
            return;
        }

        ExecuteCatch(frisbee);
    }


    // ーーーキャッチ処理(地上キャッチ)ーーー

    private void ExecuteCatch(FrisbeeController frisbee)
    {
        frisbee.OnCaught();
        EnterWaitingMode();
        StopHorizontalMovement();
    }


    // ーーーキャッチ中のフリスビー位置を更新ーーー

    private void UpdateCaughtFrisbeePosition()
    {
        if (trackedFrisbee == null)
        {
            return;
        }

        if (!trackedFrisbee.IsCaught)
        {
            return;
        }

        Vector2 mouthPos = GetMouthWorldPosition();
        trackedFrisbee.SetCaughtPosition(mouthPos);
    }


    // ーーー犬の口のワールド座標を計算ーーー

    private Vector2 GetMouthWorldPosition()
    {
        Vector2 center = (Vector2)transform.position;
        return new Vector2(
            center.x + (mouthOffset.x * facingDirection),
            center.y + mouthOffset.y
        );
    }


    // ーーーフリスビー投擲の検知ーーー

    private void CheckFrisbeeThrown()
    {
        if (frisbeeThrower == null)
        {
            return;
        }

        FrisbeeController currentFrisbee = frisbeeThrower.CurrentFrisbee;

        if (currentMode == DogMode.Normal && currentFrisbee != null && currentFrisbee != trackedFrisbee)
        {
            trackedFrisbee = currentFrisbee;
            fetchStartDelayTimer = fetchStartDelay;
            wasFrisbeeDecelerating = false;
        }
    }


    // ーーー行ってこいモード開始のディレイタイマー更新ーーー

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

            // 通常モードでのすり抜け状態をリセット(Fetch中はUpdateWaitingCollisionで位置ベース管理に切り替えるため)
            ClearAllIgnoredColliders();
            waitingPlayerIsAbove = false;

            // Fetch開始時、プレイヤーが犬の上にいなければ衝突無効化(横から突っ込んだ時に押されないように)
            // 以降はUpdateWaitingCollisionが位置ベースで切り替えてくれる
            SetPlayerCollisionIgnore(true);
        }
    }


    // ーーー行ってこいモードの移動処理ーーー
    // フリスビーが犬より下にある+接地中の時は、段差から落とすために強制前進する

    private void UpdateFetchMovement()
    {
        if (trackedFrisbee == null)
        {
            // Fetchモードで衝突無効化されてる可能性があるので元に戻す
            SetPlayerCollisionIgnore(false);
            currentMode = DogMode.Normal;
            return;
        }

        Vector2 frisbeePos = trackedFrisbee.Position;
        float dx = frisbeePos.x - transform.position.x;
        float dy = frisbeePos.y - transform.position.y;
        float distanceX = Mathf.Abs(dx);

        int directionToFrisbee;
        if (dx > 0f)
        {
            directionToFrisbee = 1;
        }
        else
        {
            directionToFrisbee = -1;
        }

        // フリスビーが犬より十分下にある + 接地してる場合は、段差から落とすために強制前進
        bool needDropDown = (dy <= -fetchDropDownThreshold) && CheckGrounded();

        float maxStepDistance = fetchModeMoveSpeed * Time.fixedDeltaTime;

        float currentSpeed;
        if (needDropDown)
        {
            // 段差から落とす：X差に関係なく最大速度で進む
            currentSpeed = fetchModeMoveSpeed;
        }
        else if (distanceX < maxStepDistance)
        {
            // 通常時：通り過ぎ防止のため、X差が小さいときは速度を抑える
            currentSpeed = distanceX / Time.fixedDeltaTime;
        }
        else
        {
            currentSpeed = fetchModeMoveSpeed;
        }

        Vector2 v = rb.linearVelocity;
        v.x = directionToFrisbee * currentSpeed;
        rb.linearVelocity = v;

        facingDirection = directionToFrisbee;
    }


    // ーーー水平移動を停止ーーー

    private void StopHorizontalMovement()
    {
        Vector2 v = rb.linearVelocity;
        v.x = 0f;
        rb.linearVelocity = v;
    }


    // ーーー衝突発生時の処理(プレイヤーとのすり抜け制御、通常モード/Fetch/JumpCatching用)ーーー
    // 待機モード・おかえりモード中は別の仕組み(UpdateWaitingCollision)で衝突制御するのでスキップ

    private void OnCollisionEnter2D(Collision2D collision)
    {
        if (currentMode == DogMode.Waiting || currentMode == DogMode.Return || currentMode == DogMode.Fetch)
        {
            return;
        }

        if (collision.gameObject != player.gameObject)
        {
            return;
        }

        Vector2 normal = collision.GetContact(0).normal;

        if (normal.y <= -upwardCollisionThreshold)
        {
            return;
        }

        Physics2D.IgnoreCollision(collision.collider, boxCollider, true);

        if (!ignoredColliders.Contains(collision.collider))
        {
            ignoredColliders.Add(collision.collider);
        }
    }


    // ーーーすり抜け中コライダーの再有効化チェックーーー
    // 待機モード・おかえりモード中はこの処理を行わない(別ロジックで管理してる)

    private void UpdateIgnoredColliders()
    {
        if (currentMode == DogMode.Waiting || currentMode == DogMode.Return || currentMode == DogMode.Fetch)
        {
            return;
        }

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


    // ーーージャンプ初速度の逆算(通常モードのジャンプ連動用)ーーー

    private void CalculateJumpVelocity()
    {
        float effectiveGravity = Mathf.Abs(Physics2D.gravity.y) * rb.gravityScale;

        if (effectiveGravity <= 0f)
        {
            calculatedJumpVelocity = 0f;
            return;
        }

        calculatedJumpVelocity = Mathf.Sqrt(2f * effectiveGravity * jumpMaxHeight);
    }


    // ーーー指定高さに対するジャンプ初速度を計算ーーー
    // h = v² / 2g  →  v = √(2gh)

    private float CalculateJumpVelocityForHeight(float heightInUnits)
    {
        float effectiveGravity = Mathf.Abs(Physics2D.gravity.y) * rb.gravityScale;
        if (effectiveGravity <= 0f)
        {
            return 0f;
        }
        return Mathf.Sqrt(2f * effectiveGravity * heightInUnits);
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
    // 接地中 + 進行方向に壁あり、の時に発動
    // wallJumpModeに応じてA案(事前計測) or B案(段階的)で必要な高さを決める

    private void HandleStuckJump()
    {
        if (currentMode == DogMode.Normal && !dogShouldFollow)
        {
            return;
        }

        if (!CheckGrounded())
        {
            return;
        }

        if (!CheckWallAhead())
        {
            return;
        }

        int requiredMasses;
        switch (wallJumpMode)
        {
            case WallJumpMode.Predictive:
                requiredMasses = MeasureWallHeightPredictive();
                break;
            case WallJumpMode.Stepwise:
                requiredMasses = 1;  // TODO: B案実装、ひとまず1マスにしておく
                break;
            default:
                requiredMasses = 1;
                break;
        }

        if (requiredMasses <= 0)
        {
            return;
        }

        // 必要な高さに応じたジャンプ初速度を計算してジャンプ
        float requiredHeightInUnits = requiredMasses * cellSize;
        float jumpVelocity = CalculateJumpVelocityForHeight(requiredHeightInUnits);

        Vector2 v = rb.linearVelocity;
        v.y = jumpVelocity;
        rb.linearVelocity = v;
    }


    // ーーーA案：壁の高さを事前計測ーーー
    // 進行方向の壁の真上を1マス、2マス、3マスと順にチェック
    // 最初に何もない高さがジャンプ目標
    // 全部壁ならmaxAutoJumpHeightでジャンプ(永遠ジャンプ)

    private int MeasureWallHeightPredictive()
    {
        Vector2 dogPos = transform.position;
        float probeX = dogPos.x + (wallProbeOffsetX * facingDirection);

        for (int h = 1; h <= maxAutoJumpHeight; h++)
        {
            // 犬の足元(コライダー下端)からhマス上の位置を測る
            float probeY = GetGroundCheckOrigin().y + (h * cellSize);
            Vector2 origin = new Vector2(probeX, probeY);

            Collider2D hit = Physics2D.OverlapBox(origin, wallProbeSize, 0f, groundLayer);
            if (hit == null)
            {
                return h;  // この高さなら越えられる
            }
        }

        return maxAutoJumpHeight;  // 越えられない壁、最大高さで永遠ジャンプ
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

        int directionToTarget;
        if (dx > 0f)
        {
            directionToTarget = 1;
        }
        else
        {
            directionToTarget = -1;
        }

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
        if (boxCollider == null)
        {
            boxCollider = GetComponent<BoxCollider2D>();
        }

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

        Gizmos.color = Color.magenta;
        int dirForGizmo;
        if (facingDirection == 0)
        {
            dirForGizmo = 1;
        }
        else
        {
            dirForGizmo = facingDirection;
        }
        Vector2 mouthPos = new Vector2(
            transform.position.x + (mouthOffset.x * dirForGizmo),
            transform.position.y + mouthOffset.y
        );
        Gizmos.DrawWireSphere(mouthPos, 0.1f);

        // ジャンプキャッチ判定距離(黄色の円)
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(transform.position, jumpCatchDistance);

        // おかえりモード時のフリスビー回収判定距離(オレンジの円)
        Gizmos.color = new Color(1f, 0.5f, 0f);
        Gizmos.DrawWireSphere(transform.position, returnPickupDistance);

        // 待機・おかえりモード時の上空判定ライン(青い水平線)
        Gizmos.color = Color.blue;
        Vector3 lineStart = new Vector3(transform.position.x - 1f, transform.position.y + waitingPlayerAboveThreshold, 0f);
        Vector3 lineEnd = new Vector3(transform.position.x + 1f, transform.position.y + waitingPlayerAboveThreshold, 0f);
        Gizmos.DrawLine(lineStart, lineEnd);

        // 壁の高さ計測位置の可視化(進行方向にだけ表示、maxAutoJumpHeightマス分)
        if (boxCollider != null)
        {
            float probeX = transform.position.x + (wallProbeOffsetX * dirForGizmo);
            float groundY = GetGroundCheckOrigin().y;

            for (int h = 1; h <= maxAutoJumpHeight; h++)
            {
                Vector2 origin = new Vector2(probeX, groundY + (h * cellSize));

                if (Application.isPlaying)
                {
                    Collider2D hit = Physics2D.OverlapBox(origin, wallProbeSize, 0f, groundLayer);
                    Gizmos.color = (hit != null) ? Color.red : Color.green;
                }
                else
                {
                    Gizmos.color = Color.gray;
                }

                Gizmos.DrawWireCube(origin, wallProbeSize);
            }
        }

        if (!Application.isPlaying || trail == null || trail.Count == 0)
        {
            return;
        }

        Gizmos.color = Color.white;
        for (int i = 0; i < trail.Count - 1; i++)
        {
            Gizmos.DrawLine(trail[i], trail[i + 1]);
        }

        Gizmos.color = Color.gray;
        for (int i = 0; i < trail.Count; i++)
        {
            Gizmos.DrawWireSphere(trail[i], 0.05f);
        }

        Gizmos.color = Color.green;
        Vector2 target = GetTrailPositionAtDistance(followDistance);
        Gizmos.DrawWireSphere(target, 0.2f);

        Gizmos.color = Color.cyan;
        Gizmos.DrawWireSphere(transform.position, collisionResetDistance);
    }
}