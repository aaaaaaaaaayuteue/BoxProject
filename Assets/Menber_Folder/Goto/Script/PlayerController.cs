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

    // ーーー向きーーー
    [Header("ゲーム開始時のプレイヤーの向き(+1=右、-1=左)")]
    [SerializeField] private int initialFacingDirection = 1;

    [Header("元のスプライト画像が右向きならtrue、左向きならfalse")]
    [SerializeField] private bool spriteOriginallyFacesRight = true;

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

    // ーーー外部公開プロパティーーー
    public bool IsGrounded => CheckGrounded();
    public bool IsRidingDog => CheckRidingDog();
    public float CalculatedJumpVelocity => calculatedJumpVelocity;
    public int FacingDirection => facingDirection;
    public bool JustJumped => justJumped;


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
        // 入力読み取りはUpdateで(フレームごとに最新の入力を取りたいため)
        ReadInputs();

        // 向き判定もUpdateで(入力に応じて毎フレーム更新するため)
        UpdateFacingDirection();

        // 向きの状態に合わせてスプライトを反転
        ApplySpriteFlip();
    }

    private void FixedUpdate()
    {
        // ジャンプ初速度は重力に依存するので毎FixedUpdateで再計算する
        // 実行中にGravity Scaleが変更されても即座に反映できるようにするため
        CalculateJumpVelocity();

        // 物理演算系の処理は固定間隔のFixedUpdateで(フレームレートに依存させないため)
        HorizontalMove();
        HandleJump();
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

    private void HorizontalMove()
    {
        // 現在の速度を取得し、X成分だけ入力値×移動速度で上書き
        // Y成分(重力やジャンプによる縦速度)は維持する
        Vector2 v = rb.linearVelocity;
        v.x = horizontalInput * moveSpeed;
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
    // 足元の判定ボックス内に「犬」レイヤーのコライダーがあるか確認する
    // 接地判定とは別に犬限定で判定することで、ジャンプ感知のON/OFFなどに使える

    private bool CheckRidingDog()
    {
        Vector2 origin = GetGroundCheckOrigin();

        // dogLayerだけを対象に判定
        Collider2D hit = Physics2D.OverlapBox(origin, groundCheckSize, 0f, dogLayer);

        return hit != null;
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