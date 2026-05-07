using UnityEngine;

// 圧力ボタン (Pressure Button)
// プレイヤー(or 白箱)が触れている間 IsPressed = true となる
// LiftPlatform / RotatingSaw が このボタンの IsPressed を参照して動作する
//
// 構造:
//   Button(root): BoxCollider2D(Trigger) + SpriteRenderer + PressureButton スクリプト
//     → 押下時にローカルYを下げて沈み込み演出する(自身の transform を動かす)
//
// 旧構造との互換: visualTransform を Inspector で指定すればそちらの localPosition.y を動かす
// (空欄ならスクリプト自身の transform を動かす = 統合版)
[RequireComponent(typeof(Collider2D))]
public class PressureButton : MonoBehaviour
{
    [Header("ーーーーーーー 押下検知 ーーーーーーー")]
    [Header("ボタンを押せる対象として認識するレイヤー(Player など)")]
    [SerializeField] private LayerMask pressableLayer;

    [Header("デバッグログを出すか")]
    [SerializeField] private bool showDebugInfo = false;

    [Header("ーーーーーーー 沈み込み演出 ーーーーーーー")]
    [Header("沈み込みの見た目を反映する Transform(空欄なら自身の transform)")]
    [SerializeField] private Transform visualTransform;

    [Header("通常時の localPosition.y (押されてない状態)")]
    [SerializeField] private float restingLocalY = 0f;

    [Header("押下時の localPosition.y (沈み込んだ状態、resting より小さい値にする)")]
    [SerializeField] private float pressedLocalY = -0.05f;

    [Header("沈み込み・復帰のアニメ速度(秒、小さいほど即座に動く)")]
    [SerializeField] private float sinkSmoothTime = 0.05f;

    // ーーー内部状態ーーー
    // 同時に複数のオブジェクトが乗ることもあるのでカウンタで管理
    private int contactCount;

    // 沈み込みアニメ用の Lerp 速度 (SmoothDamp 用)
    private float currentLocalY;
    private float visualVelocity;


    // ーーー外部公開プロパティーーー
    public bool IsPressed => contactCount > 0;
    public int ContactCount => contactCount;


    // ーーーUnityイベントーーー

    private void Awake()
    {
        // 起動時に visualTransform の Y を resting に合わせる(Inspector の値とズレてた時用)
        currentLocalY = restingLocalY;
        ApplyVisualY(currentLocalY);
    }

    private void OnEnable()
    {
        // 再アクティブ時にカウンタリセット(古いカウントが残らないように)
        contactCount = 0;

        // visual も resting に戻す
        currentLocalY = restingLocalY;
        visualVelocity = 0f;
        ApplyVisualY(currentLocalY);
    }

    private void Update()
    {
        // 押下状態に応じた目標Yに向かって滑らかに動く
        // Time.unscaledDeltaTime を使うので timeScale=0.3(憑依中スロー)でもサクサク反応する
        float targetY = IsPressed ? pressedLocalY : restingLocalY;
        currentLocalY = Mathf.SmoothDamp(currentLocalY, targetY, ref visualVelocity, sinkSmoothTime, Mathf.Infinity, Time.unscaledDeltaTime);
        ApplyVisualY(currentLocalY);
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        if (!IsPressableLayer(other.gameObject.layer))
        {
            return;
        }

        contactCount++;

        if (showDebugInfo)
        {
            Debug.Log($"[PressureButton] Pressed by {other.name}, count={contactCount}", this);
        }
    }

    private void OnTriggerExit2D(Collider2D other)
    {
        if (!IsPressableLayer(other.gameObject.layer))
        {
            return;
        }

        contactCount = Mathf.Max(0, contactCount - 1);

        if (showDebugInfo)
        {
            Debug.Log($"[PressureButton] Released by {other.name}, count={contactCount}", this);
        }
    }


    // ーーー内部処理ーーー

    private void ApplyVisualY(float localY)
    {
        // visualTransform 未指定なら自身の transform を動かす(統合版)
        Transform target = visualTransform != null ? visualTransform : transform;
        Vector3 p = target.localPosition;
        p.y = localY;
        target.localPosition = p;
    }

    private bool IsPressableLayer(int layer)
    {
        return (pressableLayer.value & (1 << layer)) != 0;
    }


    // ーーーGizmosーーー
    // ボタン押下中は緑、それ以外は灰色で Trigger の範囲を可視化

    private void OnDrawGizmos()
    {
        Collider2D coll = GetComponent<Collider2D>();
        if (coll == null)
        {
            return;
        }

        Gizmos.color = IsPressed ? Color.green : Color.gray;
        Gizmos.DrawWireCube(coll.bounds.center, coll.bounds.size);
    }
}
