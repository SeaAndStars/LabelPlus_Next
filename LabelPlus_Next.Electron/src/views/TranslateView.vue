<!--
  TranslateView — 翻译主视图

  整合所有翻译相关组件：
  - 顶部工具栏（TranslateToolbar）
  - 模式工具栏（ModeToolbar）
  - 左侧图片查看器（PicViewer）+ 图片导航栏
  - 右侧标签列表（LabelGrid）+ 文本编辑区（LabelTextEditor）
  - 弹窗：图片管理器 / 文件设置

  对应 Avalonia 端 Views/Pages/TranslateView.axaml + TranslateView.axaml.cs
-->

<script setup lang="ts">
import { ref, onUnmounted } from 'vue'
import { LabelCategory } from '../types/label'
import type { LabelItem } from '../types/label'
import { useTranslateState } from '../composables/useTranslateState'
import { useKeyboard } from '../composables/useKeyboard'

/* 子组件 */
import TranslateToolbar from '../components/TranslateToolbar.vue'
import ModeToolbar from '../components/ModeToolbar.vue'
import PicViewer from '../components/PicViewer.vue'
import LabelGrid from '../components/LabelGrid.vue'
import LabelTextEditor from '../components/LabelTextEditor.vue'
import ImageManagerDialog from '../components/ImageManagerDialog.vue'
import FileSettingsDialog from '../components/FileSettingsDialog.vue'

/* ---- 状态管理 ---- */
const state = useTranslateState()

/* ---- 弹窗控制 ---- */
const showImageManager = ref(false)
const showFileSettings = ref(false)
const showAbout = ref(false)

/* ---- PicViewer 引用 ---- */
const picViewerRef = ref<InstanceType<typeof PicViewer> | null>(null)

/** 当前缩放比例 */
const currentScale = ref(1.0)

/** 缩放选项 */
const scaleOptions = [0.25, 0.5, 1.0, 1.5, 2.0, 3.0]

/** 设置缩放 */
function setScale(value: number) {
  currentScale.value = value
  picViewerRef.value?.setScale(value)
}

/* ---- 标签操作回调 ---- */

/** PicViewer 中添加标签 */
function onAddLabelFromViewer(x: number, y: number, category: number) {
  state.addLabel(category, x, y)
}

/** PicViewer 中标签位置更新 */
function onLabelMoved(label: LabelItem, x: number, y: number) {
  state.updateLabelPosition(label, x, y)
}

/** 设置选中标签的分类 */
function setCategory(category: number) {
  if (state.selectedLabel.value) {
    state.setLabelCategory(state.selectedLabel.value, category)
  }
}

/* ---- 弹窗回调 ---- */

/** 图片管理器确认 */
function onImageManagerConfirm(included: string[]) {
  state.updateIncludedImages(included)
  showImageManager.value = false
}

/** 文件设置保存 */
function onFileSettingsSave(groups: string[], comment: string) {
  state.updateFileSettings(groups, comment)
  showFileSettings.value = false
}

/* ---- 主题切换 ---- */
function toggleTheme() {
  const html = document.documentElement
  const current = html.getAttribute('data-theme')
  html.setAttribute('data-theme', current === 'dark' ? 'light' : 'dark')
}

/* ---- 键盘快捷键 ---- */
useKeyboard({
  save: () => state.saveCurrent(),
  newFile: () => state.newTranslation(),
  openFile: () => state.openTranslation(),
  setCategoryInner: () => setCategory(LabelCategory.Inner),
  setCategoryOuter: () => setCategory(LabelCategory.Outer),
  undoRemove: () => state.undoRemoveLabel(),
  deleteLabel: () => state.removeLabel(),
})

/* ---- 生命周期 ---- */
onUnmounted(() => {
  state.stopAutoSave()
})
</script>

<template>
  <div class="translate-view">
    <!-- 顶部工具栏 -->
    <TranslateToolbar
      @new="state.newTranslation()"
      @open="state.openTranslation()"
      @save="state.saveCurrent()"
      @save-as="state.saveAs()"
      @open-folder="state.openTranslationFolder()"
      @image-manager="showImageManager = true"
      @toggle-theme="toggleTheme"
      @about="showAbout = true"
    />

    <!-- 模式工具栏 -->
    <ModeToolbar
      :mode="state.viewerMode.value"
      @update:mode="state.viewerMode.value = $event"
      @set-category="setCategory"
    />

    <!-- 主内容区域 -->
    <div class="main-content">
      <!-- 左侧：图片查看器 + 导航栏 -->
      <div class="left-panel">
        <PicViewer
          ref="picViewerRef"
          :image-src="state.currentImageSrc.value"
          :labels="state.currentLabels.value"
          :selected-label="state.selectedLabel.value"
          :mode="state.viewerMode.value"
          @update:selected-label="state.selectedLabel.value = $event"
          @add-label="onAddLabelFromViewer"
          @label-moved="onLabelMoved"
        />

        <!-- 图片导航栏 -->
        <div class="image-nav no-select">
          <div class="nav-left">
            <span class="nav-label">缩放:</span>
            <select
              class="scale-select"
              :value="currentScale"
              @change="setScale(Number(($event.target as HTMLSelectElement).value))"
            >
              <option
                v-for="s in scaleOptions"
                :key="s"
                :value="s"
              >
                {{ Math.round(s * 100) }}%
              </option>
            </select>
          </div>
          <div class="nav-right">
            <select
              class="image-select"
              :value="state.selectedImageFile.value ?? ''"
              @change="state.selectedImageFile.value = ($event.target as HTMLSelectElement).value || null"
            >
              <option
                value=""
                disabled
              >
                选择图片...
              </option>
              <option
                v-for="name in state.imageFileNames.value"
                :key="name"
                :value="name"
              >
                {{ name }}
              </option>
            </select>
            <button
              class="nav-btn"
              title="上一张"
              @click="state.prevImage()"
            >
              ←
            </button>
            <button
              class="nav-btn"
              title="下一张"
              @click="state.nextImage()"
            >
              →
            </button>
          </div>
        </div>
      </div>

      <!-- 分隔线 -->
      <div class="divider" />

      <!-- 右侧：标签列表 + 文本编辑 -->
      <div class="right-panel">
        <LabelGrid
          :labels="state.currentLabels.value"
          :selected-label="state.selectedLabel.value"
          @update:selected-label="state.selectedLabel.value = $event"
          @add="state.addLabel()"
          @remove="state.removeLabel()"
          @undo="state.undoRemoveLabel()"
          @file-settings="showFileSettings = true"
          @reorder="state.moveLabel"
        />

        <!-- 标签列表与文本编辑区的分隔线 -->
        <div class="horizontal-divider" />

        <LabelTextEditor
          :model-value="state.currentText.value"
          :disabled="!state.selectedLabel.value"
          @update:model-value="state.currentText.value = $event"
        />
      </div>
    </div>

    <!-- 状态栏 -->
    <div class="status-bar no-select">
      <span
        v-if="state.isDirty.value"
        class="status-dirty"
      >● 未保存</span>
      <span
        v-else
        class="status-saved"
      >● 已保存</span>
      <span
        v-if="state.openFilePath.value"
        class="status-path"
      >
        {{ state.openFilePath.value }}
      </span>
    </div>

    <!-- ---- 弹窗 ---- -->

    <!-- 图片管理器 -->
    <ImageManagerDialog
      v-if="showImageManager"
      :get-available-images="state.getAvailableImages"
      :included-images="state.imageFileNames.value"
      @confirm="onImageManagerConfirm"
      @cancel="showImageManager = false"
    />

    <!-- 文件设置 -->
    <FileSettingsDialog
      v-if="showFileSettings"
      :group-list="state.groupList.value"
      :comment="state.fileComment.value"
      @save="onFileSettingsSave"
      @cancel="showFileSettings = false"
    />

    <!-- 关于弹窗 -->
    <div
      v-if="showAbout"
      class="about-backdrop"
      @click.self="showAbout = false"
    >
      <div class="about-panel">
        <h2>LabelPlus Next</h2>
        <p>翻译标注工具 — Electron 版</p>
        <button
          class="about-close"
          @click="showAbout = false"
        >
          关闭
        </button>
      </div>
    </div>
  </div>
</template>

<style lang="scss" scoped>
@use '../styles/variables' as *;
@use '../styles/mixins' as *;

.translate-view {
  display: flex;
  flex-direction: column;
  height: 100%;
  overflow: hidden;
}

/* ---- 主内容区域 ---- */
.main-content {
  flex: 1;
  display: flex;
  overflow: hidden;
}

/* 左侧面板：图片查看器 */
.left-panel {
  flex: 1;
  display: flex;
  flex-direction: column;
  min-width: 300px;
}

/* 竖向分隔线 */
.divider {
  width: 2px;
  background: var(--accent);
  flex-shrink: 0;
}

/* 右侧面板：标签列表 + 文本编辑 */
.right-panel {
  flex: 1;
  display: flex;
  flex-direction: column;
  min-width: 300px;
  max-width: 480px;
  background: var(--bg-panel);
  overflow: hidden;

  /* LabelGrid 占上方空间，超出滚动 */
  :deep(.label-grid) {
    flex: 1;
    min-height: 0;
    overflow: hidden;
  }

  /* 文本编辑区固定最小高度，不被挤压 */
  :deep(.label-text-editor) {
    flex-shrink: 0;
    min-height: 160px;
    max-height: 280px;
  }
}

/* 横向分隔线 */
.horizontal-divider {
  height: 2px;
  background: var(--accent);
  flex-shrink: 0;
}

/* ---- 图片导航栏 ---- */
.image-nav {
  @include toolbar;
  border-top: 1px solid var(--border-default);
  padding: $spacing-xs $spacing-sm;
  flex-shrink: 0;
}

.nav-left {
  display: flex;
  align-items: center;
  gap: $spacing-sm;
}

.nav-label {
  font-size: $font-size-md;
  color: var(--text-secondary);
}

.scale-select,
.image-select {
  padding: $spacing-xs $spacing-sm;
  border: 1px solid var(--border-default);
  border-radius: $radius-md;
  background: var(--bg-panel);
  color: var(--text-primary);
  font-size: $font-size-md;
}

.image-select {
  min-width: 160px;
}

.nav-right {
  margin-left: auto;
  display: flex;
  align-items: center;
  gap: $spacing-sm;
}

.nav-btn {
  @include btn-default;
  padding: $spacing-xs $spacing-sm;
  min-width: 36px;
}

/* ---- 状态栏 ---- */
.status-bar {
  @include toolbar;
  height: 28px;
  padding: 0 $spacing-md;
  background: var(--bg-panel);
  border-top: 1px solid var(--border-default);
  font-size: $font-size-sm;
  color: var(--text-secondary);
  gap: $spacing-md;
}

.status-dirty {
  color: #d97706;
}

.status-saved {
  color: #16a34a;
}

.status-path {
  @include text-ellipsis;
  flex: 1;
}

/* ---- 关于弹窗 ---- */
.about-backdrop {
  @include overlay-backdrop;
}

.about-panel {
  background: var(--bg-panel);
  border-radius: $radius-lg;
  padding: $spacing-xl;
  text-align: center;
  min-width: 300px;

  h2 {
    margin-bottom: $spacing-sm;
  }

  p {
    color: var(--text-secondary);
    margin-bottom: $spacing-lg;
  }
}

.about-close {
  @include btn-primary;
}
</style>
