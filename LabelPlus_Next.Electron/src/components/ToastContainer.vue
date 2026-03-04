<!--
  ToastContainer — 全局通知容器

  固定定位在右上角，渲染所有活跃的 Toast 通知。
  配合 useToast composable 使用。
-->

<script setup lang="ts">
import { useToast } from '../composables/useToast'

const { toasts } = useToast()
</script>

<template>
  <Teleport to="body">
    <div
      v-if="toasts.length > 0"
      class="toast-container"
    >
      <div
        v-for="toast in toasts"
        :key="toast.id"
        :class="['toast-item', `toast-${toast.type}`]"
      >
        <span class="toast-icon">
          {{ toast.type === 'warn' ? '⚠' : toast.type === 'error' ? '✖' : 'ℹ' }}
        </span>
        <span class="toast-message">{{ toast.message }}</span>
      </div>
    </div>
  </Teleport>
</template>

<style lang="scss" scoped>
@use '../styles/variables' as *;

/* 通知容器：固定在右上角 */
.toast-container {
  position: fixed;
  top: $spacing-lg;
  right: $spacing-lg;
  z-index: 9999;
  display: flex;
  flex-direction: column;
  gap: $spacing-sm;
  pointer-events: none;
  max-width: 420px;
}

/* 单条通知样式 */
.toast-item {
  display: flex;
  align-items: flex-start;
  gap: $spacing-sm;
  padding: $spacing-sm $spacing-md;
  border-radius: $radius-md;
  font-size: $font-size-md;
  color: #fff;
  box-shadow: 0 4px 12px rgba(0, 0, 0, 0.25);
  animation: toast-in 0.25s ease-out;
  pointer-events: auto;
  word-break: break-all;
}

.toast-icon {
  flex-shrink: 0;
  font-size: 16px;
  line-height: 1.4;
}

.toast-message {
  line-height: 1.4;
}

/* 类型颜色 */
.toast-info {
  background: #2563eb;
}

.toast-warn {
  background: #d97706;
}

.toast-error {
  background: #dc2626;
}

/* 入场动画 */
@keyframes toast-in {
  from {
    opacity: 0;
    transform: translateX(40px);
  }

  to {
    opacity: 1;
    transform: translateX(0);
  }
}
</style>
