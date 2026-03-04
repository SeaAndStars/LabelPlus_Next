/**
 * Vite 渲染进程构建配置
 *
 * 构建 Vue 3 前端应用（浏览器环境）。
 * 包含 Vue 插件和 SCSS 支持。
 */

import { defineConfig } from 'vite'
import vue from '@vitejs/plugin-vue'

export default defineConfig({
  plugins: [vue()],
})
