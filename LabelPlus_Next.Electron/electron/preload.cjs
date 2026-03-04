/**
 * Electron 预加载脚本
 *
 * 通过 contextBridge 安全地向渲染进程暴露 Electron API。
 * 渲染进程通过 window.electronAPI 调用这些方法。
 */
const { contextBridge, ipcRenderer } = require('electron')

contextBridge.exposeInMainWorld('electronAPI', {
  /** 当前操作系统平台 */
  platform: process.platform,

  /* ---- 文件对话框 ---- */

  /** 打开文件对话框 */
  openFileDialog: (filters) =>
    ipcRenderer.invoke('dialog:openFile', filters),

  /** 保存文件对话框 */
  saveFileDialog: (defaultName, filters) =>
    ipcRenderer.invoke('dialog:saveFile', defaultName, filters),

  /** 选择目录对话框 */
  selectDirectoryDialog: () =>
    ipcRenderer.invoke('dialog:selectDirectory'),

  /* ---- 文件读写 ---- */

  /** 读取文本文件 */
  readTextFile: (filePath) =>
    ipcRenderer.invoke('fs:readTextFile', filePath),

  /** 写入文本文件 */
  writeTextFile: (filePath, content) =>
    ipcRenderer.invoke('fs:writeTextFile', filePath, content),

  /** 列出目录中的图片文件 */
  listImages: (dirPath) =>
    ipcRenderer.invoke('fs:listImages', dirPath),

  /** 读取图片为 Base64 Data URL */
  readImageAsDataUrl: (filePath) =>
    ipcRenderer.invoke('fs:readImageAsDataUrl', filePath),

  /* ---- 系统操作 ---- */

  /** 在文件管理器中打开目录 */
  openInExplorer: (dirPath) =>
    ipcRenderer.invoke('shell:openInExplorer', dirPath),

  /* ---- 路径工具 ---- */

  /** 获取文件所在目录 */
  getDirname: (filePath) =>
    ipcRenderer.invoke('path:dirname', filePath),

  /** 拼接路径 */
  joinPath: (...parts) =>
    ipcRenderer.invoke('path:join', ...parts),
})