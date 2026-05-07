using UnityEngine;

// 白箱個体のコントローラ
// 移動・ジャンプ・接地判定・死亡演出・押される物理を担当する
// 入力は読まない。PossessionController から Move(float) / Jump() で命令される
//
// 2つのフラグで状態を表す:
//   isPlayerBody  : この箱が「現在のプレイヤー本体」か
//                   true のとき OnTrigger 死亡判定が有効、目の追従先になる
//   isInputDriven : Move/Jump 入力が velocity に反映されるか
//                   通常モードは true、憑依モード中は false (本体は外力に任せる)
//
// 「現在のプレイヤー本体」と「入力で動かしている対象」は別概念
// 憑依モード中: isPlayerBody=true（死亡判定は生きてる）, isInputDriven=false（外力で動く）
[RequireComponent(typeof(Rigidbody2D))]
[RequireComponent(typeof(BoxCollider2D))]
[RequireComponent(typeof(SpriteRenderer))]
public class BoxController : MonoBehaviour
{
    // ーーー移動ーーー
    [Header("移動スピード(ユニット/秒)")]
    [SerializeField] private float moveSpeed = 5f;

    // ーーージャンプーーー
    [Header("ジャンプの最高到達点の高さ(マス数)")]
    [SerializeField] private float jumpMaxHeight = 1f;

    // ※ 重力はRigidbody 2DのGravity ScaleとUnityの重力設定（Physics2D.gravity）から決まる
    // ※ ジャンプ初速度はそれらの値から自動で逆算される

    // ーーー接地判定ーーー
    [Header("地面として扱うレイヤー(GroundのほかWhiteBoxレイヤーも入れると箱の上に乗れる)")]
    [SerializeField] private LayerMask groundLayer;

    [Header("接地判定ボックスのサイズ(X=幅、Y=厚み)")]
    [SerializeField] private Vector2 groundCheckSize = new Vector2(0.9f, 0.1f);

    [Header("接地判定ボックスのY方向オフセット(コライダー下端からの相対位置、負の値でさらに下)")]
    [SerializeField] private float groundCheckOffsetY = 0f;

    [Header("入力駆動でない時の地上摩擦減速(ユニット/秒²、押された箱を氷上のように滑らせない)")]
    [SerializeField] private float idleGroundDeceleration = 25f;

    // ーーー向きーーー
    [Header("ゲーム開始時のこの箱の向き(+1=右、-1=左)")]
    [SerializeField] private int initialFacingDirection = 1;

    [Header("元のスプライト画像が右向きならtrue、左向きならfalse")]
    [SerializeField] private bool spriteOriginallyFacesRight = true;

    // ーーー死亡・リスポーン関連ーーー
    [Header("ーーーーーーー ここから下は死亡・リスポーン関連 ーーーーーーー")]
    [Header("リスタート管理のGameManager")]
    [SerializeField] private GameManager gameManager;

    [Header("スパイクとして扱うレイヤー(Spikeのみ、触れたら死亡)")]
    [SerializeField] private LayerMask spikeLayer;

    [Header("死亡中の本体の色")]
    [SerializeField] private Color deathColor = Color.red;

    [Header("死亡してからリスタートまでの時間(秒)")]
    [SerializeField] private float deathFreezeDuration = 0.5f;

    // ーーー憑依関連ーーー
    [Header("ーーーーーーー ここから下は憑依関連 ーーーーーーー")]
    [Header("ゲーム開始時、この箱がプレイヤー本体か(PossessionController でも上書きされる)")]
    [SerializeField] private bool isPlayerBody = false;

    [Header("ゲーム開始時、入力でこの箱を動かすか(PossessionController でも上書きされる)")]
    [SerializeField] private bool isInputDriven = false;

    [Header("憑依モード中の本体スプライトのアルファ値(0=透明、1=不透明)")]
    [Range(0f, 1f)]
    [SerializeField] private float possessedAlpha = 0.4f;

    [Header("通常時の目のローカルオフセット(本体中心からの相対位置)")]
    [SerializeField] private Vector2 eyeNormalOffset = new Vector2(0f, 0.1f);

    [Header("移動中の目の進行方向への先行オフセット(X方向、向きで自動反転)")]
    [SerializeField] private float eyeLeadOffsetX = 0.1f;

    // ーーー内部参照ーーー
    private Rigidbody2D rb;
    private BoxCollider2D boxCollider;
    private SpriteRenderer spriteRenderer;

    // ーーー入力状態(外部から渡される)ーーー
    private float horizontalInput;  // 水平入力値(-1〜+1)、PossessionControllerが Move(x) で設定
    private bool jumpRequested;     // このフレームでジャンプ要求があったか、PossessionControllerが Jump() で設定

    // ーーージャンプ計算結果ーーー
    private float calculatedJumpVelocity;

    // ーーージャンプ検知用(他のスクリプトから検知される)ーーー
    private bool justJumped;

    // ーーー向き状態ーーー
    private int facingDirection;

    // ーーー死亡状態管理ーーー
    private bool isDead;
    private float deathFreezeTimer;
    private bool hasRequestedRestart;  // ExecuteRestart 多重呼び出し防止フラグ
    private Color originalColor;
    private RigidbodyType2D originalBodyType;

    // ーーー外部からのデルタ移動(Moving Platform から渡される)ーーー
    // LiftPlatform などが乗客を運ぶときに使う。FixedUpdate で1度だけ消費する
    private Vector2 pendingExternalDelta;

    // ーーー外部公開プロパティーーー
    public bool IsGrounded => CheckGrounded();
    public bool IsDead => isDead;
    public bool IsPlayerBody => isPlayerBody;
    public bool IsInputDriven => isInputDriven;
    public int FacingDirection => facingDirection;
    public bool JustJumped => justJumped;
    public float CalculatedJumpVelocity => calculatedJumpVelocity;
    public Vector2 EyeWorldPosition => CalculateEyeWorldPosition();


    // ーーーUnityイベントーーー

    private void Awake()
    {
        // コンポーネント参照を取得
        rb = GetComponent<Rigidbody2D>();
        boxCollider = GetComponent<BoxCollider2D>();
        spriteRenderer = GetComponent<SpriteRenderer>();

        // 衝突時に回転しないように固定(箱が転がらないようにする)
        rb.freezeRotation = true;

        // 初期の向きを設定
        facingDirection = initialFacingDirection;

        // 起動時にスプライトの反転状態も初期向きに合わせる
        ApplySpriteFlip();

        // 元の色とBodyTypeを記録(リスポーン時に戻すため)
        originalColor = spriteRenderer.color;
        originalBodyType = rb.bodyType;
    }

    private void Update()
    {
        // 死亡中はフリーズタイマーを更新するだけ、他は何もしない
        if (isDead)
        {
            UpdateDeathFreezeTimer();
            return;
        }

        // 入力に応じた向き更新(横入力がある時だけ更新、無入力時は最後の向きを保持)
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

        // 外部からのデルタ移動(Moving Platform 運搬)を最後に MovePosition で加算
        // BoxController 内で一括処理することで、linearVelocity 直書きとの競合によるカクつきを防ぐ
        ApplyPendingExternalDelta();
    }


    // ーーー外部デルタの適用ーーー
    // pendingExternalDelta が積まれていたら、velocity に反映してクリア
    //
    // ※ MovePosition ではなく velocity 経由で反映する理由:
    //   Dynamic Rigidbody2D に対して MovePosition を呼ぶと、その物理ステップで
    //   linearVelocity が上書きされる仕様があり、HorizontalMove が設定した
    //   プレイヤー入力速度が無効化されてしまう (= リフトに乗ると入力が効かなくなる)
    //
    // ※ 入力駆動(プレイヤー)と非入力駆動(空箱)で挙動を分ける理由:
    //   入力駆動: HorizontalMove で毎フレーム v.x = input * moveSpeed と上書きされるので、
    //             外部 delta は += で「入力 + リフト運搬」を合成する。
    //   非入力駆動: HorizontalMove で MoveTowards(v.x, 0, 0.5) の緩やかな減速しか入らないため、
    //             += 加算だと毎フレーム純増 1.5 で累積し、空箱が高速で吹き飛ぶ問題があった。
    //             X は = 代入で上書きして累積を断つ。Y は触らない(重力やジャンプの慣性を保持)。

    private void ApplyPendingExternalDelta()
    {
        if (pendingExternalDelta == Vector2.zero)
        {
            return;
        }
        // delta (1フレーム分の位置変化) を velocity (秒速) に変換
        Vector2 deltaVelocity = pendingExternalDelta / Time.fixedDeltaTime;

        if (isInputDriven)
        {
            // 入力駆動(プレイヤー操作中) → 加算で入力と運搬を合成
            rb.linearVelocity += deltaVelocity;
        }
        else
        {
            // 非入力駆動(空箱) → X のみ上書きして累積を断つ
            Vector2 v = rb.linearVelocity;
            v.x = deltaVelocity.x;
            rb.linearVelocity = v;
        }

        pendingExternalDelta = Vector2.zero;
    }


    // ーーー外部API(PossessionControllerから呼ばれる)ーーー

    // 「プレイヤー本体」フラグを切り替える
    // true 直後にスパイクと重なっているか即チェックして、重なっていたら即死亡
    // (OnTriggerEnter2D は Collider が新たに侵入したときしか発火しないので、すでに重なっている状況では死なない)
    // 乗り移り先がスパイク上にある場合の救済処理

    public void SetIsPlayerBody(bool value)
    {
        bool wasPlayer = isPlayerBody;
        isPlayerBody = value;

        // false → true に切り替わった瞬間にスパイク重なりチェック
        if (!wasPlayer && value && !isDead)
        {
            CheckSpikeOverlapAndDieIfNeeded();
        }
    }


    // 入力駆動フラグを切り替える
    // false にしたら入力状態もクリアして、惰性で動き続けないようにする
    // ただし velocity 自体は残る(押されたり、動く床に乗っている挙動を維持するため)

    public void SetInputDriven(bool value)
    {
        isInputDriven = value;

        if (!value)
        {
            ClearInputs();
        }
    }


    // 内部の入力状態をクリア(横入力とジャンプ要求をリセット)
    // 憑依モード突入直前など、本体に「同フレームで押された Z+Space」が引き継がれないようにする保険

    public void ClearInputs()
    {
        horizontalInput = 0f;
        jumpRequested = false;
    }


    // 外部からデルタ移動を加算する(Moving Platform などが呼ぶ)
    // 次の FixedUpdate でその delta だけ MovePosition で加算される
    // rb.position 直接書き込みは BoxController の linearVelocity 制御と競合してカクつくため、
    // BoxController 自身が物理ステップで一括処理することで滑らかに運搬される

    public void AddExternalDelta(Vector2 delta)
    {
        pendingExternalDelta += delta;
    }


    // 水平移動の入力を受け取る(-1〜+1)
    // 入力駆動でない、または死亡中なら無視

    public void Move(float horizontal)
    {
        if (!isInputDriven || isDead)
        {
            return;
        }

        horizontalInput = horizontal;
    }


    // ジャンプ要求を受け取る
    // 入力駆動でない、または死亡中なら無視
    // 実際の発動はFixedUpdateのHandleJumpで接地判定と合わせて判定

    public void Jump()
    {
        if (!isInputDriven || isDead)
        {
            return;
        }

        jumpRequested = true;
    }


    // 本体スプライトの半透明化(憑依モード中の表現)
    // 当たり判定は変更しないので物理的な挙動は維持される
    // ※ Alpha のみ操作する。RGB は触らない
    //   理由: 死亡演出(deathColor=red)中に EnterDyingState から SetTransparent(false) が呼ばれた時、
    //   RGB を originalColor に戻すと「赤くなった瞬間に白に戻る」バグが起きるため
    //   完全な色リセットは Respawn() / ResetDeathState() の方で明示的に実行される

    public void SetTransparent(bool transparent)
    {
        if (spriteRenderer == null)
        {
            return;
        }

        Color c = spriteRenderer.color;
        if (transparent)
        {
            c.a = possessedAlpha;
        }
        else
        {
            c.a = originalColor.a;
        }
        spriteRenderer.color = c;
    }


    // 死亡状態を解除(GameManager のリスタート時に、初期本体以外の死んだ箱もリセットするため)
    // 位置は変えず、色・BodyType・isDead だけ初期状態に戻す

    public void ResetDeathState()
    {
        if (!isDead)
        {
            return;
        }

        isDead = false;
        deathFreezeTimer = 0f;
        hasRequestedRestart = false;

        spriteRenderer.color = originalColor;
        rb.bodyType = originalBodyType;
        rb.linearVelocity = Vector2.zero;
        rb.angularVelocity = 0f;

        ClearInputs();
    }


    // ーーースパイク接触検知ーーー
    // 「現在の本体」のときだけスパイク死亡判定を行う
    // 非操作中の白箱はスパイクに触れても死なない(放置される)

    private void OnTriggerEnter2D(Collider2D other)
    {
        // 既に死亡中なら何もしない
        if (isDead)
        {
            return;
        }

        // 「プレイヤー本体」でなければ死亡判定しない
        if (!isPlayerBody)
        {
            return;
        }

        if (IsSpikeCollider(other))
        {
            EnterDeathState();
        }
    }


    // ーーースパイクとの重なりを即チェックーーー
    // SetIsPlayerBody(true) 直後に呼ばれる
    // 既にスパイクの上に置かれた箱に憑依した場合の救済処理

    private void CheckSpikeOverlapAndDieIfNeeded()
    {
        if (boxCollider == null || rb == null)
        {
            return;
        }

        Vector2 boxCenter = (Vector2)transform.position + boxCollider.offset * (Vector2)transform.lossyScale;
        Vector2 boxSize = new Vector2(
            Mathf.Abs(boxCollider.size.x * transform.lossyScale.x),
            Mathf.Abs(boxCollider.size.y * transform.lossyScale.y)
        );

        Collider2D hit = Physics2D.OverlapBox(boxCenter, boxSize, 0f, spikeLayer);
        if (hit != null)
        {
            EnterDeathState();
        }
    }


    // ーーーレイヤーがスパイクかどうか判定ーーー

    private bool IsSpikeCollider(Collider2D other)
    {
        int otherLayerBit = 1 << other.gameObject.layer;
        return (spikeLayer.value & otherLayerBit) != 0;
    }


    // ーーー死亡状態への移行ーーー
    // 色を変更、入力無効化、物理停止、フリーズタイマー開始

    private void EnterDeathState()
    {
        isDead = true;
        deathFreezeTimer = deathFreezeDuration;
        hasRequestedRestart = false;

        // 色を死亡色に変更
        spriteRenderer.color = deathColor;

        // 速度をゼロにして、Kinematicにして重力も止める
        rb.linearVelocity = Vector2.zero;
        rb.angularVelocity = 0f;
        rb.bodyType = RigidbodyType2D.Kinematic;

        // 入力状態もクリア
        ClearInputs();
    }


    // ーーー死亡フリーズタイマーの更新ーーー
    // タイマーが0になったらGameManagerにリスタート要求
    // 多重呼び出しを防ぐため hasRequestedRestart フラグでガード

    private void UpdateDeathFreezeTimer()
    {
        deathFreezeTimer -= Time.deltaTime;

        if (deathFreezeTimer <= 0f && !hasRequestedRestart)
        {
            hasRequestedRestart = true;

            if (gameManager != null)
            {
                gameManager.ExecuteRestart();
            }
            else
            {
                Debug.LogWarning("[BoxController] GameManager が未設定のため、リスタートできません。Inspector で gameManager を設定してください。", this);
            }
        }
    }


    // ーーーリスポーン処理(GameManagerから呼ばれる)ーーー
    // 指定された位置と向きで、箱を復帰させる
    // 色・BodyType・速度・入力状態をリセットする

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

        // 入力状態をクリア(死亡中に押された入力が引き継がれないように)
        ClearInputs();

        // 死亡状態を解除
        isDead = false;
        hasRequestedRestart = false;
    }


    // ーーー目のワールド位置を計算ーーー
    // 通常時は本体中心からeyeNormalOffsetだけずらした位置
    // 横方向に動いている場合は、進行方向にeyeLeadOffsetXだけさらに先行させる
    // EyesControllerが追従先として参照する

    private Vector2 CalculateEyeWorldPosition()
    {
        Vector2 boxCenter = (Vector2)transform.position;

        // 基本オフセット
        float eyeX = boxCenter.x + eyeNormalOffset.x;
        float eyeY = boxCenter.y + eyeNormalOffset.y;

        // 横方向に動いている場合は進行方向に先行
        // 入力が無い時でも、最後に向いていた方向を facingDirection が保持しているので
        // 「現在動いてるか」を horizontalInput の絶対値で判定する
        if (Mathf.Abs(horizontalInput) > 0f)
        {
            int moveDir;
            if (horizontalInput > 0f)
            {
                moveDir = 1;
            }
            else
            {
                moveDir = -1;
            }
            eyeX += eyeLeadOffsetX * moveDir;
        }

        return new Vector2(eyeX, eyeY);
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
    // 最高到達点の高さhと、現在の箱にかかる重力gから、必要な初速度v0を計算する
    // エネルギー保存則：v0 = √(2gh)

    private void CalculateJumpVelocity()
    {
        // この箱にかかる実際の重力加速度を計算する
        // Physics2D.gravityは負のY方向を持つベクトルなので絶対値を取る
        // それにRigidbody 2DのGravity Scaleを掛けたものが、この箱が受ける重力の大きさ
        float effectiveGravity = Mathf.Abs(Physics2D.gravity.y) * rb.gravityScale;

        // 重力が0以下のときは0除算やマイナスのルートを避けるためガード
        if (effectiveGravity <= 0f)
        {
            calculatedJumpVelocity = 0f;
            return;
        }

        calculatedJumpVelocity = Mathf.Sqrt(2f * effectiveGravity * jumpMaxHeight);
    }


    // ーーー水平移動ーーー
    // 入力駆動の時: horizontalInput * moveSpeed で X 速度を直接上書き(プレイヤー操作)
    // 入力駆動でない && 接地中: idleGroundDeceleration で X 速度を 0 に近づける(摩擦相当)
    //   これで「押された箱が氷上のように滑り続ける」のを防ぐ
    // 入力駆動でない && 空中: 何もしない(押されてジャンプ台から飛んだ等の慣性は維持)

    private void HorizontalMove()
    {
        Vector2 v = rb.linearVelocity;

        if (isInputDriven)
        {
            v.x = horizontalInput * moveSpeed;
        }
        else if (CheckGrounded())
        {
            v.x = Mathf.MoveTowards(v.x, 0f, idleGroundDeceleration * Time.fixedDeltaTime);
        }
        else
        {
            return;
        }

        rb.linearVelocity = v;
    }


    // ーーージャンプ処理ーーー
    // ジャンプ要求があり、かつ接地している時だけ発動

    private void HandleJump()
    {
        // 今フレームのジャンプ発動フラグはまずfalseにリセット
        justJumped = false;

        // 入力駆動でなければジャンプ処理は不要(要求も消す)
        if (!isInputDriven)
        {
            jumpRequested = false;
            return;
        }

        // ジャンプリクエストがあり、かつ接地しているときだけジャンプ発動
        if (jumpRequested && IsGrounded)
        {
            Vector2 v = rb.linearVelocity;
            // Y速度を計算した初速度で上書き(既存のY速度は破棄)
            v.y = calculatedJumpVelocity;
            rb.linearVelocity = v;

            // ジャンプ発動した瞬間のフラグを立てる
            justJumped = true;
        }

        // リクエストは1フレームで消費するため毎回falseに戻す
        jumpRequested = false;
    }


    // ーーー接地判定ーーー
    // 足元の判定ボックス内に「地面または白箱」レイヤーのコライダーがあれば接地
    // ただし自分自身の Collider は除外する(WhiteBox レイヤーを groundLayer に含めた場合の自己検出を防ぐ)

    private bool CheckGrounded()
    {
        Vector2 origin = GetGroundCheckOrigin();
        Collider2D[] hits = Physics2D.OverlapBoxAll(origin, groundCheckSize, 0f, groundLayer);

        for (int i = 0; i < hits.Length; i++)
        {
            // 自分の Collider はスキップ(WhiteBox レイヤーを groundLayer に含んでいる場合の保険)
            if (hits[i] == boxCollider)
            {
                continue;
            }

            return true;
        }

        return false;
    }


    // ーーー接地判定ボックスの中心位置を計算ーーー

    private Vector2 GetGroundCheckOrigin()
    {
        Vector2 colliderCenter = (Vector2)transform.position + boxCollider.offset * (Vector2)transform.lossyScale;
        float colliderBottomY = colliderCenter.y - (boxCollider.size.y * Mathf.Abs(transform.lossyScale.y) * 0.5f);
        return new Vector2(colliderCenter.x, colliderBottomY + groundCheckOffsetY);
    }


    // ーーーGizmos(接地判定ボックスと目位置の可視化)ーーー

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

        // 接地判定ボックス
        Vector2 origin = GetGroundCheckOrigin();

        if (Application.isPlaying)
        {
            // 接地中は緑、空中は赤
            if (IsGrounded)
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

        // 目の位置(マゼンタの小さい円)
        Gizmos.color = Color.magenta;
        Vector2 eyePos = CalculateEyeWorldPosition();
        Gizmos.DrawWireSphere(eyePos, 0.08f);

        // 「現在の本体」のときは箱の周りに白い枠を表示
        if (Application.isPlaying && isPlayerBody)
        {
            Gizmos.color = Color.white;
            Vector2 boxCenter = (Vector2)transform.position + boxCollider.offset * (Vector2)transform.lossyScale;
            Vector2 boxSize = new Vector2(
                boxCollider.size.x * Mathf.Abs(transform.lossyScale.x),
                boxCollider.size.y * Mathf.Abs(transform.lossyScale.y)
            );
            // 少し外側に枠を描画
            Gizmos.DrawWireCube(boxCenter, boxSize * 1.05f);
        }
    }
}
