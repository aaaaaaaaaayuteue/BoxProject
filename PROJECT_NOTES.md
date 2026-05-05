# PROJECT_NOTES — 入れ替えゲーム（仮称）

最終更新: 2026-05-05
仕様書: `C:\Users\suras\Downloads\入れ替えゲーム(仮称).pdf`
ベースプロジェクト: 既存 BoxProject（犬連れフリスビーゲームから流用）

---

## 1. ゲーム仕様（仕様書 PDF より）

**ジャンル**: 2D 横スクロール パズルアクション
**コアコンセプト**: 顔のついた白い箱が「憑依」で別の白い箱に入れ替わる

### キャラクター・操作
- 主人公 = 顔（目「○○」）付き白箱
- 移動・ジャンプ（既存犬と同じ操作感）
- **Z**: 憑依モード ON / 別の白箱に乗り移り
- **X**: 憑依モード OFF（魂を本体に戻す）

### 憑依モード仕様
| 要素 | 仕様 |
|---|---|
| 発動 | Z で幽体離脱、魂(=目)が本体から出る |
| 移動 | 重力・当たり判定なしで上下左右自由 |
| 範囲制限 | 本体から最大距離までクランプ（限界あり） |
| 無敵 | 魂はデッドゾーン(赤い領域)に入っても平気 |
| **死亡条件** | **本体の箱がダメージを受けると死亡**（憑依中も本体は無防備） |
| 時間 | 憑依モード中はスローモーション |
| 乗り移り | 別の白箱に重なって Z で新本体に切替 |

### 箱
- 白箱は押して動かせる
- 押せる箱と憑依先は**同じ白箱**

### ステージギミック（仕様書例より）
- トゲ・のこぎり（即死）
- ボタン式リフト（押している間だけ動く）

---

## 2. 既存プロジェクト調査結果

### 流用するもの
- `PlayerController.cs`（540行）— 移動/ジャンプ/接地判定/死亡演出/リスポーン完備、[Header]+SerializeField でインスペクター可変、マジックナンバーなし。**ロジックを `BoxController` に移植**
- `GameManager.cs` — リスポーン地点管理、`ExecuteRestart()`。基本そのまま流用、`player` 参照を `PossessionController` 経由に小修正
- `RespawnPoint.cs` — そのまま流用
- 新 Input System (`PlayerInputActions.inputactions`)、Move/Jump/Throw アクション既存

### 今回未使用（シーンで OFF にする・削除しない）
- `DogController.cs`（1600行、フリスビー追従の複雑ロジック）
- `FrisbeeController.cs` / `FrisbeeThrower.cs` / `CursorController.cs`
- 既存 Input Actions の `Throw=Z` バインディングは残す（衝突するが Dog/Frisbee OFF なので実害なし）

---

## 3. アーキテクチャ（Codex レビュー反映版）

### クラス分割
| ファイル | 役割 | 補足 |
|---|---|---|
| **`BoxController.cs`**（新規） | 白箱個体の物理・移動・ジャンプ・接地・死亡演出・押される。**入力は読まない**、外部から `Move(float)` / `Jump()` 呼ばれる | 既存 `PlayerController.cs` の移動/ジャンプ/死亡ロジックを移植 |
| **`PossessionController.cs`**（新規） | 司令塔。シーンに1個。現在本体・状態遷移・入力読み取り・Z/X 切替・乗り移り・スローモーション・timeScale 復旧責務 | static 廃止。Inspector で「初期本体」を指定 |
| **`EyesController.cs`**（新規） | 目・点線・進行方向先行・距離Hardクランプ・近接箱検出 | `Update + Time.unscaledDeltaTime + transform.position`、Rigidbody2D 不使用 |
| `GameManager.cs`（小修正） | リスポーン・チェックポイント | `player` 参照を `PossessionController` 経由に |
| `RespawnPoint.cs`（流用） | そのまま | |
| 既存 `PlayerController.cs` | 使わなくする（削除はしない、Hierarchy で OFF） | |

### 状態遷移表
| 状態 | Z 押下 | X 押下 | 本体被弾 | 完了 |
|---|---|---|---|---|
| **Normal** | → PossessingSoul | （無視） | → Dying | – |
| **PossessingSoul** | 魂が他箱と重なってる→**SwappingBody** / なければ無視 | → Normal（魂を本体へ戻す） | → Dying（魂強制帰還、timeScale=1） | – |
| **SwappingBody** | （一瞬の遷移） | – | – | → Normal（新本体） |
| **Dying** | 入力無効 | 入力無効 | （無視） | タイマー経過 → Respawning |
| **Respawning** | 入力無効 | 入力無効 | – | 初期本体へ復帰、timeScale=1 → Normal |

### 設計判断（Codex 宿題への回答）
| # | 項目 | 方針 |
|---|---|---|
| 1 | 状態遷移 | ↑ 5状態で管理 |
| 2 | 死亡対象 | **「現在の本体」だけが死亡判定**。乗り移り後の元本体は普通の白箱（色も元通り） |
| 3 | Rigidbody2D 方針 | **白箱は常に Dynamic**。死亡時のみ Kinematic（既存挙動踏襲）。操作中は `linearVelocity.x` を入力で上書き |
| 4 | 魂の移動方式 | `Update + Time.unscaledDeltaTime + transform.position`、Rigidbody2D 不使用 |
| 5 | timeScale 責務 | `PossessionController` 独占管理。`ExitPossessMode` / `Dying` / `Respawning` / `OnDisable` / `OnApplicationQuit` で必ず 1.0 に戻す |
| 6 | 命名 | `PlayerController` は使わず、`BoxController` ＋ `PossessionController` ＋ `EyesController` |

### エッジケース対応
- **Z 連打**: 状態フラグで多重発火ガード
- **憑依中に本体スパイク死**: 強制 Dying。魂を本体位置に戻し timeScale=1.0、その後通常の死亡演出
- **乗り移り候補複数**: 一番近い箱を選ぶ
- **壁越し箱**: 今は考慮せず（必要なら後で Linecast 追加）
- **チェックポイント更新**: 本体接触のみ。魂は無視
- **リスポーン位置**: 「初期本体」(Inspector 指定)に戻る
- **非操作の白箱がデッドゾーン落下**: 放置（消えてもゲーム続行）
- **本体半透明化**: SpriteRenderer の `color.a` だけ変更、当たり判定は保持

### 目（Eyes）の設計
- シーン直下の独立 GameObject（どの箱の子でもない）
- 通常時: 現在の本体に追従＋進行方向に少し先行
- 憑依時: PossessionController から直接 transform 操作で自由移動
- 子に EyeLeft / EyeRight (SpriteRenderer) と LineRenderer（点線、黒→限界で赤）
- スプライトは楕円を動的生成 or 既存 Knob を縦楕円スケール

### Input Actions
- 既存: Move (Vector2) / Jump (Button) / Throw (Button=Z) ← 残す
- **追加**: `Possess` (Z) / `Unpossess` (X)

---

## 4. 実装フェーズ

| Phase | 内容 |
|---|---|
| **A** | Input Actions に Possess(Z) / Unpossess(X) 追加 |
| **B** | `BoxController.cs` 作成（既存 PlayerController から移動/ジャンプ/死亡ロジック移植） |
| **C** | `EyesController.cs` 作成 + Eyes GameObject をシーンに配置 |
| **D** | `PossessionController.cs` 作成 + 状態遷移実装 |
| **E** | 既存 Scene の改造（既存 Player に BoxController 付ける、PlayerController OFF、Eyes 追加、PossessionController 配置） |
| **F** | 別の白箱を1〜2個配置して乗り移り動作確認 |
| **G** | あなたが Unity で再生して動作テスト・FB |

---

## 5. 配置・命名規則

- スクリプト: `Assets/Menber_Folder/Goto/Script/`（既存と同じ）
- シーン: 既存 `Assets/Menber_Folder/Goto/Scene/Scene.unity` を改造
- 新規 GameObject 命名: `Eyes`, `WhiteBox_01`, `WhiteBox_02`, ...
- 既存 Spikes / RespawnPoint はそのまま流用

---

## 6. 安全弁

- MCP の削除系ツール (`assets-delete`, `gameobject-destroy`, `script-delete` 等) は **OFF**
- 不要オブジェクトは Hierarchy で無効化のみ
- 大きな変更前に git commit 推奨
