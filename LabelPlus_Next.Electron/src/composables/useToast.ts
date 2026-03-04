/**
 * Toast 通知 Composable
 *
 * 提供应用级别的消息提示功能（成功、警告、错误）。
 * 使用全局单例模式，任何组件均可调用。
 */

import { ref, type Ref } from 'vue'

/** 通知类型 */
export type ToastType = 'info' | 'warn' | 'error'

/** 通知条目 */
export interface ToastItem {
  id: number
  message: string
  type: ToastType
}

/** 全局通知列表（单例） */
const toasts: Ref<ToastItem[]> = ref([])

/** 自增 ID */
let nextId = 0

/** 默认显示时长（毫秒） */
const DEFAULT_DURATION = 4000

/**
 * 添加一条通知
 *
 * @param message - 显示文本
 * @param type    - 通知类型，默认 'info'
 * @param duration - 自动消失时长（ms），默认 4000
 */
function show(message: string, type: ToastType = 'info', duration = DEFAULT_DURATION): void {
  const id = nextId++
  toasts.value.push({ id, message, type })

  setTimeout(() => {
    toasts.value = toasts.value.filter(t => t.id !== id)
  }, duration)
}

/** 显示警告通知 */
function warn(message: string, duration?: number): void {
  show(message, 'warn', duration)
}

/** 显示错误通知 */
function error(message: string, duration?: number): void {
  show(message, 'error', duration)
}

/** 显示信息通知 */
function info(message: string, duration?: number): void {
  show(message, 'info', duration)
}

/**
 * useToast — Toast 通知 Composable
 *
 * 返回通知列表（用于渲染）和通知方法（用于触发）。
 */
export function useToast() {
  return {
    toasts,
    show,
    info,
    warn,
    error,
  }
}
