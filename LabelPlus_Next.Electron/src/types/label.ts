/**
 * 标签相关类型定义
 *
 * 与 Avalonia 端 LabelItem.cs / LabelStoreManager.cs 对应，
 * 确保数据结构在两端保持一致。
 */

/** 标签分类枚举（对应 Avalonia 的 Category 1-9） */
export enum LabelCategory {
  /** 框内文本 */
  Inner = 1,
  /** 框外文本 */
  Outer = 2,
}

/** 查看器交互模式（对应 Avalonia PicViewer.ViewerMode） */
export enum ViewerMode {
  /** 浏览模式 — 仅查看和选中标签 */
  Browse = 'browse',
  /** 标签编辑模式 — 可添加/拖拽标签 */
  Label = 'label',
  /** 输入模式 — 逐条输入文本 */
  Input = 'input',
  /** 校对模式 — 对照检查 */
  Check = 'check',
}

/**
 * 标签数据项
 *
 * 对应 Avalonia 端 Models/LabelItem.cs
 * - xPercent / yPercent：标签在图片上的百分比坐标 (0~1)
 * - text：标签翻译文本（可多行）
 * - category：分类编号（1=框内, 2=框外, ...）
 * - index：显示编号（从 1 开始）
 */
export interface LabelItem {
  /** 水平百分比坐标 (0~1) */
  xPercent: number
  /** 垂直百分比坐标 (0~1) */
  yPercent: number
  /** 翻译文本内容 */
  text: string
  /** 分类编号 (1=框内, 2=框外) */
  category: number
  /** 显示编号 */
  index: number
  /** 分类显示字符串（如 "框内"、"框外"） */
  categoryString: string
}

/** 分类编号到显示文本的映射 */
export const CATEGORY_LABELS: Record<number, string> = {
  [LabelCategory.Inner]: '框内',
  [LabelCategory.Outer]: '框外',
}

/** 分类编号到颜色的映射 */
export const CATEGORY_COLORS: Record<number, string> = {
  [LabelCategory.Inner]: '#e53e3e',
  [LabelCategory.Outer]: '#3182ce',
}

/**
 * 创建新的空白标签
 *
 * @param index - 编号
 * @param category - 分类编号，默认框内
 * @param x - X 百分比坐标，默认 0.5
 * @param y - Y 百分比坐标，默认 0.5
 */
export function createLabel(
  index: number,
  category: number = LabelCategory.Inner,
  x: number = 0.5,
  y: number = 0.5,
): LabelItem {
  return {
    xPercent: x,
    yPercent: y,
    text: '',
    category,
    index,
    categoryString: CATEGORY_LABELS[category] ?? `分类${category}`,
  }
}
