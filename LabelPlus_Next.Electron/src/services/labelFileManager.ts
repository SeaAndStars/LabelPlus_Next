/**
 * 标注文件管理器
 *
 * 统一管理标注文件的加载、保存、脏状态追踪，以及标签的增删改撤销。
 * 对应 Avalonia 端 Models/LabelFileManager.cs + LabelManager.cs + LabelStoreManager.cs
 */

import type { LabelItem } from '../types/label'
import type { LabelFileData } from '../types/file'
import { createEmptyLabelFile } from '../types/file'
import { CATEGORY_LABELS, createLabel } from '../types/label'
import { parseLabelFile } from './labelFileReader'
import { serializeLabelFile } from './labelFileWriter'

/** 删除操作的撤销记录 */
interface UndoEntry {
  /** 被删除的标签 */
  item: LabelItem
  /** 被删除时的索引位置 */
  index: number
}

/**
 * 标注文件管理器类
 *
 * 提供标注文件的完整生命周期管理：
 * - 加载/保存文件
 * - 标签增删改查
 * - 撤销删除
 * - 脏状态追踪
 * - 标签排序
 */
export class LabelFileManager {
  /** 当前标注文件数据 */
  private _data: LabelFileData = createEmptyLabelFile()

  /** 脏标志：是否有未保存的修改 */
  private _isDirty = false

  /** 每张图片的删除撤销栈 */
  private _undoStacks: Map<string, UndoEntry[]> = new Map()

  /** 获取当前标注文件数据（只读） */
  get data(): LabelFileData {
    return this._data
  }

  /** 是否有未保存的修改 */
  get isDirty(): boolean {
    return this._isDirty
  }

  /** 获取图片文件名列表 */
  get imageFileNames(): string[] {
    return Object.keys(this._data.store)
  }

  // ---------- 文件操作 ----------

  /**
   * 从文本内容加载标注文件
   *
   * @param content - 文件文本内容
   */
  load(content: string): void {
    this._data = parseLabelFile(content)
    this._isDirty = false
    this._undoStacks.clear()
  }

  /**
   * 创建新的空白标注文件
   */
  createNew(): void {
    this._data = createEmptyLabelFile()
    this._isDirty = false
    this._undoStacks.clear()
  }

  /**
   * 序列化为文本内容（用于保存）
   *
   * @returns 文件文本内容
   */
  serialize(): string {
    return serializeLabelFile(this._data)
  }

  /** 标记为已保存（清除脏状态） */
  markSaved(): void {
    this._isDirty = false
  }

  /** 标记为有修改 */
  private touchDirty(): void {
    this._isDirty = true
  }

  // ---------- 图片管理 ----------

  /**
   * 获取指定图片的标签列表
   *
   * @param imageFile - 图片文件名
   * @returns 标签数组（可能为空数组）
   */
  getLabels(imageFile: string): LabelItem[] {
    return this._data.store[imageFile] ?? []
  }

  /**
   * 添加图片文件条目
   *
   * @param imageFile - 图片文件名
   */
  addImageFile(imageFile: string): void {
    if (!(imageFile in this._data.store)) {
      this._data.store[imageFile] = []
      this.touchDirty()
    }
  }

  /**
   * 移除图片文件条目
   *
   * @param imageFile - 图片文件名
   */
  removeImageFile(imageFile: string): void {
    if (imageFile in this._data.store) {
      delete this._data.store[imageFile]
      this.touchDirty()
    }
  }

  // ---------- 标签操作 ----------

  /**
   * 添加新标签到指定图片
   *
   * @param imageFile - 图片文件名
   * @param category - 分类编号，默认 1
   * @param x - X 百分比坐标，默认 0.5
   * @param y - Y 百分比坐标，默认 0.5
   * @returns 新创建的标签
   */
  addLabel(imageFile: string, category = 1, x = 0.5, y = 0.5): LabelItem {
    if (!(imageFile in this._data.store)) {
      this._data.store[imageFile] = []
    }
    const labels = this._data.store[imageFile]
    const newLabel = createLabel(labels.length + 1, category, x, y)
    labels.push(newLabel)
    this.touchDirty()
    return newLabel
  }

  /**
   * 删除指定图片中的标签
   *
   * @param imageFile - 图片文件名
   * @param label - 要删除的标签
   * @returns 是否成功删除
   */
  removeLabel(imageFile: string, label: LabelItem): boolean {
    const labels = this._data.store[imageFile]
    if (!labels) return false

    /* 优先引用匹配，回退到 index 匹配（防止 Vue Proxy 包装导致引用不一致） */
    let idx = labels.indexOf(label)
    if (idx < 0) {
      idx = labels.findIndex(l => l.index === label.index)
    }
    if (idx < 0) return false

    /* 保存到撤销栈 */
    if (!this._undoStacks.has(imageFile)) {
      this._undoStacks.set(imageFile, [])
    }
    this._undoStacks.get(imageFile)!.push({ item: labels[idx], index: idx })

    /* 执行删除 */
    labels.splice(idx, 1)
    this.reindexLabels(imageFile)
    this.touchDirty()
    return true
  }

  /**
   * 撤销删除标签
   *
   * @param imageFile - 图片文件名
   * @returns 恢复的标签，如果无可撤销操作则返回 null
   */
  undoRemoveLabel(imageFile: string): LabelItem | null {
    const stack = this._undoStacks.get(imageFile)
    if (!stack || stack.length === 0) return null

    const { item, index } = stack.pop()!
    if (!(imageFile in this._data.store)) {
      this._data.store[imageFile] = []
    }
    const labels = this._data.store[imageFile]
    const insertIdx = Math.min(index, labels.length)
    labels.splice(insertIdx, 0, item)
    this.reindexLabels(imageFile)
    this.touchDirty()
    return item
  }

  /**
   * 移动标签顺序（拖拽排序）
   *
   * @param imageFile - 图片文件名
   * @param oldIndex - 原位置索引
   * @param newIndex - 新位置索引
   */
  moveLabel(imageFile: string, oldIndex: number, newIndex: number): void {
    const labels = this._data.store[imageFile]
    if (!labels || labels.length === 0) return
    if (oldIndex < 0 || oldIndex >= labels.length) return
    if (newIndex < 0) newIndex = 0
    if (newIndex > labels.length) newIndex = labels.length
    if (oldIndex === newIndex) return

    const item = labels.splice(oldIndex, 1)[0]
    /* 移除后索引需要调整 */
    const adjustedNew = newIndex > oldIndex ? newIndex - 1 : newIndex
    labels.splice(Math.min(adjustedNew, labels.length), 0, item)
    this.reindexLabels(imageFile)
    this.touchDirty()
  }

  /**
   * 更新标签文本
   *
   * @param label - 要更新的标签引用
   * @param text - 新文本
   */
  updateLabelText(label: LabelItem, text: string): void {
    label.text = text
    this.touchDirty()
  }

  /**
   * 更新标签分类
   *
   * @param label - 要更新的标签引用
   * @param category - 新分类编号
   */
  updateLabelCategory(label: LabelItem, category: number): void {
    label.category = category
    label.categoryString = CATEGORY_LABELS[category] ?? `分类${category}`
    this.touchDirty()
  }

  /**
   * 更新标签位置
   *
   * @param label - 要更新的标签引用
   * @param x - 新的 X 百分比坐标
   * @param y - 新的 Y 百分比坐标
   */
  updateLabelPosition(label: LabelItem, x: number, y: number): void {
    label.xPercent = x
    label.yPercent = y
    this.touchDirty()
  }

  // ---------- 文件头操作 ----------

  /**
   * 更新文件头信息（分组和备注）
   *
   * @param groupList - 新的分组列表
   * @param comment - 新的备注
   */
  updateHeader(groupList: string[], comment: string): void {
    this._data.groupList = groupList
    this._data.comment = comment
    this.touchDirty()
  }

  // ---------- 内部工具方法 ----------

  /**
   * 重新编号指定图片中的标签
   *
   * 在增删、排序后调用，确保 index 从 1 连续递增。
   */
  private reindexLabels(imageFile: string): void {
    const labels = this._data.store[imageFile]
    if (!labels) return
    labels.forEach((label, i) => {
      label.index = i + 1
    })
  }
}
