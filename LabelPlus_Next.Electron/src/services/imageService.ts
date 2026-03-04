/**
 * 图片服务 — 图片加载与缓存
 *
 * 负责通过 IPC 读取图片并缓存，减少重复的文件 I/O 开销。
 */

import { isElectron } from './fileService'

/** 图片缓存（Data URL 格式） */
const imageCache = new Map<string, string>()

/** 缓存容量上限（超过后清除最早的条目） */
const MAX_CACHE_SIZE = 50

/**
 * 加载图片为 Data URL
 *
 * 优先从缓存读取，缓存未命中时通过 IPC 从磁盘读取。
 *
 * @param filePath - 图片文件绝对路径
 * @returns data:image/xxx;base64,... 格式的字符串
 */
export async function loadImage(filePath: string): Promise<string> {
  /* 检查缓存 */
  const cached = imageCache.get(filePath)
  if (cached) return cached

  if (!isElectron()) {
    throw new Error('图片加载仅在 Electron 环境中可用')
  }

  /* 通过 IPC 读取（文件不存在时返回空字符串） */
  const dataUrl = await window.electronAPI.readImageAsDataUrl(filePath)
  if (!dataUrl) return ''

  /* 写入缓存（LRU 简化版：超过上限时清除最早条目） */
  if (imageCache.size >= MAX_CACHE_SIZE) {
    const firstKey = imageCache.keys().next().value
    if (firstKey !== undefined) {
      imageCache.delete(firstKey)
    }
  }
  imageCache.set(filePath, dataUrl)

  return dataUrl
}

/**
 * 列出目录中的图片文件
 *
 * @param dirPath - 目录路径
 * @returns 图片文件名数组（已排序）
 */
export async function listImageFiles(dirPath: string): Promise<string[]> {
  if (!isElectron()) return []
  return window.electronAPI.listImages(dirPath)
}

/**
 * 清除图片缓存
 */
export function clearImageCache(): void {
  imageCache.clear()
}
