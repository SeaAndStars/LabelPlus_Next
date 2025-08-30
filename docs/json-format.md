# JSON 清单格式参考

## /project.json（聚合清单）

- 结构：

```json
{
  "projects": {
    "项目显示名": "/{项目名}/{项目名}_project.json"
  }
}
```

- 特性：使用 System.Text.Json UnsafeRelaxedJsonEscaping 编码，中文不转义、缩进美观。

## {项目名}_project.json（项目 JSON）

- 结构示例：

```json
{
  "items": {
    "01": { "status": "", "sourcePath": "/项目/01/a.7z", "translatePath": "/项目/01/b.txt", "typesetPath": null },
    "番外": { "status": "", "sourcePath": null, "translatePath": "/项目/番外/c.txt", "typesetPath": null },
    "卷01": { "status": "", "sourcePath": "/项目/卷01/d.7z", "translatePath": null, "typesetPath": null }
  }
}
```

- 键规则：见《parsing.md》与《project-structure.md》；排序遵循 RankKey 保证稳定输出。

{% include back-home.md %}
