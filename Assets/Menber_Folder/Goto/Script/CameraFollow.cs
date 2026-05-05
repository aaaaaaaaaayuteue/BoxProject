using UnityEngine;

// カメラ追従コントローラ
// PossessionController の状態に応じて追従先を切り替える
//   通常モード: 現在の本体(BoxController)を追従
//   憑依モード: 目(EyesController.transform)を追従
// SmoothDamp で滑らかに追従。Time.unscaledDeltaTime を使うのでスローモーション中もカクつかない
[RequireComponent(typeof(Camera))]
public class CameraFollow : MonoBehaviour
{
    [Header("司令塔(現在の本体と状態を取得するため)")]
    [SerializeField] private PossessionController possessionController;

    [Header("目(憑依モード中はこちらを追従先にする)")]
    [SerializeField] private EyesController eyes;

    [Header("カメラのZ座標(2Dゲームでは通常 -10)")]
    [SerializeField] private float cameraZ = -10f;

    [Header("追従先からのワールドオフセット(画面中央からのズラし)")]
    [SerializeField] private Vector2 followOffset = new Vector2(0f, 0f);

    [Header("追従の追いつき速度(SmoothDampの時定数、小さいほど即座に追従、大きいほどゆっくり)")]
    [SerializeField] private float smoothTime = 0.15f;

    [Header("追従の最大速度(ユニット/秒、大きい値で実質無制限)")]
    [SerializeField] private float maxFollowSpeed = 100f;

    // ーーー内部状態ーーー
    private Vector3 currentVelocity;


    // ーーーUnityイベントーーー

    private void LateUpdate()
    {
        Vector3 target = GetTargetWorldPosition();

        Vector3 desired = new Vector3(
            target.x + followOffset.x,
            target.y + followOffset.y,
            cameraZ
        );

        // SmoothDamp は Time.deltaTime を内部で使うので、
        // スローモーション(Time.timeScale=0.3)中もカメラが鈍くならないように unscaledDeltaTime を使う
        transform.position = Vector3.SmoothDamp(
            transform.position,
            desired,
            ref currentVelocity,
            smoothTime,
            maxFollowSpeed,
            Time.unscaledDeltaTime
        );
    }


    // ーーー追従先のワールド座標を取得ーーー
    // 憑依モード中なら目(魂)の位置、それ以外は現在の本体の位置

    private Vector3 GetTargetWorldPosition()
    {
        if (possessionController != null && eyes != null
            && possessionController.CurrentState == PossessionController.PossessionState.PossessingSoul)
        {
            return eyes.transform.position;
        }

        if (possessionController != null && possessionController.CurrentBody != null)
        {
            return possessionController.CurrentBody.transform.position;
        }

        // 何も参照できなければカメラの現在位置を維持
        return transform.position;
    }
}
