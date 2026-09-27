# Contributing to Eikura

## Workflow

1. Create a focused branch from main.
2. Keep each change scoped to one concern.
3. Add or update tests when behavior changes.
4. Run repository validation before opening a pull request.
5. Describe user-visible changes and localization impact in the pull request.

Recommended branch prefixes: feat/, fix/, refactor/, docs/, chore/, codex/.

Recommended commit prefixes follow Conventional Commits: feat:, fix:, refactor:, docs:, test:, build:, and chore:.

## Localization / 本地化 / ローカライズ

Supported locales:

- zh-CN — 简体中文
- ja-JP — 日本語
- en-US — English

Every user-facing string must exist in all three Resources.resw files. Do not add user-facing UI strings directly to C# or XAML when a resource can be used.

Run:

    ./scripts/Test-Localization.ps1

CI rejects missing keys, duplicate keys, and empty translations.

## Japanese media conventions

When adding media-recognition behavior:

- preserve Japanese native titles;
- do not treat romaji as a replacement for the native title;
- distinguish TV, Movie, OVA, OAD, ONA, Special, and Extras where source data permits;
- keep release-group and technical tags separate from work titles;
- prefer deterministic parsing and explicit confidence scores over destructive filename rewriting.

## Secrets and signing

Never commit private signing certificates or private keys, OAuth client secrets, access or refresh tokens, local .env files, or store publishing credentials.

## Pull requests

A pull request should state what changed, why it changed, how it was verified, whether localization changed, and whether storage, authentication, playback, metadata, packaging, or Store behavior changed.
