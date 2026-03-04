/**
 * Electron 主进程入口
 *
 * 负责创建应用窗口、注册 IPC 处理函数，
 * 为渲染进程提供文件系统和对话框访问能力。
 * 使用 Electron Forge + Vite 插件管理构建流程。
 */

import { app, BrowserWindow, ipcMain, dialog, shell } from 'electron'
import path from 'node:path'
import fs from 'node:fs'

/* ---- Forge Vite 插件注入的全局常量 ---- */
declare const MAIN_WINDOW_VITE_DEV_SERVER_URL: string
declare const MAIN_WINDOW_VITE_NAME: string

/** 支持的图片扩展名集合 */
const IMAGE_EXTENSIONS = new Set([
  '.png', '.jpg', '.jpeg', '.bmp', '.gif', '.webp', '.tiff', '.tif'
])

/** 扩展名到 MIME 类型映射表 */
const MIME_MAP: Record<string, string> = {
  '.png': 'image/png',
  '.jpg': 'image/jpeg',
  '.jpeg': 'image/jpeg',
  '.bmp': 'image/bmp',
  '.gif': 'image/gif',
  '.webp': 'image/webp',
  '.tiff': 'image/tiff',
  '.tif': 'image/tiff',
}

/**
 * 创建主窗口
 *
 * 开发模式下加载 Vite 开发服务器 URL，
 * 生产模式下加载打包后的 HTML 文件。
 */
function createWindow(): void {
  const mainWindow = new BrowserWindow({
    width: 1300,
    height: 800,
    webPreferences: {
      preload: path.join(__dirname, 'preload.js'),
      contextIsolation: true,
      nodeIntegration: false,
    },
  })

  if (MAIN_WINDOW_VITE_DEV_SERVER_URL) {
    mainWindow.loadURL(MAIN_WINDOW_VITE_DEV_SERVER_URL)
  } else {
    mainWindow.loadFile(
      path.join(__dirname, `../renderer/${MAIN_WINDOW_VITE_NAME}/index.html`)
    )
  }
}

/* ========== IPC 处理函数注册 ========== */

/** 打开文件对话框 */
ipcMain.handle('dialog:openFile', async (_event, filters) => {
  const result = await dialog.showOpenDialog({
    properties: ['openFile'],
    filters: filters || [{ name: '翻译文件', extensions: ['txt'] }],
  })
  return result.canceled ? null : result.filePaths[0]
})

/** 保存文件对话框 */
ipcMain.handle('dialog:saveFile', async (_event, defaultName, filters) => {
  const result = await dialog.showSaveDialog({
    defaultPath: defaultName,
    filters: filters || [{ name: '翻译文件', extensions: ['txt'] }],
  })
  return result.canceled ? null : result.filePath
})

/** 选择目录对话框 */
ipcMain.handle('dialog:selectDirectory', async () => {
  const result = await dialog.showOpenDialog({
    properties: ['openDirectory'],
  })
  return result.canceled ? null : result.filePaths[0]
})

/** 读取文本文件 */
ipcMain.handle('fs:readTextFile', async (_event, filePath: string) => {
  return fs.promises.readFile(filePath, 'utf-8')
})

/** 写入文本文件 */
ipcMain.handle('fs:writeTextFile', async (_event, filePath: string, content: string) => {
  const dir = path.dirname(filePath)
  await fs.promises.mkdir(dir, { recursive: true })
  await fs.promises.writeFile(filePath, content, 'utf-8')
})

/** 列出目录中的图片文件（目录不存在时返回空数组） */
ipcMain.handle('fs:listImages', async (_event, dirPath: string) => {
  try {
    const entries = await fs.promises.readdir(dirPath)
    return entries
      .filter((name: string) => IMAGE_EXTENSIONS.has(path.extname(name).toLowerCase()))
      .sort()
  } catch (err: unknown) {
    const code = (err as NodeJS.ErrnoException).code
    if (code === 'ENOENT') {
      console.warn(`[图片列表] 目录不存在: ${dirPath}`)
      return []
    }
    throw err
  }
})

/** 读取图片为 Base64 Data URL（文件不存在时返回 null） */
ipcMain.handle('fs:readImageAsDataUrl', async (_event, filePath: string) => {
  try {
    const buffer = await fs.promises.readFile(filePath)
    const ext = path.extname(filePath).toLowerCase()
    const mime = MIME_MAP[ext] || 'image/png'
    return `data:${mime};base64,${buffer.toString('base64')}`
  } catch (err: unknown) {
    const code = (err as NodeJS.ErrnoException).code
    if (code === 'ENOENT') {
      console.warn(`[图片加载] 文件不存在: ${filePath}`)
      return null
    }
    throw err
  }
})

/** 在文件管理器中打开目录 */
ipcMain.handle('shell:openInExplorer', async (_event, dirPath: string) => {
  await shell.openPath(dirPath)
})

/** 获取文件所在目录 */
ipcMain.handle('path:dirname', (_event, filePath: string) => {
  return path.dirname(filePath)
})

/** 拼接路径 */
ipcMain.handle('path:join', (_event, ...parts: string[]) => {
  return path.join(...parts)
})

/* ========== 应用生命周期 ========== */

app.whenReady().then(() => {
  createWindow()

  /* macOS: 点击 Dock 图标重新创建窗口 */
  app.on('activate', () => {
    if (BrowserWindow.getAllWindows().length === 0) {
      createWindow()
    }
  })
})

/* 所有窗口关闭后退出应用（macOS 除外） */
app.on('window-all-closed', () => {
  if (process.platform !== 'darwin') {
    app.quit()
  }
})
