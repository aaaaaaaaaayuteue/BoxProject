using UnityEngine;

// 目(魂)のコントローラ
// シーン直下の独立 GameObject にアタッチする
// 通常モード: 現在の本体に追従(本体側で計算済みの目位置にスナップ)
// 憑依モード: PossessionController から MoveByInput(dir) で命令されて自由移動
//             本体からの距離は最大値で Hard clamp される
// LineRenderer で本体↔目の点線を描画(限界距離に近づくほど赤くなる)
//
// [ExecuteAlways] により Edit モードでも Awake/LateUpdate/OnValidate が走る
// → エディタで目を見ながらサイズ・色・配置を調整できる
[ExecuteAlways]
[RequireComponent(typeof(LineRenderer))]
public class EyesController : MonoBehaviour
{
    // ーーー目の見た目ーーー
    [Header("左目のTransform(子オブジェクト)")]
    [SerializeField] private Transform eyeLeft;

    [Header("右目のTransform(子オブジェクト)")]
    [SerializeField] private Transform eyeRight;

    [Header("両目の左右間隔(本体中心からの距離)")]
    [SerializeField] private float eyeSpacing = 0.12f;

    [Header("目スプライトの描画順序(Playerより大きい値で目が前面に出る)")]
    [SerializeField] private int eyeSortingOrder = 10;

    [Header("目のスプライトが未設定なら自動生成する(楕円形・指定色)")]
    [SerializeField] private bool autoGenerateEyeSprite = true;

    [Header("自動生成する目スプライトの解像度(ピクセル)")]
    [SerializeField] private Vector2Int eyeSpriteResolution = new Vector2Int(32, 48);

    [Header("自動生成する目スプライトの色")]
    [SerializeField] private Color eyeColor = Color.black;

    [Header("自動生成時の目の表示サイズ(ワールドユニット)")]
    [SerializeField] private Vector2 eyeWorldSize = new Vector2(0.08f, 0.12f);

    // ーーー憑依モードの自由移動ーーー
    [Header("ーーーーーーー ここから下は憑依モードの自由移動 ーーーーーーー")]
    [Header("魂の移動速度(ユニット/秒、Time.unscaledDeltaTimeで動かす)")]
    [SerializeField] private float soulMoveSpeed = 6f;

    [Header("本体からの最大移動距離(ユニット、これ以上は離れられない)")]
    [SerializeField] private float maxDistanceFromBody = 4f;

    // ーーー憑依先候補の検出ーーー
    [Header("ーーーーーーー ここから下は憑依先候補の検出 ーーーーーーー")]
    [Header("魂が憑依先候補と判定する円の半径(ユニット)")]
    [SerializeField] private float possessionDetectionRadius = 0.4f;

    [Header("白箱として扱うレイヤー(このレイヤーの中から憑依先候補を探す)")]
    [SerializeField] private LayerMask boxLayer;

    // ーーー点線(LineRenderer)関連ーーー
    [Header("ーーーーーーー ここから下は点線(LineRenderer)関連 ーーーーーーー")]
    [Header("通常時の点線の色(限界に達してない間)")]
    [SerializeField] private Color lineColorNormal = Color.black;

    [Header("限界距離に達した時の線の色")]
    [SerializeField] private Color lineColorAtMax = Color.red;

    [Header("点線の太さ")]
    [SerializeField] private float lineWidth = 0.04f;

    // ※ 点線の細かさは CreateDashTexture() のテクスチャパターンで決まる
    //   Inspector からの動的調整は廃止(LineRenderer.Tile モードと mainTextureScale の組み合わせが
    //   Unity の挙動上うまく噛み合わなかったため、固定密度に変更)

    [Header("赤くなる距離比率の閾値(0.0〜1.0、これ以上で赤線、未満で黒点線)")]
    [Range(0.5f, 1f)]
    [SerializeField] private float redThresholdRatio = 0.98f;

    [Header("通常モード中も点線を描画するか(falseなら憑依モード中だけ表示)")]
    [SerializeField] private bool drawLineInNormalMode = false;

    // ーーー内部状態ーーー
    public enum EyesMode
    {
        Normal,        // 本体に追従
        Possessing     // 自由移動(憑依モード中)
    }

    private EyesMode currentMode = EyesMode.Normal;
    private BoxController currentBody;

    // ーーー内部参照ーーー
    private LineRenderer lineRenderer;

    // ーーー動的生成リソース(OnDestroyで破棄するため保持)ーーー
    private Texture2D generatedDashTexture;
    private Material generatedDashMaterial;
    private Sprite generatedEyeSprite;
    private Texture2D generatedEyeTexture;


    // ーーー外部公開プロパティーーー
    public EyesMode CurrentMode => currentMode;
    public BoxController CurrentBody => currentBody;
    public float DistanceFromBodyRatio => CalculateDistanceRatio();


    // ーーーUnityイベントーーー

    private void Awake()
    {
        lineRenderer = GetComponent<LineRenderer>();
        SetupLineRenderer();

        if (autoGenerateEyeSprite)
        {
            EnsureEyeSpriteGenerated();
            AssignEyeSpriteIfMissing(eyeLeft);
            AssignEyeSpriteIfMissing(eyeRight);

            // 初期セットアップ時のみスケールを適用
            // localScale が初期値 (1,1,1) のままなら eyeWorldSize から計算
            // ユーザーが Scene View でドラッグして調整済みなら触らない
            ApplyAutoGenScaleIfDefault(eyeLeft);
            ApplyAutoGenScaleIfDefault(eyeRight);
        }

        // 初期セットアップ時のみ間隔を適用
        // localPosition が (0,0,0) なら eyeSpacing で配置、それ以外はユーザー調整済みとして触らない
        ApplyEyeSpacingIfDefault();

        ApplyEyeSortingOrder();
    }

    private void OnDestroy()
    {
        // 動的生成したリソースを破棄(シーン再ロード時のメモリリーク防止)
        // ExecuteAlways のため Edit モードでも呼ばれる場合があるので、Edit モードでは DestroyImmediate を使う必要がある
        DestroySafe(generatedDashTexture);
        DestroySafe(generatedDashMaterial);
        DestroySafe(generatedEyeSprite);
        DestroySafe(generatedEyeTexture);
    }

    private void DestroySafe(Object obj)
    {
        if (obj == null)
        {
            return;
        }
        if (Application.isPlaying)
        {
            Destroy(obj);
        }
        else
        {
            DestroyImmediate(obj);
        }
    }


    // OnValidate は Inspector で SerializeField の値が変わった時に呼ばれる
    // ※ ここで eyeSpacing / eyeWorldSize から自動的に Transform を更新するのは廃止
    //   理由: ユーザーが Scene View で手動調整した値が、Play 開始時に scene 再シリアライズ
    //   をトリガーに OnValidate が走って上書きされる問題があったため
    //   サイズ・間隔は Scene View でドラッグするか、EyeLeft/EyeRight の Transform を直接編集してください
    //   Inspector の eyeWorldSize / eyeSpacing は「初期セットアップ時のデフォルト値」としてだけ機能します

    private void OnValidate()
    {
        if (this == null)
        {
            return;
        }

        ApplyEyeSortingOrder();
    }

    private void LateUpdate()
    {
        // 通常モード: 本体に追従(本体の Update で更新された目位置を使うので LateUpdate)
        if (currentMode == EyesMode.Normal)
        {
            FollowCurrentBody();
        }

        // 憑依モード中の自由移動は MoveByInput が外部から呼ばれて transform を直接動かす
        // ここでは「本体との距離をクランプする」処理だけ行う
        if (currentMode == EyesMode.Possessing)
        {
            ClampDistanceFromBody();
        }

        UpdateLineRenderer();
    }


    // ーーー外部API(PossessionControllerから呼ばれる)ーーー

    // 現在の本体を切り替える(リスポーン時、乗り移り時に呼ばれる)
    // 切替直後に位置を本体に合わせる(瞬間移動)

    public void SetCurrentBody(BoxController body)
    {
        currentBody = body;

        if (currentBody != null)
        {
            transform.position = new Vector3(
                currentBody.EyeWorldPosition.x,
                currentBody.EyeWorldPosition.y,
                transform.position.z
            );
        }
    }


    // モードを切り替える(Normal/Possessing)
    // Normal に戻すときは本体位置に瞬間スナップする

    public void SetMode(EyesMode mode)
    {
        currentMode = mode;

        if (mode == EyesMode.Normal)
        {
            FollowCurrentBody();
        }
    }


    // 憑依モード中の魂の自由移動入力を受け取る(input は -1〜+1 の Vector2)
    // Time.unscaledDeltaTime で動かすので、スローモーション中もリアルタイム操作感を維持

    public void MoveByInput(Vector2 input)
    {
        if (currentMode != EyesMode.Possessing)
        {
            return;
        }

        Vector3 delta = new Vector3(input.x, input.y, 0f) * soulMoveSpeed * Time.unscaledDeltaTime;
        transform.position += delta;
    }


    // 距離クランプを外部からも呼べるように公開
    // 通常は LateUpdate で自動的に呼ばれるが、PossessionController が
    // 「魂を動かす → 即クランプ → 憑依先判定」を1フレーム内で順序保証したい時に使う
    // (LateUpdate 待ちだと境界外で Z 判定できてしまう問題を防ぐ)

    public void EnforceDistanceClamp()
    {
        ClampDistanceFromBody();
    }


    // 魂の現在位置の周辺に憑依先候補の白箱があれば返す(なければ null)
    // 自分自身(現在の本体)は除外する

    public BoxController FindPossessionTarget()
    {
        Collider2D[] hits = Physics2D.OverlapCircleAll(transform.position, possessionDetectionRadius, boxLayer);

        BoxController nearest = null;
        float nearestSqrDistance = float.MaxValue;

        for (int i = 0; i < hits.Length; i++)
        {
            BoxController box = hits[i].GetComponent<BoxController>();
            if (box == null)
            {
                continue;
            }

            // 自分自身(現在の本体)は除外
            if (box == currentBody)
            {
                continue;
            }

            // 死亡中の箱は除外(乗り移っても意味ない)
            if (box.IsDead)
            {
                continue;
            }

            float sqrDist = ((Vector2)hits[i].transform.position - (Vector2)transform.position).sqrMagnitude;
            if (sqrDist < nearestSqrDistance)
            {
                nearestSqrDistance = sqrDist;
                nearest = box;
            }
        }

        return nearest;
    }


    // ーーー本体追従(通常モード)ーーー

    private void FollowCurrentBody()
    {
        if (currentBody == null)
        {
            return;
        }

        Vector2 target = currentBody.EyeWorldPosition;
        transform.position = new Vector3(target.x, target.y, transform.position.z);
    }


    // ーーー距離クランプ(憑依モード)ーーー
    // 本体からのベクトル長が maxDistanceFromBody を超えたら、最大距離上に丸める

    private void ClampDistanceFromBody()
    {
        if (currentBody == null)
        {
            return;
        }

        Vector2 bodyPos = currentBody.transform.position;
        Vector2 soulPos = transform.position;
        Vector2 offset = soulPos - bodyPos;
        float distance = offset.magnitude;

        if (distance > maxDistanceFromBody)
        {
            Vector2 clamped = bodyPos + offset.normalized * maxDistanceFromBody;
            transform.position = new Vector3(clamped.x, clamped.y, transform.position.z);
        }
    }


    // ーーー本体からの距離比率(0.0〜1.0)を計算ーーー

    private float CalculateDistanceRatio()
    {
        if (currentBody == null || maxDistanceFromBody <= 0f)
        {
            return 0f;
        }

        float distance = Vector2.Distance(transform.position, currentBody.transform.position);
        return Mathf.Clamp01(distance / maxDistanceFromBody);
    }


    // ーーーLineRendererのセットアップーーー
    // 動的に点線テクスチャを生成して、sharedMaterial に割り当てる
    // textureMode = Tile で、Unity が線の長さに応じて自動でテクスチャを繰り返す
    //
    // 重要: lineRenderer.material はアクセス時にインスタンスコピーを作るため、
    //   後で generatedDashMaterial を変更しても描画される material には反映されない
    //   sharedMaterial を使うことで参照を共有して、変更が確実に反映されるようにする

    private void SetupLineRenderer()
    {
        lineRenderer.positionCount = 2;
        lineRenderer.startWidth = lineWidth;
        lineRenderer.endWidth = lineWidth;
        lineRenderer.useWorldSpace = true;
        lineRenderer.numCapVertices = 0;
        lineRenderer.textureMode = LineTextureMode.Tile;

        // 既に生成済みのリソースがあれば破棄(SetupLineRenderer が複数回呼ばれた時の保険)
        DestroySafe(generatedDashTexture);
        DestroySafe(generatedDashMaterial);

        // 点線テクスチャを動的生成
        generatedDashTexture = CreateDashTexture();

        // URP / Built-in 両対応のシェーダーフォールバック
        Shader shader = FindLineShader();
        generatedDashMaterial = new Material(shader);
        generatedDashMaterial.mainTexture = generatedDashTexture;

        // Sprites/Default 系シェーダーは _MainTex 以外に _Color プロパティを持つので白に固定して
        // 色管理は LineRenderer.colorGradient 側で行う
        if (generatedDashMaterial.HasProperty("_Color"))
        {
            generatedDashMaterial.color = Color.white;
        }

        // sharedMaterial で参照共有(material は instance コピーされるため避ける)
        lineRenderer.sharedMaterial = generatedDashMaterial;
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

        // どれも見つからなければ Standard 系で諦め
        return Shader.Find("Standard");
    }


    // ーーー点線テクスチャを動的生成ーーー
    // 8x4 のテクスチャに「白1px → 透明1px」の4サイクルを横並びに格納
    // Tile モードでこのテクスチャがライン幅単位で繰り返されることで点線になる
    // パターンを密にすればするほど、点線間隔が短くなる
    // LineRenderer の colorGradient で実際の色がかかる(白テクスチャは色のフィルタ役)

    private Texture2D CreateDashTexture()
    {
        int w = 8;
        int h = 4;
        Texture2D tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
        tex.filterMode = FilterMode.Point;
        tex.wrapMode = TextureWrapMode.Repeat;

        Color opaque = Color.white;
        Color transparent = new Color(0f, 0f, 0f, 0f);

        // 横方向に「1px白 + 1px透明」のサイクルを4回繰り返すパターン
        // Tile モードと組み合わせると 1ライン幅あたり 4個の点が表示される
        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                // x が偶数なら白、奇数なら透明
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


    // ーーーLineRenderer の更新(毎フレーム)ーーー
    // 始点 = 現在の本体、終点 = 魂(=この目の位置)
    // 距離が redThresholdRatio 以上なら赤線、それ未満なら黒点線

    // 色変更時のみ Gradient を作り直すためのキャッシュ
    private Color lastAppliedLineColor = new Color(-1f, -1f, -1f, -1f);
    private Gradient cachedGradient;

    private void UpdateLineRenderer()
    {
        if (lineRenderer == null)
        {
            return;
        }

        // 通常モードで描画したくないなら線を消す
        bool shouldDraw = (currentMode == EyesMode.Possessing) || drawLineInNormalMode;
        lineRenderer.enabled = shouldDraw;

        if (!shouldDraw)
        {
            return;
        }

        if (currentBody == null)
        {
            lineRenderer.enabled = false;
            return;
        }

        Vector3 bodyPos = currentBody.transform.position;
        Vector3 soulPos = transform.position;

        lineRenderer.SetPosition(0, bodyPos);
        lineRenderer.SetPosition(1, soulPos);

        // 線の太さも毎フレーム反映(Inspector で調整したらすぐ反映されるように)
        lineRenderer.startWidth = lineWidth;
        lineRenderer.endWidth = lineWidth;

        // 距離比率で色を決定(閾値以上で赤、未満で黒)
        float ratio = CalculateDistanceRatio();
        Color color;
        if (ratio >= redThresholdRatio)
        {
            color = lineColorAtMax;
        }
        else
        {
            color = lineColorNormal;
        }

        // 色が変わった時だけ Gradient を作り直す
        // (LineRenderer の startColor/endColor は colorGradient の単純版で、
        //  デフォルトの白→赤グラデーションが残ることがあるため、明示的に colorGradient を上書きする)
        if (color != lastAppliedLineColor)
        {
            if (cachedGradient == null)
            {
                cachedGradient = new Gradient();
            }
            cachedGradient.SetKeys(
                new GradientColorKey[]
                {
                    new GradientColorKey(color, 0f),
                    new GradientColorKey(color, 1f)
                },
                new GradientAlphaKey[]
                {
                    new GradientAlphaKey(color.a, 0f),
                    new GradientAlphaKey(color.a, 1f)
                }
            );
            lineRenderer.colorGradient = cachedGradient;
            lastAppliedLineColor = color;
        }

        // ※ mainTextureScale の動的調整は廃止
        //   Tile モードは Unity がライン幅とテクスチャから自動でタイリングするため、
        //   mainTextureScale を手動で設定すると干渉して期待通りに動かない
        //   点線の細かさを変えたい場合は CreateDashTexture() のパターンを編集してください
    }


    // ーーー両目の左右間隔を適用(localPositionが初期値の場合のみ)ーーー
    // ユーザーが手動で Scene View で動かした位置を上書きしないため、
    // localPosition が (0,0,0) のときだけ eyeSpacing から配置する

    private void ApplyEyeSpacingIfDefault()
    {
        if (eyeLeft != null && eyeLeft.localPosition == Vector3.zero)
        {
            eyeLeft.localPosition = new Vector3(-eyeSpacing, 0f, 0f);
        }
        if (eyeRight != null && eyeRight.localPosition == Vector3.zero)
        {
            eyeRight.localPosition = new Vector3(eyeSpacing, 0f, 0f);
        }
    }


    // ーーー目スプライトの描画順序を適用ーーー
    // Player スプライトより大きい値にすることで、目が前面に表示される

    private void ApplyEyeSortingOrder()
    {
        ApplySortingOrderToEye(eyeLeft);
        ApplySortingOrderToEye(eyeRight);
    }

    private void ApplySortingOrderToEye(Transform eye)
    {
        if (eye == null)
        {
            return;
        }
        SpriteRenderer sr = eye.GetComponent<SpriteRenderer>();
        if (sr != null)
        {
            sr.sortingOrder = eyeSortingOrder;
        }
    }


    // ーーー目スプライトのスケールを eyeWorldSize に合わせる(localScaleが初期値の場合のみ)ーーー
    // ユーザーが Scene View で手動調整したスケールを尊重するため、
    // localScale が (1,1,1) のままのときだけ eyeWorldSize ベースの値を適用する
    // 初期セットアップ時のヘルパー、Awake からのみ呼ばれる

    private void ApplyAutoGenScaleIfDefault(Transform eye)
    {
        if (eye == null)
        {
            return;
        }
        if (eye.localScale != Vector3.one)
        {
            return;
        }

        const float pixelsPerUnit = 100f;
        float referenceWidth = eyeSpriteResolution.x / pixelsPerUnit;
        float referenceHeight = eyeSpriteResolution.y / pixelsPerUnit;

        float scaleX = (referenceWidth > 0f) ? eyeWorldSize.x / referenceWidth : 1f;
        float scaleY = (referenceHeight > 0f) ? eyeWorldSize.y / referenceHeight : 1f;
        eye.localScale = new Vector3(scaleX, scaleY, 1f);
    }


    // ーーー目のスプライト生成(一度だけ)ーーー
    // 楕円形の単色スプライトを Texture2D で作って generatedEyeSprite に保持
    // 両目で1つのスプライトを共有してメモリ節約

    private void EnsureEyeSpriteGenerated()
    {
        if (generatedEyeSprite == null)
        {
            generatedEyeSprite = CreateEllipseSprite();
        }
    }


    // ーーー指定の eye Transform にスプライトを割り当てる(まだ無い時だけ)ーーー
    // ※ Transform の localScale は触らない(ユーザーが Scene View で調整した値を保持するため)

    private void AssignEyeSpriteIfMissing(Transform eye)
    {
        if (eye == null)
        {
            return;
        }

        SpriteRenderer sr = eye.GetComponent<SpriteRenderer>();
        if (sr == null)
        {
            return;
        }

        // 既にスプライトが設定されているなら何もしない(ユーザーが手動で設定した場合)
        if (sr.sprite != null)
        {
            return;
        }

        sr.sprite = generatedEyeSprite;
    }


    // ーーー楕円形のスプライトを動的生成ーーー
    // 中心からの距離が楕円方程式で1.0以下のピクセルだけ eyeColor で塗る
    // それ以外は完全透明

    private Sprite CreateEllipseSprite()
    {
        int w = eyeSpriteResolution.x;
        int h = eyeSpriteResolution.y;

        generatedEyeTexture = new Texture2D(w, h, TextureFormat.RGBA32, false);
        generatedEyeTexture.filterMode = FilterMode.Bilinear;

        Color clear = new Color(0f, 0f, 0f, 0f);
        float halfW = w * 0.5f;
        float halfH = h * 0.5f;

        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                float nx = (x - halfW) / halfW;
                float ny = (y - halfH) / halfH;

                if (nx * nx + ny * ny <= 1f)
                {
                    generatedEyeTexture.SetPixel(x, y, eyeColor);
                }
                else
                {
                    generatedEyeTexture.SetPixel(x, y, clear);
                }
            }
        }

        generatedEyeTexture.Apply();

        Sprite sprite = Sprite.Create(
            generatedEyeTexture,
            new Rect(0f, 0f, w, h),
            new Vector2(0.5f, 0.5f),
            100f
        );

        return sprite;
    }


    // ーーーGizmos(検出範囲と最大距離の可視化)ーーー

    private void OnDrawGizmos()
    {
        // 憑依先検出範囲(マゼンタ)
        Gizmos.color = Color.magenta;
        Gizmos.DrawWireSphere(transform.position, possessionDetectionRadius);

        // 本体からの最大距離(シアンの円、本体中心)
        if (currentBody != null)
        {
            Gizmos.color = Color.cyan;
            Gizmos.DrawWireSphere(currentBody.transform.position, maxDistanceFromBody);
        }
    }
}
