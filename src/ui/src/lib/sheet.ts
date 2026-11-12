// 面板进出动画：侧栏里从底部升起；工作台里面板停在右边，从右边滑入
import { fly } from 'svelte/transition'
import { cubicOut } from 'svelte/easing'
import { app } from './state.svelte'

export function sheet(node: Element, { y, duration }: { y: number; duration: number }) {
  return fly(node, app.workbench ? { x: 360, duration, easing: cubicOut } : { y, duration, easing: cubicOut })
}
