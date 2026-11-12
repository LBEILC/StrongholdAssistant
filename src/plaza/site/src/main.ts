import { mount } from 'svelte'
import '../../../ui/src/app.css'
import './site.css'
import App from './App.svelte'

mount(App, { target: document.getElementById('app')! })
