"use strict";
const electron = require("electron");
electron.contextBridge.exposeInMainWorld("electronAPI", {
  /** 当前操作系统平台 */
  platform: process.platform,
  /* ---- 文件对话框 ---- */
  /** 打开文件对话框 */
  openFileDialog: (filters) => electron.ipcRenderer.invoke("dialog:openFile", filters),
  /** 保存文件对话框 */
  saveFileDialog: (defaultName, filters) => electron.ipcRenderer.invoke("dialog:saveFile", defaultName, filters),
  /** 选择目录对话框 */
  selectDirectoryDialog: () => electron.ipcRenderer.invoke("dialog:selectDirectory"),
  /* ---- 文件读写 ---- */
  /** 读取文本文件 */
  readTextFile: (filePath) => electron.ipcRenderer.invoke("fs:readTextFile", filePath),
  /** 写入文本文件 */
  writeTextFile: (filePath, content) => electron.ipcRenderer.invoke("fs:writeTextFile", filePath, content),
  /** 列出目录中的图片文件 */
  listImages: (dirPath) => electron.ipcRenderer.invoke("fs:listImages", dirPath),
  /** 读取图片为 Base64 Data URL */
  readImageAsDataUrl: (filePath) => electron.ipcRenderer.invoke("fs:readImageAsDataUrl", filePath),
  /* ---- 系统操作 ---- */
  /** 在文件管理器中打开目录 */
  openInExplorer: (dirPath) => electron.ipcRenderer.invoke("shell:openInExplorer", dirPath),
  /* ---- 路径工具 ---- */
  /** 获取文件所在目录 */
  getDirname: (filePath) => electron.ipcRenderer.invoke("path:dirname", filePath),
  /** 拼接路径 */
  joinPath: (...parts) => electron.ipcRenderer.invoke("path:join", ...parts)
});
