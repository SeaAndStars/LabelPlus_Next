/**
 * 类型重新导出（向后兼容）
 *
 * 新的类型定义已移至 types/ 目录，此文件保留用于向后兼容。
 */

export type { Direction } from './types/file'

/** 简单标签项（用于标点转换等简单场景） */
export interface LabelItem {
  id: string
  text: string
}