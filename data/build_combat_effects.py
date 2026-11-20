"""从产品目录生成站位模拟效果表；复杂机制明确列出未实现原因，不以命中一个数字算整件支持。"""
import json
import re
from pathlib import Path

DATA = Path(__file__).resolve().parent


def attributes(text):
    effects = {}
    for label, key, percent in [("攻击力", "atk", True), ("生命上限", "hp", True),
                                ("防御力", "def", True), ("法术抗性", "res", False),
                                ("攻击速度", "aspd", False), ("阻挡数", "block", False)]:
        m = re.search(label + r"([+-]\d+(?:\.\d+)?)" + ("%" if percent else ""), text)
        if m:
            effects[key] = float(m[1]) / (100 if percent else 1)
    return effects


UNSUPPORTED = {
    "雪祀之衣": "缺少装备嘲讽优先级",
    "不屈弹射器": "缺少携带者再部署时间模型",
    "战栗维式重锤": "缺少战栗状态和地面限定触发",
    "卫戍推荐信": "缺少休整期结束的装备叠层事件",
    "聚合急救箱": "缺少每个友方首次低血量触发记录",
    "灵巧涡轮手套": "缺少受伤清空的攻速叠层状态",
    "奥术法阵": "缺少敌方特殊能力沉默开关",
    "坚固维式重锤": "缺少首次致命伤害不死窗口",
    "突袭手雷": "缺少部署后限时攻击控制窗口",
    "乌萨斯呼吸器": "缺少装备跨再部署累计状态",
    "多功能补剂": "缺少装备临时盟约层数事件与上限",
    "不退者之盾": "缺少完整冻结等状态免疫模型",
    "蜂鸣器": "缺少装备嘲讽优先级",
    "伪装服": "缺少首次受伤后隐匿窗口",
    "卡西米尔竞技旗": "描述没有给出伤害衰减曲线",
    "护盾无人机": "缺少治疗触发友方护盾机制",
    "M3茧甲": "缺少装备独立复活次数与触发机制",
    "莱塔尼亚小鼓": "缺少范围内友敌击倒的装备技力事件",
    "谢拉格不融冰": "缺少寒冷叠加冻结模型",
    "可靠怀表": "缺少只在技能期间修改攻击间隔的装备机制",
    "驱逐者警棍": "缺少完整异常状态集合与条件伤害",
    "电流维式重锤": "缺少麻痹层数及条件穿透模型",
    "瓶装烈性反应": "缺少敌方元素爆发状态",
    "不退者力场": "缺少动态阻挡数量驱动生命上限与防御",
    "驱逐者瓦斯": "缺少完整异常状态时长光环",
    "动能加速器": "缺少攻击治疗驱动攻速叠层事件",
    "终极维式重锤": "缺少场上装备特殊效果复制及去重模型",
    "不退者卫护系统": "缺少动态阻挡增益、免疫与诱导机制",
    "动能轰击弩": "缺少攻速叠层上限后的随机范围伤害",
    "汲取者之镰": "缺少技能窗口内装备效果翻倍模型",
    "驱逐者防暴装备": "缺少完整异常状态、持续光环与传播机制",
}


def equipment_effect(name, text):
    if not text:
        return {"status": "unavailable", "reason": "目录无精锐版本"}
    if name in UNSUPPORTED:
        return {"status": "unsupported", "reason": UNSUPPORTED[name]}
    if "装备时" in text and "销毁" in text or name == "博士投影":
        return {"status": "noCombat", "reason": "获取、消耗或晋升结果由当前场上快照体现"}
    result = {"status": "simulated", **attributes(text)}
    if name == "灼燃维式重锤": result["burnRatio"] = int(re.search(r"相当于(\d+)%伤害", text)[1]) / 100
    if name == "源石溶剂": result["lossFlat"] = 60
    if name == "虹彩活血饮料":
        result.update(noHeal=True, regenFlat=int(re.search(r"回复(\d+)点", text)[1]))
    if name == "萨尔贡浓茶": result["sp"] = float(re.search(r"\+([\d.]+)/秒", text)[1])
    if name == "叙拉古正装": result["sideAspd"] = int(re.findall(r"攻击速度\+(\d+)", text)[1])
    if name == "精准狙击镜": result["distantDamage"] = int(re.search(r"提高(\d+)%", text)[1]) / 100
    if name == "炎国短刀": result["castAtk"] = int(re.search(r"获得(\d+)%", text)[1]) / 100
    if name == "迅捷作战粮": result["sharedInitSp"] = int(re.search(r"获得(\d+)点", text)[1])
    if name == "歌利亚头盔": result["frontHp"] = int(re.search(r"额外\+(\d+)%", text)[1]) / 100
    if name == "伊比利亚骨刺刀": result["extraArts"] = int(re.search(r"额外造成(\d+)次", text)[1]) * .25
    if name == "动能单手弩": result["crit"] = .3 * .3
    if name == "汲取者之喙": result["lifesteal"] = .05
    if name == "双模机械臂": result["weakness"] = True
    if name == "佣兵名牌":
        result = {"status": "simulated", "perCovenant": int(re.search(r"攻击力\+(\d+)%", text)[1]) / 100}
    if name == "汲取者之心": result.update(lossRatio=.01, nonlethalLoss=True)
    if name == "天马之耀": result.update(regen=.08, extraTrue=.3)
    if name == "变形同构体": result["reason"] = "沿用候选单位转职盟约判定"
    return result


def main():
    equipment = json.loads((DATA / "equipment.json").read_text(encoding="utf-8-sig"))["equipment"]
    table = {e["name"]: {v: equipment_effect(e["name"], e[v]["effectDescription"])
                         for v in ("normal", "elite")} for e in equipment}
    # 战斗类按完整描述人工审核；未列入者只改变经济、商店或当前输入已经体现的人员与层数。
    battle = {
        "band_amiya": {"kind": "activeCovenants", "thresholds": [3, 4, 5], "bonuses": [.2, .3, .4]},
        "band_ioleta": {"kind": "eliteCount", "perElite": .1},
        "band_weakness": {"kind": "weakness", "eliteAtk": .16, "eliteAspd": 16},
    }
    unsupported = {
        "band_emperor": "缺少干员再部署时间模型", "band_qalaisa": "缺少队友击倒后全队累计增益事件",
        "band_survive": "输入未提供使用目标和额外复活次数", "band_dance": "输入未提供策略目标地块",
        "band_evolve": "输入未提供临时精锐目标", "band_takecare": "输入未提供调度中心等级及随机叠层目标",
        "band_shield": "输入未提供使用目标，缺少攻击触发友方护盾模型",
        "band_amedic": "缺少策略额外医疗单位的生成位置与替换机制",
        "band_breakaway": "输入未提供使用目标，缺少首伤替换机制",
        "band_ducklord": "缺少替换敌人的抽样概率",
        "band_pudd": "输入未提供被销毁并临时分发的装备",
        "band_limit": "输入未提供叠层上限增益目标",
        "band_yan2": "缺少炎佑召唤及属性收集模型", "band_sargon": "描述未给出技能开启后的增益量与上限",
        "band_victoria": "缺少装备共享窗口和装备效果复制模型",
        "band_egir": "缺少被击倒后从整备区按次序替换部署的模型",
        "band_clementia": "缺少策略击倒叠层事件",
        "band_kazimier": "描述未给出真实伤害倍率及晕眩时长",
        "band_kjerag2": "缺少寒冷叠加冻结模型", "band_siracusa": "缺少隐匿、恐惧及其触发概率",
    }
    strategies = {}
    for s in json.loads((DATA / "strategies.json").read_text(encoding="utf-8-sig"))["strategies"]:
        sid = s["id"]
        strategies[sid] = {"name": s["name"], "status": "simulated" if sid in battle else
            "unsupported" if sid in unsupported else "noCombat", **battle.get(sid, {})}
        if sid not in battle:
            strategies[sid]["reason"] = unsupported.get(sid, "不影响战斗：经济、商店、送人或准备阶段结果由当前输入体现")
    for name, value in (("equipment_effects", table), ("strategy_effects", strategies)):
        (DATA / (name + ".json")).write_text(json.dumps(value, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")


if __name__ == "__main__":
    main()
