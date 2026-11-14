import { mount } from 'svelte'
import '../../ui/src/app.css'
import '../../plaza/site/src/assets/fonts/misans/index.css'
import './site.css'
import App from './App.svelte'
import type { PageId } from './data'

const el = document.getElementById('app')!
mount(App, { target: el, props: { page: el.dataset.page as PageId } })
