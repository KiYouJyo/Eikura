# Localization

Eikura treats localization as a build-time invariant.

## Supported locales

| Locale | Language | Product name |
| --- | --- | --- |
| zh-CN | 简体中文 | Eikura |
| ja-JP | 日本語 | Eikura |
| en-US | English | Eikura |

Resource locations:

    src/Eizo.App/Strings/zh-CN/Resources.resw
    src/Eizo.App/Strings/ja-JP/Resources.resw
    src/Eizo.App/Strings/en-US/Resources.resw

All three files must contain exactly the same keys.

## 简体中文

- 使用自然、简洁的软件界面中文。
- 日本作品的本地化标题与日文原名是不同字段，不互相覆盖。
- “日本ドラマ”在中文 UI 中统一写作“日剧”。
- WebDAV、OneDrive、ASS、HDR 等技术名称保留标准写法。

## 日本語

- UI は自然な日本語を優先し、中国語の語順を直訳しない。
- 日本作品のネイティブタイトルを保持する。
- 一般的な UI 用語は Windows / 日本語ソフトウェアで自然な表現を使う。
- 技術固有名詞は必要以上にカタカナ化しない。

## English

- Use concise sentence case for UI labels.
- Preserve native Japanese titles as media data; do not replace them with romanization.
- Prefer established media terms such as episode, season, special, cast, staff, and subtitle track.

## Resource keys

Use semantic groups: App_, Nav_, Section_, Common_, Media_, Anime_, Playback_, Source_, Settings_, Status_, Error_, Search_, and Library_.

Run ./scripts/Test-Localization.ps1 before submitting localization changes. CI rejects missing files, duplicate keys, empty values, and mismatched key sets.
