<script lang="ts" module>
  // Cloudflare Turnstile 人机验证：脚本只加载一次，小部件按需渲染
  const SITE_KEY = '0x4AAAAAAF9mRM6aVkzCSsea'
  type TS = {
    render: (el: HTMLElement, o: Record<string, unknown>) => string
    reset: (id: string) => void
    remove: (id: string) => void
  }
  let loading: Promise<TS> | null = null
  function load(): Promise<TS> {
    const w = window as unknown as { turnstile?: TS }
    if (w.turnstile) return Promise.resolve(w.turnstile)
    return (loading ??= new Promise((resolve, reject) => {
      const s = document.createElement('script')
      s.src = 'https://challenges.cloudflare.com/turnstile/v0/api.js?render=explicit'
      s.async = true
      s.onload = () => resolve(w.turnstile!)
      s.onerror = () => reject(new Error('人机验证加载失败，检查一下网络再刷新页面。'))
      document.head.append(s)
    }))
  }
</script>

<script lang="ts">
  // token 变化时回调；用过一次的 token 作废，父组件调用 reset() 换新的
  let { ontoken }: { ontoken: (token: string | null) => void } = $props()
  let el: HTMLDivElement
  let wid = ''
  let ts: TS | null = null
  let error = $state('')

  export function reset() {
    ontoken(null)
    if (ts && wid) ts.reset(wid)
  }

  $effect(() => {
    load()
      .then((t) => {
        ts = t
        wid = t.render(el, {
          sitekey: SITE_KEY,
          theme: 'dark',
          language: 'zh-cn',
          callback: (token: string) => ontoken(token),
          'expired-callback': () => ontoken(null),
          'error-callback': () => ontoken(null),
        })
      })
      .catch((e) => (error = e.message))
    return () => ts && wid && ts.remove(wid)
  })
</script>

<div class="ts" bind:this={el}></div>
{#if error}<p class="err">{error}</p>{/if}

<style>
  .ts {
    min-height: 65px;
  }
</style>
