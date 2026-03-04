<!--
  LabelGrid — 标签列表表格

  功能：
  - 表格显示标签（编号、文本预览、分类）
  - 分类列按颜色着色
  - 单选联动 PicViewer
  - 拖拽排序
  - 顶部工具栏（添加/删除/撤销/文件设置）

  对应 Avalonia 端 TranslateView.axaml 中的 DataGrid 部分
-->

<script setup lang="ts">
import { ref } from 'vue'
import type { LabelItem } from '../types/label'
import { CATEGORY_COLORS } from '../types/label'

/* ---- Props ---- */
defineProps<{
  /** 标签列表 */
  labels: LabelItem[]
  /** 当前选中的标签 */
  selectedLabel: LabelItem | null
}>()

/* ---- Emits ---- */
const emit = defineEmits<{
  /** 选中标签变更 */
  'update:selectedLabel': [label: LabelItem | null]
  /** 添加标签 */
  'add': []
  /** 删除标签 */
  'remove': []
  /** 撤销删除 */
  'undo': []
  /** 打开文件设置 */
  'file-settings': []
  /** 标签排序（拖拽） */
  'reorder': [oldIndex: number, newIndex: number]
}>()

/* ---- 拖拽排序状态 ---- */
const dragIndex = ref<number | null>(null)
const dropIndex = ref<number | null>(null)

/** 拖拽开始 */
function onDragStart(index: number, e: DragEvent) {
  dragIndex.value = index
  if (e.dataTransfer) {
    e.dataTransfer.effectAllowed = 'move'
  }
}

/** 拖拽经过 */
function onDragOver(index: number, e: DragEvent) {
  e.preventDefault()
  dropIndex.value = index
}

/** 拖拽放下 */
function onDrop(index: number) {
  if (dragIndex.value !== null && dragIndex.value !== index) {
    emit('reorder', dragIndex.value, index)
  }
  dragIndex.value = null
  dropIndex.value = null
}

/** 拖拽结束 */
function onDragEnd() {
  dragIndex.value = null
  dropIndex.value = null
}

/** 获取标签文本预览（截取首行，最多 30 字） */
function textPreview(text: string): string {
  const firstLine = text.split('\n')[0] ?? ''
  return firstLine.length > 30 ? firstLine.substring(0, 30) + '…' : firstLine
}
</script>

<template>
  <div class="label-grid">
    <!-- 工具栏 -->
    <div class="label-toolbar">
      <button
        class="tool-btn add-btn"
        title="添加标签"
        @click="emit('add')"
      >
        ✚
      </button>
      <span class="separator" />
      <button
        class="tool-btn remove-btn"
        title="删除标签"
        @click="emit('remove')"
      >
        🗙
      </button>
      <span class="separator" />
      <button
        class="tool-btn"
        title="撤销删除"
        @click="emit('undo')"
      >
        ↺
      </button>
      <span class="separator" />
      <button
        class="tool-btn"
        title="文件设置"
        @click="emit('file-settings')"
      >
        文件设置
      </button>
    </div>

    <!-- 表格 -->
    <div class="grid-container">
      <table class="grid-table">
        <thead>
          <tr>
            <th class="col-index">
              编号
            </th>
            <th class="col-text">
              文本
            </th>
            <th class="col-category">
              分类
            </th>
          </tr>
        </thead>
        <tbody>
          <tr
            v-for="(label, index) in labels"
            :key="label.index"
            :class="{
              'row-selected': selectedLabel != null && label.index === selectedLabel.index,
              'row-drop-before': dropIndex === index && dragIndex !== null && dragIndex > index,
              'row-drop-after': dropIndex === index && dragIndex !== null && dragIndex < index,
            }"
            draggable="true"
            @click="emit('update:selectedLabel', label)"
            @dragstart="onDragStart(index, $event)"
            @dragover="onDragOver(index, $event)"
            @drop="onDrop(index)"
            @dragend="onDragEnd"
          >
            <td class="col-index">
              {{ label.index }}
            </td>
            <td class="col-text">
              {{ textPreview(label.text) }}
            </td>
            <td
              class="col-category"
              :style="{ color: CATEGORY_COLORS[label.category] ?? '#888' }"
            >
              {{ label.categoryString }}
            </td>
          </tr>
          <!-- 空状态 -->
          <tr v-if="labels.length === 0">
            <td
              colspan="3"
              class="empty-hint"
            >
              暂无标签
            </td>
          </tr>
        </tbody>
      </table>
    </div>
  </div>
</template>

<style lang="scss" scoped>
@use '../styles/variables' as *;
@use '../styles/mixins' as *;

.label-grid {
  display: flex;
  flex-direction: column;
  overflow: hidden;
}

/* ---- 工具栏 ---- */
.label-toolbar {
  @include toolbar;
  border-bottom: 1px solid var(--border-default);
  flex-shrink: 0;
}

.tool-btn {
  @include btn-default;
  padding: $spacing-xs $spacing-sm;
  font-size: $font-size-md;
}

.add-btn {
  color: var(--accent);
}

.remove-btn {
  color: $category-inner-color;
}

.separator {
  @include toolbar-separator;
}

/* ---- 表格 ---- */
.grid-container {
  flex: 1;
  overflow-y: auto;
}

.grid-table {
  width: 100%;
  border-collapse: collapse;
  font-size: $font-size-md;

  th {
    position: sticky;
    top: 0;
    background: var(--bg-panel);
    text-align: left;
    padding: $spacing-sm;
    border-bottom: 2px solid var(--border-default);
    font-weight: 600;
    user-select: none;
  }

  td {
    padding: $spacing-sm;
    border-bottom: 1px solid var(--border-default);
    cursor: pointer;
  }

  tr:hover td {
    background: var(--bg-hover);
  }
}

.col-index {
  width: 50px;
  text-align: center;
}

.col-text {
  @include text-ellipsis;
  min-width: 120px;
}

.col-category {
  width: 60px;
  font-weight: 600;
}

/* 选中行 */
.row-selected td {
  background: var(--bg-active);
  border-color: var(--border-active);
  font-weight: 600;
  box-shadow: inset 3px 0 0 var(--accent);
}

/* 拖拽插入位置提示 */
.row-drop-before td {
  border-top: 2px solid var(--accent);
}

.row-drop-after td {
  border-bottom: 2px solid var(--accent);
}

/* 空状态 */
.empty-hint {
  text-align: center;
  color: var(--text-muted);
  padding: $spacing-xl !important;
}
</style>
