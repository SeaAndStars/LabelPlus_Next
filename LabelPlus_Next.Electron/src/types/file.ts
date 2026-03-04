/**
 * 文件相关类型定义
 *
 * 定义标注文件（.txt）的内存数据结构，
 * 与 Avalonia 端 Models/LabelFileManager.cs 对应。
 */

import type { LabelItem } from './label'

/**
 * 标注文件数据结构
 *
 * 对应 Avalonia LabelFileManager 的运行时状态：
 * - fileHead：文件头版本信息 [版本号, 标志位]
 * - groupList：分组名称列表（用户自定义的分类分组）
 * - comment：文件备注
 * - store：图片名 → 标签列表的映射
 */
export interface LabelFileData {
  /** 文件头信息 [版本号, 标志位]，默认 ["1", "0"] */
  fileHead: [string, string]
  /** 分组名称列表 */
  groupList: string[]
  /** 文件备注 */
  comment: string
  /**
   * 标签存储：图片文件名 → 标签数组
   *
   * 键为图片文件名（如 "page001.png"），
   * 值为该图片上的全部标签。
   */
  store: Record<string, LabelItem[]>
}

/**
 * 创建空白标注文件数据
 */
export function createEmptyLabelFile(): LabelFileData {
  return {
    fileHead: ['1', '0'],
    groupList: [],
    comment: '',
    store: {},
  }
}

/**
 * 标点转换方向（复用已有类型）
 */
export type Direction = 'enToZh' | 'zhToEn'
