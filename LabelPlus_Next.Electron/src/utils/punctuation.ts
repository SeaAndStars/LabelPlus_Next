import type { Direction } from '../types'

const EN_TO_ZH_RULES: Array<[string, string]> = [
  ['...', '…'],
  [',', '，'],
  ['.', '。'],
  ['?', '？'],
  ['!', '！'],
  [':', '：'],
  [';', '；'],
  ['(', '（'],
  [')', '）'],
  ['[', '【'],
  [']', '】'],
  ['"', '“'],
  ['\'', '‘']
]

const ZH_TO_EN_RULES: Array<[string, string]> = [
  ['…', '...'],
  ['，', ','],
  ['。', '.'],
  ['？', '?'],
  ['！', '!'],
  ['：', ':'],
  ['；', ';'],
  ['（', '('],
  ['）', ')'],
  ['【', '['],
  ['】', ']'],
  ['“', '"'],
  ['”', '"'],
  ['‘', '\''],
  ['’', '\'']
]

function replaceAllWithCount(text: string, search: string, replacement: string): { text: string; count: number } {
  if (search.length === 0) {
    return { text, count: 0 }
  }

  const escaped = search.replace(/[.*+?^${}()|[\]\\]/g, '\\$&')
  const regex = new RegExp(escaped, 'g')
  const matches = text.match(regex)
  const count = matches ? matches.length : 0

  return {
    text: text.replace(regex, replacement),
    count
  }
}

export function convertPunctuation(input: string, direction: Direction): { output: string; replaced: number } {
  const rules = direction === 'enToZh' ? EN_TO_ZH_RULES : ZH_TO_EN_RULES
  let output = input
  let replaced = 0

  for (const [search, replacement] of rules) {
    const result = replaceAllWithCount(output, search, replacement)
    output = result.text
    replaced += result.count
  }

  return { output, replaced }
}