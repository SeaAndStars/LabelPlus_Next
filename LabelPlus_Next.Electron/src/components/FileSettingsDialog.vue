<!--
  FileSettingsDialog — 文件设置弹窗

  功能：
  - 左栏：分组列表编辑（每行一个分组名）
  - 右栏：备注编辑
  - 底部：保存/取消按钮

  对应 Avalonia 端 TranslateView.axaml 中的 BrowserFileSettingsPanel
-->

<script setup lang="ts">
import { ref, onMounted } from 'vue'

/* ---- Props ---- */
const props = defineProps<{
  /** 当前分组列表 */
  groupList: string[]
  /** 当前备注 */
  comment: string
}>()

/* ---- Emits ---- */
const emit = defineEmits<{
  /** 保存设置 */
  'save': [groups: string[], comment: string]
  /** 取消关闭 */
  'cancel': []
}>()

/** 分组文本（每行一个） */
const groupText = ref('')
/** 备注文本 */
const noteText = ref('')

onMounted(() => {
  groupText.value = props.groupList.join('\n')
  noteText.value = props.comment
})

/** 保存设置 */
function save() {
  const groups = groupText.value
    .split('\n')
    .map(line => line.trim())
    .filter(line => line.length > 0)
  emit('save', groups, noteText.value.trim())
}
</script>

<template>
  <div
    class="dialog-backdrop"
    @click.self="emit('cancel')"
  >
    <div class="dialog-panel">
      <div class="dialog-body">
        <!-- 左栏：分组 -->
        <div class="edit-column">
          <h3 class="column-title">
            分组（每行一个）
          </h3>
          <textarea
            v-model="groupText"
            class="edit-area"
            placeholder="输入分组名称，每行一个..."
          />
        </div>

        <!-- 右栏：备注 -->
        <div class="edit-column">
          <h3 class="column-title">
            备注
          </h3>
          <textarea
            v-model="noteText"
            class="edit-area"
            placeholder="输入备注..."
          />
        </div>
      </div>

      <!-- 底部按钮 -->
      <div class="dialog-footer">
        <button
          class="btn-cancel"
          @click="emit('cancel')"
        >
          取消
        </button>
        <button
          class="btn-save"
          @click="save"
        >
          保存
        </button>
      </div>
    </div>
  </div>
</template>

<style lang="scss" scoped>
@use '../styles/variables' as *;
@use '../styles/mixins' as *;

.dialog-backdrop {
  @include overlay-backdrop;
}

.dialog-panel {
  width: 760px;
  max-height: 80vh;
  background: var(--bg-panel);
  border-radius: $radius-lg;
  padding: $spacing-lg;
  display: flex;
  flex-direction: column;
  gap: $spacing-md;
}

.dialog-body {
  display: flex;
  gap: $spacing-md;
  flex: 1;
  min-height: 350px;
}

.edit-column {
  flex: 1;
  display: flex;
  flex-direction: column;
}

.column-title {
  font-size: $font-size-lg;
  margin-bottom: $spacing-sm;
}

.edit-area {
  flex: 1;
  resize: none;
  border: 1px solid var(--border-default);
  border-radius: $radius-md;
  padding: $spacing-md;
  font-size: $font-size-md;
  background: var(--bg-panel);
  color: var(--text-primary);
  outline: none;

  &:focus {
    border-color: var(--border-active);
  }

  &::placeholder {
    color: var(--text-muted);
  }
}

.dialog-footer {
  display: flex;
  justify-content: flex-end;
  gap: $spacing-sm;
}

.btn-cancel {
  @include btn-default;
  min-width: 100px;
}

.btn-save {
  @include btn-primary;
  min-width: 100px;
}
</style>
