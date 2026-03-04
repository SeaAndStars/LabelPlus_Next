/**
 * 标注文件读取器
 *
 * 解析 LabelPlus .txt 标注文件格式。
 * 与 Avalonia 端 Models/LabelFileReader.cs 逻辑完全对应。
 *
 * 文件格式结构：
 *   文件头（版本,标志）
 *   -
 *   分组列表（每行一个）
 *   -
 *   备注
 *
 *   >>>>>>>>[图片文件名]<<<<<<<<
 *   ----------------[编号]----------------[x,y,分类]
 *   翻译文本（可多行）
 *   ...
 */

import type { LabelItem } from '../types/label'
import { CATEGORY_LABELS } from '../types/label'
import type { LabelFileData } from '../types/file'

/**
 * 解析文件头部分
 *
 * 文件头由三段组成，以独占一行的 "-" 分隔：
 *   段1: 版本号,标志位  (如 "1,0")
 *   段2: 分组列表（每行一个分组名）
 *   段3: 备注文本
 *
 * @param headerText - 文件头原始文本
 * @returns 解析后的 fileHead、groupList、comment
 */
function parseHeader(headerText: string): {
  fileHead: [string, string]
  groupList: string[]
  comment: string
} {
  /* 按独占一行的 "-" 拆分（兼容 \r\n 和 \n） */
  const blocks = headerText.split(/\r?\n-\r?\n/)

  if (blocks.length < 3) {
    throw new Error('标注文件头格式错误：缺少必要的分隔块')
  }

  /* 段1：文件头版本信息 */
  const headParts = blocks[0].split(',')
  const fileHead: [string, string] = [
    headParts[0]?.trim() ?? '1',
    headParts[1]?.trim() ?? '0',
  ]

  /* 段2：分组列表 */
  const groupList = blocks[1]
    .split(/\r?\n/)
    .map(line => line.trim())
    .filter(line => line.length > 0)

  /* 段3：备注 */
  const comment = blocks[2].trim()

  return { fileHead, groupList, comment }
}

/**
 * 解析标注文件全文
 *
 * @param content - 文件完整文本内容
 * @returns 解析后的 LabelFileData
 */
export function parseLabelFile(content: string): LabelFileData {
  const lines = content.split(/\r?\n/)
  const headerLines: string[] = []
  let i = 0

  /* ---- 读取文件头 ---- */
  while (i < lines.length) {
    const line = lines[i]
    /* 遇到图片块或标签条目行，说明头部结束 */
    if (
      (line.startsWith('>>>>>>>>') || line.startsWith('----------------')) &&
      line.includes('[') && line.includes(']')
    ) {
      break
    }
    headerLines.push(line)
    i++
  }

  const headerText = headerLines.join('\n')
  const { fileHead, groupList, comment } = parseHeader(headerText)

  /* ---- 解析主体 ---- */
  const store: Record<string, LabelItem[]> = {}
  let currentList: LabelItem[] | null = null

  while (i < lines.length) {
    const line = lines[i]

    /* 图片文件名块：>>>>>>>>[filename]<<<<<<<< */
    if (line.startsWith('>>>>>>>>') && line.includes(']<<<<<<<<')) {
      const start = line.indexOf('[') + 1
      const end = line.indexOf(']<<<<<<<<')
      if (end > start) {
        const fileName = line.substring(start, end)
        currentList = []
        store[fileName] = currentList
      }
      i++
      continue
    }

    /* 标签条目行：----------------[编号]----------------[x,y,分类] */
    if (line.startsWith('----------------[') && line.includes(']----------------')) {
      const headerEnd = line.indexOf(']----------------')
      const rightStart = headerEnd + ']----------------'.length

      let x = 0
      let y = 0
      let category = 1

      /* 解析坐标和分类 */
      if (rightStart <= line.length) {
        const rightText = line.substring(rightStart)
        if (rightText.startsWith('[') && rightText.endsWith(']')) {
          const inner = rightText.substring(1, rightText.length - 1)
          const parts = inner.split(',')
          if (parts.length >= 3) {
            x = parseFloat(parts[0]) || 0
            y = parseFloat(parts[1]) || 0
            category = parseInt(parts[2], 10) || 1
          }
        }
      }

      /* 读取多行文本，直到遇到下一个标记行 */
      i++
      const textLines: string[] = []
      while (i < lines.length) {
        const peek = lines[i]
        if (
          peek.startsWith('----------------[') ||
          (peek.startsWith('>>>>>>>>') && peek.includes(']<<<<<<<<'))
        ) {
          break
        }
        textLines.push(peek)
        i++
      }

      /* 去除尾部空行 */
      while (textLines.length > 0 && textLines[textLines.length - 1].trim() === '') {
        textLines.pop()
      }
      const text = textLines.join('\n')

      /* 添加标签 */
      if (currentList) {
        const label: LabelItem = {
          xPercent: x,
          yPercent: y,
          text,
          category,
          index: currentList.length + 1,
          categoryString: CATEGORY_LABELS[category] ?? `分类${category}`,
        }
        currentList.push(label)
      }
      continue
    }

    /* 其他行（空行等）跳过 */
    i++
  }

  return { fileHead, groupList, comment, store }
}
