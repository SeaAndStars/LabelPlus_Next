<!--
  LabelTextEditor — 文本编辑区

  功能：
  - 多行文本编辑框
  - 双向绑定选中标签的文本
  - 无选中标签时显示占位提示

  对应 Avalonia 端 TranslateView.axaml 中的 LabelTextBox
-->

<script setup lang="ts">
/* ---- Props ---- */
defineProps<{
  /** 当前编辑的文本 */
  modelValue: string
  /** 是否禁用（无选中标签时） */
  disabled: boolean
}>()

/* ---- Emits ---- */
const emit = defineEmits<{
  'update:modelValue': [value: string]
}>()

/** 输入事件处理 */
function onInput(e: Event) {
  const target = e.target as HTMLTextAreaElement
  emit('update:modelValue', target.value)
}
</script>

<template>
  <div class="label-text-editor">
    <textarea
      class="text-area"
      :value="modelValue"
      :disabled="disabled"
      :placeholder="disabled ? '请先选中一个标签' : '输入翻译文本...'"
      @input="onInput"
    />
  </div>
</template>

<style lang="scss" scoped>
@use '../styles/variables' as *;

.label-text-editor {
  display: flex;
  flex-direction: column;
  overflow: hidden;
}

.text-area {
  flex: 1;
  width: 100%;
  resize: none;
  border: none;
  outline: none;
  padding: $spacing-md;
  font-size: $font-size-xl;
  line-height: 1.6;
  background: var(--bg-panel);
  color: var(--text-primary);
  font-family: inherit;

  &::placeholder {
    color: var(--text-muted);
  }

  &:disabled {
    opacity: 0.5;
    cursor: not-allowed;
  }
}
</style>
