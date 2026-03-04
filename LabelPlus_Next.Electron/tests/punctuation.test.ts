import { describe, expect, it } from 'vitest'
import { convertPunctuation } from '../src/utils/punctuation'

describe('convertPunctuation', () => {
  it('converts English punctuation to Chinese punctuation', () => {
    const result = convertPunctuation('Hello, world! (test)...', 'enToZh')
    expect(result.output).toBe('Hello， world！ （test）…')
    expect(result.replaced).toBe(5)
  })

  it('converts Chinese punctuation to English punctuation', () => {
    const result = convertPunctuation('你好，世界！（测试）…', 'zhToEn')
    expect(result.output).toBe('你好,世界!(测试)...')
    expect(result.replaced).toBe(5)
  })

  it('is idempotent when converting same direction repeatedly', () => {
    const once = convertPunctuation('A, B.', 'enToZh')
    const twice = convertPunctuation(once.output, 'enToZh')
    expect(twice.output).toBe(once.output)
    expect(twice.replaced).toBe(0)
  })
})