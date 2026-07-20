# YomiagePlayer iOS版 — 実装計画 (タスク単位)

> 仕様は同日付の `2026-07-20-ios-app-claude-prompt.md` を正とする。本書はそれを
> **実行可能なタスク列**に分解したもの。両ファイルを新リポジトリの `docs/plans/` に置き、
> Claude Code (Sonnet) には「仕様書を読んだ上で、本書のタスクを番号順に実行せよ」と指示する。
>
> 各タスクは「ビルド+全テストが通る」状態で完了すること。テストコードが載っているタスクは
> **テストを先に書いて red → 実装して green** の順で進める(TDD)。

---

## リポジトリ構成と開発コマンド

Xcode プロジェクトは手書きせず **XcodeGen** で生成する(pbxproj をエージェントが安全に編集するため)。

```
YomiagePlayer-iOS/
├── project.yml                 # XcodeGen 定義(これが真実、pbxproj は生成物)
├── App/                        # アプリターゲット
│   ├── App.swift               # @main, DI組み立て
│   ├── Views/                  # SwiftUI
│   ├── ViewModels/
│   ├── Services/               # AVFoundation / WhisperKit / bookmark 等 iOS 依存層
│   └── Resources/              # Assets, ライセンス文書
├── YomiageCore/                # ローカル Swift Package(UI非依存ロジック)
│   ├── Package.swift
│   ├── Sources/YomiageCore/
│   └── Tests/YomiageCoreTests/
└── docs/plans/                 # 仕様書と本書
```

```bash
brew install xcodegen                       # 初回のみ
xcodegen generate                           # project.yml → .xcodeproj
swift test --package-path YomiageCore       # Core の単体テスト(macOSホストで実行、最速ループ)
xcodebuild -project YomiagePlayer.xcodeproj -scheme YomiagePlayer \
  -destination 'platform=iOS Simulator,name=iPhone 16' build      # アプリのビルド確認
xcodebuild ... test                         # アプリ側テスト(ViewModel層)
```

- `YomiageCore` は **iOS/macOS 両対応**にする(`swift test` を Mac 上で回すため)。
  UIKit/SwiftUI/WhisperKit を import しない。Foundation + CryptoKit + AVFoundation(アートワーク読取のみ)まで可
- アプリ側は WhisperKit を SwiftPM で依存追加(`https://github.com/argmaxinc/WhisperKit`)

---

## 事前スパイク(Task 0 の前に 30分ずつ、結果を docs/plans/spike-notes.md に記録)

本計画には iOS 固有の「動くと信じているが未検証」の仮定が3つある。先に最小コードで潰す:

- **S1: WhisperKit のストリーミング** — セグメント確定ごとのコールバック
  (`segmentCallback` / `TranscriptionCallback` 相当)が本当に逐次発火するか、
  タイムスタンプは秒で取れるか、`Task` キャンセルで即中断できるかを確認。
  → 取れる粒度次第で Task 13 のプロトコル設計を確定する
- **S2: security-scoped bookmark** — フォルダを UIDocumentPicker で取得 → bookmark 保存 →
  アプリ再起動後に解決 → `startAccessingSecurityScopedResource` 下で再帰列挙、が
  「On My iPhone」と「iCloud Drive」の両方で動くか。iCloud 側は未ダウンロードファイルの扱いも見る
- **S3: バックグラウンド再生中の解析継続** — audio バックグラウンドモードで再生中、
  裏で CPU タスク(ダミーの重い処理)が動き続けるかを実機で確認。
  動かない場合は「解析はフォアグラウンドのみ、復帰時に再開」に仕様を縮退させる

---

## マイルストーン0: 骨格

### Task 0: リポジトリ初期化
- `project.yml`(最小構成: ターゲット `YomiagePlayer`, iOS 17.0, `YomiageCore` をローカルパッケージ参照)

```yaml
name: YomiagePlayer
options:
  bundleIdPrefix: com.example        # ユーザーの開発チームIDに合わせて後で変更
packages:
  YomiageCore: { path: YomiageCore }
  WhisperKit: { url: https://github.com/argmaxinc/WhisperKit, from: 0.9.0 }
targets:
  YomiagePlayer:
    type: application
    platform: iOS
    deploymentTarget: "17.0"
    sources: [App]
    dependencies:
      - package: YomiageCore
      - package: WhisperKit
    info:
      path: App/Info.plist
      properties:
        UIBackgroundModes: [audio]
        UIFileSharingEnabled: true
        LSSupportsOpeningDocumentsInPlace: true
```

- `YomiageCore/Package.swift`(platforms: `[.iOS(.v17), .macOS(.v14)]`、Testing 使用)
- 空の `App.swift`(TabView 3枚 + プレースホルダ)、`.gitignore`(`*.xcodeproj`, `DerivedData`, `.build`)
- 新リポジトリ用 `CLAUDE.md`(上記コマンド、構成、両 docs へのポインタを記載)
- **完了条件**: `xcodegen generate` → シミュレータでビルド成功、`swift test` が(0件で)成功

---

## マイルストーン1: YomiageCore(UIなし・全てテストファースト)

### Task 1: `TranscriptSegment` + `SegmentLocator`

```swift
public struct TranscriptSegment: Codable, Equatable, Sendable {
    public let start: Double   // 秒
    public let end: Double
    public let text: String
}

public enum SegmentLocator {
    /// start昇順の配列に対する二分探索。半開区間 [start, end)。該当なしは nil。
    public static func findIndex(in segments: [TranscriptSegment], at seconds: Double) -> Int?
    /// 再生位置が最後のセグメントの end を超えているか(解析追い越しバナー用)
    public static func isAheadOfLast(in segments: [TranscriptSegment], at seconds: Double) -> Bool
}
```

先に書くテスト:

```swift
import Testing
@testable import YomiageCore

@Suite struct SegmentLocatorTests {
    let segs = [
        TranscriptSegment(start: 0, end: 2, text: "a"),
        TranscriptSegment(start: 2, end: 5, text: "b"),
        TranscriptSegment(start: 7, end: 9, text: "c"),
    ]
    @Test func 該当セグメントを返す() {
        #expect(SegmentLocator.findIndex(in: segs, at: 0.0) == 0)
        #expect(SegmentLocator.findIndex(in: segs, at: 4.99) == 1)
        #expect(SegmentLocator.findIndex(in: segs, at: 8.0) == 2)
    }
    @Test func 半開区間でendは次のセグメント() {
        #expect(SegmentLocator.findIndex(in: segs, at: 2.0) == 1)
    }
    @Test func 隙間と範囲外はnil() {
        #expect(SegmentLocator.findIndex(in: segs, at: 5.5) == nil)  // 5〜7の隙間
        #expect(SegmentLocator.findIndex(in: segs, at: 9.0) == nil)
        #expect(SegmentLocator.findIndex(in: segs, at: -1) == nil)
        #expect(SegmentLocator.findIndex(in: [], at: 0) == nil)
    }
    @Test func 追い越し判定() {
        #expect(SegmentLocator.isAheadOfLast(in: segs, at: 9.5))
        #expect(!SegmentLocator.isAheadOfLast(in: segs, at: 8.0))
        #expect(SegmentLocator.isAheadOfLast(in: [], at: 0))  // 何も解析されていない=常に先
    }
}
```

### Task 2: `ContentHasher`
- `SHA256(fileSize(8バイトLE) + 先頭1MB + 末尾1MB)` の hex。2MB未満は全読み。CryptoKit 使用
- テスト(一時ディレクトリに 3MB のファイルを生成して検証):
  - 同一内容 → 同一キー/リネーム後も同一キー
  - **中央のバイトを変えてもキーは不変**(仕様であることをテストで固定)
  - 先頭・末尾・サイズを変えるとキーが変わる
  - 2MB未満ファイルでも安定して同一キー

### Task 3: `TranscriptionCache`
- `init(directory: URL)`。`load(key:model:) -> TranscriptionResult?` / `save(_:key:model:)`
- ファイル名 `{key}-{model}.json`、保存は `.tmp` に書いて `FileManager.replaceItem`(原子的)
- `TranscriptionResult = { language: String, segments: [TranscriptSegment] }`
- テスト: save→load ラウンドトリップ / 不存在は nil / **壊れたJSONを置いても nil(クラッシュしない)** /
  保存後にディレクトリへ `.tmp` が残っていない

### Task 4: `HallucinationFilter`
- 仕様書の3ルールを実装。`shouldDrop(_ segment:, previous:) -> Bool`
- テスト(仕様の境界を全て固定):

```swift
@Suite struct HallucinationFilterTests {
    let f = HallucinationFilter()
    func seg(_ t: String, _ s: Double = 0, _ e: Double = 1) -> TranscriptSegment {
        .init(start: s, end: e, text: t)
    }
    @Test func 括弧のみは捨てる() {
        #expect(f.shouldDrop(seg("(笑)"), previous: nil))
        #expect(f.shouldDrop(seg("【咀嚼音】"), previous: nil))
        #expect(f.shouldDrop(seg("（拍手）（笑）"), previous: nil))
    }
    @Test func 本文があれば括弧付きでも残す() {
        #expect(!f.shouldDrop(seg("そうなんだ(笑)"), previous: nil))
    }
    @Test func 定型句で始まるものは捨てる() {
        #expect(f.shouldDrop(seg("ご視聴ありがとうございました"), previous: nil))
        #expect(f.shouldDrop(seg("ご視聴ありがとうございました。また次回!"), previous: nil))
    }
    @Test func 短時間の同文反復は捨てるが長い反復は残す() {
        let prev = seg("ラララ", 0, 1.5)
        #expect(f.shouldDrop(seg("ラララ", 1.5, 3.0), previous: prev))          // 1.5s < 2s
        #expect(!f.shouldDrop(seg("ラララ", 1.5, 5.0), previous: prev))         // 3.5s ≥ 2s
        #expect(!f.shouldDrop(seg("ラララ", 1.5, 3.0), previous: seg("別の文"))) // 不一致
    }
    @Test func 空白記号のみは捨てる() {
        #expect(f.shouldDrop(seg("  …。"), previous: nil))
    }
}
```

### Task 5: `M3u8Serializer`
- 仕様どおり(UTF-8 BOMなし、`#EXTINF:-1,名前`、読み込みは `#`/空行スキップ+相対パス解決)
- テスト: ラウンドトリップ / 日本語ファイル名 / 相対パスが m3u8 基準で絶対化される / コメント行無視

### Task 6: `AppSettings` + `SettingsStore`
- Codable struct: `modelId`(既定 "small")、`language`(既定 "ja")、`volume`、
  `folderBookmarks: [Data]`、`idlePrefetchEnabled`(既定 true)、`idlePrefetchOnlyWhileCharging`(既定 true)、
  `playbackRate`(既定 1.0)
- `.tmp` → replace の原子的保存。読み込み失敗は既定値
- テスト: ラウンドトリップ / 壊れたファイル→既定値 / 未知キーを含むJSONでも読める(前方互換)

**M1 完了条件**: `swift test --package-path YomiageCore` が全緑。アプリ側は未着手のまま。

---

## マイルストーン2: 再生の縦切り

### Task 7: `PlaybackService`(App/Services)
- `AVPlayer` を1つ保持し wrap。API: `open(url:)`, `play()`, `pause()`, `seek(to:)`,
  `rate`, `volume`。イベント(Combine か AsyncStream): `positionChanged`(periodicTimeObserver,
  0.25s間隔)、`durationKnown`, `didFinish`, `isPlayingChanged`
- `AVAudioSession` category `.playback` を起動時に設定。中断(電話等)ハンドリング
- **注意(Windows版からの教訓)**: 停止状態からのシークは「シーク位置を保持してから再生開始」の順を
  サービス内で吸収し、呼び出し側(歌詞タップ)は状態を気にせず `seek(to:)` だけ呼べばよい設計にする
- テスト: シミュレータでのXCTest は薄く(状態遷移のみ)。実挙動はフィクスチャ wav の手動確認で可

### Task 8: `NowPlayingService`
- `MPNowPlayingInfoCenter`(タイトル・時間・レート・アートワーク)と
  `MPRemoteCommandCenter`(play/pause/next/prev/changePlaybackPosition)を PlaybackService に接続
- **完了条件**: ロック画面・コントロールセンターから操作できる(実機確認項目としてメモを残す)

### Task 9: プレイヤーUI(操作系のみ)
- `PlayerBarView`(ミニプレイヤー: タブの上に常駐、タイトル+再生/一時停止)
- `PlayerScreenView`(fullScreenCover: 仮アートワーク領域、シークバー、
  再生/一時停止/前後、速度メニュー 0.5–2.0x、音量)
- `PlaybackViewModel`(@MainActor、PlaybackService のイベントを購読して @Published を更新)
- ライブラリタブに仮の「ファイルを開く」(fileImporter)を置き、1ファイル再生を通す
- **完了条件**: ファイル選択 → 再生 → シーク → バックグラウンド再生継続、が動く

---

## マイルストーン3: ライブラリ + プレイリスト

### Task 10: `BookmarkStore` + `MediaFiles`
- `BookmarkStore`(App/Services): フォルダURL ⇄ bookmark Data の保存・解決・失効(stale)時の再作成、
  `withAccess(url) { ... }` ヘルパ(start/stopAccessingSecurityScopedResource の対応漏れ防止)
- `MediaFiles`(YomiageCore): 対応拡張子定義(仕様書の v1 リスト+非対応リスト)、
  再帰列挙(名前順、大文字小文字無視)。列挙自体は URL ベースで Core に置きテスト可能にする
- テスト(Core側): 一時ディレクトリにダミーファイル群 → 対応形式のみ・名前順で列挙される

### Task 11: ライブラリ画面
- `LibraryViewModel`: 登録フォルダ一覧(SettingsStore の bookmarks から復元)、追加
  (UIDocumentPicker, `.folder`)、複数選択削除、フォルダ内ファイルのサブフォルダごと
  グルーピング、全フォルダ横断のインクリメンタル検索
- `LibraryView`: フォルダ一覧 → ファイル一覧(セクション=サブフォルダ)、検索バー、
  行スワイプ「プレイリストに追加」、非対応形式はグレーアウト+タップ不可
- **完了条件**: フォルダ登録 → アプリ再起動 → 一覧が復元され、タップで再生が始まる

### Task 12: プレイリスト
- `PlaylistViewModel`(Core のロジックは薄いので VM 直実装): `replaceAll`, `append`,
  `playItem`, `playNext(manual:)`, `playPrev`, `remove`, 並べ替え。`didFinish` で自動 next、
  末尾なら停止。フォルダごとセクション表示
- ライブラリの行タップ =「そのフォルダ全曲で replaceAll + タップ曲から再生」、
  スワイプ =「append」
- m3u8: ShareLink でエクスポート、fileImporter でインポート(読み込んだパスは
  登録フォルダ配下と突き合わせて解決、解決できない行はスキップして件数を表示)
- テスト(アプリ側テストターゲット): next/prev の端の挙動、remove 中の再生継続、m3u8 の解決
- **完了条件**: 連続再生・並べ替え・ロック画面の前後トラックが機能する

---

## マイルストーン4: 文字起こしの縦切り

### Task 13: `Transcriber` プロトコル + WhisperKit アダプタ + モデル管理
- Core に置くプロトコル(S1 スパイクの結果で確定させる):

```swift
public protocol Transcriber: Sendable {
    /// セグメント確定ごとに yield する。キャンセルで即中断。
    func transcribe(audioURL: URL, language: String) -> AsyncThrowingStream<TranscriptSegment, Error>
}
public protocol TranscriberFactory: Sendable {
    func make(modelId: String) async throws -> Transcriber
}
```

- App 側 `WhisperKitTranscriber`: WhisperKit をロードし、動画/音声URLを 16kHz mono に
  デコード(WhisperKit の AudioProcessor で足りなければ AVAssetReader で自前デコード)して
  ストリーミング認識。コンテキスト持ち越し無効、無音系パラメータ設定
- `ModelDownloadManager`: tiny/small/medium の一覧、ダウンロード(進捗)、削除、
  保存先 `Application Support/models/`+`isExcludedFromBackup`
- 設定タブUI: モデル一覧(サイズ・DL済みバッジ・進捗バー・削除)、言語、先読み設定
- **完了条件**: 実機でモデルをDLし、フィクスチャ音声(短い日本語読み上げ)の認識結果が流れてくる

### Task 14: `TranscriptionQueue`(Core, actor)
- API: `enqueue(key: String, preempt: Bool, job: @Sendable (CancellationToken的な文脈) async throws -> Void)`,
  `var isIdle: Bool`, `shutdown()`
- 不変条件: 同時実行1 / 待ちはLIFO / `preempt: true` は実行中ジョブに `Task.cancel` をかけて先頭で開始 /
  preempt:false は決して割り込まない
- テスト(フェイクジョブ = `CheckedContinuation` で進行を手動制御):
  - 3件投入 → 完了順が LIFO であること
  - 実行中に preempt 投入 → 実行中ジョブが CancellationError で終わり、新ジョブが先に走る
  - preempt:false 投入では実行中ジョブがキャンセルされない
  - キャンセルされたジョブの後続処理(保存)が走らないこと(呼び出し側フラグで検証)

### Task 15: `TranscriptionCoordinator`(Core)
- 依存は全てプロトコル注入: `TranscriberFactory` / `TranscriptionCache` / `TranscriptionQueue` /
  `HallucinationFilter` / UIコールバック(`@MainActor` クロージャ群:
  `onReset`, `onSegment`, `onReady(fromCache:)`, `onFailed(message:)`)
- `mediaChanged(url:)`: ハッシュ → cache hit なら onReady / miss なら preempt ジョブ投入
- `reanalyze(url:)`: キャッシュ削除 → preempt ジョブ
- `ensureAnalyzed(url:)`: cache-only(UIコールバックを一切呼ばない)、preempt: false
- **表示ゲート**: `currentKey` と一致しないジョブのコールバックは無視
- テスト(フェイク Transcriber = 指定セグメントを await 付きで流す):
  - キャッシュヒットで transcribe が呼ばれない
  - 曲Aの解析中に曲Bへ変更 → Aのセグメント/完了が UI に届かない(ゲート)、Aは保存されない
  - フィルタで落ちたセグメントが UI にもキャッシュにも入らない
  - 完走時のみ save が呼ばれ、その内容がフィルタ通過分と一致
  - ensureAnalyzed が UI コールバックを呼ばずキャッシュだけ作る

### Task 16: トランスクリプトUI
- `TranscriptViewModel`: `rows: [SegmentRow]`(逐次 append)、`state`(idle/analyzing/ready/failed)、
  `currentIndex`(SegmentLocator)、`isAheadOfAnalysis`、自動スクロール停止/再開、タップシーク、再解析
- `TranscriptView`(PlayerScreenView 内): ScrollViewReader で現在行へスクロール、
  ユーザースクロール検知で自動追従停止+「現在位置へ」ボタン、解析中フッタ、追い越しバナー、
  モデル未DL時の導線表示
- `PlaybackService.positionChanged` → `TranscriptViewModel.updatePosition`(@MainActor)
- テスト: VM のみ(updatePosition でのハイライト遷移、隙間で nil、追い越しフラグ)
- **完了条件**: 曲を開く → 再生開始と同時に解析が走り、行が順次増え、位置同期・タップシーク・
  曲切替の即時割り込みが体感で正しい

---

## マイルストーン5: 仕上げ

### Task 17: `ArtworkLocator` + サムネイル + ギャラリー
- Core 実装(仕様書のヒューリスティック: 埋め込み優先→フォルダ画像、`.meta.json` 境界、
  祖先2階層、定番名順、埋め込みとフォルダ画像は混在させない)
- テスト: 一時ディレクトリでフォルダ構成を組んで各分岐を固定
  (同名優先 / cover系優先 / library root 再帰 / marker あり祖先の再帰 / marker なし祖先は同名・定番名のみ)
- App 側: `NSCache` サムネイルキャッシュ、ライブラリ/プレイリスト行に表示、
  再生画面は TabView(.page) で全候補スワイプ
- 動画ファイルは `AVAssetImageGenerator` で先頭フレームをサムネイルに

### Task 18: `IdleAnalysisService`
- タイマー(15s)で `queue.isIdle` のときだけ: ①プレイリストの未再生曲を順に →
  ②登録フォルダ走査、未キャッシュ1件を `ensureAnalyzed`
- ガード: フォアグラウンドのみ(`scenePhase`)/設定 OFF で停止/「充電中のみ」ON なら
  `UIDevice.batteryState` を確認/`thermalState >= .serious` で停止
- visited セット、フォルダ変更でリセット
- テスト(Core にロジック部を置く): 候補選定順(プレイリスト優先)、visited、ガード条件

### Task 19: 磨き
- 設定: キャッシュ使用量表示+全クリア、ライセンス表示(WhisperKit ほか)
- プレイリスト永続化(前回終了時の内容を復元)、最後に再生していた曲と位置の復元
- エラーの見せ方統一(解析失敗はトランスクリプト欄内に表示、再生は殺さない)
- アクセシビリティ(Dynamic Type でトランスクリプトが破綻しない)、ダークモード確認
- README(スクリーンショット、モデルDLの説明、v1 非対応形式の明記)

---

## リスクと逃げ道

| リスク | 兆候 | 逃げ道 |
|---|---|---|
| WhisperKit がセグメント逐次コールバックを出さない | S1 で判明 | ウィンドウ分割(30s チャンク)で自前ストリーミング化。`Transcriber` プロトコルの裏に隠れるので上位は不変 |
| medium が実機で遅すぎる/発熱 | Task 13 実機確認 | 既定 small のまま、medium に「高性能端末向け」注記。チャンク間に休止を入れる |
| バックグラウンドで解析が止められる | S3 で判明 | 解析はフォアグラウンド限定に縮退。復帰時に途中から再開はせず再実行(キャッシュ粒度はファイル単位のため) |
| iCloud Drive の未DLファイル | S2 で判明 | 列挙時に `ubiquitousItemDownloadingStatus` を見て未DLはグレーアウト+タップでDL開始 |
| m3u8 のパスがサンドボックスで解決不能 | Task 12 | 解決はベストエフォート仕様(スキップ+件数表示)で確定済み。無理に bookmark 化しない |

## 完了の定義 (v1 リリース判定)

- 実機 (iPhone) で: フォルダ登録 → 一覧表示 → 再生 → 文字起こしストリーミング表示 →
  行タップシーク → 曲切替で解析が即座に切り替わる → 2回目に開いた曲は即表示(キャッシュ)
- ロック画面操作・バックグラウンド再生が機能する
- `swift test` + アプリ側テストが全緑
- 30分以上の実ファイルで、無音区間に幻覚定型句が表示されないことを目視確認
