<!--
  PicViewer — Canvas 图片查看器

  功能：
  - Canvas 渲染图片 + 标签叠加
  - 鼠标滚轮缩放 / 拖拽平移
  - 标签编号圆点绘制（按分类着色）
  - 选中标签高亮
  - 浏览模式：点击选中标签
  - 标签编辑模式：点击空白添加标签，拖拽移动标签

  对应 Avalonia 端 CustomControls/PicViewer.cs
-->

<script setup lang="ts">
import { ref, watch, onMounted, onUnmounted } from 'vue'
import type { LabelItem } from '../types/label'
import { ViewerMode, CATEGORY_COLORS, LabelCategory } from '../types/label'

/* ---- Props ---- */
const props = defineProps<{
  /** 图片 Data URL */
  imageSrc: string
  /** 当前图片的标签列表 */
  labels: LabelItem[]
  /** 当前选中的标签 */
  selectedLabel: LabelItem | null
  /** 查看器模式 */
  mode: ViewerMode
}>()

/* ---- Emits ---- */
const emit = defineEmits<{
  /** 选中标签变更 */
  'update:selectedLabel': [label: LabelItem | null]
  /** 请求添加标签（传入百分比坐标和分类） */
  'add-label': [x: number, y: number, category: number]
  /** 标签位置更新（拖拽后） */
  'label-moved': [label: LabelItem, x: number, y: number]
}>()

/* ---- 响应式状态 ---- */

/** Canvas DOM 引用 */
const canvasRef = ref<HTMLCanvasElement | null>(null)

/** 容器 DOM 引用 */
const containerRef = ref<HTMLDivElement | null>(null)

/** 缩放比例 */
const scale = ref(1.0)

/** 平移偏移量 */
const translateX = ref(0)
const translateY = ref(0)

/** 图片对象（缓存） */
let imgElement: HTMLImageElement | null = null

/** 图片是否已加载 */
const imageLoaded = ref(false)

/** 拖拽状态 */
let isDragging = false
let hasDragMoved = false
let dragStartX = 0
let dragStartY = 0
let dragStartTransX = 0
let dragStartTransY = 0

/** 标签拖拽状态 */
let isLabelDragging = false
let draggingLabel: LabelItem | null = null

/** 当前鼠标悬停的标签（用于绘制悬浮高亮） */
let hoveredLabel: LabelItem | null = null

/** 标签圆点半径（像素） */
const LABEL_RADIUS = 12

/** 可用的缩放档位（供外部组件引用） */
// eslint-disable-next-line @typescript-eslint/no-unused-vars
const SCALE_OPTIONS = [0.25, 0.5, 1.0, 1.5, 2.0, 3.0]

/* ---- 绘制逻辑 ---- */

/**
 * 重绘 Canvas
 *
 * 绘制顺序：清空 → 图片 → 标签圆点 → 选中高亮
 */
function draw() {
  const canvas = canvasRef.value
  if (!canvas) return
  const ctx = canvas.getContext('2d')
  if (!ctx) return

  const { width, height } = canvas

  /* 清空画布 */
  ctx.clearRect(0, 0, width, height)

  if (!imgElement || !imageLoaded.value) return

  /* 计算图片绘制区域（居中适配） */
  const imgW = imgElement.naturalWidth * scale.value
  const imgH = imgElement.naturalHeight * scale.value
  const drawX = (width - imgW) / 2 + translateX.value
  const drawY = (height - imgH) / 2 + translateY.value

  /* 绘制图片 */
  ctx.drawImage(imgElement, drawX, drawY, imgW, imgH)

  /* 绘制标签圆点 */
  for (const label of props.labels) {
    const lx = drawX + label.xPercent * imgW
    const ly = drawY + label.yPercent * imgH
    const color = CATEGORY_COLORS[label.category] ?? '#888'
    const isSelected = props.selectedLabel != null && label.index === props.selectedLabel.index
    const isHovered = label === hoveredLabel && !isSelected

    ctx.save()

    /* 选中标签：多层光晕 */
    if (isSelected) {
      /* 外层柔和光晕（大范围） */
      const gradient = ctx.createRadialGradient(lx, ly, LABEL_RADIUS, lx, ly, LABEL_RADIUS + 16)
      gradient.addColorStop(0, 'rgba(9, 105, 218, 0.35)')
      gradient.addColorStop(1, 'rgba(9, 105, 218, 0)')
      ctx.beginPath()
      ctx.arc(lx, ly, LABEL_RADIUS + 16, 0, Math.PI * 2)
      ctx.fillStyle = gradient
      ctx.fill()

      /* 中层高亮光环 */
      ctx.beginPath()
      ctx.arc(lx, ly, LABEL_RADIUS + 5, 0, Math.PI * 2)
      ctx.strokeStyle = 'rgba(9, 105, 218, 0.7)'
      ctx.lineWidth = 2.5
      ctx.stroke()

      /* 内层白色高亮环 */
      ctx.beginPath()
      ctx.arc(lx, ly, LABEL_RADIUS + 1, 0, Math.PI * 2)
      ctx.strokeStyle = 'rgba(255, 255, 255, 0.9)'
      ctx.lineWidth = 2
      ctx.stroke()
    }

    /* 悬浮标签：半透明高亮环 + 放大效果 */
    if (isHovered) {
      ctx.beginPath()
      ctx.arc(lx, ly, LABEL_RADIUS + 6, 0, Math.PI * 2)
      ctx.strokeStyle = 'rgba(255, 255, 255, 0.5)'
      ctx.lineWidth = 2
      ctx.stroke()
    }

    /* 阴影效果 */
    if (isSelected) {
      ctx.shadowColor = 'rgba(9, 105, 218, 0.6)'
      ctx.shadowBlur = 10
    } else if (isHovered) {
      ctx.shadowColor = 'rgba(255, 255, 255, 0.5)'
      ctx.shadowBlur = 8
    } else {
      ctx.shadowColor = 'rgba(0, 0, 0, 0.3)'
      ctx.shadowBlur = 4
      ctx.shadowOffsetX = 1
      ctx.shadowOffsetY = 1
    }

    /* 标签圆点（悬浮时略微放大） */
    const drawRadius = isHovered ? LABEL_RADIUS + 2 : LABEL_RADIUS
    ctx.beginPath()
    ctx.arc(lx, ly, drawRadius, 0, Math.PI * 2)
    ctx.fillStyle = color
    ctx.globalAlpha = isSelected || isHovered ? 1.0 : 0.75
    ctx.fill()
    ctx.globalAlpha = 1.0

    /* 圆点边框 */
    ctx.shadowColor = 'transparent'
    ctx.shadowBlur = 0
    ctx.shadowOffsetX = 0
    ctx.shadowOffsetY = 0
    ctx.strokeStyle = isSelected ? '#fff' : isHovered ? 'rgba(255,255,255,0.95)' : 'rgba(255,255,255,0.85)'
    ctx.lineWidth = isSelected ? 2.5 : isHovered ? 2 : 1.5
    ctx.stroke()

    /* 编号文本 */
    ctx.fillStyle = '#fff'
    ctx.font = isSelected || isHovered ? 'bold 13px sans-serif' : 'bold 11px sans-serif'
    ctx.textAlign = 'center'
    ctx.textBaseline = 'middle'
    ctx.fillText(String(label.index), lx, ly)

    ctx.restore()
  }
}

/**
 * 根据鼠标位置查找最近的标签
 *
 * @param clientX - 鼠标 X 坐标（相对视口）
 * @param clientY - 鼠标 Y 坐标（相对视口）
 * @returns 命中的标签，未命中返回 null
 */
function hitTestLabel(clientX: number, clientY: number): LabelItem | null {
  const canvas = canvasRef.value
  if (!canvas || !imgElement || !imageLoaded.value) return null

  const rect = canvas.getBoundingClientRect()
  const mx = clientX - rect.left
  const my = clientY - rect.top

  const imgW = imgElement.naturalWidth * scale.value
  const imgH = imgElement.naturalHeight * scale.value
  const drawX = (canvas.width - imgW) / 2 + translateX.value
  const drawY = (canvas.height - imgH) / 2 + translateY.value

  /* 从后往前检测（后绘制的在上层） */
  for (let i = props.labels.length - 1; i >= 0; i--) {
    const label = props.labels[i]
    const lx = drawX + label.xPercent * imgW
    const ly = drawY + label.yPercent * imgH
    const dist = Math.sqrt((mx - lx) ** 2 + (my - ly) ** 2)
    if (dist <= LABEL_RADIUS + 4) {
      return label
    }
  }
  return null
}

/**
 * 将鼠标坐标转换为图片上的百分比坐标
 */
function clientToImagePercent(clientX: number, clientY: number): { x: number; y: number } | null {
  const canvas = canvasRef.value
  if (!canvas || !imgElement || !imageLoaded.value) return null

  const rect = canvas.getBoundingClientRect()
  const mx = clientX - rect.left
  const my = clientY - rect.top

  const imgW = imgElement.naturalWidth * scale.value
  const imgH = imgElement.naturalHeight * scale.value
  const drawX = (canvas.width - imgW) / 2 + translateX.value
  const drawY = (canvas.height - imgH) / 2 + translateY.value

  const x = (mx - drawX) / imgW
  const y = (my - drawY) / imgH

  /* 限制在图片范围内 */
  if (x < 0 || x > 1 || y < 0 || y > 1) return null
  return { x, y }
}

/* ---- 事件处理 ---- */

/**
 * 鼠标按下
 *
 * 左键（button=0）：选中/拖拽标签 或 开始平移画布
 * 右键（button=2）：标签模式下添加框外标签
 */
function onMouseDown(e: MouseEvent) {
  /* 右键：标签模式下添加框外标签 */
  if (e.button === 2 && props.mode === ViewerMode.Label) {
    e.preventDefault()
    const pos = clientToImagePercent(e.clientX, e.clientY)
    if (pos && !hitTestLabel(e.clientX, e.clientY)) {
      emit('add-label', pos.x, pos.y, LabelCategory.Outer)
    }
    return
  }

  if (e.button !== 0) return

  /* 标签编辑模式：检测标签拖拽 */
  if (props.mode === ViewerMode.Label) {
    const hit = hitTestLabel(e.clientX, e.clientY)
    if (hit) {
      isLabelDragging = true
      draggingLabel = hit
      emit('update:selectedLabel', hit)
      return
    }
  }

  /* 浏览/标签模式：点选标签 */
  const hit = hitTestLabel(e.clientX, e.clientY)
  if (hit) {
    emit('update:selectedLabel', hit)
  }

  /* 开始拖拽平移（记录起点，稍后判断是否实际移动） */
  isDragging = true
  hasDragMoved = false
  dragStartX = e.clientX
  dragStartY = e.clientY
  dragStartTransX = translateX.value
  dragStartTransY = translateY.value
}

/** 鼠标移动 */
function onMouseMove(e: MouseEvent) {
  /* 标签拖拽 */
  if (isLabelDragging && draggingLabel) {
    const pos = clientToImagePercent(e.clientX, e.clientY)
    if (pos) {
      emit('label-moved', draggingLabel, pos.x, pos.y)
      draw()
    }
    return
  }

  /* 画布平移（超过 3px 才视为拖拽，避免误判点击） */
  if (isDragging) {
    const dx = e.clientX - dragStartX
    const dy = e.clientY - dragStartY
    if (!hasDragMoved && Math.abs(dx) + Math.abs(dy) > 3) {
      hasDragMoved = true
    }
    if (hasDragMoved) {
      translateX.value = dragStartTransX + dx
      translateY.value = dragStartTransY + dy
      draw()
    }
    return
  }

  /* 悬浮检测：更新 hoveredLabel 并切换鼠标指针 */
  const hit = hitTestLabel(e.clientX, e.clientY)
  if (hit !== hoveredLabel) {
    hoveredLabel = hit
    const canvas = canvasRef.value
    if (canvas) {
      canvas.style.cursor = hit ? 'pointer' : 'default'
    }
    draw()
  }
}

/**
 * 鼠标松开
 *
 * 标签模式下：如果既没有拖拽标签也没有平移画布，
 * 视为一次"点击空白"操作 → 添加框内标签。
 */
function onMouseUp(e: MouseEvent) {
  if (e.button !== 0) {
    return
  }

  /* 标签模式：点击空白处添加框内标签 */
  if (
    props.mode === ViewerMode.Label &&
    !isLabelDragging &&
    !hasDragMoved
  ) {
    const pos = clientToImagePercent(e.clientX, e.clientY)
    if (pos && !hitTestLabel(e.clientX, e.clientY)) {
      emit('add-label', pos.x, pos.y, LabelCategory.Inner)
    }
  }

  isLabelDragging = false
  draggingLabel = null
  isDragging = false
  hasDragMoved = false
}

/** 禁用右键菜单（标签模式下右键用于添加框外标签） */
function onContextMenu(e: MouseEvent) {
  if (props.mode === ViewerMode.Label) {
    e.preventDefault()
  }
}

/**
 * 鼠标滚轮缩放（以鼠标位置为中心）
 *
 * 计算缩放前后鼠标对应的世界坐标偏移量，
 * 调整 translateX/Y 使鼠标下方的图片内容保持不动。
 */
function onWheel(e: WheelEvent) {
  e.preventDefault()
  const canvas = canvasRef.value
  if (!canvas || !imgElement) return

  const rect = canvas.getBoundingClientRect()
  /* 鼠标在 canvas 中的位置 */
  const mouseX = e.clientX - rect.left
  const mouseY = e.clientY - rect.top

  const oldScale = scale.value
  const delta = e.deltaY > 0 ? -0.1 : 0.1
  const newScale = Math.max(0.1, Math.min(5.0, oldScale + delta))

  /* 缩放前：鼠标位置对应的图片世界坐标 */
  const cw = canvas.width
  const ch = canvas.height
  const worldX = (mouseX - cw / 2 - translateX.value) / oldScale
  const worldY = (mouseY - ch / 2 - translateY.value) / oldScale

  /* 缩放后：调整平移使同一世界坐标仍在鼠标下方 */
  translateX.value = mouseX - cw / 2 - worldX * newScale
  translateY.value = mouseY - ch / 2 - worldY * newScale

  scale.value = newScale
  draw()
}

/* ---- Canvas 尺寸自适应 ---- */

/** 调整 Canvas 尺寸以适配容器 */
function resizeCanvas() {
  const canvas = canvasRef.value
  const container = containerRef.value
  if (!canvas || !container) return

  const rect = container.getBoundingClientRect()
  const dpr = window.devicePixelRatio || 1

  canvas.width = rect.width * dpr
  canvas.height = rect.height * dpr
  canvas.style.width = `${rect.width}px`
  canvas.style.height = `${rect.height}px`

  const ctx = canvas.getContext('2d')
  if (ctx) ctx.scale(dpr, dpr)

  /* 重新调整内部尺寸以匹配 CSS 尺寸 */
  canvas.width = rect.width
  canvas.height = rect.height

  draw()
}

let resizeObserver: ResizeObserver | null = null

/* ---- 图片加载 ---- */

/**
 * 加载/更换图片
 */
function loadImageFromSrc(src: string) {
  if (!src) {
    imgElement = null
    imageLoaded.value = false
    draw()
    return
  }
  const img = new Image()
  img.onload = () => {
    imgElement = img
    imageLoaded.value = true
    /* 重置视图 */
    translateX.value = 0
    translateY.value = 0
    scale.value = 1.0
    draw()
  }
  img.onerror = () => {
    imgElement = null
    imageLoaded.value = false
    draw()
  }
  img.src = src
}

/* ---- 生命周期 ---- */

onMounted(() => {
  resizeCanvas()
  resizeObserver = new ResizeObserver(() => resizeCanvas())
  if (containerRef.value) {
    resizeObserver.observe(containerRef.value)
  }
  if (props.imageSrc) {
    loadImageFromSrc(props.imageSrc)
  }
})

onUnmounted(() => {
  resizeObserver?.disconnect()
})

/* ---- 监听 Props 变化 ---- */

watch(() => props.imageSrc, (src) => loadImageFromSrc(src))
watch(() => props.labels, () => draw(), { deep: true })
watch(() => props.selectedLabel, () => draw())
watch(scale, () => draw())

/**
 * 设置缩放比例（供外部 ComboBox 调用）
 */
function setScale(newScale: number) {
  scale.value = newScale
}

defineExpose({ scale, setScale })
</script>

<template>
  <!-- 图片查看器容器 -->
  <div
    ref="containerRef"
    class="pic-viewer"
  >
    <canvas
      ref="canvasRef"
      class="pic-canvas"
      @mousedown="onMouseDown"
      @mousemove="onMouseMove"
      @mouseup="onMouseUp"
      @mouseleave="onMouseUp"
      @wheel="onWheel"
      @contextmenu="onContextMenu"
    />
    <!-- 无图片时的占位提示 -->
    <div
      v-if="!imageSrc"
      class="pic-placeholder"
    >
      <span>暂无图片</span>
    </div>
  </div>
</template>

<style lang="scss" scoped>
@use '../styles/variables' as *;

.pic-viewer {
  position: relative;
  width: 100%;
  height: 100%;
  overflow: hidden;
  background: var(--bg-primary);
  border-radius: $radius-md;
}

.pic-canvas {
  width: 100%;
  height: 100%;
  cursor: default;
}

.pic-placeholder {
  position: absolute;
  inset: 0;
  display: flex;
  align-items: center;
  justify-content: center;
  color: var(--text-muted);
  font-size: $font-size-lg;
  pointer-events: none;
}
</style>
