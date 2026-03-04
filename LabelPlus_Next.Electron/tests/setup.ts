/**
 * Vitest 全局测试设置
 *
 * 为 jsdom 环境补充缺失的浏览器 API mock（如 ResizeObserver）。
 */

import { vi } from 'vitest'

/* Mock ResizeObserver（jsdom 不支持） */
global.ResizeObserver = vi.fn().mockImplementation(() => ({
  observe: vi.fn(),
  unobserve: vi.fn(),
  disconnect: vi.fn()
}))

/* Mock HTMLCanvasElement.getContext（jsdom 不支持） */
HTMLCanvasElement.prototype.getContext = vi.fn().mockReturnValue({
  clearRect: vi.fn(),
  fillRect: vi.fn(),
  drawImage: vi.fn(),
  beginPath: vi.fn(),
  arc: vi.fn(),
  fill: vi.fn(),
  stroke: vi.fn(),
  fillText: vi.fn(),
  measureText: vi.fn().mockReturnValue({ width: 0 }),
  save: vi.fn(),
  restore: vi.fn(),
  translate: vi.fn(),
  scale: vi.fn(),
  setTransform: vi.fn()
}) as unknown as CanvasRenderingContext2D
