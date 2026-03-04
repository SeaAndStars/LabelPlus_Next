/**
 * ESLint 配置文件（Flat Config 格式）
 *
 * 规则说明：
 * - 基于 Vue 3 推荐规则 + TypeScript 严格规则
 * - 忽略构建产物和依赖目录
 */
import js from '@eslint/js'
import pluginVue from 'eslint-plugin-vue'
import tseslint from 'typescript-eslint'

export default [
  /* 忽略不需要检查的目录 */
  { ignores: ['dist/**', 'node_modules/**', 'electron/**', '.vite/**', 'out/**'] },

  /* JavaScript 基础推荐规则 */
  js.configs.recommended,

  /* TypeScript 推荐规则 */
  ...tseslint.configs.recommended,

  /* Vue 3 推荐规则 */
  ...pluginVue.configs['flat/recommended'],

  /* Vue 文件使用 TypeScript 解析器 */
  {
    files: ['**/*.vue'],
    languageOptions: {
      parserOptions: { parser: tseslint.parser }
    }
  },

  /* 浏览器环境全局变量（HTMLCanvasElement, MouseEvent 等） */
  {
    languageOptions: {
      globals: {
        window: 'readonly',
        document: 'readonly',
        HTMLCanvasElement: 'readonly',
        HTMLDivElement: 'readonly',
        HTMLTextAreaElement: 'readonly',
        HTMLImageElement: 'readonly',
        MouseEvent: 'readonly',
        WheelEvent: 'readonly',
        DragEvent: 'readonly',
        Event: 'readonly',
        Image: 'readonly',
        ResizeObserver: 'readonly',
        requestAnimationFrame: 'readonly',
        cancelAnimationFrame: 'readonly',
        setTimeout: 'readonly',
        clearTimeout: 'readonly',
        setInterval: 'readonly',
        clearInterval: 'readonly',
        console: 'readonly',
        alert: 'readonly'
      }
    }
  },

  /* 项目自定义规则 */
  {
    rules: {
      /* 允许使用 any（迁移过渡期） */
      '@typescript-eslint/no-explicit-any': 'warn',
      /* Vue 组件名称必须多单词（根组件 App 除外） */
      'vue/multi-word-component-names': 'off',
      /* 未使用变量警告（以 _ 开头的忽略） */
      '@typescript-eslint/no-unused-vars': ['warn', { argsIgnorePattern: '^_' }]
    }
  }
]
