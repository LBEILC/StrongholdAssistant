// 进入视口时加 .in（配合 site.css 的 .rv），只触发一次
const io =
  typeof IntersectionObserver === 'undefined'
    ? null
    : new IntersectionObserver(
        (entries) => {
          for (const e of entries) {
            if (!e.isIntersecting) continue
            e.target.classList.add('in')
            io!.unobserve(e.target)
          }
        },
        { rootMargin: '0px 0px -8% 0px' },
      )

export function reveal(node: HTMLElement, delay = 0) {
  node.classList.add('rv')
  if (delay) node.style.setProperty('--d', `${delay}ms`)
  if (!io) node.classList.add('in')
  else io.observe(node)
  return { destroy: () => io?.unobserve(node) }
}
