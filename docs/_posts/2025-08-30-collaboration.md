---
layout: post
title: 协作流程：自动保存、远程同步与冲突合并
author: SeaAndStars
categories: [文档]
tags: [协作, 冲突, 合并, 备份]
---

讲解 TranslateViewModel 的协作闭环：快照、哈希比对、SafePut 备份、冲突合并助手与回退策略。

阅读完整文档：/collaboration.html

<!--more-->

上传与下载均为流式处理，带 MB/s 进度与重试。

{% include back-home.md %}
