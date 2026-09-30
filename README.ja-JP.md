[简体中文](README.md) | 日本語 | [English](README.en-US.md)

# Eikura

Windows 向けのネイティブな個人メディアライブラリです。日本のアニメとドラマを優先しつつ、映画・TVシリーズ・その他の個人メディアにも対応し、認識、メタデータ、再生、WebDAV、キャッシュ、視聴管理を一体化します。

<a href="https://get.microsoft.com/installer/download/9N1PTDCCND8S?referrer=appbadge"><img src="https://get.microsoft.com/images/ja%20dark.svg" width="240" alt="Microsoft Store から Eikura を入手"></a>

[![GitHub Release](https://img.shields.io/github/v/release/KiYouJyo/Eikura?display_name=tag&sort=semver&color=2F81F7&label=Release)](https://github.com/KiYouJyo/Eikura/releases/latest)
[![CI](https://github.com/KiYouJyo/Eikura/actions/workflows/repository-validation.yml/badge.svg?branch=main)](https://github.com/KiYouJyo/Eikura/actions/workflows/repository-validation.yml)
[![Windows](https://img.shields.io/badge/Windows-WinUI%203-0078D4?logo=windows&logoColor=white)](https://github.com/KiYouJyo/Eikura)
[![Architecture](https://img.shields.io/badge/Architecture-x64-005A9E)](#システム要件)
[![Languages](https://img.shields.io/badge/Languages-%E4%B8%AD%E6%96%87%20%7C%20%E6%97%A5%E6%9C%AC%E8%AA%9E%20%7C%20English-6F42C1)](#言語)
[![Local First](https://img.shields.io/badge/Design-Local--first-2EA043)](#プライバシーと通信)

## 入手

- [Microsoft Store（推奨）](https://apps.microsoft.com/detail/9N1PTDCCND8S)：ストアがインストールと更新を管理し、発行元証明書の手動設定は不要です。
- [GitHub サイドロード版](https://github.com/KiYouJyo/Eikura/releases/latest)：ワンクリック導入版、署名済み MSIXBundle、チェックサムを提供します。
- [プロジェクトサイト](https://kiyoujyo.github.io/Eikura/)：現在のバージョン、機能、ダウンロード、サポート情報を確認できます。
- 上級者向けに署名済み `.msixbundle` と `SHA256SUMS.txt` も Release Assets で提供します。

## 主な機能

- **ネイティブメディアライブラリ**：ローカル / WebDAV ソース、スキャン、分類、作品・シーズン・話数の集約。
- **日本メディア優先の認識**：シーズン、話数、OVA/OAD/ONA/SP、リリースグループ、技術タグを扱い、原題を保持。
- **実データのメタデータ**：TMDB、Bangumi などからポスター、背景、概要、シーズン/話数、キャスト・スタッフを取得。
- **再生**：LibVLC バックエンド、再生位置、シーク、倍速、全画面、キュー、一般的な動画形式。
- **字幕**：外部 ASS/SSA/SRT、内蔵字幕の統一表示、二重字幕、優先言語。
- **キャッシュ / ダウンロード**：リモート動画の進捗表示、一時停止、再開、オフライン再生。
- **Bangumi**：放送カレンダー、発見、コレクション/追番、アカウント関連機能。
- **Windows ネイティブ UI**：WinUI 3、Mica、ライト/ダークテーマ、3言語、高 DPI アセット。

## インストールと更新

### Microsoft Store からのインストール（推奨）

[Microsoft Store の商品ページ](https://apps.microsoft.com/detail/9N1PTDCCND8S) から Eikura をインストールできます。ストア版と GitHub 版は別々の更新チャネルを使用します。ストア版 1.3.4 は審査へ提出済みです。入手可能なバージョンはストアをご確認ください。

### GitHub サイドロード版のインストール

[最新 Release](https://github.com/KiYouJyo/Eikura/releases/latest) からワンクリック導入パッケージを取得し、展開後に同梱手順に従ってください。

### 更新

Microsoft Store 版はストアから更新でき、1.3.4 以降はアプリの「About」からもストア更新を確認・インストールできます。GitHub サイドロード版は GitHub Releases から更新を取得し、整合性と発行元署名を検証してインストールします。両チャネルともアプリ全体の更新のみを提供し、Metadata はアプリと一緒に配布します。

## プライバシーと通信

Eikura はローカル優先です。ライブラリ索引、設定、再生履歴は基本的に端末内へ保存され、ローカル動画を Eikura 独自サーバーへアップロードしません。メタデータ、Bangumi、更新確認、ユーザー設定済み WebDAV などを利用する場合のみ、対応する第三者サービスやメディアソースへ通信します。

詳細は [プライバシー](PRIVACY.md) を参照してください。

## システム要件

- Windows 10 2004 以降 / Windows 11
- x64
- Windows App SDK Runtime

## 言語

简体中文、日本語、English に対応し、3言語のリソースキーを一致させています。

## ドキュメント

- [プロジェクトサイト](https://kiyoujyo.github.io/Eikura/)
- [ロードマップ](docs/ROADMAP.md)
- [アーキテクチャ](docs/ARCHITECTURE.md)
- [再生統合](docs/PLAYBACK_INTEGRATION.md)
- [ローカライズ](docs/LOCALIZATION.md)
- [UI ガイドライン](docs/UI_GUIDELINES.md)
- [リリース手順](docs/RELEASE.md)
- [変更履歴](CHANGELOG.md)
- [プライバシー](PRIVACY.md) · [コントリビューション](CONTRIBUTING.md) · [セキュリティ](SECURITY.md)

## 開発

```powershell
dotnet restore Eikura.slnx
dotnet test Eikura.slnx -c Debug
```

## フィードバック

[GitHub Issues](https://github.com/KiYouJyo/Eikura/issues) または [サポートページ](https://kiyoujyo.github.io/Eikura/support/) を利用してください。

---

Eikura
