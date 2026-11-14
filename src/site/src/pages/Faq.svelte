<script lang="ts">
  import Icon from '../components/Icon.svelte'
  import PageHead from '../components/PageHead.svelte'
  import FaqList from '../components/FaqList.svelte'
  import { FAQ } from '../faq'
  import { LINKS } from '../data'
  import { reveal } from '../lib'
</script>

<PageHead en="FAQ" title="常见问题">安装、模拟器设置、识别和安全。没找到答案的话，到 QQ 群问或在 GitHub 提 Issue，附上设置里导出的诊断包最好。</PageHead>

<section class="wrap body">
  <nav class="toc" aria-label="分类">
    <span class="lbl">Index</span>
    {#each FAQ as g, i (g.id)}
      <a href={'#' + g.id}><span class="num">0{i + 1}</span>{g.title}</a>
    {/each}
    <div class="help">
      <a class="btn sm" href={LINKS.qq} target="_blank" rel="noopener"><Icon name="qq" size={16} />QQ 群 {LINKS.qqGroup}</a>
      <a class="btn sm ghost" href={LINKS.issues} target="_blank" rel="noopener"><Icon name="github" size={16} />提 Issue</a>
    </div>
  </nav>
  <div class="groups">
    {#each FAQ as g, i (g.id)}
      <div class="grp" id={g.id} use:reveal>
        <h2><span class="lbl"><b class="mint">0{i + 1}</b> · {g.en}</span>{g.title}</h2>
        <FaqList items={g.items} />
      </div>
    {/each}
  </div>
</section>

<style>
  .body {
    display: grid;
    grid-template-columns: 220px 1fr;
    gap: 56px;
    padding-top: 56px;
    padding-bottom: 40px;
  }
  .toc {
    position: sticky;
    top: calc(var(--head-h) + 32px);
    align-self: start;
    display: flex;
    flex-direction: column;
    gap: 2px;
  }
  .toc .lbl {
    margin-bottom: 10px;
  }
  .toc > a {
    display: flex;
    gap: 12px;
    padding: 8px 0;
    color: var(--fg-2);
    border-bottom: 1px solid var(--line-soft);
  }
  .toc > a:hover {
    color: var(--mint);
  }
  .toc .num {
    font-size: 12px;
    color: var(--fg-4);
  }
  .help {
    display: flex;
    flex-direction: column;
    gap: 8px;
    margin-top: 24px;
  }
  .groups {
    display: flex;
    flex-direction: column;
    gap: 56px;
    min-width: 0;
  }
  h2 {
    display: flex;
    flex-direction: column;
    gap: 4px;
    margin-bottom: 18px;
    font-size: var(--d-2);
  }
  @media (max-width: 900px) {
    .body {
      grid-template-columns: 1fr;
      gap: 32px;
    }
    .toc {
      position: static;
      flex-direction: row;
      flex-wrap: wrap;
      gap: 8px;
    }
    .toc .lbl {
      display: none;
    }
    .toc > a {
      padding: 6px 12px;
      border: 1px solid var(--line);
      font-size: 14px;
    }
    .help {
      flex-direction: row;
      margin-top: 0;
      width: 100%;
    }
  }
</style>
