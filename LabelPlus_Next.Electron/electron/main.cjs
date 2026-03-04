/**
 * Electron 主进程
 *
 * 负责窗口管理和 IPC 通信，为渲染进程提供文件系统访问能力。
 */
const { app, BrowserWindow, ipcMain, dialog, shell } = require('electron')
const path = require('node:path')
const fs = require('node:fs')

/** 支持的图片扩展名 */
const IMAGE_EXTENSIONS = new Set(['.png', '.jpg', '.jpeg', '.bmp', '.gif', '.webp', '.tiff', '.tif'])

/** 扩展名到 MIME 类型映射 */
const MIME_MAP = {
  '.png': 'image/png',
  '.jpg': 'image/jpeg',
  '.jpeg': 'image/jpeg',
  '.bmp': 'image/bmp',
  '.gif': 'image/gif',
  '.webp': 'image/webp',
  '.tiff': 'image/tiff',
  '.tif': 'image/tiff',
}

function createWindow() {
  const mainWindow = new BrowserWindow({
    width: 1300,
    height: 800,
    webPreferences: {
      preload: path.join(__dirname, 'preload.cjs'),
      contextIsolation: true,
      nodeIntegration: false
    }
  })

  const devUrl = process.env.VITE_DEV_SERVER_URL || 'http://localhost:5173'
  if (!app.isPackaged) {
    mainWindow.loadURL(devUrl)
    return
  }

  mainWindow.loadFile(path.join(__dirname, '..', 'dist', 'index.html'))
}

/* ========== IPC 处理函数注册 ========== */

/** 打开文件对话框 */
ipcMain.handle('dialog:openFile', async (_event, filters) => {
  const result = await dialog.showOpenDialog({
    properties: ['openFile'],
    filters: filters || [{ name: '翻译文件', extensions: ['txt'] }]
  })
  return result.canceled ? null : result.filePaths[0]
})

/** 保存文件对话框 */
ipcMain.handle('dialog:saveFile', async (_event, defaultName, filters) => {
  const result = await dialog.showSaveDialog({
    defaultPath: defaultName,
    filters: filters || [{ name: '翻译文件', extensions: ['txt'] }]
  })
  return result.canceled ? null : result.filePath
})

/** 选择目录对话框 */
ipcMain.handle('dialog:selectDirectory', async () => {
  const result = await dialog.showOpenDialog({
    properties: ['openDirectory']
  })
  return result.canceled ? null : result.filePaths[0]
})

/** 读取文本文件 */
ipcMain.handle('fs:readTextFile', async (_event, filePath) => {
  return fs.promises.readFile(filePath, 'utf-8')
})

/** 写入文本文件 */
ipcMain.handle('fs:writeTextFile', async (_event, filePath, content) => {
  const dir = path.dirname(filePath)
  await fs.promises.mkdir(dir, { recursive: true })
  await fs.promises.writeFile(filePath, content, 'utf-8')
})

/** 列出目录中的图片文件 */
ipcMain.handle('fs:listImages', async (_event, dirPath) => {
  const entries = await fs.promises.readdir(dirPath)
  return entries
    .filter(name => IMAGE_EXTENSIONS.has(path.extname(name).toLowerCase()))
    .sort()
})

/** 读取图片为 Base64 Data URL */
ipcMain.handle('fs:readImageAsDataUrl', async (_event, filePath) => {
  const buffer = await fs.promises.readFile(filePath)
  const ext = path.extname(filePath).toLowerCase()
  const mime = MIME_MAP[ext] || 'image/png'
  return `data:${mime};base64,${buffer.toString('base64')}`
})

/** 在文件管理器中打开目录 */
ipcMain.handle('shell:openInExplorer', async (_event, dirPath) => {
  await shell.openPath(dirPath)
})

/** 获取文件所在目录 */
ipcMain.handle('path:dirname', (_event, filePath) => {
  return path.dirname(filePath)
})

/** 拼接路径 */
ipcMain.handle('path:join', (_event, ...parts) => {
  return path.join(...parts)
})

/* ========== 应用生命周期 ========== */

app.whenReady().then(() => {
  createWindow()
  app.on('activate', () => {
    if (BrowserWindow.getAllWindows().length === 0) {
      createWindow()
    }
  })
})

app.on('window-all-closed', () => {
  if (process.platform !== 'darwin') {
    app.quit()
  }
})