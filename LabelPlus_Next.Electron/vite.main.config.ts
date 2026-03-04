/**
 * Vite 主进程构建配置
 *
 * 构建 Electron 主进程代码（Node.js 环境）。
 */

import { defineConfig } from 'vite'

export default defineConfig({
  build: {
    rollupOptions: {
      external: ['electron'],
    },
  },
})
