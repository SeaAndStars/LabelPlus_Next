/**
 * App 根组件测试
 *
 * App.vue 已重构为渲染 MainLayout，
 * 此处仅验证根组件可正常挂载。
 */

import { describe, expect, it } from 'vitest'
import { mount } from '@vue/test-utils'
import App from '../src/App.vue'

describe('App 根组件', () => {
  it('可正常挂载渲染', () => {
    const wrapper = mount(App)
    expect(wrapper.exists()).toBe(true)
  })
})