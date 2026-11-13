// 来源：Kengxxiao/ArknightsGameData zh_CN；只读取本地 JSON，不执行来源文件。
// node scripts/generate-garrison-routes.mjs <关卡目录> <activity_table.json> [输出文件]
import fs from 'node:fs';
import path from 'node:path';
import { pathToFileURL } from 'node:url';

const read = file => JSON.parse(fs.readFileSync(file, 'utf8'));
// 等距先取同一行（左先于右），其余按行从上到下、列从左到右固定取点。
export function snap(grid, p) {
  if (grid[p[0]]?.[p[1]]) return p;
  const candidates = grid.flatMap((row,r) => row.flatMap((ok,c) => ok ? [[r,c]] : []));
  candidates.sort((a,b) => (Math.abs(a[0]-p[0])+Math.abs(a[1]-p[1]))-(Math.abs(b[0]-p[0])+Math.abs(b[1]-p[1]))
    || Number(b[0]===p[0])-Number(a[0]===p[0]) || a[0]-b[0] || a[1]-b[1]);
  return candidates[0];
}

export function shortest(grid, start, end) {
  const key = p => p.join(',');
  const open = [{ p: start, cost: 0 }], costs = new Map([[key(start), 0]]), prev = new Map();
  const can = (r, c) => grid[r]?.[c] === true;
  if (!can(...start) || !can(...end)) throw new Error(`不可走的路线端点 ${start} → ${end}`);
  while (open.length) {
    open.sort((a, b) => a.cost - b.cost || a.p[0] - b.p[0] || a.p[1] - b.p[1]);
    const { p, cost } = open.shift();
    if (cost > costs.get(key(p))) continue;
    if (key(p) === key(end)) {
      const result = [p];
      while (prev.has(key(result[0]))) result.unshift(prev.get(key(result[0])));
      return result;
    }
    for (let dr = -1; dr <= 1; dr++) for (let dc = -1; dc <= 1; dc++) {
      if ((!dr && !dc) || !can(p[0] + dr, p[1] + dc)) continue;
      if (dr && dc && (!can(p[0] + dr, p[1]) || !can(p[0], p[1] + dc))) continue;
      const q = [p[0] + dr, p[1] + dc], next = cost + (dr && dc ? Math.SQRT2 : 1);
      if (next < (costs.get(key(q)) ?? Infinity) - 1e-9) {
        costs.set(key(q), next); prev.set(key(q), p); open.push({ p: q, cost: next });
      }
    }
  }
  throw new Error(`无通路 ${start} → ${end}`);
}

export function simplify(points) {
  const result = [];
  for (const p of points) {
    if (result.length && JSON.stringify(result.at(-1)) === JSON.stringify(p)) continue;
    while (result.length > 1) {
      const a = result.at(-2), b = result.at(-1);
      if ((b.col-a.col)*(p.row-b.row) !== (b.row-a.row)*(p.col-b.col)
          || (b.col-a.col)*(p.col-b.col)+(b.row-a.row)*(p.row-b.row) <= 0) break;
      result.pop();
    }
    result.push(p);
  }
  return result;
}

export function actions(level) {
  return [...level.waves.flatMap(w => w.fragments.flatMap(f => f.actions)).map(a => ({ ...a, side: 'left' })),
    ...(level.branches?.boss_battle_multi_player?.phases ?? []).flatMap(p => p.actions).map(a => ({ ...a, side: 'right' }))]
    .filter(a => a.actionType === 'SPAWN');
}

export function generate(directory, activityFile, bossFile = 'data/bosses.json') {
  const activity = read(activityFile).activity.AUTOCHESS.act3autochess.V3_1;
  const bosses = read(bossFile).bosses.map(b => b.enemyId);
  const bossKey = key => bosses.find(id => key === id || key.startsWith(id + '_')); 
  const maps = Object.keys(activity.stageDatasDict).sort().map(id => ({
    id: id.replace('act3autochess_', ''), data: read(path.join(directory, `level_${id}.json`)).mapData
  }));
  const levels = Object.fromEntries(fs.readdirSync(directory).sort()
    .filter(f => /^level_act3autochess_(\d+|h\d+(?:_\d+)?(?:_s)?)\.json$/i.test(f))
    .map(f => [f.slice(20, -5).toLowerCase(), read(path.join(directory, f))]));
  const battles = Object.fromEntries(Object.entries(activity.battleDataDict).map(([mode, rounds]) => [mode,
    Object.fromEntries(Object.entries(rounds).map(([round, entries]) => [round, entries.map(e => ({
      bossId: e.bossId, level: e.levelId.split('/').at(-1).replace('level_act3autochess_', '').toLowerCase()
    }))]))]));
  const bossIds = {};
  for (const rounds of Object.values(battles)) for (const entries of Object.values(rounds)) for (const entry of entries) {
    if (!entry.bossId || !levels[entry.level]) continue;
    for (const a of actions(levels[entry.level])) if (bossKey(a.key)) bossIds[entry.bossId] = bossKey(a.key);
  }
  const output = { source: 'Kengxxiao/ArknightsGameData zh_CN act3autochess V3_1', battles, bossIds, snaps: {}, maps: {} };
  for (const map of maps) {
    const tile = (r, c) => map.data.tiles[map.data.map[r][c]];
    const board = {
      walkable: Array.from({length: 5}, (_,r) => Array.from({length: 12}, (_,c) => tile(r+30,c+1).passableMask === 'ALL')),
      bossWalkable: Array.from({length: 6}, (_,r) => Array.from({length: 17}, (_,c) => tile(r+14,c+2).passableMask === 'ALL')),
      levels: {}
    };
    for (const [id, level] of Object.entries(levels)) {
      const bossLevel = /^h0[78]_/.test(id), offset = bossLevel ? 14 : 29;
      const height = level.mapData.map.length, width = level.mapData.map[0].length;
      const grid = Array.from({length: height}, (_,r) => Array.from({length: width}, (_,c) => tile(offset+r,2+c).passableMask === 'ALL'));
      const index = p => [height-1-p.row, p.col];
      const point = ([r,c]) => ({ col: (bossLevel && c > 8 ? 16-c : c)-.5, row: r-.5 });
      const merged = new Map(), spots = new Map();
      for (const action of actions(level)) {
        const route = level.routes[action.routeIndex];
        if (!route) throw new Error(`${id}: 路线 ${action.routeIndex} 不存在`);
        const boss = !!bossKey(action.key);
        if (boss && JSON.stringify(route.startPosition) === JSON.stringify(route.endPosition)
            && route.checkpoints.every(c => c.type.startsWith('WAIT'))) {
          const p = point(index(route.startPosition)); spots.set(JSON.stringify(p), p); continue;
        }
        let approx = false;
        const resolve = p => {
          const original = index(p), target = route.motionMode === 'WALK' ? snap(grid, original) : original;
          if (target[0] !== original[0] || target[1] !== original[1]) {
            approx = true;
            // 按被引用路线与检查点坐标去重，不按出生动作或敌人数重复累计。
            output.snaps[map.id + '/' + id + '/' + action.routeIndex + '/' + original.join(',')] = target;
          }
          return target;
        };
        let current = resolve(route.startPosition), segment = [current];
        const segments = [];
        const move = target => {
          segment.push(...(route.motionMode === 'WALK' ? shortest(grid, current, target).slice(1) : [target]));
          current = target;
        };
        for (const cp of route.checkpoints) {
          if (cp.type === 'MOVE' || cp.type === 'PATROL_MOVE') move(resolve(cp.position));
          else if (cp.type === 'DISAPPEAR') { if (segment.length) segments.push(segment); segment = []; }
          else if (cp.type === 'APPEAR_AT_POS') {
            if (segment.length) segments.push(segment);
            current = resolve(cp.position); segment = [current];
          } else if (!cp.type.startsWith('WAIT')) throw new Error(`未知检查点 ${cp.type}`);
        }
        move(resolve(route.endPosition));
        if (segment.length) segments.push(segment);
        const side = boss && !id.endsWith('_s') ? action.side : null;
        const patrol = route.checkpoints.some(cp => cp.type === 'PATROL_MOVE');
        const result = { motion: route.motionMode.toLowerCase(), boss, approx, side, patrol,
          segments: segments.map(s => route.motionMode === 'WALK' ? simplify(s.map(point)) : s.map(point)) };
        const key = JSON.stringify({ motion: result.motion, boss, side, patrol, segments: result.segments });
        if (merged.has(key)) {
          merged.get(key).count += action.count;
          merged.get(key).approx ||= approx;
        }
        else merged.set(key, { ...result, count: action.count });
      }
      board.levels[id] = { routes: [...merged.values()], bossSpots: [...spots.values()] };
    }
    output.maps[map.id] = board;
  }
  return output;
}

if (process.argv[1] && import.meta.url === pathToFileURL(path.resolve(process.argv[1])).href) {
  const [, , directory, activityFile, target = 'data/garrison_routes.json'] = process.argv;
  if (!directory || !activityFile) throw new Error('请指定关卡目录和 activity_table.json');
  const data = generate(directory, activityFile);
  fs.writeFileSync(target, JSON.stringify(data) + '\n');
  console.log(`地图 ${Object.keys(data.maps).length}；关卡 ${Object.keys(Object.values(data.maps)[0].levels).length}；路线 ${Object.values(data.maps).flatMap(m => Object.values(m.levels)).reduce((n,l) => n+l.routes.length,0)}`);
}
