/**
 * 文件服务 — IPC 调用封装
 *
 * 为渲染进程提供文件操作的统一接口。
 * 在 Electron 环境中通过 IPC 调用主进程，
 * 在浏览器环境中提供降级方案。
 */

/** 检测是否在 Electron 环境中运行 */
export function isElectron(): boolean {
  return typeof window !== 'undefined' && !!window.electronAPI
}

/**
 * 打开翻译文件对话框
 *
 * @returns 文件路径，取消时返回 null
 */
export async function openTranslationFile(): Promise<string | null> {
  if (!isElectron()) return null
  return window.electronAPI.openFileDialog([
    { name: '翻译文件', extensions: ['txt'] },
  ])
}

/**
 * 保存翻译文件对话框
 *
 * @param defaultName - 默认文件名
 * @returns 文件路径，取消时返回 null
 */
export async function saveTranslationFile(defaultName?: string): Promise<string | null> {
  if (!isElectron()) return null
  return window.electronAPI.saveFileDialog(defaultName, [
    { name: '翻译文件', extensions: ['txt'] },
  ])
}

/**
 * 选择目录对话框
 *
 * @returns 目录路径，取消时返回 null
 */
export async function selectDirectory(): Promise<string | null> {
  if (!isElectron()) return null
  return window.electronAPI.selectDirectoryDialog()
}

/**
 * 读取文本文件内容
 *
 * @param filePath - 文件路径
 * @returns 文件文本内容
 */
export async function readTextFile(filePath: string): Promise<string> {
  if (!isElectron()) throw new Error('文件操作仅在 Electron 环境中可用')
  return window.electronAPI.readTextFile(filePath)
}

/**
 * 写入文本文件
 *
 * @param filePath - 文件路径
 * @param content - 文本内容
 */
export async function writeTextFile(filePath: string, content: string): Promise<void> {
  if (!isElectron()) throw new Error('文件操作仅在 Electron 环境中可用')
  return window.electronAPI.writeTextFile(filePath, content)
}

/**
 * 在文件管理器中打开目录
 *
 * @param dirPath - 目录路径
 */
export async function openInExplorer(dirPath: string): Promise<void> {
  if (!isElectron()) return
  return window.electronAPI.openInExplorer(dirPath)
}

/**
 * 获取文件所在目录
 *
 * @param filePath - 文件路径
 * @returns 目录路径
 */
export async function getDirname(filePath: string): Promise<string> {
  if (!isElectron()) throw new Error('路径操作仅在 Electron 环境中可用')
  return window.electronAPI.getDirname(filePath)
}

/**
 * 拼接路径
 *
 * @param parts - 路径片段
 * @returns 拼接后的路径
 */
export async function joinPath(...parts: string[]): Promise<string> {
  if (!isElectron()) return parts.join('/')
  return window.electronAPI.joinPath(...parts)
}
