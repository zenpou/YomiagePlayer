# Task 11 スパイク結果: LibVLCSharp.WPF Airspace問題

実施日: 2026-07-08 / 環境: Windows 11, LibVLCSharp.WPF 3.10.0, VideoLAN.LibVLC.Windows 3.0.23.1

## 確認したこと

1. **mp4再生**: `VideoView` + `MediaPlayer` で問題なく映像描画される(tone-1s.mp4をループ再生で確認)
2. **`LibVLCSharp.Shared.Core` と `YomiagePlayer.Core` の名前空間衝突**: `Core.Initialize()` は完全修飾 `LibVLCSharp.Shared.Core.Initialize()` が必要
3. **VideoView.Contentの描画構造**: LibVLCSharp.WPFはContentを**別のフローティングウィンドウ**として映像の上に重ねる実装。PrintWindowによるメインウィンドウ単体のキャプチャにはオーバーレイが写らないことを確認(=別HWNDである証拠)。実画面ではオーバーレイは表示される
4. **D&Dへの影響(推定+構造上の確認)**: 映像領域上のドラッグ&ドロップは、メインウィンドウではなくフローティングウィンドウ側に落ちるため、`Window.Drop` だけでは映像領域上のD&Dを受けられない

## 実装方針への反映

- D&D受付はメインウィンドウ(`Window.AllowDrop`)に加え、**VideoView.Contentのルート要素にも `AllowDrop`+ハンドラを付ける**(両建て)
- 主要なD&D導線としてはプレイリストペイン(映像外)を案内する
- 歌詞パネル・コントロールバーは映像の外side(Gridの別行/列)に置くため影響なし。映像上へのUI重畳はVideoView.Content経由でのみ行う(トランスフォームや透過に制約がある点に注意)
- Task 19の統合テストで映像領域上D&Dを実機確認する
