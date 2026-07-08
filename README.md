# YomiagePlayer

ASMR・話し声・歌(BGM付き)などの音声/動画ファイルを解析し、日本語の歌詞・文章として書き起こして、再生に同期した字幕風表示を行うWindows向けメディアプレイヤーです。

一度解析した内容はファイルの内容ハッシュをキーにキャッシュされるため、同じファイルを再度開いても再解析は行われません(リネーム・フォルダ移動をしても有効です)。

## 主な機能

- 音声/動画再生(LibVLCエンジン、mp3/wav/flac/m4a/ogg/opus/mp4/mkv/avi/webmに対応)、シーク・音量(150%まで増幅可)・リピート・シャッフル
- プレイリスト(ドラッグ&ドロップ並び替え、`.m3u8`保存/読み込み)
- フォルダのライブラリ登録、フォルダを開いての一括プレイリスト投入
- Whisper(Whisper.net / whisper.cpp)による日本語文字起こしをバックグラウンドで自動実行し、解析済み区間から歌詞パネルへ逐次反映
- 歌詞パネル: 再生位置に同期した行ハイライト、行クリックでシーク、解析中/失敗状態の表示、再解析ボタン
- 無音・非音声区間でのハルシネーション対策(「(笑)」「（拍手）」等の定型句・注釈フィルタ、短時間の同文反復除去)
- アイドル時にライブラリ内の未解析ファイルをバックグラウンドで先読み解析(ユーザー操作が常に優先)
- Whisperモデル選択(small / medium / large-v3-turbo)とダウンロード、GPU自動フォールバック(CUDA → Vulkan → CPU)
- サードパーティライセンス表記(設定画面内)

## 動作環境

- Windows
- .NET 10 SDK
- (任意)NVIDIA GPU — CUDAが利用可能ならWhisper解析が高速化されます

## セットアップ

1. .NET 10 SDKをインストール
2. ffmpeg(LGPLビルド)を配置 — 詳細は [`tools/ffmpeg/README.md`](tools/ffmpeg/README.md) を参照
3. ビルド

   ```powershell
   dotnet build
   ```

## 実行

```powershell
dotnet run --project src/YomiagePlayer
```

ビルド済みexeを直接起動することもできます(引数にファイルパスを渡すとそのまま再生開始):

```powershell
src\YomiagePlayer\bin\Debug\net10.0-windows\YomiagePlayer.exe "C:\path\to\file.mp3"
```

初回、設定画面からWhisperモデル(既定: medium)をダウンロードしてください。モデルは `%AppData%\YomiagePlayer\models\` に保存されます。

## テスト

```powershell
dotnet test
```

## プロジェクト構成

```
src/
  YomiagePlayer/       … WPFアプリ本体(UI, ViewModels, Services)
  YomiagePlayer.Core/  … プラットフォーム非依存のコアロジック(再生連携以外)
tests/
  YomiagePlayer.Tests/ … xUnitテスト
tools/ffmpeg/          … ffmpeg実行ファイル配置先(gitignore対象)
docs/
  plans/               … 設計書・実装プラン・スパイク結果
  licenses/            … サードパーティライセンス表記
```

## データ保存先

| データ | 保存先 |
|---|---|
| 文字起こしキャッシュ | `%AppData%\YomiagePlayer\cache\{contentHash}-{model}.json` |
| Whisperモデル | `%AppData%\YomiagePlayer\models\` |
| 一時WAV | `%AppData%\YomiagePlayer\temp\`(起動時に自動掃除) |
| 設定(登録フォルダ・音量・モデル等) | `%AppData%\YomiagePlayer\settings.json` |
| ログ | `%AppData%\YomiagePlayer\logs\` |

## 設計ドキュメント

詳細な設計・アーキテクチャ上の決定は [`docs/plans/`](docs/plans/) 配下を参照してください。

- [設計書](docs/plans/2026-07-08-yomiage-player-design.md)
- [実装プラン](docs/plans/2026-07-08-yomiage-player-implementation.md)
- [統合テスト結果](docs/plans/2026-07-08-integration-checklist.md)

## ライセンス

利用しているサードパーティソフトウェアのライセンス表記は [`docs/licenses/THIRD-PARTY-NOTICES.md`](docs/licenses/THIRD-PARTY-NOTICES.md)、およびアプリ内の設定画面「ライセンス」タブに記載しています。
