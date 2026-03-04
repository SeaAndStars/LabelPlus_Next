/**
 * Electron IPC API 类型声明
 *
 * 通过 preload.ts 的 contextBridge.exposeInMainWorld 暴露到渲染进程，
 * 挂载在 window.electronAPI 上。
 */

/** 文件对话框过滤器 */
export interface FileFilter {
  name: string
  extensions: string[]
}

/** Electron API — 渲染进程可调用的接口 */
export interface ElectronAPI {
  /** 获取当前平台标识 */
  platform: string

  /**
   * 打开文件对话框
   * @param filters - 文件类型过滤器
   * @returns 选中的文件路径，取消时返回 null
   */
  openFileDialog: (filters?: FileFilter[]) => Promise<string | null>

  /**
   * 保存文件对话框
   * @param defaultName - 默认文件名
   * @param filters - 文件类型过滤器
   * @returns 选择的保存路径，取消时返回 null
   */
  saveFileDialog: (defaultName?: string, filters?: FileFilter[]) => Promise<string | null>

  /**
   * 选择目录对话框
   * @returns 选中的目录路径，取消时返回 null
   */
  selectDirectoryDialog: () => Promise<string | null>

  /**
   * 读取文本文件内容
   * @param filePath - 文件绝对路径
   * @returns 文件文本内容
   */
  readTextFile: (filePath: string) => Promise<string>

  /**
   * 写入文本文件
   * @param filePath - 文件绝对路径
   * @param content - 文本内容
   */
  writeTextFile: (filePath: string, content: string) => Promise<void>

  /**
   * 列出目录中的图片文件
   * @param dirPath - 目录路径
   * @returns 图片文件名数组（仅文件名，不含路径）
   */
  listImages: (dirPath: string) => Promise<string[]>

  /**
   * 读取图片文件为 Base64 数据 URL
   * @param filePath - 图片文件绝对路径
   * @returns data:image/xxx;base64,... 格式的字符串
   */
  readImageAsDataUrl: (filePath: string) => Promise<string>

  /**
   * 在系统文件管理器中打开指定目录
   * @param dirPath - 目录路径
   */
  openInExplorer: (dirPath: string) => Promise<void>

  /**
   * 获取文件所在目录路径
   * @param filePath - 文件路径
   * @returns 目录路径
   */
  getDirname: (filePath: string) => Promise<string>

  /**
   * 拼接路径
   * @param parts - 路径片段
   * @returns 拼接后的路径
   */
  joinPath: (...parts: string[]) => Promise<string>
}

/* 扩展全局 Window 接口 */
declare global {
  interface Window {
    electronAPI: ElectronAPI
  }
}

export {}
