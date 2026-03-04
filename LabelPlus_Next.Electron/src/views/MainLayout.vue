<!--
  MainLayout — 主布局（侧边导航 + 内容区）

  功能：
  - 左侧导航菜单（翻译/校对/协作/交付/设置）
  - 导航可折叠/展开
  - 右侧动态内容区域

  对应 Avalonia 端 Views/MainView.axaml + MainView.axaml.cs
-->

<script setup lang="ts">
import { ref, shallowRef, markRaw } from 'vue'
import TranslateView from './TranslateView.vue'

/** 导航菜单项定义 */
interface NavItem {
  /** 标识键 */
  tag: string
  /** 显示文本 */
  label: string
  /** 图标（emoji） */
  icon: string
  /** 是否位于底部 */
  bottom?: boolean
}

/** 主导航菜单项 */
const mainNavItems: NavItem[] = [
  { tag: 'translate', label: '翻译', icon: '📝' },
  { tag: 'proof', label: '校对', icon: '✅' },
  { tag: 'teamwork', label: '协作', icon: '🤝' },
  { tag: 'deliver', label: '交付', icon: '📦' },
]

/** 底部导航菜单项 */
const bottomNavItems: NavItem[] = [
  { tag: 'settings', label: '设置', icon: '⚙', bottom: true },
]

/** 导航是否折叠 */
const navCollapsed = ref(false)

/** 当前选中的导航标签 */
const activeTag = ref('translate')

/** 当前显示的视图组件 */
const currentView = shallowRef(markRaw(TranslateView))

/** 占位文本（用于未实现的页面） */
const placeholderText = ref('')

/** 切换导航折叠状态 */
function toggleNav() {
  navCollapsed.value = !navCollapsed.value
}

/** 切换导航页面 */
function navigate(tag: string) {
  activeTag.value = tag
  switch (tag) {
    case 'translate':
      currentView.value = markRaw(TranslateView)
      placeholderText.value = ''
      break
    case 'proof':
      currentView.value = null as never
      placeholderText.value = '校对页面（开发中）'
      break
    case 'teamwork':
      currentView.value = null as never
      placeholderText.value = '协作页面（开发中）'
      break
    case 'deliver':
      currentView.value = null as never
      placeholderText.value = '交付页面（开发中）'
      break
    case 'settings':
      currentView.value = null as never
      placeholderText.value = '设置页面（开发中）'
      break
  }
}
</script>

<template>
  <div class="main-layout">
    <!-- 侧边导航 -->
    <nav :class="['side-nav', { collapsed: navCollapsed }]">
      <!-- 折叠切换按钮 -->
      <button
        class="nav-toggle"
        @click="toggleNav"
      >
        <span class="nav-icon">{{ navCollapsed ? '▶' : '◀' }}</span>
        <span
          v-if="!navCollapsed"
          class="nav-label"
        >{{ navCollapsed ? '展开' : '收起' }}</span>
      </button>

      <!-- 主导航 -->
      <div class="nav-main">
        <button
          v-for="item in mainNavItems"
          :key="item.tag"
          :class="['nav-item', { active: activeTag === item.tag }]"
          :title="item.label"
          @click="navigate(item.tag)"
        >
          <span class="nav-icon">{{ item.icon }}</span>
          <span
            v-if="!navCollapsed"
            class="nav-label"
          >{{ item.label }}</span>
        </button>
      </div>

      <!-- 底部导航 -->
      <div class="nav-bottom">
        <button
          v-for="item in bottomNavItems"
          :key="item.tag"
          :class="['nav-item', { active: activeTag === item.tag }]"
          :title="item.label"
          @click="navigate(item.tag)"
        >
          <span class="nav-icon">{{ item.icon }}</span>
          <span
            v-if="!navCollapsed"
            class="nav-label"
          >{{ item.label }}</span>
        </button>
      </div>
    </nav>

    <!-- 内容区域 -->
    <main class="content-area">
      <component
        :is="currentView"
        v-if="currentView"
      />
      <div
        v-else
        class="placeholder"
      >
        <p>{{ placeholderText }}</p>
      </div>
    </main>
  </div>
</template>

<style lang="scss" scoped>
@use '../styles/variables' as *;

.main-layout {
  display: flex;
  height: 100%;
  overflow: hidden;
}

/* ---- 侧边导航 ---- */
.side-nav {
  display: flex;
  flex-direction: column;
  width: $nav-width-expanded;
  background: var(--bg-panel);
  border-right: 1px solid var(--border-default);
  transition: width 0.2s ease;
  flex-shrink: 0;
  user-select: none;

  &.collapsed {
    width: $nav-width-collapsed;
  }
}

.nav-main {
  flex: 1;
  display: flex;
  flex-direction: column;
  gap: $spacing-xs;
  padding: $spacing-xs;
}

.nav-bottom {
  display: flex;
  flex-direction: column;
  gap: $spacing-xs;
  padding: $spacing-xs;
  border-top: 1px solid var(--border-default);
}

.nav-toggle,
.nav-item {
  display: flex;
  align-items: center;
  gap: $spacing-sm;
  width: 100%;
  border: none;
  background: none;
  padding: $spacing-sm;
  border-radius: $radius-md;
  cursor: pointer;
  color: var(--text-primary);
  font-size: $font-size-md;
  text-align: left;
  white-space: nowrap;

  &:hover {
    background: var(--bg-hover);
  }

  &.active {
    background: var(--bg-active);
    color: var(--accent);
    font-weight: 600;
  }
}

.nav-icon {
  flex-shrink: 0;
  width: 24px;
  text-align: center;
  font-size: 18px;
}

.nav-label {
  overflow: hidden;
  text-overflow: ellipsis;
}

/* ---- 内容区域 ---- */
.content-area {
  flex: 1;
  overflow: hidden;
}

.placeholder {
  display: flex;
  align-items: center;
  justify-content: center;
  height: 100%;
  color: var(--text-muted);
  font-size: $font-size-lg;
}
</style>
