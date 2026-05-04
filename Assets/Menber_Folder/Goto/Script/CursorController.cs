using UnityEngine;

[RequireComponent(typeof(SpriteRenderer))]
public class CursorController : MonoBehaviour
{
    // ーーー追従対象ーーー
    [Header("追従対象のプレイヤー")]
    [SerializeField] private PlayerController player;

    // ーーー位置調整ーーー
    [Header("プレイヤーからの水平距離(ユニット)")]
    [SerializeField] private float distanceFromPlayer = 3f;

    [Header("プレイヤーからの垂直オフセット(ユニット、正の値で上)")]
    [SerializeField] private float verticalOffset = 0f;

    [Header("壁として扱うレイヤー(カーソルがめり込まないようにブロックする)")]
    [SerializeField] private LayerMask groundLayer;

    // ※ カーソルの大きさはTransform.Scaleで調整する
    // ※ Sceneビューでドラッグするか、InspectorのTransform > Scaleで変更可能
    // ※ 当たり判定や挙動には関係ないため、SerializeFieldでは管理しない

    // ーーー内部参照ーーー
    private SpriteRenderer spriteRenderer;

    // ーーー外部公開プロパティーーー
    // フリスビーの投擲目標として、フリスビー側から参照される

    // カーソル中心のワールド座標
    public Vector2 CenterPosition => (Vector2)transform.position;

    // カーソルのプレイヤー側の側面のX座標(フリスビーがここに到達したら減速開始)
    public float NearSideX => GetNearSideX();

    // カーソルの実際の大きさ(Transform.Scaleから取得)
    public Vector2 Size => new Vector2(transform.localScale.x, transform.localScale.y);


    // ーーーUnityイベントーーー

    private void Awake()
    {
        spriteRenderer = GetComponent<SpriteRenderer>();
    }

    private void LateUpdate()
    {
        // LateUpdateで位置更新する
        // プレイヤーの位置と向きの更新(Update内)が完了した後に追従するため
        // これによりカクつきを防げる
        if (player == null)
        {
            return;
        }

        UpdateCursorPosition();
    }


    // ーーーカーソル位置の更新ーーー
    // プレイヤーの位置と向きを取得して、その方向に固定距離ずらした位置に自分を移動する
    // ただし、目前に壁がある場合は壁の表面にぴったりつくようにブロックする(壁にめり込まない)

    private void UpdateCursorPosition()
    {
        // プレイヤーの向き(+1=右、-1=左)を取得
        int direction = player.FacingDirection;

        // プレイヤー位置とカーソルY座標を計算
        Vector2 playerPos = player.transform.position;
        float targetY = playerPos.y + verticalOffset;

        // カーソルの幅の半分(壁判定時の補正で使う)
        float cursorHalfWidth = Mathf.Abs(transform.localScale.x) * 0.5f;

        // 理想位置のX(壁判定なしの基本位置)
        float idealX = playerPos.x + (distanceFromPlayer * direction);

        // プレイヤーから理想位置までX方向にRaycastを飛ばして、壁があるかチェック
        // 始点はカーソルのY位置に合わせる(プレイヤーのYにverticalOffsetを足した位置)
        Vector2 rayOrigin = new Vector2(playerPos.x, targetY);
        Vector2 rayDirection = new Vector2(direction, 0f);
        float rayLength = distanceFromPlayer;

        RaycastHit2D hit = Physics2D.Raycast(rayOrigin, rayDirection, rayLength, groundLayer);

        float targetX;
        if (hit.collider != null)
        {
            // 壁にヒット：壁の表面からカーソル幅の半分だけ手前にずらす
            // (カーソルの壁側の側面が壁の表面にピタッと一致、隙間なし)
            targetX = hit.point.x - (cursorHalfWidth * direction);

            // プレイヤーが壁の中にいる等で、targetXがプレイヤーより手前(逆側)になる場合
            // カーソルがプレイヤーの逆側に表示されないように、プレイヤー位置に置く
            bool cursorBehindPlayer = (direction == 1 && targetX < playerPos.x) || (direction == -1 && targetX > playerPos.x);
            if (cursorBehindPlayer)
            {
                targetX = playerPos.x;
            }
        }
        else
        {
            // 壁にヒットしなかった：理想位置を使う
            targetX = idealX;
        }

        transform.position = new Vector3(targetX, targetY, transform.position.z);
    }


    // ーーーカーソルのプレイヤー側の側面のX座標を計算ーーー
    // フリスビーがこのX座標に到達したら減速処理を始める
    // プレイヤーの向きによって、左側か右側かが変わる
    // カーソルの幅はTransform.localScale.xを参照する

    private float GetNearSideX()
    {
        if (player == null)
        {
            return transform.position.x;
        }

        // 現在のスケールから幅の半分を計算
        float halfWidth = Mathf.Abs(transform.localScale.x) * 0.5f;
        int direction = player.FacingDirection;

        // プレイヤーが右向きならカーソルの左側面が「プレイヤー側」
        // プレイヤーが左向きならカーソルの右側面が「プレイヤー側」
        if (direction == 1)
        {
            return transform.position.x - halfWidth;
        }
        else
        {
            return transform.position.x + halfWidth;
        }
    }


    // ーーーGizmos(エディタ上での可視化)ーーー
    // Sceneビューでカーソルの範囲とプレイヤー側の側面、壁判定Raycastを確認できるようにする
    // カーソルの大きさはTransform.localScaleから取得するので、Sceneビューでドラッグして変更可能

    private void OnDrawGizmos()
    {
        // 現在のScaleからGizmoのサイズを取得
        Vector2 currentSize = new Vector2(
            Mathf.Abs(transform.localScale.x),
            Mathf.Abs(transform.localScale.y)
        );

        // カーソル本体の範囲を黄色のワイヤーフレームで表示
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireCube(transform.position, currentSize);

        // プレイヤー側の側面を赤い縦線で表示、壁判定Raycastをシアンで表示(再生中のみ)
        if (Application.isPlaying && player != null)
        {
            // プレイヤー側の側面を赤い縦線で表示
            Gizmos.color = Color.red;
            float nearX = GetNearSideX();
            Vector3 top = new Vector3(nearX, transform.position.y + currentSize.y * 0.5f, 0f);
            Vector3 bottom = new Vector3(nearX, transform.position.y - currentSize.y * 0.5f, 0f);
            Gizmos.DrawLine(top, bottom);

            // 壁判定Raycastの可視化(プレイヤーから理想位置までシアンの線)
            int direction = player.FacingDirection;
            Vector2 playerPos = player.transform.position;
            float rayY = playerPos.y + verticalOffset;
            Vector3 rayStart = new Vector3(playerPos.x, rayY, 0f);
            Vector3 rayEnd = new Vector3(playerPos.x + (distanceFromPlayer * direction), rayY, 0f);
            Gizmos.color = Color.cyan;
            Gizmos.DrawLine(rayStart, rayEnd);
        }
    }
}