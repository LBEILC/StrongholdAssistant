import fs from 'node:fs';
import path from 'node:path';

// 从仓库根目录运行；可传入本地国服 gamedata 目录，不下载数据表。
const source = process.argv[2] ?? 'docs/research/refs/ArknightsGameData/zh_CN/gamedata';
const read = file => JSON.parse(fs.readFileSync(file, 'utf8'));
const handbook = read(path.join(source, 'excel/enemy_handbook_table.json')).enemyData;
const database = new Map(read(path.join(source, 'levels/enemydata/enemy_database.json')).enemies
  .map(e => [e.Key, e.Value.find(v => v.level === 0)?.enemyData]));
const targets = new Map();
for (const type of Object.values(read('data/enemy_types.json').types))
  for (const enemy of [...type.firstHalfVariants, ...type.secondHalfVariants])
    targets.set(enemy.specialEnemyId, { name: enemy.enemyName, level: 'NORMAL' });
for (const boss of read('data/bosses.json').bosses)
  targets.set(boss.enemyId, { name: boss.name, level: 'BOSS' });

const clean = text => text.replace(/<[@$][^>]*>|<\/>/g, '').replace(/\\n/g, '\n');
const damageNames = { PHYSIC: '物理', MAGIC: '法术', HEAL: '治疗', NO_DAMAGE: '无' };
const missingHandbook = [], missingDatabase = [];
const enemies = [...targets.keys()].sort().map(id => {
  const info = handbook[id], base = database.get(id), fallback = targets.get(id);
  if (!info) missingHandbook.push(id);
  if (!base) missingDatabase.push(id);
  // 第 0 级没有前级可继承；未定义字段使用表内序列化默认值，缺属性表时数值为 0。
  const number = key => base ? base.attributes[key].m_value : 0;
  return {
    id, name: info?.name ?? fallback.name, level: info?.enemyLevel ?? fallback.level,
    attackType: info?.attackType ?? '',
    damage: (info?.damageType ?? []).map(type => {
      if (!(type in damageNames)) throw new Error(`未知伤害类型：${id} ${type}`);
      return damageNames[type];
    }),
    motion: base?.motion.m_value ?? 'WALK',
    hp: number('maxHp'), atk: number('atk'), def: number('def'), res: number('magicResistance'),
    attackTime: number('baseAttackTime'), speed: number('moveSpeed'), weight: number('massLevel'),
    abilities: (info?.abilityList ?? []).map(a => clean(a.text)),
    description: clean(info?.description ?? ''),
  };
});
fs.writeFileSync('data/enemies.json', JSON.stringify(enemies, null, 2) + '\n');
const mockPath = 'src/ui/src/lib/mock/catalog.json';
const mock = read(mockPath);
mock.enemies = enemies;
fs.writeFileSync(mockPath, JSON.stringify(mock, null, 1) + '\n');
console.log(`输出：${enemies.length} 个敌人；缺图鉴：${missingHandbook.join('、') || '无'}；缺属性：${missingDatabase.join('、') || '无'}`);
