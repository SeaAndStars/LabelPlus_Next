/**
 * 标注文件读取器 / 写入器单元测试
 *
 * 验证 parseLabelFile 和 serializeLabelFile 的正确性，
 * 以及两者之间的往返一致性（round-trip）。
 */

import { describe, expect, it } from 'vitest'
import { parseLabelFile } from '../src/services/labelFileReader'
import { serializeLabelFile } from '../src/services/labelFileWriter'

/** 模拟标注文件内容（与 Avalonia 端格式一致） */
const SAMPLE_FILE = `1,0
-
框内
框外
-
这是备注

>>>>>>>>[page001.png]<<<<<<<<
----------------[1]----------------[0.100,0.200,1]
你好世界

----------------[2]----------------[0.500,0.600,2]
第二条标签
多行文本

>>>>>>>>[page002.png]<<<<<<<<
----------------[1]----------------[0.300,0.400,1]
第二页标签
`

describe('labelFileReader', () => {
  it('正确解析文件头信息', () => {
    const data = parseLabelFile(SAMPLE_FILE)
    expect(data.fileHead).toEqual(['1', '0'])
    expect(data.groupList).toEqual(['框内', '框外'])
    expect(data.comment).toBe('这是备注')
  })

  it('正确解析图片和标签数据', () => {
    const data = parseLabelFile(SAMPLE_FILE)

    /* 应有两张图片 */
    expect(Object.keys(data.store)).toEqual(['page001.png', 'page002.png'])

    /* 第一张图片有 2 个标签 */
    const labels1 = data.store['page001.png']
    expect(labels1).toHaveLength(2)

    expect(labels1[0].xPercent).toBeCloseTo(0.1)
    expect(labels1[0].yPercent).toBeCloseTo(0.2)
    expect(labels1[0].category).toBe(1)
    expect(labels1[0].text).toBe('你好世界')
    expect(labels1[0].index).toBe(1)

    expect(labels1[1].xPercent).toBeCloseTo(0.5)
    expect(labels1[1].yPercent).toBeCloseTo(0.6)
    expect(labels1[1].category).toBe(2)
    expect(labels1[1].text).toBe('第二条标签\n多行文本')
    expect(labels1[1].index).toBe(2)

    /* 第二张图片有 1 个标签 */
    const labels2 = data.store['page002.png']
    expect(labels2).toHaveLength(1)
    expect(labels2[0].text).toBe('第二页标签')
  })

  it('处理空文件（只有文件头）', () => {
    const emptyFile = `1,0\n-\n\n-\n\n`
    const data = parseLabelFile(emptyFile)
    expect(data.fileHead).toEqual(['1', '0'])
    expect(data.groupList).toEqual([])
    expect(Object.keys(data.store)).toEqual([])
  })
})

describe('labelFileWriter', () => {
  it('序列化后可被正确解析（往返一致性）', () => {
    const original = parseLabelFile(SAMPLE_FILE)
    const serialized = serializeLabelFile(original)
    const reparsed = parseLabelFile(serialized)

    /* 文件头一致 */
    expect(reparsed.fileHead).toEqual(original.fileHead)
    expect(reparsed.groupList).toEqual(original.groupList)
    expect(reparsed.comment).toBe(original.comment)

    /* 图片列表一致 */
    expect(Object.keys(reparsed.store)).toEqual(Object.keys(original.store))

    /* 标签数据一致 */
    for (const [key, labels] of Object.entries(original.store)) {
      const reparsedLabels = reparsed.store[key]
      expect(reparsedLabels).toHaveLength(labels.length)

      for (let i = 0; i < labels.length; i++) {
        expect(reparsedLabels[i].xPercent).toBeCloseTo(labels[i].xPercent, 2)
        expect(reparsedLabels[i].yPercent).toBeCloseTo(labels[i].yPercent, 2)
        expect(reparsedLabels[i].category).toBe(labels[i].category)
        expect(reparsedLabels[i].text).toBe(labels[i].text)
      }
    }
  })
})
