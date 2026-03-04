/**
 * 标注文件写入器
 *
 * 将内存中的 LabelFileData 序列化为 LabelPlus .txt 格式字符串。
 * 与 Avalonia 端 Models/LabelFileWriter.cs 逻辑完全对应，
 * 确保两端产出文件可互相读取。
 */

import type { LabelFileData } from '../types/file'

/**
 * 生成文件头字符串
 *
 * 格式：
 *   版本号,标志位
 *   -
 *   分组1
 *   分组2
 *   ...
 *   -
 *   备注
 *
 * @param fileHead - 文件头版本信息
 * @param groupList - 分组列表
 * @param comment - 备注
 * @returns 文件头文本
 */
function generateHeader(
  fileHead: [string, string],
  groupList: string[],
  comment: string,
): string {
  let result = fileHead.join(',') + '\n-\n'
  for (const group of groupList) {
    result += group + '\n'
  }
  result += '-\n'
  result += comment + '\n'
  return result
}

/**
 * 格式化浮点数为固定 3 位小数（与 C# F3 格式一致）
 */
function formatFloat(value: number): string {
  return value.toFixed(3)
}

/**
 * 将标签数据序列化为文件文本内容
 *
 * @param data - 标注文件数据
 * @returns 完整的文件文本内容
 */
export function serializeLabelFile(data: LabelFileData): string {
  const lines: string[] = []

  /* 写入文件头 */
  lines.push(generateHeader(data.fileHead, data.groupList, data.comment))

  /* 写入各图片的标签数据 */
  for (const [fileName, labels] of Object.entries(data.store)) {
    lines.push('')
    lines.push(`>>>>>>>>[${fileName}]<<<<<<<<`)

    let count = 0
    for (const label of labels) {
      count++
      const coord = `[${formatFloat(label.xPercent)},${formatFloat(label.yPercent)},${label.category}]`
      lines.push(`----------------[${count}]----------------${coord}`)
      lines.push(label.text)
      lines.push('')
    }
  }

  return lines.join('\n')
}
