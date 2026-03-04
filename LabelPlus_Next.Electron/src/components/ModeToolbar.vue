<!--
  ModeToolbar — 模式工具栏

  功能：
  - 切换查看器模式（浏览/标签/输入/校对）
  - 分类快捷按钮（框内/框外）
  - 当前模式高亮

  对应 Avalonia 端 TranslateView.axaml 中的左侧模式工具栏 + 右上分类快捷
-->

<script setup lang="ts">
import { ViewerMode, LabelCategory } from '../types/label'

/* ---- Props ---- */
defineProps<{
  /** 当前查看器模式 */
  mode: ViewerMode
}>()

/* ---- Emits ---- */
const emit = defineEmits<{
  /** 模式变更 */
  'update:mode': [mode: ViewerMode]
  /** 设置选中标签分类 */
  'set-category': [category: number]
}>()

/** 模式定义列表 */
const modes = [
  { value: ViewerMode.Browse, label: '浏览' },
  { value: ViewerMode.Label, label: '标签' },
  { value: ViewerMode.Input, label: '输入' },
  { value: ViewerMode.Check, label: '校对' },
]
</script>

<template>
  <div class="mode-toolbar no-select">
    <!-- 模式切换按钮组 -->
    <div class="mode-group">
      <button
        v-for="m in modes"
        :key="m.value"
        :class="['mode-btn', { active: mode === m.value }]"
        @click="emit('update:mode', m.value)"
      >
        {{ m.label }}
      </button>
    </div>

    <div class="toolbar-spacer" />

    <!-- 分类快捷按钮 -->
    <div class="category-group">
      <button
        class="category-btn inner"
        @click="emit('set-category', LabelCategory.Inner)"
      >
        框内(1)
      </button>
      <span class="separator" />
      <button
        class="category-btn outer"
        @click="emit('set-category', LabelCategory.Outer)"
      >
        框外(2)
      </button>
    </div>
  </div>
</template>

<style lang="scss" scoped>
@use '../styles/variables' as *;
@use '../styles/mixins' as *;

.mode-toolbar {
  @include toolbar;
  background: var(--bg-panel);
  border-bottom: 1px solid var(--border-default);
  padding: 0 $spacing-md;
}

.mode-group {
  display: flex;
  gap: $spacing-xs;
}

.mode-btn {
  @include btn-default;
  border: none;
  background: transparent;
  padding: $spacing-xs $spacing-sm;

  &:hover {
    background: var(--bg-hover);
  }

  &.active {
    background: var(--bg-active);
    color: var(--accent);
    font-weight: 600;
  }
}

.toolbar-spacer {
  flex: 1;
}

.category-group {
  display: flex;
  align-items: center;
  gap: $spacing-xs;
}

.category-btn {
  @include btn-default;
  border: none;
  background: transparent;
  padding: $spacing-xs $spacing-sm;
  font-weight: 600;

  &.inner {
    color: $category-inner-color;
  }

  &.outer {
    color: $category-outer-color;
  }

  &:hover {
    background: var(--bg-hover);
  }
}

.separator {
  @include toolbar-separator;
}
</style>
