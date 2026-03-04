<!--
  ImageManagerDialog — 图片管理器弹窗

  功能：
  - 左侧：目录中可用（尚未添加）的图片列表
  - 右侧：已包含的图片列表
  - 中间：选择/取消选择按钮（>、>>、<、<<）
  - 底部：确定/取消按钮

  对应 Avalonia 端 TranslateView.axaml 中的 BrowserImageManagerPanel
-->

<script setup lang="ts">
import { ref, onMounted } from 'vue'

/* ---- Props ---- */
const props = defineProps<{
  /** 获取可用图片列表的异步函数 */
  getAvailableImages: () => Promise<string[]>
  /** 当前已包含的图片列表 */
  includedImages: string[]
}>()

/* ---- Emits ---- */
const emit = defineEmits<{
  /** 确认更新 */
  'confirm': [included: string[]]
  /** 取消关闭 */
  'cancel': []
}>()

/** 可用图片列表（未包含的） */
const available = ref<string[]>([])
/** 已包含图片列表（副本，编辑中） */
const included = ref<string[]>([])
/** 左侧选中项 */
const selectedAvailable = ref<string | null>(null)
/** 右侧选中项 */
const selectedIncluded = ref<string | null>(null)

onMounted(async () => {
  included.value = [...props.includedImages]
  available.value = await props.getAvailableImages()
})

/** 添加选中的一个 */
function selectOne() {
  if (!selectedAvailable.value) return
  included.value.push(selectedAvailable.value)
  available.value = available.value.filter(i => i !== selectedAvailable.value)
  selectedAvailable.value = null
}

/** 添加所有 */
function selectAll() {
  included.value.push(...available.value)
  available.value = []
  selectedAvailable.value = null
}

/** 移除选中的一个 */
function unselectOne() {
  if (!selectedIncluded.value) return
  available.value.push(selectedIncluded.value)
  available.value.sort()
  included.value = included.value.filter(i => i !== selectedIncluded.value)
  selectedIncluded.value = null
}

/** 移除所有 */
function unselectAll() {
  available.value.push(...included.value)
  available.value.sort()
  included.value = []
  selectedIncluded.value = null
}

/** 确认提交 */
function confirm() {
  emit('confirm', [...included.value])
}
</script>

<template>
  <div
    class="dialog-backdrop"
    @click.self="emit('cancel')"
  >
    <div class="dialog-panel">
      <div class="dialog-body">
        <!-- 左侧：可用图片 -->
        <div class="list-column">
          <h3 class="list-title">
            目录文件
          </h3>
          <ul class="file-list">
            <li
              v-for="name in available"
              :key="name"
              :class="{ selected: name === selectedAvailable }"
              @click="selectedAvailable = name"
            >
              {{ name }}
            </li>
          </ul>
        </div>

        <!-- 中间：操作按钮 -->
        <div class="action-column">
          <button
            class="action-btn"
            title="添加选中"
            @click="selectOne"
          >
            &gt;
          </button>
          <button
            class="action-btn"
            title="添加全部"
            @click="selectAll"
          >
            &gt;&gt;
          </button>
          <button
            class="action-btn"
            title="移除选中"
            @click="unselectOne"
          >
            &lt;
          </button>
          <button
            class="action-btn"
            title="移除全部"
            @click="unselectAll"
          >
            &lt;&lt;
          </button>
        </div>

        <!-- 右侧：已包含 -->
        <div class="list-column">
          <h3 class="list-title">
            已包含
          </h3>
          <ul class="file-list">
            <li
              v-for="name in included"
              :key="name"
              :class="{ selected: name === selectedIncluded }"
              @click="selectedIncluded = name"
            >
              {{ name }}
            </li>
          </ul>
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
          class="btn-confirm"
          @click="confirm"
        >
          确定
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
  width: 900px;
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
  min-height: 400px;
}

.list-column {
  flex: 1;
  display: flex;
  flex-direction: column;
}

.list-title {
  font-size: $font-size-lg;
  margin-bottom: $spacing-sm;
}

.file-list {
  flex: 1;
  overflow-y: auto;
  border: 1px solid var(--border-default);
  border-radius: $radius-md;
  padding: $spacing-xs;

  li {
    padding: $spacing-xs $spacing-sm;
    border-radius: $radius-sm;
    cursor: pointer;
    font-size: $font-size-md;

    &:hover {
      background: var(--bg-hover);
    }

    &.selected {
      background: var(--bg-active);
      color: var(--accent);
    }
  }
}

.action-column {
  display: flex;
  flex-direction: column;
  justify-content: center;
  gap: $spacing-sm;
}

.action-btn {
  @include btn-default;
  min-width: 60px;
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

.btn-confirm {
  @include btn-primary;
  min-width: 100px;
}
</style>
