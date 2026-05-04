using UnityEngine;
using UnityEngine.InputSystem;

[RequireComponent(typeof(Rigidbody2D))]
[RequireComponent(typeof(BoxCollider2D))]
[RequireComponent(typeof(SpriteRenderer))]
public class PlayerController : MonoBehaviour
{
    // ーーー移動ーーー
    [Header("移動スピード(ユニット/秒)")]
    [SerializeField] private float moveSpeed = 5f;

    // ーーージャンプーーー
    [Header("ジャンプの最高到達点の高さ(マス数)")]
    [SerializeField] private float jumpMaxHeight = 1f;

    // ※ 重力はRigidbody 2DのGravity ScaleとUnityの重力設定（Physics2D.gravity）から決まる
    // ※ Inspectorで直接Gravity Scaleをいじれば、ジャンプの感触（落下の速さ）が変わる
    // ※ ジャンプ初速度はそれらの値から自動で逆算される

    // ーーー接地判定ーーー
    [Header("地面として扱うレイヤー(Groundのみチェック)")]
    [SerializeField] private LayerMask groundLayer;

    [Header("犬として扱うレイヤー(Dogのみチェック、乗り判定で使う)")]
    [SerializeField] private LayerMask dogLayer;

    [Header("接地判定ボックスのサイズ(X=幅、Y=厚み)")]
    [SerializeField] private Vector2 groundCheckSize = new Vector2(0.9f, 0.1f);

    [Header("接地判定ボックスのY方向オフセット(コライダー下端からの相対位置、負の値でさらに下)")]
    [SerializeField] private float groundCheckOffsetY = 0f;

    [Header("乗ってる判定のマージン(犬の上端よりプレイヤーがこの値以上上にいたら乗ってる扱い、0で犬の上端ピッタリ)")]
    [SerializeField] private float ridingDogMargin = 0f;

    // ーーー向きーーー
    [Header("ゲーム開始時のプレイヤーの向き(+1=右、-1=左)")]
    [SerializeField] private int initialFacingDirection = 1;

    [Header("元のスプライト画像が右向きならtrue、左向きならfalse")]
    [SerializeField] private bool spriteOriginallyFacesRight = true;

    // ーーー死亡・リスポーン関連ーーー
    [Header("ーーーーーーー ここから下は死亡・リスポーン関連 ーーーーーーー")]
    [Header("リスタート管理のGameManager")]
    [SerializeField] private GameManager gameManager;

    [Header("スパイクとして扱うレイヤー(Spikeのみ、触れたら死亡)")]
    [SerializeField] private LayerMask spikeLayer;

    [Header("死亡中のプレイヤーの色")]
    [SerializeField] private Color deathColor = Color.red;

    [Header("死亡してからリスタートまでの時間(秒)")]
    [SerializeField] private float deathFreezeDuration = 0.5f;

    // ーーー内部参照ーーー
    private Rigidbody2D rb;
    private BoxCollider2D boxCollider;
    private SpriteRenderer spriteRenderer;

    // ーーー入力システム関連ーーー
    private PlayerInputActions inputActions;  // 自動生成されたInput Actionsクラス
    private InputAction moveAction;           // 移動アクション(Vector2)
    private InputAction jumpAction;           // ジャンプアクション(Button)

    // ーーー入力状態ーーー
    private float horizontalInput;  // 水平入力値(-1〜+1)
    private bool jumpRequested;     // このフレームでジャンプボタンが押されたか

    // ーーージャンプ計算結果ーーー
    private float calculatedJumpVelocity;  // 最高到達点の高さと現在の重力から逆算したジャンプ初速度

    // ーーージャンプ検知用ーーー
    // このフレームでジャンプが発動したか(他のスクリプトから検知される、犬の連動ジャンプなどで使用)
    private bool justJumped;

    // ーーー向き状態ーーー
    private int facingDirection;  // 現在の向き(+1=右、-1=左)

    // ーーー死亡状態管理ーーー
    private bool isDead;                          // 死亡中かどうか
    private float deathFreezeTimer;               // 死亡フリーズの残り時間
    private Color originalColor;                  // 死亡前の元の色(リスポーン時に戻す)
    private RigidbodyType2D originalBodyType;     // 死亡前の元のBodyType(リスポーン時に戻す)

    // ーーー外部公開プロパティーーー
    public bool IsGrounded => CheckGrounded();
    public bool IsRidingDog => CheckRidingDog();
    public float CalculatedJumpVelocity => calculatedJumpVelocity;
    public int FacingDirection => facingDirection;
    public bool JustJumped => justJumped;
    public bool IsDead => isDead;


    // ーーーUnityイベントーーー

    private void Awake()
    {
        // コンポーネント参照を取得
        rb = GetComponent<Rigidbody2D>();
        boxCollider = GetComponent<BoxCollider2D>();
        spriteRenderer = GetComponent<SpriteRenderer>();

        // プレイヤーが衝突時に回転しないように固定
        rb.freezeRotation = true;

        // Input Actionsのインスタンスを生成し、Move/Jumpアクションへの参照を取得
        inputActions = new PlayerInputActions();
        moveAction = inputActions.Player.Move;
        jumpAction = inputActions.Player.Jump;

        // 初期の向きを設定(Inspectorで指定された方向)
        facingDirection = initialFacingDirection;

        // 起動時にスプライトの反転状態も初期向きに合わせる
        ApplySpriteFlip();

        // 元の色とBodyTypeを記録(リスポーン時に戻すため)
        originalColor = spriteRenderer.color;
        originalBodyType = rb.bodyType;
    }

    private void OnEnable()
    {
        // このオブジェクトが有効になったらPlayerアクションマップを有効化
        // これがないと入力を受け取れない
        inputActions.Player.Enable();
    }

    private void OnDisable()
    {
        // 無効化時にアクションマップも無効化(メモリリーク防止)
        inputActions.Player.Disable();
    }

    private void Update()
    {
        // 死亡中はフリーズタイマーを更新するだけ、他は何もしない
        if (isDead)
        {
            UpdateDeathFreezeTimer();
            return;
        }

        // 入力読み取りはUpdateで(フレームごとに最新の入力を取りたいため)
        ReadInputs();

        // 向き判定もUpdateで(入力に応じて毎フレーム更新するため)
        UpdateFacingDirection();

        // 向きの状態に合わせてスプライトを反転
        ApplySpriteFlip();
    }

    private void FixedUpdate()
    {
        // 死亡中は物理処理を全部スキップ(完全フリーズ)
        if (isDead)
        {
            return;
        }

        // ジャンプ初速度は重力に依存するので毎FixedUpdateで再計算する
        // 実行中にGravity Scaleが変更されても即座に反映できるようにするため
        CalculateJumpVelocity();

        // 物理演算系の処理は固定間隔のFixedUpdateで(フレームレートに依存させないため)
        HorizontalMove();
        HandleJump();
    }


    // ーーースパイク接触検知ーーー
    // スパイクレイヤーのオブジェクトに触れたら死亡

    private void OnTriggerEnter2D(Collider2D other)
    {
        // 既に死亡中なら何もしない
        if (isDead)
        {
            return;
        }

        // 触れたオブジェクトのレイヤーがspikeLayerに含まれているか確認
        // (LayerMaskのビット演算で判定)
        int otherLayerBit = 1 << other.gameObject.layer;
        if ((spikeLayer.value & otherLayerBit) == 0)
        {
            return;
        }

        EnterDeathState();
    }


    // ーーー死亡状態への移行ーーー
    // 色を変更、入力無効化、物理停止、フリーズタイマー開始

    private void EnterDeathState()
    {
        isDead = true;
        deathFreezeTimer = deathFreezeDuration;

        // 色を死亡色に変更
        spriteRenderer.color = deathColor;

        // 速度をゼロにして、Kinematicにして重力も止める
        rb.linearVelocity = Vector2.zero;
        rb.angularVelocity = 0f;
        rb.bodyType = RigidbodyType2D.Kinematic;

        // 入力アクションを無効化(押されてもReadValueやWasPressedThisFrameが反応しなくなる)
        inputActions.Player.Disable();
    }


    // ーーー死亡フリーズタイマーの更新ーーー
    // タイマーが0になったらGameManagerにリスタート要求

    private void UpdateDeathFreezeTimer()
    {
        deathFreezeTimer -= Time.deltaTime;

        if (deathFreezeTimer <= 0f)
        {
            if (gameManager != null)
            {
                gameManager.ExecuteRestart();
            }
        }
    }


    // ーーーリスポーン処理(GameManagerから呼ばれる)ーーー
    // 指定された位置と向きで、プレイヤーを復帰させる

    public void Respawn(Vector2 position, int facing)
    {
        // 位置を設定(Z座標は維持)
        Vector3 newPos = new Vector3(position.x, position.y, transform.position.z);
        transform.position = newPos;

        // 向きを設定
        facingDirection = facing;
        ApplySpriteFlip();

        // 色を元に戻す
        spriteRenderer.color = originalColor;

        // BodyTypeを元に戻す(Dynamic等)
        rb.bodyType = originalBodyType;

        // 速度をゼロにリセット(残ってる速度を引き継がないように)
        rb.linearVelocity = Vector2.zero;
        rb.angularVelocity = 0f;

        // 入力を有効化
        inputActions.Player.Enable();

        // 入力状態もクリア(死亡中に押された入力が引き継がれないように)
        horizontalInput = 0f;
        jumpRequested = false;

        // 死亡状態を解除
        isDead = false;
    }


    // ーーー入力読み取りーーー

    private void ReadInputs()
    {
        // Moveアクションは常時値を持つValue型なのでReadValueで取得
        // 戻り値はVector2だが、今は左右移動だけなのでX成分のみ使う
        Vector2 moveValue = moveAction.ReadValue<Vector2>();
        horizontalInput = moveValue.x;

        // Jumpアクションは押された瞬間だけ反応したいのでWasPressedThisFrameを使う
        // ReadValueだと押しっぱなしの間ずっとtrueになってしまう
        if (jumpAction.WasPressedThisFrame())
        {
            jumpRequested = true;
        }
    }


    // ーーー向き更新ーーー
    // 入力が右方向ならfacingDirectionを+1、左方向なら-1にする
    // 入力が0(止まっている)ときは何もしない＝最後の向きが維持される

    private void UpdateFacingDirection()
    {
        if (horizontalInput > 0f)
        {
            facingDirection = 1;
        }
        else if (horizontalInput < 0f)
        {
            facingDirection = -1;
        }
        // 入力が0のときは更新しない(最後の向きを保持)
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


    // ーーージャンプ初速度の逆算ーーー
    // 最高到達点の高さhと、現在のプレイヤーにかかる重力gから、必要な初速度v0を計算する

    private void CalculateJumpVelocity()
    {
        // プレイヤーにかかる実際の重力加速度を計算する
        // Physics2D.gravityは負のY方向を持つベクトルなので絶対値を取る
        // それにRigidbody 2DのGravity Scaleを掛けたものが、このプレイヤーが受ける重力の大きさ
        float effectiveGravity = Mathf.Abs(Physics2D.gravity.y) * rb.gravityScale;

        // 重力が0以下のときは0除算やマイナスのルートを避けるためガード
        if (effectiveGravity <= 0f)
        {
            calculatedJumpVelocity = 0f;
            return;
        }

        // エネルギー保存則：(1/2) * v0² = g * h → v0 = √(2 * g * h)
        // つまり、高さhまで到達するために必要な初速度は √(2gh)
        calculatedJumpVelocity = Mathf.Sqrt(2f * effectiveGravity * jumpMaxHeight);
    }


    // ーーー水平移動ーーー
    // プレイヤー入力がある時は普通の歩き、入力がない時は犬の上に乗ってれば犬の速度に追従

    private void HorizontalMove()
    {
        Vector2 v = rb.linearVelocity;

        if (horizontalInput != 0f)
        {
            // プレイヤーが入力してる時：普通の歩き(犬の上でも地面でも同じ操作感)
            v.x = horizontalInput * moveSpeed;
        }
        else
        {
            // プレイヤー入力なし：犬の上に乗ってれば犬と一緒に動く、地面なら静止
            v.x = GetRidingDogVelocityX();
        }

        rb.linearVelocity = v;
    }


    // ーーージャンプ処理ーーー

    private void HandleJump()
    {
        // 今フレームのジャンプ発動フラグはまずfalseにリセット
        justJumped = false;

        // ジャンプリクエストがあり、かつ接地しているときだけジャンプ発動
        if (jumpRequested && IsGrounded)
        {
            Vector2 v = rb.linearVelocity;
            // Y速度を計算した初速度で上書き(既存のY速度は破棄)
            v.y = calculatedJumpVelocity;
            rb.linearVelocity = v;

            // ジャンプ発動した瞬間のフラグを立てる(他のスクリプトから検知される)
            justJumped = true;
        }

        // リクエストは1フレームで消費するため毎回falseに戻す
        jumpRequested = false;
    }


    // ーーー接地判定ーーー
    // 足元の判定ボックス内に「地面」または「犬」のコライダーがあれば接地
    // GroundとDog両方のレイヤーをまとめて見る

    private bool CheckGrounded()
    {
        Vector2 origin = GetGroundCheckOrigin();

        // groundLayerとdogLayerを合わせたマスクで判定
        LayerMask combinedMask = groundLayer | dogLayer;

        Collider2D hit = Physics2D.OverlapBox(origin, groundCheckSize, 0f, combinedMask);

        return hit != null;
    }


    // ーーー犬に乗っているか判定ーーー
    // 足元の判定ボックスに「犬」レイヤーのコライダーがあって、かつプレイヤーが犬の上端より上にいる時だけtrue
    // (横で重なってるだけだと、コライダーが触れててもfalseになる)

    private bool CheckRidingDog()
    {
        Vector2 origin = GetGroundCheckOrigin();

        // dogLayerだけを対象に判定
        Collider2D hit = Physics2D.OverlapBox(origin, groundCheckSize, 0f, dogLayer);

        if (hit == null)
        {
            return false;
        }

        // コライダーは触れてるが、本当に「上に乗ってる」のかをY座標で確認
        // 犬のコライダーの上端より上にプレイヤーがいる時だけ「乗ってる」と判定
        // (横で重なってる時は乗ってない扱いにして、犬の動きに引っ張られるのを防ぐ)
        float dogTopY = GetDogTopY(hit);
        float playerY = transform.position.y;

        return (playerY - dogTopY) >= ridingDogMargin;
    }


    // ーーー乗ってる犬のX速度を取得ーーー
    // 犬の上に乗っていない、または検出できない場合は0を返す
    // (CheckRidingDogと同じ判定基準)

    private float GetRidingDogVelocityX()
    {
        Vector2 origin = GetGroundCheckOrigin();
        Collider2D hit = Physics2D.OverlapBox(origin, groundCheckSize, 0f, dogLayer);

        if (hit == null)
        {
            return 0f;
        }

        // 本当に犬の上に乗ってるかY座標で確認
        // (横で重なってる時は犬の速度を継承させない)
        float dogTopY = GetDogTopY(hit);
        float playerY = transform.position.y;

        if ((playerY - dogTopY) < ridingDogMargin)
        {
            return 0f;
        }

        Rigidbody2D dogRb = hit.attachedRigidbody;
        if (dogRb == null)
        {
            return 0f;
        }

        return dogRb.linearVelocity.x;
    }


    // ーーー犬のコライダーの上端のY座標を取得ーーー
    // BoxCollider2Dならoffsetとsizeから直接計算、それ以外はboundsから取得
    // 犬のスケール(transform.lossyScale)も考慮する

    private float GetDogTopY(Collider2D dogCollider)
    {
        BoxCollider2D dogBox = dogCollider as BoxCollider2D;

        if (dogBox != null)
        {
            // BoxCollider2Dの場合：オフセットとサイズから上端を計算
            Vector2 dogPos = dogCollider.transform.position;
            float yScale = Mathf.Abs(dogCollider.transform.lossyScale.y);
            float dogCenterY = dogPos.y + dogBox.offset.y * yScale;
            float dogHalfHeight = dogBox.size.y * yScale * 0.5f;
            return dogCenterY + dogHalfHeight;
        }

        // BoxCollider2D以外の場合：コライダーのbounds(ワールド境界)から上端を取得
        return dogCollider.bounds.max.y;
    }


    // ーーー接地判定ボックスの中心位置を計算ーーー

    private Vector2 GetGroundCheckOrigin()
    {
        Vector2 colliderCenter = (Vector2)transform.position + boxCollider.offset * (Vector2)transform.lossyScale;
        float colliderBottomY = colliderCenter.y - (boxCollider.size.y * Mathf.Abs(transform.lossyScale.y) * 0.5f);
        return new Vector2(colliderCenter.x, colliderBottomY + groundCheckOffsetY);
    }


    // ーーーGizmos(接地判定ボックスの可視化)ーーー

    private void OnDrawGizmos()
    {
        if (boxCollider == null)
        {
            boxCollider = GetComponent<BoxCollider2D>();
        }

        if (boxCollider == null)
        {
            return;
        }

        Vector2 origin = GetGroundCheckOrigin();

        if (Application.isPlaying)
        {
            // 犬に乗っているときは青、地面に接地中は緑、空中は赤
            if (IsRidingDog)
            {
                Gizmos.color = Color.blue;
            }
            else if (IsGrounded)
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
}