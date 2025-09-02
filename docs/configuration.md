# 配置说明

## 更新源（AppSettings）

文件：`LabelPlus_Next/Models/AppSettings.cs`

- Update.BaseUrl：默认 `https://alist.seastarss.cn`
- Update.ManifestPath：默认 `/OneDrive2/Update/manifest.json`
- Username/Password：如更新源要求鉴权可设置。

> 主程序在需要时会读取该配置来检查更新。

{% include back-home.md %}
