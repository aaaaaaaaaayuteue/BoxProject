using UnityEngine;

// 移動式リフト (Moving Platform)
// 制御するボタン (PressureButton) が押されている間だけ、moveDirection 方向へ進む
// 終点条件: レール最大距離まで進んだら停止
// 上に乗っている乗客 (Player や白箱) は、リフト移動に合わせて手動でキャリーする
// (Unity の物理エンジンは Kinematic 上の物体を自動運搬しないため)
// Script Execution Order を BoxController より早くして、同 tick 内で
// 「Lift が動く → AddExternalDelta が積まれる → BoxController が消費する」の順を保証する
// (BoxController は DefaultExecutionOrder 指定なしなのでデフォルト=0、
//  Lift を -100 に下げることで物理 tick 内で先に走る)
[DefaultExecutionOrder(-100)]
[RequireComponent(typeof(Rigidbody2D))]
[RequireComponent(typeof(BoxCollider2D))]
public class LiftPlatform : MonoBehaviour
{
    [Header("ーーーーーーー 制御ボタン ーーーーーーー")]
    [Header("このボタンが押されている間だけ動く(必須、null だと永久停止)")]
    [SerializeField] private PressureButton button;

    [Header("ーーーーーーー 移動 ーーーーーーー")]
    [Header("移動方向(正規化される、+X で右、-X で左)")]
    [SerializeField] private Vector2 moveDirection = Vector2.right;

    [Header("移動速度(ユニット/秒)")]
    [SerializeField] private float moveSpeed = 2f;

    [Header("レールの最大移動距離(始点からの最大距離、ユニット)")]
    [SerializeField] private float railMaxDistance = 6f;

    [Header("ーーーーーーー 乗客運搬 ーーーーーーー")]
    [Header("リフトに乗っている対象として認識するレイヤー(Player など)")]
    [SerializeField] private LayerMask passengerLayer;

    [Header("乗客検知ボックスのリフト上端からのY方向オフセット(上方向)")]
    [SerializeField] private float passengerCheckOffsetY = 0.05f;

    [Header("乗客検知ボックスの厚み(Y方向、薄いほど厳密に上に乗ってる時だけ検知)")]
    [SerializeField] private float passengerCheckThickness = 0.1f;

    [Header("乗客検知ボックスの幅オフセット(リフト幅 + この値*2、+で外側に広く、-で内側に狭く)")]
    [SerializeField] private float passengerCheckWidthOffset = 0f;

    [Header("ーーーーーーー レール可視化(点線) ーーーーーーー")]
    [Header("Gameビューでレールの点線を表示するか")]
    [SerializeField] private bool showRailLine = true;

    [Header("点線の色(Alpha で透明度を調整できる)")]
    [SerializeField] private Color railLineColor = new Color(1f, 1f, 1f, 0.6f);

    [Header("点線の太さ")]
    [SerializeField] private float railLineWidth = 0.05f;

    [Header("点線のY方向オフセット(リフト中心からのズレ、0で中央)")]
    [SerializeField] private float railLineOffsetY = 0f;

    [Header("点線の描画順序(大きい値ほど手前。リフト本体は1、BackGroundは0)")]
    [SerializeField] private int railLineSortingOrder = 2;


    // ーーー内部参照ーーー
    private Rigidbody2D rb;
    private BoxCollider2D coll;
    private Vector2 startPosition;
    private GameObject railLineGameObject;
    private LineRenderer railLineRenderer;
    private Texture2D generatedDashTexture;
    private Material generatedDashMaterial;


    // ーーー外部公開プロパティーーー
    public bool IsMoving { get; private set; }
    public float DistanceFromStart => Vector2.Distance(rb.position, startPosition);
    public bool ShouldMove => button != null && button.IsPressed;


    // ーーーUnityイベントーーー

    private void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
        coll = GetComponent<BoxCollider2D>();

        // Kinematic にして物理シミュレーション対象から外す(自前で位置を制御)
        rb.bodyType = RigidbodyType2D.Kinematic;
        rb.freezeRotation = true;

        startPosition = rb.position;

        // レール点線のセットアップ(Gameビュー用、子 GameObject に LineRenderer を持たせる)
        SetupRailLineRenderer();
    }

    private void OnDestroy()
    {
        // 動的生成したリソースを破棄(シーン再ロード時のメモリリーク防止)
        if (generatedDashTexture != null)
        {
            Destroy(generatedDashTexture);
        }
        if (generatedDashMaterial != null)
        {
            Destroy(generatedDashMaterial);
        }
        if (railLineGameObject != null)
        {
            Destroy(railLineGameObject);
        }
    }

    private void LateUpdate()
    {
        // Awakeで設定が反映されないUnityのタイミング問題を回避するため、
        // 毎フレームLineRendererの主要パラメータを再適用する(EyesControllerと同じパターン)
        UpdateRailLine();
    }

    private void FixedUpdate()
    {
        IsMoving = false;

        // ボタンが押されてなければ完全停止
        if (!ShouldMove)
        {
            return;
        }

        Vector2 dir = moveDirection.sqrMagnitude > 0.001f ? moveDirection.normalized : Vector2.right;

        // レール残距離をチェック
        float distanceFromStart = Vector2.Distance(rb.position, startPosition);
        float remainingRail = Mathf.Max(0f, railMaxDistance - distanceFromStart);
        if (remainingRail <= 0f)
        {
            return;
        }

        // 今フレームに進める距離(残距離を上限とする)
        float maxStep = moveSpeed * Time.fixedDeltaTime;
        float actualStep = Mathf.Min(maxStep, remainingRail);

        Vector2 delta = dir * actualStep;

        // 乗客を先に動かす(リフトの位置確定前に動かすことで上に乗っている
        // Player/白箱がリフトと一緒に動いて見える)
        CarryPassengers(delta);

        // リフト本体を動かす
        rb.MovePosition(rb.position + delta);
        IsMoving = true;
    }


    // ーーー乗客検知 + 同 delta で運搬ーーー
    // リフトの上端付近に薄い OverlapBox を置き、その範囲内の Rigidbody2D を持つ
    // 対象を passengerLayer フィルタで拾い、AddExternalDelta or MovePosition で動かす

    private void CarryPassengers(Vector2 delta)
    {
        Vector2 boxCenter = (Vector2)transform.position + coll.offset * (Vector2)transform.lossyScale;
        Vector2 boxSize = new Vector2(
            Mathf.Abs(coll.size.x * transform.lossyScale.x),
            Mathf.Abs(coll.size.y * transform.lossyScale.y)
        );
        // 検知ボックスはリフトの上端の少し上に置く
        Vector2 passengerCenter = boxCenter + Vector2.up * (boxSize.y * 0.5f + passengerCheckOffsetY);

        // 検知ボックスの幅 = リフト幅 + オフセット (SSoT: リフトのコライダー幅から逆算)
        Vector2 passengerCheckSize = new Vector2(
            Mathf.Max(0f, boxSize.x + passengerCheckWidthOffset * 2f),
            passengerCheckThickness
        );

        Collider2D[] passengers = Physics2D.OverlapBoxAll(passengerCenter, passengerCheckSize, 0f, passengerLayer);

        for (int i = 0; i < passengers.Length; i++)
        {
            Collider2D p = passengers[i];
            if (p == null)
            {
                continue;
            }
            // 自分のコライダーは除外
            if (p.gameObject == gameObject)
            {
                continue;
            }
            // Rigidbody2D が無いオブジェクトはスキップ(自然に動かないと意味ない)
            Rigidbody2D prb = p.attachedRigidbody;
            if (prb == null)
            {
                continue;
            }
            // 自分の Rigidbody2D も除外(子コライダーが拾った場合の保険)
            if (prb == rb)
            {
                continue;
            }

            // 乗客が BoxController なら、外部デルタとして加算してもらう(BoxController が
            // 自分の linearVelocity 制御と一緒に MovePosition で1度だけ処理する → カクつきなし)
            // BoxController でない場合は通常の MovePosition を使う(押せる物理オブジェクト等)
            BoxController box = p.GetComponent<BoxController>();
            if (box == null)
            {
                box = prb.GetComponent<BoxController>();
            }
            if (box != null)
            {
                box.AddExternalDelta(delta);
            }
            else
            {
                prb.MovePosition(prb.position + delta);
            }
        }
    }


    // ーーーレール点線のセットアップ(Gameビュー用、Awakeで一度だけ実行)ーーー
    // 始点 = リフトの初期位置、終点 = startPosition + moveDirection * railMaxDistance
    // useWorldSpace=true で固定位置に描画(リフトが動いてもレール線は動かない)
    // 動的生成した点線テクスチャを Tile モードで貼り付ける(EyesController と同じ手法)

    private void SetupRailLineRenderer()
    {
        // Lift の子オブジェクトとしてレール線専用 GameObject を作る
        // リフトの SpriteRenderer と競合しないようにコンポーネント分離する
        railLineGameObject = new GameObject("RailLine");
        railLineGameObject.transform.SetParent(transform, worldPositionStays: false);
        railLineGameObject.transform.localPosition = Vector3.zero;

        railLineRenderer = railLineGameObject.AddComponent<LineRenderer>();

        railLineRenderer.positionCount = 2;
        railLineRenderer.startWidth = railLineWidth;
        railLineRenderer.endWidth = railLineWidth;
        railLineRenderer.useWorldSpace = true;       // ← 親(Lift)の transform の影響を受けず、固定位置
        railLineRenderer.numCapVertices = 0;
        railLineRenderer.textureMode = LineTextureMode.Tile;
        railLineRenderer.sortingOrder = railLineSortingOrder;

        // 点線テクスチャを動的生成
        generatedDashTexture = CreateDashTexture();

        // URP / Built-in 両対応のシェーダーフォールバック
        Shader shader = FindLineShader();
        if (shader == null)
        {
            Debug.LogWarning("[LiftPlatform] レール点線用のシェーダーが見つかりません。点線が描画されない可能性があります。", this);
        }
        generatedDashMaterial = new Material(shader);
        generatedDashMaterial.mainTexture = generatedDashTexture;
        if (generatedDashMaterial.HasProperty("_Color"))
        {
            generatedDashMaterial.color = Color.white;
        }
        railLineRenderer.sharedMaterial = generatedDashMaterial;

        // 色グラデーション(全体一色)
        Gradient g = new Gradient();
        g.SetKeys(
            new GradientColorKey[]
            {
                new GradientColorKey(railLineColor, 0f),
                new GradientColorKey(railLineColor, 1f)
            },
            new GradientAlphaKey[]
            {
                new GradientAlphaKey(railLineColor.a, 0f),
                new GradientAlphaKey(railLineColor.a, 1f)
            }
        );
        railLineRenderer.colorGradient = g;

        // 始点・終点を計算してセット
        Vector2 dir = moveDirection.sqrMagnitude > 0.001f ? moveDirection.normalized : Vector2.right;
        Vector3 startPos = (Vector3)startPosition + Vector3.up * railLineOffsetY;
        Vector3 endPos = startPos + (Vector3)(dir * railMaxDistance);
        railLineRenderer.SetPosition(0, startPos);
        railLineRenderer.SetPosition(1, endPos);

        railLineRenderer.enabled = showRailLine;

        // デバッグ: セットアップ完了時の状態を1度だけログ出力(問題切り分け用)
        Debug.Log($"[LiftPlatform] RailLine setup done. shader={(shader != null ? shader.name : "NULL")}, "
                  + $"start={startPos}, end={endPos}, enabled={railLineRenderer.enabled}, "
                  + $"posCount={railLineRenderer.positionCount}, sortingOrder={railLineRenderer.sortingOrder}, "
                  + $"width={railLineRenderer.startWidth}", this);
    }


    // ーーー毎フレームの再適用(LateUpdateから呼ばれる)ーーー
    // Awakeでの設定がUnityの内部タイミングで一部反映されないケースの保険として、
    // showRailLine / 色 / 太さ / 位置を毎フレーム反映する
    // (Inspector で値を変えたら即時反映されるメリットもある)

    private void UpdateRailLine()
    {
        if (railLineRenderer == null)
        {
            return;
        }

        railLineRenderer.enabled = showRailLine;
        if (!showRailLine)
        {
            return;
        }

        railLineRenderer.startWidth = railLineWidth;
        railLineRenderer.endWidth = railLineWidth;
        railLineRenderer.sortingOrder = railLineSortingOrder;

        // 色を毎フレーム適用(InspectorでrailLineColor変えたら反映)
        Gradient g = new Gradient();
        g.SetKeys(
            new GradientColorKey[]
            {
                new GradientColorKey(railLineColor, 0f),
                new GradientColorKey(railLineColor, 1f)
            },
            new GradientAlphaKey[]
            {
                new GradientAlphaKey(railLineColor.a, 0f),
                new GradientAlphaKey(railLineColor.a, 1f)
            }
        );
        railLineRenderer.colorGradient = g;

        // 始点・終点を再計算して反映
        Vector2 dir = moveDirection.sqrMagnitude > 0.001f ? moveDirection.normalized : Vector2.right;
        Vector3 startPos = (Vector3)startPosition + Vector3.up * railLineOffsetY;
        Vector3 endPos = startPos + (Vector3)(dir * railMaxDistance);
        railLineRenderer.SetPosition(0, startPos);
        railLineRenderer.SetPosition(1, endPos);
    }


    // ーーー点線テクスチャを動的生成ーーー
    // EyesController と同じパターン(8x4 で「白1px + 透明1px」を繰り返し)
    // Tile モードで Unity が線の長さに応じて自動でテクスチャを繰り返す

    private Texture2D CreateDashTexture()
    {
        int w = 8;
        int h = 4;
        Texture2D tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
        tex.filterMode = FilterMode.Point;
        tex.wrapMode = TextureWrapMode.Repeat;

        Color opaque = Color.white;
        Color transparent = new Color(0f, 0f, 0f, 0f);

        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                if ((x % 2) == 0)
                {
                    tex.SetPixel(x, y, opaque);
                }
                else
                {
                    tex.SetPixel(x, y, transparent);
                }
            }
        }

        tex.Apply();
        return tex;
    }


    // ーーーシェーダーのフォールバック検索ーーー
    // Built-in / URP / その他の環境で見つかるシェーダーを順番に試す

    private Shader FindLineShader()
    {
        string[] candidates = new string[]
        {
            "Sprites/Default",
            "Universal Render Pipeline/2D/Sprite-Unlit-Default",
            "Universal Render Pipeline/Unlit",
            "Unlit/Transparent",
            "Particles/Standard Unlit"
        };

        for (int i = 0; i < candidates.Length; i++)
        {
            Shader s = Shader.Find(candidates[i]);
            if (s != null)
            {
                return s;
            }
        }

        return Shader.Find("Standard");
    }


    // ーーーGizmos(レール終点と乗客検知ボックスを可視化)ーーー

    private void OnDrawGizmos()
    {
        if (coll == null)
        {
            coll = GetComponent<BoxCollider2D>();
        }
        if (coll == null)
        {
            return;
        }

        // レール終点 (緑の線+終点マーカー)
        Vector2 dir = moveDirection.sqrMagnitude > 0.001f ? moveDirection.normalized : Vector2.right;
        Vector3 startPos = Application.isPlaying ? (Vector3)startPosition : transform.position;
        Vector3 endPos = startPos + (Vector3)(dir * railMaxDistance);

        Gizmos.color = Color.green;
        Gizmos.DrawLine(startPos, endPos);
        Gizmos.DrawWireSphere(endPos, 0.1f);

        // 乗客検知ボックス (シアン) — 実行時と同じロジックで幅を逆算する
        Vector2 boxCenter = (Vector2)transform.position + coll.offset * (Vector2)transform.lossyScale;
        Vector2 boxSize = new Vector2(
            Mathf.Abs(coll.size.x * transform.lossyScale.x),
            Mathf.Abs(coll.size.y * transform.lossyScale.y)
        );
        Vector2 passengerCenter = boxCenter + Vector2.up * (boxSize.y * 0.5f + passengerCheckOffsetY);
        Vector2 passengerCheckSize = new Vector2(
            Mathf.Max(0f, boxSize.x + passengerCheckWidthOffset * 2f),
            passengerCheckThickness
        );

        Gizmos.color = Color.cyan;
        Gizmos.DrawWireCube(passengerCenter, passengerCheckSize);
    }
}
