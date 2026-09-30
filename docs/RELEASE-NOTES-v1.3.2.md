# Eikura v1.3.2

Eikura 1.3.2 完成产品品牌迁移到 **Eikura**。

## 本次更新
- 应用标题、关于页面、三语言资源与用户可见提示统一使用 Eikura。
- 项目主页、隐私/支持页面、README 与品牌规范切换到 Eikura。
- GitHub 一键安装包改用 `Eikura-v1.3.2-x64-one-click.zip`；为保证 Eizo 1.3.1 内置更新器能够无缝发现本次改名版本，1.3.2 的唯一 MSIXBundle 技术资产名暂时保留为 `Eizo_1.3.2.0_x64.msixbundle`。应用安装后的产品名称仍为 Eikura。
- 保留旧 MSIX Identity、本地数据路径等兼容标识，并通过真实签名包升级测试验证 1.3.1 可原地升级，媒体库、设置与播放历史不会因改名丢失。
- Bangumi OAuth 新增 `eikura://` 主回调协议，并继续兼容 `eizo://`；应用优先探测新的 Eikura Worker，未启用时自动回退旧 Worker。

## 兼容说明
`Eizo_1.3.2.0_x64.msixbundle`、MSIX Identity、旧数据路径及少量运行时标识仅作为 1.3.2 迁移兼容层保留，不代表当前产品名称。自后续版本起，新的发布资产继续使用 Eikura 命名。
