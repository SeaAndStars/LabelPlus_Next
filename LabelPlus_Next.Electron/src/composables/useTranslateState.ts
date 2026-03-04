/**
 * 翻译状态管理 Composable
 *
 * 集中管理翻译页面的所有响应式状态和业务逻辑，
 * 对应 Avalonia 端 TranslateViewModel.cs 的核心功能。
 */

import { ref, computed, watch, type Ref } from 'vue'
import type { LabelItem } from '../types/label'
import { ViewerMode, LabelCategory } from '../types/label'
import { LabelFileManager } from '../services/labelFileManager'
import { loadImage, listImageFiles, clearImageCache } from '../services/imageService'
import { useToast } from './useToast'
import * as fileService from '../services/fileService'

/** 自动保存间隔（毫秒） */
const AUTO_SAVE_INTERVAL = 60_000

/**
 * 翻译状态 Composable
 *
 * 提供翻译页面所有状态和操作方法。
 * 使用 Vue 的 Composition API 实现响应式。
 */
export function useTranslateState() {
  /* ========== 核心状态 ========== */

  /** 标注文件管理器实例 */
  const manager = new LabelFileManager()

  /**
   * 数据版本号（响应式触发器）
   *
   * LabelFileManager 是普通类，Vue 无法自动追踪其内部变化。
   * 每次对 manager 的变更操作后递增此计数器，
   * 从而驱动依赖 manager 数据的 computed 重新计算。
   */
  const dataVersion = ref(0)

  /** 通知 Vue 数据已变更（递增版本号） */
  function bumpVersion(): void {
    dataVersion.value++
  }

  /** 当前打开的翻译文件路径 */
  const openFilePath: Ref<string | null> = ref(null)

  /** 翻译文件所在目录 */
  const fileDir: Ref<string | null> = ref(null)

  /** 当前选中的图片文件名 */
  const selectedImageFile: Ref<string | null> = ref(null)

  /** 当前选中的标签 */
  const selectedLabel: Ref<LabelItem | null> = ref(null)

  /** 当前编辑的文本 */
  const currentText = ref('')

  /** 查看器交互模式 */
  const viewerMode = ref<ViewerMode>(ViewerMode.Browse)

  /** 是否忙碌（加载中） */
  const isBusy = ref(false)

  /** 当前图片的 Data URL */
  const currentImageSrc = ref('')

  /** 是否有未保存的修改 */
  const isDirty = computed(() => {
    void dataVersion.value
    return manager.isDirty
  })

  /** 自动保存定时器 */
  let autoSaveTimer: ReturnType<typeof setInterval> | null = null

  /* ========== 计算属性 ========== */

  /** 图片文件名列表 */
  const imageFileNames = computed(() => {
    void dataVersion.value
    return manager.imageFileNames
  })

  /** 当前图片的标签列表 */
  const currentLabels = computed<LabelItem[]>(() => {
    void dataVersion.value
    if (!selectedImageFile.value) return []
    return manager.getLabels(selectedImageFile.value)
  })

  /** 分组列表 */
  const groupList = computed(() => {
    void dataVersion.value
    return manager.data.groupList
  })

  /** 文件备注 */
  const fileComment = computed(() => {
    void dataVersion.value
    return manager.data.comment
  })

  /* ========== 监听器 ========== */

  /**
   * 选中标签变更时，同步文本编辑区
   */
  watch(selectedLabel, (label) => {
    currentText.value = label?.text ?? ''
  })

  /**
   * 文本编辑时，实时更新标签数据
   */
  watch(currentText, (text) => {
    if (selectedLabel.value) {
      manager.updateLabelText(selectedLabel.value, text)
      bumpVersion()
    }
  })

  const toast = useToast()

  /**
   * 切换图片时，加载新图片并重置选中标签。
   * 若图片文件不存在，自动跳转到下一张可用图片并提示用户。
   */
  watch(selectedImageFile, async (newFile) => {
    selectedLabel.value = null
    currentText.value = ''
    if (!newFile || !fileDir.value) {
      currentImageSrc.value = ''
      return
    }
    try {
      const imgPath = await fileService.joinPath(fileDir.value, newFile)
      const dataUrl = await loadImage(imgPath)

      /* 图片文件不存在（IPC 返回空字符串） */
      if (!dataUrl) {
        toast.warn(`图片文件不存在，已跳过: ${newFile}`)
        skipToNextAvailableImage(newFile)
        return
      }

      currentImageSrc.value = dataUrl
    } catch {
      toast.error(`加载图片失败: ${newFile}`)
      currentImageSrc.value = ''
    }
  })

  /**
   * 跳过不存在的图片，切换到下一张可用图片。
   * 从当前图片之后开始查找，到达末尾后不再循环。
   *
   * @param missingFile - 不存在的图片文件名
   */
  async function skipToNextAvailableImage(missingFile: string): Promise<void> {
    const names = imageFileNames.value
    const idx = names.indexOf(missingFile)
    if (idx < 0 || !fileDir.value) {
      currentImageSrc.value = ''
      return
    }

    /* 向后查找第一张可用的图片 */
    for (let i = idx + 1; i < names.length; i++) {
      try {
        const imgPath = await fileService.joinPath(fileDir.value!, names[i])
        const dataUrl = await loadImage(imgPath)
        if (dataUrl) {
          selectedImageFile.value = names[i]
          return
        }
      } catch {
        /* 继续查找下一张 */
      }
    }

    /* 向后无可用图片，向前查找 */
    for (let i = idx - 1; i >= 0; i--) {
      try {
        const imgPath = await fileService.joinPath(fileDir.value!, names[i])
        const dataUrl = await loadImage(imgPath)
        if (dataUrl) {
          selectedImageFile.value = names[i]
          return
        }
      } catch {
        /* 继续查找 */
      }
    }

    /* 所有图片均不可用 */
    toast.error('目录中无可用的图片文件')
    currentImageSrc.value = ''
  }

  /* ========== 文件操作 ========== */

  /**
   * 新建翻译文件
   *
   * 弹出目录选择对话框，创建空白标注文件。
   */
  async function newTranslation(): Promise<void> {
    const dir = await fileService.selectDirectory()
    if (!dir) return

    manager.createNew()
    bumpVersion()
    fileDir.value = dir
    openFilePath.value = null
    selectedImageFile.value = null
    selectedLabel.value = null
    currentText.value = ''
    clearImageCache()

    /* 加载目录下的图片 */
    const images = await listImageFiles(dir)
    for (const img of images) {
      manager.addImageFile(img)
    }
    bumpVersion()
    if (images.length > 0) {
      selectedImageFile.value = images[0]
    }

    startAutoSave()
  }

  /**
   * 打开翻译文件
   *
   * 弹出文件对话框，读取并解析标注文件。
   */
  async function openTranslation(): Promise<void> {
    const filePath = await fileService.openTranslationFile()
    if (!filePath) return

    await loadTranslationFile(filePath)
  }

  /**
   * 从指定路径加载翻译文件
   */
  async function loadTranslationFile(filePath: string): Promise<void> {
    isBusy.value = true
    try {
      const content = await fileService.readTextFile(filePath)
      manager.load(content)
      bumpVersion()
      openFilePath.value = filePath
      fileDir.value = await fileService.getDirname(filePath)
      clearImageCache()

      /* 选中第一张图片 */
      const names = manager.imageFileNames
      selectedImageFile.value = names.length > 0 ? names[0] : null
      selectedLabel.value = null
      currentText.value = ''

      startAutoSave()
    } finally {
      isBusy.value = false
    }
  }

  /**
   * 保存当前翻译文件
   */
  async function saveCurrent(): Promise<void> {
    if (!openFilePath.value) {
      await saveAs()
      return
    }
    const content = manager.serialize()
    await fileService.writeTextFile(openFilePath.value, content)
    manager.markSaved()
    bumpVersion()
  }

  /**
   * 另存为
   */
  async function saveAs(): Promise<void> {
    const filePath = await fileService.saveTranslationFile()
    if (!filePath) return
    openFilePath.value = filePath
    fileDir.value = await fileService.getDirname(filePath)
    const content = manager.serialize()
    await fileService.writeTextFile(filePath, content)
    manager.markSaved()
    bumpVersion()
  }

  /**
   * 打开翻译文件所在目录
   */
  async function openTranslationFolder(): Promise<void> {
    if (fileDir.value) {
      await fileService.openInExplorer(fileDir.value)
    }
  }

  /* ========== 标签操作 ========== */

  /**
   * 添加标签
   *
   * @param category - 分类编号，默认使用框内
   * @param x - X 百分比坐标
   * @param y - Y 百分比坐标
   */
  function addLabel(category = LabelCategory.Inner, x = 0.5, y = 0.5): void {
    if (!selectedImageFile.value) return
    const label = manager.addLabel(selectedImageFile.value, category, x, y)
    bumpVersion()
    selectedLabel.value = label
    currentText.value = label.text
  }

  /**
   * 删除当前选中的标签
   */
  function removeLabel(): void {
    if (!selectedImageFile.value || !selectedLabel.value) return
    const labels = currentLabels.value
    /* 使用 index 字段匹配，避免引用比较失败 */
    const selIdx = selectedLabel.value.index
    const oldIdx = labels.findIndex(l => l.index === selIdx)
    manager.removeLabel(selectedImageFile.value, selectedLabel.value)
    bumpVersion()

    /* 选中相邻标签 */
    const newLabels = currentLabels.value
    if (newLabels.length > 0) {
      const nextIdx = Math.min(Math.max(oldIdx, 0), newLabels.length - 1)
      selectedLabel.value = newLabels[nextIdx]
      currentText.value = selectedLabel.value.text
    } else {
      selectedLabel.value = null
      currentText.value = ''
    }
  }

  /**
   * 撤销删除标签
   */
  function undoRemoveLabel(): void {
    if (!selectedImageFile.value) return
    const restored = manager.undoRemoveLabel(selectedImageFile.value)
    if (restored) {
      bumpVersion()
      selectedLabel.value = restored
      currentText.value = restored.text
    }
  }

  /**
   * 移动标签顺序
   */
  function moveLabel(oldIndex: number, newIndex: number): void {
    if (!selectedImageFile.value) return
    manager.moveLabel(selectedImageFile.value, oldIndex, newIndex)
    bumpVersion()
  }

  /**
   * 更新标签分类
   */
  function setLabelCategory(label: LabelItem, category: number): void {
    manager.updateLabelCategory(label, category)
    bumpVersion()
  }

  /**
   * 更新标签位置（拖拽后）
   */
  function updateLabelPosition(label: LabelItem, x: number, y: number): void {
    manager.updateLabelPosition(label, x, y)
    bumpVersion()
  }

  /* ========== 图片导航 ========== */

  /**
   * 切换到上一张图片
   */
  function prevImage(): void {
    const names = imageFileNames.value
    if (names.length === 0) return
    const idx = names.indexOf(selectedImageFile.value ?? '')
    selectedImageFile.value = names[Math.max(0, idx - 1)]
  }

  /**
   * 切换到下一张图片
   */
  function nextImage(): void {
    const names = imageFileNames.value
    if (names.length === 0) return
    const idx = names.indexOf(selectedImageFile.value ?? '')
    selectedImageFile.value = names[Math.min(names.length - 1, idx + 1)]
  }

  /* ========== 图片管理 ========== */

  /**
   * 获取目录下可用的图片文件（尚未添加的）
   */
  async function getAvailableImages(): Promise<string[]> {
    if (!fileDir.value) return []
    const allImages = await listImageFiles(fileDir.value)
    const included = new Set(manager.imageFileNames)
    return allImages.filter(img => !included.has(img))
  }

  /**
   * 更新已包含的图片列表
   *
   * @param included - 新的已包含图片列表
   */
  function updateIncludedImages(included: string[]): void {
    const currentStore = manager.data.store
    const newStore: Record<string, typeof currentStore[string]> = {}

    for (const name of included) {
      newStore[name] = currentStore[name] ?? []
    }

    /* 更新 store */
    for (const key of Object.keys(currentStore)) {
      if (!(key in newStore)) {
        delete currentStore[key]
      }
    }
    for (const [key, val] of Object.entries(newStore)) {
      if (!(key in currentStore)) {
        currentStore[key] = val
      }
    }

    /* 如果当前选中的图片被移除，切换到第一张 */
    if (selectedImageFile.value && !included.includes(selectedImageFile.value)) {
      selectedImageFile.value = included.length > 0 ? included[0] : null
    }
    bumpVersion()
  }

  /**
   * 更新文件头（分组和备注）
   */
  function updateFileSettings(groups: string[], comment: string): void {
    manager.updateHeader(groups, comment)
    bumpVersion()
  }

  /* ========== 自动保存 ========== */

  /**
   * 启动自动保存定时器
   */
  function startAutoSave(): void {
    stopAutoSave()
    autoSaveTimer = setInterval(async () => {
      if (manager.isDirty && openFilePath.value) {
        try {
          await saveCurrent()
        } catch {
          /* 自动保存失败时静默处理 */
        }
      }
    }, AUTO_SAVE_INTERVAL)
  }

  /**
   * 停止自动保存定时器
   */
  function stopAutoSave(): void {
    if (autoSaveTimer) {
      clearInterval(autoSaveTimer)
      autoSaveTimer = null
    }
  }

  /* ========== 返回公开接口 ========== */

  return {
    /* 状态 */
    openFilePath,
    fileDir,
    selectedImageFile,
    selectedLabel,
    currentText,
    viewerMode,
    isBusy,
    isDirty,
    currentImageSrc,
    imageFileNames,
    currentLabels,
    groupList,
    fileComment,

    /* 文件操作 */
    newTranslation,
    openTranslation,
    loadTranslationFile,
    saveCurrent,
    saveAs,
    openTranslationFolder,

    /* 标签操作 */
    addLabel,
    removeLabel,
    undoRemoveLabel,
    moveLabel,
    setLabelCategory,
    updateLabelPosition,

    /* 图片导航 */
    prevImage,
    nextImage,

    /* 图片/文件设置管理 */
    getAvailableImages,
    updateIncludedImages,
    updateFileSettings,

    /* 生命周期 */
    stopAutoSave,

    /** 直接访问 manager（供高级用途） */
    manager,
  }
}
