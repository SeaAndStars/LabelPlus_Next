/**
 * Electron Forge 配置
 *
 * 使用 Vite 插件管理主进程、预加载脚本和渲染进程的构建。
 * 包含 Windows (Squirrel) 和通用 (zip) 打包配置。
 */

import type { ForgeConfig } from '@electron-forge/shared-types'
import { VitePlugin } from '@electron-forge/plugin-vite'

const config: ForgeConfig = {
  /* 打包配置 */
  packagerConfig: {
    name: 'LabelPlus Next',
    asar: true,
  },

  /* 安装包生成器 */
  makers: [
    {
      name: '@electron-forge/maker-squirrel',
      config: {
        name: 'LabelPlusNext',
      },
    },
    {
      name: '@electron-forge/maker-zip',
      platforms: ['darwin', 'linux'],
    },
  ],

  /* Vite 构建插件 */
  plugins: [
    new VitePlugin({
      /* 主进程和预加载脚本构建配置 */
      build: [
        {
          entry: 'src/main.ts',
          config: 'vite.main.config.ts',
          target: 'main',
        },
        {
          entry: 'src/preload.ts',
          config: 'vite.preload.config.ts',
          target: 'preload',
        },
      ],
      /* 渲染进程构建配置 */
      renderer: [
        {
          name: 'main_window',
          config: 'vite.renderer.config.ts',
        },
      ],
    }),
  ],
}

export default config
