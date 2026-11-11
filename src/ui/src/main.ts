import { mount } from 'svelte'
import './app.css'
import App from './App.svelte'

import { api } from './lib/api'
import { app } from './lib/state.svelte'

mount(App, { target: document.getElementById('app')! })
// 挂载后立刻让宿主把原生骨架换成网页（此时只有标题栏，和骨架一致）。网页在宿主里先是隐藏的，
// 隐藏页面不触发 requestAnimationFrame，所以入场动画等页面变为可见、画出第一帧后再播
api.windowCommand('ready')
const enter = () => requestAnimationFrame(() => requestAnimationFrame(() => (app.phase = 'in')))
if (document.visibilityState === 'visible') enter()
else document.addEventListener('visibilitychange', enter, { once: true })
