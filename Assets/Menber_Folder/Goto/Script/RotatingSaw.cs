using UnityEngine;

// 回転のこぎり (Rotating Saw)
// 視覚: visualTransform (子) または自身を常時回転
// 移動: moveDirection 方向へ常時進む(ハザード)。レール最大距離に到達したら停止するだけ
// 当たり判定: CircleCollider2D を Trigger にして、Spike レイヤーに置く想定
//   → BoxController.OnTriggerEnter2D が拾って即死亡演出に入る
[RequireComponent(typeof(Rigidbody2D))]
[RequireComponent(typeof(CircleCollider2D))]
public class RotatingSaw : MonoBehaviour
{
    [Header("ーーーーーーー 回転(見た目) ーーーーーーー")]
    [Header("回転速度(度/秒、+で時計回り、-で反時計回り)")]
    [SerializeField] private float rotationSpeed = 360f;

    [Header("回転を適用する子 Transform(SpriteRenderer 付き想定、null の場合は自身を回転)")]
    [SerializeField] private Transform visualTransform;

    [Header("ーーーーーーー 移動 ーーーーーーー")]
    [Header("移動方向(正規化される、+X で右、-X で左)")]
    [SerializeField] private Vector2 moveDirection = Vector2.left;

    [Header("移動速度(ユニット/秒)")]
    [SerializeField] private float moveSpeed = 1.5f;

    [Header("レールの最大移動距離(始点からの最大距離、ユニット)")]
    [SerializeField] private float railMaxDistance = 10f;

    [Header("ーーーーーーー カメラ視界トリガー ーーーーーーー")]
    [Header("メインカメラの視界に入るまで移動を遅延するか")]
    [SerializeField] private bool waitForCameraView = true;

    [Header("視界判定で使うカメラ(null ならCamera.main を使う)")]
    [SerializeField] private Camera triggerCamera;


    // ーーー内部参照ーーー
    private Rigidbody2D rb;
    private CircleCollider2D coll;
    private Vector2 startPosition;

    // 一度カメラ視界に入ったか(true 以降は永続的に true、外れても止まらない)
    private bool hasEnteredView = false;


    // ーーー外部公開プロパティーーー
    public bool IsMoving { get; private set; }
    public float DistanceFromStart => Vector2.Distance(rb.position, startPosition);


    // ーーーUnityイベントーーー

    private void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
        coll = GetComponent<CircleCollider2D>();

        // Trigger にする(プレイヤー死亡判定用、物理衝突は不要)
        coll.isTrigger = true;

        // Kinematic にして物理シミュレーション対象から外す
        rb.bodyType = RigidbodyType2D.Kinematic;
        rb.freezeRotation = true;

        startPosition = rb.position;
    }

    private void Update()
    {
        // 見た目の回転(常時、視界トリガーとは無関係に最初から回り続ける)
        // visualTransform が指定されてればそちらだけ回す。なければ自身を回す
        // CircleCollider2D は回転させても形状不変なので物理的問題なし
        Transform target = visualTransform != null ? visualTransform : transform;
        target.Rotate(0f, 0f, rotationSpeed * Time.deltaTime);

        // カメラ視界トリガー: まだ入ってなければ毎フレーム判定
        if (waitForCameraView && !hasEnteredView)
        {
            CheckCameraView();
        }
    }


    // ーーーカメラ視界判定ーーー
    // メインカメラの viewport 矩形 (0,0)〜(1,1) 内に saw の中心があれば
    // 「視界に入った」とみなして hasEnteredView を立てる
    // z > 0 のチェックでカメラの後ろにいる場合を除外

    private void CheckCameraView()
    {
        Camera cam = triggerCamera != null ? triggerCamera : Camera.main;
        if (cam == null)
        {
            return;
        }

        Vector3 viewportPos = cam.WorldToViewportPoint(transform.position);
        bool inView = viewportPos.z > 0f
            && viewportPos.x >= 0f && viewportPos.x <= 1f
            && viewportPos.y >= 0f && viewportPos.y <= 1f;

        if (inView)
        {
            hasEnteredView = true;
        }
    }

    private void FixedUpdate()
    {
        IsMoving = false;

        // カメラ視界に入るまで移動しない(回転は Update で続いてる)
        if (waitForCameraView && !hasEnteredView)
        {
            return;
        }

        Vector2 dir = moveDirection.sqrMagnitude > 0.001f ? moveDirection.normalized : Vector2.left;

        // レール残距離
        float distanceFromStart = Vector2.Distance(rb.position, startPosition);
        float remainingRail = Mathf.Max(0f, railMaxDistance - distanceFromStart);
        if (remainingRail <= 0f)
        {
            return;
        }

        // 今フレームの移動量(残距離を上限とする)
        float maxStep = moveSpeed * Time.fixedDeltaTime;
        float actualStep = Mathf.Min(maxStep, remainingRail);

        rb.MovePosition(rb.position + dir * actualStep);
        IsMoving = true;
    }


    // ーーーGizmos(レール始点〜終点と進行方向)ーーー

    private void OnDrawGizmos()
    {
        if (coll == null)
        {
            coll = GetComponent<CircleCollider2D>();
        }

        Vector2 dir = moveDirection.sqrMagnitude > 0.001f ? moveDirection.normalized : Vector2.left;
        Vector3 startPos = Application.isPlaying ? (Vector3)startPosition : transform.position;
        Vector3 endPos = startPos + (Vector3)(dir * railMaxDistance);

        // レールの線(赤)と終点(赤)
        Gizmos.color = new Color(1f, 0.3f, 0.3f);
        Gizmos.DrawLine(startPos, endPos);
        Gizmos.DrawWireSphere(endPos, 0.1f);
    }
}
