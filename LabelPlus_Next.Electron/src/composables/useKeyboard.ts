/**
 * 键盘快捷键 Composable
 *
 * 集中管理翻译页面的全局键盘快捷键。
 * 对应 Avalonia 端 TranslateView.axaml.cs 中的 OnGlobalKeyDown。
 */

import { onMounted, onUnmounted } from 'vue'

/** 快捷键处理函数映射 */
export interface KeyboardActions {
  /** Ctrl+S：保存 */
  save: () => void
  /** Ctrl+N：新建 */
  newFile: () => void
  /** Ctrl+O：打开 */
  openFile: () => void
  /** 数字键 1：设置标签为"框内"分类 */
  setCategoryInner: () => void
  /** 数字键 2：设置标签为"框外"分类 */
  setCategoryOuter: () => void
  /** Ctrl+Z：撤销删除 */
  undoRemove: () => void
  /** Delete：删除标签 */
  deleteLabel: () => void
}

/**
 * 注册全局键盘快捷键
 *
 * @param actions - 各快捷键对应的处理函数
 */
export function useKeyboard(actions: KeyboardActions) {
  function handleKeyDown(e: KeyboardEvent) {
    /* 如果焦点在文本输入区，只处理 Ctrl 组合键 */
    const target = e.target as HTMLElement
    const isTextInput = target.tagName === 'TEXTAREA' || target.tagName === 'INPUT'

    if (e.ctrlKey || e.metaKey) {
      switch (e.key.toLowerCase()) {
        case 's':
          e.preventDefault()
          actions.save()
          return
        case 'n':
          e.preventDefault()
          actions.newFile()
          return
        case 'o':
          e.preventDefault()
          actions.openFile()
          return
        case 'z':
          if (!isTextInput) {
            e.preventDefault()
            actions.undoRemove()
          }
          return
      }
    }

    /* 非文本输入区的快捷键 */
    if (!isTextInput) {
      switch (e.key) {
        case '1':
          actions.setCategoryInner()
          return
        case '2':
          actions.setCategoryOuter()
          return
        case 'Delete':
          actions.deleteLabel()
          return
      }
    }
  }

  onMounted(() => {
    window.addEventListener('keydown', handleKeyDown)
  })

  onUnmounted(() => {
    window.removeEventListener('keydown', handleKeyDown)
  })
}
