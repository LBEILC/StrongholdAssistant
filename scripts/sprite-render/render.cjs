// PRTS 元数据决定皮肤及朝向；只读取资源，不运行资源中的代码。
const fs = require('node:fs');
const path = require('node:path');
const readline = require('node:readline');
const { createCanvas, loadImage } = require('@napi-rs/canvas');
const { BaseTexture, TextureMatrix } = require('@pixi/core');
const { TextureAtlas, settings } = require('@pixi-spine/base');
settings.yDown = false;
const { SkeletonBinary, AtlasAttachmentLoader, Skeleton, AnimationState, AnimationStateData,
  RegionAttachment, MeshAttachment, ClippingAttachment } = require('@pixi-spine/runtime-3.8');
const root = path.resolve(__dirname, '../..');
const args = process.argv.slice(2);
const option = (key, fallback) => args.includes(key) ? args[args.indexOf(key) + 1] : fallback;
const output = path.resolve(option('--out', path.join(root, 'out/214/rendered')));
const assets = path.join(root, 'docs/research/refs/spine');
// 所有干员、皮肤和帧共用骨骼单位比例，禁止按各自外框归一。
const pixelsPerUnit = 0.5;
const read = p => JSON.parse(fs.readFileSync(p, 'utf8').replace(/^\uFEFF/, ''));
const summonOnly = args.includes('--summons');
const prototypeOnly = args.includes('--prototypes') || summonOnly;
const prototypes = summonOnly ? read(path.join(root, 'data/summons.json')).summons.map(s => ({ ...s, name: s.charId }))
  : prototypeOnly ? read(path.join(root, 'data/prototypes.json')).prototypes : [];
const operators = prototypeOnly ? prototypes : read(path.join(root, 'data/operators.json')).operators;
const extra = prototypeOnly ? Object.fromEntries(prototypes.map(p => [p.name, { ...p, skins: [] }]))
  : read(path.join(root, 'data/codex_extra.json')).operators;
fs.mkdirSync(output, { recursive: true });
fs.mkdirSync(assets, { recursive: true });

async function download(url, file) {
  if (fs.existsSync(file)) return fs.readFileSync(file);
  const response = await fetch(url, { signal: AbortSignal.timeout(30000) });
  if (!response.ok) throw Error(`${response.status}: ${url}`);
  const bytes = Buffer.from(await response.arrayBuffer());
  fs.mkdirSync(path.dirname(file), { recursive: true });
  fs.writeFileSync(file, bytes);
  return bytes;
}

// 仿射映射 atlas 三角形到骨骼顶点，保留模型的网格变形和绘制顺序。
function triangle(ctx, image, vertices, uvs, ids) {
  const [i, j, k] = ids.map(n => n * 2);
  const x0 = vertices[i], y0 = -vertices[i + 1];
  const x1 = vertices[j], y1 = -vertices[j + 1];
  const x2 = vertices[k], y2 = -vertices[k + 1];
  const u0 = uvs[i] * image.width, v0 = uvs[i + 1] * image.height;
  const u1 = uvs[j] * image.width, v1 = uvs[j + 1] * image.height;
  const u2 = uvs[k] * image.width, v2 = uvs[k + 1] * image.height;
  const det = (u1-u0)*(v2-v0)-(u2-u0)*(v1-v0);
  if (Math.abs(det) < 1e-8) return;
  const a = ((x1-x0)*(v2-v0)-(x2-x0)*(v1-v0))/det;
  const b = ((y1-y0)*(v2-v0)-(y2-y0)*(v1-v0))/det;
  const c = ((x2-x0)*(u1-u0)-(x1-x0)*(u2-u0))/det;
  const d = ((y2-y0)*(u1-u0)-(y1-y0)*(u2-u0))/det;
  ctx.save(); ctx.beginPath(); ctx.moveTo(x0,y0); ctx.lineTo(x1,y1); ctx.lineTo(x2,y2); ctx.closePath(); ctx.clip();
  ctx.transform(a,b,c,d,x0-a*u0-c*v0,y0-b*u0-d*v0);
  ctx.drawImage(image,0,0); ctx.restore();
}

async function renderModel(meta, model, directory, prefix) {
  const local = path.join(directory, model.file);
  const atlasText = (await download(`${meta.prefix}${model.file}.atlas`, `${local}.atlas`)).toString();
  const bytes = await download(`${meta.prefix}${model.file}.skel`, `${local}.skel`);
  const pages = atlasText.split(/\r?\n/).filter((line, i, all) => line.trim() && (i === 0 || !all[i-1].trim()));
  const images = new Map();
  for (const page of pages) {
    const url = new URL(page, `${meta.prefix}${model.file}.atlas`).href;
    const file = path.resolve(path.dirname(local), page);
    if (!file.startsWith(directory + path.sep)) throw Error('atlas 页越过资源目录');
    images.set(page, await loadImage(await download(url, file)));
  }
  const atlas = new TextureAtlas(atlasText, (name, done) => {
    const image = images.get(name);
    done(new BaseTexture(null, { width: image.width, height: image.height }));
  });
  const data = new SkeletonBinary(new AtlasAttachmentLoader(atlas)).readSkeletonData(bytes);
  const animation = data.animations.find(a => /^idle$/i.test(a.name)) ?? data.animations.find(a => /idle/i.test(a.name))
    ?? (summonOnly ? data.animations.find(a => /^loop$/i.test(a.name)) : null);
  if (!animation) throw Error(`没有 Idle: ${model.file} (${data.animations.map(a => a.name).join(', ')})`);
  const files = [];
  const origins = {};
  for (let frame = 0; frame < 4; frame++) {
    const skeleton = new Skeleton(data);
    const state = new AnimationState(new AnimationStateData(data));
    state.setAnimation(0, animation.name, true); state.update(animation.duration * frame / 4); state.apply(skeleton); skeleton.updateWorldTransform();
    const draws = [];
    let clipping = null, clipEnd = null;
    for (const slot of skeleton.drawOrder) {
      const a = slot.getAttachment();
      if (a instanceof ClippingAttachment) {
        clipping = new Float32Array(a.worldVerticesLength);
        a.computeWorldVertices(slot,0,clipping.length,clipping,0,2); clipEnd = a.endSlot;
        continue;
      }
      if (!(a instanceof RegionAttachment || a instanceof MeshAttachment)) {
        if (slot.data === clipEnd) clipping = null;
        continue;
      }
      const vertices = new Float32Array(a instanceof RegionAttachment ? 8 : a.worldVerticesLength);
      if (a instanceof RegionAttachment) {
        // Pixi 的加载器只赋 region，通常由显示对象初始化；离线绘制必须补齐 UV 与局部顶点。
        // TextureRegion.u/v 已含 atlas 旋转，不能再用旧版 setRegion 的旋转分支。
        // 顶点顺序为左下、左上、右上、右下，纹理四角由 Pixi 负责旋转映射。
        const uv = a.region.texture._uvs;
        a.uvs.set([uv.x3, uv.y3, uv.x0, uv.y0, uv.x1, uv.y1, uv.x2, uv.y2]);
        a.updateOffset();
        a.computeWorldVertices(slot.bone, vertices, 0, 2);
      }
      else a.computeWorldVertices(slot, 0, vertices.length, vertices, 0, 2);
      let uvs = a.uvs;
      if (a instanceof MeshAttachment) {
        const matrix = new TextureMatrix(a.region.texture); matrix.update();
        uvs = matrix.multiplyUvs(a.regionUVs,new Float32Array(a.regionUVs.length));
      }
      draws.push({ vertices, uvs, triangles:a instanceof RegionAttachment ? [0,1,2,2,3,0] : a.triangles,
        clipping, image:images.get(a.region.page.name), alpha:slot.color.a*a.color.a });
      if (slot.data === clipEnd) clipping = null;
    }
    const xs = draws.flatMap(d => [...d.vertices].filter((_,i) => i%2 === 0));
    const ys = draws.flatMap(d => [...d.vertices].filter((_,i) => i%2 === 1));
    const minX = Math.min(...xs), maxX = Math.max(...xs), minY = Math.min(...ys), maxY = Math.max(...ys);
    const scale = pixelsPerUnit;
    const canvas = createCanvas(Math.ceil((maxX-minX)*scale)+8,Math.ceil((maxY-minY)*scale)+8);
    const ctx = canvas.getContext('2d'); ctx.translate(4-minX*scale,4+maxY*scale); ctx.scale(scale,scale);
    for (const draw of draws) {
      ctx.save();
      if (draw.clipping) {
        ctx.beginPath();
        for (let i=0;i<draw.clipping.length;i+=2) {
          if (i===0) ctx.moveTo(draw.clipping[i],-draw.clipping[i+1]);
          else ctx.lineTo(draw.clipping[i],-draw.clipping[i+1]);
        }
        ctx.closePath(); ctx.clip();
      }
      ctx.globalAlpha = draw.alpha;
      for (let i=0;i<draw.triangles.length;i+=3) triangle(ctx,draw.image,draw.vertices,draw.uvs,draw.triangles.slice(i,i+3));
      ctx.restore();
    }
    const file = `${prefix}-${frame}.png`; fs.writeFileSync(path.join(output,file),canvas.toBuffer('image/png')); files.push(file);
    origins[file] = { x: 4-minX*scale, y: 4+maxY*scale };
  }
  atlas.dispose();
  return { animation:animation.name, files, origins };
}

async function main() {
  const priority = new Set(['暴躁铁皮']);
  const cache = option('--cache', path.join(root,'out/211/cache'));
  const requested = option('--names', '').split(',').filter(Boolean);
  if (!prototypeOnly && !requested.length) for await (const line of readline.createInterface({ input:fs.createReadStream(path.join(cache,'observations.jsonl')) }))
    for (const name of Object.values(JSON.parse(line).Observation?.Shop ?? {})) priority.add(name);
  const list = operators.map(o => ({...o,name:o.name.replace(/[“”]/g,'')})).sort((a,b) => Number(priority.has(b.name))-Number(priority.has(a.name)));
  const selected = requested.length ? list.filter(o => requested.includes(o.name))
    : args.includes('--priority-only') ? list.filter(o => priority.has(o.name)) : list;
  const manifestFile = path.join(output,'manifest.json');
  const manifest = fs.existsSync(manifestFile) ? read(manifestFile) : { operators:{}, errors:{} };
  if (manifest.pixelsPerUnit !== pixelsPerUnit) manifest.operators = {};
  for (const op of selected) {
    if (manifest.pixelsPerUnit === pixelsPerUnit && manifest.operators[op.name]?.records.every(r => r.origins)
        && (!summonOnly || manifest.operators[op.name].allSkins)) continue;
    try {
      const details = extra[op.name] ?? extra[`“${op.name}”`];
      const directory = path.join(assets,details.charId);
      const meta = JSON.parse(await download(`https://torappu.prts.wiki/assets/char_spine/${details.charId}/meta.json`,path.join(directory,'meta.json')));
      const records = [];
      for (const [skinName, directions] of Object.entries(meta.skin)) {
        if (prototypeOnly && !summonOnly && skinName !== '默认') continue;
        const skinId = skinName === '默认' ? 'default' : summonOnly ? skinName : details.skins.find(s => s.name === skinName)?.id;
        if (!skinId) throw Error(`皮肤编号未映射: ${op.name}/${skinName}`);
        for (const [direction, model] of Object.entries(directions)) {
          if (!['正面','背面','战斗'].includes(direction)) continue;
          if (!/^[\w\-/]+$/.test(model.file)) throw Error('无效模型路径');
          const prefix = `${skinId === 'default' ? 'defaultskin' : 'skin'}-${op.name}-${encodeURIComponent(skinId)}-${direction === '背面' ? 'back' : 'front'}`;
          const rendered = await renderModel(meta,model,directory,prefix);
          records.push({skinId,skinName,direction,...rendered});
        }
      }
      if (!records.some(r => r.skinId === 'default')) throw Error('没有默认战斗模型');
      manifest.operators[op.name] = { charId:details.charId, records, ...(summonOnly ? { allSkins: true } : {}) }; delete manifest.errors[op.name];
      console.log(`${Object.keys(manifest.operators).length}/${selected.length} ${op.name} ${records.length} models`);
    } catch (error) { manifest.errors[op.name] = error.message; console.error(`${op.name}: ${error.message}`); }
    manifest.pixelsPerUnit = pixelsPerUnit;
    fs.writeFileSync(manifestFile,JSON.stringify(manifest,null,2));
  }
  if (selected.some(o => !manifest.operators[o.name])) process.exitCode = 1;
}
main().catch(error => { console.error(error); process.exitCode=1; });
