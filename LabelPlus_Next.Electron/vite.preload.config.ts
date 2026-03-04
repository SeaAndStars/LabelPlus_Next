/**
 * Vite 预加载脚本构建配置
 *
 * 构建 Electron 预加载脚本（受限 Node.js 环境）。
 */

import { defineConfig } from 'vite'

export default defineConfig({
  build: {
    rollupOptions: {
      external: ['electron'],
    },
  },
})
