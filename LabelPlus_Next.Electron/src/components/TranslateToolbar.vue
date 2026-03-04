<!--
  TranslateToolbar — 顶部主工具栏

  功能：
  - 文件菜单（新建/打开/保存/另存为/打开文件夹）
  - 图片管理按钮
  - 输出按钮
  - 帮助按钮
  - 主题切换

  对应 Avalonia 端 TranslateView.axaml 中的顶部 ToolBar
-->

<script setup lang="ts">
import { ref } from 'vue'

/* ---- Emits ---- */
const emit = defineEmits<{
  'new': []
  'open': []
  'save': []
  'save-as': []
  'open-folder': []
  'image-manager': []
  'toggle-theme': []
  'about': []
}>()

/** 文件菜单是否展开 */
const showFileMenu = ref(false)

/** 切换文件菜单 */
function toggleFileMenu() {
  showFileMenu.value = !showFileMenu.value
}

/** 点击菜单项后关闭菜单 */
function menuAction(action: () => void) {
  showFileMenu.value = false
  action()
}
</script>

<template>
  <div class="translate-toolbar no-select">
    <!-- 文件菜单 -->
    <div class="menu-wrapper">
      <button
        class="toolbar-btn"
        @click="toggleFileMenu"
      >
        文件
      </button>
      <div
        v-if="showFileMenu"
        class="dropdown-menu"
        @mouseleave="showFileMenu = false"
      >
        <button
          class="menu-item"
          @click="menuAction(() => emit('new'))"
        >
          新建
        </button>
        <div class="menu-divider" />
        <button
          class="menu-item"
          @click="menuAction(() => emit('open'))"
        >
          打开
        </button>
        <div class="menu-divider" />
        <button
          class="menu-item"
          @click="menuAction(() => emit('save'))"
        >
          保存
        </button>
        <div class="menu-divider" />
        <button
          class="menu-item"
          @click="menuAction(() => emit('save-as'))"
        >
          另存为
        </button>
        <div class="menu-divider" />
        <button
          class="menu-item"
          @click="menuAction(() => emit('open-folder'))"
        >
          打开翻译文件夹
        </button>
      </div>
    </div>

    <span class="separator" />

    <!-- 图片管理 -->
    <button
      class="toolbar-btn"
      @click="emit('image-manager')"
    >
      图片
    </button>
    <span class="separator" />

    <!-- 输出（暂未实现） -->
    <button
      class="toolbar-btn"
      disabled
    >
      输出
    </button>
    <span class="separator" />

    <!-- 帮助 -->
    <button
      class="toolbar-btn"
      @click="emit('about')"
    >
      帮助
    </button>
    <span class="separator" />

    <!-- 右侧区域 -->
    <div class="toolbar-spacer" />

    <!-- 主题切换 -->
    <button
      class="toolbar-btn theme-btn"
      @click="emit('toggle-theme')"
    >
      🌓
    </button>
  </div>
</template>

<style lang="scss" scoped>
@use '../styles/variables' as *;
@use '../styles/mixins' as *;

.translate-toolbar {
  @include toolbar;
  background: var(--bg-panel);
  border-bottom: 2px solid var(--accent);
  padding: 0 $spacing-md;
}

.toolbar-btn {
  @include btn-default;
  border: none;
  background: transparent;
  padding: $spacing-xs $spacing-sm;

  &:hover {
    background: var(--bg-hover);
  }

  &:disabled {
    opacity: 0.5;
    cursor: not-allowed;
  }
}

.separator {
  @include toolbar-separator;
}

.toolbar-spacer {
  flex: 1;
}

/* ---- 下拉菜单 ---- */
.menu-wrapper {
  position: relative;
}

.dropdown-menu {
  position: absolute;
  top: 100%;
  left: 0;
  z-index: 50;
  min-width: 160px;
  background: var(--bg-panel);
  border: 1px solid var(--border-default);
  border-radius: $radius-md;
  box-shadow: 0 4px 12px rgba(0, 0, 0, 0.15);
  padding: $spacing-xs 0;
}

.menu-item {
  display: block;
  width: 100%;
  text-align: left;
  border: none;
  background: none;
  padding: $spacing-sm $spacing-md;
  font-size: $font-size-md;
  color: var(--text-primary);
  cursor: pointer;

  &:hover {
    background: var(--bg-hover);
  }
}

.menu-divider {
  height: 1px;
  background: var(--border-default);
  margin: $spacing-xs 0;
}

.theme-btn {
  font-size: 18px;
}
</style>
