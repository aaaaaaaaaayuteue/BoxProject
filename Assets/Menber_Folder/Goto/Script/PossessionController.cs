using UnityEngine;
using UnityEngine.InputSystem;

// 憑依状態の司令塔
// シーンに1個だけ存在する想定
// 入力を読み取り、現在の本体(BoxController)に Move/Jump を流すか、目(EyesController)に自由移動を流すかを切り替える
// 状態遷移: Normal / PossessingSoul / Dying
// timeScale 復旧の責務もこのクラスが持つ
// GameManager からのリスタート時は HandleRestart() が呼ばれて初期本体に戻す
public class PossessionController : MonoBehaviour
{
    // ーーー参照ーーー
    [Header("ゲーム開始時の本体(Inspectorで指定)")]
    [SerializeField] private BoxController initialBody;

    [Header("目(EyesController、シーン直下のEyesオブジェクトを指定)")]
    [SerializeField] private EyesController eyes;

    // ーーースローモーションーーー
    [Header("ーーーーーーー ここから下はスローモーション関連 ーーーーーーー")]
    [Header("憑依モード中のtimeScale(0=完全停止、1=通常、0.3で3割速)")]
    [Range(0.05f, 1f)]
    [SerializeField] private float possessTimeScale = 0.3f;

    [Header("通常時のtimeScale(復旧時に戻す値)")]
    [Range(0.5f, 2f)]
    [SerializeField] private float normalTimeScale = 1f;

    // ーーー状態管理ーーー
    public enum PossessionState
    {
        Normal,            // 通常モード(本体を操作)
        PossessingSoul,    // 憑依モード(魂を自由移動)
        Dying              // 死亡演出中(入力無効、復帰待ち)
    }

    // ーーー内部状態ーーー
    private PossessionState currentState = PossessionState.Normal;
    private BoxController currentBody;

    // シーン内の全 BoxController(リスポーン時にリセットするため、非アクティブも含めて取得)
    private BoxController[] allBoxes;

    // ーーー入力アクション関連ーーー
    private PlayerInputActions inputActions;
    private InputAction moveAction;
    private InputAction jumpAction;
    private InputAction possessAction;
    private InputAction unpossessAction;


    // ーーー外部公開プロパティーーー
    public PossessionState CurrentState => currentState;
    public BoxController CurrentBody => currentBody;


    // ーーーUnityイベントーーー

    private void Awake()
    {
        // Input Actionsのインスタンスを生成
        inputActions = new PlayerInputActions();
        moveAction = inputActions.Player.Move;
        jumpAction = inputActions.Player.Jump;
        possessAction = inputActions.Player.Possess;
        unpossessAction = inputActions.Player.Unpossess;

        // シーン内の全 BoxController を取得しておく(非アクティブも含める、後生成は対象外)
        allBoxes = FindObjectsByType<BoxController>(FindObjectsInactive.Include, FindObjectsSortMode.None);

        // 初期本体を設定
        currentBody = initialBody;
    }

    private void Start()
    {
        // Awakeでは他コンポーネントのAwakeが終わってない可能性があるので、初期化はStartで
        // 初期本体だけ Player+InputDriven、それ以外は false
        ApplyControlledFlagToAllBoxes();

        // 目を初期本体に追従させる
        if (eyes != null)
        {
            eyes.SetCurrentBody(currentBody);
            eyes.SetMode(EyesController.EyesMode.Normal);
        }

        // timeScaleを通常値に
        Time.timeScale = normalTimeScale;
    }

    private void OnEnable()
    {
        inputActions.Player.Enable();
    }

    private void OnDisable()
    {
        inputActions.Player.Disable();

        // OnDisable でも timeScale を必ず復旧(シーン遷移やゲーム終了時の保険)
        Time.timeScale = normalTimeScale;
    }

    private void OnApplicationQuit()
    {
        // アプリ終了時も timeScale を復旧(エディタ実行で次回正常な状態にするため)
        Time.timeScale = normalTimeScale;
    }

    private void Update()
    {
        // 死亡検知: 現在の本体が死亡したら Dying 状態へ
        DetectBodyDeath();

        // 入力の読み取りと処理(状態に応じて分岐)
        HandleInputs();
    }


    // ーーー死亡検知ーーー
    // 現在の本体が IsDead になっていて、まだ Dying 状態に入ってなければ遷移する
    // Dying 中は GameManager.ExecuteRestart からの HandleRestart() 待ち

    private void DetectBodyDeath()
    {
        if (currentBody == null)
        {
            return;
        }

        if (currentBody.IsDead && currentState != PossessionState.Dying)
        {
            EnterDyingState();
        }
    }


    // ーーーDying 状態への遷移ーーー
    // 憑依モード中だった場合は強制終了して、本体に魂を戻し、timeScaleを復旧する

    private void EnterDyingState()
    {
        // 憑依中なら本体半透明を解除する(死亡演出が見えるように)
        if (currentBody != null)
        {
            currentBody.SetTransparent(false);
        }

        // 目を本体に瞬間スナップ&通常モードへ
        if (eyes != null)
        {
            eyes.SetMode(EyesController.EyesMode.Normal);
        }

        // timeScaleを通常に戻す(死亡演出が遅延再生にならないように)
        Time.timeScale = normalTimeScale;

        currentState = PossessionState.Dying;
    }


    // ーーー入力処理ーーー
    // 状態に応じて Move/Jump/Possess/Unpossess を解釈する

    private void HandleInputs()
    {
        Vector2 moveValue = moveAction.ReadValue<Vector2>();
        bool jumpPressed = jumpAction.WasPressedThisFrame();
        bool possessPressed = possessAction.WasPressedThisFrame();
        bool unpossessPressed = unpossessAction.WasPressedThisFrame();

        switch (currentState)
        {
            case PossessionState.Normal:
                HandleNormalInputs(moveValue, jumpPressed, possessPressed);
                break;

            case PossessionState.PossessingSoul:
                HandlePossessingInputs(moveValue, possessPressed, unpossessPressed);
                break;

            case PossessionState.Dying:
                // 入力は全て無視
                break;
        }
    }


    // ーーー通常モードの入力処理ーーー
    // 移動・ジャンプは現在の本体に流す
    // Z(Possess)押下で憑依モードに入る
    // Z+Space同時押し対応: Z が押されていたらジャンプは無視して憑依に専念する

    private void HandleNormalInputs(Vector2 moveValue, bool jumpPressed, bool possessPressed)
    {
        if (currentBody == null)
        {
            return;
        }

        // Z 押下があれば、ジャンプ要求は無視して憑依に切り替える
        // (同フレームに Z+Space を押された時、本体が憑依直前にジャンプしないため)
        if (possessPressed)
        {
            EnterPossessingMode();
            return;
        }

        // 水平入力を本体に渡す
        currentBody.Move(moveValue.x);

        // ジャンプ
        if (jumpPressed)
        {
            currentBody.Jump();
        }
    }


    // ーーー憑依モードの入力処理ーーー
    // 移動入力は目(魂)に流す
    // 本体には何も入力しない(本体は外力に任せる)
    // X押下: 憑依解除して通常モードに戻る
    // Z押下: 魂が憑依先候補に重なっていれば乗り移り、なければ無視

    private void HandlePossessingInputs(Vector2 moveValue, bool possessPressed, bool unpossessPressed)
    {
        if (eyes == null)
        {
            return;
        }

        // X押下で憑依解除
        if (unpossessPressed)
        {
            ExitPossessingMode();
            return;
        }

        // 魂を自由移動 → 即座に距離クランプ → その後で憑依先判定
        // (クランプを LateUpdate に任せると「限界距離を1フレーム超えた状態で Z 判定」されてしまう問題を防ぐ)
        eyes.MoveByInput(moveValue);
        eyes.EnforceDistanceClamp();

        // Z押下: 魂が憑依先候補に重なっていれば乗り移り、なければ無視
        if (possessPressed)
        {
            BoxController target = eyes.FindPossessionTarget();
            if (target != null)
            {
                SwapBody(target);
            }
        }
    }


    // ーーー憑依モードへの遷移ーーー
    // 本体を半透明化、入力を切る、目を Possessing モードへ、timeScale をスローに

    private void EnterPossessingMode()
    {
        if (currentBody == null || eyes == null)
        {
            return;
        }

        // 本体を半透明化(視覚演出、当たり判定は維持)
        currentBody.SetTransparent(true);

        // 本体の入力駆動を切る(これで Move/Jump 入力を受け付けなくなる、
        // また内部入力もクリアされ「同フレームに押された Space」が引き継がれない)
        currentBody.SetInputDriven(false);

        // 目を Possessing モードに
        eyes.SetMode(EyesController.EyesMode.Possessing);

        // 時間スロー
        Time.timeScale = possessTimeScale;

        currentState = PossessionState.PossessingSoul;
    }


    // ーーー憑依モードの解除ーーー
    // 本体の半透明を戻し、入力駆動を再開、目を Normal モードに、timeScale を通常に

    private void ExitPossessingMode()
    {
        if (currentBody != null)
        {
            currentBody.SetTransparent(false);
            currentBody.SetInputDriven(true);
        }

        if (eyes != null)
        {
            eyes.SetMode(EyesController.EyesMode.Normal);
        }

        Time.timeScale = normalTimeScale;

        currentState = PossessionState.Normal;
    }


    // ーーー乗り移り処理ーーー
    // 旧本体: SetIsPlayerBody(false), SetInputDriven(false), SetTransparent(false)
    // 新本体: SetIsPlayerBody(true) [この内部でスパイク重なりチェック → 即死可], SetInputDriven(true), ClearInputs
    // 目: 新本体に追従するよう SetCurrentBody、Normal モードへ
    // timeScale 復旧

    private void SwapBody(BoxController newBody)
    {
        // 旧本体の解除
        if (currentBody != null)
        {
            currentBody.SetTransparent(false);
            currentBody.SetInputDriven(false);
            currentBody.SetIsPlayerBody(false);
        }

        // 新本体に切り替え
        currentBody = newBody;
        currentBody.SetIsPlayerBody(true);   // ※ true 直後にスパイク重なりチェック → 重なってたら即死
        currentBody.SetInputDriven(true);
        currentBody.ClearInputs();           // 同フレームに押された Z+Space などが新本体に引き継がれない保険

        // 目を新本体へ
        if (eyes != null)
        {
            eyes.SetCurrentBody(currentBody);
            eyes.SetMode(EyesController.EyesMode.Normal);
        }

        // 時間を通常に戻す
        Time.timeScale = normalTimeScale;

        currentState = PossessionState.Normal;
    }


    // ーーー全 BoxController に Player/InputDriven フラグを適用ーーー
    // 現在の本体だけ Player=true, InputDriven=true、他は両方 false にする

    private void ApplyControlledFlagToAllBoxes()
    {
        if (allBoxes == null)
        {
            return;
        }

        for (int i = 0; i < allBoxes.Length; i++)
        {
            BoxController box = allBoxes[i];
            if (box == null)
            {
                continue;
            }

            bool isCurrent = (box == currentBody);
            box.SetIsPlayerBody(isCurrent);
            box.SetInputDriven(isCurrent);

            // 半透明化も全部解除
            box.SetTransparent(false);
        }
    }


    // ーーーリスタート処理(GameManagerから呼ばれる)ーーー
    // 死亡した本体 (= currentBody、憑依先かもしれない) を checkpoint にリスポーンする
    // それ以外の Box (initialBody含む、生きてるもの) は位置・状態とも触らない
    // リスポーン後、Player は同じ Box (リスポーンした body) を引き続き操作する
    //
    // currentBody が null の場合のみ initialBody にフォールバック
    //
    // ※ ApplyControlledFlagToAllBoxes() は呼ばない:
    //   それは全 Box の isPlayerBody/isInputDriven/transparent を触るため、
    //   「他の Box は触らない」という今回の仕様と相性が悪い。
    //   currentBody だけ直接フラグを再適用する。

    public void HandleRestart(Vector2 respawnPosition, int respawnFacing)
    {
        // リスポーン対象は「現在の本体 = 死亡した body」
        // 憑依してない元の Player (initialBody) は触らない (ユーザー要望)
        BoxController bodyToRespawn = currentBody;
        if (bodyToRespawn == null)
        {
            // 異常時のフォールバック: initialBody を使う
            bodyToRespawn = initialBody;
        }
        if (bodyToRespawn == null)
        {
            Debug.LogWarning("[PossessionController] リスポーン対象の Box が見つかりません(currentBody / initialBody 両方 null)。", this);
            return;
        }

        // 死亡した body を checkpoint にリスポーン (位置リセット + 色 / BodyType / 速度 / 入力 / isDead クリア)
        bodyToRespawn.Respawn(respawnPosition, respawnFacing);

        // currentBody はそのまま (引き続きこの body を操作する)
        currentBody = bodyToRespawn;

        // currentBody だけ Player/InputDriven/不透明を直接再適用 (他の Box には触らない)
        // 憑依モード中に死亡した場合のため、半透明解除も明示的に行う
        currentBody.SetTransparent(false);
        currentBody.SetIsPlayerBody(true);
        currentBody.SetInputDriven(true);

        // 目を currentBody に追従させる (憑依モード中だった場合に魂が宙ぶらりんになるのを防ぐ)
        if (eyes != null)
        {
            eyes.SetCurrentBody(currentBody);
            eyes.SetMode(EyesController.EyesMode.Normal);
        }

        // 時間と状態を初期化
        Time.timeScale = normalTimeScale;
        currentState = PossessionState.Normal;
    }
}
