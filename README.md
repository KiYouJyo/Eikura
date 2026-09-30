简体中文 | [日本語](README.ja-JP.md) | [English](README.en-US.md)

# Eikura

面向 Windows 的原生个人影音库。优先优化日本动漫与日剧，同时兼容电影、剧集和其他个人媒体；围绕媒体识别、元数据、播放、WebDAV、缓存与追番体验构建。

[![GitHub Release](https://img.shields.io/github/v/release/KiYouJyo/Eikura?display_name=tag&sort=semver&color=2F81F7&label=Release)](https://github.com/KiYouJyo/Eikura/releases/latest)
[![CI](https://github.com/KiYouJyo/Eikura/actions/workflows/repository-validation.yml/badge.svg?branch=main)](https://github.com/KiYouJyo/Eikura/actions/workflows/repository-validation.yml)
[![Windows](https://img.shields.io/badge/Windows-WinUI%203-0078D4?logo=windows&logoColor=white)](https://github.com/KiYouJyo/Eikura)
[![Architecture](https://img.shields.io/badge/Architecture-x64-005A9E)](#系统要求)
[![Languages](https://img.shields.io/badge/Languages-%E4%B8%AD%E6%96%87%20%7C%20%E6%97%A5%E6%9C%AC%E8%AA%9E%20%7C%20English-6F42C1)](#语言)
[![Local First](https://img.shields.io/badge/Design-Local--first-2EA043)](#隐私与联网)

## 获取应用

- [GitHub 最新正式 Release](https://github.com/KiYouJyo/Eikura/releases/latest)：推荐下载 `Eikura-v*-x64-one-click.zip` 进行首次安装。
- [项目主页](https://kiyoujyo.github.io/Eikura/)：查看当前版本、功能概览、下载入口与支持信息。
- 高级用户可在 Release Assets 中直接下载签名的 `.msixbundle` 与 `SHA256SUMS.txt` 进行手动部署和校验。

## 核心功能

- **原生媒体库**：本地与 WebDAV 媒体来源、扫描、分类、作品/季度/剧集聚合。
- **日本媒体优先识别**：处理季、集、OVA/OAD/ONA/SP、发布组和常见技术标签，并保留原生标题。
- **真实元数据**：以 TMDB、Bangumi 等数据源补全海报、背景图、简介、季集信息、演职人员与作品信息。
- **播放体验**：LibVLC 后端，进度记忆、拖动、倍速、全屏、播放队列及常见视频格式。
- **字幕**：外挂 ASS/SSA/SRT，内封字幕统一渲染，并支持双字幕与首选语言。
- **缓存与下载**：远程视频可加入缓存页，查看下载进度、暂停、继续与离线播放。
- **Bangumi**：放送日历、发现、追番/收藏及账户相关能力。
- **Windows 原生体验**：WinUI 3、Mica、浅色/深色主题、三语界面与高 DPI 视觉资产。

## 安装与更新

### 首次 GitHub 安装

从 [最新 Release](https://github.com/KiYouJyo/Eikura/releases/latest) 下载一键安装包，解压后按包内说明安装。它包含所需安装脚本与发布证书处理流程。

### 后续更新

应用内的“关于”页可检查 GitHub Release 更新。更新包会进行完整性与签名校验后再进入安装流程。

### 手动校验

Release 同时提供签名的 MSIXBundle 与 `SHA256SUMS.txt`。需要手工部署、归档或验证时可直接使用这些文件。

## 隐私与联网

Eikura 采用本地优先设计。媒体库索引、设置和播放历史默认保存在本机，不会把本地视频上传到 Eikura 自有服务器。使用元数据、Bangumi、更新检查或用户配置的 WebDAV 等联网功能时，应用会访问对应第三方服务或媒体来源。

详见 [隐私说明](PRIVACY.md)。

## 系统要求

- Windows 10 2004 或更高版本 / Windows 11
- x64
- Windows App SDK Runtime（安装流程会按需要处理运行环境）

## 语言

支持简体中文、日本語与 English，三套资源键保持一致。

## 文档

- [项目主页](https://kiyoujyo.github.io/Eikura/)
- [路线图](docs/ROADMAP.md)
- [架构](docs/ARCHITECTURE.md)
- [播放集成](docs/PLAYBACK_INTEGRATION.md)
- [本地化规范](docs/LOCALIZATION.md)
- [UI 规范](docs/UI_GUIDELINES.md)
- [发布流程](docs/RELEASE.md)
- [更改日志](CHANGELOG.md)
- [隐私说明](PRIVACY.md) · [贡献指南](CONTRIBUTING.md) · [安全政策](SECURITY.md)

## 开发与构建

```powershell
dotnet restore Eikura.slnx
dotnet test Eikura.slnx -c Debug
```

WinUI 3 x64 Release 构建、签名、MSIXBundle 与 GitHub Release 发布流程见 [docs/RELEASE.md](docs/RELEASE.md)。

## 问题反馈

请通过 [GitHub Issues](https://github.com/KiYouJyo/Eikura/issues) 反馈问题，或访问[支持页面](https://kiyoujyo.github.io/Eikura/support/)。

---

Eikura
