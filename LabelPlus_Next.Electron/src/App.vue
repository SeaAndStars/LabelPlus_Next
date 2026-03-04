<!--
  App.vue — 应用根组件

  渲染 MainLayout 作为应用入口，并挂载全局 Toast 通知容器。
-->

<script setup lang="ts">
import MainLayout from './views/MainLayout.vue'
import ToastContainer from './components/ToastContainer.vue'
</script>

<template>
  <MainLayout />
  <ToastContainer />
</template>