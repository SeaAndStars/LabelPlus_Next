# 性能与大文件上传

## 设计要点

- 流式直传：大文件通过本地路径以 FileStream 直接上传，避免整段读入内存。
- 连接复用：共享 HttpClient（SocketsHttpHandler），禁用 Expect: 100-Continue。
- 超时无限：PUT/GET 均设为无限超时，避免 100 秒取消。
- 进度与速度：进度回调计算 MB/s，UI 实时显示。

## 参数

- PutMany 默认并发 6（调用端可调整）。
- 上传缓冲区 256KB；可按网络情况在源码侧调整。

## 实践建议

- 将 .txt 与超大源文件分开批次上传可提升 UI 响应。
- 并发不是越大越好；6~10 之间通常较稳。

{% include back-home.md %}
