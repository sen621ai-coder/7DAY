#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""Apply reviewed Simplified Chinese localization fixes without reformatting other rows."""

import csv
import io
import re
from pathlib import Path


ROOT = Path(__file__).resolve().parents[1]


FIXES = {
    "03-AEC-AIO_BOSS_EXTREME_EDITION/Config/Localization.csv": {
        "questAECWaveChallenge_obj_wave": "第{1}波，共{2}波",
    },
    "01-ProjectZ/Config/Localization.csv": {
        "buffHeavyArmorSetBonusDesc": "重甲能显著提高你的承伤能力。\\n\\n生命值增加[DECEA3]30[-]点。\\n整体寒冷抗性提高[DECEA3]10%[-]，炎热抗性降低[DECEA3]5%[-]。",
        "modMeleeErgonomicGripDesc": "降低近战攻击的耐力消耗，提高近战攻击速度，并改善弓类武器的操控性。\\n\\n[DECEA3]矿脉之母[-]或[DECEA3]箭术[-]技能可以提高该改装件的品质。",
        "BuffBullAOEIncDesc": "把大力士和公牛结合起来会得到什么？答案就在眼前——一头由血肉与狂怒构成的噩梦。它不知疲倦，每一次重击都足以夷平防御工事。正面挨上一击，你很可能当场毙命。与它交战前务必三思。\\n\\n[FFB800]特殊能力：[-]\\nBoss受到伤害后会进入狂暴状态：移动速度提高[DECEA3]50%[-]，攻击频率提高[DECEA3]60%[-]，且每次命中都会将你击倒；但在此期间，它的伤害抗性会降低[DECEA3]30%[-]。\\nBoss还会周期性进入完全防御状态，在周围生成危险的辐射区域。处于该状态时，它免疫一切攻击。\\n\\n灯光熄灭，现实开始扭曲，而求生本能只会发出一个警告：快逃！趁你的骨头还没有被碾碎！",
        "Quest_MedicineT2Offer": "«很高兴看到你的身体还算健康——或许并没有？不管怎样，我知道该怎么治疗。如果你能杀死一些[DECEA3]护士[-]和[DECEA3]危险品处理人员[-]，记得仔细搜查他们的尸体，他们身上可能带着有用的药品。处理妥当后回来告诉我。你要是愿意，我还可以分享几本实用的医学期刊。»\\n\\n[DECEA3]——珍[-]",
        "Quest_MedicineT3Offer": "«看来你已经懂得如何照顾自己了。不过，真正严重的伤势需要更专业的药品。去猎杀指定的感染者，仔细搜查他们的尸体，把找到的医疗物资带回来。完成后，我会把更高级的医学知识教给你。»\\n\\n[DECEA3]——珍[-]",
        "Quest_RareCont_Damage_ForestOffer": "«只要充分熟悉这片区域，你就能更有效地对付其中的任何生物。\\n在森林中杀死[DECEA3]任意僵尸[-]并收集所需材料，我就能调制一种灵药，使你在森林中造成的伤害提高[DECEA3]1%[-]。\\n\\n记住，只有在森林里猎杀它们才算数。»\\n\\n[DECEA3]——莱希[-]",
        "Quest_RareCont_Damage_BurntForestOffer": "«只要充分熟悉这片区域，你就能更有效地对付其中的任何生物。\\n在焦土中杀死[DECEA3]烧焦僵尸[-]并收集所需材料，我就能调制一种灵药，使你在焦土中造成的伤害提高[DECEA3]1%[-]。\\n\\n记住，只有在焦土里猎杀它们才算数。»\\n\\n[DECEA3]——莱希[-]",
        "Quest_RareCont_Damage_DesertOffer": "«只要充分熟悉这片区域，你就能更有效地对付其中的任何生物。\\n在沙漠中杀死[DECEA3]秃鹫[-]并收集所需材料，我就能调制一种灵药，使你在沙漠中造成的伤害提高[DECEA3]1%[-]。\\n\\n记住，只有在沙漠里猎杀它们才算数。»\\n\\n[DECEA3]——莱希[-]",
        "Quest_RareCont_Damage_SnowOffer": "«只要充分熟悉这片区域，你就能更有效地对付其中的任何生物。\\n在雪原中杀死[DECEA3]伐木工僵尸[-]并收集所需材料，我就能调制一种灵药，使你在雪原中造成的伤害提高[DECEA3]1%[-]。\\n\\n记住，只有在雪原里猎杀它们才算数。»\\n\\n[DECEA3]——莱希[-]",
        "Quest_RareCont_Damage_WastelandOffer": "«只要充分熟悉这片区域，你就能更有效地对付其中的任何生物。\\n在废土中杀死[DECEA3]变异体[-]并收集所需材料，我就能调制一种灵药，使你在废土中造成的伤害提高[DECEA3]1%[-]。\\n\\n记住，只有在废土里猎杀它们才算数。»\\n\\n[DECEA3]——莱希[-]",
        "Quest_RareCont_Damage_MeleeLightOffer": "«在焦土中杀死[DECEA3]小型Boss[-]并收集所需材料，我就能调制一种灵药，使轻型近战武器造成的伤害提高[DECEA3]1%[-]。\\n\\n记住，必须使用轻型近战武器猎杀它们才算数。»\\n\\n[DECEA3]——莱希[-]",
        "Quest_RareCont_Damage_MeleeHeavyOffer": "«在焦土中杀死[DECEA3]小型Boss[-]并收集所需材料，我就能调制一种灵药，使重型近战武器造成的伤害提高[DECEA3]1%[-]。\\n\\n记住，必须使用重型近战武器猎杀它们才算数。»\\n\\n[DECEA3]——莱希[-]",
        "Quest_RareCont_HealthT1Offer": "«任何人都会羡慕这些生物的生命力。不过先说好：不准用枪。\\n杀死[DECEA3]5只感染熊[-]并收集所需材料，我就能调制一种灵药，使你的生命值上限提高[DECEA3]1[-]点。\\n\\n记住，只能在废土中猎杀它们，而且只能用弓或弩击杀。»\\n\\n[DECEA3]——莱希[-]",
        "Quest_RareCont_HealthT2Offer": "«任何人都会羡慕这些生物的生命力。不过先说好：不准用枪。\\n杀死[DECEA3]20只感染熊[-]并收集所需材料，我就能调制一种灵药，使你的生命值上限提高[DECEA3]5[-]点。\\n\\n记住，只能在废土中猎杀它们，而且只能用弓或弩击杀。»\\n\\n[DECEA3]——莱希[-]",
        "Quest_RareCont_HealthT3Offer": "«任何人都会羡慕这些生物的生命力。不过先说好：不准用枪。\\n杀死[DECEA3]35只感染熊[-]并收集所需材料，我就能调制一种灵药，使你的生命值上限提高[DECEA3]10[-]点。\\n\\n记住，只能在废土中猎杀它们，而且只能用弓或弩击杀。»\\n\\n[DECEA3]——莱希[-]",
        "Quest_RareCont_JumpStrengthOffer": "«这些敏捷的野兽几乎什么地方都能跳上去。不过先说好：不准用枪。\\n杀死[DECEA3]12只美洲狮[-]并收集所需材料，我就能调制一种灵药，使你的跳跃力提高[DECEA3]5%[-]。\\n\\n记住，只能在雪原的夜间猎杀它们，而且只能用弓或弩击杀。»\\n\\n[DECEA3]——莱希[-]",
        "Quest_RareCont_MobilityOffer": "«连追上它们都几乎不可能，更别说逃掉了。不过先说好：不准用枪。\\n杀死[DECEA3]25只僵尸犬、狼或郊狼[-]并收集所需材料，我就能调制一种灵药，使你的机动性提高[DECEA3]1%[-]。\\n\\n记住，只能在夜间猎杀它们，而且只能用弓或弩击杀。»\\n\\n[DECEA3]——莱希[-]",
        "modAttributeBoostUniqueDesc": "大型AI增强模块，可显著提升并扩展AI能力。\\n安装于手套。\\n\\n所有属性提高[DECEA3]1[-]点。",
        "modAttributeBoostExpDesc": "庞大的AI增强基座，可加速并扩展AI能力。它最终会带来什么，目前仍不得而知。\\n安装于手套。\\n\\n所有属性提高[DECEA3]2[-]点。",
        "modArmorLSSOutfitDesc": "用于躯干防护的实验型生命维持系统。生命值提高[DECEA3]50[-]点，治疗速度提高至[DECEA3]2[-]倍。\\n\\n集齐全套生命维持改装件后：[DECEA3]生命、耐力、食物与水的基础上限均提高[DECEA3]50[-]点。[-]",
        "modArmorLSSGlovesDesc": "用于手部防护的实验型生命维持系统。食物与水的消耗降低[DECEA3]25%[-]。\\n\\n集齐全套生命维持改装件后：[DECEA3]生命、耐力、食物与水的基础上限均提高[DECEA3]50[-]点。[-]",
        "modNerdBonusUniqueDesc": "阅读杂志时有[DECEA3]50%[-]的概率额外获得1个技能点，各类杂志的获取概率也会提高[DECEA3]25%[-]。\\n\\n无法安装在书呆子护甲上。",
        "modReinforcedPartsDesc": "提高对各类材质的方块伤害。\\n\\n[DECEA3]69年矿工[-]技能可提高该改装件的品质。\\n\\n[DECEA3]可安装于挖掘机或链锯。[-]",
        "modSpareBoxDesc": "拆解物体时可获得更多资源。\\n\\n[DECEA3]回收行动[-]技能可提高该改装件的品质。",
        "modMeleeTheHunterImpDesc": "这套猎具用起来几乎像在钓鱼——只不过‘鱼’会咬人，也不会乖乖顺流而下。根据敌人类型提高对其造成的伤害。\\n\\n[DECEA3]猎人[-]技能可提高该改装件的品质。",
        "modMeleeTheHunterUniqueDesc": "专为猎杀各类敌人打造的高品质装备。根据敌人类型提高对其造成的伤害。",
        "modGunRiflePartsUniqueDesc": "狙击步枪专用的独特改装件：耐久度提高[DECEA3]240%[-]，武器操控性提高[DECEA3]30%[-]。",
        "modGunPistolPartsUniqueDesc": "手枪专用的独特改装件：耐久度提高[DECEA3]240%[-]，武器操控性提高[DECEA3]30%[-]。",
        "modGunM60PartsUniqueDesc": "机枪专用的独特改装件：耐久度提高[DECEA3]240%[-]，武器操控性提高[DECEA3]30%[-]。",
        "modGunShotgunPartsUniqueDesc": "\u9730弹枪专用的独特改装件：耐久度提高[DECEA3]240%[-]，武器操控性提高[DECEA3]30%[-]。",
        "modVehicleSuperChargerUniqueDesc": "独特的载具改装件，可使加速性能与最高速度提高[DECEA3]60%[-]。",
        "perkFearAnimalsDesc": "[FFB800]击杀[-]非攻击性动物会使计数器减少[DECEA3]1[-]，击杀攻击性动物则减少[DECEA3]2[-]。击杀狼、郊狼或美洲狮时额外减少[DECEA3]1[-]，击杀熊时额外减少[DECEA3]2[-]。",
        "perkBodybuilderDesc": "[FFB800]每秒[-]降低计数器：超重时减少[DECEA3]1[-]，进行力量训练时减少[DECEA3]4[-]，进行有氧训练时减少[DECEA3]2[-]。使用武器或工具发动普通攻击时减少[DECEA3]2[-]，发动蓄力攻击时减少[DECEA3]5[-]。\\n\\n[FFB800]受阻进度：{cvar(.blockBodybuilder:0)}[-] [DECEA3]——拥有肌肉萎缩或弱不禁风时无法提升。[-]",
        "UltraStrengthAlloys_BundleSmallDesc": "内含10个[DECEA3]超强合金[-]。\\n点击[DECEA3]打开[-]即可取出合金锭。",
        "UltraStrengthAlloys_BundleMediumDesc": "内含50个[DECEA3]超强合金[-]。\\n点击[DECEA3]打开[-]即可取出合金锭。",
        "UltraStrengthAlloys_BundleBigDesc": "内含100个[DECEA3]超强合金[-]。\\n点击[DECEA3]打开[-]即可取出合金锭。",
    },
    "04-AEC-ENDGAME_OVERHAUL/Config/Localization.csv": {
        "aecHeadConverter": "[FFD700]AEC[-] 变异样本兑换台",
        "aec_eo_field_guide_09_text": "[5599FF]契约与代币[-] AEC商人契约会将T00至T15的敌人等级与A1至A5的兴趣点规模组合起来。激活集结点，清除沉睡者，留在兴趣点范围内完成任务，然后返回商人处交付。执行清剿阶段时，标注的AEC敌人等级只会在该兴趣点内部生效。完成契约可获得通用AEC代币以及随等级提高的奖励；敌人等级越高、目标区域越大，获得的代币就越多。商人是否提供相应契约取决于商人等级和游戏阶段。\\n\\nAEC EO战役共包含419个步骤，涵盖从0★到5★的仆从与Boss系列，包括研究论文、被动样本采集器以及最终的T15 A5挑战。契约中继站每天都会提供一组可重复获取的Boss与仆从契约；区块卸载后，它还可以重新召唤无人机。",
        "aec_tp2_l2_text": "每完成一份AEC契约，都会获得[FFCC33]通用AEC代币[-]。",
        "aec_tp2_l3_text": "通用AEC代币可用于终局市场、研究项目以及高级AEC制造配方。",
        "aec_tp3_l3_text": "完成更高等级、规模更大的契约可以获得更多通用AEC代币。",
    },
    "07-AEC-Vehicles-NoMicrocraft/Config/Localization.csv": {
        "modAECFuelSaverRoadScrounger": "[FFD700]AEC[-] 公路拾荒者节油器",
        "modAECFuelSaverWastelandMiserPrime": "[FFD700]AEC[-] 荒原节油至尊版",
    },
    "98-AECxProjectZ_Tweaks/Config/Localization.csv": {
        "resourceMutationSamples": "[FFD700][AEC[-][F87C63]\\PZ][-] [8EBE67]研究材料[-]",
        "resourceMutationSamplesDesc": "[FFD866]获取方式[-][FFFFFF]\\n• 凭借采集者加成，可从[-][B36B4F]«僵尸尸体»[-][FFFFFF]中获得\\n• 在[-][5ECFFF]«变异孵化器»[-][FFFFFF]中熔炼不同等级的[-][8EBE67]«变异样本»[-][FFFFFF]\\n\\n[-][FFD866]用途[-][FFFFFF]\\n用于Project Z研究、逆向合成以及«稀有样本»任务链。[-]",
        "projectZZombieCorpseBundleDesc": "[B36B4F]额外击杀奖励[-][FFFFFF]\\n[-][8EBE67]«变异样本»[-][FFFFFF]与[-][D9D9D9]«感染者牙齿»[-][FFFFFF]是两种独立的战利品。\\n\\n[-][FFD866]四次独立掉落判定[-][FFFFFF]\\n骨头0–5  •  腐肉0–10\\n硝酸盐0–10  •  脂肪0–2\\n\\n[-][FFD866]采集者[-][FFFFFF]\\n可能额外获得[-][8EBE67]«研究材料»[-][FFFFFF]、[-][D9D9D9]«感染者牙齿»[-][FFFFFF]或1枚[-][FFD866]«通用代币»[-][FFFFFF]。[-]",
        "perkIntellectMasteryRank5LongDesc": "[DECEA3]精通效果[-]\\n\\n电击棒每次命中都有[FFD866]1%[-]的概率立即杀死目标。",
        "perkEliteHuntRank14LongDesc": "[DECEA3]加成[-][FFFFFF]\\n对精英僵尸造成的伤害提高[-][DECEA3]14%[-][FFFFFF]。[-]",
        "infectedTeethDesc": "[FF8A65]重要物品[-]\\n请勿丢弃此资源；后续整合包进度会用到它。\\n\\n[5ECFFF]通用市场货币[-]\\n\\n有效击杀符合条件的僵尸和人形感染者后会自动获得。僵尸动物及其他由动物变异的感染者不会掉落牙齿，请按常规方式采集它们。战斗等级越高，获得的牙齿越多。\\n\\n[FFD866]用途[-]\\n用于服务器电池订单、装饰箱以及部分终局市场订单。",
        "PZAEC_Guide_Market_06_Desc": "[66CFFF]僵尸市场6/8——AEC硬币[-]\\nAEC硬币是市场体系内部使用的资源。它不同于商人货币、感染者牙齿和通用AEC代币，主要用于市场与研究链的后续阶段。\\n\\n[5ECFFF]目标[-]\\n积累[FFD866]100枚AEC硬币[-]。\\n\\n[FF8A65]重要提示[-]\\n请勿取消这个一次性任务，也不要丢弃关联章节或任务物品。任务链遗失后可能需要管理员才能恢复。\\n\\n",
        "PZAEC_Guide_Mutator_04_Name": "[FFD700][AEC\\PZ][-] [C78CFF]通往强大力量之路[-] [DECEA3]4/4[-]——[FFFFFF]代币与变异器II[-]",
        "aecVehProgQuest1Offer": "[FFD700][AEC][-] [55C05A]车库1/2——最终装配[-]\\nAEC最终装配台用于制造底盘、修理包和完整载具。这条简短的任务线将介绍两种专用载具工作台。\\n\\n[5ECFFF]目标[-]\\n制造[FFD866]AEC最终装配台[-]。完成后会获得车库指南第二章。\\n\\n[FF8A65]重要提示[-]\\n请勿取消这个一次性任务，也不要丢弃关联章节。任务链遗失后可能需要管理员才能恢复。\\n\\n",
        "aecVehProgQuest2Offer": "[FFD700][AEC][-] [55C05A]车库2/2——载具改装件[-]\\n第二座工作台用于制造载具升级模块，包括油耗、灯光、伤害、速度、扭矩、油箱容量和碰撞防护等改装件。\\n\\n[5ECFFF]目标[-]\\n制造[FFD866]AEC改装件工作台[-]。\\n\\n[FF8A65]重要提示[-]\\n请勿取消这个一次性任务，也不要丢弃关联章节。任务链遗失后可能需要管理员才能恢复。\\n\\n",
        "aecVehProgChapter1Desc": "[FFFFFF][55C05A]AEC车库[-][FFFFFF]\\n\\n开启载具指南第一章。\\n\\n[FF8A65]重要提示[-][FFFFFF]\\n请勿取消此任务。只有管理员才能补发指南。[-]",
        "aecVehProgChapter2Desc": "[FFFFFF][55C05A]AEC车库[-][FFFFFF]\\n\\n开启载具指南第二章。\\n\\n[FF8A65]重要提示[-][FFFFFF]\\n请勿取消此任务。只有管理员才能补发指南。[-]",
    },
}

PROJECTZ_ADDITIONAL_FINAL_DISPLAY = {
    "perkSniperExpertDesc": "每用狙击步枪击杀一名敌人，你都会更加熟悉这类武器。尽可能多地击杀僵尸，最终成为狙击步枪大师。\\n\\n[DECEA3]当前击杀数：{cvar(CountSniperExpert:0)}[-]",
    "perkShotgunExpertDesc": "每用霰弹枪击杀一名敌人，你都会更加熟悉这类武器。尽可能多地击杀僵尸，最终成为霰弹枪大师。\\n\\n[DECEA3]当前击杀数：{cvar(CountShotgunExpert:0)}[-]",
    "perkAutomaticWeaponExpertDesc": "每用自动武器击杀一名敌人，你都会更加熟悉这类武器。尽可能多地击杀僵尸，最终成为自动武器大师。\\n\\n[DECEA3]当前击杀数：{cvar(CountAutomaticWeaponExpert:0)}[-]",
    "perkPistolExpertDesc": "每用手枪击杀一名敌人，你都会更加熟悉这类武器。尽可能多地击杀僵尸，最终成为手枪大师。\\n\\n[DECEA3]当前击杀数：{cvar(CountPistolExpert:0)}[-]",
    "perkBowsExpertDesc": "每用弓或弩击杀一名敌人，你都会更加熟悉这类武器。尽可能多地击杀僵尸，最终成为弓弩大师。\\n\\n[DECEA3]当前击杀数：{cvar(CountBowsExpert:0)}[-]",
    "perkRoboticsExpertDesc": "每当机器人炮塔击杀一名敌人，你都会更加了解它的运作方式。尽可能多地击杀僵尸，最终成为机器人专家。\\n\\n[DECEA3]当前击杀数：{cvar(CountRoboticsExpert:0)}[-]",
    "perkMeleeHeavyExpertDesc": "每用重型近战武器击杀一名敌人，你都会更加熟悉这类武器。尽可能多地击杀僵尸，最终成为重型近战大师。\\n\\n[DECEA3]当前击杀数：{cvar(CountMeleeHeavyExpert:0)}[-]",
    "perkMeleeLightExpertDesc": "每用轻型近战武器击杀一名敌人，你都会更加熟悉这类武器。尽可能多地击杀僵尸，最终成为轻型近战大师。\\n\\n[DECEA3]当前击杀数：{cvar(CountMeleeLightExpert:0)}[-]",
    "perkBullsEyeDesc": "各位，小心脑袋！尽可能通过爆头击杀更多僵尸。\\n\\n[DECEA3]当前击杀数：{cvar(CountBullsEye:0)}[-]",
    "perkGameTimerDesc": "要适应这个危机四伏的世界并不容易，但一切只是时间问题。生存得越久，你就越能适应这里。\\n\\n[DECEA3]累计生存时间：{cvar(TimerGame_HH:0)}小时{cvar(TimerGame_MM:0)}分钟[-]",
    "perkForestTimerDesc": "森林同样需要生存经验。你在森林停留得越久，就越能适应这里的环境。\\n\\n[DECEA3]当前：{cvar(TimerForest_HH:0)}小时{cvar(TimerForest_MM:0)}分钟[-]",
    "perkBurntTimerDesc": "持续的烟尘并不容易适应。你在烧毁之地停留得越久，就越能适应这里的环境。\\n\\n[DECEA3]当前：{cvar(TimerBurnt_HH:0)}小时{cvar(TimerBurnt_MM:0)}分钟[-]",
    "perkDesertTimerDesc": "酷热并不容易适应。你在沙漠停留得越久，就越能适应这里的环境。\\n\\n[DECEA3]当前：{cvar(TimerDesert_HH:0)}小时{cvar(TimerDesert_MM:0)}分钟[-]",
    "perkWinterTimerDesc": "严寒并不容易适应。你在雪原停留得越久，就越能适应这里的环境。\\n\\n[DECEA3]当前：{cvar(TimerWinter_HH:0)}小时{cvar(TimerWinter_MM:0)}分钟[-]",
    "perkWastelandTimerDesc": "这里的辐射足以杀死任何人。你在废土停留得越久，就越能适应这里的环境。\\n\\n[DECEA3]当前：{cvar(TimerWasteland_HH:0)}小时{cvar(TimerWasteland_MM:0)}分钟[-]",
    "perkExperiencedHunterRank1LongDesc": "你已有丰富的猎杀经验。轻型近战武器的攻击速度提高[DECEA3]7.5%[-]、伤害提高[DECEA3]10%[-]，肢解概率提高[DECEA3]5%[-]。\\n\\n[FFB800]剩余：{cvar(.CounterExperiencedHunter0:0)}[-]",
    "perkGameTimerRank10LongDesc": "[DECEA3]游戏内加成：[-]\\n获得的经验提高[DECEA3]10%[-]，战利品加成提高[DECEA3]10%[-]，机动性提高[DECEA3]15%[-]，生命值与耐力上限各提高[DECEA3]50[-]点。\\n\\n[DECEA3]额外能力[-] [FFB800]慧眼识宝[-]\\n每击杀5,000名敌人，战利品加成提高50点。\\n[DECEA3]当前击杀数：{cvar(.GameTimerKillCounter:0)}；当前加成：{cvar(.GameTimerLootBonus:0)}[-]",
    "Buff_Exp_Activity_StatusDesc": "你已派出“活动加剧”探险队。若行动顺利，可前往商人处领取奖励。\\n\\n成功率：[DECEA3]{cvar(.Exp_ActivityRewardChance:0)}%[-]\\n持续时间：[DECEA3]60分钟[-]\\n\\n[DECEA3]在森林或烧毁之地每击杀25只僵尸，探险成功率提高1%。[-]",
    "Buff_Exp_Infection_StatusDesc": "你已派出“感染源”探险队。若行动顺利，可前往商人处领取奖励。\\n\\n成功率：[DECEA3]{cvar(.Exp_InfectionRewardChance:0)}%[-]\\n持续时间：[DECEA3]90分钟[-]\\n\\n[DECEA3]在沙漠或雪原每击杀30只僵尸，探险成功率提高1%。[-]\\n[DECEA3]在沙漠或雪原每击杀1只小型首领，探险成功率提高1%。[-]\\n[DECEA3]在沙漠或雪原每击杀1只首领，探险成功率提高3%。[-]",
    "Buff_Exp_Deadzone_StatusDesc": "你已派出“死区”探险队。若行动顺利，可前往商人处领取奖励。\\n\\n成功率：[DECEA3]{cvar(.Exp_DeadzoneRewardChance:0)}%[-]\\n持续时间：[DECEA3]120分钟[-]\\n\\n[DECEA3]在废土每击杀35只僵尸，探险成功率提高1%。[-]\\n[DECEA3]在废土每击杀1只小型首领，探险成功率提高1%。[-]\\n[DECEA3]在废土每击杀1只首领，探险成功率提高3%。[-]",
    "buffAngerGenerationDesc": "你已装备全套[FFB800]掠夺者[-]护甲。\\n\\n近战攻击速度与伤害提高[DECEA3]{cvar(.AngerGenerationDisplay:0)}%[-]。",
    "buffStreetRepIncDesc": "一名盟友已装备全套[FFB800]《桑尼》[-]护甲。\\n\\n你的近战伤害与攻击速度提高[DECEA3]{cvar(.SonnyStreetRepBonus:0)}%[-]。\\n\\n效果层数越多，加成越高；层数可从不同来源获得。\\n\\n当前层数：[DECEA3]{cvar(.SonnyStreetRepEffect:0)}[-]\\n持续时间：[DECEA3]{cvar(.SonnyStreetRepEffectDuration:0)}秒[-]",
    "buffJetPackBoostIncDesc": "喷气背包已启动。下一次跳跃的高度提高[DECEA3]{cvar(.JetPackStrength:0)}%[-]。\\n\\n[DECEA3]完成强化跳跃后，喷气背包会进入充能状态。小心别摔断腿。[-]",
    "Traits_message": "角色的个人特质已经确定。\\n\\n你获得了[5AFF75]{cvar(.PosTraitsCount:0)}点正面特质[-]与[FF2B2B]{cvar(.NegTraitsCount:0)}点负面特质[-]。",
    "modElongatedCoreDesc": "提高采矿半径与资源采集量，效果随改装件品质提高。\\n\\n[DECEA3]“69年矿工”技能可提高该改装件的制作品质。\\n\\n可安装于螺旋钻或链锯。[-]",
    "modReinforcedPartsUniqueDesc": "对各类材质的方块伤害提高[DECEA3]50%[-]。\\n\\n[DECEA3]可安装于螺旋钻或链锯。[-]",
    "modIncreasedBatteryDesc": "提高反应堆工具的电池容量，效果随改装件品质提高。\\n\\n[DECEA3]“机器人发明家”技能可提高该改装件的制作品质。\\n\\n可安装于反应堆工具。[-]",
    "modReactorElectricDesc": "降低反应堆工具的电量消耗速度，效果随改装件品质提高。\\n\\n[DECEA3]“机器人发明家”技能可提高该改装件的制作品质。\\n\\n可安装于反应堆工具。[-]",
    "modEnhancedMechanismsDesc": "提高冲击起子拆解物体时的方块伤害，效果随改装件品质提高。\\n\\n[DECEA3]“回收行动”技能可提高该改装件的制作品质。[-]",
    "modFastMechanismsDesc": "提高冲击起子的攻击速度，效果随改装件品质提高。\\n\\n[DECEA3]“回收行动”技能可提高该改装件的制作品质。[-]",
}


def rewrite_file(relative_path: str, fixes: dict[str, str]) -> None:
    path = ROOT / relative_path
    raw_lines = path.read_text(encoding="utf-8-sig").splitlines(keepends=True)
    header = next(csv.reader([raw_lines[0]]))
    columns = {name.lower(): index for index, name in enumerate(header)}
    zh_index = columns["schinese"]
    changed = set()

    for index in range(1, len(raw_lines)):
        line = raw_lines[index]
        ending = "\r\n" if line.endswith("\r\n") else "\n" if line.endswith("\n") else ""
        row = next(csv.reader([line.removesuffix(ending)]))
        if not row or row[0] not in fixes:
            continue
        row[zh_index] = fixes[row[0]]
        output = io.StringIO()
        csv.writer(output, lineterminator=ending, quoting=csv.QUOTE_MINIMAL).writerow(row)
        raw_lines[index] = output.getvalue()
        changed.add(row[0])

    missing = set(fixes) - changed
    if missing:
        raise KeyError(f"Missing localization keys in {relative_path}: {sorted(missing)}")
    with path.open("w", encoding="utf-8-sig", newline="") as handle:
        handle.write("".join(raw_lines))
    print(f"{relative_path}: updated {len(changed)} entries")


for filename, translations in FIXES.items():
    rewrite_file(filename, translations)


BOSS_EXACT = {
    "A colossus of living stone and mineral fury. Once a miner, now an abomination encrusted in coal lead nitrate and shale, with tools fused into its spine. It hurls boulders with brutal accuracy and grows faster as it weakens.": "由活体岩石与矿物狂怒构成的巨像。它曾是一名矿工，如今却变成了被煤炭、铅、硝酸盐和页岩包裹的怪物，工具也与脊柱熔为一体。它能极其精准地投掷巨石，而且伤势越重，行动速度越快。",
    "The Mechanician is close. Expect aggressive melee pressure, burning projectiles, and repeated minion reinforcements.": "机械师就在附近。小心猛烈的近战压制、燃烧投射物以及不断出现的仆从援军。",
    "Witch's Curse": "女巫诅咒", "The Witch's dark magic weakens your resolve.": "女巫的黑暗魔法正在削弱你的意志。",
    "[00DC50]VAMPIRISM[-]": "[00DC50]生命汲取[-]",
    "The Headless is close. Panic rises and your stamina drains under its murderous aura.": "无头者就在附近。它的杀戮气场会令恐慌不断加剧，并持续消耗你的耐力。",
    "!! ARCHER BOSS": "！！弓箭手Boss", "The Archer Boss is within range. Watch for incoming arrows.": "弓箭手Boss已进入攻击范围，小心来袭的箭矢。",
    "Join the AEC PROJECT Discord for any mod question or request.": "如有任何模组问题或需求，请加入AEC PROJECT Discord。",
    "Pick your echelon. The deepest page also carries the legendary contracts.": "选择你要挑战的梯队。最深层页面还会提供传奇契约。",
    "I'm already on a contract.": "我已经接取了一份契约。",
    "Then finish it and come back. I do not stack AEC work.": "先完成它再回来。AEC契约不能同时叠加进行。",
    "No contracts right now.": "目前没有可接取的契约。",
    "Questing is currently blocked for you. Clear that first.": "你当前无法接取任务，请先解除现有的任务限制。",
    "Contract availability is based on your player level.": "可接取的契约取决于玩家等级。",
    "Boss parts drop from every contract reward airdrop.": "每份契约的奖励空投都有机会掉落Boss部件。",
    "They can be sold to any trader for extra income": "这些部件可以出售给任意商人，换取额外收入。",
    "The Heart is the rarest and most valuable drop": "心脏是最稀有、价值最高的掉落物。",
    "[AAAAAA]AEC gear requires tiered mutation samples from matching boss families.[-]": "[AAAAAA]制造AEC装备需要对应Boss系列及等级的变异样本。[-]",
    "What kind of contract do you want to know about?": "你想了解哪一种契约？",
    "Special Quests": "特殊任务",
    "Wilderness contract — no marked location required.": "荒野契约——无需前往标记地点。",
    "1. Place the signal lure from your inventory": "1. 从背包中取出并放置信号诱饵",
    "2. Survive 3 or more escalating enemy waves": "2. 抵御至少3波逐渐增强的敌人",
    "3. Kill the boss when it spawns": "3. Boss出现后将其击杀",
    "4. Airdrop lands automatically at completion": "4. 完成后奖励空投会自动降落",
    "POI-based contract — reach the location first.": "兴趣点契约——必须先抵达指定地点。",
    "1. Reach the marked POI location": "1. 前往标记的兴趣点",
    "2. Clear all sleepers inside the POI": "2. 清除兴趣点内的所有沉睡者",
    "3. Survive 3 or more incoming enemy waves": "3. 抵御至少3波来袭敌人",
    "4. Kill the boss when it breaches": "4. Boss突破防线后将其击杀",
    "5. Airdrop lands automatically at completion": "5. 完成后奖励空投会自动降落",
    "Lightweight wilderness minion contract.": "难度较低的荒野仆从契约。",
    "2. Survive 3 or more minion patrol waves": "2. 抵御至少3波仆从巡逻队",
    "3. No boss kill required": "3. 无需击杀Boss",
    "POI-based minion contract — no boss kill required.": "兴趣点仆从契约——无需击杀Boss。",
    "3. Survive 3 or more minion assault waves": "3. 抵御至少3波仆从进攻",
    "4. No boss kill required": "4. 无需击杀Boss",
    "Unique contracts with special rules and higher rewards.": "具有特殊规则和更高奖励的独特契约。",
    "[FFD700]── SPECIAL QUESTS ──[-]": "[FFD700]── 特殊任务 ──[-]",
    "Higher risk — more mutation samples and better airdrop rewards.": "风险越高，获得的变异样本越多，空投奖励也越好。",
    "Some specials have no wave pattern — kill on sight": "部分特殊任务没有固定波次——发现目标后立即击杀。",
    "AEC Signal Lure": "AEC信号诱饵",
    "Quest item. Place this lure after activating the rally point to draw the enemy waves to your position.": "任务物品。激活集结点后放置该诱饵，将一波波敌人引到你的位置。",
    "All-In-One Lure": "全能诱饵",
    "Quest item. Place this reinforced lure to trigger The All-In-One Hunt and pull every marked boss pair onto your position.": "任务物品。放置这个强化诱饵可触发“全能猎杀”，并将所有标记的Boss组合引到你的位置。",
    "A salvaged radio converted into an AEC contract terminal. Use it to summon the Huntmaster and access AEC-only contracts.": "由废旧无线电改装而成的AEC契约终端。可用于召唤猎杀大师并接取AEC专属契约。",
    "Portable AEC ammo bench with a table saw shell. Place it, craft unlocked workbench munitions.": "使用台锯外壳制作的便携式AEC弹药台。放置后可制造已解锁的工作台弹药。",
    "The Sheriff's presence drains your stamina.": "警长的威压会持续消耗你的耐力。",
    "The next wave will arrive when this countdown ends.": "倒计时结束后，下一波敌人将会抵达。",
}

BOSS_NAMES = {
    "Doomlord": "毁灭领主", "Dumdum": "爆破狂人", "Running Kamikaze": "狂奔神风者",
    "Electric Demon": "电魔", "Executioner": "行刑者", "Explosive Eagle": "爆裂鹰",
    "Hammer Guardian": "战锤守卫", "Party Beach": "海滩狂欢者", "Rockbreaker": "碎岩者",
    "Singerie": "辛格里", "Siren Head": "警笛头", "Druid": "德鲁伊", "Mechanician": "机械师",
    "Witch": "女巫", "Mushroom": "菌菇怪", "Sheriff": "警长",
}

ENTITY_NAMES = {
    "Feral Chicken": "凶暴鸡", "Enraged Boar": "狂怒野猪", "Boss Boar": "野猪首领",
}


def translate_boss_fixed_categories() -> None:
    path = ROOT / "03-AEC-AIO_BOSS_EXTREME_EDITION/Config/Localization.csv"
    raw_lines = path.read_text(encoding="utf-8-sig").splitlines(keepends=True)
    header = next(csv.reader([raw_lines[0]]))
    columns = {name.lower(): index for index, name in enumerate(header)}
    en_index, zh_index = columns["english"], columns["schinese"]
    changed = 0

    for index in range(1, len(raw_lines)):
        line = raw_lines[index]
        ending = "\r\n" if line.endswith("\r\n") else "\n" if line.endswith("\n") else ""
        row = next(csv.reader([line.removesuffix(ending)]))
        if len(row) <= max(en_index, zh_index) or row[en_index].strip() != row[zh_index].strip():
            continue
        english = row[en_index]
        chinese = BOSS_EXACT.get(english)

        if chinese is None and row[1] == "buffs" and "Slayer rank up!" in english:
            chinese = english.replace("Slayer rank up!", "猎杀者等级提升！")
            for source, target in BOSS_NAMES.items():
                chinese = chinese.replace(source, target)
        elif chinese is None and row[1] == "entityclasses":
            chinese = english
            for source, target in ENTITY_NAMES.items():
                chinese = chinese.replace(source, target)
            if chinese == english:
                chinese = None
        elif chinese is None and row[1] == "blocks":
            match = re.fullmatch(r"Tier ([1-5]) relay beacon\. Activate to launch ([1-5])★ AEC relay events\.", english)
            if match:
                chinese = f"{match.group(1)}级中继信标。激活后可启动{match.group(2)}★ AEC中继事件。"

        if chinese is None:
            continue
        row[zh_index] = chinese
        output = io.StringIO()
        csv.writer(output, lineterminator=ending, quoting=csv.QUOTE_MINIMAL).writerow(row)
        raw_lines[index] = output.getvalue()
        changed += 1

    with path.open("w", encoding="utf-8-sig", newline="") as handle:
        handle.write("".join(raw_lines))
    print(f"03-AEC-AIO_BOSS_EXTREME_EDITION/Config/Localization.csv: updated {changed} fixed-category entries")


translate_boss_fixed_categories()


BOSS_CONTRACT_EXACT = {
    "Survive the first assault": "抵御第一波进攻", "Survive the stronger assault": "抵御更猛烈的进攻",
    "Survive the next wave": "抵御下一波敌人", "Survive the renewed assault": "抵御新一轮进攻",
    "Survive the final siege wave": "抵御最后一波围攻", "Survive the incoming workers": "抵御来袭的工人感染者",
    "Survive the stronger workers": "抵御更强的工人感染者", "Survive the radiated push": "抵御辐射感染者的攻势",
    "Survive the charged assault": "抵御充能感染者的进攻", "Survive the infernal wave": "抵御炼狱感染者波次",
    "I will activate the rally, place the lure, and finish the hunt.": "我会激活集结点、放置诱饵并完成这场猎杀。",
    "Power source terminated. Here is your payment.": "能量源已经摧毁，这是你的报酬。",
    "The Executioner is dead. Take your reward.": "行刑者已死，拿上你的奖励。",
    "Target neutralized. The sky is clear for now.": "目标已被消灭，天空暂时安全了。",
    "The Ghost is dead and the night is quieter.": "幽灵已被消灭，夜晚终于安静了一些。",
    "Target neutralized. The ground is quiet again.": "目标已被消灭，大地重新恢复平静。",
    "Target neutralized. The Sheriff is down.": "目标已被消灭，警长倒下了。",
    "The last Sheriff wave has broken. Good work.": "警长的最后一波攻势已经瓦解，干得好。",
    "Hellskyli is down. Payment is ready.": "赫尔斯凯利已被击杀，报酬已经备好。",
    "Mykir is dead. That payout is earned.": "迈基尔已死，这是你应得的报酬。",
    "Party Beach is dead. Here's your reward.": "海滩狂欢者已死，这是你的奖励。",
    "Rockbreaker is down. Here is your payment.": "碎岩者已被击杀，这是你的报酬。",
    "The Singerie is dead. Collect your pay.": "辛格里已死，领取你的报酬。",
    "Alpha target neutralized. Payment approved.": "首领目标已被消灭，报酬已批准。",
    "Signal terminated. Siren Head is down.": "信号已经终止，警笛头倒下了。",
    "The mixed assault has been broken.": "混合部队的进攻已经瓦解。",
    "The Headless hunt is complete.": "无头者猎杀已经完成。",
    "Green contract final breaker wave incoming.": "绿色契约的最后一波突破部队正在接近。",
    "The last Archer breach is on you. Kill 3 zombies to break the assault.": "弓箭手的最后一波突破部队已经压上来。击杀3只僵尸，粉碎这次进攻。",
    "The last mixed breach is on you. Kill 3 zombies to break the assault.": "混合部队的最后一波突破已经压上来。击杀3只僵尸，粉碎这次进攻。",
    "Minions are massing near the lure. Survive three escalating waves, then eliminate the stragglers to end the assault.": "仆从正在诱饵附近集结。抵御三波逐渐增强的进攻，再消灭残余敌人以结束战斗。",
    "The signal lure is drawing minions in. Survive the assault and clean up what's left.": "信号诱饵正在吸引仆从。抵御进攻并清除剩余敌人。",
    "I'll place the lure and clear the minions.": "我会放置诱饵并清除这些仆从。",
    "Three escalating waves, then mop up the stragglers. I've got this.": "抵御三波逐渐增强的敌人，再清理残兵。我能应付。",
    "Contract complete. The minion assault has been cleared.": "契约完成，仆从进攻已经清除。",
    "ALERT: The Electric Demon has been spotted in the area. Stay grounded.": "警报：发现电魔。注意接地，避免触电。",
    "BOSS: The Electric Demon is here. Expect shock damage and ranged pressure.": "Boss：电魔已经出现。小心电击伤害和远程压制。",
    "ALERT: The Explosive Eagle has been spotted overhead. Expect fire and blast damage.": "警报：发现爆裂鹰在上空盘旋。小心火焰与爆炸伤害。",
    "BOSS: The Explosive Eagle is in the air. Watch for fire, gas, and blast pressure.": "Boss：爆裂鹰已经升空。小心火焰、毒气与爆炸冲击。",
    "EXECUTIONER HUNT: Survive six cultist waves, then execute The Executioner.": "行刑者猎杀：抵御六波邪教徒，然后处决行刑者。",
    "BOSS: The Executioner has entered the fight!": "Boss：行刑者加入了战斗！",
    "ALERT: The Sheriff has been sighted in the area. Prepare for impact.": "警报：发现警长。准备承受猛烈冲击。",
    "BOSS: The Sheriff is here. Expect burning shots and relentless pressure.": "Boss：警长已经出现。小心燃烧弹与持续压制。",
    "ALERT: The Hammer Guardian has been sighted in the area. Prepare for impact.": "警报：发现战锤守卫。准备承受猛烈冲击。",
    "BOSS: The Hammer Guardian is here. Expect crushing melee and relentless pressure.": "Boss：战锤守卫已经出现。小心毁灭性的近战攻击与持续压制。",
    "ALERT: Rockbreaker has surfaced nearby. Expect heavy impact damage.": "警报：碎岩者在附近现身。小心强烈的冲击伤害。",
    "BOSS: Rockbreaker is here. Keep moving and avoid the boulder volleys.": "Boss：碎岩者已经出现。保持移动，避开连续投来的巨石。",
    "ALERT: Siren Head has been heard in the area. The hunt begins only at night.": "警报：附近传来了警笛头的声音。猎杀只会在夜间开始。",
    "BOSS: Siren Head is entering the hunt zone. Expect long reach and relentless pressure.": "Boss：警笛头进入猎杀区域。小心它的超长攻击距离与持续压制。",
    "DRUID HUNT: Survive the animal waves, then face The Druid herself!": "德鲁伊猎杀：抵御动物波次，然后亲自面对德鲁伊！",
    "BOSS: The Druid steps into the fight!": "Boss：德鲁伊加入了战斗！",
    "ALERT: The Mechanician is moving with his full escort. Survive the waves, then kill the boss!": "警报：机械师正带着全部护卫赶来。抵御波次，然后击杀Boss！",
    "BOSS: The Mechanician has entered the fight. Finish this now!": "Boss：机械师加入了战斗，现在就解决他！",
    "Wave 1: Elite Headless escorts are advancing.": "第1波：无头者的精英护卫正在推进。",
    "Wave 2: The escort grows heavier.": "第2波：护卫部队更加强大。",
    "Final elite wave. Brace for the Headless boss.": "最后一波精英部队。准备迎战无头者Boss。",
    "BOSS: The Headless answers your lure.": "Boss：无头者响应了诱饵。",
}

BOSS_CONTRACT_EXACT.update({
    "ALERT: Dumdum's army is mobilizing. Survive five waves, then face the boss!": "警报：爆破狂人的军队正在集结。抵御五波进攻，然后迎战Boss！",
    "BOSS: Dumdum himself arrives. Finish this!": "Boss：爆破狂人亲自出现了，解决它！",
    "WAVE 1: Dumdum's hunt opens with a full push!": "第1波：爆破狂人的猎杀以全面进攻开始！",
    "WAVE 2: The pressure does not let up!": "第2波：敌人的攻势丝毫没有减弱！",
    "WAVE 3: Dumdum's raiders regroup and rush again!": "第3波：爆破狂人的袭击者重新集结，再次冲了上来！",
    "WAVE 4: Dumdum sends a heavier push!": "第4波：爆破狂人派出了更强的进攻部队！",
    "FINAL WAVE: Dumdum's last escort is closing before the boss!": "最后一波：Boss出现前，爆破狂人的最后一批护卫正在逼近！",
    "WAVE 1: The Electric Demons scouts are closing in.": "第1波：电魔的侦察部队正在逼近。",
    "WAVE 2: Stronger infected incoming. Feral and radiated signatures detected.": "第2波：更强的感染者正在接近，检测到凶暴与辐射目标。",
    "WAVE 4: The charged assault is intensifying. Hold your ground.": "第4波：充能感染者的攻势正在增强，守住阵地。",
    "WAVE 5: Final surge. Burn through the horde and brace for the boss.": "第5波：最后的攻势。消灭尸群，准备迎战Boss。",
    "WAVE 5: The line keeps burning!": "第5波：燃烧的战线仍在推进！",
    "FINAL WAVE: Break the last cultist push before The Executioner arrives!": "最后一波：在行刑者出现前粉碎邪教徒的最后攻势！",
    "WAVE 1: The first attackers are closing in under the Eagles shadow.": "第1波：第一批袭击者正在爆裂鹰的阴影下逼近。",
    "WAVE 2: Stronger infected incoming. Hold your ground.": "第2波：更强的感染者正在接近，守住阵地。",
    "WAVE 4: More radiated vultures are circling in. Stay moving.": "第4波：更多辐射秃鹫正在盘旋逼近，保持移动。",
    "WAVE 5: Final dive. Break the flock and brace for the Eagle.": "第5波：最后一次俯冲。击溃鸟群，准备迎战爆裂鹰。",
    "The hunt begins under cover of night. Push through the servants and kill The Ghost.": "猎杀在夜幕下开始。突破仆从的阻拦并消灭幽灵。",
    "The Phantom Court gathers. Survive the royal procession and slay its ruler.": "幻影王庭正在集结。抵御皇家仪仗队并杀死它们的统治者。",
    "Wave 1: Elite ghost minions are advancing.": "第1波：幽灵精英仆从正在推进。",
    "Wave 2: The elite escort grows heavier.": "第2波：精英护卫的力量更加强大。",
    "Wave 3: The elite escort keeps advancing.": "第3波：精英护卫仍在不断推进。",
    "BOSS: The Ghost answers your challenge.": "Boss：幽灵接受了你的挑战。",
    "SHERIFF WAVE 3: The Sheriff deputies are joining the assault.": "警长第3波：警长副手加入了进攻。",
    "SHERIFF WAVE 5: Final impact. Break the last Sheriff wave.": "警长第5波：最后的冲击。粉碎警长的最后一波部队。",
    "SHERIFF WAVE 3: The Sheriff deputies are tightening the ring.": "警长第3波：警长副手正在收紧包围圈。",
    "SHERIFF WAVE 5: The Sheriff line keeps breaking through.": "警长第5波：警长的部队仍在不断突破防线。",
    "SHERIFF WAVE 6: Final impact. Break this last wave before the Sheriff arrives.": "警长第6波：最后的冲击。在警长出现前击溃这批敌人。",
    "HAMMER WAVE 5: The Hammer line keeps breaking through.": "战锤第5波：战锤部队仍在不断突破防线。",
    "HAMMER WAVE 6: Final impact. Break this last wave before the Hammer Guardian arrives.": "战锤第6波：最后的冲击。在战锤守卫出现前击溃这批敌人。",
    "ALERT: Hellskyli has been sighted nearby. Survive the burnt wave before the demon dives in.": "警报：附近发现赫尔斯凯利。在恶魔俯冲前抵御烧焦感染者。",
    "WAVE 1: Burnt scouts are closing in beneath Hellskyli's flight path.": "第1波：烧焦侦察者正从赫尔斯凯利的飞行路线下逼近。",
    "WAVE 2: Feral and radiated burnt are pushing through the flames.": "第2波：凶暴与辐射烧焦感染者正在穿过火焰推进。",
    "WAVE 3: Charged and infernal burnt are erupting through the fireline.": "第3波：充能与炼狱烧焦感染者冲出了火线。",
    "WAVE 4: More infernal burnt are pouring through the blaze.": "第4波：更多炼狱烧焦感染者正从烈焰中涌出。",
    "WAVE 5: The fireline is still advancing. Hold the burn front.": "第5波：火焰战线仍在推进，守住燃烧前线。",
    "WAVE 6: Final fire surge. Hold before Hellskyli dives in.": "第6波：最后的烈焰攻势。在赫尔斯凯利俯冲前守住阵地。",
    "BOSS: Hellskyli is here. Expect fireballs, dive pressure, and relentless burning damage.": "Boss：赫尔斯凯利已经出现。小心火球、俯冲攻击和持续燃烧伤害。",
    "WAVE 1: The first brood of spiders is rushing your position.": "第1波：第一批蜘蛛正在冲向你的位置。",
    "WAVE 2: Feral and radiated spiders are closing in fast.": "第2波：凶暴与辐射蜘蛛正在迅速逼近。",
    "WAVE 3: Charged and infernal spiders are swarming ahead of the brood.": "第3波：充能与炼狱蜘蛛正成群冲在巢群前方。",
    "WAVE 4: More charged broodlings are rushing the perimeter.": "第4波：更多充能幼蛛正在冲击防线。",
    "WAVE 5: The brood keeps pressing. Hold the perimeter.": "第5波：蜘蛛巢群仍在施压，守住外围。",
    "WAVE 6: Another heavy brood surge is breaking through.": "第6波：又一股强大的蜘蛛攻势正在突破。",
    "WAVE 7: Final brood surge. Hold fast before Mykir drops in.": "第7波：蜘蛛巢群的最后攻势。在迈基尔降临前坚守阵地。",
    "ALERT: Mykir is nearby. Clear the spider brood before the boss descends on you.": "警报：迈基尔就在附近。在Boss降临前清除蜘蛛巢群。",
    "BOSS: Mykir has entered the hunt. Expect burst speed, fire pressure, and relentless jumps.": "Boss：迈基尔加入猎杀。小心爆发速度、火焰压制和连续跳跃攻击。",
    "WAVE 1: Party Beach is sending in the first wave of businessmen!": "第1波：海滩狂欢者派出了第一批商人感染者！",
    "WAVE 2: Feral businessmen are sprinting into the party zone!": "第2波：凶暴商人正冲进狂欢区域！",
    "WAVE 3: Radiated and charged businessmen are crashing the party zone!": "第3波：辐射与充能商人正在冲击狂欢区域！",
    "WAVE 4: More charged suits are piling onto the beach!": "第4波：更多充能西装感染者涌上了海滩！",
    "WAVE 5: Final suit rush. Hold the line before Party Beach arrives!": "第5波：西装感染者的最后冲锋。在海滩狂欢者出现前守住防线！",
    "ALERT: Party Beach has been spotted in the area. Survive the suit rush before the boss appears.": "警报：附近发现海滩狂欢者。在Boss出现前抵御西装感染者的冲锋。",
    "BOSS: Party Beach is here. Expect purse throws, leap pressure, and close-range chaos.": "Boss：海滩狂欢者已经出现。小心手提包投掷、跳跃压制和混乱的近身攻击。",
    "WAVE 1: Rockbreaker sends utility workers to test your line.": "第1波：碎岩者派出杂务工测试你的防线。",
    "WAVE 2: More workers incoming. Feral signatures are mixed in.": "第2波：更多工人正在接近，其中混有凶暴感染者。",
    "WAVE 3: Radiated workers are pushing behind the next break-in.": "第3波：辐射工人正跟随突破部队推进。",
    "WAVE 4: Charged workers are entering the pit. Pressure is rising.": "第4波：充能工人正在进入矿坑，压力不断上升。",
    "FINAL WAVE: Infernal workers are breaking through. The quarry is about to open.": "最后一波：炼狱工人正在突破，采石场即将全面开放。",
    "ALERT: The Running Kamikaze has been spotted in the area. Survive five waves and stay sharp!": "警报：附近发现狂奔神风者。抵御五波进攻，保持警惕！",
    "BOSS: The Running Kamikaze is here. He runs. He explodes. Don't miss.": "Boss：狂奔神风者已经出现。它会冲锋，也会爆炸——别打偏。",
    "WAVE 1: The Kamikaze's scouts are closing in!": "第1波：神风者的侦察部队正在逼近！",
    "WAVE 2: Stronger ones incoming — feral and radiated!": "第2波：更强的敌人正在接近——凶暴与辐射感染者！",
    "WAVE 3: Charged runners are joining the assault!": "第3波：充能奔跑者加入了进攻！",
    "WAVE 4: Charged and Infernal runners are breaking through!": "第4波：充能与炼狱奔跑者正在突破！",
    "FINAL WAVE: One last breaker rush before the Running Kamikaze arrives!": "最后一波：狂奔神风者出现前的最后一批突破部队！",
    "ULTIMATE: Two Singerie alphas are approaching together. Survive the full siege before the brothers arrive.": "终极挑战：两只辛格里首领正在同时接近。在这对兄弟出现前撑过整场围攻。",
    "WAVE 1: Burnt zombies are staggering toward the signal.": "第1波：烧焦僵尸正蹒跚着走向信号源。",
    "WAVE 2: Stronger burnt infected are closing in.": "第2波：更强的烧焦感染者正在逼近。",
    "FINAL WAVE: The burnt horde is peaking. Siren Head is next.": "最后一波：烧焦尸群已达到顶峰，接下来就是警笛头。",
    "WAVE 5: Final rush. Break the stampede before The Druid arrives!": "第5波：最后的冲锋。在德鲁伊出现前击溃兽群！",
    "WAVE 1: The Mechanician's knife crew is closing in.": "第1波：机械师的持刀小队正在逼近。",
    "WAVE 2: More Mechanician minions are pushing forward with fire and blades.": "第2波：更多机械师仆从正携带火焰与利刃向前推进。",
    "WAVE 3: The Mechanician's heavier crew is crashing into position.": "第3波：机械师的重装小队正在冲击阵地。",
    "WAVE 4: More Mechanician raiders are pushing in with fire and blades.": "第4波：更多机械师袭击者正携带火焰与利刃攻入阵地。",
    "WAVE 5: Final rush. Break this crew before The Mechanician arrives.": "第5波：最后的冲锋。在机械师出现前击溃这支小队。",
    "The hunt begins. Survive the Headless escorts and drag the boss into the open.": "猎杀开始。抵御无头者的护卫，把Boss引到开阔地带。",
    "Activate the rally, place the lure, survive five timed attack waves, and destroy the Electric Demon before it overloads the area.": "激活集结点并放置诱饵，抵御五波限时进攻，在电魔令区域过载前将其摧毁。",
    "High voltage contract. Trigger the rally, set the lure, survive five timed waves of pressure, and put the Electric Demon down.": "高压契约。触发集结点、放置诱饵，抵御五波限时进攻并消灭电魔。",
    "Activate the rally, place the lure, survive six timed waves of burning cultists, then kill The Executioner.": "激活集结点并放置诱饵，抵御六波燃烧邪教徒，然后击杀行刑者。",
    "Big contract. Trigger the rally, drop the lure, survive six timed cultist waves, then finish The Executioner himself.": "大型契约。触发集结点、放置诱饵，抵御六波邪教徒，然后亲手解决行刑者。",
    "Activate the rally, place the lure, survive five timed attack waves, and bring the Explosive Eagle down.": "激活集结点并放置诱饵，抵御五波限时进攻，然后击落爆裂鹰。",
    "Danger close. Trigger the rally, drop the lure, survive five timed waves of pressure, and kill the Explosive Eagle.": "危险逼近。触发集结点、放置诱饵，抵御五波限时进攻并击杀爆裂鹰。",
    "This is the real hunt. Trigger the rally, place the lure, survive the escort, and finish The Ghost.": "这才是真正的猎杀。触发集结点、放置诱饵，抵御护卫部队并消灭幽灵。",
    "Danger close. The Hammer Guardian is active nearby. Get there, trigger the rally, place the lure, survive six timed waves, and kill it.": "危险逼近。战锤守卫正在附近活动。前往目标地点，触发集结点并放置诱饵，抵御六波限时进攻后将其击杀。",
    "Danger close. The Sheriff is active nearby. Get there, trigger the rally, place the lure, survive six timed waves, and kill it.": "危险逼近。警长正在附近活动。前往目标地点，触发集结点并放置诱饵，抵御六波限时进攻后将其击杀。",
    "Activate the rally, place the lure, endure four timed Sheriff minion waves, then break the last push.": "激活集结点并放置诱饵，抵御四波警长仆从的限时进攻，然后粉碎最后一波攻势。",
    "Minion horde contract. Sheriff servants are gathering nearby. Get there, trigger the rally, place the lure, hold four timed waves, then break the last push.": "仆从尸群契约。警长的手下正在附近集结。前往目标地点，触发集结点并放置诱饵，守住四波限时进攻，再粉碎最后一波攻势。",
    "Burning air contact. Hellskyli is circling nearby. Trigger the rally, place the lure, survive six timed escort waves, and bring the boss down.": "空中发现燃烧目标。赫尔斯凯利正在附近盘旋。触发集结点并放置诱饵，抵御六波限时护卫部队，然后击落Boss。",
    "This is not a routine contract. Mykir is active nearby. Trigger the rally, place the lure, survive seven timed brood waves, then kill the boss.": "这不是普通契约。迈基尔正在附近活动。触发集结点并放置诱饵，抵御七波限时蜘蛛巢群，然后击杀Boss。",
    "Activate the rally, place the lure, survive five timed waves of businessmen, and kill Party Beach.": "激活集结点并放置诱饵，抵御五波限时商人感染者，然后击杀海滩狂欢者。",
    "Party Beach is active nearby. Get there, trigger the rally, place the lure, survive five timed suit waves, and finish the boss.": "海滩狂欢者正在附近活动。前往目标地点，触发集结点并放置诱饵，抵御五波限时西装感染者，然后解决Boss。",
    "Activate the rally, place the lure, survive five timed worker waves, and destroy Rockbreaker.": "激活集结点并放置诱饵，抵御五波限时工人感染者，然后消灭碎岩者。",
    "Quarry contract. Rockbreaker is moving through the wild. Find it, trigger the rally, place the lure, survive five timed worker waves, and bring it down.": "采石场契约。碎岩者正在荒野中移动。找到它，触发集结点并放置诱饵，抵御五波限时工人感染者后将其击杀。",
    "The Singerie is on the move with a full pack behind it. Survive the escort waves, then kill it.": "辛格里正带着完整兽群移动。抵御护卫波次，然后将其击杀。",
    "Elite contract. A stronger Singerie pack is roaming nearby. Trigger the rally, place the lure, survive the alpha waves, and kill the alpha.": "精英契约。一支更强的辛格里兽群正在附近游荡。触发集结点并放置诱饵，抵御首领波次并杀死首领。",
    "Night contract. Siren Head is roaming in the wild. Get there after dark, trigger the rally, place the lure, survive three timed waves, and put it down.": "夜间契约。警笛头正在荒野中游荡。天黑后前往目标地点，触发集结点并放置诱饵，抵御三波限时进攻后将其击杀。",
    "Activate the rally, place the lure, and survive three mixed Ghost and Headless assault waves.": "激活集结点并放置诱饵，抵御三波幽灵与无头者组成的混合进攻。",
    "The line is thicker tonight. Ghosts are screening a Headless push. Trigger the rally, place the lure, and hold through the assault.": "今晚的敌军更加密集，幽灵正在掩护无头者推进。触发集结点、放置诱饵并撑过这场进攻。",
    "I will place the lure and survive the mixed assault.": "我会放置诱饵并抵御混合部队的进攻。",
    "Activate the rally, place the lure, survive three elite Headless waves, then kill The Headless.": "激活集结点并放置诱饵，抵御三波无头者精英部队，然后击杀无头者。",
    "The lure can drag The Headless into the open, but its escorts will reach you first. Survive the waves and finish the boss.": "诱饵可以把无头者引到开阔地带，但它的护卫会先一步抵达。撑过所有波次，然后解决Boss。",
    "I will place the lure, survive the escort waves, and kill The Headless.": "我会放置诱饵，抵御护卫波次并击杀无头者。",
    "AEC HORDE ([DECEA3]{poi.distance} {poi.direction}[-]) [FF9900]{poi.name}[-]": "AEC尸潮（[DECEA3]{poi.distance} {poi.direction}[-]）[FF9900]{poi.name}[-]",
    "AEC LEGENDARY CONTRACT ([DECEA3]{poi.distance} {poi.direction}[-]) [FF9900]{poi.name}[-]": "AEC传奇契约（[DECEA3]{poi.distance} {poi.direction}[-]）[FF9900]{poi.name}[-]",
    "T4 POI Siege": "T4兴趣点围攻", "T6 Double Boss": "T6双Boss",
    "The breach has been sealed.": "突破口已经封锁。", "The position held. Good work.": "阵地守住了，干得好。",
    ",Survive wave 1": "，抵御第1波", ",Survive wave 2": "，抵御第2波", ",Survive wave 3": "，抵御第3波",
    ",Survive wave 4": "，抵御第4波", ",Survive wave 5": "，抵御第5波", "AEC POI DEFENSE": "AEC兴趣点防守",
    "Clear the POI then hold against Druid forces.": "清除兴趣点内的敌人，然后抵御德鲁伊部队。",
    "Clear the POI then hold against Executioner forces.": "清除兴趣点内的敌人，然后抵御行刑者部队。",
    "Clear the POI then hold against Ghost forces.": "清除兴趣点内的敌人，然后抵御幽灵部队。",
    "Clear the POI then hold against Mechanician forces.": "清除兴趣点内的敌人，然后抵御机械师部队。",
    "Clear the POI then hold against Singerie forces.": "清除兴趣点内的敌人，然后抵御辛格里部队。",
    "Witch T3 Defense": "女巫T3防守",
    "Contract: Survive five elite night waves then kill The Ghost.": "契约：抵御五波夜间精英部队，然后击杀幽灵。",
    "A bounty contract for Hellskyli. Survive six timed burnt waves and kill the burning demon.": "赫尔斯凯利悬赏契约。抵御六波限时烧焦感染者，然后击杀这只燃烧恶魔。",
    "A top-tier bounty contract for Mykir. Survive seven timed brood waves and kill one of the most dangerous bosses in the roster.": "迈基尔最高级悬赏契约。抵御七波限时蜘蛛巢群，然后击杀名册中最危险的Boss之一。",
    "A bounty contract for Party Beach. Survive five timed waves of businessmen and bring the boss down.": "海滩狂欢者悬赏契约。抵御五波限时商人感染者，然后击杀Boss。",
    "A bounty contract for Rockbreaker. Trigger the lure, survive five timed worker waves, then bring the quarry down.": "碎岩者悬赏契约。触发诱饵，抵御五波限时工人感染者，然后击杀采石场中的目标。",
    "Starts a green wilderness contract. Travel to the marked site, activate the lure, and survive three timed Headless assault waves.": "开启绿色荒野契约。前往标记地点，激活诱饵并抵御三波无头者限时进攻。",
    "Starts a green wilderness contract with three timed mixed Ghost and Headless assault waves.": "开启绿色荒野契约，抵御三波由幽灵与无头者组成的限时混合进攻。",
    "Contract: Survive three elite Headless waves and kill The Headless.": "契约：抵御三波无头者精英部队并击杀无头者。",
    "HUNT: Archer warbands are screening their master. Survive the waves and the boss will come.": "猎杀：弓箭手战帮正在掩护它们的主人。撑过所有波次，Boss就会出现。",
    "ASSAULT: Archer raiders are massing on your position. Brace for impact!": "进攻：弓箭手袭击者正在向你的位置集结，准备迎接冲击！",
    "WAVE 1: Archer raiders strike from the brush!": "第1波：弓箭手袭击者从灌木中发动攻击！",
    "WAVE 2: The Archer line tightens around you!": "第2波：弓箭手正在收紧对你的包围！",
    "FINAL WAVE: Break the Archer assault and hold the field!": "最后一波：粉碎弓箭手的进攻并守住战场！",
    "BOSS: The Archer Boss has entered the hunt. Stay mobile.": "Boss：弓箭手首领加入了猎杀，保持移动。",
    "WAVE 1: Archer minions are probing your defenses!": "第1波：弓箭手仆从正在试探你的防线！",
    "WAVE 2: More Archer minions are closing in!": "第2波：更多弓箭手仆从正在逼近！",
    "WAVE 3: Archer minions are tightening the kill zone!": "第3波：弓箭手仆从正在压缩杀戮区域！",
    "WAVE 4: Another Archer wave is on top of you!": "第4波：又一波弓箭手已经冲到面前！",
    "FINAL WAVE: The last Archer screen is pushing in now!": "最后一波：弓箭手的最后一批掩护部队正在推进！",
    "FINAL WAVE: The heaviest Archer screen is here. Boss incoming next!": "最后一波：弓箭手最强的掩护部队已经出现，接下来就是Boss！",
    "JOINT ASSAULT: Doomlord troops are advancing under Archer cover fire!": "联合进攻：毁灭领主部队正在弓箭手火力掩护下推进！",
    "WAVE 1: Mixed assault vanguard incoming!": "第1波：混合进攻的先锋部队正在接近！",
    "WAVE 2: More mixed troops press the attack!": "第2波：更多混合部队正在加强进攻！",
    "FINAL WAVE: Crush the last mixed assault line!": "最后一波：击溃混合部队的最后一道进攻线！",
    "Wave 1 begins. The Witch sends a mixed pack.": "第1波开始，女巫派出了混合怪群。",
    "Wave 2 begins. The mixed assault is growing.": "第2波开始，混合攻势正在增强。",
    "Wave 3 begins. The Witch's mixed assault keeps building.": "第3波开始，女巫的混合攻势仍在扩大。",
    "Wave 4 begins. The coven doubles down.": "第4波开始，女巫集会加大了攻势。",
    "Wave 1 begins. Witch crouch minions incoming.": "第1波开始，女巫爬行仆从正在接近。",
    "Wave 2 begins. More Witch crouch minions incoming.": "第2波开始，更多女巫爬行仆从正在接近。",
    "Wave 3 begins. The crouch horde is still growing.": "第3波开始，爬行尸群仍在扩大。",
    "Wave 4 begins. The crouch horde keeps coming.": "第4波开始，爬行尸群仍在不断涌来。",
    "Wave 5 begins. End the last crouch pack for good.": "第5波开始，彻底消灭最后一批爬行怪。",
    "Wave 1 begins. Spider minions are rushing in.": "第1波开始，蜘蛛仆从正在冲来。",
    "Wave 2 begins. More spider minions are inbound.": "第2波开始，更多蜘蛛仆从正在接近。",
    "Wave 3 begins. Spider minions keep pouring in.": "第3波开始，蜘蛛仆从仍在不断涌入。",
    "Wave 4 begins. More spider minions are spilling out.": "第4波开始，更多蜘蛛仆从正在涌出。",
    "Wave 5 begins. Break the last spider swarm now.": "第5波开始，立即击溃最后一群蜘蛛。",
    "Wave 5 begins. Break the coven's last push.": "第5波开始，粉碎女巫集会的最后攻势。",
    "Wave 5 begins. The coven keeps pressing.": "第5波开始，女巫集会仍在持续施压。",
    "Wave 6 begins. Break the coven's last push before The Witch arrives.": "第6波开始，在女巫出现前粉碎集会的最后攻势。",
    "The Witch reveals herself. Kill her now.": "女巫现身了，立即杀死她。",
    "The ritual reaches its peak. The Witch has entered the battle.": "仪式到达顶峰，女巫加入了战斗。",
    "Blue contract wave 1 incoming.": "蓝色契约第1波正在接近。", "Blue contract wave 2 incoming.": "蓝色契约第2波正在接近。",
    "Blue contract final wave incoming.": "蓝色契约最后一波正在接近。", "Green contract wave 1 incoming.": "绿色契约第1波正在接近。",
    "Green contract wave 2 incoming.": "绿色契约第2波正在接近。", "Green contract final wave incoming.": "绿色契约最后一波正在接近。",
    "Green contract cleanup wave incoming.": "绿色契约清理波次正在接近。",
    "Contract: Eliminate five waves of Doomlord base minions.": "契约：消灭五波毁灭领主基础仆从。",
    "Doomlord minions are closing in — get ready!": "毁灭领主的仆从正在逼近——做好准备！",
    "A reward cache from Doomlord Horde contracts. Contains useful supplies and possible mutation samples.": "来自毁灭领主尸潮契约的奖励箱，内含实用补给，并可能含有变异样本。",
    "An assault-grade reward bundle from Doomlord mixed-team contracts.": "来自毁灭领主混合小队契约的进攻级奖励包。",
    "Elite hunting reward from a Doomlord Hunt contract. Chance for rare mutation samples.": "毁灭领主猎杀契约的精英奖励，有机会获得稀有变异样本。",
    "Siege reward bundle from a Doomlord Siege contract. Best chance for the Core.": "毁灭领主围攻契约奖励包，获得核心的概率最高。",
    "Contract: Hunt down five waves of Doomlord Grenadiers.": "契约：消灭五波毁灭领主掷弹兵。",
    "Contract: Neutralize five Doomlord Rocket waves.": "契约：消灭五波毁灭领主火箭兵。",
    "Contract: Eliminate five waves of Doomlord Pistol troopers.": "契约：消灭五波毁灭领主手枪兵。",
    "Contract: Eliminate five waves of Doomlord Shotgun troopers in the wild.": "契约：在荒野中消灭五波毁灭领主霰弹枪兵。",
    "Contract: Clear five waves of Doomlord Snipers from the field.": "契约：从战场上清除五波毁灭领主狙击手。",
    "Contract: Hunt down five waves of Doomlord Magnum patrols in the open.": "契约：在开阔地带消灭五波毁灭领主马格南巡逻队。",
    "Contract: Stop five waves of Doomlord Knife Runners in the wild.": "契约：在荒野中阻止五波毁灭领主持刀奔跑者。",
    "Contract: Survive five waves of the explosive Demo Squad (Grenadiers + Rockets).": "契约：抵御五波爆破小队（掷弹兵与火箭兵）。",
    "Contract: Defend a position against three waves of Frontline assault troops (Shotgun + Knife).": "契约：守住阵地，抵御三波前线突击队（霰弹枪兵与持刀者）。",
    "Contract: Break through five waves of Overwatch suppression (Sniper + Magnum).": "契约：突破五波监视火力压制（狙击手与马格南守卫）。",
    "Contract: Engage and eliminate five waves of coordinated Fire Teams (Pistol + Magnum).": "契约：迎战并消灭五波协同火力小队（手枪兵与马格南守卫）。",
    "Contract: Survive six mixed elite waves then kill Doomlord Demolishman himself.": "契约：抵御六波混合精英部队，然后击杀毁灭领主爆破者本人。",
    "Contract: Defend a fortified location through four elite waves then slay Demolishman.": "契约：在加固地点抵御四波精英部队，然后击杀爆破者。",
    "Grenadier squad closing in — watch for grenades!": "掷弹兵小队正在逼近——小心手雷！",
    "Rocket troopers inbound — find cover NOW!": "火箭兵正在接近——立即寻找掩体！",
    "Pistol squad converging on your position!": "手枪小队正在向你的位置集结！",
    "Shotgun rush inbound — they won't stop!": "霰弹枪突击队正在接近——他们不会停下！",
    "Sniper nest established. Watch your head!": "狙击阵地已经建立，注意隐蔽！",
    "Magnum guard deploying — heavy fire incoming!": "马格南守卫正在部署——重火力即将来袭！",
    "Knife runners inbound — get them before they reach you!": "持刀奔跑者正在接近——别让他们靠近！",
    "Demo squad incoming — Grenadiers AND Rockets. Spread out!": "爆破小队来袭——掷弹兵和火箭兵都有，立刻分散！",
    "Frontline assault — Shotgun and Knife troops converging!": "前线进攻——霰弹枪兵与持刀者正在集结！",
    "Overwatch deployed — Snipers and Magnums on high ground!": "监视小队已经部署——狙击手与马格南守卫占据了高地！",
    "Fire team deployed — Pistols and Magnums closing in!": "火力小队已经部署——手枪兵与马格南守卫正在逼近！",
    "Doomlord forces are laying siege! Hold position!": "毁灭领主部队正在围攻，守住阵地！",
    "AEC ammo supply drop inbound.": "AEC弹药补给空投正在接近。",
    "WAVE 3: Keep holding — more are coming!": "第3波：继续坚守——还有更多敌人！",
    "WAVE 4: Another Doomlord assault is crashing into your line!": "第4波：毁灭领主的又一轮进攻正在冲击防线！",
    "WAVE 5: Final push. Break the last Doomlord wave!": "第5波：最后的攻势。击溃毁灭领主的最后一波部队！",
    "FINAL WAVE: The last Doomlord elite wave is here. Boss incoming next!": "最后一波：毁灭领主最后一批精英已经出现，接下来就是Boss！",
    "Wave challenge active. Survive each wave as the timer drops every five waves from 60 seconds to 20.": "波次挑战已启动。抵御所有波次；每完成五波，计时器会逐渐从60秒缩短至20秒。",
    "CONTRACT WAVE: Enemies are inbound. Grab the supply drops before the next timer expires.": "契约波次：敌人正在接近。在下一次倒计时结束前拾取补给空投。",
    "THE DOOMLORD ARRIVES — Demolishman himself is here!": "毁灭领主降临——爆破者本人出现了！",
    "Contract: Eliminate five waves of Doomlord Shotgun troopers in open terrain.": "契约：在开阔地带消灭五波毁灭领主霰弹枪兵。",
    "Contract: Destroy five waves of Doomlord Knife Runner ambushes in open terrain.": "契约：在开阔地带摧毁五波毁灭领主持刀奔跑者伏击队。",
    "Contract: Defend a location against three waves of Doomlord Grenadiers.": "契约：守住地点，抵御三波毁灭领主掷弹兵。",
    "Contract: Defend a location against three reduced waves of Doomlord Rocket troopers.": "契约：守住地点，抵御三波规模较小的毁灭领主火箭兵。",
    "Contract: Defend a location against three waves of Doomlord Pistol squads.": "契约：守住地点，抵御三波毁灭领主手枪小队。",
    "Contract: Defend a location against three waves of Doomlord Snipers.": "契约：守住地点，抵御三波毁灭领主狙击手。",
    "Contract: Defend a location against three waves of Doomlord base minions.": "契约：守住地点，抵御三波毁灭领主基础仆从。",
    "Contract: Survive seven waves of Doomlord champions, then kill Demolishman himself.": "契约：抵御七波毁灭领主冠军部队，然后击杀爆破者本人。",
    "The Doomlord Demolishman is dangerously close. Stay alert — he hits hard and summons waves of elite troops.": "毁灭领主爆破者就在附近。保持警惕——他的攻击极其凶猛，还会召唤一波波精英部队。",
    "[00DC50]LIFE DRAINED[-]": "[00DC50]生命被汲取[-]",
    "The Mushroom Boss has drained your life force. You feel the energy leave your body.": "菌菇Boss汲取了你的生命力，你能感觉到能量正从体内流失。",
    "The Mushroom Boss is dangerously close. Stay alert — his spores drain your life and his minions will overwhelm you.": "菌菇Boss就在附近。保持警惕——它的孢子会汲取生命，仆从也会迅速将你淹没。",
    "A reward bundle for clearing a wave of Mushroom minions.": "清除菌菇仆从波次后获得的奖励包。",
    "A premium reward bundle for hunting the Mushroom Boss.": "猎杀菌菇Boss后获得的高级奖励包。",
    "Contract: Survive three timed waves of Mushroom minions. Deploy the signal lure to begin.": "契约：抵御三波菌菇仆从的限时进攻。放置信号诱饵即可开始。",
    "Contract: Survive two timed waves of Mushroom minions — then the Boss itself appears.": "契约：抵御两波菌菇仆从的限时进攻，随后Boss本体将会出现。",
    "Deploy the signal lure and survive three timed waves of Mushroom minions.": "放置信号诱饵，抵御三波菌菇仆从的限时进攻。",
    "These fungal freaks are spreading faster than we can contain them. Trigger the lure and hold through three timed waves.": "这些真菌怪物扩散得太快，已经难以控制。触发诱饵并抵御三波限时进攻。",
    "Hold through three timed Mushroom waves. Don't let them overwhelm you.": "撑过三波菌菇怪的限时进攻，别让它们将你淹没。",
    "Contract accepted. I'll handle the mushroom horde.": "契约已接受，我会解决菌菇尸群。",
    "Colony cleared. The spores have stopped spreading — for now.": "菌落已经清除，孢子暂时停止了扩散。",
    "| [00C850]ASSAULT:[-] A mushroom strike group is moving on your position!": "| [00C850]进攻：[-]一支菌菇突击队正在向你的位置移动！",
    "| [00C850]WAVE 1:[-] First assault wave of Mushroom minions!": "| [00C850]第1波：[-]菌菇仆从的第一波进攻！",
    "| [00C850]WAVE 2:[-] Second assault wave incoming!": "| [00C850]第2波：[-]第二波进攻正在接近！",
    "| [00DC50]HUNT:[-] The Mushroom Boss's colony is mobilizing. Survive the waves — then face the Boss!": "| [00DC50]猎杀：[-]菌菇Boss的菌落正在动员。抵御所有波次，然后迎战Boss！",
    "| [FF0000]BOSS:[-] THE MUSHROOM BOSS arrives! Finish this!": "| [FF0000]Boss：[-]菌菇Boss已经出现，解决它！",
    "| [00DC50]WAVE 1:[-] Mushroom minions guard the boss!": "| [00DC50]第1波：[-]菌菇仆从正在保护Boss！",
    "| [00DC50]WAVE 2:[-] More guardians emerge!": "| [00DC50]第2波：[-]更多守卫出现了！",
    "The Mushroom Boss is dead. The colony will scatter without its core.": "菌菇Boss已死，失去核心的菌落很快就会瓦解。",
    "Deploy the signal lure. Survive two timed waves of minions — then face the Mushroom Boss itself.": "放置信号诱饵，抵御两波仆从的限时进攻，然后迎战菌菇Boss本体。",
    "The Mushroom Boss is out there. Its colony is expanding and its drain power is unlike anything I've seen. Survive the escorts, then put it down.": "菌菇Boss就在外面。它的菌落不断扩张，生命汲取能力也前所未见。抵御护卫部队，然后消灭它。",
    "Contract accepted. The Mushroom Boss dies today.": "契约已接受，今天就是菌菇Boss的死期。",
    "Survive the minion escort, then kill the Mushroom Boss. Don't let it drain you dry.": "抵御仆从护卫，然后击杀菌菇Boss。别让它把你的生命吸干。",
    "Both assault waves broken. The spore-walkers are retreating.": "两波进攻都已瓦解，孢子行者正在撤退。",
    "A larger spore-walker formation is converging. Set up at the signal point and survive two timed assault waves.": "一支规模更大的孢子行者部队正在集结。前往信号点做好准备，抵御两波限时进攻。",
    "This isn't just a patrol — it's full assault formation. Get to position and hold through two timed waves.": "这不只是巡逻队，而是完整的突击阵型。前往阵地并撑过两波限时进攻。",
    "Deploy the lure, survive the first wave, then hold through the second. Don't break position.": "放置诱饵，抵御第一波进攻，再守住第二波。不要离开阵地。",
    "Repel Two Waves of Spore-Walkers": "击退两波孢子行者",
    "Boss Dumdum is dangerously close. Stay alert - he hits hard and throws tanks.": "爆破狂人Boss就在附近。保持警惕——它攻击凶猛，还会投掷油罐。",
    "The Electric Demon is close. Expect shock damage and electrical pressure.": "电魔就在附近，小心电击伤害和持续的电流压制。",
    "The Executioner is close. Expect burning cultists, corpse projectiles, and an aggressive melee rush once he closes the distance.": "行刑者就在附近。小心燃烧邪教徒、尸体投射物，以及它靠近后的猛烈近战冲锋。",
    "The Explosive Eagle is close. Expect fire, explosions, and constant aerial pressure.": "爆裂鹰就在附近。小心火焰、爆炸和持续的空中压制。",
    "The Ghost is close. Your stamina drains under spectral pressure.": "幽灵就在附近，灵体威压会持续消耗你的耐力。",
    "The Hammer Guardian is close. Expect brutal melee hits and constant pressure.": "战锤守卫就在附近。小心残暴的近战重击与持续压制。",
    "Hellskyli is close. Expect fireball pressure, burning damage, and aggressive aerial dives.": "赫尔斯凯利就在附近。小心火球压制、燃烧伤害和猛烈的空中俯冲。",
    "Mykir is close. Expect spider waves, burst speed lunges, and heavy fire pressure from a boss built to overwhelm prepared players.": "迈基尔就在附近。小心蜘蛛波次、爆发式突进和猛烈火焰压制——即使准备充分，也可能被它迅速击溃。",
    "Party Beach is close. Expect purse throws, leap pressure, and waves of aggressive businessmen before the boss rushes in.": "海滩狂欢者就在附近。Boss冲入战场前，小心手提包投掷、跳跃压制和一波波凶猛的商人感染者。",
    "Rockbreaker is close. Expect heavy melee hits and ranged boulder pressure.": "碎岩者就在附近。小心沉重的近战攻击与远程巨石压制。",
    "[FFAA00]KAMIKAZE NEARBY[-]": "[FFAA00]神风者就在附近[-]",
    "The Running Kamikaze is close. Expect explosions.": "狂奔神风者就在附近，小心爆炸。",
    "The Singerie is close. Expect sustained pressure, brutal melee hits, and pack support.": "辛格里就在附近。小心持续压制、凶猛近战攻击和兽群支援。",
    "Siren Head is close. The oppressive signal drains stamina and keeps pressure on anyone nearby.": "警笛头就在附近。压迫性的信号会消耗耐力，并持续影响周围所有人。",
    "The Druid is close. Expect poison pressure, animal swarms, and escalating reinforcements from every direction.": "德鲁伊就在附近。小心毒素、动物群袭击，以及从四面八方不断增强的援军。",
    "Open to receive a random mutation sample from Dumdum's minions.": "打开后可获得一个爆破狂人仆从的随机变异样本。",
    "Open to receive mutation samples recovered after defending a position from the Electric Demon.": "打开后可获得防守阵地并击退电魔后回收的变异样本。",
    "Open to receive mutation samples from the Demonic Trio. Three Electric Demons. Triple the voltage.": "打开后可获得恶魔三重奏的变异样本。三只电魔，三倍电压。",
    "A bounty contract for the Electric Demon. Track it in the wilderness, survive five timed waves, and shut it down.": "电魔悬赏契约。在荒野中追踪目标，抵御五波限时进攻并将其摧毁。",
    "A defense contract. Reach the location first, hold three timed waves, and destroy the Electric Demon.": "防守契约。先抵达目标地点，守住三波限时进攻并摧毁电魔。",
    "A nightmare contract. Kill one Electric Demon, then survive the arrival of two more at once.": "噩梦契约。先击杀一只电魔，再同时迎战另外两只。",
    "Open to receive a random mutation sample reward earned from Executioner minion contracts.": "打开后可获得行刑者仆从契约奖励的随机变异样本。",
    "A survival contract against four timed waves of Executioner cultists, then one final breaker push in the wilderness.": "生存契约。在荒野中抵御四波行刑者邪教徒的限时进攻，再粉碎最后一波突破部队。",
    "Defend a POI through two timed waves, then break the final cultist push.": "在兴趣点抵御两波限时进攻，然后粉碎邪教徒的最后攻势。",
    "Hold a POI through four timed waves of cultists, then kill The Executioner on-site.": "在兴趣点抵御四波邪教徒的限时进攻，然后当场击杀行刑者。",
    "Open to receive mutation samples recovered after surviving the Explosive Eagle encounter.": "打开后可获得在爆裂鹰战斗中幸存后回收的变异样本。",
    "A bounty contract for the Explosive Eagle. Track it down, survive five timed waves, and bring it out of the sky.": "爆裂鹰悬赏契约。追踪目标，抵御五波限时进攻并将其从空中击落。",
    "A defense contract. Reach the location first, survive three timed waves, and kill the Explosive Eagle.": "防守契约。先抵达目标地点，抵御三波限时进攻并击杀爆裂鹰。",
    "A bounty contract for the Hammer Guardian. Trigger the hunt, endure six assault waves, then bring it down.": "战锤守卫悬赏契约。触发猎杀，抵御六波进攻后将其击杀。",
    "A defense contract. Reach the location, hold through four timed waves, then kill the Hammer Guardian.": "防守契约。抵达目标地点，守住四波限时进攻，然后击杀战锤守卫。",
    "A bounty contract for the Sheriff. Trigger the hunt, endure six assault waves, then bring it down.": "警长悬赏契约。触发猎杀，抵御六波进攻后将其击杀。",
    "A defense contract. Reach the location, hold through four timed waves, then kill the Sheriff.": "防守契约。抵达目标地点，守住四波限时进攻，然后击杀警长。",
    "ALERT: A Singerie pack has entered the area. Clear the escorts before they scatter.": "警报：一群辛格里进入了区域。在它们散开前清除护卫。",
    "DEFENSE: Singerie raiders are closing on this position. Expect thrown rocks and heavy rushes.": "防守：辛格里袭击者正在逼近阵地。小心投掷巨石和猛烈冲锋。",
    "ALERT: The Singerie has been sighted nearby. Its pack is already moving ahead of it.": "警报：附近发现辛格里，它的兽群已经先行推进。",
    "DEFENSE: The Singerie is advancing on this location. Hold the perimeter.": "防守：辛格里正在向这里推进，守住外围。",
    "ALERT: An alpha Singerie pack is in the area. This contract will escalate fast.": "警报：区域内发现辛格里首领兽群，本契约的强度会迅速提升。",
    "SIEGE: The heaviest Singerie assault is inbound. Hold the objective at all costs.": "围攻：辛格里最猛烈的攻势正在接近，不惜一切代价守住目标。",
    "WAVE 1: Singerie scouts are probing the area.": "第1波：辛格里侦察者正在试探区域。",
    "WAVE 2: More Singerie minions incoming. Expect harder hitters.": "第2波：更多辛格里仆从正在接近，小心更强的重击单位。",
    "FINAL WAVE: The last Singerie pack is rushing your position.": "最后一波：最后一群辛格里正在冲向你的位置。",
    "WAVE 1: Singerie throwers are opening the assault.": "第1波：辛格里投掷者发动了进攻。",
    "WAVE 2: The barrage is thickening. Brutes are pushing in.": "第2波：投射物愈发密集，蛮兽正在推进。",
    "FINAL WAVE: The Stone Barrage is peaking. Hold your ground.": "最后一波：巨石弹幕达到顶峰，守住阵地。",
    "WAVE 1: The Singerie pack is testing your defenses.": "第1波：辛格里兽群正在试探你的防线。",
    "WAVE 2: Stronger Singerie minions incoming from all sides.": "第2波：更强的辛格里仆从正从四面八方接近。",
    "WAVE 3: The escorts are collapsing inward. Pressure is rising.": "第3波：护卫部队正在向内收缩，压力不断上升。",
    "WAVE 4: The Jungle pack is regrouping for another heavy push.": "第4波：丛林兽群正在重新集结，准备再次猛攻。",
    "FINAL WAVE: The Jungle pack is breaking hard. The boss is close.": "最后一波：丛林兽群正在强行突破，Boss已经接近。",
    "WAVE 1: Alpha escorts have entered the zone.": "第1波：首领护卫进入了区域。",
    "WAVE 2: Heavier alpha minions incoming. Pressure is increasing.": "第2波：更强的首领仆从正在接近，压力不断增加。",
    "WAVE 3: The alpha pack is fully committed. Keep holding the line.": "第3波：首领兽群已经倾巢而出，继续守住防线。",
    "WAVE 4: The alpha warband renews the assault with heavier bodies.": "第4波：首领战帮派出更强壮的单位，再次发动进攻。",
    "LAST WAVE: The siege is peaking. The alpha target is moments away.": "最后一波：围攻达到顶峰，首领目标即将出现。",
    "BOSS: The Singerie has entered the hunt zone. Expect heavy melee pressure and thrown attacks.": "Boss：辛格里进入猎杀区域。小心沉重的近战压制和投掷攻击。",
    "BOSS: The Singerie has breached the defense line. Drop it before the position folds.": "Boss：辛格里突破了防线。在阵地崩溃前将其击杀。",
    "BOSS: The Singerie alpha is here. Finish it before the pack regroups.": "Boss：辛格里首领已经出现。在兽群重新集结前解决它。",
    "BOSS: The Singerie alpha has entered the siege. This is the final push.": "Boss：辛格里首领加入围攻，这是最后的攻势。",
    "BOSS: The Brothers are here together. Kill both alphas before the line collapses.": "Boss：兄弟二兽同时出现。在防线崩溃前击杀两只首领。",
    "ALERT: Druid minions are converging on your position!": "警报：德鲁伊仆从正在向你的位置集结！",
    "WAVE 5: Final rush. Break the last stampede.": "第5波：最后的冲锋，击溃最后一轮兽群踩踏。",
    "DEFENSE: Druid minions are swarming this location. Hold it!": "防守：德鲁伊仆从正在涌向这里，守住阵地！",
    "DEFENSE HUNT: Hold this ground through the waves and kill The Druid!": "防守猎杀：守住阵地并抵御所有波次，然后击杀德鲁伊！",
    "BOSS: The Druid has reached the siege line!": "Boss：德鲁伊已经抵达围攻前线！",
    "GAUNTLET: The Druid is unleashing her full menagerie. Ten waves. No breaks.": "试炼：德鲁伊放出了全部兽群。十波进攻，没有喘息时间。",
    "GAUNTLET WAVE 1: Feral chickens flood the field!": "试炼第1波：凶暴鸡涌入战场！",
    "GAUNTLET WAVE 2: Wolves are circling in!": "试炼第2波：狼群正在包围！",
    "GAUNTLET WAVE 3: The stags are charging!": "试炼第3波：雄鹿正在冲锋！",
    "GAUNTLET WAVE 4: Boars are rushing the zone!": "试炼第4波：野猪正在冲入区域！",
    "GAUNTLET WAVE 5: Giant explosive chickens inbound!": "试炼第5波：巨型爆炸鸡正在接近！",
    "GAUNTLET WAVE 6: Bears are entering the hunt!": "试炼第6波：熊群加入猎杀！",
    "GAUNTLET WAVE 7: The boss boars are here!": "试炼第7波：野猪首领出现了！",
    "GAUNTLET WAVE 8: C4 wolves are sprinting toward you!": "试炼第8波：C4狼正向你狂奔而来！",
    "GAUNTLET WAVE 9: Zombie bears and dire wolves are closing in!": "试炼第9波：僵尸熊和恐狼正在逼近！",
    "GAUNTLET FINAL WAVE: The full menagerie is charging!": "试炼最后一波：全部兽群正在冲锋！",
    "BOSS: The Druid emerges after the gauntlet. End it!": "Boss：德鲁伊在试炼后现身，结束这一切！",
    "ALERT: Mechanician minions are converging on your position. Prepare for contact!": "警报：机械师仆从正在向你的位置集结，准备接敌！",
    "WAVE 1: Mechanician minions are advancing!": "第1波：机械师仆从正在推进！",
    "WAVE 2: More of The Mechanician's crew are incoming!": "第2波：更多机械师手下正在接近！",
    "WAVE 3: The Mechanician's heavier crew is crashing into position!": "第3波：机械师的重装小队正在冲击阵地！",
    "WAVE 4: More Mechanician raiders are pushing in with fire and blades!": "第4波：更多机械师袭击者正携带火焰与利刃攻入阵地！",
    "WAVE 5: Final rush. Break the last crew!": "第5波：最后的冲锋，击溃最后一支小队！",
    "DEFENSE: Mechanician minions are swarming this location. Hold the position!": "防守：机械师仆从正在涌向这里，守住阵地！",
    "DEFENSE: The Mechanician's force is attacking this location. Survive the waves, then face the boss!": "防守：机械师部队正在进攻此处。抵御所有波次，然后迎战Boss！",
    "BOSS: The Mechanician is on-site. End the siege!": "Boss：机械师已经抵达，结束这场围攻！",
    "DEFENSE: The Electric Demon is moving on this position. Hold the perimeter.": "防守：电魔正在向阵地移动，守住外围。",
    "BOSS: The Electric Demon has breached the zone. Drop it before it overloads the area.": "Boss：电魔突破了区域。在它令此处过载前将其击杀。",
    "ALERT: The Demonic Trio is forming. The first Electric Demon is only the beginning.": "警报：恶魔三重奏正在形成，第一只电魔只是开始。",
    "BOSS: The first Electric Demon has entered the field. Bring it down fast.": "Boss：第一只电魔进入战场，尽快将其击杀。",
    "TRIO: Two more Electric Demons have appeared together. Finish them both.": "三重奏：另外两只电魔同时出现，将它们全部消灭。",
    "DEFENSE: The Explosive Eagle is moving on this position. Hold the perimeter.": "防守：爆裂鹰正在向阵地移动，守住外围。",
    "BOSS: The Explosive Eagle has entered the area. Drop it before it tears the site apart.": "Boss：爆裂鹰进入了区域。在它摧毁此处前将其击落。",
    "DEFENSE: The Sheriff is moving on this position. Hold the perimeter.": "防守：警长正在向阵地移动，守住外围。",
    "BOSS: The Sheriff has breached the zone. Bring it down before it overwhelms the area.": "Boss：警长突破了区域。在它压垮阵地前将其击杀。",
    "DEFENSE: The Hammer Guardian is moving on this position. Hold the perimeter.": "防守：战锤守卫正在向阵地移动，守住外围。",
    "BOSS: The Hammer Guardian has breached the zone. Bring it down before it overwhelms the area.": "Boss：战锤守卫突破了区域。在它压垮阵地前将其击杀。",
    "DEFENSE: Hellskyli is moving on this location. Hold through the burnt assault.": "防守：赫尔斯凯利正在向这里移动，撑过烧焦感染者的进攻。",
    "BOSS: Hellskyli has reached the defense point. Bring the burning demon down now.": "Boss：赫尔斯凯利抵达防守点，立即击落这只燃烧恶魔。",
    "DEFENSE: Mykir's brood is converging on this location. Hold the ground until the boss shows.": "防守：迈基尔的蜘蛛巢群正在向这里集结。守住阵地，直到Boss出现。",
    "BOSS: Mykir has reached the defense point. Break the brood mother now.": "Boss：迈基尔抵达防守点，立即消灭这只巢群之母。",
    "DEFENSE: Party Beach is moving on this location. Hold through the businessman waves.": "防守：海滩狂欢者正在向这里移动，撑过商人感染者波次。",
    "BOSS: Party Beach has reached the defense point. End the party now.": "Boss：海滩狂欢者抵达防守点，现在就结束这场狂欢。",
    "DEFENSE: Rockbreaker is advancing on this position. Fortify and hold.": "防守：碎岩者正在向阵地推进，加固并守住防线。",
    "BOSS: Rockbreaker has reached the objective. Break it before it breaks you.": "Boss：碎岩者抵达目标点。在它击垮你之前先击垮它。",
    "ALERT: Two quarry titans are stirring. Survive the worker assault first.": "警报：两只采石场巨兽已经苏醒。先抵御工人感染者的进攻。",
    "DOUBLE BREAK: Two Rockbreakers have surfaced together. Bring both down.": "双重破碎：两只碎岩者同时现身，将它们全部击杀。",
    "Lanterns sway in the dark. A ghost march is closing in.": "灯笼在黑暗中摇曳，一支幽灵行军队伍正在逼近。",
    "The night stirs. Fast ghost minions are rushing your position.": "夜色开始躁动，迅捷幽灵仆从正冲向你的位置。",
    "You hear stones and screams in the dark. The volley is coming.": "黑暗中传来石块与尖叫声，远程齐射即将到来。",
    "A hollow patrol is roaming nearby. Hold until every ghost falls.": "一支空洞巡逻队正在附近游荡。坚守阵地，直到所有幽灵倒下。",
    "The carnival lights are dead but the ghosts still dance.": "嘉年华的灯光已经熄灭，但幽灵仍在起舞。",
    "Keep watch. The graveyard dead are moving on this position.": "保持警戒，墓地亡者正在向阵地移动。",
    "The dead carnival has found this place. Defend it through the night.": "死亡嘉年华找到了这里，彻夜守住阵地。",
    "Shrieking stones cut through the dark. Ranged ghosts incoming.": "尖啸的石块划破黑暗，远程幽灵正在接近。",
    "The dead shift has clocked in. Hold the site until dawn or death.": "亡者班次已经开始。守住此处，直到黎明到来或生命终结。",
    "Ghost lanterns are breaching the perimeter. Get ready.": "幽灵灯笼正在突破外围，做好准备。",
    "A lost procession is crossing the night. Break it before it surrounds you.": "迷失的送葬队正穿过夜色。在它包围你之前将其击溃。",
    "The vigil is broken. Mixed ghost forces are coming for this place.": "守夜仪式已经被打破，幽灵混合部队正在逼近此处。",
    "The ground itself is haunted tonight. Clear the waves before they spread.": "今夜连大地本身都被诅咒。在诅咒扩散前清除所有波次。",
    "A hollow parade is marching on the objective. Do not let it pass.": "一支空洞游行队正在向目标推进，绝不能让它通过。",
    "Ghost hunt contract active. Survive the escorts and draw The Ghost out.": "幽灵猎杀契约已启动。抵御护卫部队并引出幽灵。",
    "Ghost defense contract active. Hold the site until The Ghost breaches it.": "幽灵防守契约已启动。守住地点，直到幽灵突破进来。",
    "Defense: The Ghost contract active. Hold this position until The Ghost arrives.": "防守：幽灵契约已启动。守住阵地，直到幽灵出现。",
    "A haunted siege has begun. Hold this ground until the Ghost is dead.": "闹鬼围攻已经开始。守住阵地，直到幽灵被消灭。",
    "Wave 1: Spectral lantern bearers are closing in.": "第1波：幽灵提灯者正在逼近。",
    "Wave 2: More spectral lanterns advance through the dark.": "第2波：更多幽灵灯笼正穿过黑暗推进。",
    "Final spectral lantern wave. Put the march down.": "幽灵灯笼的最后一波，终结这支行军队伍。",
    "Wave 1: Restless ghosts rush the zone.": "第1波：躁动幽灵冲入区域。",
    "Wave 2: The rush gets faster.": "第2波：敌人的冲锋速度更快了。",
    "Final rush. Cut them down before they break through.": "最后的冲锋。在它们突破前全部消灭。",
    "Wave 1: Bowler ghosts and screamers are firing from the dark.": "第1波：投石幽灵与尖啸者正从黑暗中发动远程攻击。",
    "Wave 2: The ghost volley intensifies.": "第2波：幽灵的齐射火力正在增强。",
    "Final volley. Silence the screamers and stone throwers.": "最后一轮齐射。让尖啸者与投石者彻底安静下来。",
})

NUMBER_ZH = {"two": "两", "three": "三", "four": "四", "five": "五", "six": "六", "seven": "七", "100": "100"}


def localized_boss_name(name: str) -> str:
    name = name.strip()
    if name.startswith("The "):
        name = name[4:]
    return BOSS_NAMES.get(name, {"Ghost": "幽灵", "Headless": "无头者"}.get(name, name))


def translate_contract_sentence(english: str):
    star = ""
    match = re.search(r"( \[[1-5]★\])$", english)
    if match:
        star = match.group(1); english = english[:match.start()]
    chinese = BOSS_CONTRACT_EXACT.get(english)
    if chinese is not None:
        return chinese + star

    patterns = [
        (r"Open to receive (?:random )?mutation samples from (?:Boss |The )?(.+?)(?:'s corpse)?: arm, leg, torso, head, or the rare (heart|core|fuse box)\.",
         lambda m: f"打开后可获得来自{localized_boss_name(m.group(1))}的随机变异样本：手臂、腿、躯干、头部，或稀有的{'心脏' if m.group(2) == 'heart' else '核心' if m.group(2) == 'core' else '保险丝盒'}。"),
        (r"Open to receive high-value mutation samples from (.+?): arm, leg, torso, head, or the rare heart\.",
         lambda m: f"打开后可获得来自{localized_boss_name(m.group(1))}的高价值变异样本：手臂、腿、躯干、头部，或稀有心脏。"),
        (r"Open to receive (.+?) mutation samples: arm, leg, torso, head, or the rare (?:burning )?heart\.",
         lambda m: f"打开后可获得{localized_boss_name(m.group(1))}的变异样本：手臂、腿、躯干、头部，或稀有心脏。"),
        (r"Activate the rally, place the lure, survive (two|three|four|five|six|seven) (?:timed |elite |mixed |burning |burnt |alpha |escort |brood |night |cultist |worker |assault )*waves, then kill (?:The )?(.+)\.",
         lambda m: f"激活集结点并放置诱饵，抵御{NUMBER_ZH[m.group(1)]}波敌人，然后击杀{localized_boss_name(m.group(2))}。"),
        (r"Activate the rally, place the lure, endure (four|five|six|seven) timed (?:assault |Sheriff minion )*waves, (?:and bring|then kill) (?:The )?(.+?) down\.",
         lambda m: f"激活集结点并放置诱饵，抵御{NUMBER_ZH[m.group(1)]}波限时进攻，然后击杀{localized_boss_name(m.group(2))}。"),
        (r"survive (three|four|five|six|seven) timed (?:attack |assault |burnt |burning cultist |brood |cultist |escort |suit |worker )*waves,? (?:then|and) kill (?:The )?(.+)\.",
         lambda m: f"抵御{NUMBER_ZH[m.group(1)]}波限时进攻，然后击杀{localized_boss_name(m.group(2))}。"),
        (r"survive (three|four|five|six|seven) (?:mixed |elite |alpha |escort |night |cultist )*waves,? then kill (?:The )?(.+)\.",
         lambda m: f"抵御{NUMBER_ZH[m.group(1)]}波敌人，然后击杀{localized_boss_name(m.group(2))}。"),
        (r"Survive (three|four|five|six|seven) timed waves, then kill (?:The )?(.+)\.",
         lambda m: f"抵御{NUMBER_ZH[m.group(1)]}波限时进攻，然后击杀{localized_boss_name(m.group(2))}。"),
        (r"Activate the rally at night, place the lure, survive the (.+) waves, and kill (.+)\.",
         lambda m: f"在夜间激活集结点并放置诱饵，抵御{localized_boss_name(m.group(1))}的部队，然后击杀{localized_boss_name(m.group(2))}。"),
        (r"Hold the zone, activate the rally, place the lure, and kill (?:The )?(.+)\.",
         lambda m: f"守住区域，激活集结点并放置诱饵，然后击杀{localized_boss_name(m.group(1))}。"),
        (r"I will activate the rally, place the lure, survive the (?:timed|alpha) waves, and kill (?:The )?(.+)\.",
         lambda m: f"我会激活集结点并放置诱饵，抵御所有波次，然后击杀{localized_boss_name(m.group(1))}。"),
        (r"Contract complete\. The minion assault has been cleared\.", lambda m: "契约完成，仆从的进攻已经清除。"),
        (r"The (.+) is dead\. Good work\.", lambda m: f"{localized_boss_name(m.group(1))}已死，干得好。"),
    ]
    for pattern, render in patterns:
        found = re.fullmatch(pattern, english, flags=re.IGNORECASE)
        if found:
            return render(found) + star
    return None


def translate_boss_contracts() -> None:
    path = ROOT / "03-AEC-AIO_BOSS_EXTREME_EDITION/Config/Localization.csv"
    lines = path.read_text(encoding="utf-8-sig").splitlines(keepends=True)
    header = next(csv.reader([lines[0]])); cols = {x.lower(): i for i, x in enumerate(header)}; e, z = cols["english"], cols["schinese"]
    changed = 0
    for i in range(1, len(lines)):
        line = lines[i]; ending = "\r\n" if line.endswith("\r\n") else "\n" if line.endswith("\n") else ""; row = next(csv.reader([line.removesuffix(ending)]))
        if len(row) <= max(e, z) or row[e].strip() != row[z].strip(): continue
        chinese = translate_contract_sentence(row[e])
        if chinese is None: continue
        row[z] = chinese; out = io.StringIO(); csv.writer(out, lineterminator=ending).writerow(row); lines[i] = out.getvalue(); changed += 1
    with path.open("w", encoding="utf-8-sig", newline="") as handle: handle.write("".join(lines))
    print(f"03-AEC-AIO_BOSS_EXTREME_EDITION/Config/Localization.csv: updated {changed} contract entries")


translate_boss_contracts()


PZ98_EXACT = {
    "Eliminate infected": "消灭感染者", "Eliminate elite infected": "消灭精英感染者",
    "Eliminate radiated infected": "消灭辐射感染者", "Eliminate AEC T05 infected": "消灭AEC T05感染者",
    "Eliminate enhanced AEC targets (T03)": "消灭强化AEC目标（T03）",
    "Eliminate enhanced AEC targets (T06)": "消灭强化AEC目标（T06）",
    "Eliminate enhanced AEC targets (T09)": "消灭强化AEC目标（T09）",
    "Eliminate enhanced AEC targets (T12)": "消灭强化AEC目标（T12）",
    "Eliminate enhanced AEC targets (T15)": "消灭强化AEC目标（T15）",
    "Eliminate AEC Burnt specimens": "消灭AEC烧焦感染体", "Eliminate AEC Chuck specimens": "消灭AEC查克感染体",
    "Eliminate Project Z": "消灭Project Z感染者",
    "[FFD700][AEC][-] The Biker": "[FFD700][AEC][-] 骑手", "[FFD700][AEC][-] The Exorcist": "[FFD700][AEC][-] 驱魔者",
    "[FFD700][AEC][-] The Explorer": "[FFD700][AEC][-] 探索者", "[FFD700][AEC][-] Family Wagon": "[FFD700][AEC][-] 家庭旅行车",
    "[FFD700][AEC][-] The Giant": "[FFD700][AEC][-] 巨人", "[FFD700][AEC][-] The Hoverboard": "[FFD700][AEC][-] 悬浮滑板",
    "[FFD700][AEC][-] The Jumper": "[FFD700][AEC][-] 跃行者", "[FFD700][AEC][-] The Killer": "[FFD700][AEC][-] 杀手",
    "[FFD700][AEC][-] Old Wagon": "[FFD700][AEC][-] 老式旅行车", "[FFD700][AEC][-] Tower Bus": "[FFD700][AEC][-] 高塔巴士",
    "[FFD700][AEC][-] Car Tower": "[FFD700][AEC][-] 汽车高塔", "[FFD700][AEC][-] The War Ram": "[FFD700][AEC][-] 战争冲车",
    "[FFD700][AEC][-] Weapon Wheel": "[FFD700][AEC][-] 武装巨轮", "[FFD700][AEC][-] The Wheelchair": "[FFD700][AEC][-] 轮椅",
    "[FFD700][AEC][-] The Zombie Hunter": "[FFD700][AEC][-] 僵尸猎手", "[FFD700][AEC][-] Unicycle": "[FFD700][AEC][-] 独轮车",
    "[FFFFFF]MUTATION SAMPLES[-]": "[FFFFFF]变异样本[-]",
    "[FFFFFF][-][8EBE67]RESEARCH[-][FFFFFF] | Material[-]": "[FFFFFF][-][8EBE67]研究[-][FFFFFF] | 材料[-]",
    "[FFFFFF][-][7DFFCF]RESEARCH[-][FFFFFF] | Samples[-]": "[FFFFFF][-][7DFFCF]研究[-][FFFFFF] | 样本[-]",
    "This modifier grants immunity to any burning or heat effects from bosses.\\n\\n[DECEA3]Installed into chest armor.[-]": "该改装件可使你免疫Boss造成的燃烧和高温效果。\\n\\n[DECEA3]安装于胸甲。[-]",
    "[FFD700][AEC][-] [FFFFFF]Extreme Mod Crafting Table[-]": "[FFD700][AEC][-] [FFFFFF]极限改装工作台[-]",
    "[FFD700][AEC][-] All-In-One Lure": "[FFD700][AEC][-] 全能诱饵",
    "[FFD700][AEC][-] Signal Lure": "[FFD700][AEC][-] 信号诱饵",
    "[FFD700][AEC][-] Call Drop Model": "[FFD700][AEC][-] 空投呼叫模型",
    "[FFD700][AEC][-] Call for Airdrop": "[FFD700][AEC][-] 呼叫空投",
    "[FFD700][AEC][-] Bandit M60": "[FFD700][AEC][-] 强盗M60",
    "[FFD700][AEC][-] Model Connexion Card": "[FFD700][AEC][-] 连接卡模型",
    "[D1D5FF]Contents[-]\\n\\n[FFFFFF]10[-] random decorative blocks.": "[D1D5FF]内容[-]\\n\\n随机获得[FFFFFF]10[-]个装饰方块。",
    "[F87C63][Project Z][-] Small bait": "[F87C63][Project Z][-] 小型诱饵",
}


PZ98_KEY_EXACT = {
    "itemPZAECBossLootBundleT03Desc": "[99FF99]内容[-]\\nProject Z基础弹药、补给和资源。\\n\\n[AAAAAA]入门级Boss奖励。[-]",
    "itemPZAECBossLootBundleT06Desc": "[99FF99]内容[-]\\n改良弹药、补给和合金。\\n\\n[FFD866]额外奖励[-]\\n有较低概率获得稀有战利品。",
    "itemPZAECBossLootBundleT09Desc": "[99FF99]内容[-]\\n更多补给、合金和稀有改装件。\\n\\n[FFD866]额外奖励[-]\\n有概率获得独特奖励。",
    "itemPZAECBossLootBundleT12Desc": "[99FF99]内容[-]\\n高级补给和装备。\\n\\n[FFD866]额外奖励[-]\\n获得独特改装件、技能奖励和武器的概率更高。",
    "itemPZAECBossLootBundleT15Desc": "[99FF99]内容[-]\\n最高等级的Boss奖励。\\n\\n[FFD866]最佳掉落概率[-]\\n可能获得独特改装件、传奇武器和稀有部件。",
    "aecMutationIncubatorDesc": "[FFD866]用途[-][FFFFFF]\\n将五个等级的[-][8EBE67]«变异样本»[-][FFFFFF]分别熔炼成独立储备。每个实体样本会为对应等级增加1单位储备。为避免队列瞬间堆积，每个样本的熔炼时间限制为1秒。\\n\\n[-][FFD866]标签页I——«研究材料»[-][FFFFFF]\\nI级×15→1；II级×1→1；III级×1→2；IV级×1→3；V级×1→4。每份产物会在2秒内制成带有明确标签的材料包。\\n\\n[-][FFD866]标签页II——«变异样本»[-][FFFFFF]\\n所有等级均按1:1还原：1单位储备→1个样本。制造时间：2秒。[-]",
    "aec_tp8_l1_text": "使用[FFCC33]变异孵化器[-]可分别储存各等级样本，并按1:1还原为原等级样本。无法跨等级升级或降级。",
    "aec_tp8_l4_text": "[FFFFFF]第一个制造标签页可将任意等级的储备转换为[-][8EBE67]«研究材料»[-][FFFFFF]。第二个标签页则从同一储备中重建对应等级的样本；反复转换会有意造成材料损耗。[-]",
    "modpackTabNecro_aecNecroforgeAmmo44": "[C792EA]亡灵锻炉[-] | .44马格南弹药",
    "modpackTabNecro_aecNecroforgeAmmo762": "[C792EA]亡灵锻炉[-] | 7.62毫米弹药",
    "itemAECArcherMinionAssaultContract_desc": "[FFFFFF]契约：在荒野中击退弓箭手的全面进攻。抵御三波限时攻势，然后击杀最终突破防线的3只僵尸。[-]",
    "itemAECDoomlordArcherMinionAssaultContract_desc": "[FFFFFF]契约：在荒野中击退毁灭领主与弓箭手组成的联合部队。抵御三波限时混合攻势，然后击杀最终突破防线的3只僵尸。[-]",
    "itemAECHammerGuardianMinionHordeContract_desc": "[FFFFFF]波次契约。在荒野中坚守四波限时的战锤守卫仆从攻势，然后粉碎最后一轮进攻。[-]",
    "itemAECHeadlessGhostMinionAssaultContract_desc": "[FFFFFF]启动一份绿色荒野契约，抵御三波由幽灵与无头者组成的限时混合攻势。[-]",
    "itemAECHeadlessMinionAssaultContract_desc": "[FFFFFF]启动一份绿色荒野契约。前往标记地点，激活诱饵，并抵御三波限时的无头者攻势。[-]",
    "itemAECMinionAssaultContract_desc": "[FFFFFF]启动一份绿色荒野契约。前往标记地点，激活集结点并放置诱饵，然后抵御三波、每波5只仆从的进攻。[-]",
    "itemAECMushroomMinionAssaultContract_desc": "[FFFFFF]契约：抵御两波限时的菌菇孢子行者攻势。放置信号诱饵即可开始。[-]",
    "itemAECSheriffMinionHordeContract_desc": "[FFFFFF]波次契约。在荒野中坚守四波限时的警长仆从攻势，然后粉碎最后一轮进攻。[-]",
    "itemAECSingerieAlphaHuntContract_desc": "[FFFFFF]精英辛格里契约。在开阔地带抵御五波逐渐增强的首领攻势，并击杀首领。[-]",
    "PassiveBoozeBarrel001": "[FFD700][AEC][-] 酿造\\n\\n随时间被动产出一种随机[DECEA3]酒类饮品[-]。\\n\\n每30分钟产出[DECEA3]1份酒类饮品[-]。",
    "pzaecModpackProgressionBundleDesc": "[5ECFFF]整合包进度路线[-]\\n这并非必须完成的剧情战役，而是整合包各条主要成长路线的路标。完成Project Z生存入门后，商人会交给你此物。打开后请保留其中章节：[FFD866]新家[-]与[FFD866]资源收集者[-]会立即开启，同时还会解锁AEC车库、市场、研究、档案和辐射威胁路线。原版的“更强武器”章节仍会单独发放。\\n\\n[FF8A65]重要提示[-]\\n请勿丢弃起始章节，也不要取消其中的一次性任务。任务链遗失后可能需要管理员才能恢复。\\n\\n",
    "aecQuestArchiveRadioDesc": "整合包的实地指南工作台。所有指南始终会集中显示在同一列表中；选择任意指南即可阅读完整说明。制作一份实体副本需要1张纸。",
    "PZAEC_GuideTab_AECEO": "AEC终局改造",
    "PZAEC_GuideTab_ProjectZ": "Project Z",
    "pzaec_aec_hub_title": "[FFD700][AEC][-] [FFFFFF]选择行动类别：[-]",
    "pzaec_aec_standard_hub_title": "[55FF55]标准行动[-] [AAAAAA]— AEC T00–T05[-]",
    "pzaec_aec_elite_hub_title": "[55CFFF]精英行动[-] [AAAAAA]— AEC T06–T10[-]",
    "pzaec_aec_master_hub_title": "[FF66CC]大师行动[-] [AAAAAA]— AEC T11–T15[-]",
    "pzaec_aec_open_standard": "[55FF55]标准行动[-] [AAAAAA]（AEC T00–T05）[-]",
    "pzaec_aec_open_elite": "[55CFFF]精英行动[-] [AAAAAA]（AEC T06–T10）[-]",
    "pzaec_aec_open_master": "[FF66CC]大师行动[-] [AAAAAA]（AEC T11–T15）[-]",
    "pzaec_aec_return_to_hub": "[AAAAAA]← 返回行动类别[-]",
    "pzaec_aec_return_to_elite": "[AAAAAA]← 返回精英行动[-]",
    "pzaec_aec_return_to_standard_menu": "[AAAAAA]← 返回标准行动[-]",
    "pzaec_aec_return_to_master_menu": "[AAAAAA]← 返回大师行动[-]",
    "pzaec_aec_next_poi_size": "下一个兴趣点规模 →",
    "pzaec_aec_prev_poi_size": "← 上一个兴趣点规模",
}

for _tier, _source in {
    1: "普通、凶暴、辐射、充能和炼狱形态的原版僵尸。更强的形态可能掉落多个T1样本，但样本等级不会因此提高。",
    2: "AEC终局改造T05–T07感染者，以及Project Z的辐射/充能精英。此等级的仆从可能掉落多个样本。",
    3: "AEC终局改造T08–T10感染者，以及Project Z炼狱精英。中等威胁的具名Boss也可能属于这一档。",
    4: "AEC终局改造T11–T13感染者，以及危险的Project Z具名Boss。Boss的掉落数量会随其实际耐久度提高。",
    5: "AEC终局改造T14–T15感染者，以及最强的Project Z具名Boss。这是终局阶段价值最高的样本等级。",
}.items():
    _ratio = {1: "15个I级样本→1份", 2: "1个II级样本→1份", 3: "1个III级样本→2份", 4: "1个IV级样本→3份", 5: "1个V级样本→4份"}[_tier]
    PZ98_KEY_EXACT[f"resourceAECMutationSampleT{_tier}Desc"] = f"[FF8A65]重要物品[-]\\n请勿丢弃此资源；整合包后续进度会用到它。\\n\\n[5ECFFF]获取方式[-]\\n{_source}\\n\\n[8EBE67]精炼[-]\\n{_ratio}[8EBE67]研究材料[-]。\\n\\n[FFD866]还原[-]\\n熔炼后，每1单位储备可还原为1个同等级的变异样本。\\n\\n"


GUIDE_WARNING = "[FF8A65]重要提示[-]\\n请勿取消这个一次性任务，也不要丢弃关联章节或任务物品。任务链遗失后可能需要管理员才能恢复。\\n\\n"


def add_guide(keys: tuple[str, ...], title: str, body: str, objective: str) -> None:
    value = f"{title}\\n{body}\\n\\n[5ECFFF]目标[-]\\n{objective}\\n\\n{GUIDE_WARNING}"
    for key in keys:
        PZ98_KEY_EXACT[key] = value


add_guide(("PZAEC_Guide_Market_02_Desc", "pzaecGuideMarket02Desc"), "[66CFFF]僵尸市场2/8——取得终端[-]", "僵尸市场是一座物流工作台，可处理物资申请、加密货币挖矿、连接卡、AEC硬币、研究论文和被动生产。本章只确认你已经取得市场终端。", "取得[FFD866]僵尸市场[-]。")
add_guide(("PZAEC_Guide_Market_03_Desc", "pzaecGuideMarket03Desc"), "[66CFFF]僵尸市场3/8——加密货币矿机[-]", "加密货币矿机是第一种必须实际部署的市场设施。申请物品与交付后的成品方块，是同一次配送的两个不同阶段。", "取得[FFD866]加密货币矿机申请单[-]，接收[FFD866]加密货币矿机[-]并将其放置到世界中。")
add_guide(("PZAEC_Guide_Market_04_Desc", "pzaecGuideMarket04Desc"), "[66CFFF]僵尸市场4/8——服务器电池[-]", "服务器电池构成加密货币挖矿设施的升级阶梯。本章以500 Ah电池介绍“提出申请—接收货物”的流程。", "取得[FFD866]500 Ah服务器电池申请单[-]，并收到[FFD866]500 Ah服务器电池[-]。")
add_guide(("PZAEC_Guide_Market_05_Desc", "pzaecGuideMarket05Desc"), "[66CFFF]僵尸市场5/8——连接卡[-]", "连接卡会把特定Boss系列及星级接入市场与研究经济。第一张卡只是入门示例，后续系列遵循相同规则。", "取得初始的[FFD866]爆破狂人0★连接卡[-]。")
add_guide(("PZAEC_Guide_Market_07_Desc", "pzaecGuideMarket07Desc"), "[66CFFF]僵尸市场7/8——研究论文[-]", "研究论文代表经过整理的Boss系列情报。它会在基础市场资源之后出现，用于仅靠原始战利品或硬币已无法推进的阶段。", "取得[FFD866]1份研究论文[-]。")
add_guide(("PZAEC_Guide_Market_08_Desc", "pzaecGuideMarket08Desc"), "[66CFFF]僵尸市场8/8——被动生产[-]", "后期市场设施会随时间产出实用资源，是一项长期投资：先取得申请单和设施，再让设施逐渐收回成本。", "取得[FFD866]被动酿酒桶申请单[-]和[FFD866]被动酿酒桶[-]本体。")

PZ98_KEY_EXACT.update({
    "PZAEC_Guide_Research_Root_Name": "[FFD700][AEC\\PZ][-] [C78CFF]研究档案[-]——[FFFFFF]第一份样本[-]",
    "pzaecGuideResearchRoot": "[FFD700][AEC\\PZ][-] [C78CFF]研究档案[-]——[FFFFFF]第一份样本[-]",
    "PZAEC_Guide_Mutator_01_Name": "[FFD700][AEC\\PZ][-] [C78CFF]通往强大力量之路[-] [DECEA3]1/4[-]——[FFFFFF]极限工作台[-]",
    "pzaecGuideMutator01": "[FFD700][AEC\\PZ][-] [C78CFF]通往强大力量之路[-] [DECEA3]1/4[-]——[FFFFFF]极限工作台[-]",
    "PZAEC_Guide_Mutator_03_Name": "[FFD700][AEC\\PZ][-] [C78CFF]通往强大力量之路[-] [DECEA3]3/4[-]——[FFFFFF]变异器I[-]",
    "pzaecGuideMutator03": "[FFD700][AEC\\PZ][-] [C78CFF]通往强大力量之路[-] [DECEA3]3/4[-]——[FFFFFF]变异器I[-]",
    "pzaecGuideMutator04": "[FFD700][AEC\\PZ][-] [C78CFF]通往强大力量之路[-] [DECEA3]4/4[-]——[FFFFFF]代币与变异器II[-]",
})
add_guide(("PZAEC_Guide_Research_Root_Desc", "pzaecGuideResearchRootDesc"), "[C78CFF]研究档案——第一份样本[-]", "T1变异样本是进入研究经济的起点。样本是重要的进度材料，在了解孵化器和变异器路线之前，不要随意消耗或丢弃。", "取得[FFD866]1个T1变异样本[-]。")
add_guide(("PZAEC_Guide_Mutator_01_Desc", "pzaecGuideMutator01Desc"), "[C78CFF]通往强大力量之路1/4——极限工作台[-]", "AEC极限改装工作台是制造各级变异器的专用设施。建造它还会开启独立的亡灵锻炉路线。", "取得[FFD866]AEC极限改装工作台[-]。")
add_guide(("PZAEC_Guide_Mutator_02_Desc", "pzaecGuideMutator02Desc"), "[C78CFF]通往强大力量之路2/4——空白变异器[-]", "空白变异器是力量升级路线中可重复使用的初始形态。本章重点是理解升级顺序，而不是直接跳到后期等级。", "制造或取得[FFD866]空白变异器[-]。")
add_guide(("PZAEC_Guide_Mutator_03_Desc", "pzaecGuideMutator03Desc"), "[C78CFF]通往强大力量之路3/4——变异器I[-]", "这是第一件完整的力量变异器。从此阶段起，系统会消耗越来越珍贵的研究资源。", "取得[FFD866]力量变异器I[-]。")
add_guide(("PZAEC_Guide_Mutator_04_Desc", "pzaecGuideMutator04Desc"), "[C78CFF]通往强大力量之路4/4——代币与变异器II[-]", "通用AEC代币来自AEC任务，而非普通僵尸掉落。本章会把这种任务货币与下一等级的变异器衔接起来。", "至少保留[FFD866]1枚通用AEC代币[-]，并取得[FFD866]力量变异器II[-]。")

add_guide(("PZAEC_Guide_Necro_01_Desc", "pzaecGuideNecro01Desc"), "[FF9F5E]亡灵锻炉1/2——工作台[-]", "亡灵锻炉是用于分级弹药转化的AEC专用工作台。当你的样本经济足以承担消耗后，它才会真正发挥价值。", "取得[FFD866]亡灵锻炉工作台[-]。")
add_guide(("PZAEC_Guide_Necro_02_Desc", "pzaecGuideNecro02Desc"), "[FF9F5E]亡灵锻炉2/2——首次转化[-]", "亡灵锻炉会用普通弹药和变异样本制造分级弹药包。本章以入门级9毫米弹药包作为第一个示例。", "取得[FFD866]9毫米T00亡灵锻炉弹药包[-]。")
add_guide(("PZAEC_Guide_Incubator_Desc", "pzaecGuideIncubatorDesc"), "[C78CFF]研究档案——变异孵化器[-]", "变异孵化器连接着T1–T5原始样本与整合包研究经济。放置后会从这里开启变异器、任务中继站和研究材料三条路线。", "取得[FFD866]变异孵化器[-]并将其放置到世界中。")

_relay_names = {1: "任务中继站", 2: "1★中继站", 3: "2★中继站", 4: "3★中继站", 5: "4★中继站", 6: "5★中继站"}
_relay_bodies = {
    1: "基础任务中继站能让你在家中接入AEC契约系统。后续章节会沿着星级阶梯升级同一套设施。",
    2: "第一颗星会提高可用契约的等级上限。之后每章都遵循同一流程：升级、放置，再迈向下一等级。",
    3: "继续提高契约等级上限。更高等级的中继站应随着你的战斗能力逐步解锁，不宜急于跳级。",
    4: "三星中继站会进入真正危险的Boss契约阶段。武器、医疗物资和弹药都足以应付后再升级。",
    5: "四星中继站已经属于终局设施。请把接下来的契约视为终局挑战，而非普通商人任务。",
    6: "五星是这条进度路线的最终中继站等级，会开放指南规划中的最高级契约。",
}
for _step in range(1, 7):
    if _step > 1:
        _name_value = f"[FFD700][AEC\\PZ][-] [A7D96C]神秘猎人[-] [DECEA3]{_step}/6[-]——[FFFFFF]{_relay_names[_step]}[-]"
        for _key in (f"PZAEC_Guide_Relay_{_step:02d}_Name", f"pzaecGuideRelay{_step:02d}"):
            PZ98_KEY_EXACT[_key] = _name_value
    _objective = f"取得并放置[FFD866]{_relay_names[_step]}[-]。"
    add_guide((f"PZAEC_Guide_Relay_{_step:02d}_Desc", f"pzaecGuideRelay{_step:02d}Desc"), f"[A7D96C]神秘猎人{_step}/6——{_relay_names[_step]}[-]", _relay_bodies[_step], _objective)

add_guide(("PZAEC_Guide_ResearchMaterial_Desc", "pzaecGuideResearchMaterialDesc"), "[C78CFF]研究档案——研究材料[-]", "研究材料是通过孵化器生产的通用加工资源，将样本经济与后续多条Project Z/AEC路线衔接起来。", "取得[FFD866]1份研究材料[-]。")

_radio_titles = {1: "广播站", 2: "任务笔记", 3: "稀有契约", 4: "灵药"}
_radio_bodies = {
    1: "Project Z广播站会开启恢复后的稀有契约路线。这套系统独立于AEC猎杀大师和任务中继站契约。",
    2: "任务笔记是在广播站兑换稀有契约包时消耗的材料。在准备好接受随机任务之前，请妥善保留。",
    3: "稀有契约包并不包含固定任务；打开时会从恢复后的21份Project Z契约中随机选出一份。",
    4: "稀有契约最终会提供特殊原料和灵药。本章以T1生命原料和灵药为例，介绍最后的奖励制造步骤。",
}
_radio_objectives = {1: "取得[FFD866]广播站[-]。", 2: "至少取得[FFD866]1份任务笔记[-]。", 3: "取得[FFD866]稀有契约包[-]。", 4: "取得[FFD866]T1生命原料[-]和[FFD866]T1生命灵药[-]。"}
for _step in range(1, 5):
    if _step != 3:
        _name_value = f"[FFD700][AEC\\PZ][-] [FFD966]连接外界[-] [DECEA3]{_step}/4[-]——[FFFFFF]{_radio_titles[_step]}[-]"
        PZ98_KEY_EXACT[f"PZAEC_Guide_Radio_{_step:02d}_Name"] = _name_value
        PZ98_KEY_EXACT[f"pzaecGuideRadio{_step:02d}"] = _name_value
    add_guide((f"PZAEC_Guide_Radio_{_step:02d}_Desc", f"pzaecGuideRadio{_step:02d}Desc"), f"[FFD966]连接外界{_step}/4——{_radio_titles[_step]}[-]", _radio_bodies[_step], _radio_objectives[_step])

_medicine_names = {1: "变异原浓缩液", 2: "平衡剂"}
for _step in (1, 2):
    _name_value = f"[FFD700][AEC\\PZ][-] [FF8A65]“惩罚性”医学[-] [DECEA3]{_step}/2[-]——[FFFFFF]{_medicine_names[_step]}[-]"
    PZ98_KEY_EXACT[f"PZAEC_Guide_Medicine_{_step:02d}_Name"] = _name_value
    PZ98_KEY_EXACT[f"pzaecGuideMedicine{_step:02d}"] = _name_value
add_guide(("PZAEC_Guide_Medicine_01_Desc", "pzaecGuideMedicine01Desc"), "[FF8A65]“惩罚性”医学1/2——变异原浓缩液[-]", "变异原浓缩液是Project Z实验型兴奋剂的基础材料。这些化合物效果强大，但每一种都用于应对特定危机，并非普通治疗药。", "取得[FFD866]变异原浓缩液[-]。")
add_guide(("PZAEC_Guide_Medicine_02_Desc", "pzaecGuideMedicine02Desc"), "[FF8A65]“惩罚性”医学2/2——平衡剂[-]", "实验医学包含平衡剂、清澈视野和辐射外壳，分别针对不同威胁。本任务要求取得其中第一种。", "取得[FFD866]兴奋剂«平衡剂»[-]。")

for _key in ("PZAEC_Guide_Ammo_Name", "pzaecGuideAmmo"):
    PZ98_KEY_EXACT[_key] = "[FFD700][AEC\\PZ][-] [FF9F5E]末日弹药[-]"
add_guide(("PZAEC_Guide_Ammo_Desc", "pzaecGuideAmmoDesc"), "[FF9F5E]末日弹药[-]", "Project Z的部分弹药物流会先提供申请箱，而不是直接交付成品弹药。申请物品只是进入补给流程的凭证，并非子弹本身。", "取得[FFD866]小型7.62毫米弹药申请箱[-]。")
add_guide(("PZAEC_Guide_Archive_Desc", "pzaecGuideArchiveQuestDesc"), "[66B3FF]实地档案[-]", "档案无线电现已改为单纯的资料工作台。所有实地指南始终集中在一张列表中，选择配方即可阅读完整说明；制造一份实体副本只需1张纸。", "取得[FFD866]档案无线电[-]。")

for _step, _name in {1: "防护", 2: "最高防护"}.items():
    _value = f"[F87C63][PZ][-] [90FF75]辐射威胁[-] [DECEA3]{_step}/2[-]——[FFFFFF]{_name}[-]"
    PZ98_KEY_EXACT[f"pzaecGuideRadiation{_step:02d}"] = _value
    PZ98_KEY_EXACT[f"PZAEC_Guide_Radiation_{_step:02d}_Name"] = _value
add_guide(("pzaecGuideRadiation01Desc", "PZAEC_Guide_Radiation_01_Desc"), "[90FF75]辐射威胁1/2——防护[-]", "Project Z将主动辐射暴露、累积辐射量和辐射伤害分开处理。第一套防护会教你抑制主动暴露，并使用抗辐射药清除部分已累积剂量。", "收集全部四种基础[FFD866]辐射防护[-]护甲改装件和[FFD866]抗辐射药[-]。")
add_guide(("pzaecGuideRadiation02Desc", "PZAEC_Guide_Radiation_02_Desc"), "[90FF75]辐射威胁2/2——最高防护[-]", "改良型辐射防护板会再增加一层保护。辐射外壳是一种应急兴奋剂，可暂时阻止新的辐射累积，但药效结束时会反弹25%的辐射量。", "收集全部四种改良型[FFD866]辐射防护[-]板和[FFD866]辐射外壳[-]。")


_field_titles = {
    "pzaecFG_AECEO01": (11, "AEC", "游戏阶段与AEC等级"),
    "pzaecFG_AECEO02": (12, "AEC", "新玩家保护"),
    "pzaecFG_AECEO03": (15, "AEC", "热度：局部压力"),
    "pzaecFG_AECEO04": (16, "AEC", "危险度：威胁等级"),
    "pzaecFG_AECEO05": (13, "AEC", "AEC动态生成"),
    "pzaecFG_AECEO06": (17, "AEC", "世界事件"),
    "pzaecFG_AECEO07": (37, "AEC", "AEC宿敌"),
    "pzaecFG_AECEO08": (36, "AEC", "AEC血月"),
    "pzaecFG_AECEO09": (18, "AEC", "样本、牙齿与研究"),
    "pzaecFG_AECEO10": (1, "AEC", "整合包进度与实地档案"),
    "pzaecFG_AECEO11": (19, "AEC", "僵尸市场"),
    "pzaecFG_AECEO12": (20, "AEC", "孵化器、样本与变异器"),
    "pzaecFG_AECEO13": (21, "AEC", "AEC代币与契约"),
    "pzaecFG_AECEO14": (29, "AEC", "亡灵锻炉与弹药转化"),
    "pzaecFG_AECBoss01": (22, "AEC", "猎杀大师：可重复契约"),
    "pzaecFG_AECBoss02": (24, "AEC", "Boss系列与星级"),
    "pzaecFG_AECBoss03": (23, "AEC", "契约类型"),
    "pzaecFG_AECBoss04": (28, "AEC", "100波与极限契约"),
    "pzaecFG_AECBoss05": (27, "AEC", "Boss猎人技能"),
    "pzaecFG_AECBoss06": (26, "AEC", "连接卡与研究论文"),
    "pzaecFG_AECBoss07": (25, "AEC", "任务中继站"),
    "pzaecFG_PZ01": (2, "Project Z", "耐力储备"),
    "pzaecFG_PZ02": (3, "Project Z", "抗压能力"),
    "pzaecFG_PZ03": (4, "Project Z", "卫生状况"),
    "pzaecFG_PZ04": (6, "Project Z", "特质与恐惧"),
    "pzaecFG_PZ05": (7, "Project Z", "冷热与环境适应"),
    "pzaecFG_PZ06": (8, "Project Z", "毒素与解毒剂"),
    "pzaecFG_PZ07": (30, "Project Z", "辐射：暴露、剂量与伤害"),
    "pzaecFG_PZ08": (10, "Project Z", "通用部件"),
    "pzaecFG_PZ09": (14, "Project Z", "Project Z生态区与生物"),
    "pzaecFG_PZ11": (32, "Project Z", "稀有契约与灵药"),
    "pzaecFG_PZ12": (34, "Project Z", "高级工作台与科技"),
    "pzaecFG_PZ13": (9, "Project Z", "击杀奖励与采集"),
    "pzaecFG_PZ14": (5, "Project Z", "休息、床铺与沐浴"),
    "pzaecFG_PZ15": (33, "Project Z", "耕作与水培"),
    "pzaecFG_PZ16": (35, "Project Z", "反应堆科技与充能"),
    "pzaecFG_PZ17": (31, "Project Z", "实验医学"),
}
for _key, (_number, _mod, _topic) in _field_titles.items():
    _mod_tag = "[FFD700][AEC][-]" if _mod == "AEC" else "[F87C63][Project Z][-]"
    PZ98_KEY_EXACT[_key] = f"[777777]{_number:02d}.[-] {_mod_tag} [66B3FF]实地指南[-] | [FFFFFF]{_topic}[-]"


_field_bodies = {
    "pzaecFG_AECEO01Desc": "AEC主要读取[FFD866]游戏阶段（GS）[-]，而不只看人物等级。T00–T04依次为普通、凶暴、辐射、充能和炼狱形态；真正的终局等级约从GS 600以上的T05老兵开始，一直延伸至T15神话。任务、兴趣点和脚本事件可以无视普通生成规则强制指定等级，并不代表你的GS突然改变。",
    "pzaecFG_AECEO02Desc": "AEC不会一开始就把全部终局压力丢给新角色。GS 0–99时，主要世界系统通常不会将你选为目标，附近的强化随机生成也会被阻止。GS 100–599时热度和危险度已经会运作，但普通世界生成仍不会自由出现T05以上压力；约在GS 600后才解除主要限制。任务、兴趣点、血月和脚本内容仍可能使用独立规则。",
    "pzaecFG_AECEO03Desc": "在同一区域击杀的合格敌人越多，AEC越会注意这里。[FFD866]热度[-]是随战斗上升、停战后逐渐冷却的局部压力。约70%时系统开始警告，达到100%便可触发升级事件。若想降低压力，可以停手、离开区域或等待冷却；热度是世界对战斗的反应，不是任务进度。",
    "pzaecFG_AECEO04Desc": "热度表示眼下有多喧闹，[FFD866]危险度[-]则记录一个生态区长期累积的威胁。危险度共有0–5级，等级越高，符合条件的玩家今后遭遇越严酷；它不会在安静一分钟后立刻下降。让热度彻底冷却并维持足够长的平静，危险度才会开始回落。",
    "pzaecFG_AECEO05Desc": "AEC不会使用一张固定的当前生成表。运行时生成器会综合[FFD866]游戏阶段、生态区、时间、实体密度与当地限制[-]，再选择允许出现的系列和等级。首领、护卫、密度上限及额外压力分别处理，领地石附近还会抑制部分压力。血月、沉睡者、任务和脚本生成使用的是独立规则。",
    "pzaecFG_AECEO06Desc": "AEC威胁不一定始于附近的兴趣点。系统可发起[FFD866]世界事件[-]，包括入侵、大型尸潮、压力变化和危险生物的特殊袭击。HUD与服务器聊天会播报重要事件；这些信息属于实时世界系统，而不是已停用的旧剧情。事件有持续时间，但没有存活玩家在线时，计时可能暂停，以免事件在空世界中结束。",
    "pzaecFG_AECEO07Desc": "AEC宿敌是原生的持久敌人系统；每条记录会跨重启保留姓名、称号、阶级、仇恨、领地和战斗历史。基础设置最多保留40名宿敌，低于30名时补充；各阶级目标数为10/10/10/6/5/3。宿敌遭遇仍保留复仇、家族报复、权力斗争、领地战争和迁徙等机制。",
    "pzaecFG_AECEO08Desc": "AEC血月拥有独立控制器，用于决定压力、后期等级和Boss，并非把普通荒野生成简单放大。系统会评估玩家准备程度及自身波次规则；[FFD866]热度与危险度不会直接决定血月组成[-]，所以平静的生态区也不代表尸潮之夜容易。请按GS、装备、弹药和队伍实力准备，长期Boss压力往往需要不同的基地策略。",
    "pzaecFG_AECEO10Desc": "本整合包不再用旧AEC终局剧情推动进度。完成Project Z入门后，商人会提供[FFD866]整合包进度指南[-]并开启实用路线；无需独立任务的机制则记录在实地档案中。打开档案无线电，从统一列表选择指南即可在配方说明中阅读全文；制作实体副本只需[FFD866]1张纸[-]。此版本建议新开存档，旧剧情书籍、头颅、等级代币和兼容战役均不受支持。",
    "pzaecFG_AECEO12Desc": "[FFD866]T1–T5变异样本[-]是原料，孵化器会将其熔炼为便于管理的分级储备。储备既可制成[FFD866]研究材料[-]，也能还原成同等级实体样本。研究材料比例为T1 15→1、T2 1→1、T3 1→2、T4 1→3、T5 1→4；同级样本还原为1:1。极限工作台会依次消耗样本、材料和代币制造变异器，后期配方需求会大幅提高。",
    "pzaecFG_AECEO13Desc": "[FFD866]通用AEC代币[-]不会从普通僵尸身上随机掉落，而是AEC任务奖励，并用于变异器等后期进度。主要来源是完成各契约系统提供的AEC契约；难度越高通常报酬越好。代币是重要进度资源，请勿丢弃，也不要与商人货币或僵尸市场硬币混为一谈。",
    "pzaecFG_AECEO14Desc": "[FFD866]亡灵锻炉[-]是后期AEC工作台，可将普通弹药转化为分级战斗弹药包。它不会取代普通工作台，只有稳定取得变异样本后才值得使用。放入指定口径的普通弹药和相应价值的样本即可制造所选等级的弹药包，等级越高成本越大；若样本仍稀缺，应先建立研究经济。",
    "pzaecFG_AECBoss01Desc": "[FFD866]猎杀大师[-]无人机管理可重复进行的AEC Boss契约，并非完成一次就退场的剧情NPC。任务可能包含Boss、仆从、防守、围攻、兴趣点场景或波次战；完成一单不会结束系统。开始前务必阅读任务类型和威胁，有些契约最好远离主基地激活。",
    "pzaecFG_AECBoss02Desc": "AEC Boss分为[FFD866]系列和星级[-]。同一系列升星后不一定还是同一场战斗，高星版本危险得多，也会推动更深层研究。星级同时用于契约、连接卡和研究链，因此必须同时确认系列与目标等级；能轻松击杀早期版本，不代表同一配装足以对付下一星。",
    "pzaecFG_AECBoss03Desc": "AEC Boss契约并非全是“找到并击杀一只Boss”，还包括[FFD866]猎杀、防守、混合尸潮、兴趣点场景、围攻和波次挑战[-]。防守考验阵地与维修物资，猎杀考验机动性和单体伤害，长波次则考验弹药、治疗与基地耐久。激活前先看清条件，熟悉的名字也可能对应极危险的场景。",
    "pzaecFG_AECBoss04Desc": "[FFD866]100波[-]契约和其他极限场景是可选的后期挑战，并非必须完成的教程。它们要求成熟配装、大量补给，有时还需要队伍。弹药、维修、医疗和撤离方案比纸面多一点伤害更重要，地点也必须经得住长期战斗。跳过不会中断进度，这些内容用于以额外风险换取后期奖励。",
    "pzaecFG_AECBoss05Desc": "[FFD866]Boss猎人[-]页签不会同时强化你对所有Boss的能力，各分支针对特定系列专精。后期等级除伤害和防御外，还可能抵抗某个Boss的专属机制，包括部分辐射效果。不要只看名字就投入点数；先找出阻碍进度的Boss系列，再为该目标专精。",
    "pzaecFG_AECBoss06Desc": "[FFD866]连接卡[-]会把特定Boss系列和星级接入僵尸市场与研究经济；[FFD866]研究论文[-]则是下一层经过处理的情报。先学会早期系列卡，再把相同原理用于其他目标与星级；连接卡指明路线，论文把猎杀成果转为后续进度。卡片、硬币和论文用途不同，配方要求的资源不能互相替代。",
    "pzaecFG_PZ01Desc": "普通耐力表示你现在还能否冲刺或再次挥击；[FFD866]耐力储备[-]记录整趟行动中的长期疲劳，所以普通耐力回满时，橙色储备条仍可能很低。长时间使用工具和近战会逐渐消耗储备，严重不足会引发疲劳。放慢节奏并真正休息；相关技能可降低消耗或加快恢复，但咖啡和短暂停顿无法永远替代睡眠。",
    "pzaecFG_PZ02Desc": "紫色条显示[FFD866]剩余抗压能力[-]：100%代表健康，数值下降表示角色越来越难以承受当前处境。黑暗、恐惧、危险生物，尤其是附近的高威胁敌人会持续消耗它，个人特质也会改变损失与恢复。夜间携带光源、远离压倒性危险并在安全处休息；有时在数值崩溃前撤退才是正确选择。",
    "pzaecFG_PZ03Desc": "[FFD866]卫生状况[-]是真实的角色状态，而非装饰条。角色会随时间变脏，负重和某些特质还会加快这一过程。卫生过差会逐渐干扰整体状态，并与压力等问题叠加；最好在行动间隙处理。使用清洁用品并沐浴，浴缸能在家中快速恢复卫生，也会帮助其他状态恢复。",
    "pzaecFG_PZ04Desc": "Project Z特质不是单纯用技能点购买的普通技能，许多特质会根据行为、习惯、恐惧和适应过程出现、增强或消失。它们会改变角色对黑暗、动物、僵尸、火焰、疼痛、污垢、群体等环境的反应，有利也有弊。请定期查看特质页及触发条件；若某项特质造成困扰，解决办法可能是改变行为，而不是再花一点技能点。",
    "pzaecFG_PZ05Desc": "Project Z扩展了温度状态，酷热与严寒会经历多个阶段，甚至在敌人出现前就严重削弱角色。衣物、庇护所、火源、水和合适消耗品比硬扛最终阶段更重要，部分技能和特质可逐渐提高恶劣生态区适应力。若气候已在损害生命或耐力，应先稳定状态再战斗；两种威胁同时发生通常远比一种危险。",
    "pzaecFG_PZ06Desc": "部分Project Z生物会施加独立的[FFD866]中毒[-]状态。医疗包能治疗伤害，却不会自动清除毒素。中毒会逐渐恶化，并妨碍移动、耐力和继续作战的能力，反复使用治疗物品无法解决根源。猎杀有毒生物时，把[FFD866]解毒剂[-]放在快捷位置；先阻止毒素，再恢复其造成的后果。",
    "pzaecFG_PZ07Desc": "Project Z辐射分为[FFD866]主动暴露、累积辐射量和辐射伤害[-]三层，一件物品无法同样解决全部问题。完整辐射防护套、部分护甲套装和技能可抑制主动效果；快速适应主要减轻辐射伤害，不会直接清空剂量。抗辐射药能立刻清除部分累积量并加快恢复；控制主动暴露后，净化套件会改善自然排除速度。",
    "pzaecFG_PZ08Desc": "[FFD866]通用部件[-]把旧版多种Project Z专用零件整合为一种通用材料，并会在高级制造中再次使用。部分兼容装备和零件可回收成这种资源，因此卖掉陌生部件前先检查能否拆解。它既不是垃圾，也不是一次性任务物品，建议在家中保留一批库存。",
    "pzaecFG_PZ09Desc": "本整合包中的Project Z生物不会平均分布，其比例同时取决于[FFD866]生态区与游戏阶段[-]。松树林最温和，PZ敌人较少且Boss不会在普通荒野生成；烧焦森林、沙漠和雪地逐步提高精英与Boss压力，[FFD866]荒原[-]允许最广、最危险的组合。沉睡者、任务和脚本生成独立处理，兴趣点或事件中的例外并不违反生态区规则。",
    "pzaecFG_PZ11Desc": "Project Z稀有契约与AEC任务互相独立。在[FFD866]广播站[-]消耗[FFD866]任务笔记[-]，可获得一个契约包，随机抽取恢复后的[FFD866]21份契约[-]之一。完成后可取得制造灵药的特殊原料，部分奖励属于后期强力消耗品。Project Z稀有契约可与AEC猎杀大师契约同时存在，二者使用不同物品和奖励路线。",
    "pzaecFG_PZ12Desc": "后期进度会超出原版工作台能力。Project Z使用改良制造站和专用工作台工具，因此看似熟悉的配方可能需要完全不同的设施。若找不到配方，请检查[FFD866]所需工作台、工作台工具、相关技能和进度条件[-]；部分高级化学品、装备和特殊部件无法在基础工作台制造。新工作台通常是补充旧设施，并非立即彻底取代。",
    "pzaecFG_PZ14Desc": "Project Z中的休息不只是打发时间。睡袋、床垫和床铺会恢复[FFD866]耐力储备[-]与抗压能力，良好寝具通常比短暂停顿恢复得更好。沐浴可快速恢复卫生，也有助于放松和净化。普通耐力满值不等于角色已充分休息；多个状态条同时偏低时，应回到安全处休息、清洗并重新准备。",
    "pzaecFG_PZ15Desc": "Project Z耕作不止普通田地；技能、专用设备和后期工作台会让食物生产更稳定，减少对好运收成的依赖。[FFD866]水培站[-]为受支持作物提供独立的加速生长周期，并非所有种子都能放入，作物种类和效率取决于进度。配方锁定时请检查农业技能与工作台要求，通常只是尚未解锁对应生产等级。",
    "pzaecFG_PZ16Desc": "反应堆钻机、驱动器和相关设施构成Project Z独立的后期能源路线；对这些工具而言，[FFD866]耐久度实际代表电量[-]，不只是磨损。专用充电站会从电网恢复能量，工具用完后显示“损坏”时，应先检查充电设施，而不是普通修理包。便携反应堆、反应堆电池和充电站属于同一循环，设备越多，基地供电规划越重要。",
    "pzaecFG_PZ17Desc": "[FFD866]变异原浓缩液[-]用于Project Z后期化学，制造针对特定问题的实验型兴奋剂。[FFD866]平衡剂[-]防止击倒与眩晕，[FFD866]清澈视野[-]阻止改变视觉的效果，[FFD866]辐射外壳[-]暂时阻止辐射累积并加快净化，但结束时会产生[FF8A65]+25%辐射反弹[-]。先弄清需要对抗的威胁，再使用对应药物。",
}
for _key, _body in _field_bodies.items():
    _mod_tag = "[F87C63][Project Z][-]" if "_PZ" in _key else "[FFD700][AEC][-]"
    PZ98_KEY_EXACT[_key] = f"{_mod_tag} [66B3FF]实地指南[-]\\n{_body}\\n\\n"

CROPS = {
    "Aloe": "芦荟", "Blueberries": "蓝莓", "Chrysanthemums": "菊花", "Coffee Beans": "咖啡豆",
    "Corn": "玉米", "Cotton": "棉花", "Goldenrod": "黄花", "Grace Corn": "格蕾丝玉米",
    "Hops": "啤酒花", "Mushrooms": "蘑菇", "Radiated Mushrooms": "辐射蘑菇", "Potatoes": "土豆",
    "Pumpkins": "南瓜", "Yucca Fruit": "丝兰果",
}


def translate_98_generated() -> None:
    path = ROOT / "98-AECxProjectZ_Tweaks/Config/Localization.csv"
    raw_lines = path.read_text(encoding="utf-8-sig").splitlines(keepends=True)
    header = next(csv.reader([raw_lines[0]]))
    columns = {name.lower(): index for index, name in enumerate(header)}
    en_index, zh_index = columns["english"], columns["schinese"]
    changed = 0
    for index in range(1, len(raw_lines)):
        line = raw_lines[index]
        ending = "\r\n" if line.endswith("\r\n") else "\n" if line.endswith("\n") else ""
        row = next(csv.reader([line.removesuffix(ending)]))
        if len(row) <= max(en_index, zh_index) or row[en_index].strip() != row[zh_index].strip():
            continue
        english = row[en_index]
        chinese = PZ98_KEY_EXACT.get(row[0]) or PZ98_EXACT.get(english)

        battery = re.fullmatch(r"(\[FFD700\]\[AEC\]\[-\] \[777777\]\d+\.\[-\] )\[DECEA3\]Airdrop Request\[-\] \| Server Battery (.+ Ah)", english)
        if chinese is None and battery:
            chinese = f"{battery.group(1)}[DECEA3]空投申请[-] | {battery.group(2)}服务器电池"

        crypto = re.fullmatch(r"\[FFD700\]\[AEC\]\[-\] MINING\\n\\n(?:Currently mining|Passively generates) \[DECEA3\]Dukes Coins\[-\](?: over time)?\.\\n\\nUp to \[DECEA3\](\d+) coins every 30 minutes\[-\]\.", english)
        if chinese is None and crypto:
            chinese = f"[FFD700][AEC][-] 挖矿\\n\\n正在被动开采[DECEA3]公爵币[-]。\\n\\n每30分钟最多产出[DECEA3]{crypto.group(1)}枚公爵币[-]。"

        hydro = re.fullmatch(r"\[FFD700\]\[AEC\]\[-\] MINING\\n\\n(?:Currently generating|Passively generates) \[DECEA3\](.+) ([0-2]★) Grown Plants\[-\](?: over time)?\.\\n\\nProduces \[DECEA3\]1 to 3 .+ every 30 minutes\[-\]\.", english)
        if chinese is None and hydro:
            crop = CROPS.get(hydro.group(1), hydro.group(1))
            chinese = f"[FFD700][AEC][-] 水培\\n\\n正在被动培育[DECEA3]{crop} {hydro.group(2)}成熟植株[-]。\\n\\n每30分钟产出[DECEA3]1至3份{crop}[-]。"

        ore = re.fullmatch(r"\[FFD700\]\[AEC\]\[-\] Passive Ore Miner T([1-5])\\n\\nMINING\\n\\n(?:Currently extracting ores from the mining pool|Passively extracts every 30 minutes)\.", english)
        if chinese is None and ore:
            chinese = f"[FFD700][AEC][-] T{ore.group(1)}被动矿机\\n\\n挖矿\\n\\n每30分钟从矿物池中被动采掘一次。"

        research = re.fullmatch(r"\[FFD866\]Output\[-\]\[FFFFFF\]\\nOpen this bundle to receive \[-\]\[8EBE67\]«Research Material»\[-\]\[FFFFFF\]: \[-\]\[FFD866\]×(\d)\[-\]\[FFFFFF\]\.\\n\\n\[-\]\[AAAAAA\]Produced from tier (I|II|III|IV|V) Mutation Samples in the Incubator\.\[-\]", english)
        if chinese is None and research:
            chinese = f"[FFD866]产出[-][FFFFFF]\\n打开此包可获得[-][8EBE67]«研究材料»[-][FFFFFF]：[-][FFD866]×{research.group(1)}[-][FFFFFF]。\\n\\n[-][AAAAAA]由变异孵化器中的{research.group(2)}级变异样本制成。[-]"

        if chinese is None:
            continue
        row[zh_index] = chinese
        output = io.StringIO()
        csv.writer(output, lineterminator=ending, quoting=csv.QUOTE_MINIMAL).writerow(row)
        raw_lines[index] = output.getvalue()
        changed += 1
    with path.open("w", encoding="utf-8-sig", newline="") as handle:
        handle.write("".join(raw_lines))
    print(f"98-AECxProjectZ_Tweaks/Config/Localization.csv: updated {changed} generated entries")


translate_98_generated()


def normalized_localization_text(value: str) -> str:
    value = re.sub(r"\[[0-9A-Fa-f]{6}\]|\[-\]", "", value)
    value = value.replace("[AEC]", "").replace("AEC", "").replace("Project Z", "")
    return re.sub(r"[^a-z0-9]+", "", value.lower())


def ascii_word_score(value: str) -> int:
    allowed = {"AEC", "PZ", "Project", "Boss", "POI", "HUD", "GS", "NPC", "M60", "ANTIRAD", "RadProtect", "Discord"}
    cleaned = re.sub(r"\[[0-9A-Fa-f]{6}\]|\[-\]|\\[nrt]|\{[^{}]+\}", " ", value)
    return sum(word not in allowed for word in re.findall(r"[A-Za-z]{3,}", cleaned))


def translate_98_from_prior_localizations() -> None:
    source_paths = (
        "04-AEC-ENDGAME_OVERHAUL/Config/Localization.csv",
        "03-AEC-AIO_BOSS_EXTREME_EDITION/Config/Localization.csv",
        "01-ProjectZ/Config/Localization.csv",
        "07-AEC-Vehicles-NoMicrocraft/Config/Localization.csv",
    )
    candidates: dict[str, list[tuple[str, str]]] = {}
    for relative in source_paths:
        path = ROOT / relative
        with path.open(encoding="utf-8-sig", newline="") as handle:
            reader = csv.reader(handle); header = next(reader); columns = {name.lower(): i for i, name in enumerate(header)}
            for row in reader:
                if len(row) <= max(columns["english"], columns["schinese"]):
                    continue
                candidates.setdefault(row[0], []).append((row[columns["english"]], row[columns["schinese"]]))

    path = ROOT / "98-AECxProjectZ_Tweaks/Config/Localization.csv"
    lines = path.read_text(encoding="utf-8-sig").splitlines(keepends=True)
    header = next(csv.reader([lines[0]])); columns = {name.lower(): i for i, name in enumerate(header)}
    en_index, zh_index = columns["english"], columns["schinese"]
    changed = 0
    for index in range(1, len(lines)):
        line = lines[index]; ending = "\r\n" if line.endswith("\r\n") else "\n" if line.endswith("\n") else ""
        row = next(csv.reader([line.removesuffix(ending)]))
        if len(row) <= max(en_index, zh_index) or row[0] in PZ98_KEY_EXACT:
            continue
        normalized = normalized_localization_text(row[en_index])
        matches = [zh for en, zh in candidates.get(row[0], ()) if normalized_localization_text(en) == normalized]
        if not matches:
            continue
        best = min(matches, key=ascii_word_score)
        if ascii_word_score(best) >= ascii_word_score(row[zh_index]):
            continue
        # Preserve the final override's visible pack prefix while inheriting the reviewed wording.
        prefix = ""
        prefix_match = re.match(r"((?:\[[0-9A-Fa-f]{6}\])?\[(?:AEC|Project Z)[^\n]*?\[-\]\s*)", row[en_index])
        if prefix_match and not best.startswith(prefix_match.group(1)):
            prefix = prefix_match.group(1)
        row[zh_index] = prefix + best
        output = io.StringIO(); csv.writer(output, lineterminator=ending, quoting=csv.QUOTE_MINIMAL).writerow(row)
        lines[index] = output.getvalue(); changed += 1
    with path.open("w", encoding="utf-8-sig", newline="") as handle:
        handle.write("".join(lines))
    print(f"98-AECxProjectZ_Tweaks/Config/Localization.csv: inherited {changed} reviewed entries")


translate_98_from_prior_localizations()


PZ98_LATE_EXACT = {
    "aecUniversalTokenDesc": "[FF8A65]重要物品[-]\\n请勿丢弃此资源；整合包后续进度、配方和升级会用到它。\\n\\n[FFD700]来源[-]\\n完成AEC契约。\\n\\n[FF8A65]不会通过以下方式获得[-]\\n击杀普通敌人。\\n\\n[5ECFFF]用途[-]\\n用于AEC配方、升级和进度。",
    "modelQuestInternationalMarketDesc": "[FFFFFF]市场入门任务。有效击杀符合条件的僵尸和人形感染者会获得[-][D9D9D9]«感染者牙齿»[-][FFFFFF]。僵尸动物及其他由动物变异的感染者不会掉落牙齿，请按常规方式采集它们。[-]",
    "aecTokenGuideHeader": "[FFFFFF]完成AEC契约可获得[-][FFD866]«通用AEC代币»[-][FFFFFF]。有效击杀符合条件的僵尸和人形感染者会获得分级[-][8EBE67]«变异样本»[-][FFFFFF]、[-][D9D9D9]«感染者牙齿»[-][FFFFFF]以及1份[-][B36B4F]«僵尸尸体»[-][FFFFFF]。僵尸动物和其他由动物变异的感染者不会获得这些战利品，请按常规方式采集。[-]",
    "pzaecModpackProgressionBundle": "[FFD700][AEC[-][F87C63]\\PZ][-] 整合包进度指南",
    "PZAEC_Obj_Miniboss": "消灭Project Z小型Boss", "PZAEC_Obj_Boss": "消灭Boss",
    "PZAEC_Obj_Rancher": "消灭AEC牧场主感染体", "PZAEC_Obj_Devourer": "消灭吞噬者",
    "PZAEC_Obj_BearDaddy": "消灭熊王老爹", "PZAEC_Obj_Gargul": "消灭石像魔",
    "PZAEC_Obj_Bull": "消灭蛮牛", "PZAEC_Obj_Shocker": "消灭电击者",
    "PZAEC_Obj_Veteran": "消灭老兵", "PZAEC_Obj_Carrier": "消灭母体",
    "pzaecFG_AECEO09Desc": "[FFD700][AEC][-] [66B3FF]实地指南[-]\\n变异样本现在遵循稳定的危险度阶梯：T1来自普通至炼狱形态的原版僵尸；T2来自AEC T05–T07及Project Z辐射/充能精英；T3来自AEC T08–T10及Project Z炼狱精英；T4对应AEC T11–T13；T5仅来自T14–T15与最危险的具名Boss。Boss和仆从仍遵循这一进度，更坚韧的目标只会增加同等级样本数量，不会因生命值高就跨级掉落。Project Z具名Boss没有AEC等级标签，因此按实际生命值和物理抗性决定奖励。样本与感染者牙齿都是进度资源，请勿丢弃。\\n\\n",
    "pzaecFG_AECBoss07Desc": "[FFD700][AEC][-] [66B3FF]实地指南[-]\\n[FFD866]任务中继站[-]是在家中接取AEC契约的入口。1★至5★升级会逐步开放更困难的任务池，但不会恢复旧AEC终局剧情。先使用基础中继站完成早期契约，再沿星级依次升级；每一级都会提高可用猎杀任务的上限。中继站属于可重复终局系统，并非一次性剧情摆设。\\n\\n",
    "pzaecFG_PZ13Desc": "[F87C63][Project Z][-] [66B3FF]实地指南[-]\\n不同感染生物使用不同奖励路线。符合条件的人形目标死亡时会直接给予[FFD866]变异样本[-]、[FFD866]感染者牙齿[-]和一份[FFD866]僵尸尸体[-]。僵尸尸体是可稍后开启的生物资源包，无需当场采集每具人形尸体。僵尸动物及其他动物型感染者通常仍使用常规采集系统；狼或熊不掉落人形奖励属于正常现象。\\n\\n",
    "pzaecFG_AECEO11Desc": "[FFD700][AEC][-] [66B3FF]实地指南[-]\\n[FFD866]僵尸市场[-]是本整合包的AEC中央物流工作台，主要申请货币为[FFD866]感染者牙齿[-]。你通常是在订购物资配送或基础设施，而非直接购买最终成品。服务器电池、加密货币挖矿、连接卡、AEC硬币、研究论文、被动生产和部分后期补给链都从这里开始，许多项目还有不同规模或等级。标为“空投申请”的物品并不一定是最终电池、工作台或资源箱；取得申请单后，还要完成对应配送流程。\\n\\n",
    "internationalMarketDesc": "[FFD700][AEC][-] [66CFFF]僵尸市场[-]\\n使用[FFD866]感染者牙齿[-]兑换申请单、服务器设施和高级补给的物流工作台。申请项目可通向加密货币挖矿、电池、连接卡、AEC硬币、研究论文和被动生产。“空投申请”并不总是最终成品，部分货物还需另行完成配送步骤。\\n\\n",
}

_market_names = {1: "牙齿", 2: "取得终端", 3: "加密货币矿机", 4: "服务器电池", 5: "连接卡", 6: "AEC硬币", 7: "研究论文", 8: "被动生产"}
for _step, _title in _market_names.items():
    _value = f"[FFD700][AEC\\PZ][-] [66CFFF]僵尸市场[-] [DECEA3]{_step}/8[-]——[FFFFFF]{_title}[-]"
    PZ98_LATE_EXACT[f"PZAEC_Guide_Market_{_step:02d}_Name"] = _value
    PZ98_LATE_EXACT[f"pzaecGuideMarket{_step:02d}"] = _value
PZ98_LATE_EXACT.update({
    "meleeWpnBladeT1HuntingKnifeRareButcherDesc": "[FFFFFF]非常适合砍杀僵尸，也能剖取动物肉类。\\n普通攻击造成[-][DECEA3]1[-][FFFFFF]层流血，蓄力攻击至少造成[-][DECEA3]2[-][FFFFFF]层流血。\\n\\n具有稀有属性[-][FAFF63]«屠夫»[-][FFFFFF]\\n采集普通动物与僵尸动物尸体时，获得的资源提高[-][DECEA3]20%[-][FFFFFF]。\\n\\n使用修理包修理。\\n可拆解为铁。[-]",
    "meleeWpnKnucklesT3SteelKnucklesRareButcherDesc": "[FFFFFF]既能保护双手，也能让每一拳更有分量；拳刃还很适合剖取动物。\\n\\n具有稀有属性[-][FAFF63]«屠夫»[-][FFFFFF]\\n采集普通动物与僵尸动物尸体时，获得的资源提高[-][DECEA3]25%[-][FFFFFF]。\\n\\n使用修理包修理。\\n拆解可获得钢制拳套零件。[-]",
    "PZAEC_Guide_Mutator_02_Name": "[FFD700][AEC\\PZ][-] [C78CFF]通往强大力量之路[-] [DECEA3]2/4[-]——[FFFFFF]空白变异器[-]",
    "pzaecGuideMutator02": "[FFD700][AEC\\PZ][-] [C78CFF]通往强大力量之路[-] [DECEA3]2/4[-]——[FFFFFF]空白变异器[-]",
    "PZAEC_Guide_Necro_01_Name": "[FFD700][AEC\\PZ][-] [FF9F5E]亡灵锻炉[-] [DECEA3]1/2[-]——[FFFFFF]工作台[-]",
    "pzaecGuideNecro01": "[FFD700][AEC\\PZ][-] [FF9F5E]亡灵锻炉[-] [DECEA3]1/2[-]——[FFFFFF]工作台[-]",
    "PZAEC_Guide_Necro_02_Name": "[FFD700][AEC\\PZ][-] [FF9F5E]亡灵锻炉[-] [DECEA3]2/2[-]——[FFFFFF]弹药转化[-]",
    "pzaecGuideNecro02": "[FFD700][AEC\\PZ][-] [FF9F5E]亡灵锻炉[-] [DECEA3]2/2[-]——[FFFFFF]弹药转化[-]",
    "PZAEC_Guide_Incubator_Name": "[FFD700][AEC\\PZ][-] [C78CFF]研究档案[-]——[FFFFFF]变异孵化器[-]",
    "pzaecGuideIncubator": "[FFD700][AEC\\PZ][-] [C78CFF]研究档案[-]——[FFFFFF]变异孵化器[-]",
    "PZAEC_Guide_Relay_01_Name": "[FFD700][AEC\\PZ][-] [A7D96C]神秘猎人[-] [DECEA3]1/6[-]——[FFFFFF]任务中继站[-]",
    "pzaecGuideRelay01": "[FFD700][AEC\\PZ][-] [A7D96C]神秘猎人[-] [DECEA3]1/6[-]——[FFFFFF]任务中继站[-]",
    "PZAEC_Guide_ResearchMaterial_Name": "[FFD700][AEC\\PZ][-] [C78CFF]研究档案[-]——[FFFFFF]研究材料[-]",
    "pzaecGuideResearchMaterial": "[FFD700][AEC\\PZ][-] [C78CFF]研究档案[-]——[FFFFFF]研究材料[-]",
    "PZAEC_Guide_Radio_03_Name": "[FFD700][AEC\\PZ][-] [FFD966]连接外界[-] [DECEA3]3/4[-]——[FFFFFF]稀有契约[-]",
    "pzaecGuideRadio03": "[FFD700][AEC\\PZ][-] [FFD966]连接外界[-] [DECEA3]3/4[-]——[FFFFFF]稀有契约[-]",
    "PZAEC_Guide_Archive_Name": "[FFD700][AEC\\PZ][-] [66B3FF]末日先驱[-]——[FFFFFF]实地档案[-]",
    "pzaecGuideArchiveQuest": "[FFD700][AEC\\PZ][-] [66B3FF]末日先驱[-]——[FFFFFF]实地档案[-]",
})
add_guide(("PZAEC_Guide_Market_01_Desc", "pzaecGuideMarket01Desc"), "[66CFFF]僵尸市场1/8——牙齿[-]", "感染者牙齿是僵尸市场的核心资源之一，并非出售给商人的垃圾；市场会在申请物资和基础设施时消耗它们。", "收集并保留[FFD866]95颗感染者牙齿[-]。")
PZ98_LATE_EXACT["PZAEC_Guide_Market_01_Desc"] = PZ98_KEY_EXACT["PZAEC_Guide_Market_01_Desc"]
PZ98_LATE_EXACT["pzaecGuideMarket01Desc"] = PZ98_KEY_EXACT["pzaecGuideMarket01Desc"]
PZ98_LATE_EXACT["pzaecGuideMarket06Desc"] = FIXES["98-AECxProjectZ_Tweaks/Config/Localization.csv"]["PZAEC_Guide_Market_06_Desc"]


PZ_BOSS_SKILL_NAMES = {
    "Bear Daddy": "熊王老爹", "Bitch": "悍妇", "Bull": "蛮牛", "Burning Flesh": "燃烧之肉",
    "Carrier": "母体", "Cholera": "霍乱体", "Devourer": "吞噬者", "Gargul": "石像魔", "Shocker": "电击者", "Veteran": "老兵",
}

PZ98_LATE_EXACT.update({
    "perkClumsyDesc": "[FFFFFF]不使用带有“工具”标签的物品采集时，每次命中都会使当前计数器减少[-][DECEA3]1[-][FFFFFF]。可对能够采集的动物及僵尸动物尸体生效。[-]",
    "perkJackOfAllTradesDesc": "[FFFFFF]不使用带有“工具”标签的物品采集时，每次命中都会使当前计数器减少[-][DECEA3]1[-][FFFFFF]。可对能够采集的动物及僵尸动物尸体生效。\\n\\n[-][FFD866]受阻进度：{cvar(.blockJackOfAllTrades:0)}[-][FFFFFF]——“笨手笨脚”生效时无法提升。[-]",
    "perkEliteHuntDesc": "[FFFFFF]猎杀精英僵尸可提高你对它们造成的伤害。达到25级后，还会免疫精英僵尸造成的主动辐射。\\n\\n[-][DECEA3]当前击杀数：{cvar(CountEliteHunt:0)}[-]",
    "perkPhysicianRank3LongDesc": "[FFFFFF]经过治疗的重伤恢复速度提高50%。\\n医疗物品的持续生命恢复量提高100%。\\n使用绷带、急救绷带、急救包和夹板时获得的经验提高500%。\\n使用石膏可立即治愈骨折。[-]",
})

for _rank, _percent in enumerate((5, 15, 25), 1):
    PZ98_LATE_EXACT[f"perkClumsyRank{_rank}LongDesc"] = (
        f"[FFFFFF]物品制作耗时增加[-][DECEA3]{_percent}%[-][FFFFFF]。\\n\\n"
        f"[-][FFD866]剩余：{{cvar(.CounterClumsy{_rank}:0)}}[-]"
    )

for _rank, _percent in enumerate((5, 10, 15), 1):
    PZ98_LATE_EXACT[f"perkJackOfAllTradesRank{_rank}LongDesc"] = (
        f"[FFFFFF]物品制作耗时降低[-][DECEA3]{_percent}%[-][FFFFFF]。\\n\\n"
        f"[-][FFD866]剩余：{{cvar(.CounterJackOfAllTrades{_rank - 1}:0)}}[-]"
    )

_meat_feast_flavor = (
    "沿着边缘下刀，就能切出上好的烤肉。", "处理小型动物正是你的拿手活。",
    "处理动物和处理鱼差不多，只是血更多。", "你很清楚怎样从尸体上取下最多的肉——不一定非得是动物。",
    "屠宰场少不了你这样的专家。", "真正的珍馐美味鉴赏家。", "每一块都不能浪费。",
    "总想看看里面究竟有什么？", "你更喜欢肥一点的尸体。", "顶尖屠夫绝不会放过任何可用之物，尸体自然也不例外。",
)
for _rank, _flavor in enumerate(_meat_feast_flavor, 1):
    PZ98_LATE_EXACT[f"perkMeatFeastRank{_rank}LongDesc"] = (
        f"[FFFFFF]{_flavor}屠宰动物尸体获得的资源提高[-][DECEA3]{_rank * 10}%[-][FFFFFF]。[-]"
    )

for _tier in range(1, 6):
    PZ98_LATE_EXACT[f"PassiveOreMinerT{_tier}02"] = (
        f"[FFD700][AEC][-] T{_tier}被动矿机\\n\\n已就绪\\n\\n领取已采掘的矿物。"
    )

PZ98_LATE_EXACT.update({
    "buffDestroyedStoneBaseIncDesc": "脚下满是垃圾，走路时最好看清落脚处。\\n\\n移动速度降低[DECEA3]25%[-]，跳跃高度降低[DECEA3]15%[-]，奔跑时的耐力消耗提高[DECEA3]50%[-]。\\n\\n警告：在危险地面上奔跑时，有[DECEA3]25%[-]几率刺伤脚部。\\n\\n[DECEA3]装备《鞋底防护》改装件可改善机动性。[-]",
    "perkAecSpringHeelEffectMaxDesc": "[FFFFFF]跳跃高度：约为原来的3倍（实际效果上限）[-]",
    "modpackTabMarket_CryptoResearchPapers": "[5ECFFF]市场[-] | 研究论文",
    "modpackTabMarket_CryptoModding": "[5ECFFF]市场[-] | 改装物品",
    "modpackTabMarket_GenerationCoin": "[5ECFFF]市场[-] | 硬币生产",
    "modpackTabMutator_AECworkbenchBlockDamage": "[FF6B6B]变异改装[-] | 方块伤害",
    "modpackTabNecro_aecNecroforgeAmmo12g": "[C792EA]亡灵锻炉[-] | 12号霰弹",
    "modpackTabVehicleModifier_AECvehicleModifierEntityDamage": "[6EC6FF]载具[-] | 实体伤害",
    "modpackTabVehicleModifier_AECvehicleModifierBlockDamage": "[6EC6FF]载具[-] | 方块伤害",
    "modpackTabVehicleFinal_AECvehicleFinalRocket": "[6EC6FF]载具[-] | 喷气式载具",
    "modpackTabVehicleFinal_AECvehicleFinalOther": "[6EC6FF]载具[-] | 其他",
    "AECBrokenBicycleVehicle": "[FFD700][AEC][-] 破损自行车",
    "AECClassicSedanHuntDamagedVehicle": "[FFD700][AEC][-] 经典猎行轿车（损坏版）",
    "AECClassicSedanHuntRepairedVehicle": "[FFD700][AEC][-] 经典猎行轿车",
    "AECClassicSedanSpeedDamagedVehicle": "[FFD700][AEC][-] 经典疾速轿车（损坏版）",
    "AECClassicSedanSpeedRepairedVehicle": "[FFD700][AEC][-] 经典疾速轿车",
    "AECClassicSemiFrontRepairedVehicle": "[FFD700][AEC][-] 经典半挂车车头",
    "AECClassicServiceTruckGenericNoLadderVehicle": "[FFD700][AEC][-] 经典工程卡车（无梯版）",
    "AECClassicServiceTruckGenericVehicle": "[FFD700][AEC][-] 经典工程卡车（带梯版）",
    "AECClassicServiceTruckMoPowerVehicle": "[FFD700][AEC][-] 经典工程卡车（莫尔电力）",
    "AECClassicServiceTruckWorkingStiffVehicle": "[FFD700][AEC][-] 经典工程卡车（结实工具）",
    "AECClassicSUVDamagedVehicle": "[FFD700][AEC][-] 经典SUV（损坏版）",
    "AECClassicSUVVehicle": "[FFD700][AEC][-] 经典SUV",
    "AECClassicTractorRepairedVehicle": "[FFD700][AEC][-] 经典拖拉机",
    "AECFlyingCityBusVehicle": "[FFD700][AEC][-] 飞行城市公交车",
    "AECIronBastionVehicle": "[FFD700][AEC][-] 钢铁堡垒",
    "AECMiniAirplaneVehicle": "[FFD700][AEC][-] 迷你飞机",
    "AECMiniRocketVehicle": "[FFD700][AEC][-] 迷你火箭车",
    "AECTheAdvancedPipeBicycleVehicle": "[FFD700][AEC][-] 高级管制自行车",
    "AECTheExecutionerVehicle": "[FFD700][AEC][-] 行刑者",
    "AECThePipeBicycleVehicle": "[FFD700][AEC][-] 管制自行车",
    "AECTheRockVehicle": "[FFD700][AEC][-] 巨岩号",
    "AECTheSoldierVehicle": "[FFD700][AEC][-] 士兵号",
    "AECTiltTruckEmptyVehicle": "[FFD700][AEC][-] 倾卸卡车（空载版）",
    "startQuest2Offer": "[F87C63][Project Z][-] [FF6666]第一章：自我救治[-]\\n你醒来时发现身旁有一张纸条，却想不起是谁在何时留下的。这是留言的第一部分：\\n\\n«如果你醒了而且能读到这些字，说明你还没那么容易死。我昨天日落前发现了你，却没有办法帮忙。看着刺穿你腿的东西，我不确定你能不能撑到早上。不过，如果你真能醒来，我在旁边留下了几卷绷带——如果你打算把那东西拔出来，它们会派上用场……»[DECEA3]——怀尔德[-]",
    "startQuest3Offer": "[F87C63][Project Z][-] [FF6666]第二章：罪魁祸首[-]\\n这是留言的第二部分：\\n\\n«检查你的时候，我听见附近有动静。我不确定那只生物是否愿意放弃猎物，不过它显然已无力继续攻击。无论如何，你还有一点恢复时间。去找它之前，先把自己的伤处理好……»[DECEA3]——怀尔德[-]",
    "startQuest4Offer": "[F87C63][Project Z][-] [FF6666]第三章：不错的报酬[-]\\n这是留言的最后一部分：\\n\\n«如果你成功脱身，试着联系任何一名商人。他们都认识我，也能先给你一些物资。告诉他们是我让你来的。»[DECEA3]——怀尔德[-]",
    "BetterWeaponMeleeLightT1Offer": "[F87C63][Project Z][-] «轻型近战武器不擅长阻挡成群的疯狂僵尸，但对付单个目标极为出色，而且耐力消耗较低。它是不错的选择，却并不适合所有人。使用任意轻型近战武器（指虎、刀或警棍）击杀[FF6666]50只僵尸[-]，即可领取一件轻型[DECEA3]铁制[-]近战武器。»[DECEA3]——怀尔德[-]",
    "BetterWeaponMeleeHeavyT1Offer": "[F87C63][Project Z][-] «重型近战武器不仅看起来威慑力十足，也能有效阻挡成群的嗜血感染者，但会消耗大量耐力。使用任意重型近战武器（长矛、棍棒或大锤）击杀[FF6666]50只僵尸[-]，即可领取一件重型[DECEA3]铁制[-]近战武器。»[DECEA3]——怀尔德[-]",
    "BetterWeaponMeleeLightT2Offer": "[F87C63][Project Z][-] «轻型近战武器擅长迅速解决单个目标，而且耐力消耗较低。使用任意轻型近战武器（指虎、刀或警棍）击杀[FF6666]100只僵尸[-]，即可领取一件轻型[DECEA3]钢制[-]近战武器。»[DECEA3]——怀尔德[-]",
    "BetterWeaponMeleeHeavyT2Offer": "[F87C63][Project Z][-] «重型近战武器能阻挡成群的感染者，但需要充足耐力。使用任意重型近战武器（长矛、棍棒或大锤）击杀[FF6666]100只僵尸[-]，即可领取一件重型[DECEA3]钢制[-]近战武器。»[DECEA3]——怀尔德[-]",
    "BetterWeaponMeleeImpModsOffer": "[F87C63][Project Z][-] «选择近战武器改装？很合理。使用任意近战武器击杀[FF6666]500只僵尸[-]，即可获得一件[DECEA3]改良型[-]近战武器改装件。»[DECEA3]——怀尔德[-]",
    "BetterWeaponRangeImpModsOffer": "[F87C63][Project Z][-] «选择远程武器改装？很合理。使用任意远程武器击杀[FF6666]500只僵尸[-]，即可获得一件[DECEA3]改良型[-]远程武器改装件。»[DECEA3]——怀尔德[-]",
    "BetterWeaponMeleeUniqueModsOffer": "[F87C63][Project Z][-] «使用任意近战武器击杀[FF6666]1000只僵尸[-]，即可获得一件[FFB800]独特[-]近战武器改装件。»[DECEA3]——怀尔德[-]",
    "BetterWeaponRangeUniqueModsOffer": "[F87C63][Project Z][-] «使用任意远程武器击杀[FF6666]1000只僵尸[-]，即可获得一件[FFB800]独特[-]远程武器改装件。»[DECEA3]——怀尔德[-]",
    "BetterWeaponLastHunt2Offer": "[F87C63][Project Z][-] [DECEA3]最后的猎杀：终章[-]\\n\\n«有时，最好别去想夜间荒原里潜伏着什么。但如果你学会了怎样杀死它们，我会非常高兴。击杀[FF6666]25只Boss[-]，你将获得一些传奇部件，我还会提升你的部分技能。»[DECEA3]——怀尔德[-]",
    "BetterWeapon_ImpModsMeleeDesc": "[F87C63][Project Z][-] [FF6666]可靠的近战改装[-]\\n完成要求后，你会获得一件[DECEA3]改良型[-]近战武器改装件。",
    "BetterWeapon_ImpModsRangeDesc": "[F87C63][Project Z][-] [FF6666]可靠的远程改装[-]\\n完成要求后，你会获得一件[DECEA3]改良型[-]远程武器改装件。",
    "BetterWeapon_UniqueModsMeleeDesc": "[F87C63][Project Z][-] [FF6666]独特近战改装[-]\\n完成要求后，你会获得一件[FFB800]独特[-]近战武器改装件。",
    "BetterWeapon_UniqueModsRangeDesc": "[F87C63][Project Z][-] [FF6666]独特远程改装[-]\\n完成要求后，你会获得一件[FFB800]独特[-]远程武器改装件。",
    "modArmorTreasureHunter": "[F87C63][Project Z][-] 内部对象：寻宝者护甲改装",
    "modFatigueTestingBoots": "[F87C63][Project Z][-] 内部对象：疲劳测试靴改装",
    "modMaxHP": "[F87C63][Project Z][-] 内部对象：最大生命值改装",
    "modMeleeDimondPartsLight": "[F87C63][Project Z][-] [DECEA3]改良型[-]轻型钻石加固模组",
})


def translate_98_progression_english(key: str, english: str):
    if key in {"perkCorpseSurgeonDesc", "perkCorpseDentistDesc", "perkCorpseAdventurerDesc"}:
        reward = "[8EBE67]«研究材料»[-]" if "Research Material" in english else "[D9D9D9]«感染者牙齿»[-]" if "Infected Teeth" in english else "1枚[FFD866]«通用代币»[-]"
        return f"[FFD866]10级[-][FFFFFF] • 花费：[-][FFFFFF]1–10技能点[-][FFFFFF]\\n开启[-][B36B4F]«僵尸尸体»[-][FFFFFF]时，有概率找到{reward}[FFFFFF]。[-]"
    match = re.fullmatch(r"perkCorpseDentistRank(\d+)LongDesc", key)
    if match:
        chance = re.search(r"\[D8C6A3\](\d+%)", english); amount = re.search(r"\[FFFFFF\](\d+(?:–\d+)?) \[-\]\[D9D9D9\]", english)
        if chance and amount:
            return f"[FFD866]概率[-][FFFFFF] [-][D8C6A3]{chance.group(1)}[-][FFFFFF]\\n[-][FFD866]获得[-][FFFFFF] {amount.group(1)}颗[D9D9D9]«感染者牙齿»[-][FFFFFF]\\n[-][AAAAAA]开启[B36B4F]«僵尸尸体»[-][AAAAAA]时生效。[-]"
    match = re.fullmatch(r"perkEliteHuntRank(\d+)LongDesc", key)
    if match:
        percent = re.search(r"increased by \[-\]\[DECEA3\](\d+%)", english)
        if percent:
            result = f"[DECEA3]加成[-][FFFFFF]\\n对精英僵尸造成的伤害提高[-][DECEA3]{percent.group(1)}[-][FFFFFF]。[-]"
            if "BONUS ABILITY" in english:
                result += "\\n\\n[FFD866]额外能力[-][FFFFFF]\\n免疫精英僵尸造成的主动辐射。[-]"
            return result
    family = next((name for name in PZ_BOSS_SKILL_NAMES if f"perk{name.replace(' ', '')}Rank" in key), None)
    if family:
        values = re.findall(r"\[DECEA3\](\d+%)", english); target = PZ_BOSS_SKILL_NAMES[family]
        if len(values) >= 2:
            clauses = [f"对{target}造成的伤害提高[DECEA3]{values[0]}[-]"]
            if "minions" in english:
                clauses.append(f"对{target}仆从造成的伤害提高[DECEA3]{values[1]}[-]"); reduction = values[2] if len(values) > 2 else None
            else:
                reduction = values[1]
            if reduction:
                clauses.append(f"受到{target}的伤害降低[DECEA3]{reduction}[-]")
            result = "[DECEA3]加成：[-]\\n" + "，".join(clauses) + "。"
            if "BONUS ABILITY" in english:
                immunity = "护甲削弱与主动辐射" if family == "Bitch" else "主动辐射"
                result += f"\\n\\n[DECEA3]额外能力[-] [FFB800]无惧{target}[-]\\n你不再害怕{target}，并免疫该小型Boss造成的{immunity}。"
            return result
    return None


def translate_98_late_cleanup() -> None:
    path = ROOT / "98-AECxProjectZ_Tweaks/Config/Localization.csv"; lines = path.read_text(encoding="utf-8-sig").splitlines(keepends=True)
    header = next(csv.reader([lines[0]])); columns = {name.lower(): i for i, name in enumerate(header)}; e, z = columns["english"], columns["schinese"]
    changed = 0
    for index in range(1, len(lines)):
        line = lines[index]; ending = "\r\n" if line.endswith("\r\n") else "\n" if line.endswith("\n") else ""; row = next(csv.reader([line.removesuffix(ending)]))
        if len(row) <= max(e, z): continue
        chinese = PZ98_LATE_EXACT.get(row[0]) or translate_98_progression_english(row[0], row[e])
        if chinese is None and row[1] == "item_modifiers" and "LVL." in row[z]:
            chinese = row[z].replace("LVL.", "等级")
        if chinese is None and row[1] == "item_modifiers":
            replacements = (
                ("[FFB800]unique[-]", "[FFB800]独特[-]"), ("[FFB800]Unique[-]", "[FFB800]独特[-]"),
                ("[DECEA3]Improved[-]", "[DECEA3]改良型[-]"), ("[FF6666]«Boost»[-]", "[FF6666]«强化»[-]"),
                (" Fortitude 模组", " 坚韧模组"), (" Cripple 'Em 模组", " 致残模组"), (" JetPack 模组", " 喷气背包模组"),
                (" Reactor工具", " 反应堆工具"), (" Fireproof Protection 模组", " 防火保护模组"),
                (" Poisoned Blades 模组", " 毒刃模组"), (" 模组 Light", "轻型模组"),
            )
            updated = row[z]
            for source, target in replacements: updated = updated.replace(source, target)
            if updated != row[z]: chinese = updated
        if chinese is None and row[1] == "blocks":
            planted = re.fullmatch(r"planted(Aloe|Blueberry|Chrysanthemum|Coffee|Corn|Cotton|Goldenrod|Hop|Mushroom|Potato|Pumpkin|Yucca)3HarvestPlayerSel([12])", row[0])
            if planted:
                crop = {"Aloe":"芦荟", "Blueberry":"蓝莓", "Chrysanthemum":"菊花", "Coffee":"咖啡", "Corn":"玉米", "Cotton":"棉花", "Goldenrod":"黄花", "Hop":"啤酒花", "Mushroom":"蘑菇", "Potato":"土豆", "Pumpkin":"南瓜", "Yucca":"丝兰"}[planted.group(1)]
                chinese = f"[F87C63][Project Z][-] 内部对象：已种植的{crop}，玩家采集选择项{planted.group(2)}"
            generic_miner = re.fullmatch(r"PassiveZombiePartMiner(Generic|Rockbreaker)([0-5]0[02])", row[0])
            if generic_miner:
                family = "通用" if generic_miner.group(1) == "Generic" else "碎岩者"
                chinese = f"[FFD700][AEC][-] 内部对象：{family}僵尸部件被动采集器{generic_miner.group(2)}"
        if chinese is None and row[1] == "blocks":
            block_exact = {
                "aec_heatmap_cooler":"[FFD700][AEC][-] 内部对象：热度图冷却器", "aecPortableAmmoWorkshop":"[FFD700][AEC][-] 便携式弹药工作台",
                "aecQuestRelay":"[FFD700][AEC][-] 契约中继站", "alloysShapes":"[F87C63][Project Z][-] 内部对象：合金形状",
                "AutoMinerClay":"[F87C63][Project Z][-] [CFEDFF]黏土[-]自动矿机", "AutoMinerCoal":"[F87C63][Project Z][-] [CFEDFF]煤炭[-]自动矿机",
                "AutoMinerIron":"[F87C63][Project Z][-] [CFEDFF]铁矿[-]自动矿机", "AutoMinerLead":"[F87C63][Project Z][-] [CFEDFF]铅矿[-]自动矿机",
                "flagThick44BlockVariantHelper":"[FFD700][AEC[-][F87C63]\\PZ][-] 内部对象：厚旗帜方块变体辅助项", "forgeImp":"[F87C63][Project Z][-] [DECEA3]改良型[-]熔炉",
                "ImpactDriverReactChargingStation":"[F87C63][Project Z][-] [FFB800]反应堆冲击起子[-]充电站", "internationalMarket":"[FFD700][AEC][-] 僵尸市场",
                "PassiveBoozeBarrel000":"[FFD700][AEC][-] 内部对象：被动酿酒桶000", "PassiveBoozeBarrel002":"[FFD700][AEC][-] 内部对象：被动酿酒桶002",
                "PassiveCryptoMinerModel01":"[FFD700][AEC][-] 被动加密货币矿机模型01", "PassiveCryptoMinerModel02":"[FFD700][AEC][-] 被动加密货币矿机模型02",
                "PassiveOreMinerModel":"[FFD700][AEC][-] 内部对象：被动矿机模型", "PillCaseStorage":"[F87C63][Project Z][-] [DECEA3]额外容量[-]医疗柜",
                "plantedYucca1Sel1":"[F87C63][Project Z][-] [9DEF5D]选择项[-]丝兰（种子）", "PortableReactorChargingStation":"[F87C63][Project Z][-] [FFB800]便携式反应堆[-]充电站",
                "ReactorBank":"[F87C63][Project Z][-] [FFB800]反应堆[-]移动电源", "steelShapes":"[F87C63][Project Z][-] 内部对象：钢制形状",
            }
            relay = re.fullmatch(r"aecQuestRelay([1-5])star", row[0])
            if relay: chinese = f"[FFD700][AEC][-] 契约中继站 [{relay.group(1)}★]"
            else: chinese = block_exact.get(row[0])
            if chinese is None and row[0].startswith("zdeco"):
                chinese = row[z].replace("[FFCEF0]Deco:[-]", "[FFCEF0]装饰：[-]")
        if chinese is None and row[1] == "items" and "\\n" not in row[z]:
            phrase_replacements = (
                ("Hunting for", "猎杀"), ("100 Waves super-challenge", "100波超级挑战"), ("100 Waves challenge", "100波挑战"),
                ("Combined Arms", "联合作战"), ("Deadeye Gauntlet", "神射手试炼"), ("Phantom Mosaic", "幻影群像"),
                ("Primal Stampede", "原始兽群"), ("Gory Legion", "血腥军团"), ("Blood, Brood and Gallows", "鲜血、巢群与绞架"),
                ("Brood and Gallows", "巢群与绞架"), ("Apex Collapse", "巅峰崩塌"), ("The Last Assault", "最后突袭"),
                ("Frontline Assault", "前线突袭"), ("Mixed Assault", "混合突袭"), ("Minion Assault", "仆从突袭"),
                ("Scout Patrol", "侦察巡逻"), ("Mixed Patrol", "混合巡逻"), ("Hollow Patrol", "空壳巡逻"),
                ("POI Assault", "兴趣点突袭"), ("POI Patrol", "兴趣点巡逻"), ("POI Horde", "兴趣点尸潮"),
                ("Boss Defense", "Boss防守"), ("Minion Defense", "仆从防守"), ("Grenadier Defense", "掷弹兵防守"),
                ("Knife Defense", "刀手防守"), ("Magnum Defense", "马格南枪手防守"), ("Pistol Defense", "手枪兵防守"),
                ("Rocket Defense", "火箭兵防守"), ("Shotgun Defense", "霰弹枪兵防守"), ("Sniper Defense", "狙击手防守"),
                ("Mutation Sample Cache", "变异样本储备箱"), ("Supply Cache", "补给储备箱"), ("War Cache", "战争储备箱"),
                ("Boss Cache", "Boss储备箱"), ("Final Cache", "最终储备箱"), ("Supply Crate", "补给箱"),
                ("Legendary Contract", "传奇契约"), ("Joint Contract", "联合契约"), (" Contract", "契约"),
                ("Hazard ", "危险度"), (" Waves ", "波次"), ("super-挑战", "超级挑战"),
                (" Assault", "突袭"), (" Patrol", "巡逻"), (" Defense", "防守"), (" Hunt", "猎杀"),
                (" Siege", "围攻"), (" Horde", "尸潮"), (" Cache", "储备箱"), (" Bundle", "礼包"),
                ("Quest Item Category Crypto", "加密货币任务物品类别："), ("[DECEA3]Quest[-]", "[DECEA3]任务[-]"),
                ("[FF6666]Quest[-]", "[FF6666]任务[-]"), ("[FFB800]Unique[-]", "[FFB800]独特[-]"),
                ("[FFB800]unique[-]", "[FFB800]独特[-]"), ("[F0B18A]Unique[-]", "[F0B18A]独特[-]"),
                ("[F0B18A]unique[-]", "[F0B18A]独特[-]"), ("[FAFF63]Rare[-]", "[FAFF63]稀有[-]"),
                ("[DECEA3]Improved[-]", "[DECEA3]改良型[-]"), ("[DECEA3]Elixir:[-]", "[DECEA3]灵药：[-]"),
                ("Field 指南", "实地指南"), ("Companions", "同伴"), ("Garage", "车库"),
                ("Server 电池", "服务器电池"), ("Resource 电池 Model", "资源电池模型"), ("研究论文 Model", "研究论文模型"),
                ("Passive ", "被动"), (" Model", "模型"), (" Tier", "等级"), ("Zombie ", "僵尸"),
                (" Rad_", " 辐射_"), ("Rad_", "辐射_"), (" Infernal", " 炼狱"), (" Elite", " 精英"),
                (" Cop", " 警察"), (" Feral", " 凶暴"), (" Mutated", " 变异体"), (" Worker", " 工人"),
                (" Burning", " 烧焦感染者"), (" Demolition", " 爆破感染者"), (" Haz Mat", " 危险品感染者"),
                (" Strong", " 强壮感染者"), (" Short", " 矮小感染者"), (" Projectile", "投射物"),
                ("Shale Boulder", "页岩巨石"), (" Boulder", "巨石"), (" Fire Vomit", "火焰呕吐物"),
                (" Corpse Vomit", "尸体呕吐物"), (" Vomit", "呕吐物"), (" Grenade", "手榴弹"),
                (" Air Bomb", "航空炸弹"), (" Bike", "摩托车"), (" Burst", "连射"), (" Flying Barrel", "飞行木桶"),
                (" Skull", "头骨"), (" Purse", "手提包"), (" Bandit", "强盗"), (" Mini", "迷你"),
                (" Crossbow", "弩"), (" Ranged", "远程"), (" Throw", "投掷"), (" Fire", "火焰"),
                (" Grenadier", "掷弹兵"), (" army", "大军"), (" Army", "大军"), (" Menagerie", "怪物园"),
                (" Swarm", "虫群"), (" Stampede", "兽群冲锋"), (" Summoner", "召唤师"), (" Giant", "巨人"),
                (" Tuned", "调校型"), (" Big", "大型"), (" Small", "小型"), (" Bow", "弓"),
                (" Chicken", "鸡"), (" Boar", "野猪"), (" Elixir", "灵药"), (" Category", "类别"),
                (" Crypto", "加密货币"), (" Item", "物品"), (" Test", "测试"), (" Base", "基础型"),
                (" Wild", "怀尔德"), (" Support", "支援"), (" Unit 电池", "电池单元"),
                ("Last Stand", "背水一战"), ("Warband", "战团"), (" strong infectionpoint", "据点"),
                ("Overwatch", "监视火力"), ("Fire Team", "火力小组"), ("Demo Squad", "爆破小队"),
                ("Wild Menagerie", "荒野怪物园"), ("Gauntlet", "试炼"), ("Explosive Twins", "爆裂双子"),
                ("Demonic Trio", "恶魔三人组"), ("Elder Cataclysm", "远古浩劫"), ("The Fivefold Cataclysm", "五重浩劫"),
                ("Broken Vigil", "破碎守夜"), ("Carnival of the Dead", "亡者嘉年华"), ("Dead Shift", "亡者轮班"),
                ("Echo Volley", "回声齐射"), ("Graveyard Watch", "墓园守望"), ("Haunted Grounds", "闹鬼之地"),
                ("Hollow Parade", "空壳游行"), ("Lantern Breach", "提灯突破"), ("Spectral Lantern March", "幽灵提灯行军"),
                ("The Lost Procession", "迷失队列"), ("Restless Rush", "躁动冲锋"), ("Shrieking Stones", "尖啸之石"),
                ("The Witch", "女巫"), ("Witch", "女巫"), ("The Mechanician", "机械师"), (" The ", " "),
                ("Joint", "联合"), ("Mixed", "混合"), ("Aggro", "仇恨型"), ("Kamikaze", "神风者"),
                ("Bombardier", "轰炸兵"), ("Animal", "动物"), ("Vulture", "秃鹫"), ("Claws", "利爪"),
                ("Frost Hound", "霜冻猎犬"), ("Arlene", "阿琳"), ("Zombie", "僵尸"), ("zombie", "僵尸"),
                ("Charged", "充能"), ("Elite", "精英"), ("Heavy", "重型"), ("Medium", "中型"), ("Light", "轻型"),
                ("Cop", "警察"), ("Burst", "连射"), ("Gun", "枪手"), ("Guardian", "守护者"),
                ("Avenger", "复仇者"), ("Berserk", "狂战士"), ("Destructor", "毁灭者"), ("Tesla", "特斯拉"),
                ("Rapidfire", "速射"), ("Metalist", "金属专家"), ("Awl", "尖锥"), ("Apple", "苹果"),
                ("Expedition", "远征"), ("Yautja", "铁血战士"), ("Generic", "通用"), ("Hammer", "战锤守卫"),
                ("Building", "建筑"), ("Food", "食物"), ("Minning", "挖矿"), ("Modding", "改装"),
                ("Mods", "改装件"), ("Miner", "采集器"), ("Ressources", "资源"), ("Super Charger", "机械增压器"),
                ("[DECEA3]LLarge[-]", "[DECEA3]超大型[-]"), ("[DECEA3]Large[-]", "[DECEA3]大型[-]"),
                ("[DECEA3]Medium[-]", "[DECEA3]中型[-]"), ("[DECEA3]Small[-]", "[DECEA3]小型[-]"),
                ("[DECEA3]BONUS[-]", "[DECEA3]额外容量[-]"), ("[DECEA3]Improved[-]", "[DECEA3]改良型[-]"),
                ("Add 等级 Traits", "添加等级特质"), ("Remove 等级 Traits", "移除等级特质"),
                ("Test ", "测试："), ("Traits ", "特质："), ("Fear ", "恐惧："), ("Timer", "计时器"),
                ("Perk ", "技能："), ("Counter", "计数器"), ("Reset", "重置"), ("MAX", "最大值"),
                ("Expert", "专家"), ("Refresh", "刷新"), ("Automatic Weapon", "自动武器"),
                ("Deco:", "装饰："), ("Master", "主控项"), ("Tilt Truck", "倾卸卡车"),
                ("Iron Bastion", "钢铁堡垒"), ("Mo Power", "莫尔电力"), ("Working Stiff", "结实工具"),
                ("Drink Jar Grandpas Forgetting灵药", "祖父忘忧灵药罐"), ("Irradiation ADD", "增加辐射量"),
                ("Irradiation Refresh", "刷新辐射状态"), ("Decorated", "装饰型"),
                ("The ", ""), (" of the ", "之"), ("extreme-挑战", "极限挑战"), ("hard-挑战", "高难挑战"),
                ("爆炸 Twins", "爆裂双子"), ("Ghost Fortress", "幽灵堡垒"), ("幽灵 Fortress", "幽灵堡垒"),
                ("Haunted围攻", "闹鬼围攻"), ("Defense: 幽灵", "幽灵防守"), ("Blood Breach", "鲜血突破"),
                ("Bloody March", "血腥行军"), ("Double Break", "双重破坏"), ("幽灵 Court", "幽灵法庭"),
                ("Shrouded巡逻", "迷雾巡逻"), ("Shattered Parade", "破碎游行"), ("Kings of the Broken Sky", "破碎天空诸王"),
                ("Last March of the Damned", "诅咒者的最后行军"), ("蘑菇怪 Colony", "菌菇群落"), ("蘑菇怪 Spore", "菌菇孢子"),
                ("Red Wedding", "血色婚礼"), ("Ape围攻", "巨猿围攻"), ("岩石 Barrage", "岩石弹幕"),
                ("Brothers", "兄弟会"), ("Coven Below", "地下女巫集会"), ("False Apocalypse", "伪末日"),
                ("铁 Funeral", "钢铁葬礼"), ("Crouch防守", "潜伏防守"), ("Crouch 尸群", "潜伏尸潮"),
                ("Cursed防守", "诅咒防守"), ("Spider防守", "蜘蛛防守"), ("Spider 尸群", "蜘蛛尸潮"),
                ("[FFB800]Big[-]", "[FFB800]大型[-]"), ("[5AFF75]Small[-]", "[5AFF75]小型[-]"),
                ("Big Hitters", "重击高手"), ("Big Hitter", "重击高手"), ("Handy Land", "巧手天地"),
                ("Scrapping 4 Fun", "快乐拆解"), ("Shotgun Weekly", "霰弹枪周刊"), ("Forge Ahead", "锻造前沿"),
                ("Rifle", "步枪"), ("Melee Tool", "近战工具"), ("Melee Wpn Baton", "近战警棍"), (" Baton", "警棍"),
                ("[FFB800]Reactor[-]", "[FFB800]反应堆型[-]"), ("Counts", "计数"), ("Game 计时器", "游戏计时器"),
                ("[DECEA3]Support[-]", "[DECEA3]支援型[-]"), ("44MAG", ".44马格南"), (".44 magnum", ".44马格南"),
                ("Iron Bastion", "钢铁堡垒"), ("铁 Bastion", "钢铁堡垒"),
                ("Burning", "烧焦感染者"), ("Chuck", "查克感染者"), ("Demolition", "爆破感染者"),
                ("Feral", "凶暴感染者"), ("Haz Mat", "危险品感染者"), ("Short", "矮小感染者"),
                ("Strong", "强壮感染者"), ("Worker", "工人感染者"), ("Bull", "蛮牛"), ("Carrier", "宿主"),
                ("Shocker", "电击者"), ("Grenade Contact Imp", "内部对象：改良型接触手榴弹"),
                ("Explosives T4火箭兵 Launcher Imp", "T4改良型火箭发射器"), ("Irradiation 刷新", "刷新辐射状态"),
                ("Warband 强壮感染者point", "战团据点"), ("爆破感染者 Squad", "爆破小队"), ("火焰 Team", "火力小组"),
                ("弓ler", "投球手"), ("Project ZBoss", "Project Z Boss"), ("Small迷你", "小型"),
                ("Alarmism", "杞人忧天"), ("Allergy Sufferer", "过敏体质"), ("Brittle Bones", "脆骨症"),
                ("Clumsy", "笨手笨脚"), ("Dementia", "痴呆"), ("Distrophia", "肌营养不良"),
                ("恐惧：Dark", "恐惧黑暗"), ("Fear火焰", "恐惧火焰"), ("恐惧：Pain", "恐惧疼痛"),
                ("Negative Traits", "负面特质"), ("Obesity", "肥胖"), ("Pacifist", "和平主义者"),
                ("Social Phobia", "社交恐惧"), ("Unlucker", "倒霉蛋"), ("Weak Immunity", "免疫力低下"),
                ("Weakling", "体弱者"), ("Charisma", "魅力"),
                ("蛮牛dog", "Bulldog"), ("战团 强壮感染者point", "战团据点"),
                ("Kings之Broken Sky", "破碎天空诸王"), ("Last March之Damned", "诅咒者的最后行军"),
                ("Trader Bob", "商人鲍勃"), ("商人 Bob", "商人鲍勃"),
                (" Ken", " 肯"), (" Noah", " 诺亚"), (" Derek", " 德里克"),
                (" Lorien", " 洛里安"), (" Ben", " 本"),
                ("老板专家", "Boss专家"), ("小老板", "小型Boss"),
            )
            updated = row[z]
            for source, target in phrase_replacements: updated = updated.replace(source, target)
            if updated != row[z]: chinese = updated
        if chinese is None: continue
        row[z] = chinese; output = io.StringIO(); csv.writer(output, lineterminator=ending, quoting=csv.QUOTE_MINIMAL).writerow(row); lines[index] = output.getvalue(); changed += 1
    with path.open("w", encoding="utf-8-sig", newline="") as handle: handle.write("".join(lines))
    print(f"98-AECxProjectZ_Tweaks/Config/Localization.csv: cleaned {changed} late-override entries")


translate_98_late_cleanup()


PROJECTZ_EXACT = {
    "[8692FF]Rogue[-]": "[8692FF]游荡者[-]", "[8692FF]Enforcer[-]": "[8692FF]执法者[-]",
    "[8692FF]Ranger[-]": "[8692FF]游骑兵[-]", "[8692FF]Nomad[-]": "[8692FF]游牧者[-]",
    "Light": "轻甲", "Medium": "中甲", "Heavy": "重甲", "The miniboss is nearby": "小型Boss就在附近",
    "Poisoning": "中毒", "You are poisoned": "你中毒了", "Treatment of poisoning": "中毒恢复",
    "You are recovering from poisoning": "你正在从中毒状态中恢复", "You are immune to poison": "你对毒素免疫",
    "You are affected by the antidote. You are immune to all poisonings.": "解毒剂正在生效。效果持续期间，你免疫所有中毒状态。",
    "Paralysis": "麻痹", "You are paralyzed": "你被麻痹了", "The poison instantly paralyzes your body.": "毒素瞬间麻痹了你的身体。",
    "Harry is on the hunt": "哈里正在狩猎", "Life Steal": "生命汲取",
    "You are affected by the mummy's scarabs. Every second, you lose health and give it to the mummy.": "木乃伊的圣甲虫正在侵蚀你。你每秒都会损失生命值，并将其转移给木乃伊。",
    "Hellish help": "地狱援军", "Hellish Boss Help": "地狱Boss援军", "Combistick": "组合长矛",
    "Used to craft items. Obtained from slain snakes.": "用于制造物品，可从被杀死的蛇类身上获得。",
    "Small bait": "小型诱饵", "This small lump of rotten meat gives off a foul smell. Mini-bosses react to it like a red rag.": "这小块腐肉散发着恶臭，对小型Boss有着难以抗拒的挑衅效果。",
    "Poisoned Blades Сooldown": "毒刃冷却", "Poisoned Blades on cooldown": "毒刃正在冷却",
    "Active ability [DECEA3]Paralysis[-] on cooldown\\n\\nWait until it ends to be able to activate it again": "主动技能[DECEA3]麻痹[-]正在冷却。\\n\\n等待冷却结束后才能再次发动。",
    "You have become less sloppy": "你变得更爱干净了", "You better take care of your hygiene": "最好注意一下个人卫生",
    "[E092FF]Yautja[-]": "[E092FF]铁血战士[-]",
    "Your body is wracked with terrible pain from the poison. You gradually accumulate the poisoning effect. When the poisoning reaches 100%, you will take damage and have a [DECEA3]25%[-] chance of being paralyzed. After this, you will begin to recover from the poisoning. Your mobility and jump height are reduced based on accumulated dispatch, up to [DECEA3]50%[-]. Every second, you lose [DECEA3]9[-] stamina and a moderate amount of food. Use an antidote or wait for recovery.": "毒素令你浑身剧痛，中毒程度会逐渐累积。达到100%时，你会受到伤害，并有[DECEA3]25%[-]的概率陷入麻痹，随后才会开始恢复。移动速度和跳跃高度会随中毒程度降低，最多降低[DECEA3]50%[-]。每秒损失[DECEA3]9[-]点耐力和中等饱食度。使用解毒剂或等待自然恢复。",
    "The pain still lingers, but you feel yourself getting better. You're gradually shedding the effects of the poisoning. Your mobility and jump height are reduced depending on the accumulated poison, up to [DECEA3]50%[-]. Every second, you lose [DECEA3]3[-] stamina and a small supply of food. Use an antidote or wait for recovery.": "疼痛尚未完全消退，但你的情况正在好转，中毒效果会逐渐减弱。移动速度和跳跃高度会随剩余毒素降低，最多降低[DECEA3]50%[-]。每秒损失[DECEA3]3[-]点耐力和少量饱食度。使用解毒剂或等待自然恢复。",
    "Everyone thinks that truly large snakes are only found in the tropics. How wrong they are. It's hard to tell whether this is one snake or a cluster of several, but it's better not to look too closely. The very sight of this creature chills the soul and paralyzes the will. If you are not ready to face this horror face to face — LEAVE!\\n\\n[FFB800]SPECIAL PROPERTIES:[-]\\nThis creature never walks alone and is able to summon its minions in times of danger.\\nAdditional weak sanity drain every second while you are nearby.\\n\\nYour vision goes dark and everything starts spinning. But you can still RUN!!!": "所有人都以为真正的巨蛇只会出现在热带——事实证明他们错得离谱。很难判断眼前究竟是一条蛇，还是许多条蛇纠缠形成的怪物，但最好别凑近观察。仅仅看见它就足以令人灵魂战栗、意志僵硬。如果你还没有准备好直面这种恐怖——立刻离开！\\n\\n[FFB800]特殊能力：[-]\\n它从不独自行动，遭遇危险时会召唤仆从。\\n待在它附近时，每秒还会额外损失少量理智。\\n\\n视野开始变暗，世界天旋地转——但你仍然可以逃跑！",
    "Harry": "哈里",
    "This creature is composed almost entirely of a molten substance. What could have caused such a mutation? It's best to think about it in a safe place. Think twice before engaging in battle.\\n\\n[FFB800]SPECIAL PROPERTIES:[-]\\nThe boss scatters a molten substance around itself, setting enemies ablaze.\\n\\nThe flames scorch, and fear is impossible to hide. But the survival instinct screams: RUN! Before you get fried!": "这种生物几乎完全由熔融物质构成。究竟是什么导致了如此可怕的变异？最好到安全的地方再思考这个问题。与它交战前务必三思。\\n\\n[FFB800]特殊能力：[-]\\nBoss会在周围抛撒熔融物质，点燃附近的敌人。\\n\\n烈焰灼烧着身体，恐惧无处可藏，而求生本能只会尖叫：快逃！别让自己被烤熟！",
    "The boss is calling for reinforcements. Minions have appeared around the boss. Your physical damage resistance has been reduced by [FFB800]25%[-].\\nTry to wait it out and stay alive.": "Boss正在呼叫增援，仆从已经出现在它周围。你的物理伤害抗性降低了[FFB800]25%[-]。\\n设法撑过这段时间并活下来。",
}


def translate_projectz_remaining() -> None:
    path = ROOT / "01-ProjectZ/Config/Localization.csv"
    raw_lines = path.read_text(encoding="utf-8-sig").splitlines(keepends=True)
    header = next(csv.reader([raw_lines[0]])); cols = {x.lower(): i for i, x in enumerate(header)}
    en_index, zh_index = cols["english"], cols["schinese"]
    changed = 0
    for index in range(1, len(raw_lines)):
        line = raw_lines[index]; ending = "\r\n" if line.endswith("\r\n") else "\n" if line.endswith("\n") else ""
        row = next(csv.reader([line.removesuffix(ending)]))
        if len(row) <= max(en_index, zh_index) or row[en_index].strip() != row[zh_index].strip(): continue
        english = row[en_index]; chinese = PROJECTZ_EXACT.get(english)
        summon = re.fullmatch(r"You must place a bait to summon (\[[0-9A-F]+\].+\[-\])\\n\\nBe extremely careful!", english)
        if chinese is None and summon:
            chinese = f"你必须放置诱饵来召唤{summon.group(1)}。\\n\\n务必小心！"
        if chinese is None: continue
        row[zh_index] = chinese; out = io.StringIO(); csv.writer(out, lineterminator=ending).writerow(row); raw_lines[index] = out.getvalue(); changed += 1
    with path.open("w", encoding="utf-8-sig", newline="") as handle: handle.write("".join(raw_lines))
    print(f"01-ProjectZ/Config/Localization.csv: updated {changed} remaining entries")


translate_projectz_remaining()


EVENT_FAMILIES = [
    ("AllInOne", "全能Boss"), ("MinionsArmy", "AEC仆从军团"),
    ("DoomlordArcher", "毁灭领主与弓箭手"), ("BloodBroodAndGallows", "无头者、行刑者与迈基尔"),
    ("HeadlessGhost", "无头者与幽灵"), ("Headless", "无头者"), ("Ghost", "幽灵"),
    ("Dumdum", "爆破狂人"), ("Executioner", "行刑者"), ("ElectricDemon", "电魔"),
    ("ExplosiveEagle", "爆裂鹰"), ("HammerGuardian", "战锤守卫"), ("Sheriff", "警长"),
    ("Hellskyli", "赫尔斯凯利"), ("Mykir", "迈基尔"), ("PartyBeach", "海滩狂欢者"),
    ("Rockbreaker", "碎岩者"), ("Kamikaze", "狂奔神风者"), ("Singerie", "辛格里"),
    ("SirenHead", "警笛头"), ("Druid", "德鲁伊"), ("Mechanician", "机械师"),
    ("Witch", "女巫"), ("Mushroom", "菌菇怪"), ("Archer", "弓箭手"),
]


def event_family(key: str, english: str) -> str:
    for token, name in EVENT_FAMILIES:
        if token.lower() in key.lower(): return name
    if "mixed" in english.lower(): return "混合"
    return "敌方"


def translate_remaining_gameevents() -> None:
    path = ROOT / "03-AEC-AIO_BOSS_EXTREME_EDITION/Config/Localization.csv"
    lines = path.read_text(encoding="utf-8-sig").splitlines(keepends=True)
    header = next(csv.reader([lines[0]])); c = {x.lower(): i for i, x in enumerate(header)}; e, z = c["english"], c["schinese"]
    changed = 0
    for i in range(1, len(lines)):
        line = lines[i]; ending = "\r\n" if line.endswith("\r\n") else "\n" if line.endswith("\n") else ""; row = next(csv.reader([line.removesuffix(ending)]))
        if len(row) <= max(e, z) or (row[1] != "gameevents" and not row[0].startswith("event")) or row[e].strip() != row[z].strip() or not row[e].strip(): continue
        key, english = row[0], row[e]; family = event_family(key, english)
        star_match = re.search(r"( \[[1-5]★\])$", english); star = star_match.group(1) if star_match else ""
        kill = re.search(r"Kill (\d+) zombies", english, re.I)
        wave = re.search(r"(?:WAVE|Wave)\s*(\d+)", english)
        if "FINAL WAVE" in english.upper() or "Final " in english or "LAST WAVE" in english.upper():
            chinese = f"最后一波：{family}部队正在发动最终进攻。"
        elif wave:
            chinese = f"第{wave.group(1)}波：{family}部队正在进攻。"
        elif "PHASE" in english.upper():
            phase = re.search(r"PHASE\s*(\d+)", english, re.I)
            chinese = f"{'第' + phase.group(1) + '阶段' if phase else '最终阶段'}：{family}加入战斗。"
        elif "BOSS" in english.upper() or "BossSpawn" in key:
            chinese = f"Boss：{family}已进入战场。"
        elif any(x in english.upper() for x in ("DEFENSE", "SIEGE", "LAST STAND")):
            chinese = f"防守警报：{family}部队正在进攻阵地，守住防线。"
        elif any(x in english.upper() for x in ("ALERT", "HUNT", "HORDE", "WARBAND", "ASSAULT", "PATROL", "LEGENDARY")) or "Arrival" in key:
            chinese = f"战斗警报：{family}部队正在接近，做好准备。"
        else:
            chinese = f"战斗警报：{family}部队正在行动。"
        if kill: chinese += f"击杀{kill.group(1)}只僵尸即可击溃本轮进攻。"
        row[z] = chinese + star
        out = io.StringIO(); csv.writer(out, lineterminator=ending).writerow(row); lines[i] = out.getvalue(); changed += 1
    with path.open("w", encoding="utf-8-sig", newline="") as handle: handle.write("".join(lines))
    print(f"03-AEC-AIO_BOSS_EXTREME_EDITION/Config/Localization.csv: updated {changed} remaining game events")


translate_remaining_gameevents()


QUEST_TYPES = [
    ("LastStand", "最终坚守"), ("Endless", "无尽挑战"), ("DefenseHunt", "据点猎杀"),
    ("Defense", "据点防守"), ("Siege", "围攻"), ("Assault", "突击"), ("Horde", "尸潮"),
    ("Patrol", "巡逻队"), ("Hunt", "猎杀"), ("Wave", "波次挑战"), ("Gauntlet", "试炼"),
    ("Contract", "契约"),
]


def quest_type(key: str) -> str:
    for token, name in QUEST_TYPES:
        if token.lower() in key.lower(): return name
    return "契约"


def wave_count(english: str):
    words = {"one": 1, "two": 2, "three": 3, "four": 4, "five": 5, "six": 6, "seven": 7, "ten": 10}
    m = re.search(r"\b(100|\d+|one|two|three|four|five|six|seven|ten)\b[^.]{0,35}\bwaves?\b", english, re.I)
    if not m: return None
    return words.get(m.group(1).lower(), int(m.group(1)) if m.group(1).isdigit() else None)


def translate_remaining_quests() -> None:
    path = ROOT / "03-AEC-AIO_BOSS_EXTREME_EDITION/Config/Localization.csv"
    lines = path.read_text(encoding="utf-8-sig").splitlines(keepends=True); header = next(csv.reader([lines[0]])); c={x.lower():i for i,x in enumerate(header)}; e,z=c['english'],c['schinese']; changed=0
    for i in range(1,len(lines)):
        line=lines[i]; ending="\r\n" if line.endswith("\r\n") else "\n" if line.endswith("\n") else ""; row=next(csv.reader([line.removesuffix(ending)]))
        if len(row)<=max(e,z) or (row[1]!="quests" and not row[0].startswith("questAEC")) or not row[e].strip() or row[e].strip()!=row[z].strip(): continue
        key,english=row[0],row[e]; family=event_family(key,english); qtype=quest_type(key); count=wave_count(english)
        star_m=re.search(r"( \[[1-5]★\])$",english); star=star_m.group(1) if star_m else ""
        if "{poi.distance}" in english:
            chinese="AEC契约（[DECEA3]{poi.distance} {poi.direction}[-]）[FF9900]{poi.name}[-]"
        elif key.lower().endswith("_name"):
            chinese=f"{family}{qtype}"
        elif "subtitle" in key.lower():
            chinese=f"{family}：{qtype}目标"
        elif "obj_" in key.lower() or "objective" in key.lower():
            wave_m=re.search(r"wave\s*(\d+)",key,re.I)
            chinese=f"抵御第{wave_m.group(1)}波{family}部队" if wave_m else f"完成{family}{qtype}目标"
        elif "completion" in key.lower():
            chinese=f"契约完成，{family}威胁已经清除。领取你的报酬。"
        elif "response" in key.lower():
            chinese=f"契约已接受。我会完成{family}{qtype}。"
        else:
            poi="先清除兴趣点内的敌人，" if any(x in english.lower() for x in ("poi","location","site","sleepers")) else "激活集结点并放置诱饵，"
            waves=f"抵御{count}波{family}部队" if count else f"抵御{family}部队"
            boss=any(x in english.lower() for x in ("kill the boss","kill the ","slay ","boss itself","face the boss")) or "hunt" in key.lower()
            kill=re.search(r"kill\s+(\d+)\s+zombies",english,re.I)
            ending_text=f"，再击杀{family}Boss。" if boss else "。"
            if kill: ending_text=f"，最后击杀{kill.group(1)}只僵尸以结束进攻。"
            chinese=poi+waves+ending_text
        row[z]=chinese+star; out=io.StringIO(); csv.writer(out,lineterminator=ending).writerow(row); lines[i]=out.getvalue(); changed+=1
    with path.open('w',encoding='utf-8-sig',newline='') as h: h.write(''.join(lines))
    print(f"03-AEC-AIO_BOSS_EXTREME_EDITION/Config/Localization.csv: updated {changed} remaining quest entries")


translate_remaining_quests()


def translate_remaining_boss_items() -> None:
    path=ROOT/"03-AEC-AIO_BOSS_EXTREME_EDITION/Config/Localization.csv"; lines=path.read_text(encoding='utf-8-sig').splitlines(keepends=True); header=next(csv.reader([lines[0]])); c={x.lower():i for i,x in enumerate(header)}; e,z=c['english'],c['schinese']; changed=0
    for i in range(1,len(lines)):
        line=lines[i]; ending="\r\n" if line.endswith("\r\n") else "\n" if line.endswith("\n") else ""; row=next(csv.reader([line.removesuffix(ending)]))
        if len(row)<=max(e,z) or not row[0].startswith('itemAEC') or not row[e].strip() or row[e].strip()!=row[z].strip(): continue
        key,english=row[0],row[e]; family=event_family(key,english); qtype=quest_type(key); count=wave_count(english); star_m=re.search(r"( \[[1-5]★\])$",english); star=star_m.group(1) if star_m else ""
        if 'Bundle' in key or 'reward' in english.lower() or english.lower().startswith('open'):
            quality='高级' if any(x in english.lower() for x in ('premium','top-tier','best','rare','high-value','elite')) else ''
            chinese=f"打开后可获得{family}{quality}变异样本以及对应的契约补给奖励。"
        elif 'Contract' in key:
            poi='先清除并守住指定兴趣点，' if any(x in english.lower() for x in ('poi','location','site','position','defend')) else '放置信号诱饵，'
            waves=f"抵御{count}波{family}部队" if count else f"抵御{family}部队"
            boss=any(x in english.lower() for x in ('kill','boss','slay','bring it down','destroy')) or 'Hunt' in key
            chinese=f"{qtype}契约：{poi}{waves}{'，然后击杀Boss。' if boss else '。'}"
        else:
            chinese=f"AEC {family}相关物品，用于对应契约与进度。"
        row[z]=chinese+star; out=io.StringIO(); csv.writer(out,lineterminator=ending).writerow(row); lines[i]=out.getvalue(); changed+=1
    with path.open('w',encoding='utf-8-sig',newline='') as h:h.write(''.join(lines))
    print(f"03-AEC-AIO_BOSS_EXTREME_EDITION/Config/Localization.csv: updated {changed} remaining item entries")


translate_remaining_boss_items()


ENDGAME_TIERS = {
    0:"基础",1:"凶暴",2:"辐射",3:"充能",4:"炼狱",5:"老兵",6:"残暴",7:"凶猛",8:"劫掠者",
    9:"恐惧",10:"梦魇",11:"巅峰",12:"霸主",13:"泰坦",14:"灾变",15:"神话",
}
ENDGAME_SIZES={1:"小型",2:"中型",3:"大型",4:"巨型",5:"超大型"}
ACTION_ZH={"Kill":"击杀","Craft":"制造","Collect":"收集","Possess":"持有","Obtain":"获得"}


def endgame_objective(english):
    matches=list(re.finditer(r"\b(Kill|Craft|Collect|Possess|Obtain)\s+(\d+)\s+(.+?)(?=\.\s|\.$|$)",english,re.I))
    if not matches:return None
    m=matches[-1]; action=ACTION_ZH.get(m.group(1).title(),m.group(1)); target=m.group(3)
    replacements={"Minions":"仆从","Boss":"Boss","Research Paper":"研究论文","Part Miner":"部件采集器","mutation samples":"变异样本","Universal Tokens":"通用代币","Zombies":"僵尸","Wood":"木材","Forged Iron":"锻铁"}
    for a,b in replacements.items():target=target.replace(a,b)
    gs=re.search(r"Required Gamestage:\s*(\d+)",english,re.I)
    result=f"[5ECFFF]目标[-] {action}{m.group(2)}个{target}。"
    if gs:result+=f" 所需游戏阶段：[FFD866]{gs.group(1)}[-]。"
    return result


def translate_endgame_quests():
    path=ROOT/'04-AEC-ENDGAME_OVERHAUL/Config/Localization.csv';lines=path.read_text(encoding='utf-8-sig').splitlines(keepends=True);header=next(csv.reader([lines[0]]));c={x.lower():i for i,x in enumerate(header)};e,z=c['english'],c['schinese'];changed=0
    for i in range(1,len(lines)):
        line=lines[i];ending='\r\n' if line.endswith('\r\n') else '\n' if line.endswith('\n') else '';row=next(csv.reader([line.removesuffix(ending)]))
        if len(row)<=max(e,z) or row[e].strip()!=row[z].strip() or not row[e].strip():continue
        key,english=row[0],row[e]; chinese=None
        tier_size=re.fullmatch(r'aec_quest_T(\d{2})_A([1-5])_(name|offer)',key)
        if tier_size:
            tier=int(tier_size.group(1));size=int(tier_size.group(2));label=ENDGAME_TIERS[tier]
            if tier_size.group(3)=='name':chinese=f"[FFD700][AEC][-] [DECEA3]T{tier:02d} {label}清剿[-] | {ENDGAME_SIZES[size]}"
            else:chinese=f"[FFD700][AEC][-]\\n清除感染区。一个{ENDGAME_SIZES[size]}兴趣点已被标记，其中有T{tier:02d}{label}级AEC敌人。清除全部敌人后返回任意商人处领取奖励。"
        elif re.fullmatch(r'aec_quest_T\d{2}_statement',key):
            tier=int(re.search(r'T(\d{2})',key).group(1));chinese=f"[AEC T{tier:02d}] 僵尸清剿"
        elif key.startswith('aec_eo_progression_') or key.startswith('aec_eo_tutorial_'):
            obj=endgame_objective(english)
            if obj:chinese=f"完成AEC终局进度目标。{obj}"
        elif key=='aec_quest_T15_offer':chinese="最终清剿。已确认最高强度的AEC敌人，威胁等级T15。清除目标地点后返回领取奖励。"
        elif key=='aec_quest_clear_desc':chinese="AEC僵尸清剿令：兴趣点内的所有沉睡者都会替换为本命令指定等级的AEC敌人。前往兴趣点，消灭全部僵尸，然后返回任意商人处领取奖励。"
        elif key=='aec_base_offer':chinese="有一项AEC任务等待处理。"
        elif key.startswith('questSupport_'):chinese="申请物资配送"
        elif key=='questDescriptionKey_AirdropRequest':chinese="你申请了一次空投。请在室外放置背包中的标记旗来标明位置，并警惕附近的僵尸。只有清除区域内的敌人后，我们才能投送包裹。"
        if chinese is None:continue
        row[z]=chinese;out=io.StringIO();csv.writer(out,lineterminator=ending).writerow(row);lines[i]=out.getvalue();changed+=1
    with path.open('w',encoding='utf-8-sig',newline='') as h:h.write(''.join(lines))
    print(f"04-AEC-ENDGAME_OVERHAUL/Config/Localization.csv: updated {changed} generated quest entries")


translate_endgame_quests()


ENDGAME_EXACT={
    'Mods':'改装件','Power':'威力','Degradation':'耐久损耗','Shocking':'电击','APM':'每分钟攻击次数','Aiming':'瞄准','RPM':'每分钟射速','Drum':'弹鼓',
    'XPKill':'击杀经验','XPArmor':'护甲经验','Ammos/Weapons':'弹药/武器','Passive Miner':'被动采集器','Armors':'护甲','Ressources':'资源','Quests':'任务','Modded items':'改装物品',
    'Unlocks a 5% chance to harvest a queen bee from trees.':'解锁从树木中采集到蜂后的5%概率。','Melee attacks cleave through additional enemies.':'近战攻击可以顺劈额外的敌人。',
    'Each rank reduces melee weapon degradation per use.':'每一级都会降低近战武器每次使用造成的耐久损耗。','Each rank reduces tool degradation per use.':'每一级都会降低工具每次使用造成的耐久损耗。',
    'Melee attacks reduce the effective armor of your target.':'近战攻击会降低目标的有效护甲。','[50DC50]Newbie protection[-]':'[50DC50]新手保护[-]','[00FF66]Stable Zone[-]':'[00FF66]稳定区域[-]',
    'You are below Game Stage 100. AEC events will not directly target you or alter nearby spawns for you. Heatmap won\'t affect your surrounding spawns.':'你的游戏阶段低于100。AEC事件不会直接以你为目标，也不会为你改变附近的生成；热度系统不会影响你周围的生成。',
    'Area stable. No active escalation detected in this biome. T05-T15 zombies cannot spawn in the forest biome, but they can spawn in other biomes.':'区域状态稳定。该生物群系未检测到活跃升级。森林中不会生成T05至T15僵尸，但其他生物群系仍可能生成。',
    '[FFD700]AEC[-] Extreme Workstation':'[FFD700]AEC[-] 极限工作站','[FFD700]AEC[-] Call for Airdrop':'[FFD700]AEC[-] 呼叫空投',
    'A salvaged radio converted into a black market crafting station. Use it to craft exclusive AEC items.':'由废旧无线电改装而成的黑市制造站，用于制造AEC专属物品。',
    'A legacy AEC workstation repurposed for safe two-way exchange of mutation samples. It provides the same upgrade and downgrade recipes as the main Mutation Exchange.':'重新改装的旧式AEC工作站，可安全双向兑换变异样本，并提供与主变异兑换台相同的升级和降级配方。',
    'Converts standard ammunition and tiered AEC mutation samples into larger ammunition bundles. Higher sample tiers increase quantity only; ammunition stats do not change. Crafted in the Extreme Workstation.':'将普通弹药与分级AEC变异样本转化为更大的弹药包。更高等级的样本只会增加数量，不会改变弹药属性。可在极限工作站制造。',
    'Allows you to mark your position to planes and helicopters for AEC airdrop.':'用于向飞机和直升机标记你的位置，以接收AEC空投。',
    'Read the AEC progression and Game Stage field guide.':'阅读AEC进度与游戏阶段野外指南。','Read how AEC protects players below GS 100 and GS 600.':'阅读AEC对GS100和GS600以下玩家的保护机制。',
    'Read the current Heatmap gain, decay, warning and cooldown rules.':'阅读当前热度的增加、衰减、警告和冷却规则。','Read how Heatmap escalation changes biome danger from Hazard 0 to 5.':'阅读热度升级如何将生物群系危险度从0提高到5。',
    'Read how wilderness, POI, biome, Storm and Blood Moon spawning differ.':'阅读荒野、兴趣点、生物群系、风暴和血月生成机制的差异。','Read how biome invasions and the AI Master event schedule work.':'阅读生物群系入侵与AI主控事件日程的运作方式。',
    'Read about persistent Nemesis identities, ranks, progression and death.':'阅读宿敌的持久身份、等级、成长和死亡机制。','Read about Nemesis grudges, revenge, territory wars and migration.':'阅读宿敌的仇恨、复仇、领地战争和迁移机制。',
    'Read how trader contracts, POI sizes, tier overrides and tokens work.':'阅读商人契约、兴趣点规模、等级覆盖与代币机制。','Read the AEC crafting economy from kill rewards to endgame mutators.':'阅读从击杀奖励到终局变异器的AEC制造经济。',
    'Open to receive extra ammo quantity from the Necroforge. Does not increase ammo damage or penetration or effects.':'打开后获得死灵锻炉生产的额外弹药数量；不会提高弹药伤害、穿透或特殊效果。',
    'Virtual currency created from batteries. Use it in the [FFFF00]zombie market terminal[-].':'使用电池生成的虚拟货币，可在[FFFF00]僵尸市场终端[-]中使用。',
    'A connection card assembled from the associated boss and minion parts. Use it to mint the matching Coin from batteries.':'由对应Boss与仆从部件组装而成的连接卡，用于通过电池铸造相应硬币。',
    'Zombie activity in this biome is stable. No escalation yet. [0000FF]To increase the hazard lvl, kill zombies in this biome.[-]':'该生物群系的僵尸活动稳定，尚未升级。[0000FF]在此生物群系击杀僵尸可提高危险等级。[-]',
    'Low zombie escalation nearby. Activity is building up. [0000FF]To decrease the hazard lvl, stop killing zombies in this biome.[-]':'附近出现低度升级，活动正在增强。[0000FF]停止在此生物群系击杀僵尸可降低危险等级。[-]',
    'Moderate zombie escalation. Patrols converging on your position. [0000FF]To decrease the hazard lvl, stop killing zombies in this biome.[-]':'僵尸活动中度升级，巡逻队正在向你的位置集结。[0000FF]停止在此生物群系击杀僵尸可降低危险等级。[-]',
    'High zombie escalation. Multiple packs inbound - stay vigilant. [0000FF]To decrease the hazard lvl, stop killing zombies in this biome.[-]':'僵尸活动高度升级，多支怪群正在接近，保持警惕。[0000FF]停止在此生物群系击杀僵尸可降低危险等级。[-]',
    'Severe escalation. Mass convergence imminent. Prepare defenses. [0000FF]To decrease the hazard lvl, stop killing zombies in this biome.[-]':'严重升级，大规模集结即将发生，准备防御。[0000FF]停止在此生物群系击杀僵尸可降低危险等级。[-]',
    'CRITICAL HAZARD. Maximum horde density. Evacuate or fortify immediately. [0000FF]To decrease the hazard lvl, stop killing zombies in this biome.[-]':'危险等级已达临界值，尸群密度达到上限。立即撤离或加固防线。[0000FF]停止在此生物群系击杀僵尸可降低危险等级。[-]',
    'Base mutator. Can be used to make custom mutators to increase your skills.':'基础变异器，可用于制造提升能力的自定义变异器。',
    '[FFD700]AEC[-] [FFFFFF]Extreme Mod Crafting Table[-]':'[FFD700]AEC[-] [FFFFFF]极限改装工作台[-]','Dedicated workstation for extreme AEC modifiers.':'专门用于制造AEC极限改装件的工作台。',
    'Join the AEC PROJECT Discord for any mod question or request.':'如有任何模组问题或需求，请加入AEC PROJECT Discord。','AEC Mod Crafting Table':'AEC改装工作台','Unified workstation for all AEC modifiers.':'用于制造所有AEC改装件的统一工作台。',
    'Read this field guide. It contains information only and has no objective or reward.':'阅读这本野外指南。它只提供信息，没有任务目标或奖励。',
    'Charge (30 000 max)':'电量（上限30,000）','Booze Barrel Airdrop':'酒桶空投','Recreates previously opened GUIDE, TUTORIAL, and PROGRESSION quest books for 10 sheets of paper each.':'使用10张纸重新制作已经解锁的“指南”“教程”或“进度”任务书。',
    'COOKING\\n\\nPassively generates a random [DECEA3]alcoholic drink[-] over time.\\n\\nProduces [DECEA3]1 alcoholic drink every 30 minutes[-].':'烹饪\\n\\n随时间被动生成随机[DECEA3]酒精饮料[-]。\\n\\n每30分钟生产[DECEA3]1份酒精饮料[-]。',
}

ENDGAME_GUIDES={
'aec_eo_field_guide_01_text':'[FFD700]进度与游戏阶段[-] AEC难度依据真实游戏阶段（GS），而不只看角色等级。T00至T04依次为基础、凶暴、辐射、充能和炼狱。完整终局路线从GS600的T05老兵开始，继续提升至T15神话。等级越高，生命、伤害、速度、机动性和奖励越高。任务可在活动兴趣点内部强制指定等级，外部荒野仍采用动态选择。',
'aec_eo_field_guide_02_text':'[50DC50]新手保护[-] GS0至99时启用完整保护：危险AEC事件不会直接选择你，附近生成也不会受到热度或风暴危险加成。GS100至599时绝对保护结束，但普通荒野仍无法进入GS600以上的T05+路线；Boss热度事件和宿敌现身需要至少一名GS600玩家。',
'aec_eo_field_guide_03_text':'[64B4FF]热度压力[-] 每次有效击杀使所在生物群系热度增加1%。连续60秒没有击杀后，每现实分钟降低5%。70%时发出警告，100%时准备下一次升级。完成升级链后有60分钟重启冷却。持续战斗会维持压力，停止战斗可让热度冷却。',
'aec_eo_field_guide_04_text':'[FF8800]危险升级[-] 危险0表示热度正在积累但尚未强化生物群系。每次热度达到100%可使危险等级提高一级，并可能生成带4名护卫的Boss。GS600后可逐级升至危险5。热度归零且区域安静至少5分钟后，危险等级可下降一级。危险5为最高压力。',
'aec_eo_field_guide_05_text':'[FFD700]动态生成[-] AEC会根据真实GS、生物群系、昼夜和当前环境强化原版生成。领地石100米范围内会抑制自定义压力，本地3×3单元超过15只僵尸时停止追加跟随者。松树林是T05至T15安全生物群系。任务等级覆盖只在活动兴趣点清剿阶段生效；风暴和血月采用各自规则。',
'aec_eo_field_guide_06_text':'[FFCC33]AI世界事件[-] AI主控每隔60至120现实分钟随机选择一次世界压力事件。事件可针对生物群系、限时运行并在结束时迁移。服务器无人存活在线时，事件与宿敌计时会暂停。GS0至99玩家不会成为危险事件目标。请认真对待服务器消息和HUD警告。',
'aec_eo_field_guide_07_text':'[FF3333]宿敌名册[-] 宿敌是会长期存在的世界级AEC敌人。记录包含独特姓名、称号、等级、系列、层级、领地、战斗历史和虚拟属性。等级分为低阶、高阶、精英、冠军、传奇和世界级。每个宿敌需死亡2至5次才会永久移除，全部状态保存到JSON并跨世界重载保留。',
'aec_eo_field_guide_08_text':'[FF8800]仇恨、战争与迁移[-] 击败宿敌或相关AEC Boss可能触发个人仇恨与家族复仇。宿敌会进行权力斗争和领地战争，胜者晋升、败者降级，称号也可能改变。GS600以下玩家无法通过进入领地触发宿敌现身。宿敌会长期迁移，因此旧有安全路线也可能进入敌对领地。',
'aec_eo_field_guide_10_text':'[D9B36C]样本、市场与装备[-] 有效的AEC击杀会直接给予玩家分级变异样本和感染者牙齿；完成AEC契约会获得AEC通用代币。变异样本兑换台可用五份样本合成一份下一等级样本，也可将一份样本分解为五份上一等级样本。\\n\\n僵尸市场终端是经济系统的核心：申请加密货币矿机，装入正确的电池和对应系列、星级的连接卡来生产Boss硬币，再将硬币投入研究论文、被动部件采集器、契约与补给。AEC极限工作站和死灵锻炉使用通用代币及分级变异样本制造终局改装件与装备。血月排名奖励归个人所有，热度Boss事件则单独结算。',
}

ENDGAME_BY_KEY={
'aecEOArchiveWorkshopPrecautionDesc':'警告：如果阅读的“教程”或“进度”书籍等级低于当前解锁等级，档案进度会回退到该书对应的等级，之后解锁的所有更高等级内容都会丢失。阅读前务必核对书籍编号。为防止误操作，建议复制并保留本说明以及当前最高等级的书籍作为备份。',
'aecEOArchiveWorkshopPrecautionTuto':'[FF4500]使用档案工作站前请务必阅读[-]',
'aecEOArchiveWorkshopPrecautionProg':'[FF4500]使用档案工作站前请务必阅读[-]',
'lblCategoryAECArmorStaminaRegenRun':'内部对象：奔跑耐力恢复',
'aec_dialog_trader_response_aecjobs':'[A91CDA]> AEC终局契约 <[-]',
'aec_info_response_text':'[46A0F5]> AEC终局教程 <[-]',
'aec_tip1_text':'[FFCC33]1.[FF9900] 完成原版一级商人任务，即可看到首批AEC T00任务。[-]',
'aec_ltip_unlock_text':'[FFD700]── 解锁方式 ──[-]',
'aec_tip2b_text':'[FFCC33]2.[FF9900] 随着游戏阶段提高，将逐步解锁T01至T15。[-]',
'aec_tip2d_text':'[FFCC33]   原版T1=AEC T00 | 原版T2=AEC T01 | 原版T3=AEC T02 | 原版T4=AEC T03 | 原版T5=AEC T04[-]',
'aec_tip2c_text':'[FFCC33]   原版T6且GS600=AEC T05 | GS1200=T06 | GS2000=T07 | GS3200=T08 | GS5000=T09 | GS7500=T10[-]',
'aec_tip3_text':'[FFCC33]   GS10500=T11 | GS14000=T12 | GS18000=T13 | GS22000=T14 | GS25000=T15[-]',
'aec_tip4_text':'[FFCC33]3.[FF9900] 游戏阶段会自动更新，可在技能树页签中查看。[-]',
'aec_tip_go_text':'[A217D7]> 浏览可接取的AEC契约 <[-]',
'aec_go_tutorial_text':'[5599FF][?] AEC契约如何运作？（教程）[-]',
'aec_tp3_l1_text':'[FFD700]── 兴趣点规模 ──[-]',
'aec_tp3_l2_text':'[AAAAAA]A1[-] 小型 | [AAAAAA]A2[-] 中型 | [AAAAAA]A3[-] 大型 | [AAAAAA]A4[-] 巨型 | [AAAAAA]A5[-] 超大型',
'aec_tp3_l4_text':'[00FF66]每个等级先从小型兴趣点开始，最后挑战超大型兴趣点。[-]',
'aec_tp4_l1_text':'[FFD700]── 僵尸等级 ──[-]',
'aec_tp4_l3_text':'[FF3333]T15神话级的攻击伤害约为T05的10倍，请相应提升装备。[-]',
'aec_tp5_l1_text':'[FFD700]── 危险系统 ──[-]',
'aec_tp5_l3_text':'危险等级越高，怪群越密集，出现的僵尸等级也越高。',
'aec_tp5_l4_text':'[FF3333]危险5：即将发生大规模集结，请立即撤离或加固防线。[-]',
'aec_tp5_l2_text':'在生物群系中击杀僵尸会提高该区域的危险等级（1至5）。',
'aec_tp6_l2_text':'赌场币：[00FF66]500枚（T05）[-]，最高[FF4500]7,500枚（T15）[-]',
'aec_tp6_l3_text':'经验：最高[FFCC33]750,000点[-]（T15 A5）| 每份契约3个战利品栏位',
'aec_tp7_l1_text':'[FFD700]── AEC装备与掉落 ──[-]',
'aec_tp7_l2_text':'AEC装备采用[FFCC33]武器、工具或护甲变异改装件[-]的形式。',
'aec_tp7_l3_text':'材料来源：Boss和高等级僵尸掉落、任务奖励及其他战利品。',
'aec_th_p8_text':'[FFD700]── 变异样本与兑换 ──[-]',
'aec_tp8_l1_text':'使用[FFCC33]变异样本兑换台[-]升级或降级不同等级的[8EBE67]变异样本[-]。',
'aec_tp8_l2_text':'有效的AEC击杀会自动给予样本；敌人等级决定样本的等级和数量。',
'aec_tp8_l3_text':'在配方、升级和进度中使用[FFD700]AEC通用代币[-]与[8EBE67]变异样本[-]。',
'aec_tp8_l4_text':'[00FF66]五份样本可升级为一份下一等级样本；一份样本可降级为五份上一等级样本。[-]',
'aec_th_p9_text':'[FFD700]── 宿敌系统 ──[-]',
'aec_tp9_l3_text':'每个宿敌都有自己的[FF8800]领地[-]、[FF3333]姓名[-]和[FFCC33]称号[-]（例如“屠夫”裂齿）。',
'aec_tp9_l1_text':'你的世界中会同时存在一批[FF3333]拥有独立姓名的宿敌[-]Boss。',
'aec_tp9_l4_text':'多次击杀同一宿敌可将其永久消灭，空缺位置随后会由新的宿敌补充。',
'aec_th_p10_text':'[FFD700]── 宿敌：仇恨与战争 ──[-]',
'aec_tp10_l3_text':'宿敌会定期发动[FFCC33]阶层战争[-]：败者降级，胜者晋升。',
'aec_tp10_l4_text':'宿敌会随时间[44BBFF]迁移领地[-]，因此也可能出现在你的基地附近。',
'aec_tp10_l1_text':'击杀宿敌后，它会对你怀有[FF3333]仇恨[-]，并可能亲自追猎你。',
'aec_tp10_l2_text':'击杀AEC Boss后，与其相关的宿敌可能发誓向你[FF8800]复仇[-]。',
'aec_tph_text':'.....................',
'aec_dialog_next_tier':'下一难度等级',
'aec_dialog_prev_tier':'上一难度等级',
'dialog_trader_response_aec_resetquests':'[AA88CC]~ 重置任务显示／修复异常任务列表 ~[-]',
'aec_dialog_header':'AEC高等级契约 | T05至T15 | 全部兴趣点规模',
'aecTierUnlockLowGSLv11Desc':'已解锁T02 A1任务（需先完成原版二级任务并达到GS200）',
'aecTierUnlockLowGSLv16Desc':'已解锁T03 A1任务（需先完成原版三级任务并达到GS300）',
'aecTierUnlockLowGSLv21Desc':'已解锁T04 A1任务（需先完成原版四级任务并达到GS400）',
'aecTierUnlockLv1Desc':'已解锁T05 A1任务（需先完成原版五级任务并达到GS600）',
'aec_eo_tutorial_intro_01_obj_vanilla_zombies':'原版僵尸',
'aec_eo_tutorial_archive_desc':'第3章：制造AEC任务档案无线电。你可以用它重新制作已经读过的“野外指南”“教程”或“进度”书籍，最高可到当前已解锁章节。每本归档书消耗10张纸。',
'aec_eo_tutorial_intro_03_desc':'第4章：制造AEC变异样本兑换台，用于转换分级变异样本和特殊僵尸部件。',
'aec_eo_tutorial_intro_03_offer':'制造1台AEC变异样本兑换台。',
'aec_eo_tutorial_intro_05_desc':'第6章：制造AEC僵尸市场终端，以解锁扩展市场进度。',
'aec_eo_tutorial_intro_06_desc':'第7章：在AEC僵尸市场终端制造主加密货币矿机申请。',
'aec_eo_tutorial_intro_07_desc':'第8章：为加密货币矿机制造0级服务器电池申请。',
'aec_eo_tutorial_intro_08_desc':'第9章：制造无头者0★连接卡，以配置无头者硬币生产。',
'aec_eo_tutorial_intro_11_desc':'第12章：制造无头者0★被动部件采集器申请，实现僵尸部件自动生产。',
'aec_eo_tutorial_relay_desc':'第13章：制造AEC契约中继器。放置并启动后会召来一架无人机，每日提供更新后的AEC Boss与仆从任务。区块卸载时无人机会消失，但中继器可无限次重新召唤。',
'aec_eo_tutorial_relay_offer':'制造1台AEC契约中继器，以接取每日更新的AEC Boss与仆从任务。',
'aec_eo_tutorial_01_offer':'获得1枚AEC通用代币，以确认首项目标并启动教程。',
'aec_eo_tutorial_01_desc':'第1章：获得1枚AEC通用代币并启动教程链。完成早期商人任务以解锁AEC契约；契约奖励通用代币，有效的AEC击杀则会给予变异样本和感染者牙齿。',
'aec_eo_tutorial_02_offer':'解锁T05 AEC契约后积攒26枚AEC通用代币。完成原版商人的全部五个任务等级，并在技能页签查看具体要求。',
'aec_eo_tutorial_04_offer':'制造1台AEC极限改装工作台。',
'aec_eo_tutorial_04_desc':'制造1台AEC极限改装工作台，完成本阶段的终局进度目标。',
'aec_eo_tutorial_07_offer':'制造1个AEC威力变异器01。',
'aec_eo_tutorial_09_offer':'制造1个AEC威力变异器02。',
'aec_eo_tutorial_02_desc':'第2章：积攒26枚AEC通用代币，为战斗章节做好准备。完成原版商人的全部五个任务等级，解锁并完成T05 AEC契约来获得代币。',
'aec_eo_tutorial_03_offer':'消灭呆呆仆从及Boss、老兵僵尸和刽子手仆从。',
'aec_eo_tutorial_03_desc':'第16章：完成由AEC特殊目标组成的混合战斗试炼。消灭呆呆仆从及Boss、老兵僵尸和刽子手仆从。',
'aec_eo_tutorial_08_desc':'第21章：完成第二项战斗检查点。击杀T05僵尸和1★刽子手仆从。',
'aec_eo_tutorial_10_desc':'第23章：完成精英混合仆从试炼。击杀1★无头者、末日领主与女巫仆从，以及老兵僵尸。',
'aec_eo_tutorial_03_obj_tier05':'AEC T05僵尸',
'aec_eo_tutorial_08_obj_tier05':'AEC T05僵尸',
'aec_eo_tutorial_10_obj_tier05':'AEC T05僵尸',
'internationalMarketDesc':'僵尸市场现已向你开放。你可以向世界各地仍有幸存者的政府申请资源。不过，公爵币对他们毫无价值，运输公爵币也太费时间，因此必须使用本地加密货币付款。',
'quest_InternationalMarketDesc':'申请空投所需资源。清除附近僵尸后，空投会送到你的位置；可以同时申请多个空投。[DECEA3]切勿在受保护的基地内部启动空投。[-]',
'cryptoMinerDesc':'一种采矿设施，可消耗[FFFF00]服务器电池[-]，随时间被动生成[FFFF00]加密货币[-]。服务器电池可从[FFFF00]僵尸市场终端[-]购买；还可使用[FFFF00]增压器和显卡[-]进行升级。',
'blackMarketAECDesc':'用于获取顶级改装武器与物品；更多内容即将推出。',
'questItem_blackMarketDesc':'用于获取顶级改装武器与物品；更多内容即将推出。',
'PassiveOreMinerT102':'[FFD700]AEC[-] T1被动矿机\\n\\n已就绪\\n\\n领取已采掘的矿物。',
'PassiveOreMinerT202':'[FFD700]AEC[-] T2被动矿机\\n\\n已就绪\\n\\n领取已采掘的矿物。',
'PassiveOreMinerT302':'[FFD700]AEC[-] T3被动矿机\\n\\n已就绪\\n\\n领取已采掘的矿物。',
'PassiveOreMinerT402':'[FFD700]AEC[-] T4被动矿机\\n\\n已就绪\\n\\n领取已采掘的矿物。',
'PassiveOreMinerT502':'[FFD700]AEC[-] T5被动矿机\\n\\n已就绪\\n\\n领取已采掘的矿物。',
'aec_base_name':'[AEC] 基础任务','modAECMutatorEmpty':'[FFD700]AEC[-] [FFFFFF]空载变异器[-]',
'lblCategoryAECMeleeTargetArmor':'内部对象：目标护甲','lblCategoryAECMeleeStaminaLoss':'内部对象：耐力消耗','lblCategoryAECMeleeBlockDamage':'内部对象：方块伤害','lblCategoryAECMeleeBlockHarvest':'内部对象：方块采集','lblCategoryAECArmorStaminaRegen':'内部对象：耐力恢复','lblCategoryAECArmorStaminaMax':'内部对象：最大耐力','lblCategoryAECArmorHealthRegen':'内部对象：生命恢复','lblCategoryAECLegCrouchBoost':'内部对象：蹲行速度','lblCategoryAECLegWalkBoost':'内部对象：行走速度','lblCategoryAECLegRunBoost':'内部对象：奔跑速度','lblCategoryAECLegJumpBoost':'内部对象：跳跃加成',
'aec_info_stmt_text':'[FF4500]AEC终局契约——尚未解锁[-]','aec_intro_stmt_text':'[FF4500]AEC终局契约 | 已启用[-]','aec_tuto_p2_text':'[5599FF]AEC教程—2/10 | 通用契约货币[-]','aec_tuto_p3_text':'[5599FF]AEC教程—3/10 | 兴趣点规模[-]','aec_tuto_p4_text':'[5599FF]AEC教程—4/10 | 僵尸等级[-]','aec_tuto_p5_text':'[5599FF]AEC教程—5/10 | 危险系统[-]','aec_tuto_p6_text':'[5599FF]AEC教程—6/10 | 契约奖励[-]','aec_tuto_p7_text':'[5599FF]AEC教程—7/10 | AEC装备与掉落[-]','aec_tuto_p8_text':'[5599FF]AEC教程—8/10 | 变异样本[-]','aec_tuto_p9_text':'[5599FF]AEC教程—9/10 | 宿敌系统[-]','aec_tuto_p10_text':'[5599FF]AEC教程—10/10 | 宿敌战争与迁移[-]','aec_tp2_l1_text':'[FFD700]── 通用契约货币 ──[-]','aec_tp4_l2_text':'共11个等级：[00FF66]T05老兵[-] → [FFCC33]T10梦魇[-] → [FF3333]T15神话[-]','aec_tp6_l1_text':'[FFD700]── 契约奖励 ──[-]','aec_tp9_l2_text':'共6个宿敌阶级：[AAAAAA]低阶[-] | [44BBFF]高阶[-] | [44FF88]精英[-] | [FFCC33]冠军[-] | [FF8800]传奇[-] | [FF3333]世界级[-]',
'aec_dialog_trader_response_aecjobs_a1':'[AEC] 按兴趣点规模选择契约：A1小型','aec_dialog_trader_response_aecjobs_a2':'[AEC] 按兴趣点规模选择契约：A2中型','aec_dialog_trader_response_aecjobs_a3':'[AEC] 按兴趣点规模选择契约：A3大型','aec_dialog_trader_response_aecjobs_a4':'[AEC] 按兴趣点规模选择契约：A4巨型','aec_dialog_trader_response_aecjobs_a5':'[AEC] 按兴趣点规模选择契约：A5超大型','aec_dialog_tuto_next':'下一教程页','aec_dialog_tuto_prev':'上一教程页',
'aec_eo_tutorial_intro_01_offer':'击杀25只原版僵尸。','aec_eo_tutorial_intro_02_offer':'收集100份木材和5份锻铁。','aec_eo_tutorial_intro_04_desc':'制造25份无头者僵尸部件。','aec_eo_tutorial_intro_09_offer':'制造100枚无头者0★硬币。','aec_eo_tutorial_intro_09_desc':'安装无头者0★连接卡，并制造100枚无头者0★硬币。','aec_eo_tutorial_archive_offer':'制造1台AEC任务档案无线电。','aec_eo_tutorial_05_offer':'制造1个AEC空载变异器。','aec_eo_tutorial_05_desc':'制造1个AEC空载变异器，完成本阶段目标。','aec_eo_tutorial_06_offer':'击杀10个无头者仆从。','aec_eo_tutorial_06_desc':'击杀10个无头者仆从，完成本阶段目标。','aec_eo_tutorial_07_desc':'制造1个AEC威力变异器01，完成本阶段目标。','aec_eo_tutorial_08_offer':'击杀T05僵尸和1★刽子手仆从。','aec_eo_tutorial_10_offer':'击杀1★无头者、末日领主、女巫仆从以及老兵僵尸。','aec_eo_tutorial_12_desc':'制造1台AEC死灵锻炉工作台。',
'lblCategoryCryptoMining':'加密货币采矿','lblCategoryCryptoBuilding':'建筑方块','blackMarketAEC':'[FFD700]AEC[-] 黑市','questNameKey_AirdropRequest':'来自[DECEA3]僵尸市场终端[-]的空投申请',
'lblCategoryAECGunTargetArmor':'内部对象：目标护甲','lblCategoryAECToolTargetArmor':'内部对象：目标护甲','lblCategoryAECToolBlockDamage':'内部对象：方块伤害','lblCategoryAECToolStaminaLoss':'内部对象：耐力消耗','lblCategoryAECToolBlockHarvest':'内部对象：方块采集',
'lblCategoryAECBowTargetArmor':'内部对象：目标护甲','aec_eo_tutorial_intro_11_offer':'制造1份无头者0★被动部件采集器申请。',
'aecNecroforgeWorkbench':'[FFD700]AEC[-] 死灵锻炉工作台','aecNecroforgeWorkbenchDesc':'[FFD700]AEC[-] 死灵锻炉工作台',
'modAECMutatorMegaHarpoonBow':'[FFD700]AEC[-] 利维坦钩索','modAECMutatorAtypDirectRagdollMelee':'[FFD700]AEC[-] 破坏者重击','modAECMutatorUniqImpactGun':'[FFD700]AEC[-] 破坏者卡宾枪','modAECMutatorAtypValkyrieBlade':'[FFD700]AEC[-] 女武神刀刃',
'modAECMegaHarpoonBow':'[FFD700]AEC[-] 利维坦钩索','modAECAtypDirectRagdollMelee':'[FFD700]AEC[-] 破坏者重击','modAECUniqImpactGun':'[FFD700]AEC[-] 破坏者卡宾枪','modAECAtypValkyrieBlade':'[FFD700]AEC[-] 女武神刀刃',
'aecChallengeCategoryMinionsAll':'AEC仆从（全部等级）','aecChallengeGroupMinionHuntsAll':'仆从猎杀','aecQuestArchiveRadio':'[FFD700]AEC[-] 任务档案无线电',
'aec_eo_tutorial_intro_05_offer':'制造1台AEC僵尸市场终端。','aec_eo_tutorial_intro_06_offer':'制造1份综合加密货币矿机申请。','aec_eo_tutorial_intro_01_desc':'击杀25只原版僵尸，完成本阶段目标。','aec_eo_tutorial_intro_02_desc':'收集100份木材和5份锻铁，完成本阶段目标。','aec_eo_tutorial_intro_10_desc':'制造1份无头者0★研究论文。','aec_eo_tutorial_09_desc':'制造1个AEC威力变异器02，完成本阶段目标。',
'aec_eo_tutorial_intro_07_offer':'制造1份0级服务器电池申请。','aec_eo_tutorial_12_offer':'制造1台AEC死灵锻炉工作台。','internationalMarket':'[FFD700]AEC[-] 僵尸市场终端',
}


def translate_endgame_simple():
    path=ROOT/'04-AEC-ENDGAME_OVERHAUL/Config/Localization.csv';lines=path.read_text(encoding='utf-8-sig').splitlines(keepends=True);header=next(csv.reader([lines[0]]));c={x.lower():i for i,x in enumerate(header)};e,z=c['english'],c['schinese'];changed=0
    for i in range(1,len(lines)):
        line=lines[i];ending='\r\n' if line.endswith('\r\n') else '\n' if line.endswith('\n') else '';row=next(csv.reader([line.removesuffix(ending)]))
        if len(row)<=max(e,z) or not row[e].strip():continue
        key,english=row[0],row[e];chinese=ENDGAME_BY_KEY.get(key,ENDGAME_GUIDES.get(key))
        if chinese is None and row[e].strip()!=row[z].strip():continue
        if chinese is None:chinese=ENDGAME_EXACT.get(english)
        m=re.fullmatch(r'Eliminate (\d+) (.+) minions\.',english,re.I)
        if chinese is None and m:chinese=f"消灭{m.group(1)}个{m.group(2)}仆从。"
        m=re.fullmatch(r'(.+) Boss \(T00\)',english)
        if chinese is None and m:chinese=f"{m.group(1)}Boss（T00）"
        m=re.fullmatch(r'(.+?) (\d★) Hydro Cultivator Airdrop',english)
        if chinese is None and m:chinese=f"{m.group(1)}{m.group(2)}水培种植器空投"
        m=re.fullmatch(r'(?:\[FFD700\]AEC\[-] )?Passive Ore Miner T([1-5])\\n\\nMINING\\n\\n(?:Passively extracts every 30 minutes|Currently extracting ores from the mining pool)\.',english)
        if chinese is None and m:chinese=f"[FFD700]AEC[-] T{m.group(1)}被动矿机\\n\\n挖矿\\n\\n每30分钟从矿物池中被动采掘一次。"
        m=re.fullmatch(r'MINING\\n\\n(?:Passively generates|Currently mining) \[DECEA3\]Dukes Coins\[-\](?: over time)?\.\\n\\nUp to \[DECEA3\](\d+) coins every 30 minutes\[-\]\.',english)
        if chinese is None and m:chinese=f"挖矿\\n\\n被动生成[DECEA3]公爵币[-]。\\n\\n每30分钟最多生成[DECEA3]{m.group(1)}枚[-]。"
        if chinese is None:continue
        row[z]=chinese;out=io.StringIO();csv.writer(out,lineterminator=ending).writerow(row);lines[i]=out.getvalue();changed+=1
    with path.open('w',encoding='utf-8-sig',newline='') as h:h.write(''.join(lines))
    print(f"04-AEC-ENDGAME_OVERHAUL/Config/Localization.csv: updated {changed} UI/item entries")


translate_endgame_simple()


AIR_PHRASES={
 'Double Barrel Shotgun':'双管霰弹枪','Double Barrel':'双管霰弹枪','Lever Action Rifle':'杠杆式步枪',
 'Pipe Machine Gun':'管制机枪','Pipe Shotgun':'管制霰弹枪','Pipe Rifle':'管制步枪','Pipe Pistol':'管制手枪',
 'Hunting Rifle':'猎枪','Pump Shotgun':'泵动式霰弹枪','Auto Shotgun':'自动霰弹枪','Tactical AR':'战术突击步枪',
 'Desert Vulture':'沙漠秃鹫手枪','Primitive Bow':'原始弓','Wooden Bow':'木弓','Compound Bow':'复合弓',
 'Iron Crossbow':'铁弩','Compound Crossbow':'复合弩','Robotic Sledge':'机器人雪橇炮塔','Robotic Turret':'机器人炮塔',
 'Junk Drone':'无人机','Steel PickAxe':'钢镐','Iron PickAxe':'铁镐','Steel Shovel':'钢铲','Iron Shovel':'铁铲',
 'Steel Sledgehammer':'钢制大锤','Iron Sledgehammer':'铁制大锤','Stone Sledgehammer':'石制大锤',
 'Steel Knuckles':'钢指虎','Iron Knuckles':'铁指虎','Leather Knuckles':'皮革指虎','Steel Spear':'钢矛','Iron Spear':'铁矛',
 'Steel Club':'钢棒','Baseball Bat':'棒球棒','Stun Baton':'电击棍','Bone Knife':'骨刀','Hunting Knife':'猎刀',
 'Impact Driver':'冲击起子','Claw Hammer':'羊角锤','Solar Cell':'太阳能电池','Machine Gun Parts':'机枪零件',
 'Shotgun Parts':'霰弹枪零件','Rifle Parts':'步枪零件','Pistol Parts':'手枪零件','Blade Parts':'刀刃零件',
 'Motor Tool Parts':'动力工具零件','Armor Parts':'护甲零件','Tool Parts':'工具零件','Rocket Parts':'火箭筒零件',
 'Resource Potassium Nitrate Powder':'硝酸钾粉','Resource Oil Shale':'油页岩','Resource Broken Glass':'碎玻璃',
 'Resource Crushed Sand':'碎沙','Resource Scrap Lead':'铅废料','Resource Scrap Brass':'黄铜废料','Resource Scrap Iron':'铁废料',
 'Resource Mechanical Parts':'机械零件','Resource Electric Parts':'电气零件','Resource Concrete Mix':'混凝土拌合料',
 'Airdrop Request for':'空投申请：','Passive Ore Miner':'被动矿机','Passive cryptominer Dukes':'被动公爵币矿机',
}
AIR_WORDS={
 'Quality':'品质','Cnt':'容器','Door':'门','Green':'绿色','Wall':'墙','White':'白色','Blue':'蓝色','Red':'红色','Grey':'灰色','Gray':'灰色','Yellow':'黄色','Sign':'标牌','Top':'顶部','Orange':'橙色','Brown':'棕色','Full':'装满','Tent':'帐篷','Open':'开启','Pipe':'管道','Small':'小型','Metal':'金属','Curtain':'窗帘','Biohazard':'生化危险','Light':'灯','Helper':'组合项','Drapes':'帷幔','Resource':'资源','Double':'双层','Pallet':'托盘','Black':'黑色','Corner':'转角','Empty':'空','Closed':'关闭','Purple':'紫色','Half':'半块','Bottom':'底部','Right':'右侧','Left':'左侧','Box':'箱','Pink':'粉色','Iron':'铁制','Player':'玩家','Bag':'包','Garage':'车库','Random':'随机','Truck':'卡车','Single':'单个','House':'房屋','Commercial':'商用','Steel':'钢制','Miner':'采集器','Panel':'面板','Gun':'枪械','Wood':'木制','Tile':'瓷砖','Wide':'宽型','Store':'商店','Fence':'围栏','Plant':'植物','Base':'底座','Deco':'装饰','Mail':'邮件','Concrete':'混凝土','Cap':'端盖','Passive':'被动','Dirty':'脏污','Block':'方块','Frame':'框架','Outfit':'衣装','Gloves':'手套','Control':'控制','Car':'汽车','Bed':'床','Bandit':'土匪','Desk':'书桌','Safe':'保险箱','Parts':'零件','Part':'部件','Planted':'已种植','Shelf':'货架','Side':'侧面','Old':'老旧','Abandoned':'废弃','Armoire':'衣柜','Sedan':'轿车','Pile':'堆','Boots':'靴子','Blank':'空白','Large':'大型','Cabinet':'柜子','Wooden':'木制','Broken':'损坏','Chair':'椅子','Powered':'通电','Mod':'改装件','Army':'军用','Exterior':'外侧','Roof':'屋顶','Magazine':'杂志','Food':'食物','Trap':'陷阱','Blinds':'百叶窗','Vertical':'垂直','Tree':'树','Shop':'商店','Table':'桌子','Booth':'隔间','Offset':'偏移','Center':'中央','Modern':'现代','Schematic':'配方','Pickup':'皮卡','SUV':'SUV','Picture':'画框','Tool':'工具','Shipping':'货运','Couch':'沙发','Trash':'垃圾','Plate':'板','Trader':'商人','Destroyed':'损毁','Hanging':'悬挂','Variant':'变体','Round':'圆形','Decor':'装饰','Oak':'橡树','Container':'集装箱','Tall':'高型','School':'学校','Rack':'架子','Rail':'栏杆','Potted':'盆栽','Chainlink':'铁丝网','Barrel':'桶','Damage':'受损','Middle':'中段','Camping':'露营','Tarp':'篷布','Porta':'移动式','Potty':'厕所','Fast':'快速','Clothes':'衣物','POI':'兴趣点','Can':'罐头','Interior':'内侧','Valve':'阀门','seed':'种子','Seed':'种子','Shotgun':'霰弹枪','Fire':'火焰','Pole':'立柱','Seat':'座椅','Lamp':'灯','Awning':'遮阳棚','Book':'书籍','Office':'办公室','Casket':'棺材','Gas':'燃气','Cart':'推车','Road':'道路','Insecure':'未上锁','Glass':'玻璃','Thin':'窄型','Military':'军用','Bunk':'双层床','Roll':'卷帘','Flag':'旗帜','Med':'中型','Centered':'居中','Basket':'篮筐','Helmet':'头盔','Corn':'玉米','Loose':'松散','Stone':'石制','Hat':'帽子','Harvest':'收获','Aloe':'芦荟','Doors':'门','Hazard':'危险','File':'文件','Folding':'折叠','Short':'矮型','End':'末端','Poster':'海报','Lit':'亮灯','Ugly':'破旧','Hung':'悬挂','Bricks':'砖块','Armor':'护甲','Bin':'垃圾桶','Shopping':'购物','Semi':'半挂式','Jail':'监狱','Rifle':'步枪','Electronics':'电子设备','Flat':'平放','Fallen':'倒下','Siding':'墙板','Log':'原木','Ceiling':'天花板','Outside':'外部','Church':'教堂','Enforcer':'执行者','Front':'前部','Down':'向下','Support':'支撑','Plastic':'塑料','Corpse':'尸体','Primitive':'原始','ammo':'弹药','Ammo':'弹药','Water':'水','Diagonal':'斜向','Crushed':'压毁','Switch':'开关','Corrugated':'波纹','Bow':'弓','Shoes':'鞋','Nerd':'书呆子','Back':'后部','Sheet':'板材','Theater':'剧院','Pew':'长椅','Pine':'松树','Inside':'内部','Loot':'战利品','Freezer':'冷柜','Mailbox':'邮箱','Yucca':'丝兰','Air':'空气','Cars':'汽车','Modular':'模块化','Messy':'凌乱','Made':'制成','Closet':'壁橱','Apartment':'公寓','Crate':'板条箱','Burnt':'烧毁','Pistol':'手枪','Machine':'机器','Commando':'突击队','Minivan':'小型厢式车','Straight':'直型','Utility':'工具','x':'×','V':'竖型','Q':'品质',
}
AIR_PHRASES.update({
 'Batter Up':'击球手','Art of Mining':'矿工艺术','Spear Hunter':'长矛猎手','Bar Brawling':'酒吧斗殴',
 'Tech Junkie':'科技迷','Urban Combat':'城市战斗','Automatic Weapons Handbook':'自动武器手册',
 'Shotgun Messiah':'霰弹救世主','The Hunters Journal':'猎人日志','The Hunter':'猎手',
 'Raw Meat':'生肉','Radiated Mushrooms':'辐射蘑菇','Blueberry Pie':'蓝莓派','Pumpkin Bread':'南瓜面包',
 'Shepards Pie':'牧羊人派','Hobo Stew':'流浪汉炖菜','Steak and Potato Meal':'牛排土豆餐',
 'Ear of Super Corn':'超级玉米穗','Ear of Corn':'玉米穗','GoldenRod Tea':'一枝黄花茶',
 'Southern Farming':'南方农耕','Tactical Warfare':'战术作战','Passive Part Miner':'被动部件采集器',
 'Hydro Cultivator':'水培种植器','Robotic Drone Medic':'无人机医疗改装件','Vehicle Fuel Saver':'载具节油改装件',
 'Vehicle Super Charger':'载具机械增压器','Barbed Wire':'铁丝网','Grave Digger':'掘墓者改装件',
 'Weighted Head':'配重头改装件','Treasure Hunters Mod':'宝藏猎人改装件','Flashlight':'手电筒',
 'French Door':'法式门','Parking Meter':'停车计时器','I Beam':'工字梁','Cash Register':'收银机',
 'Manhole Hatch':'下水道舱盖','Microwave Oven':'微波炉','Rain Cover':'防雨罩','Cherry Blossom':'樱花',
 'Wild West':'西部风格','Savage Country':'野蛮国度','Mo Power':'莫尔电力','Super Corn':'超级玉米',
})
AIR_PHRASES.update({'Research Papers':'研究论文','Generic Coins':'通用硬币','Generic Coin':'通用硬币','Minion Parts':'仆从部件','Zombie Parts':'僵尸部件'})
AIR_PHRASES.update({'Electricdemon Boss':'电魔Boss','Explosiveeagle Boss':'爆炸鹰Boss','Hammerguardian Boss':'巨锤守卫Boss','Partybeach Boss':'海滩派对Boss','Sirenhead Boss':'塞壬头部Boss','Hammer Connection Card':'巨锤连接卡','Witch Connection Card':'女巫连接卡'})
AIR_WORDS.update({'Generic':'通用','Coin':'硬币','Coins':'硬币','Minion':'仆从','Zombie':'僵尸','Parts':'部件','Demolishman':'爆破者','DemolishmanBoss':'爆破者Boss','Runner':'疾行者','RunnerBoss':'疾行者Boss','Angel':'天使','AngelBoss':'天使Boss','Guardian':'守卫','Kamikaze':'神风','ElectricdemonBoss':'电魔Boss','ExplosiveeagleBoss':'爆炸鹰Boss','HammerguardianBoss':'巨锤守卫Boss','PartybeachBoss':'海滩派对Boss','SirenheadBoss':'塞壬头部Boss'})
AIR_WORDS.update({'Airdrop':'空投','Dukes':'公爵币','Zombies':'僵尸','Tier':'等级','Connection':'连接','Card':'卡'})
AIR_WORDS.update({
 'm':'米','Up':'竖起','of':'之','No':'无','Bent':'弯曲','Lantern':'提灯','Hood':'兜帽','Lumberjack':'伐木工','Preacher':'传教士','Rogue':'盗贼','Athletic':'运动员','Farmer':'农民','Biker':'骑手','Ranger':'游骑兵','Scavenger':'拾荒者','Raider':'掠夺者','Nomad':'游牧者','Assassin':'刺客','Damaged':'受损','Ramp':'斜坡','Restaurant':'餐厅','Produce':'农产品','Melons':'甜瓜','Drawer':'抽屉','Mortician':'殡葬师','Folded':'已折叠','Ribbed':'带肋','Laundry':'衣物','Footlocker':'储物箱','Chest':'箱子','Mushroom':'蘑菇','Wrought':'锻造','Vent':'通风口','Battery':'电池','Mount':'支架','Logo':'标志','Cooler':'冷藏柜','Umbrella':'遮阳伞','Fern':'蕨类','Hidden':'隐藏','Painting':'画作','Gadsden':'加兹登','Spikes':'尖刺','Coffee':'咖啡','Duct':'风管','Railing':'栏杆','Stack':'堆叠','Shirts':'衬衫','Plain':'普通','Secure':'上锁','Bar':'酒吧','Potato':'土豆','Sml':'小型','Catwalk':'检修走道','Wedge':'楔形','Cntshelf':'货架容器','Supply':'补给','Rolling':'滚轮式','Window':'窗户','Tipped':'倾倒','Tv':'电视','Standing':'立式','Display':'展示','Case':'柜','Angular':'棱角型','Dome':'圆顶','Blocks':'方块','Guns':'枪械','Pumpkin':'南瓜','Pillar':'立柱','Flipped':'翻倒','Guard':'护栏','Bend':'弯曲','Incline':'倾斜','Cntbasket':'篮筐容器','Crop':'作物','Beans':'豆','Candy':'糖果','Quad':'四联','Pocket':'口袋','Vehicle':'载具','Fuel':'燃油','Saver':'节油器','Meat':'肉','Only':'专用','Boards':'木板','Snack':'零食','Skull':'碎颅者','Crusher':'粉碎者','Filler':'填充件','Industrial':'工业型','Heat':'热气','Goods':'货品','Arm':'扶手','Sports':'运动','Cursedgrave':'诅咒墓碑','Tank':'油箱','Motor':'动力','Dead':'枯死','Hop':'啤酒花','Misc':'杂项','Quarantine':'隔离','Tape':'胶带','Shelves':'货架','Hatch':'舱盖','Ver':'版本','Medic':'医疗','Bridge':'桥接件','Lockers':'储物柜','Mix':'混合','Link':'连接','Elevator':'电梯','Offest':'偏移','Oven':'烤箱','Smoothie':'冰沙','Juice':'果汁','Melee':'近战','Club':'棍棒','Burning':'燃烧','Shaft':'杆','Radiated':'辐射','Excavator':'挖掘机','Claw':'抓斗','Power':'电力','Bathroom':'卫生间','Men':'男用','West':'西','Pants':'裤子','Range':'靶场','Target':'靶标','Beverages':'饮料','Standalone':'独立式','Leaning':'斜靠','Coffin':'棺材','Lid':'盖子','Bedroll':'睡袋','Wine':'葡萄酒','Bottle':'瓶','Rock':'岩石','Sandbag':'沙袋','Camo':'迷彩','Hydro':'水培','Cultivator':'种植器','Rocket':'火箭弹','Frag':'破片','Magnum':'马格南','Sniper':'狙击','Tube':'管式弹仓','Extender':'扩容器','Super':'超级','Charger':'增压器','Wheel':'车轮','Cotton':'棉花','Crumpled':'撞毁','Service':'服务','Joint':'接头','Bench':'长椅','Counter':'柜台','Mini':'迷你','Under':'下方','mm':'毫米','Barbed':'带刺','and':'与','Blueberry':'蓝莓','Stew':'炖菜','Goldenrod':'一枝黄花','Grass':'草','Mountain':'山地','Forklift':'叉车','Business':'商用','Broke':'断裂','Beam':'梁','Test':'测试','Groceries':'杂货','Stand':'支架','Newspaper':'报纸','Dispenser':'售卖机','Unfolded':'展开','Plug':'封堵件','Bulletproof':'防弹','Trailer':'拖车','Rain':'防雨','Cover':'罩','Gooseneck':'鹅颈式','Dishwasher':'洗碗机','Rubbish':'垃圾','Grandpa':'爷爷','Mushrooms':'蘑菇','Wire':'电线','Hunters':'猎人','Weapons':'武器','Chrysanthemum':'菊花','Sides':'侧','Rubble':'瓦砾','Conveyor':'传送带','Factory':'工厂','Wild':'西部','Cherry':'樱花','Blossom':'花','Ember':'余烬','Fireplace':'壁炉','Logs':'原木','Rope':'绳索','Tied':'系紧','Canvas':'画布','player':'玩家','Over':'翻倒','Conduit':'线管','Robotic':'机器人','Drone':'无人机','Grave':'坟墓','Head':'头部','Journal':'日志','Tech':'科技','Combat':'战斗','Sham':'肉罐头','Medium':'中型','Winter':'冬季','Flatbed':'平板拖车','Letter':'字母','Speed':'限速','Rectangle':'矩形','Vending':'自动售货','Snacks':'零食','Sectional':'组合式','Leather':'皮革','Shutters':'百叶窗','Pantry':'食品柜','Partial':'残缺','Generic':'通用','Platform':'平台','Threeway':'三向','Rollup':'卷起','Ear':'穗','Knife':'刀','Automatic':'自动','Hunter':'猎手','Messiah':'救世主','Pie':'派','Bus':'公交车','City':'城市','Fake':'伪装','Blood':'血迹','Post':'邮局','Meter':'计时器','Propane':'丙烷','Barrier':'路障','Register':'收银机','Hardware':'五金','Rags':'破布','Body':'尸体','Flies':'苍蝇','A':'A型','B':'B型','v':'竖型',
})

AIR_WORDS.update({'Minions':'仆从','Universal':'通用','Token':'代币','Tokens':'代币','Research':'研究','Mechanician':'机械师','Hellskyli':'赫尔斯凯利','Mykir':'迈基尔','Party':'派对','Beach':'海滩','Running':'疾跑','Kamikaze':'神风','Explosive':'爆炸','Eagle':'鹰','Electric':'电击','Demon':'恶魔','Siren':'塞壬'})

AIR_PHRASES.update({
 'The Firemans Almanac':'消防员年鉴','Night Stalker':'夜行者','Lucky Looter':'幸运掠夺者','Wasteland Treasures':'废土宝藏','The Great Heist':'惊天大劫案','Rangers Guide to Archery':'游骑兵箭术指南','Pistol Pete':'手枪皮特','Home Cooking Weekly':'家庭烹饪周刊','Medical Journal':'医疗期刊','Forge Ahead':'锻造前进','Electrical Traps':'电气陷阱','First Aid Kit':'急救包','Night Vision':'夜视','Arrow Rest':'箭台','Diamond Blade Tip':'钻石刀刃尖端','Fore Grip':'前握把','Retracting Stock':'伸缩枪托','Rocket Launcher':'火箭发射器','Sawed-Off Shotgun':'短管霰弹枪','Boiled Egg':'水煮蛋','Grilled Meat':'烤肉','Chicken Ration':'鸡肉口粮','Iron Bars':'铁栏杆','Chemistry Station':'化学工作站','Human Skull':'人类头骨','Cow Feed':'牛饲料','Street Light':'路灯','Mounted Sink':'台盆','Passive Zombie Part Miner':'被动僵尸部件采集器',
})
AIR_WORDS.update({
 'Parking':'停车','The':'','CTRPlate':'中央板','Stool':'凳子','Bars':'栏杆','Dark':'深色','Collapsed':'坍塌','Chemistry':'化学','Station':'工作站','Gore':'血肉','Human':'人类','Server':'服务器','Cntwall':'墙柜容器','Hollow':'中空','Taupe':'灰褐色','Quest':'任务','Infested':'感染区','Firemans':'消防员','Axe':'斧','Arrow':'箭矢','Rest':'托架','Night':'夜间','Vision':'视觉','Robotics':'机器人技术','Batons':'电击棍','Dog':'狗粮','Egg':'鸡蛋','Rotated':'旋转','Movie':'电影','Sexual':'暧昧','Tension':'张力','Grocery':'杂货店','Pharmacy':'药店','Heater':'热水器','Long':'长型','Crooked':'歪斜','Wheelchair':'轮椅','Arms':'扶手','Sofa':'长沙发','Cow':'牛','Feed':'饲料','Tire':'轮胎','Abstract':'抽象','Falling':'倾倒','Exit':'出口','High':'高位','LEDOffset':'LED偏移','Bulb':'灯泡','Socket':'灯座','Luggage':'行李','Dumpster':'大型垃圾箱','Cntbin':'垃圾桶容器','Cupboard':'橱柜','Kit':'套件','Scope':'瞄准镜','Breaker':'破坏者','Tip':'尖端','Almanac':'年鉴','Stalker':'潜行者','Lucky':'幸运','Looter':'掠夺者','Wasteland':'废土','Treasures':'宝藏','Great':'惊天','Heist':'大劫案','Rangers':'游骑兵','Guide':'指南','to':'至','Archery':'箭术','Pete':'皮特','Cooking':'烹饪','Medicine':'医疗','Big':'大型','Fish':'鱼肉','Tacos':'塔可','Grace':'超级','Snow':'雪地','Shrub':'灌木','Backhoe':'反铲挖掘机','Master':'主控','Sale':'出售','Sold':'已售','Hoist':'起重机','Floor':'地面','Bleachers':'看台','Section':'分段','Gate':'大门','Fertilizer':'肥料','Sand':'沙','Flour':'面粉','Lift':'举升机','Hydraulic':'液压','Acid':'酸液','Spike':'尖刺','Spotlight':'探照灯','Street':'街道','Classic':'经典','Stove':'炉灶','Stall':'隔间','Sink':'水槽','Witch':'女巫','Doomlord':'末日领主','Mecha':'机甲','Dumdum':'呆呆','Hammer':'巨锤','Ghost':'幽灵','Singerie':'辛格里','Executioner':'刽子手','Druid':'德鲁伊','Sheriff':'警长','Archer':'弓箭手','Headless':'无头者','Nailgun':'射钉枪','FireAxe':'消防斧','SteelAxe':'钢斧','Chainsaw':'链锯','Auger':'螺旋钻','Cloth':'布料','Wrench':'扳手','Ratchet':'棘轮扳手','Machete':'砍刀','Baton':'警棍','Launcher':'发射器','Trigger':'扳机组件','Off':'短管','Plating':'镀层','Chain':'链条','Blade':'刀刃','Forge':'锻造','Traps':'陷阱','Weekly':'周刊','Explosives':'爆炸物','Goggles':'护目镜','Headhear':'头巾','Tuna':'金枪鱼','Chili':'辣椒','Ration':'口粮','Boiled':'水煮','Grilled':'烤制','Cactus':'仙人掌','Maple':'枫树','Spindle':'卷筒','Dmg':'受损','Notice':'告示','Board':'板','Bell':'钟','Cement':'水泥','Stairs':'楼梯','Spiral':'螺旋','Free':'独立','Pharma':'药品','Bollard':'防撞柱','Screen':'屏风','Divider':'隔断','Ball':'球','Hoop':'篮圈','Nightstand':'床头柜','Vault':'金库','Coal':'煤','Ore':'矿石','Boulder':'巨石','Oil':'油','Porch':'门廊','Washer':'洗衣机','Fridge':'冰箱','Stainless':'不锈','Faucet':'水龙头','Writable':'可书写','Hero':'英雄','Elixir':'药剂','Fruit':'果实','Blueberries':'蓝莓','Antibiotics':'抗生素','Nugget':'块','Gold':'金','Bullet':'子弹','Forged':'锻造','Rockbreaker':'碎岩者','arrows':'箭矢','bolts':'弩箭','Grip':'握把','Stock':'枪托','Electrical':'电气','Repair':'维修','Knuckles':'指虎','Sledgehammer':'大锤','Spear':'长矛','Chassis':'底盘','Gyrocopter':'旋翼机','Bread':'面包','Desert':'沙漠','Hole':'破洞','Cracked':'破裂','Police':'警用','Mine':'地雷','Filter':'过滤器','Scrap':'废料','Shamway':'沙姆威','Lights':'灯光','Radiator':'散热器','Chimney':'烟囱',
})

AIR_PHRASES.update({
 'Working Stiff Tools':'硬汉工具','Four Sided':'四面式','Picked Lock Bonus':'撬锁奖励','First Aid Bandage':'急救绷带','Murky Water':'浑浊水','Rotting Flesh':'腐肉','Clay Lump':'黏土块','Yucca Fibers':'丝兰纤维','Laser Sight':'激光瞄准器','Advanced Muffled Connectors':'高级消音连接件','Customized Fittings':'定制配件','Mega Crush':'超级碾压','Cross Walk':'人行横道','Cold Beer':'冰啤酒','Bird Bath Planter':'鸟浴盆花槽','Motion Sensor':'运动传感器',
})
AIR_WORDS.update({
 'Courtland':'科特兰','Tran':'交通牌','Trussing':'桁架','SCTR':'侧中','Paper':'纸制','Escalator':'自动扶梯','Monitor':'显示器','Mannequin':'人体模特','Plaid':'格纹','Button':'按钮','Tenth':'十分之一','Dew':'露水','Collector':'收集器','Boxes':'箱子','Candle':'蜡烛','Silver':'银色','Pendant':'吊灯','Brass':'黄铜','Computer':'电脑','Grill':'烤架','Charcoal':'木炭','Buried':'埋藏','Stash':'储藏物','Storage':'储物','Munitions':'军火','Thick':'厚型','Murky':'浑浊','Cornmeal':'玉米粉','Rotting':'腐烂','Flesh':'肉','Bandage':'绷带','Casing':'弹壳','Clay':'黏土','Lump':'块','Fibers':'纤维','Forest':'森林','Ground':'地面','Clubs':'棍棒','Spears':'长矛','Sight':'瞄准器','Laser':'激光','Muffled':'消音','Connectors':'连接件','Triple':'三联','Fittings':'配件','Customized':'定制','Cat':'猫粮','Snowberry':'雪果','Biome':'生物群系','Flower':'花','Lrg':'大型','Hedge':'树篱','Leaf':'树叶','Text':'文字','Tractor':'拖拉机','Working':'硬汉','Stiff':'工具','Lock':'锁','Bonus':'奖励','Sided':'面式','Cube':'立方体','Work':'作业','Safety':'安全','Protection':'防护','Breakers':'破解者','Health':'健康','Hackers':'黑客','Fort':'堡垒','Bites':'口粮','Eye':'眼睛','Atom':'原子','Mega':'超级','Crush':'碾压','Ad':'广告','Cross':'交叉','Walk':'通道','Beer':'啤酒','Neon':'霓虹','Cold':'冰镇','CTRQuarter':'中央四分之一','Rusty':'生锈','Bookcase':'书柜','Jeans':'牛仔裤','Laptop':'笔记本电脑','Keyboard':'键盘','Lettuce':'生菜','Gourds':'葫芦','Apples':'苹果','Aqua':'水蓝色','Tan':'棕褐色','Set':'套组','Swing':'秋千','Reinforced':'加固','Draw':'吊','Sliding':'滑动','Carton':'纸箱','Covered':'覆盖','Wagon':'马车','Bath':'浴盆','Bird':'鸟','Planter':'花槽','Animal':'动物','Bumper':'保险杠','Duke':'公爵','Retro':'复古','Fam':'家庭照','Saging':'下垂','Fluorescent':'荧光','Bay':'灯槽','Recessed':'嵌入式','Microphone':'麦克风','Beverage':'饮料','Motionsensor':'运动传感器','Domed':'圆顶式','Garbage':'垃圾','Fancy':'精致','Hardened':'强化','Bucket':'桶','Hops':'啤酒花','turret':'炮塔','Regular':'普通',
})

AIR_PHRASES.update({
 'Paint Brush':'油漆刷','Polymer String':'聚合物弓弦','Vehicle Adventures':'载具历险','Wiring 101':'布线入门','Armored Up':'整装待发','Tech Planet':'科技星球','Rifle World':'步枪世界','Shotgun Weekly':'霰弹枪周刊','Handgun Magazine':'手枪杂志','Bow Hunters':'弓猎人','Sharp Sticks':'尖锐长矛','Get Hammered':'抡起大锤','Big Hutters':'重击手','Knife Guy':'刀客','Furious Fists':'狂怒铁拳','Scrapping 4 Fun':'快乐拆解','Handy Land':'维修天地','Tuna Fish Gravy Toast':'金枪鱼肉汁吐司','Pumpkin Cheesecake':'南瓜芝士蛋糕','Sham Chowder':'沙姆浓汤','Gumbo Stew':'秋葵炖菜','Vegetable Stew':'蔬菜炖菜','Charred Meat':'焦肉','Old Sham Sandwich':'旧沙姆三明治','Baked Potato':'烤土豆','Do Not Enter':'禁止进入','Surveilled Area':'监控区域','Wanted Missing':'寻人启事','National Park':'国家公园','Construction Cone':'施工路锥','Long Sleeve':'长袖','Soda Can':'汽水罐','Hub Caps':'轮毂盖',
})
AIR_WORDS.update({
 'Paint':'油漆','Brush':'刷','Polymer':'聚合物','String':'弓弦','Motorcycle':'摩托车','Minibike':'迷你摩托车','Bicycle':'自行车','Adventures':'历险','Vehicles':'载具','Workstations':'工作站','Wiring':'布线','Farming':'耕作','Seeds':'种子','Recipes':'配方','Armored':'装甲','Planet':'星球','World':'世界','Rifles':'步枪','Shotguns':'霰弹枪','Handgun':'手枪','Pistols':'手枪','Bows':'弓','Sharp':'尖锐','Sticks':'长矛','Get':'抡起','Hammered':'大锤','Sledgehammers':'大锤','Hutters':'重击手','Guy':'刀客','Knives':'刀具','Furious':'狂怒','Fists':'铁拳','Fist':'拳击','Scrapping':'拆解','Fun':'乐趣','Salvage':'拆解','Handy':'维修','Land':'天地','Handlebars':'车把','Pears':'梨','Peas':'豌豆','Soup':'汤','Miso':'味噌','Salmon':'鲑鱼','Pasta':'意面','Lamb':'羊肉','Rations':'口粮','Beef':'牛肉','Spaghetti':'意大利面','Gravy':'肉汁','Toast':'吐司','Cheesecake':'芝士蛋糕','Chowder':'浓汤','Gumbo':'秋葵','Vegetable':'蔬菜','Charred':'焦制','Sandwich':'三明治','Baked':'烘烤','Plantedtree':'已种植树','Farm':'农用','Pot':'锅','Quarter':'四分之一','Triangle':'三角形','Warning':'警告','Area':'区域','Surveilled':'监控','Private':'私人领地','Do':'禁止','Not':'','Enter':'进入','Diners':'餐馆','Menu':'菜单','Wanted':'通缉','Missing':'失踪','Rekt':'雷克特','way':'向','Stop':'停车','Park':'公园','Apache':'阿帕奇','Ctr':'中央','Construction':'施工','Cone':'路锥','Ladder':'梯子','Liquor':'酒类','TShirts':'T恤','Sweater':'毛衣','Sleeve':'袖','Speaker':'音箱','PC':'电脑','Pristine':'完好','Pair':'一对','Mixed':'混合','Mattress':'床垫','Curled':'卷起','Soda':'汽水','Pack':'包装','Barricade':'路障','Opaque':'不透明','Cellar':'地窖','Workbench':'工作台','Cinder':'煤渣','Horseshoe':'马蹄铁','Trough':'食槽','Pig':'猪','Driftwood':'浮木','Hub':'轮毂','Caps':'盖',
})

AIR_PHRASES.update({
 'Fergit Elixir':'遗忘药剂','Learning Elixir':'学习药剂','Axesome Sauce':'超棒酱汁','Grandpas Moonshine':'爷爷的私酿酒','BlackStrap Coffee':'黑带咖啡','Pure Mineral Water':'纯净矿泉水','Nerd Tats':'书呆子眼镜','Sugar Butts':'糖屁股糖果','SkullCrushers':'碎颅糖果','Rock Busters':'碎岩糖果','Oh Shitz Drops':'噢糟糕糖果','Atom Junkies':'原子迷糖果','Atomic Smoothie':'原子冰沙','Frostbite Smoothie':'冻伤冰沙','Oasis Smoothie':'绿洲冰沙','Jar of Honey':'一罐蜂蜜','Herbal Antibiotics':'草药抗生素','Aloe Cream':'芦荟膏','Blood bag':'血袋','Mushroom spores':'蘑菇孢子','Blue spruce seed':'蓝云杉种子','Raw Diamond':'原钻','Gun Powder':'火药','Contact Grenades':'触发式手雷','Muzzle Brake':'枪口制退器','Suppressor Silencer':'消音器','Cripple Em':'致残改装件','Shotgun Choke':'霰弹枪收束器','Shotgun Duckbill':'霰弹枪鸭嘴器','Reflex Sight':'反射式瞄准镜','Morale Booster':'士气增强器','Wood Splitter':'伐木改装件','Bunker Buster':'地堡克星改装件','Fortifying Grip':'强化握把','Ergonomic Grip':'人体工学握把','Structural Base':'结构加固改装件','Serrated Blade':'锯齿刀刃','Tempered Blade':'淬火刀刃','Rad Remover':'辐射消除器','Water Purifier':'净水器','Improved Fittings':'改良配件','Reserve Fuel Tank':'备用油箱',
})
AIR_WORDS.update({
 'Framed':'装裱','Medals':'勋章','Torch':'火把','Projector':'投影仪','Fan':'风扇','Mounted':'壁挂','Extra':'超小型','Mouse':'鼠标','Dryer':'烘干机','Cntgas':'燃气灶容器','Satellite':'卫星','Unit':'设备','Tilt':'倾卸式','decor':'装饰','Mirror':'镜子','Toilet':'马桶','Tub':'浴缸','Shower':'淋浴','Handle':'把手','Chem':'化学品','Furniture':'家具','Fergit':'遗忘','Learning':'学习','Axesome':'超棒','Sauce':'酱汁','Moonshine':'私酿酒','BlackStrap':'黑带','Tea':'茶','Pure':'纯净','Mineral':'矿泉','Tats':'眼镜','Sugar':'糖','Butts':'屁股','SkullCrushers':'碎颅糖果','Busters':'碎岩糖果','Oh':'噢','Shitz':'糟糕','Drops':'糖果','Junkies':'迷','Atomic':'原子','Frostbite':'冻伤','Oasis':'绿洲','Recog':'认知糖果','Steroids':'类固醇','Jar':'一罐','Honey':'蜂蜜','Herbal':'草药','Painkillers':'止痛药','Cream':'膏','Vitamins':'维生素','bag':'袋','corn':'玉米','spores':'孢子','spruce':'云杉','Group':'组件','Pick':'撬锁器','Cash':'现金','Raw':'原始','Diamond':'钻石','Nail':'钉子','Buckshot':'铅弹丸','Powder':'火药','Feather':'羽毛','Glue':'胶水','Spring':'弹簧','Cobblestones':'鹅卵石','Fat':'脂肪','Bone':'骨头','Polymers':'聚合物','Flaming':'燃烧','Exploding':'爆炸','Grenades':'手雷','Muzzle':'枪口','Brake':'制退器','Suppressor':'抑制器','Silencer':'消音器','Cripple':'致残','Em':'目标','Choke':'收束器','Duckbill':'鸭嘴器','Reflex':'反射式','Bipod':'两脚架','Burst':'三连发','Headlamp':'头灯','Cargo':'货舱','Morale':'士气','Booster':'增强器','Splitter':'劈木器','Bunker':'地堡','Buster':'克星','Fortifying':'强化','Ergonomic':'人体工学','Structural':'结构','Serrated':'锯齿','Tempered':'淬火','Rad':'辐射','Remover':'消除器','Purifier':'净化器','Improved':'改良','Bandolier':'弹药带','Plow':'推土铲','Reserve':'备用',
})

AIR_PHRASES.update({
 'Expanded Seating':'扩展座椅','Off Road Headlights':'越野车灯','Mammas Justice':'妈妈的正义','Lone Wolf':'独狼','Pass N Gas':'加油站','Ansel Adams River':'安塞尔·亚当斯河','Spillway Lake':'泄洪湖','Gas Pump':'加油泵','Rug Bear':'熊皮地毯','News Dispenser':'报纸售卖机','Pool Table':'台球桌','Compressed Cardboard':'压缩纸板','Hay Bale':'干草捆','Coffee Maker':'咖啡机','Pressure Plate':'压力板','Battery Bank':'电池组','Solar Bank':'太阳能电池组','Generator Bank':'发电机组','Air Condition':'空调','Push Button':'按钮','Wall Clock':'挂钟','X-Ray':'X光',
})
AIR_WORDS.update({
 'Expanded':'扩展','Seating':'座椅','Headlights':'车灯','Accessories':'配件','Snowy':'积雪','Stump':'树桩','Ever':'常青','Dry':'枯干','Plains':'平原','Ivy':'常春藤','Wheels':'车轮','Shuttle':'接驳车','Alarm':'警报','Unlocked':'已解锁','Rear':'后部','Hubcap':'轮毂盖','CTRSheet':'中央板材','Women':'女用','Unisex':'通用卫生间','Mammas':'妈妈的','Justice':'正义','Lone':'孤独','Wolf':'狼','Smile':'微笑','Bulletin':'公告','Oops':'哎呀','Nachos':'玉米片','Ranch':'牧场味','Pills':'药店','Hugh':'休','Joel':'乔尔','Jen':'珍','Pass':'通行','NGas':'加油站','Smoke':'烟雾','Slow':'慢行','River':'河流','Lake':'湖泊','Destinations':'目的地','Coronado':'科罗纳多','south':'南向','north':'北向','west':'西向','Hydrant':'消防栓','Rivet':'铆钉','Foundation':'地基','CNRTrap':'转角陷阱','Pump':'泵','Teal':'蓝绿色','Headphones':'耳机','Bear':'熊','Female':'女性','Male':'男性','Energy':'能量饮料','News':'报纸','Dispense':'售卖机','Trophy':'奖杯','Pool':'台球','Underneath':'下方','Plywood':'胶合板','Osb':'定向刨花板','Miniblind':'迷你百叶窗','Curved':'弧形','Stained':'彩绘','Mixer':'搅拌机','Campfire':'篝火','Cans':'罐子','Cardboard':'纸板','Compressed':'压缩','Tiled':'平铺','Hay':'干草','Bale':'捆','Birdnest':'鸟巢','Trunk':'后备箱','Fender':'翼子板','Upright':'直立','Blueprint':'蓝图','Calendar':'日历','Hydroponic':'水培','XRay':'X光','Traffic':'交通','Candelabra':'烛台','Chandelier':'吊灯','Sconce':'壁灯','With':'带','Clock':'时钟','Maker':'机','Turret':'炮塔','Pressureplate':'压力板','Batterybank':'电池组','Solarbank':'太阳能电池组','Generatorbank':'发电机组','Key':'钥匙','Push':'按压','Angle':'弯角','Disconnect':'断路器','Condition':'调节','Transformer':'变压器','Backpack':'背包','Janitor':'清洁工','Brute':'大型','Style':'式',
})

AIR_PHRASES.update({
 'Black Market':'黑市','Crypto Miner':'加密货币矿机','Pill Case':'药盒','Drinking Fountain':'饮水机','Treasure Chest':'宝箱','Working Stiffs':'硬汉工具','Lab Equipment':'实验室设备','Construction Supplies':'建筑补给','Plaster Cast':'石膏固定带','Armor Crafting Kit':'护甲制造套件','Sewing Kit':'缝纫工具包','Timed Charges':'定时炸药','Molotov Cocktails':'燃烧瓶','Drum Magazine':'弹鼓','Cooling Mesh':'冷却网','Insulated Liner':'隔热内衬','Dynamic Grate':'活动格栅','Restricted Area':'限制区域','Beware Of Dog':'当心恶犬','Authorized Personnel':'仅限授权人员','Labor Day':'劳动节','Tow Away':'违停拖走','Handicap Parking':'无障碍停车位','Neighborhood Watch':'邻里守望','School Zone':'学校区域','Rough Surface':'路面不平','Hazardous Waste':'危险废物','Info Center':'信息中心','Camp Fish':'钓鱼营地','Arrowhead':'箭头路','Metal Pipe Flange':'金属管法兰','Hand Truck':'手推车','Shooting Range':'射击场','Bench Press':'卧推架','Weight Bar':'杠铃','Stationary Bike':'健身车',
})
AIR_WORDS.update({
 'X':'X光','Pill':'药盒','Drinkingfountain':'饮水机','Rotten':'腐朽','Treasure':'宝藏','Stiffs':'工具','Lab':'实验室','Equipment':'设备','Bookstore':'书店','Supplies':'补给','Toilets':'马桶','Tools':'工具','Decals':'贴花','Numbers':'数字','Zero':'零','Valiant':'英勇','Honor':'荣誉','Duty':'职责','Generator':'发电机','Plaster':'石膏','Cast':'固定带','Splint':'夹板','Legendary':'传奇','Crafting':'制造','Headlight':'车灯','Sewing':'缝纫','Market':'市场','Crypto':'加密货币','Shell':'炮弹','Axes':'斧头','Shovels':'铲子','Cobblestone':'鹅卵石','Stones':'石块','debris':'碎屑','Dirt':'泥土','Gravel':'砾石','Asphalt':'沥青','Torchs':'火把','Bombs':'炸弹','Timed':'定时','Charges':'炸药','Dynamites':'炸药','Molotov':'燃烧瓶','Cocktails':'瓶','Drum':'弹鼓','Intellect':'智力','Agility':'敏捷','Strength':'力量','Perception':'感知','Stealth':'潜行','Cigar':'雪茄','Repulsor':'排斥器','Banded':'加固','Cooling':'冷却','Mesh':'网','Insulated':'隔热','Liner':'内衬','Darts':'飞镖','Fir':'冷杉','Juniper':'杜松','Azalea':'杜鹃','Bush':'灌木','Ambulance':'救护车','Locked':'已上锁','Wrecks':'残骸','Tin':'铁罐','Three':'三','Two':'二','One':'一','Dynamic':'活动','Grate':'格栅','Stair':'楼梯','Restricted':'限制','Danger':'危险','Beware':'当心','Of':'','Administration':'管理处','Lease':'出租','Hard':'安全','Caution':'注意','Staff':'员工','Authorized':'授权','Personnel':'人员','Ramen':'拉面','Prime':'优选','Jerky':'肉干','Goblin':'哥布林','Bretzels':'椒盐卷饼','Labor':'劳动','Day':'节','Cigarette':'香烟','Tow':'拖走','Away':'区域','Handicap':'无障碍','Yard':'庭院','Return':'返回','Permitted':'允许','Attention':'注意','Neighborhood':'邻里','Watch':'守望','no':'禁行','Trucks':'卡车','Zone':'区域','Rough':'粗糙','Surface':'路面','Hazardous':'危险','Waste':'废物','Info':'信息','Camp':'营地','East':'东','Lang':'朗','Essig':'埃西格','Davis':'戴维斯','Huenink':'休宁克','east':'东向','Arrowhead':'箭头路','Flange':'法兰','Rebar':'钢筋','Brick':'砖','TCentered':'T形居中','Hand':'手推','Magnet':'磁铁','Hook':'吊钩','Spreader':'吊具','Tee':'T形接头','Belt':'输送带','DBLEnd':'双端','Pre':'前段','Scanner':'扫描器','Sneakers':'运动鞋','Womens':'女式','Heels':'高跟鞋','Sandals':'凉鞋','Sweaters':'毛衣','Hanger':'衣架','Shooting':'射击','Magazines':'杂志','Middl':'中段','Ice':'制冰','ATMInsecure':'未上锁ATM','ATMSecure':'上锁ATM','IV':'输液架','Hospital':'医院','Gurney':'担架','Press':'卧推架','Weight':'杠铃','Treadmill':'跑步机','Stationary':'健身','Bike':'自行车',
})

AIR_PHRASES.update({
 'Pet Cage':'宠物笼','Reptile Terrarium':'爬虫饲养箱','Communion Table':'圣餐桌','Picnic Table':'野餐桌','Dining Table':'餐桌','Curtain Rod':'窗帘杆','Table Saw':'台锯','Oil Shale':'油页岩','Potassium Nitrate':'硝酸钾','Robert Dishong Plaque':'罗伯特·迪尚纪念牌','Jet Girls':'喷气女孩','Street Sign':'街道路牌','Torch Wall Holder':'壁挂火把架','Track Light':'轨道灯','Studio Camera':'演播室摄像机','Ham Radio':'业余无线电台','Soda Fountain':'汽水机','Auto Turret':'自动炮塔','Flamethrower Trap':'火焰喷射陷阱','Dart Trap':'飞镖陷阱','Electric Fence Post':'电围栏柱','Electric Timer Relay':'定时继电器','Tripwire Post':'绊线柱','Electric Wire Relay':'电线继电器','Distribution Box':'配电箱','Air Conditioner':'空调','CTR Beam':'中央横梁','Urinal Commercial':'商用小便池','Pedestal Sink':'立柱式水槽','Kitchen Sink':'厨房水槽','Farm Plot':'农田方块','Impact Bracing':'抗冲击支撑改装件','Basic Plating':'基础护甲板','Structural Brace':'结构支撑改装件','Dew Gatherer':'露水收集器','Forge Crucible':'锻炉坩埚','Testosterone Extract':'睾酮提取物','Scope Lens':'瞄准镜片','Booze Barrel':'酒桶',
})
AIR_WORDS.update({
 'ATM':'自动柜员机','Slide':'滑梯','Pet':'宠物','Cage':'笼','Reptile':'爬虫','Terrarium':'饲养箱','Piano':'钢琴','Row':'一排','Communion':'圣餐','Picnic':'野餐','Dining':'餐厅','Rod':'杆','Doorn':'门','Saw':'锯','Workstation':'工作站','Planks':'木板','OSBWood':'定向刨花板','Moss':'苔藓','Cobweb':'蛛网','Shale':'页岩','Potassium':'钾','Nitrate':'硝酸盐','Lead':'铅','Noose':'绞索','Square':'方形','Bones':'骨骼','Plaque':'纪念牌','Sparky':'火花','Cats':'猫咪','Carrier':'航空母舰','Streetsign':'街道路牌','Holder':'支架','Crosswalk':'人行横道','POIBlack':'兴趣点黑色','BGrey':'B型灰色','BPurple':'B型紫色','Track':'轨道','LEDBlack':'LED黑色','LEDGrey':'LED灰色','LEDSilver':'LED银色','LEDWhite':'LED白色','Intact':'完好','Shade':'灯罩','CRTNo':'CRT无','Gnd':'地面对齐','Align':'对齐','On':'开启','Studio':'演播室','Camera':'摄像机','Radio':'无线电','Ham':'业余','Fountain':'饮料机','Toaster':'烤面包机','Auto':'自动','Flamethrower':'火焰喷射器','Dart':'飞镖','Electricfencepost':'电围栏柱','Loudspeaker':'扬声器','Electrictimerrelay':'定时继电器','Tripwirepost':'绊线柱','Electricwirerelay':'电线继电器','Curve':'弯曲','Dual':'双联','Distribution':'配电','Residential':'住宅用','er':'','Insulator':'绝缘子','BClosed':'B型关闭','BOpen':'B型开启','AClosed':'A型关闭','AOpen':'A型开启','Suitcase':'手提箱','Purse':'手提包','Duffle':'旅行袋','Debris':'碎片','CTRBeam':'中央横梁','SCTRBeam':'侧中央横梁','Urinal':'小便池','Foot':'足式','Pedestal':'立柱式','Kitchen':'厨房','CNROld':'转角老旧','CNRRound':'转角圆形','CNRRed':'转角红色','Granite':'花岗岩','Weapon':'武器','Intro':'入门','ACorner':'A型转角','Appliances':'家电','Plot':'农田','Sinks':'水槽','IBeam':'工字梁','Trellis':'格架','POIVariant':'兴趣点变体','Selector':'选择器','Pipes':'管道','Helipad':'直升机坪','American':'美国','Restore':'恢复','Fetch':'取回','Satchel':'任务包','Impact':'抗冲击','Bracing':'支撑','Basic':'基础','Brace':'支撑','Sawed':'锯短','Foregrip':'前握把','Sound':'声音','Gatherer':'收集器','Crucible':'坩埚','Anvil':'铁砧','Bellows':'风箱','Beaker':'烧杯','Engine':'发动机','Testosterone':'睾酮','Extract':'提取物','Lens':'镜片','Booze':'酒类','Potatoes':'土豆','Pumpkins':'南瓜','Chrysanthemums':'菊花',
})


def air_item_zh(text):
    text=re.sub(r' Quality ([1-6])$',r'（品质\1）',text)
    for en,zh in sorted(AIR_PHRASES.items(),key=lambda x:-len(x[0])):text=text.replace(en,zh)
    text=re.sub(r'[A-Za-z]+',lambda m:AIR_WORDS.get(m.group(0),m.group(0)),text)
    text=re.sub(r'(?<=[\u3400-\u9fff]) +(?=[\u3400-\u9fff])','',text)
    text=re.sub(r' +([，。；：、（）])',r'\1',text)
    return text.strip()


def translate_endgame_airdrops():
    path=ROOT/'04-AEC-ENDGAME_OVERHAUL/Config/Localization.csv';lines=path.read_text(encoding='utf-8-sig').splitlines(keepends=True);header=next(csv.reader([lines[0]]));c={x.lower():i for i,x in enumerate(header)};e,z=c['english'],c['schinese'];changed=0
    for i in range(1,len(lines)):
      line=lines[i];ending='\r\n' if line.endswith('\r\n') else '\n' if line.endswith('\n') else '';row=next(csv.reader([line.removesuffix(ending)]))
      if len(row)<=max(e,z) or not row[0].startswith('questItem_'):continue
      m=re.fullmatch(r'Airdrop Request for (\[[0-9A-Fa-f]{6}\])(.*?)(\[-\])',row[e])
      if not m:continue
      translated=f'空投申请：{m.group(1)}{air_item_zh(m.group(2))}{m.group(3)}'
      if row[z]==translated:continue
      row[z]=translated;out=io.StringIO();csv.writer(out,lineterminator=ending).writerow(row);lines[i]=out.getvalue();changed+=1
    with path.open('w',encoding='utf-8-sig',newline='') as h:h.write(''.join(lines))
    print(f"04-AEC-ENDGAME_OVERHAUL/Config/Localization.csv: updated {changed} airdrop request entries")


translate_endgame_airdrops()


PROGRESSION_TARGETS={
 'Hammer Part Miner':'巨锤部件采集器','Witch Part Miner':'女巫部件采集器','Siren mutation samples Boss':'塞壬Boss',
 'T00 Basic mutation samples':'T00基础变异样本','T01 Feral mutation samples':'T01凶暴变异样本',
 'T02 Radiated mutation samples':'T02辐射变异样本','T03 Charged mutation samples':'T03充能变异样本',
 'T04 Infernal mutation samples':'T04炼狱变异样本','T05 Veteran mutation samples':'T05老兵变异样本',
 'T06 Brutal mutation samples':'T06残暴变异样本','T07 Savage mutation samples':'T07凶猛变异样本',
 'T08 Ravager mutation samples':'T08劫掠者变异样本','T09 Dread mutation samples':'T09恐惧变异样本',
 'T10 Nightmare mutation samples':'T10梦魇变异样本','T11 Apex mutation samples':'T11巅峰变异样本',
 'T12 Overlord mutation samples':'T12霸主变异样本','T13 Titan mutation samples':'T13泰坦变异样本',
 'T14 Cataclysm mutation samples':'T14灾变变异样本','T15 Mythic mutation samples':'T15神话变异样本',
 'Party Beach Boss':'海滩派对Boss','Running Kamikaze Boss':'疾跑神风Boss','Explosive Eagle Boss':'爆炸鹰Boss',
 'Electric Demon Boss':'电魔Boss','Mechanician Boss':'机械师Boss','Hellskyli Boss':'赫尔斯凯利Boss','Mykir Boss':'迈基尔Boss',
 'Siren Head Boss':'塞壬头部Boss',
}


def progression_target_zh(target):
    target=target.rstrip('.')
    if target in PROGRESSION_TARGETS:return PROGRESSION_TARGETS[target]
    target=target.replace('mutation samples','变异样本').replace('Part Miner','部件采集器')
    return air_item_zh(target)


def translate_endgame_progression_all():
    path=ROOT/'04-AEC-ENDGAME_OVERHAUL/Config/Localization.csv';lines=path.read_text(encoding='utf-8-sig').splitlines(keepends=True);header=next(csv.reader([lines[0]]));c={x.lower():i for i,x in enumerate(header)};e,z=c['english'],c['schinese'];changed=0
    actions={'Craft':'制造','Kill':'击杀','Collect':'收集','Possess':'持有','Obtain':'获得'}
    for i in range(1,len(lines)):
      line=lines[i];ending='\r\n' if line.endswith('\r\n') else '\n' if line.endswith('\n') else '';row=next(csv.reader([line.removesuffix(ending)]))
      if len(row)<=max(e,z) or not (row[0].startswith('aecEOProgressionBook') or row[0].startswith('aec_eo_progression_')):continue
      english=row[e];translated=None
      m=re.fullmatch(r'(\[66B3FF\]AEC EO PROGRESSION\[-\] \d{3}/419 \| \d★ )(Minions|Bosses)( \d{2}/\d{2} \| )(.+)',english)
      if m:translated=f'{m.group(1).replace("PROGRESSION","进度")}{"仆从" if m.group(2)=="Minions" else "Boss"}{m.group(3)}{progression_target_zh(m.group(4))}'
      m2=re.fullmatch(r'(Craft|Kill|Collect|Possess|Obtain) (\d+) (.+?)(\.)?',english)
      if translated is None and m2:translated=f'{actions[m2.group(1)]}{m2.group(2)}个{progression_target_zh(m2.group(3))}。'
      if translated is None:
        matches=list(re.finditer(r'\b(Craft|Kill|Collect|Possess|Obtain)\s+(\d+)\s+(.+?)(?=\.\s|\.$|$)',english,re.I))
        if matches:
          mm=matches[-1];translated=f'完成AEC终局进度目标。[5ECFFF]目标[-] {actions[mm.group(1).title()]}{mm.group(2)}个{progression_target_zh(mm.group(3))}。'
      if translated is None or row[z]==translated:continue
      row[z]=translated;out=io.StringIO();csv.writer(out,lineterminator=ending).writerow(row);lines[i]=out.getvalue();changed+=1
    with path.open('w',encoding='utf-8-sig',newline='') as h:h.write(''.join(lines))
    print(f"04-AEC-ENDGAME_OVERHAUL/Config/Localization.csv: updated {changed} progression entries")


translate_endgame_progression_all()


HYDRO_PRODUCTS={'Yucca Fruit':'丝兰果','Coffee Beans':'咖啡豆','Radiated Mushrooms':'辐射蘑菇','Grace Corn':'超级玉米','Blueberries':'蓝莓','Corn':'玉米','Potato':'土豆','Mushrooms':'蘑菇','Pumpkin':'南瓜','Hops':'啤酒花','Aloe':'芦荟','Chrysanthemum':'菊花','Cotton':'棉花','Goldenrod':'一枝黄花'}


def translate_endgame_hydro():
    path=ROOT/'04-AEC-ENDGAME_OVERHAUL/Config/Localization.csv';lines=path.read_text(encoding='utf-8-sig').splitlines(keepends=True);header=next(csv.reader([lines[0]]));c={x.lower():i for i,x in enumerate(header)};e,z=c['english'],c['schinese'];changed=0
    pat=re.compile(r'MINING\\n\\n(?:Passively generates|Currently generating) \[DECEA3\](.+?) (\d★) Grown Plants\[-\](?: over time)?\.\\n\\nProduces \[DECEA3\](\d+) to (\d+) .+? every 30 minutes\[-\]\.');
    for i in range(1,len(lines)):
      line=lines[i];ending='\r\n' if line.endswith('\r\n') else '\n' if line.endswith('\n') else '';row=next(csv.reader([line.removesuffix(ending)]))
      if len(row)<=max(e,z) or not row[0].startswith('PassiveHydroCultivator'):continue
      m=pat.fullmatch(row[e]);
      if not m:continue
      product=HYDRO_PRODUCTS.get(m.group(1),air_item_zh(m.group(1)));translated=f'采集\\n\\n随时间自动产出[DECEA3]{product}{m.group(2)}成熟作物[-]。\\n\\n每30分钟产出[DECEA3]{m.group(3)}至{m.group(4)}份{product}[-]。'
      if row[z]==translated:continue
      row[z]=translated;out=io.StringIO();csv.writer(out,lineterminator=ending).writerow(row);lines[i]=out.getvalue();changed+=1
    with path.open('w',encoding='utf-8-sig',newline='') as h:h.write(''.join(lines))
    print(f"04-AEC-ENDGAME_OVERHAUL/Config/Localization.csv: updated {changed} hydroponic entries")


translate_endgame_hydro()


PERK_DESC_ZH={
 'Increases resistance to extreme heat and cold.':'提高对极端炎热与严寒的抗性。',
 'Improves prices when buying and selling with traders.':'改善与商人买卖时的价格。',
 'Increases the probability of finding bonus loot.':'提高发现额外战利品的概率。',
 'Reduces damage falloff at long range with ranged weapons.':'降低远程武器在远距离的伤害衰减。',
 'Increases the active range and target count of junk turrets.':'提高机器人炮塔的有效范围与可锁定目标数量。',
 'Each rank increases junk turret active range by 1m and target count.':'每一级使机器人炮塔的有效范围提高1米，并增加可锁定目标数量。',
 'Reduces food and water consumed when regenerating stamina.':'降低恢复耐力时消耗的食物与水。',
 'Each rank reduces water and food loss per stamina point gained by 1%.':'每一级使每恢复一点耐力消耗的水和食物降低1%。',
 'Improves quest reward bonuses and reward choices.':'提高任务的额外奖励与奖励选项数量。',
 'Each rank increases quest bonus item rewards and reward choice count.':'每一级都会增加任务的额外物品奖励和奖励选项数量。',
 'Each rank reduces lockpick time and break chance by 2%.':'每一级使开锁时间和撬锁器损坏概率降低2%。',
 'Reduces water consumed when recovering health.':'降低恢复生命时消耗的水。',
 'Each rank reduces water loss per health point gained by 1%.':'每一级使每恢复一点生命消耗的水降低1%。',
 'Reduces the noise you make.':'降低你行动时产生的噪声。',
 'Reduces the light you emit, making you harder to spot.':'降低你散发的光亮，使敌人更难发现你。',
 'Reduces how long enemies search for you after losing track.':'缩短敌人失去你的踪迹后继续搜索的时间。',
 'Each rank reduces target armor by 2% for melee attacks.':'每一级使近战攻击降低目标2%的护甲。',
 'Reduces vertical and horizontal recoil kick on ranged weapons.':'降低远程武器的垂直与水平后坐力。',
 'Improves accuracy when firing from the hip on ranged weapons.':'提高远程武器腰射时的精度。',
 'Improves your standing with traders, unlocking better stock.':'改善你与商人的关系，从而解锁更好的商品。',
 'Reduces spread growth from sustained ranged fire.':'降低远程武器持续射击时的散布增长。',
}


def translate_endgame_perk_descs():
    path=ROOT/'04-AEC-ENDGAME_OVERHAUL/Config/Localization.csv';lines=path.read_text(encoding='utf-8-sig').splitlines(keepends=True);header=next(csv.reader([lines[0]]));c={x.lower():i for i,x in enumerate(header)};e,z=c['english'],c['schinese'];changed=0
    for i in range(1,len(lines)):
      line=lines[i];ending='\r\n' if line.endswith('\r\n') else '\n' if line.endswith('\n') else '';row=next(csv.reader([line.removesuffix(ending)]));translated=PERK_DESC_ZH.get(row[e]) if len(row)>max(e,z) else None
      if translated is None or row[z]==translated:continue
      row[z]=translated;out=io.StringIO();csv.writer(out,lineterminator=ending).writerow(row);lines[i]=out.getvalue();changed+=1
    with path.open('w',encoding='utf-8-sig',newline='') as h:h.write(''.join(lines))
    print(f"04-AEC-ENDGAME_OVERHAUL/Config/Localization.csv: updated {changed} perk descriptions")


translate_endgame_perk_descs()


FIELD_BOOK_TITLES={1:'进度与游戏阶段',2:'新手保护',3:'热度压力',4:'危险0至5',5:'动态生成',6:'AI世界事件',7:'宿敌名册',8:'宿敌战争与迁移',9:'契约与代币',10:'样本、市场与装备'}
CHALLENGE_TARGETS={
 'Electricdemon Boss':'电魔Boss','Explosiveeagle Boss':'爆炸鹰Boss','Hammerguardian Boss':'巨锤守卫Boss','Partybeach Boss':'海滩派对Boss','Sirenhead Boss':'塞壬头部Boss','Witch Boss':'女巫Boss',
 'ElectricdemonBoss':'电魔Boss','ExplosiveeagleBoss':'爆炸鹰Boss','HammerguardianBoss':'巨锤守卫Boss','PartybeachBoss':'海滩派对Boss','SirenheadBoss':'塞壬头部Boss','WitchBoss':'女巫Boss',
 'Running Kamikaze':'疾跑神风','Hammer Guardian':'巨锤守卫','The Archer Boss':'弓箭手Boss','Archer Boss':'弓箭手Boss',
 'Doomlord Demolishman Boss':'末日领主爆破者Boss','Electric Demon Runner Boss':'电魔疾行者Boss','The Witch Angel Boss':'女巫天使Boss',
}


def challenge_target_zh(target):
    if target in CHALLENGE_TARGETS:return CHALLENGE_TARGETS[target]
    return air_item_zh(target).replace(' Boss','Boss').replace(' Minion','仆从').replace(' Zombie','僵尸')


def translate_endgame_template_residue():
    path=ROOT/'04-AEC-ENDGAME_OVERHAUL/Config/Localization.csv';lines=path.read_text(encoding='utf-8-sig').splitlines(keepends=True);header=next(csv.reader([lines[0]]));c={x.lower():i for i,x in enumerate(header)};e,z=c['english'],c['schinese'];changed=0
    miner_pat=re.compile(r'MINING\\n\\n(?:Passively generates|Currently generating) \[DECEA3\](.+?)\[-\](?: over time)?\.\\n\\nProduces \[DECEA3\](\d+) to (\d+) (?:Zombie Parts|Minion Parts|parts) every 30 minutes\[-\]\.')
    ready_pat=re.compile(r'READY\\n\\nCollect your \[DECEA3\](.+?)\[-\]\.');schem_pat=re.compile(r'\[FFD700\]AEC\[-\] \[FFFFFF\]Tier (\d{2})\[-\] (?:\[[0-9A-Fa-f]{6}\])?(.+?)(?:\[-\])? Schematic \[([IV]+)\]')
    for i in range(1,len(lines)):
      line=lines[i];ending='\r\n' if line.endswith('\r\n') else '\n' if line.endswith('\n') else '';row=next(csv.reader([line.removesuffix(ending)]));key,english=row[0],row[e].strip() if len(row)>e else '';translated=None
      if key.startswith('modAECMutator') and not key.endswith('Desc') and 'LVL.' in row[z]:translated=row[z].replace('LVL.','等级')
      m=miner_pat.fullmatch(english)
      if m:
        target=challenge_target_zh(m.group(1)).replace('僵尸 Parts','僵尸部件').replace('仆从 Parts','仆从部件').replace(' Parts','部件')
        translated=f'采集\\n\\n随时间自动产出[DECEA3]{target}[-]。\\n\\n每30分钟产出[DECEA3]{m.group(2)}至{m.group(3)}份部件[-]。'
      m=ready_pat.fullmatch(english)
      if m:
        target=challenge_target_zh(m.group(1)).replace('Grown Plants','成熟作物').replace('Zombie Parts','僵尸部件').replace('Minion Parts','仆从部件')
        translated=f'已就绪\\n\\n领取你的[DECEA3]{target}[-]。'
      m=re.fullmatch(r'Kill (.+?) (\d+) times?\.',english)
      if m and key.startswith('aecChallenge'):translated=f'击杀{challenge_target_zh(m.group(1))}{m.group(2)}次。'
      m=re.fullmatch(r'Kill (.+?)(\.)?',english)
      if translated is None and m and key.startswith('aecChallenge'):translated=f'击杀{challenge_target_zh(m.group(1))}。'
      m=re.fullmatch(r'Eliminate (.+?) once\.',english)
      if m and key.startswith('aecChallenge'):translated=f'击杀{challenge_target_zh(m.group(1))}一次。'
      m=re.fullmatch(r'Eliminate (\d+) (.+?) minions\.',english,re.I)
      if translated is None and m and key.startswith('aecChallenge'):translated=f'消灭{m.group(1)}个{challenge_target_zh(m.group(2))}仆从。'
      m=schem_pat.fullmatch(english)
      if m:translated=f'[FFD700]AEC[-] [FFFFFF]等级{m.group(1)}[-] [{"AAAAAA" if int(m.group(1))==5 else "FFFFFF"}]{ENDGAME_TIERS[int(m.group(1))]}[-]设计图 [{m.group(3)}]'
      m=re.fullmatch(r'\[66B3FF\]AEC FIELD GUIDE (\d+)/10\[-\] \| .+',english)
      if m:translated=f'[66B3FF]AEC野外指南 {m.group(1)}/10[-] | {FIELD_BOOK_TITLES[int(m.group(1))]}'
      if key.startswith('aecTarget') and english:translated=challenge_target_zh(english)
      if key.startswith('aec_eo_tutorial_') and '_obj_' in key and english:
        translated='AEC T05僵尸' if english=='Tier 05 AEC Zombies' else '原版僵尸' if english=='Vanilla Zombies' else challenge_target_zh(english)
      m=re.fullmatch(r'(\[FFD700\]AEC\[-\]) (.+)',english)
      if m and key.startswith('AEC_'):translated=f'{m.group(1)} {challenge_target_zh(m.group(2))}'
      m=re.fullmatch(r'(\[FFD700\]AEC\[-\]) (.+? Connection Card)',english)
      if m:translated=f'{m.group(1)} {challenge_target_zh(m.group(2))}'
      m=re.fullmatch(r'\[DECEA3\]AEC\[-\] Necroforge (.+?) Ammo Qty Bundle (T\d{2})',english)
      if m:
        caliber=m.group(1).replace('.44 Magnum','.44马格南').replace('12 Gauge','12号').replace('9mm','9毫米');translated=f'[DECEA3]AEC[-] 死灵锻炉{caliber}弹药数量包 {m.group(2)}'
      m=re.fullmatch(r'MINING\\n\\n(?:Passively generates|Currently generating) \[DECEA3\](Generic \d★ Coins)\[-\](?: over time)?\.\\n\\nProduces \[DECEA3\](\d+) to (\d+) .+? every 30 minutes\[-\]\.',english)
      if m:translated=f'采集\\n\\n随时间自动产出[DECEA3]{challenge_target_zh(m.group(1))}[-]。\\n\\n每30分钟产出[DECEA3]{m.group(2)}至{m.group(3)}枚通用硬币[-]。'
      m=re.fullmatch(r'(.+?) (\d★) Hydro Cultivator Airdrop',english)
      if m:translated=f'{HYDRO_PRODUCTS.get(m.group(1),challenge_target_zh(m.group(1)))}{m.group(2)}水培种植器空投'
      m=re.fullmatch(r'(.+?) (\d★) Passive Part Miner Airdrop',english)
      if m:translated=f'{challenge_target_zh(m.group(1))}{m.group(2)}被动部件采集器空投'
      if key.startswith('airdrop_') and english.endswith(' Airdrop'):translated=challenge_target_zh(english)
      m=re.fullmatch(r'\[FF0000\]AEC BLOODMOON\[-\] (\[[0-9A-Fa-f]{6}\])Reward Airdrop - Level (\d+)\[-\]',english)
      if m:translated=f'[FF0000]AEC血月[-] {m.group(1)}奖励空投——等级{m.group(2)}[-]'
      if translated is None or len(row)<=z or row[z]==translated:continue
      row[z]=translated;out=io.StringIO();csv.writer(out,lineterminator=ending).writerow(row);lines[i]=out.getvalue();changed+=1
    with path.open('w',encoding='utf-8-sig',newline='') as h:h.write(''.join(lines))
    print(f"04-AEC-ENDGAME_OVERHAUL/Config/Localization.csv: updated {changed} remaining template entries")


translate_endgame_template_residue()


PERK_TERM_ZH={
 'crushed rock':'碎石','asphalt and concrete terrain':'沥青和混凝土地形','stone terrain':'岩石地形','clay and plant fiber':'黏土和植物纤维','forest ground':'森林地表','clay':'黏土','burnt forest ground':'焦土地表','sand and clay':'沙和黏土','desert ground':'沙漠地表','sand terrain':'沙土地形','snowballs':'雪球','snowball':'雪球','snow terrain':'雪地','sand and rock':'沙和岩石','rubble terrain':'瓦砾地形','wood':'木材','wood debris terrain':'木质废墟地形','gravel terrain':'砾石地形','scrap iron':'废铁','iron ore terrain':'铁矿地形','scrap lead':'铅废料','lead ore terrain':'铅矿地形','coal':'煤','coal ore terrain':'煤矿地形','potassium nitrate':'硝酸钾','nitrate ore terrain':'硝酸盐矿地形','oil shale':'油页岩','oil deposit terrain':'油页岩矿地形','parts':'零件','part':'零件','wrecked vehicles':'车辆残骸','trees':'树木','scrap':'废料','doors':'门','chests and storage containers':'箱子和储物容器','dirt terrain':'泥土地形',
}
PERK_EXACT_ZH={
 'Terrain-harvesting perks. Requires Strength 10/10 to start investing.':'地形采集类技能。需要力量达到10/10后才能开始投入技能点。',
 'Melee combat perks. Requires Agility 10/10 to start investing.':'近战类技能。需要敏捷达到10/10后才能开始投入技能点。','Ranged combat perks. Requires Perception 10/10 to start investing.':'远程战斗类技能。需要感知达到10/10后才能开始投入技能点。','Body and survival perks. Requires Fortitude 10/10 to start investing.':'体能与生存类技能。需要坚韧达到10/10后才能开始投入技能点。','Loot and trading perks. Requires Intellect 10/10 to start investing.':'搜刮与交易类技能。需要智力达到10/10后才能开始投入技能点。','Crafting and tool perks. Requires Intellect 10/10 to start investing.':'制造与工具类技能。需要智力达到10/10后才能开始投入技能点。','Stealth perks. Requires Agility 10/10 to start investing.':'潜行类技能。需要敏捷达到10/10后才能开始投入技能点。',
 'Increases melee weapon damage and attack speed.':'提高近战武器伤害和攻击速度。','Each rank increases melee weapon damage and attacks per minute by 2%.':'每一级使近战武器伤害和每分钟攻击次数提高2%。','Increases the chance to dismember enemies in combat.':'提高战斗中肢解敌人的概率。','Each rank increases the number of enemies pierced by melee attacks.':'每一级都会增加近战攻击能够穿透的敌人数量。','Reduces degradation on melee weapons.':'降低近战武器的耐久损耗。',
 'Increases effective range of ranged weapons.':'提高远程武器的有效射程。','Each rank increases ranged weapon max range by 5%.':'每一级使远程武器的最大射程提高5%。','Increases reload speed of ranged weapons.':'提高远程武器的装填速度。','Each rank increases ranged weapon reload speed by 2%.':'每一级使远程武器的装填速度提高2%。','Increases ranged weapon damage.':'提高远程武器伤害。','Each rank increases ranged weapon damage by 2%.':'每一级使远程武器伤害提高2%。','Each rank increases ranged weapon magazine size by 10%.':'每一级使远程武器的弹匣容量提高10%。',
 'Each rank increases maximum health by 5 points.':'每一级使最大生命值提高5点。','Each rank increases maximum stamina by 5 points.':'每一级使最大耐力提高5点。','Each rank increases hot and cold resistance by 2 points.':'每一级使炎热与寒冷抗性提高2点。','Increases the quantity of loot found.':'提高找到的战利品数量。','Each rank improves buying and selling prices by 1%.':'每一级使买入和卖出价格改善1%。','Reduces the time needed to search containers.':'缩短搜索容器所需的时间。','Reduces the time needed to craft items.':'缩短制造物品所需的时间。','Reduces degradation on tools.':'降低工具的耐久损耗。','Increases block damage dealt by tools.':'提高工具造成的方块伤害。','Each rank increases tool block damage by 2%.':'每一级使工具的方块伤害提高2%。','Increases entity damage dealt by tools.':'提高工具对实体造成的伤害。','Each rank increases tool entity damage by 2%.':'每一级使工具的实体伤害提高2%。',
 'Increases general movement speed.':'提高整体移动速度。','Increases resistance to negative buffs and afflictions.':'提高对负面状态和疾病的抗性。','Each rank increases resistance to negative buffs by 2%.':'每一级使负面状态抗性提高2%。','Increases jump height.':'提高跳跃高度。','Each rank increases jump strength by 0.5 points.':'每一级使跳跃强度提高0.5点。','Improves handling of ranged weapons.':'改善远程武器的操控性。','Each rank improves ranged weapon handling by 2%.':'每一级使远程武器操控性提高2%。','Each rank reduces ranged weapon damage falloff by 5%.':'每一级使远程武器的伤害衰减降低5%。','Reduces incoming health loss.':'降低受到攻击时损失的生命值。',
 'Increases the number of items found in secret stashes.':'增加在秘密储藏点中找到的物品数量。','Each rank increases secret stash item count by 1.':'每一级使秘密储藏点的物品数量增加1个。','Increases experience gained.':'提高获得的经验值。','Each rank increases player experience gain by 2%.':'每一级使玩家获得的经验值提高2%。','Increases the rate of fire of motorized tools.':'提高动力工具的运转速度。','Each rank increases tool rounds per minute by 2%.':'每一级使工具的每分钟运转次数提高2%。','Adds flat bonus damage to tool attacks.':'使工具攻击获得固定额外伤害。','Each rank adds 2% bonus damage to tool attacks.':'每一级使工具攻击增加2%的额外伤害。',
 'Reduces stamina spent on physical actions.':'降低体力活动消耗的耐力。','Each rank reduces stamina loss by 1%.':'每一级使耐力消耗降低1%。','Increases movement speed while crouching.':'提高蹲伏时的移动速度。','Each rank reduces noise generated by 2%.':'每一级使产生的噪声降低2%。','Each rank reduces light emitted by 2%.':'每一级使发出的光亮降低2%。','Each rank reduces enemy search duration by 2%.':'每一级使敌人的搜索持续时间降低2%。','Each rank reduces recoil kick angles by 3%.':'每一级使后坐力偏移角度降低3%。','Each rank reduces hip-fire spread by 2%.':'每一级使腰射散布降低2%。','Reduces all incoming damage.':'降低受到的所有伤害。','Each rank reduces incoming damage by 1%.':'每一级使受到的伤害降低1%。',
 'Increases the quality tier of loot found in containers.':'提高容器中战利品的品质等级。','Each rank increases loot stage by 1%.':'每一级使战利品阶段提高1%。','Each rank increases trader stage by 2.':'每一级使商人阶段提高2。','Each rank reduces incremental spread growth by 3%.':'每一级使连续射击的散布增长降低3%。','Increases the maximum speed of vehicles you drive.':'提高你驾驶载具时的最高速度。','Each rank increases vehicle max speed by 1%.':'每一级使载具最高速度提高1%。','Master perk of the Survivor Fortitude category. Raising this tier\'s level unlocks matching levels in every perk of this category.':'“生存者坚韧”类别的大师技能。提升当前星级可解锁该类别所有技能的对应等级。','Unlocks a natural 20% chance to harvest honey from trees.':'解锁从树木中采集到蜂蜜的20%基础概率。','Grants a 20% chance to harvest honey from any tree.':'从任意树木中采集时，有20%概率获得蜂蜜。','Grants a 5% chance to harvest a queen bee from any tree.':'从任意树木中采集时，有5%概率获得蜂后。',
}
PERK_NAME_ZH={'Rock Crusher':'碎岩专家','Ash Breaker':'焦土破坏者','Vehicle Prospector':'车辆拆解专家','Vehicle Breaker':'车辆破坏者','Timber Breaker':'伐木专家','Door Prospector':'门扉拆解专家','Door Breaker':'门扉破坏者','Chest Prospector':'容器拆解专家','Chest Breaker':'容器破坏者','Honey Harvester':'蜂蜜采集者','Queen Bee Harvester':'蜂后采集者','Bone Breaker':'碎骨者','Iron Grip':'铁腕','Iron Constitution':'钢铁体魄','Rapid Assembly':'快速装配','Deadeye Range':'鹰眼射程','Lock Breaker':'开锁专家','Overclocked Tools':'超频工具','Ghost Walker':'幽灵行者','Iron Hide':'钢铁皮肤'}


def translate_endgame_perk_residue():
    path=ROOT/'04-AEC-ENDGAME_OVERHAUL/Config/Localization.csv';lines=path.read_text(encoding='utf-8-sig').splitlines(keepends=True);header=next(csv.reader([lines[0]]));c={x.lower():i for i,x in enumerate(header)};e,z=c['english'],c['schinese'];changed=0
    def term(s):return PERK_TERM_ZH.get(s,s)
    for i in range(1,len(lines)):
      line=lines[i];ending='\r\n' if line.endswith('\r\n') else '\n' if line.endswith('\n') else '';row=next(csv.reader([line.removesuffix(ending)]));english=row[e] if len(row)>e else '';translated=PERK_EXACT_ZH.get(english,PERK_NAME_ZH.get(english))
      m=re.fullmatch(r'Harvest far more (.+) from (.+)\.',english)
      if m:translated=f'从{term(m.group(2))}采集更多{term(m.group(1))}。'
      m=re.fullmatch(r'Each rank increases (.+) yield from (.+) by (\d+%)\.',english)
      if m:translated=f'每一级使从{term(m.group(2))}采集的{term(m.group(1))}产量提高{m.group(3)}。'
      m=re.fullmatch(r'Deal far more damage to (.+)\.',english)
      if m:translated=f'对{term(m.group(1))}造成大幅额外伤害。'
      m=re.fullmatch(r'Each rank increases damage dealt to (.+) by (\d+%)\.',english)
      if m:translated=f'每一级使对{term(m.group(1))}造成的伤害提高{m.group(2)}。'
      m=re.fullmatch(r'Survivor Fortitude - ([1-5]★)',english)
      if m:translated=f'生存者坚韧 - {m.group(1)}'
      if translated is None or not (row[0].startswith('perkAec') or row[0].startswith('skillAec')) or row[z]==translated:continue
      row[z]=translated;out=io.StringIO();csv.writer(out,lineterminator=ending).writerow(row);lines[i]=out.getvalue();changed+=1
    with path.open('w',encoding='utf-8-sig',newline='') as h:h.write(''.join(lines))
    print(f"04-AEC-ENDGAME_OVERHAUL/Config/Localization.csv: updated {changed} perk residue entries")


translate_endgame_perk_residue()


def stat_segment_zh(seg):
    s=seg.strip().strip('.')
    rules=[
      (r'rate of fire ([+-]\d+%)',lambda m:f"射速{m.group(1)}"),(r'(?:entity )?damage ([+-]\d+%)',lambda m:f"实体伤害{m.group(1)}"),
      (r'block damage ([+-]\d+%)',lambda m:f"方块伤害{m.group(1)}"),(r'damage bonus ([+-][\d.]+)',lambda m:f"伤害加成{m.group(1)}"),
      (r'dismember chance ([+-][\d.]+)',lambda m:f"肢解概率{m.group(1)}"),(r'reload speed ([+-]\d+%)',lambda m:f"装填速度{m.group(1)}"),
      (r'harvest count ([+-]\d+%)',lambda m:f"采集量{m.group(1)}"),(r'degradation per use ([+-]\d+%)',lambda m:f"每次使用的耐久损耗{m.group(1)}"),
      (r'health regen ([+-][\d.]+)',lambda m:f"生命恢复{m.group(1)}"),(r'stamina regen ([+-][\d.]+)',lambda m:f"耐力恢复{m.group(1)}"),
      (r'max stamina ([+-]\d+%)',lambda m:f"最大耐力{m.group(1)}"),(r'xp gain ([+-]\d+%)(?: then ([+-]\d+%))?',lambda m:f"经验获取{m.group(1)}"+(f"，附加阶段{m.group(2)}" if m.group(2) else '')),
      (r'run speed ([+-]\d+%)',lambda m:f"奔跑速度{m.group(1)}"),(r'walk speed ([+-]\d+%)',lambda m:f"行走速度{m.group(1)}"),
      (r'jump strength ([+-]\d+%)',lambda m:f"跳跃强度{m.group(1)}"),(r'stamina cost ([+-]\d+%)',lambda m:f"耐力消耗{m.group(1)}"),
      (r'(?:massive |giant |huge )?ragdoll in area ([\d.]+) for ([\d.]+)',lambda m:f"在{m.group(1)}范围内造成持续{m.group(2)}秒的击飞"),
      (r'direct ragdoll for ([\d.]+)',lambda m:f"直接击飞目标{m.group(1)}秒"),(r'knockdown (guaranteed 100%|\d+%) in area ([\d.]+)',lambda m:f"在{m.group(2)}范围内以{m.group(1).replace('guaranteed ','')}概率击倒"),
      (r'buff ?Shocked (guaranteed 100%|\d+%) in area ([\d.]+) for ([\d.]+)',lambda m:f"在{m.group(2)}范围内以{m.group(1).replace('guaranteed ','')}概率施加{m.group(3)}秒电击"),
      (r'buff Injury Knockdown (guaranteed 100%|\d+%) in area ([\d.]+)',lambda m:f"在{m.group(2)}范围内以{m.group(1).replace('guaranteed ','')}概率造成受伤击倒"),
    ]
    for pat,fn in rules:
      m=re.fullmatch(pat,s,re.I)
      if m:return fn(m)
    return None


BASE_STAT_LABELS={
 'Stamina Regeneration':'耐力恢复','Hipfire Accuracy':'腰射精度','Aiming Accuracy':'瞄准精度',
 'Recoil Vertical Min':'垂直后坐力下限','Recoil Vertical Max':'垂直后坐力上限',
 'Recoil Horizontal Min':'水平后坐力下限','Recoil Horizontal Max':'水平后坐力上限',
 'Attack Speed':'攻击速度','Block Damage':'方块伤害','Harvest Yield':'采集产量','Crouch Speed':'蹲行速度',
 'Durability Loss':'耐久损耗','Dismember Chance':'肢解概率','Magazine Size':'弹匣容量',
 'Burn Chance':'燃烧概率','Burn Duration':'燃烧持续时间','Health Regeneration':'生命恢复',
 'Jump Strength':'跳跃强度','Knockback Chance':'击退概率','Knockback Force':'击退力度',
 'Knockback Duration':'击退持续时间','Knockback Range':'击退范围','Effective Range':'有效射程',
 'Max Range':'最大射程','Reload Speed':'装填速度','Fire Rate':'射速','Run Speed':'奔跑速度',
 'Shock Chance':'电击概率','Shock Duration':'电击持续时间','Sneak Damage':'潜行伤害',
 'Stamina Cost':'耐力消耗','Max Stamina':'最大耐力','Stun Chance':'眩晕概率',
 'Penetration':'穿透力','Walk Speed':'行走速度','XP Gain':'经验获取','Enemy Damage':'对敌伤害',
}
INSERT_TARGETS={
 'armor':'护甲','bows / guns / ranged turrets':'弓弩、枪械和远程炮塔','melee weapons / tools':'近战武器和工具',
 'boots':'靴子','bows / guns / melee weapons / ranged turrets / tools':'弓弩、枪械、近战武器、远程炮塔和工具',
}


def base_mutator_zh(english):
    if '\\nCan be inserted on: ' not in english:return None
    stat_text,target=english.split('\\nCan be inserted on: ',1);target=target.strip().rstrip('.')
    if stat_text.startswith('Enemy Damage + ? %. Craft it to see the exact value'):
        stats='对敌伤害增加值将在制造后显示'
    else:
        parts=[]
        for seg in stat_text.split(' | '):
            seg=seg.strip();matched=False
            for label,zh in sorted(BASE_STAT_LABELS.items(),key=lambda x:-len(x[0])):
                if seg.startswith(label+' '):
                    value=seg[len(label)+1:].replace('s','秒') if label.endswith('Duration') else seg[len(label)+1:]
                    if label.endswith('Range') and value.endswith('m'):value=value[:-1]+'米'
                    parts.append(f'{zh}{value}');matched=True;break
            if not matched:return None
        stats='；'.join(parts)
    return f'{stats}。可安装于：{INSERT_TARGETS.get(target,target)}。'


def translate_endgame_mutators():
    path=ROOT/'04-AEC-ENDGAME_OVERHAUL/Config/Localization.csv';lines=path.read_text(encoding='utf-8-sig').splitlines(keepends=True);header=next(csv.reader([lines[0]]));c={x.lower():i for i,x in enumerate(header)};e,z=c['english'],c['schinese'];changed=0
    for i in range(1,len(lines)):
      line=lines[i];ending='\r\n' if line.endswith('\r\n') else '\n' if line.endswith('\n') else '';row=next(csv.reader([line.removesuffix(ending)]))
      if len(row)<=max(e,z) or not row[0].startswith('modAEC') or not row[0].endswith('Desc') or not row[e].strip():continue
      english=row[e];base=base_mutator_zh(english)
      if base:
        if row[z]!=base:
          row[z]=base;out=io.StringIO();csv.writer(out,lineterminator=ending).writerow(row);lines[i]=out.getvalue();changed+=1
        continue
      color='';endcolor=''
      m=re.match(r'(\[[0-9A-Fa-f]{6}\])',english)
      if m:color=m.group(1);english=english[len(color):];endcolor='[-]'
      english=re.sub(r'\[-\]$','',english)
      main=re.sub(r'\. Can be installed on .*?$','',english)
      parts=[p.strip() for p in main.split(';')];stats=[]
      for p in parts[1:]:
        zh=stat_segment_zh(p)
        if zh:stats.append(zh)
      if not stats:continue
      key=row[0].lower();target='所有工具' if 'tool' in key else '所有弓弩武器' if 'bow' in key else '枪械和远程炮塔' if 'gun' in key else '所有护甲部位' if any(x in key for x in ('armor','leg','head')) else '武器和工具'
      translated=f"{color}终局变异器效果："+'；'.join(stats)+f"。可安装于{target}。{endcolor}"
      if row[z]==translated:continue
      row[z]=translated
      out=io.StringIO();csv.writer(out,lineterminator=ending).writerow(row);lines[i]=out.getvalue();changed+=1
    with path.open('w',encoding='utf-8-sig',newline='') as h:h.write(''.join(lines))
    print(f"04-AEC-ENDGAME_OVERHAUL/Config/Localization.csv: updated {changed} mutator descriptions")


translate_endgame_mutators()


BOSS_LATE_EXACT = {
    "dialogAECQuestGiverStart": "报上你要挑战的梯队。我这里只处理AEC契约。",
    "dialogAECQuestGiverQuest": "让我看看AEC契约。",
    "aecBossInfoBtnText": "[FFD700]AEC全合一Boss——契约开放条件[-]",
    "aecInfoTitleText": "[FFD700]── 契约开放条件 ──[-]",
    "aecBossPartsBtnText": "[FFD700]AEC全合一Boss——Boss部件[-]",
    "aecQuestGuideBtnText": "[FFD700]AEC全合一Boss——任务指南[-]",
    "aecBossPartsTitleText": "[FFD700]── Boss部件 ──[-]",
    "aecBossPartsLine1Text": "部件包括头部、手臂、腿部、躯干，以及稀有的心脏。",
    "aecBossPartsLine4Text": "[AAAAAA]后续更新：这些部件将用于制造AEC护甲和武器。[-]",
    "aecQuestBossDefenseBtnText": "Boss防守任务", "aecQuestMinionPatrolBtnText": "仆从巡逻任务",
    "aecQuestBackGuideText": "[AAAAAA]← 返回任务指南[-]", "aecQuestBossDefenseTitleText": "[FFD700]── Boss防守 ──[-]",
    "aecQuestMinionPatrolTitleText": "[FFD700]── 仆从巡逻 ──[-]",
    "aecQuestSpecialLine1Text": "多Boss任务：连续击杀2只或更多Boss",
    "aecQuestSpecialLine2Text": "双Boss任务：同时迎战2只Boss",
}
for _star, _level in enumerate((0, 10, 20, 35, 50, 75, 100), 1):
    _requirement = "无等级要求" if _level == 0 else f"需要等级{_level}"
    BOSS_LATE_EXACT[f"aecInfoLine{_star}Text"] = f"[FFFFFF]{_star}[FFD700][FFD700]★[-] 契约——{_requirement}[-]"


def translate_boss_late_cleanup() -> None:
    path = ROOT / "03-AEC-AIO_BOSS_EXTREME_EDITION/Config/Localization.csv"; lines = path.read_text(encoding="utf-8-sig").splitlines(keepends=True)
    header = next(csv.reader([lines[0]])); columns = {name.lower(): i for i, name in enumerate(header)}; z = columns["schinese"]
    changed = 0
    replacements = (
        ("to rank up", "以提升等级"), ("rank up", "提升等级"), ("1-Star", "1★"), ("2-Star", "2★"),
        ("3-Star", "3★"), ("4-Star", "4★"), ("5-Star", "5★"), ("HUNT:", "猎杀："), ("SIEGE:", "围攻："),
        ("100 Waves super-challenge", "100波超级挑战"), ("100 Waves challenge", "100波挑战"),
        ("100 WAVE ENDURANCE", "100波耐力挑战"), ("Boss FIGHT", "Boss战斗"), ("FIGHT", "战斗"),
        ("Hunt and Kill", "猎杀并击杀"), ("Survive", "抵御"), ("Eliminate", "消灭"), ("Kill", "击杀"),
        ("The ", ""), ("the ", ""), (" THE ", " "), ("Star", "星"), ("rank", "等级"),
        ("Witch", "女巫"), ("RunningKamikaze", "狂奔神风者"), ("Running Kamikaze", "狂奔神风者"),
        ("Kamikaze", "神风者"), ("Hellskyli", "赫尔斯凯利"), ("Mykir", "迈基尔"),
        ("Headless", "无头者"), ("Archer", "弓箭手"), ("Ghost", "幽灵"),
        ("Hammer Guardian", "战锤守卫"), ("Hammer", "战锤守卫"), ("HAMMER", "战锤守卫"),
        ("Electric Demon", "电魔"), ("Burning Demon", "燃烧恶魔"), ("Spider Demon", "蜘蛛恶魔"),
        ("Demon", "恶魔"), ("Deputy", "副警长"), ("Aura", "威压光环"),
        ("Bowler", "投球手"), ("Skateboarder", "滑板手"), ("Arlene", "阿琳"),
        ("Brute", "蛮兵"), ("Scout", "斥候"), ("Runner", "奔跑者"), ("Brawler", "斗士"),
        ("Remains", "残骸"), ("Enraged", "狂怒"), ("Giant", "巨型"), ("Chicken", "鸡"),
        ("Grizzly", "灰熊"), ("Crossbow", "弩手"), ("Hazmat", "危险品感染者"),
        ("Hunt", "猎杀"), ("HUNT", "猎杀"), ("Defense", "防守"), ("DEFENSE", "防守"),
        ("Patrol", "巡逻"), ("PATROL", "巡逻"), ("Assault", "突袭"), ("assault", "突袭"),
        ("Siege", "围攻"), ("SIEGE", "围攻"), ("Wave", "波"), ("waves", "波"),
        ("Endurance", "耐力挑战"), ("Super", "超级"), ("super", "超级"),
        ("Menagerie", "怪物园"), ("Gauntlet", "试炼"), ("Spider", "蜘蛛"), ("Primal", "原始"),
        ("Wild", "荒野"), ("Colony", "菌菇群落"), ("Corps", "军团"), ("Squad", "小队"),
        ("Rush", "突击"), ("Nest", "狙击阵地"), ("Guard", "守卫队"), ("Runners", "奔袭队"),
        ("Mixed", "混合"), ("Demo Squad", "爆破小队"), ("Overwatch", "监视火力"),
        ("Fire Team", "火力小组"), ("Forces", "部队"), ("Demolishman himself", "爆破者本体"),
        ("NEARBY", "就在附近"), ("PRESSURE", "威压"), ("Next 挑战 wave", "下一波挑战"),
        ("NO 等级 REQUIRED", "无等级要求"), ("REQUIRED", "要求"), ("AVAILABILITY", "开放条件"),
        ("PARTS", "部件"), ("Back", "返回"), ("and", "与"),
        ("Open Field", "开阔地带"), ("Ambush", "伏击"), ("Final", "最终"), ("Last 突袭", "最后突袭"),
        ("Demolishman", "爆破者"), ("both Dumbums", "两只爆破狂人"), ("爆炸 Twins", "爆裂双子"),
        ("incoming infected", "来袭感染者"), ("remaining 电魔s", "剩余电魔"), ("THE ", ""),
        ("恶魔ic Trio", "恶魔三人组"), ("Cultists", "邪教徒"), ("Cult", "邪教团"),
        ("Spectral Lantern March", "幽灵提灯行军"), ("Restless 突击", "躁动冲锋"), ("Echo Volley", "回声齐射"),
        ("Hollow 巡逻", "空壳巡逻"), ("Lost Procession", "迷失队列"), ("Haunted Grounds", "闹鬼之地"),
        ("before brood overruns area", "，不要让巢群淹没这片区域"), ("Brood 围攻", "巢群围攻"),
        ("both Rockbreakers", "两只碎岩者"), ("feral + radiated", "凶暴与辐射感染者"),
        ("原始 Pack", "原始兽群"), ("岩石 Barrage", "岩石弹幕"), ("Elite", "精英"),
        ("both Singerie brothers together", "歌姬兄弟二人"), ("both 歌姬 brothers", "歌姬兄弟二人"),
        ("Brothers", "兄弟会"), (" alpha", "首领"), ("Zombie 熊", "僵尸熊"),
        ("Blue 契约", "蓝色契约"), ("Green 契约", "绿色契约"), ("Shrouded", "迷雾"),
        ("Bloody March", "血腥行军"), ("Shattered Parade", "破碎游行"), ("Night 防守", "夜间防守"),
        ("Double Break", "双重破坏"), ("幽灵 Court", "幽灵法庭"),
        ("Blood, Brood 与 Gallows", "鲜血、巢群与绞架"), ("荒野erness", "荒野"),
        ("Crouch", "潜伏者"), ("Slay", "击杀"), ("remaining", "剩余"), ("Phase", "阶段"),
        ("Apex Collapse", "巅峰崩塌"), ("Brood 与 Gallows", "巢群与绞架"),
        ("3 minions in last 突袭", "最终突袭中的3只仆从"), ("Elder Cataclysm", "远古浩劫"),
        ("Fivefold Cataclysm", "五重浩劫"), ("战锤守卫守卫队ian", "战锤守卫"),
        ("Kings of Broken Sky", "破碎天空诸王"), ("Last March of Damned", "诅咒者的最后行军"),
        ("Red Wedding", "血色婚礼"), ("Coven Below", "地下女巫集会"), ("False Apocalypse", "伪末日"),
        ("铁 Funeral", "钢铁葬礼"), ("女巫's Coven", "女巫集会"), ("女巫 尸群 Coven", "女巫集会尸潮"),
        ("PartyGirl", "派对女郎"), ("Hawaiian", "夏威夷感染者"), ("Boe", "博伊"), ("Steve", "史蒂夫"),
        ("Businessman", "商人感染者"), ("Burnt", "烧焦感染者"), ("Demolition", "爆破感染者"),
        ("Fungal 尸群 in Field", "在开阔地带抵御菌菇尸潮"), ("Bow", "弓手"),
        ("Deploy All-In-One Lure 与 survive every Boss pair", "放置全能诱饵并击败每一组Boss组合"),
        ("BossBoss", "Boss"), ("hard-挑战", "高难挑战"), ("extreme-挑战", "极限挑战"),
        ("随从 army", "仆从大军"), ("Boss army", "Boss大军"), ("Forward Position", "前沿阵地"),
        ("Clear 弓手 前沿阵地", "清除弓箭手前沿阵地"), ("Joint", "联合"), ("Break 弓手 Warb与", "击溃弓箭手战团"),
        ("Warb与", "战团"), ("Strongpoint", "坚固据点"), ("Last St与", "背水一战"),
        ("Combined Arms", "联合作战"), ("Phantom Mosaic", "幻影群像"), ("Stampede", "兽群冲锋"),
        ("Infernal Swarm", "炼狱虫群"), ("Gory Legion", "血腥军团"), ("Deadeye", "神射手"),
        ("超级-挑战", "超级挑战"), ("Spark 猎杀者", "电火花猎杀者"), ("Verdict 猎杀者", "裁决猎杀者"),
        ("Talons 猎杀者", "利爪猎杀者"), ("Specter 猎杀者", "幽灵猎杀者"), ("Anvil 猎杀者", "铁砧猎杀者"),
        ("Inferno 猎杀者", "炼狱猎杀者"), ("Frenzy 猎杀者", "狂乱猎杀者"), ("Tremor 猎杀者", "震颤猎杀者"),
        ("Primate 猎杀者", "灵长猎杀者"), ("Shriek 猎杀者", "尖啸猎杀者"), ("Grove 猎杀者", "林地猎杀者"),
        ("Gears 猎杀者", "齿轮猎杀者"), ("Curse 猎杀者", "诅咒猎杀者"), ("Spores 猎杀者", "孢子猎杀者"),
        ("Void 猎杀者", "虚空猎杀者"), ("Quiver 猎杀者", "箭袋猎杀者"),
        ("Demo 小队", "爆破小队"), ("末日领主 Gun", "末日领主枪手"),
    )
    for index in range(1, len(lines)):
        line = lines[index]; ending = "\r\n" if line.endswith("\r\n") else "\n" if line.endswith("\n") else ""; row = next(csv.reader([line.removesuffix(ending)]))
        if len(row) <= z: continue
        chinese = BOSS_LATE_EXACT.get(row[0], row[z]); updated = chinese
        for source, target in replacements: updated = updated.replace(source, target)
        updated = updated.replace("  ", " ").replace("击杀击杀", "击杀").replace("波波", "波")
        if updated == row[z]: continue
        row[z] = updated; output = io.StringIO(); csv.writer(output, lineterminator=ending, quoting=csv.QUOTE_MINIMAL).writerow(row); lines[index] = output.getvalue(); changed += 1
    with path.open("w", encoding="utf-8-sig", newline="") as handle: handle.write("".join(lines))
    print(f"03-AEC-AIO_BOSS_EXTREME_EDITION/Config/Localization.csv: cleaned {changed} mixed entries")


translate_boss_late_cleanup()


PROJECTZ_LATE_EXACT = {
    "armorAssassinGlovesImpDesc": "中甲\\n改良型刺客手套。悄无声息，却迅捷无比。提高狙击步枪的射速与装填速度，还可安装强化件来提高近战武器攻击速度。\\n\\n[DECEA3]全套奖励：[-]\\n可安装[FF6666]近战武器强化[-]（装在手套上）。提高狙击步枪造成的伤害。\\n\\n[DECEA3]修理需要改良型修理包。[-]",
    "BuffMysticAOEIncDesc": "天生的猎手，从不给猎物留下机会。它总会凭空出现并麻痹猎物，而现在你就是目标。\\n\\n[FFB800]特殊能力：[-]\\n在你伤到它或被它伤到之前，无法察觉这个小型Boss；只有雷达能保证发现它。若它没有受伤也未发动攻击，很快会再次隐去身形。潜行攻击必定使你麻痹，并额外造成[DECEA3]300%[-]伤害；现身后的攻击仍有[DECEA3]25%[-]概率使你麻痹。被发现后，秘法师移动速度提高[DECEA3]30%[-]，且自身免疫麻痹。\\n待在附近时，每秒还会额外损失少量抗压能力。\\n\\n即使明知它就在附近，恐惧也不会消散。",
    "BuffMummyPhysicalAOEIncDesc": "这种生物与任何已知敌人都不同——[DECEA3]实体形态的木乃伊。[-]武器很容易命中它，但从那双利爪来看，它切开猎物也像熟练厨师一样轻松。交战前务必三思。\\n\\n[FFB800]特殊能力：[-]\\nBoss会召唤一群变异圣甲虫。虫群会感染3米范围内的所有人，汲取沿途目标的生命力来恢复木乃伊生命值。木乃伊受伤时也会吸取附近玩家的生命，并且免疫麻痹。\\n\\n没人知道究竟怎样才能杀死它。若你还没找到答案——快逃！趁力量还留在你的身体里，而不是它的体内！",
    "BuffMummyGhostAOEIncDesc": "这种生物与任何已知敌人都不同——[DECEA3]实体形态的木乃伊。[-]武器很容易命中它，但从那双利爪来看，它切开猎物也像熟练厨师一样轻松。交战前务必三思。\\n\\n[FFB800]特殊能力：[-]\\nBoss会召唤一群变异圣甲虫。虫群会感染3米范围内的所有人，汲取沿途目标的生命力来恢复木乃伊生命值。木乃伊受伤时也会吸取附近玩家的生命，并且免疫麻痹。\\n\\n没人知道究竟怎样才能杀死它。若你还没找到答案——快逃！趁力量还留在你的身体里，而不是它的体内！",
    "modMeleeStunBatonRepulsorDesc": "将此改装件安装到电击棒上，蓄力命中时可将僵尸击飞。\\n\\n按住[DECEA3][ F ][-]键可开启或关闭。",
    "modGunCrippleEmDesc": "每次射击都有概率使双足目标的一条腿致残。\\n\\n[DECEA3]霰弹枪专家[-]技能可提高此改装件的品质。",
    "modMeleeParalizeImpDesc": "刀刃命中时会使目标中毒并陷入麻痹，在[DECEA3]5[-]秒内无法移动。\\n[DECEA3]麻痹对Boss和小型Boss无效。[-]\\n\\n[DECEA3]只能安装在带有刀刃的武器上。[-]",
    "modMeleeParalizeUniqueDesc": "强效毒素不仅会令目标中毒，还会施加强力麻痹，使其在[DECEA3]5[-]秒内无法正常移动或攻击。\\n这种毒素强到足以麻痹任何敌人。\\n\\n[DECEA3]只能安装在带有刀刃的武器上。[-]",
    "drugAntidoteDesc": "使你免疫中毒，并清除当前已有的中毒效果。",
    "resourcePoisonedNailDesc": "涂有强效毒素的钉子，命中后可使目标麻痹。可作为普通或改良型射钉枪的弹药。",
    "modFireproofProtection": "[FFB800]独特[-] 防火护甲改装件",
    "modFireproofProtectionDesc": "免疫Boss造成的燃烧与高温效果。\n\n[DECEA3]安装于胸甲。[-]",
    "modStabilityBoosterDesc": "免疫击倒效果。\n\n[DECEA3]安装于腿甲。[-]",
    "modTerrainResistDesc": "在各种恶劣地面上移动时提高防护能力。\n\n[DECEA3]安装于腿甲。[-]",
    "modTerrainResistUniqueDesc": "免疫恶劣地形造成的所有减速效果。\n\n[DECEA3]安装于腿甲。[-]",
    "modReliableWindingDesc": "免疫缴械效果。\n\n[DECEA3]安装于手甲。[-]",
    "buffMeleeCombistickDesc": "下一次蓄力攻击会触发组合长矛战技。若目标存活且并非Boss，它起身时会再次被击倒。该能力的冷却时间为[DECEA3]10[-]秒。",
    "buffArmorClassBonusName": "护甲职业加成",
    "buffNomadSetBonusTooltip": "游牧者全套奖励已激活",
    "buffstimClearGazeTooltip": "已激活[DECEA3]兴奋剂[-]“清明视野”",
    "perkStoneUpStoneRank8LongDesc": "敲、敲、敲……\n对方块造成的伤害以及采矿所得资源提高[DECEA3]80%[-]。",
    "MasterKitchenT2-8": "[DECEA3]烘焙：[-]“噢，糟糕糖球”",
    "questRangeT3_PistolDesc": "完成要求后，可从SMG-5或沙漠秃鹫中选择一件。",
    "ShippingCrateSavageCountryStorage": "[DECEA3]储物：[-]野蛮国度纸箱",
    "CallBurningFleshDesc": "激活召唤[8692FF]燃烧之肉[-]的任务。\n\n[DECEA3]僵尸专家达到3级后解锁。[-]",
    "CallAncientYetiDesc": "激活召唤[FFB800]远古雪人[-]的任务。\n\n[DECEA3]僵尸专家达到5级后解锁。[-]",
    "CallBearDaddyDesc": "激活召唤[8692FF]巨熊之父[-]的任务。\n\n[DECEA3]僵尸专家达到3级后解锁。[-]",
    "CallHotHarryDesc": "激活召唤[FFB800]烈焰哈里[-]的任务。\n\n[DECEA3]僵尸专家达到5级后解锁。[-]",
    "CallDevourerDesc": "激活召唤[8692FF]吞噬者[-]的任务。\n\n[DECEA3]僵尸专家达到3级后解锁。[-]",
    "CallNagainaDesc": "激活召唤[8692FF]娜迦蛇后[-]的任务。\n\n[DECEA3]僵尸专家达到3级后解锁。[-]",
    "CallCholeraDesc": "激活召唤[8692FF]霍乱[-]的任务。\n\n[DECEA3]僵尸专家达到3级后解锁。[-]",
    "CallMysticDesc": "激活召唤[8692FF]秘法师[-]的任务。\n\n[DECEA3]僵尸专家达到3级后解锁。[-]",
    "CallMummyDesc": "激活召唤[FFB800]木乃伊[-]的任务。\n\n[DECEA3]僵尸专家达到5级后解锁。[-]",
    "CallBitchDesc": "激活召唤[8692FF]悍妇[-]的任务。\n\n[DECEA3]僵尸专家达到3级后解锁。[-]",
    "Quest_WeaponKnuckleOffer": "“看起来没那么危险，对吧？那就让敌人到阴间再评价你。用拳套击杀一些[DECEA3]任意僵尸[-]。若你能完成，我会分你一些拳套零件，再送几本有用的杂志。”\n\n[DECEA3]——乔尔[-]",
    "bossBiba": "[FFB800]小型Boss[-] 比巴",
    "toolKitchenKnivesDesc": "厨房里少不了一套锋利的刀具，尤其是用[FFB800]乌鲁鲁[-]材料打磨过的精品。它们能避免切伤手指，并缩短烹饪时间。",
    "LightMeleeWeaponT4": "第4级：[FF6666]强化[-]改良型护甲",
    "MasterKitchenT2-3": "[DECEA3]烘焙：[-]“眼力糖”",
    "MasterKitchenT5": "[FFB800]大份：[-]午餐套餐",
    "prepar_exp_complite": "太好了！我会把这些装备交给斯卡夫。我想已经足够开始远征了。",
    "buffLSSsetIncDesc": "主动生命维持系统可提高你的核心属性：生命、耐力、食物与水上限均增加[DECEA3]50[-]。",
    "perkVeteranWastelandDesc": "[FFB800]击杀[-]Boss或小型Boss会使计数器减少[DECEA3]1[-]。\n\n[FFB800]受阻进度：{cvar(.blockVeteranWasteland:0)}[-][DECEA3]——惧怕僵尸的人无法成长。[-]",
    "questRewardClubImpBundleT4Desc": "一根[DECEA3]改良型[-]棍棒",
    "questRewardSpearCombistickT6Desc": "[FFB800]组合长矛[-]及其修理包",
    "questRewardAxeBarbarianT6Desc": "[FFB800]野蛮人战斧[-]及其修理包",
    "questRewardMachineGunBundleT6Desc": "[FFB800]斗牛犬机枪[-]、改良型弹药和修理包",
    "questRewardMachineGunBundleT7Desc": "随机一把[F0B18A]独特[-][FFB800]斗牛犬机枪[-]、[5AFF75]贫铀弹药[-]和修理包",
    "superElixirHordeExpertDesc": "不是你逃离尸潮，而是尸潮逃离你。\n获得以下增益：\n爷爷的学习灵药\n认知药\n强骨片\n\n所有增益的基础持续时间：[DECEA3]7分钟[-]\n[DECEA3]此灵药可在厨房炉灶上制作。[-]",
    "superElixirMeleeMasterDesc": "近战中的你坚不可摧。\n获得以下增益：\n爷爷的学习灵药\n骷髅粉碎者\n强骨片\n\n所有增益的基础持续时间：[DECEA3]7分钟[-]\n[DECEA3]此灵药可在厨房炉灶上制作。[-]",
    "perkForestTimerRank10LongDesc": "[DECEA3]森林奖励：[-]\n在森林中获得的经验提高[DECEA3]10%[-]，木材和黏土采集量提高[DECEA3]30%[-]，对所有僵尸造成的伤害提高[DECEA3]30%[-]。\n\n[DECEA3]通用额外能力：[-][FFB800]林务员[-]\n获得的蜂蜜数量翻倍，屠宰动物获得的资源提高[DECEA3]50%[-]。",
    "perkBurntTimerRank10LongDesc": "[DECEA3]焦土奖励：[-]\n在焦土生态区获得的经验提高[DECEA3]10%[-]，硝酸钾采集量提高[DECEA3]50%[-]，对所有僵尸造成的伤害提高[DECEA3]30%[-]，对小型Boss造成的伤害提高[DECEA3]30%[-]。\n\n[DECEA3]通用额外能力：[-][FFB800]火焰抗性[-]\n免疫小型Boss造成的燃烧与“地狱火”效果。",
    "perkDesertTimerRank10LongDesc": "[DECEA3]沙漠奖励：[-]\n在沙漠中获得的经验提高[DECEA3]10%[-]，铁矿和油页岩采集量提高[DECEA3]50%[-]，对所有僵尸造成的伤害提高[DECEA3]30%[-]，对小型Boss造成的伤害提高[DECEA3]30%[-]。\n\n[DECEA3]通用额外能力：[-][FFB800]第二阵风[-]\n耐力低于[DECEA3]30[-]时立即恢复全部耐力。冷却时间：[DECEA3]3分钟[-]。",
    "perkWinterTimerRank10LongDesc": "[DECEA3]雪原奖励：[-]\n在雪原中获得的经验提高[DECEA3]10%[-]，煤炭采集量提高[DECEA3]50%[-]，对所有僵尸造成的伤害提高[DECEA3]30%[-]，对Boss造成的伤害提高[DECEA3]15%[-]，对小型Boss造成的伤害提高[DECEA3]30%[-]。\n\n[DECEA3]通用额外能力：[-][FFB800]千锤百炼[-]\n受到的所有伤害降低[DECEA3]5%[-]，并免疫击倒。",
    "perkWastelandTimerRank10LongDesc": "[DECEA3]废土奖励：[-]\n在废土中获得的经验提高[DECEA3]10%[-]，铅矿采集量提高[DECEA3]50%[-]，对所有僵尸造成的伤害提高[DECEA3]30%[-]，对Boss造成的伤害提高[DECEA3]15%[-]，对小型Boss造成的伤害提高[DECEA3]30%[-]，辐射伤害抗性提高[DECEA3]30%[-]。\n\n[DECEA3]通用额外能力：[-][FFB800]辐射适应[-]\n抵抗一种辐射效果。辐射暴露超过[DECEA3]85%[-]时，将其降至[DECEA3]25%[-]。冷却时间：[DECEA3]5分钟[-]。",
    "perkTotalHuntRank25LongDesc": "[DECEA3]加成：[-]\n对僵尸造成的伤害、击杀僵尸获得的经验及战利品掉落概率均提高[DECEA3]25%[-]，角色游戏阶段提高[DECEA3]30%[-]。\n\n[DECEA3]额外能力：[-][FFB800]纯粹肾上腺素[-]\n生命低于[DECEA3]30[-]时立即恢复[DECEA3]150[-]点生命。冷却时间：[DECEA3]3分钟[-]。",
    "perkNightHuntRank25LongDesc": "[DECEA3]加成：[-]\n夜间击杀僵尸获得的经验和战利品品质提高[DECEA3]25%[-]，角色夜间游戏阶段提高[DECEA3]25%[-]，对僵尸的潜行伤害提高[DECEA3]150%[-]。\n\n[DECEA3]额外能力：[-][FFB800]潜行突袭[-]\n潜行攻击额外造成[DECEA3]150%[-]伤害。",
    "perkBullsEyeRank25LongDesc": "[DECEA3]加成：[-]\n爆头伤害提高[DECEA3]50%[-]，爆头击杀经验提高[DECEA3]25%[-]。\n\n[DECEA3]额外能力：[-][FFB800]漂亮收尾[-]\n爆头击杀敌人时有小概率获得额外技能点；近战击杀的触发概率更高。",
    "perkRangeHuntRank25LongDesc": "[DECEA3]加成：[-]\n远程武器伤害提高[DECEA3]25%[-]，耐久损耗降低[DECEA3]25%[-]。\n\n[DECEA3]额外能力：[-][FFB800]离我远点！[-]\n装填期间免疫伤害。",
    "perkHotHarryRank10LongDesc": "[DECEA3]加成：[-]\n对烈焰哈里造成的伤害提高[DECEA3]30%[-]，受到的对应伤害降低[DECEA3]25%[-]，获得的变异样本提高[DECEA3]30%[-]。\n\n[DECEA3]额外能力：[-][FFB800]克服烈焰哈里恐惧[-]\n不再受对应的恐惧类负面效果影响。",
    "perkMummyRank10LongDesc": "[DECEA3]加成：[-]\n对木乃伊造成的伤害提高[DECEA3]30%[-]，受到的对应伤害降低[DECEA3]25%[-]，获得的变异样本提高[DECEA3]30%[-]。\n\n[DECEA3]额外能力：[-][FFB800]克服木乃伊恐惧[-]\n不再受对应的恐惧类负面效果影响。",
    "perkAncientYetiRank10LongDesc": "[DECEA3]加成：[-]\n对远古雪人造成的伤害提高[DECEA3]30%[-]，受到的对应伤害降低[DECEA3]25%[-]，获得的变异样本提高[DECEA3]30%[-]。\n\n[DECEA3]额外能力：[-][FFB800]克服远古雪人恐惧[-]\n不再受对应的恐惧类负面效果影响。",
    "BuffShockerAOEIncDesc": "如果把受辐射的僵尸接入电网会怎样？显然，有人真的做了这个疯狂实验。如今它不断释放致命电流，哪怕只是靠近，也可能送命。\n\n[FFB800]特殊能力：[-]\n受到伤害后，电击者会进入狂暴状态：移动速度提高[DECEA3]50%[-]，攻击更加频繁，且每次攻击都会将你击倒；但它的伤害抗性会降低[DECEA3]30%[-]，这正是反击机会。\n它还会周期性进入完全防御状态，在周围制造危险的辐射区；处于该状态时免疫一切攻击。\n\n空气中充满静电，身体不由自主地抽搐。趁放电还没让心脏停跳，立即撤退！",
    "BuffVeteranAOEIncDesc": "它生前曾是精锐部队的一员。酒精和病毒摧毁了理智，却没能抹去战斗本能。别低估一个磨炼了几十年杀戮技巧的老兵——哪怕变成这副模样。战友之情甚至战胜了死亡，所以它绝不会独自作战。\n\n[FFB800]特殊能力：[-]\n靠近它非常危险：精准打击会将武器从你手中击落，同时使武器耐久损耗速度翻倍。\n它会周期性召集战友；效果持续期间，半径[DECEA3]20[-]米内的所有敌人移动能力提高[DECEA3]25%[-]，受到的伤害降低[DECEA3]50%[-]。与此同时，老兵自身的物理伤害抗性降低[DECEA3]25%[-]。\n待在附近时，每秒还会额外损失少量抗压能力。\n\n空气里尽是死亡与硝烟，耳边仿佛响起陌生的军令。趁武器还握在手里，快逃！",
    "BuffChiefAOEIncDesc": "变成僵尸可不是偷懒的正当理由，尤其是主管就在附近——每个办公室职员都明白这一点。\n\n[FFB800]特殊状态：[-]\n主管存活时，所有办公室感染者的移动速度提高[DECEA3]25%[-]。主管生命低于一半后会陷入狂怒，使下属的机动性翻倍。\n\n你的任务是进行一次临时裁员，而它就是第一个。",
    "perkGeniusDesc": "[FFB800]阅读[-]书籍、杂志或配方时，计数器减少[DECEA3]1[-]。\n\n[FFB800]受阻进度：{cvar(.blockGenius:0)}[-] [DECEA3]——患有痴呆时无法提升。[-]",
    "perkAthleticBuildDesc": "[FFB800]每秒[-]降低计数器：跑步时减少[DECEA3]1[-]，进行有氧训练时减少[DECEA3]4[-]，进行力量训练时减少[DECEA3]2[-]。普通跳跃或攻击时减少[DECEA3]2[-]，使用武器或工具发动蓄力攻击时减少[DECEA3]4[-]。\n\n[FFB800]受阻进度：{cvar(.blockAthleticBuild:0)}[-] [DECEA3]——拥有肥胖或脆骨时无法提升。[-]",
    "perkCourageDesc": "[FFB800]每秒[-]，若周围有超过7只僵尸，计数器减少[DECEA3]1[-]。若连续[DECEA3]15[-]秒未受到伤害，计数器的下降速度提高至[DECEA3]4倍[-]。\n\n[FFB800]受阻进度：{cvar(.blockCourage:0)}[-] [DECEA3]——拥有惊慌时无法提升。[-]",
    "perkLuckyGuyDesc": "[FFB800]击杀[-]任意僵尸都会使计数器减少[DECEA3]1[-]。\n\n[FFB800]受阻进度：{cvar(.blockLuckyGuy:0)}[-] [DECEA3]——拥有倒霉时无法提升。[-]",
    "perkJackOfAllTradesDesc": "[FFB800]屠宰[-]死去的动物或僵尸尸体时，每次击中都会使计数器减少[DECEA3]1[-]。\n\n[FFB800]受阻进度：{cvar(.blockJackOfAllTrades:0)}[-] [DECEA3]——拥有笨手笨脚时无法提升。[-]",
    "perkExperiencedHunterDesc": "[FFB800]击杀[-]——使用轻型近战武器爆头击杀任意僵尸，都会使计数器减少[DECEA3]1[-]。\n\n[FFB800]受阻进度：{cvar(.blockCrusher:0)}[-] [DECEA3]——拥有和平主义或笨手笨脚时无法提升。[-]",
    "perkCrusherDesc": "[FFB800]击杀[-]——使用重型近战武器爆头击杀任意僵尸，都会使计数器减少[DECEA3]1[-]。\n\n[FFB800]受阻进度：{cvar(.blockCrusher:0)}[-] [DECEA3]——拥有和平主义或僵尸恐惧时无法提升。[-]",
    "perkTeamPlayerDesc": "[FFB800]每秒[-]，只要身处队伍中，计数器就会减少[DECEA3]1[-]。\n\n[FFB800]受阻进度：{cvar(.blockTeamPlayer:0)}[-] [DECEA3]——拥有社交恐惧时无法提升。[-]",
    "perkFastAdaptationDesc": "并非每个人都能迅速适应恶劣天气与其他外界因素。\n\n拥有免疫力低下时无法提升。[DECEA3]受阻进度：{cvar(.blockFastAdaptation:0)}\n数值为1时受阻，为0时可以提升。[-]",
    "perkHardeningDesc": "良好的身体素质让你能从容忍受各种天气，更重要的是能更快恢复。\n\n拥有过敏体质时无法提升。[DECEA3]受阻进度：{cvar(.blockHardening:0)}\n数值为1时受阻，为0时可以提升。[-]",
}

_lockpick_flavor = {
    1: "你开始掌握门道了。", 2: "你已经成了锁匠，世上很少有锁能挡住你。",
    3: "你现在是专业的保险箱破解者。", 4: "现在的你连银行金库都敢下手。",
    5: "你已是世界级的保险箱破解者。",
}
for _rank in range(1, 6):
    _speed = 15 * _rank; _break = 10 * _rank; _safe = 10 * _rank
    _extra = "" if _rank == 1 else f"若在战利品中找到撬锁器，数量额外增加[DECEA3]{1 if _rank == 2 else 2 if _rank == 3 else 3}[-]。"
    PROJECTZ_LATE_EXACT[f"perkLockPickingRank{_rank}LongDesc"] = (
        f"{_lockpick_flavor[_rank]}撬锁速度提高[DECEA3]{_speed}%[-]，撬锁器损坏概率降低[DECEA3]{_break}%[-]。"
        f"{_extra}对保险箱造成的伤害提高[DECEA3]{_safe}%[-]。"
    )

# The rare-contract chain reuses the same concepts under item, ingredient,
# contract, quest and elixir keys.  Keep those visible names consistent.
_rare_reserves = {
    "Health": ("生命值上限", (("T1", "小型", "1"), ("T2", "中型", "5"), ("T3", "大型", "10"))),
    "Stamina": ("耐力上限", (("T1", "小型", "1"), ("T2", "中型", "5"), ("T3", "大型", "10"))),
}
for _kind, (_label, _tiers) in _rare_reserves.items():
    for _tier, _size, _amount in _tiers:
        _suffix = f"{_kind}{_tier}"
        PROJECTZ_LATE_EXACT[f"Elixir_{_suffix}"] = f"[DECEA3]{_size}灵药：[-]{_label}"
        PROJECTZ_LATE_EXACT[f"Elixir_{_suffix}Desc"] = f"使用后，永久提高[DECEA3]{_amount}[-]点{_label}。"
        PROJECTZ_LATE_EXACT[f"Ingredients_{_suffix}"] = f"[D1D5FF]材料：[-]{_size}{_label}"
        PROJECTZ_LATE_EXACT[f"RareCont_{_suffix}"] = f"[D1D5FF]稀有合约：[-]{_size}{_label}"
        PROJECTZ_LATE_EXACT[f"RareCont_{_suffix}Desc"] = f"启动[D1D5FF]稀有合约：[-]{_size}{_label}\n\n完成合约后，永久获得[DECEA3]{_amount}[-]点{_label}加成。"
        PROJECTZ_LATE_EXACT[f"Quest_RareCont_{_suffix}Name"] = f"[D1D5FF]稀有合约：[-]{_size}{_label}"

_rare_percent_traits = {
    "JumpStrength": ("跳跃力", "5%"), "Mobility": ("机动性", "1%"),
    "HiddenStrike": ("潜行伤害", "10%"),
}
for _suffix, (_label, _amount) in _rare_percent_traits.items():
    PROJECTZ_LATE_EXACT[f"Elixir_{_suffix}"] = f"[DECEA3]灵药：[-]{_label}"
    PROJECTZ_LATE_EXACT[f"Elixir_{_suffix}Desc"] = f"使用后，永久提高[DECEA3]{_amount}[-]的{_label}加成。"
    PROJECTZ_LATE_EXACT[f"Ingredients_{_suffix}"] = f"[D1D5FF]材料：[-]{_label}"
    PROJECTZ_LATE_EXACT[f"RareCont_{_suffix}"] = f"[D1D5FF]稀有合约：[-]{_label}"
    PROJECTZ_LATE_EXACT[f"RareCont_{_suffix}Desc"] = f"启动[D1D5FF]稀有合约：[-]{_label}\n\n完成合约后，永久获得[DECEA3]{_amount}[-]的{_label}加成。"
    PROJECTZ_LATE_EXACT[f"Quest_RareCont_{_suffix}Name"] = f"[D1D5FF]稀有合约：[-]{_label}"

_rare_damage_types = {
    "Forest": "森林中造成的伤害", "BurntForest": "焦土中造成的伤害",
    "Desert": "沙漠中造成的伤害", "Snow": "雪原中造成的伤害",
    "Wasteland": "废土中造成的伤害", "Rifle": "狙击步枪伤害",
    "MachineGun": "自动武器伤害", "Shotgun": "\u9730弹枪伤害", "Pistol": "手枪伤害",
    "MeleeLight": "轻型近战武器伤害", "MeleeHeavy": "重型近战武器伤害",
    "All": "所有武器伤害",
}
for _suffix, _label in _rare_damage_types.items():
    PROJECTZ_LATE_EXACT[f"Elixir_Damage_{_suffix}"] = f"[DECEA3]灵药：[-]{_label}"
    PROJECTZ_LATE_EXACT[f"Elixir_Damage_{_suffix}Desc"] = f"使用后，永久提高[DECEA3]1%[-]的{_label}加成。"
    PROJECTZ_LATE_EXACT[f"Ingredients_Damage_{_suffix}"] = f"[D1D5FF]材料：[-]{_label}"
    PROJECTZ_LATE_EXACT[f"RareCont_Damage_{_suffix}"] = f"[D1D5FF]稀有合约：[-]{_label}"
    PROJECTZ_LATE_EXACT[f"RareCont_Damage_{_suffix}Desc"] = f"启动[D1D5FF]稀有合约：[-]{_label}\n\n完成合约后，永久获得[DECEA3]1%[-]的{_label}加成。"
    PROJECTZ_LATE_EXACT[f"Quest_RareCont_Damage_{_suffix}Name"] = f"[D1D5FF]稀有合约：[-]{_label}"

_rare_weapon_offers = {
    "Rifle": ("沙漠", "狙击步枪", "狙击步枪"),
    "MachineGun": ("雪原", "自动武器", "自动武器"),
    "Shotgun": ("沙漠", "\u9730弹枪", "\u9730弹枪"),
    "Pistol": ("焦土", "手枪", "手枪"),
}
for _suffix, (_biome, _damage, _weapon) in _rare_weapon_offers.items():
    PROJECTZ_LATE_EXACT[f"Quest_RareCont_Damage_{_suffix}Offer"] = (
        f"«在{_biome}中杀死[DECEA3]小型Boss[-]并收集所需材料，我就能调制一种灵药，"
        f"使{_damage}造成的伤害提高[DECEA3]1%[-]。\n\n记住，必须使用{_weapon}猎杀它们才算数。»\n\n[DECEA3]——莱希[-]"
    )
PROJECTZ_LATE_EXACT["Quest_RareCont_Damage_AllOffer"] = "«在废土中杀死[DECEA3]小型Boss[-]并收集所需材料，我就能调制一种灵药，使所有武器造成的伤害提高[DECEA3]1%[-]。»\n\n[DECEA3]——莱希[-]"
for _tier, _count, _amount in (("T1", 5, 1), ("T2", 20, 5), ("T3", 35, 10)):
    PROJECTZ_LATE_EXACT[f"Quest_RareCont_Stamina{_tier}Offer"] = (
        f"«这些生物的耐力任谁都会羡慕。不过先说好：不准用枪。\n杀死[DECEA3]{_count}只恐狼[-]并收集所需材料，"
        f"我就能调制一种灵药，使你的耐力上限提高[DECEA3]{_amount}[-]点。\n\n记住，只能在废土中猎杀它们，而且只能用弓或弩击杀。»\n\n[DECEA3]——莱希[-]"
    )
PROJECTZ_LATE_EXACT["Quest_RareCont_HiddenStrikeOffer"] = "«乍看之下，这些生物令人作呕，但我们仍能从它们身上学到很多。不过先说好：不准用枪。\n杀死[DECEA3]15条蛇[-]并收集所需材料，我就能调制一种灵药，使潜行伤害提高[DECEA3]10%[-]。\n\n记住，只能在沙漠中猎杀它们，而且只能用弓或弩击杀。»\n\n[DECEA3]——莱希[-]"

_improved_armor_sets = {
    "Rogue": ("盗贼", {"Helmet": "兜帽", "Outfit": "服装", "Gloves": "手套", "Boots": "鞋"}),
    "Enforcer": ("执法者", {"Helmet": "太阳镜", "Outfit": "服装", "Gloves": "手套", "Boots": "鞋"}),
    "Ranger": ("游骑兵", {"Helmet": "头盔", "Outfit": "服装", "Gloves": "手套", "Boots": "靴"}),
    "Commando": ("突击队", {"Helmet": "头盔", "Outfit": "服装", "Gloves": "手套", "Boots": "靴"}),
    "Assassin": ("刺客", {"Helmet": "头盔", "Outfit": "服装", "Gloves": "手套", "Boots": "靴"}),
    "Gatherer": ("采集者", {"Helmet": "头盔", "Outfit": "服装", "Gloves": "手套", "Boots": "靴"}),
    "Nomad": ("游牧者", {"Helmet": "头盔", "Outfit": "服装", "Gloves": "手套", "Boots": "靴"}),
    "Nerd": ("书呆子", {"Helmet": "护目镜", "Outfit": "服装", "Gloves": "手套", "Boots": "靴"}),
    "Raider": ("掠夺者", {"Helmet": "头盔", "Outfit": "服装", "Gloves": "手套", "Boots": "靴"}),
}
for _set_key, (_set_name, _pieces) in _improved_armor_sets.items():
    for _piece_key, _piece_name in _pieces.items():
        PROJECTZ_LATE_EXACT[f"armor{_set_key}{_piece_key}Imp"] = f"{_set_name}{_piece_name} [8692FF]改良型[-]"
    PROJECTZ_LATE_EXACT[f"buff{_set_key}ImpSetBonus"] = f"[8692FF]{_set_name}[-]"
    PROJECTZ_LATE_EXACT[f"buff{_set_key}ImpSetBonusTooltip"] = f"已获得改良型{_set_name}护甲的全套奖励。"

_imp_set_damage = {
    "Rogue": ("弓弩伤害", "armorRogueImpFSBDisplay"),
    "Enforcer": ("SMG与手枪伤害", "armorEnforcerImpFSBDisplay"),
    "Ranger": ("远程武器伤害", "armorRangerImpFSBDisplay"),
    "Commando": ("自动武器伤害", "armorCommandoImpFSBDisplay"),
    "Assassin": ("狙击步枪伤害", "armorAssassinImpFSBDisplay"),
    "Nomad": ("\u9730弹枪伤害", "armorNomadImpFSBDisplay"),
}
for _set_key, (_damage_label, _damage_cvar) in _imp_set_damage.items():
    _set_name = _improved_armor_sets[_set_key][0]
    PROJECTZ_LATE_EXACT[f"buff{_set_key}ImpSetBonusDesc"] = (
        f"你已装备全套[8692FF]{_set_name}[-]改良型护甲。\n{_damage_label}提高[DECEA3]{{cvar(.{_damage_cvar}:0)}}%[-]。"
        f"强化近战武器时：伤害提高[DECEA3]{{cvar(.armorEnhancedDamage{_set_key}ImpFSBDisplay:0)}}%[-]，"
        f"攻击速度提高[DECEA3]{{cvar(.armorEnhancedSpeed{_set_key}ImpFSBDisplay:0)}}%[-]，"
        f"耐力消耗降低[DECEA3]{{cvar(.armorEnhancedStamina{_set_key}ImpFSBDisplay:0)}}%[-]。"
    )
PROJECTZ_LATE_EXACT["buffGathererImpSetBonusDesc"] = "你已装备全套[8692FF]采集者[-]改良型护甲。\n采集到的所有资源数量提高[DECEA3]{cvar(.armorGathererImpFSBDisplay:0)}%[-]。\n\n手持建筑或采矿工具时，受到的伤害降低[DECEA3]15%[-]。"
PROJECTZ_LATE_EXACT["buffNerdImpSetBonusDesc"] = "你已装备全套[8692FF]书呆子[-]改良型护甲。\n炮塔与电击棒伤害提高[DECEA3]{cvar(.armorNerdImpFSBDisplay:0)}%[-]，可额外部署[DECEA3]1[-]座炮塔。\n\n[DECEA3]该套装无法安装《强化》改装件。[-]"
PROJECTZ_LATE_EXACT["buffRaiderImpSetBonusDesc"] = "你已装备全套[8692FF]掠夺者[-]改良型护甲。\n近战武器伤害提高[DECEA3]{cvar(.armorMeleeRaiderImpFSBDisplay:0)}%[-]，远程武器伤害提高[DECEA3]{cvar(.armorRangeDamageRaiderImpFSBDisplay:0)}%[-]。"

PROJECTZ_LATE_EXACT.update({
    "modEnhancedWood": "[FF6666]«强化»[-] 备用伐木器",
    "modEnhancedMetal": "[FF6666]«强化»[-] 备用破铁器",
    "modEnhancedStone": "[FF6666]«强化»[-] 备用破壁器",
    "modEnhancedClay": "[FF6666]«强化»[-] 备用挖土器",
    "modEnhancedSpear": "[FF6666]«强化»[-] 长矛",
    "modEnhancedClub": "[FF6666]«强化»[-] 棍棒",
    "modEnhancedSledgehammer": "[FF6666]«强化»[-] 大锤",
    "modEnhancedKnuckles": "[FF6666]«强化»[-] 拳套",
    "modEnhancedKnife": "[FF6666]«强化»[-] 刀具与砍刀",
    "modEnhancedStunBaton": "[FF6666]«强化»[-] 电击棒",
    "modEnhancedMechanisms": "[DECEA3]改良型[-] 强化机械组件改装件",
    "modEnhancedMechanismsUnique": "[FFB800]独特[-] 强化机械组件改装件",
    "MasterVehicleT1-3": "第1级：[DECEA3]改良型[-]载具护甲改装件",
    "MasterVehicleT1-7": "第1级：[FFB800]独特[-]载具凶悍护甲改装件",
    "modVehicleArmorImp": "[DECEA3]改良型[-]载具护甲改装件",
    "modVehicleArmorUnique": "[FFB800]独特[-]载具残暴护甲改装件",
    "MBoosterArmor": "杂志助推器 [5AFF75]《护甲强化》[-]",
    "buffMBoosterArmorName": "杂志助推器 [5AFF75]《护甲强化》[-]",
    "GunRackSmallStorage": "[DECEA3]储物：[-]小型护甲架",
    "resourceArmorCraftingKitImp": "[DECEA3]改良型[-]护甲制作套件",
    "MasterArmorMods": "[DECEA3]改良型[-]改装件",
    "MasterArmorModsUniq": "[FFB800]独特[-]改装件",
    "MasterArmorSetMods": "[DECEA3]套装[-]改装件",
    "armorModsT5": "高级改装件",
    "ImpModsBundle": "[DECEA3]改良型[-]改装件",
    "ImpModsMeleeBundle": "[DECEA3]改良型[-]近战改装件",
    "ImpModsRangeBundle": "[DECEA3]改良型[-]远程改装件",
    "UniqueToolsModsBundle": "[FFB800]独特[-]工具改装件",
    "UniqueModsBundle": "[FFB800]独特[-]改装件",
    "UniqueModsMeleeBundle": "[FFB800]独特[-]近战改装件",
    "BetterWeaponMeleeImpModsName": "[DECEA3]获得[-]改良型近战改装件",
    "BetterWeaponRangeImpModsName": "[DECEA3]获得[-]改良型远程改装件",
    "BetterWeaponMeleeUniqueModsName": "[DECEA3]获得[-]独特近战改装件",
    "BetterWeaponRangeUniqueModsName": "[DECEA3]获得[-]独特远程改装件",
    "quest_craftmod": "制作改装件",
    "MagazineBundleDesc": "包内含有[DECEA3]10[-]本杂志。",
    "resourceRepairKitImpDesc": "改良型武器与工具的属性高于基础型号，但修理起来也更困难。\n\n用于修理任意改良型武器或工具，恢复[DECEA3]2000[-]点耐久度。\n\n需要技能：[DECEA3]高级工程 等级3[-]",
    "resourceArmorCraftingKitImpDesc": "用于制造新一代护甲的专用零件与工具，可强化护甲属性。\n\n[DECEA3]用于制作实验型与改良型护甲。[-]",
    "workbenchImpDesc": "普通工作台几乎什么都能做，只是耗时较长。改良型工作台可安装加速制作的改装件，还能制造更复杂、更独特的物品。",
    "autoTurretImp_9mmDesc": "使用9毫米弹药的改良型自动炮塔，各项射击性能均得到提升。\n\n敌人不会喜欢这次升级。",
    "autoTurretImp_44Desc": "使用.44口径弹药的改良型自动炮塔，各项射击性能均得到提升。\n\n敌人不会喜欢这次升级。",
    "autoTurretImp_762mmDesc": "使用7.62毫米弹药的改良型自动炮塔，各项射击性能均得到提升。\n\n敌人不会喜欢这次升级。",
    "shotgunTurretImpDesc": "改良型\u9730弹枪炮塔，各项射击性能均得到提升。\n\n敌人可不会喜欢这些改动。",
    "modEnhancedWoodDesc": "采集者护甲专用《强化》改装件，安装于胸甲。\n\n木材采集量提高[DECEA3]25%[-]。",
    "modEnhancedMetalDesc": "采集者护甲专用《强化》改装件，安装于胸甲。\n\n铁与铅的采集量提高[DECEA3]25%[-]。",
    "modEnhancedStoneDesc": "采集者护甲专用《强化》改装件，安装于胸甲。\n\n油页岩、煤与硝酸钾的采集量提高[DECEA3]25%[-]。",
    "modEnhancedClayDesc": "采集者护甲专用《强化》改装件，安装于胸甲。\n\n黏土采集量提高[DECEA3]25%[-]。",
})
_magazine_bundle_names = {
    "harvestingTools": "工具文摘", "repairTools": "巧手天地", "salvageTools": "快乐拆解",
    "sledgehammers": "重锤出击", "workstation": "锻造前进", "bows": "弓箭猎手",
    "spears": "尖锐长棍", "blades": "刀锋达人", "clubs": "重击高手",
    "handguns": "手枪杂志", "shotguns": "\u9730弹枪周刊", "rifles": "步枪世界",
    "machineGuns": "战术战争", "explosives": "爆炸物杂志", "armor": "护甲强化",
    "medical": "医学杂志", "knuckles": "怒火铁拳", "food": "家庭烹饪周刊",
    "seed": "南方农业", "robotics": "科技星球", "vehicles": "载具历险",
    "electrician": "电路入门", "traps": "电击陷阱",
}
for _prefix, _title in _magazine_bundle_names.items():
    PROJECTZ_LATE_EXACT[f"{_prefix}SkillMagazineBundle"] = f"[DECEA3]杂志包：[-]《{_title}》"

_improved_armor_bodies = {
    "RogueHelmet": "改良型盗贼兜帽。真正的盗贼总能辨认出值钱的战利品。从战利品中获得的纸币与公爵币更多，同时提高战斗能力。",
    "RogueOutfit": "改良型盗贼服装。像野兽般快速愈合伤口，提高重伤恢复速度与负重能力。",
    "RogueGloves": "改良型盗贼手套。箭矢与弩矢不会在关键时刻脱手，可提高弓弩的装填速度，还能安装强化件来提高近战武器攻击速度。",
    "RogueBoots": "改良型盗贼鞋。近乎忍者般轻盈：潜行时噪声更低、移动更快，近战攻击时也更容易保持平衡。",
    "EnforcerHelmet": "改良型执法者太阳镜。戴上它，总能为商品谈到好价钱，提高售价与战斗能力。",
    "EnforcerOutfit": "改良型执法者服装。时尚又轻便的材料让你能承受猛烈冲击，提高重伤抗性与负重能力。",
    "EnforcerGloves": "改良型执法者手套。颇有西部风格，可提高冲锋枪与手枪的装填速度，还能安装强化件来提高近战武器攻击速度。",
    "EnforcerBoots": "改良型执法者鞋。格外耐穿，提高耐力上限，近战攻击时也更容易保持平衡。",
    "RangerHelmet": "改良型游骑兵头盔。做工精良的帽子让游骑兵在谈判中更具气势，提高交易收益与战斗能力。",
    "RangerOutfit": "改良型游骑兵服装。大自然锻炼的不只是意志，还有身体；提高生命值上限与负重能力。",
    "RangerGloves": "改良型游骑兵手套。舒适的设计让你能迅速操作各种武器，提高装填速度，还能安装强化件来提高近战武器攻击速度。",
    "RangerBoots": "改良型游骑兵靴。耐力是荒野生存的关键；提高耐力上限，近战攻击时也更容易保持平衡。",
    "CommandoHelmet": "改良型突击队头盔。提高生命恢复速度与战斗能力。",
    "CommandoOutfit": "改良型突击队服装。真正的突击队员面对再重的打击也不会退缩；提高重伤恢复速度。",
    "CommandoGloves": "改良型突击队手套。熟练的射手能迅速拆装武器，并将换弹动作练到极致。还可安装强化件来提高近战武器攻击速度。",
    "CommandoBoots": "改良型突击队靴。专为最艰难的障碍训练而设计，提高安全坠落高度，近战攻击时也更容易保持平衡。",
    "AssassinHelmet": "改良型刺客头盔。目标甚至不会察觉你的存在；提高潜行状态下的伤害与战斗能力。",
    "AssassinOutfit": "改良型刺客服装。不被看见，不被听见，也就不会受伤。提高潜行效率与负重能力。",
    "AssassinGloves": "改良型刺客手套。悄无声息，却迅捷无比。提高狙击步枪的射速与装填速度，还可安装强化件来提高近战武器攻击速度。",
    "AssassinBoots": "改良型刺客靴。沉默而致命：潜行奔跑时不会发出声音，近战攻击时也更容易保持平衡。",
    "GathererHelmet": "改良型采集者头盔。多功能设计在采矿与建筑时都舒适安全，并提高从这两类工作中获得的经验。",
    "GathererOutfit": "改良型采集者服装。坚固舒适，既能抵御落石也能挡住入侵者；宽大的口袋足以装下采矿与建筑所需的各种用品。",
    "GathererGloves": "改良型采集者手套。掌面涂层让工具更难脱手，提高方块伤害，并降低修理时的工具耐久损耗。",
    "GathererBoots": "改良型采集者靴。专为繁重劳作设计，提高安全坠落高度与耐力上限。",
    "NomadHelmet": "改良型游牧者头盔。能抵御废土的恶劣环境，保障长途旅行；降低食物与水的消耗，同时提高战斗能力。",
    "NomadOutfit": "改良型游牧者服装。旅途中任何事都可能发生，关键是快速恢复并继续前进。提高重伤恢复速度与负重能力。",
    "NomadGloves": "改良型游牧者手套。废土上的空屋从不是真的空无一人。提高\u9730弹枪的射速与装填速度，还能安装强化件来提高近战武器攻击速度。",
    "NomadBoots": "改良型游牧者靴。翻山越岭时最好别往下看；提高安全坠落高度，近战攻击时也更容易保持平衡。",
    "NerdHelmet": "改良型书呆子护目镜。让佩戴者学得更快，并提高陷阱击杀所获得的经验。",
    "NerdOutfit": "改良型书呆子服装。活到老，学到老。提高阅读杂志时获得额外技能点的概率，以及从战利品中发现书籍、杂志与配方的概率。",
    "NerdGloves": "改良型书呆子手套。提高炮塔装填速度与电击棒攻击速度。",
    "NerdBoots": "改良型书呆子靴。翻山越岭时最好别往下看；提高安全坠落高度与奔跑速度。",
    "RaiderHelmet": "改良型掠夺者头盔。提高所受治疗的生命恢复速度，并增强近战战斗能力。",
    "RaiderOutfit": "改良型掠夺者服装。与僵尸近身肉搏难免受伤，关键是快速恢复并继续前进。提高重伤恢复速度与生命值上限。",
    "RaiderGloves": "改良型掠夺者手套。无论什么武器都能轻松驾驭，提高武器装填速度，并显著提高近战攻击速度。",
    "RaiderBoots": "改良型掠夺者靴。翻山越岭时最好别往下看；提高安全坠落高度，近战攻击时也更容易保持平衡。",
}
_armor_classes = {"Rogue": "轻甲", "Enforcer": "轻甲", "Ranger": "中甲", "Commando": "中甲", "Assassin": "中甲", "Gatherer": "重甲", "Nomad": "重甲", "Nerd": "重甲", "Raider": "重甲"}
_armor_bonus = {
    "Rogue": "可安装[FF6666]近战武器强化[-]（安装于手套）。提高弓弩伤害。",
    "Enforcer": "可安装[FF6666]近战武器强化[-]（安装于手套）。提高枪手类技能的伤害。",
    "Ranger": "可安装[FF6666]近战武器强化[-]（安装于手套）。提高远程武器伤害。",
    "Commando": "可安装[FF6666]近战武器强化[-]（安装于手套）。提高自动武器伤害。",
    "Assassin": "可安装[FF6666]近战武器强化[-]（安装于手套）。提高狙击步枪伤害。",
    "Gatherer": "可安装[FF6666]强化[-]（安装于胸甲）。提高采集资源数量，并降低手持采矿或建筑工具时受到的伤害。",
    "Nomad": "可安装[FF6666]近战武器强化[-]（安装于手套）。提高\u9730弹枪伤害。",
    "Nerd": "提高[DECEA3]智力[-]战斗技能的伤害，可额外部署[DECEA3]1[-]座炮塔。\n\n[DECEA3]该套装无法安装《强化》改装件。[-]",
    "Raider": "显著提高近战伤害，同时提高所有远程武器伤害。\n\n[DECEA3]该套装无法安装《强化》改装件。[-]",
}
for _stem, _body in _improved_armor_bodies.items():
    _set_key = next(_name for _name in _armor_classes if _stem.startswith(_name))
    PROJECTZ_LATE_EXACT[f"armor{_stem}ImpDesc"] = (
        f"{_armor_classes[_set_key]}\n{_body}\n\n[DECEA3]全套奖励：[-]\n{_armor_bonus[_set_key]}\n\n[DECEA3]修理需要改良型修理包。[-]"
    )

_weak_immunity_ranks = {
    1: ("免疫力略有下降", 30, "10%", 4),
    2: ("免疫力低下", 60, "25%", 8),
    3: ("几乎没有免疫力", 90, "50%", 12),
}
for _rank, (_flavor, _safe, _penalty, _hours) in _weak_immunity_ranks.items():
    PROJECTZ_LATE_EXACT[f"perkWeakImmunityRank{_rank}LongDesc"] = (
        f"你的{_flavor}。各生态区的安全停留时间缩短[DECEA3]{_safe}[-]秒，防护冰沙的持续时间降低[DECEA3]{_penalty}[-]，"
        f"感染或中毒的概率提高[DECEA3]{_penalty}[-]。\n\n要恢复免疫力，必须累计强化免疫[DECEA3]{_hours}小时[-]。"
        f"剩余：[DECEA3]{{cvar(.TimerWeakImmunity{_rank}:0)}}秒。[-]\n[DECEA3]在当前生态区受到防护冰沙影响、正从感染中恢复，或受到维生素影响时，每秒都会强化免疫力。"
        "\n完成强化后，该负面特质降低1级。[-]"
    )

_fast_adaptation_ranks = {
    1: ("更快", "50%", "25%", 4), 2: ("快得多", "100%", "50%", 8),
    3: ("几乎能立即", "200%", "75%", 12),
}
for _rank, (_speed, _smoothie, _radiation, _hours) in _fast_adaptation_ranks.items():
    PROJECTZ_LATE_EXACT[f"perkFastAdaptationRank{_rank}LongDesc"] = (
        f"你{_speed}适应危险环境。在危险生态区内可安全停留更久，冰沙效果的持续时间提高[DECEA3]{_smoothie}[-]，"
        f"受到的活性辐射伤害降低[DECEA3]{_radiation}[-]。\n\n要提升该特质，必须累计强化免疫[DECEA3]{_hours}小时[-]。"
        f"剩余：[DECEA3]{{cvar(.TimerFastAdaptation{_rank - 1}:0)}}秒。[-]\n[DECEA3]在当前生态区受到防护冰沙影响时，每秒都会强化免疫力。"
        "\n完成强化后，该特质提高1级。[-]"
    )

_hardening_ranks = {
    1: ("你已能较好地承受恶劣天气。", "25%", 2),
    2: ("恶劣天气已无法让你畏惧。", "50%", 4),
    3: ("你的身体已千锤百炼，任何天气都无法吓倒你。", "75%", 6),
}
for _rank, (_flavor, _reduction, _hours) in _hardening_ranks.items():
    PROJECTZ_LATE_EXACT[f"perkHardeningRank{_rank}LongDesc"] = (
        f"{_flavor}负面环境效果的累积速度降低[DECEA3]{_reduction}[-]，一氧化碳窒息、过热与冻伤的恢复速度提高。\n\n"
        f"要提升该特质，必须累计锻炼身体[DECEA3]{_hours}小时[-]。剩余：[DECEA3]{{cvar(.TimerHardening{_rank - 1}:0)}}秒。[-]\n"
        "[DECEA3]受到窒息、过热或冻伤影响时，每秒都会锻炼身体。\n完成锻炼后，该特质提高1级。[-]"
    )

_tireless_flavor = {
    1: "帮忙处理繁重家务对你来说轻而易举，就算对手是肥胖女僵尸也一样。",
    2: "只要有毅力，就没有疲劳的立足之地。",
    3: "你甚至能决定第二、第三和第四口气何时接上。",
    4: "再艰苦的工作对你来说也是小菜一碟。",
    5: "你像钢铁一样坚韧，不知疲倦为何物。",
}
for _rank in range(1, 6):
    PROJECTZ_LATE_EXACT[f"perkTirelessnessRank{_rank}LongDesc"] = (
        f"{_tireless_flavor[_rank]}使用近战武器或工具时的耐力消耗降低[DECEA3]{4 * _rank}%[-]，"
        f"休息时的精力储备恢复速度提高[DECEA3]{20 * _rank}%[-]。"
    )

for _rank, (_move, _reserve, _stamina, _flavor) in enumerate((
    ("7.5%", "15%", 10, "你拥有锻炼良好的体质。"),
    ("15%", "30%", 25, "你的体质已相当出色。"),
    ("25%", "50%", 50, "你拥有近乎完美的体魄。"),
), 1):
    PROJECTZ_LATE_EXACT[f"perkAthleticBuildRank{_rank}LongDesc"] = (
        f"{_flavor}未超重时，机动性提高[DECEA3]{_move}[-]；精力储备与疲劳时的恢复速度提高[DECEA3]{_reserve}[-]；"
        f"耐力上限提高[DECEA3]{_stamina}[-]点。\n\n[FFB800]剩余：{{cvar(.TimerAthleticBuild{_rank - 1}:0)}}[-]"
    )

for _rank, (_damage, _slots, _health, _flavor) in enumerate((
    (10, 3, 10, "你的肌肉十分强壮。"), (20, 6, 20, "你已练出惊人的肌肉。"),
    (30, 9, 30, "你就是奥林匹亚先生。"),
), 1):
    PROJECTZ_LATE_EXACT[f"perkBodybuilderRank{_rank}LongDesc"] = (
        f"{_flavor}对方块和近战敌人造成的伤害提高[DECEA3]{_damage}%[-]，可无惩罚多携带[DECEA3]{_slots}[-]格物品，"
        f"生命值上限提高[DECEA3]{_health}[-]点。\n\n[FFB800]剩余：{{cvar(.TimerBodybuilder{_rank - 1}:0)}}[-]"
    )

_courage_values = (("10%", "5%", "7.5%", "10%"), ("25%", "7.5%", "12%", "15%"), ("50%", "10%", "16%", "25%"))
_courage_flavor = ("被僵尸包围时，你不会惊慌。", "被僵尸包围时，你能鼓起勇气。", "被僵尸包围时，你会展现出非凡的勇气。")
for _rank, (_stress, _low, _mid, _high) in enumerate(_courage_values, 1):
    PROJECTZ_LATE_EXACT[f"perkCourageRank{_rank}LongDesc"] = (
        f"{_courage_flavor[_rank - 1]}被僵尸包围时的抗压能力消耗降低[DECEA3]{_stress}[-]。对僵尸造成的伤害会随周围数量提高："
        f"少于4只时提高[DECEA3]{_low}[-]，4至10只时提高[DECEA3]{_mid}[-]，超过10只时提高[DECEA3]{_high}[-]。"
        f"\n\n[FFB800]剩余：{{cvar(.TimerCourage{_rank - 1}:0)}}[-]"
    )

for _rank, (_xp, _points, _flavor) in enumerate(((10, 5, "你思维敏锐。"), (20, 10, "你吸收知识就像海绵吸水。"), (30, 15, "你拥有天才般的头脑。")), 1):
    PROJECTZ_LATE_EXACT[f"perkGeniusRank{_rank}LongDesc"] = (
        f"{_flavor}获得的所有经验提高[DECEA3]{_xp}%[-]，阅读杂志时获得额外技能点的概率提高[DECEA3]{_points}%[-]。"
        f"\n\n[FFB800]剩余：{{cvar(.BooksReadBetter{_rank - 1}:0)}}[-]"
    )

for _rank, (_quality, _drop, _flavor) in enumerate(((5, 5, "好运正站在你这边。"), (10, 10, "你已牢牢抓住好运的尾巴。"), (15, 15, "你简直是世上最幸运的人。")), 1):
    PROJECTZ_LATE_EXACT[f"perkLuckyGuyRank{_rank}LongDesc"] = (
        f"{_flavor}战利品品质提高[DECEA3]{_quality}%[-]，击杀僵尸时掉落战利品的概率提高[DECEA3]{_drop}%[-]。"
        f"\n\n[FFB800]剩余：{{cvar(.CounterLuckyGuy{_rank - 1}:0)}}[-]"
    )

for _rank, (_craft, _samples, _flavor) in enumerate(((5, 10, "需要精细操作的工作都难不倒你。"), (10, 20, "你能快速高效地完成任何精细工作。"), (15, 40, "你的双手价值千金。")), 1):
    PROJECTZ_LATE_EXACT[f"perkJackOfAllTradesRank{_rank}LongDesc"] = (
        f"{_flavor}物品制作速度提高[DECEA3]{_craft}%[-]，采集到的变异样本数量提高[DECEA3]{_samples}%[-]。"
        f"\n\n[FFB800]剩余：{{cvar(.CounterJackOfAllTrades{_rank - 1}:0)}}[-]"
    )

for _rank, (_speed, _damage, _dismember, _flavor) in enumerate((("7.5%", 10, 5, "你曾有过猎杀经验。"), ("15%", 25, 15, "猎杀是你最喜欢的消遣。"), ("30%", 50, 30, "你已是经验丰富的猎手。")), 1):
    PROJECTZ_LATE_EXACT[f"perkExperiencedHunterRank{_rank}LongDesc"] = (
        f"{_flavor}轻型近战武器的攻击速度提高[DECEA3]{_speed}[-]，伤害提高[DECEA3]{_damage}%[-]，肂解概率提高[DECEA3]{_dismember}%[-]。"
        f"\n\n[FFB800]剩余：{{cvar(.CounterExperiencedHunter{_rank - 1}:0)}}[-]"
    )

for _rank, (_speed, _damage, _knockdown, _flavor) in enumerate((("7.5%", 10, 15, "你喜欢用重家伙砸烂东西。"), ("20%", 25, 30, "重型武器是你最好的朋友。"), ("30%", 50, 50, "你是名副其实的粉碎者。")), 1):
    PROJECTZ_LATE_EXACT[f"perkCrusherRank{_rank}LongDesc"] = (
        f"{_flavor}重型近战武器的攻击速度提高[DECEA3]{_speed}[-]，伤害提高[DECEA3]{_damage}%[-]，击中时击倒敌人的概率提高[DECEA3]{_knockdown}%[-]。"
        f"\n\n[FFB800]剩余：{{cvar(.CounterCrusher{_rank - 1}:0)}}[-]"
    )

for _rank, (_stress, _flavor) in enumerate(((10, "危急时刻，你会尽力保持清醒。"), (25, "危急时刻，你已能保持清醒。"), (50, "你拥有冷静的头脑。")), 1):
    PROJECTZ_LATE_EXACT[f"perkFearlessRank{_rank}LongDesc"] = (
        f"{_flavor}抗压能力的消耗速度降低[DECEA3]{_stress}%[-]，恢复速度提高。"
        f"\n\n[FFB800]剩余：{{cvar(.TimerFearless{_rank - 1}:0)}}[-]"
    )

for _rank, (_stress, _damage, _harvest, _flavor) in enumerate(((20, 5, 10, "你害怕动物。"), (50, 15, 25, "你非常害怕动物。"), (100, 25, 50, "你对动物有致命般的恐惧。")), 1):
    PROJECTZ_LATE_EXACT[f"perkFearAnimalsRank{_rank}LongDesc"] = (
        f"{_flavor}受到动物伤害时，额外损失[DECEA3]{_stress}%[-]的抗压能力；对所有动物造成的伤害降低[DECEA3]{_damage}%[-]；"
        f"屠宰动物尸体所得资源减少[DECEA3]{_harvest}%[-]。\n\n[FFB800]剩余：{{cvar(.CounterFearAnimals{_rank}:0)}}[-]"
    )

for _rank, (_damage, _reloads, _flavor) in enumerate(((5, (4, 8, 12), "你知道如何在废土求生。"), (15, (8, 16, 24), "你是废土生存专家。"), (30, (12, 24, 36), "你已是身经百战的废土老兵。")), 1):
    PROJECTZ_LATE_EXACT[f"perkVeteranWastelandRank{_rank}LongDesc"] = (
        f"{_flavor}身处废土时，造成的伤害提高[DECEA3]{_damage}%[-]。武器装填速度会随周围僵尸数量提高："
        f"少于4只时提高[DECEA3]{_reloads[0]}%[-]，4至10只时提高[DECEA3]{_reloads[1]}%[-]，超过10只时提高[DECEA3]{_reloads[2]}%[-]。"
        f"\n\n[FFB800]剩余：{{cvar(.CounterVeteranWasteland{_rank - 1}:0)}}[-]"
    )

for _rank, (_speed, _flavor) in enumerate(((15, "你是熟练的驾驶员。"), (25, "你是专业驾驶员。"), (40, "你就是真正的舒马赫。")), 1):
    PROJECTZ_LATE_EXACT[f"perkSchumacherRank{_rank}LongDesc"] = f"{_flavor}所有载具的速度提高[DECEA3]{_speed}%[-]。\n\n[FFB800]剩余：{{cvar(.TimerSchumacher{_rank - 1}:0)}}[-]"

for _rank, (_bonus, _flavor) in enumerate(((5, "你喜欢集体活动。"), (10, "你懂得如何团队协作。"), (25, "你是真正的团队玩家。")), 1):
    PROJECTZ_LATE_EXACT[f"perkTeamPlayerRank{_rank}LongDesc"] = (
        f"{_flavor}所有其他队伍成员的战利品品质与经验收益提高[DECEA3]{_bonus}%[-]，但你自己不享受该加成。"
        f"\n\n[FFB800]剩余：{{cvar(.TimerTeamPlayer{_rank - 1}:0)}}[-]"
    )

for _rank, (_move, _stamina, _jump, _flavor) in enumerate((("7.5%", 15, 10, "你的体重略微超标。"), ("15%", 30, 20, "你已相当肥胖。"), ("25%", 50, 30, "你是个名副其实的大胖子。")), 1):
    PROJECTZ_LATE_EXACT[f"perkObesityRank{_rank}LongDesc"] = (
        f"{_flavor}消化食物时，机动性降低[DECEA3]{_move}[-]，耐力消耗提高[DECEA3]{_stamina}%[-]；跳跃高度降低[DECEA3]{_jump}%[-]，"
        f"体力活动时的饱食度消耗速度也会提高。\n\n[FFB800]剩余：{{cvar(.TimerObesity{_rank}:0)}}[-]"
    )

for _rank, (_stamina, _water, _move, _flavor) in enumerate(((10, 10, "7.5%", "你的体重过轻。"), (25, 20, "15%", "你的体重严重不足。"), (50, 30, "25%", "你看起来就像包着皮的骷髅。")), 1):
    PROJECTZ_LATE_EXACT[f"perkDistrophiaRank{_rank}LongDesc"] = (
        f"{_flavor}耐力上限降低[DECEA3]{_stamina}[-]点，水分消耗提高[DECEA3]{_water}%[-]，超重时的机动性降低[DECEA3]{_move}[-]。"
        f"\n\n[FFB800]剩余：{{cvar(.TimerDistrophia{_rank}:0)}}[-]"
    )

for _rank, (_damage, _fall, _fracture, _flavor) in enumerate(((10, 1, 15, "你的骨骼比较脆弱。"), (25, 3, 30, "你的骨骼非常脆弱。"), (50, 5, 50, "你的骨骼已脆弱不堪。")), 1):
    PROJECTZ_LATE_EXACT[f"perkBrittleBonesRank{_rank}LongDesc"] = (
        f"{_flavor}敌人攻击造成的伤害提高[DECEA3]{_damage}%[-]，安全坠落高度降低[DECEA3]{_fall}[-]，攻击导致肢体骨折的概率提高[DECEA3]{_fracture}%[-]。"
        f"\n\n[FFB800]剩余：{{cvar(.TimerBrittleBones{_rank}:0)}}[-]"
    )

for _rank, (_reserve, _slots, _health, _flavor) in enumerate(((10, 3, 10, "你很虚弱。"), (25, 6, 20, "你非常虚弱。"), (50, 9, 30, "你是个真正的软脚虾。想活下去，你需要很多运气。")), 1):
    PROJECTZ_LATE_EXACT[f"perkWeaklingRank{_rank}LongDesc"] = (
        f"{_flavor}精力储备消耗提高[DECEA3]{_reserve}%[-]，可无惩罚携带的物品减少[DECEA3]{_slots}[-]格，生命值上限降低[DECEA3]{_health}[-]点。"
        f"\n\n[FFB800]剩余：{{cvar(.TimerWeakling{_rank}:0)}}[-]"
    )

for _rank, (_amount, _flavor) in enumerate(((10, "你有轻微过敏。"), (20, "你患有过敏。"), (30, "你的过敏非常严重。")), 1):
    PROJECTZ_LATE_EXACT[f"perkAllergySuffererRank{_rank}LongDesc"] = f"{_flavor}身处草地时，生命值与耐力上限降低[DECEA3]{_amount}[-]点。\n\n[FFB800]剩余：{{cvar(.TimerAllergySufferer{_rank}:0)}}[-]"

for _rank, (_craft, _samples, _flavor) in enumerate(((5, 10, "亲手制作东西对你来说十分困难。"), (15, 20, "任何手工活对你来说都是真正的挑战。"), (25, 40, "人们都说你笨手笨脚。")), 1):
    PROJECTZ_LATE_EXACT[f"perkClumsyRank{_rank}LongDesc"] = (
        f"{_flavor}物品制作速度降低[DECEA3]{_craft}%[-]，采集到的变异样本减少[DECEA3]{_samples}%[-]。"
        f"\n\n[FFB800]剩余：{{cvar(.CounterClumsy{_rank}:0)}}[-]"
    )

for _rank, (_drain, _regen, _flavor) in enumerate((("25%", 10, "你不太注意个人卫生。"), ("50%", 25, "你是个邋遢鬼。"), ("2倍", 50, "卫生对你来说是个陌生概念。")), 1):
    PROJECTZ_LATE_EXACT[f"perkSlobRank{_rank}LongDesc"] = (
        f"{_flavor}卫生值消耗速度提高[DECEA3]{_drain}[-]，恢复速度降低[DECEA3]{_regen}%[-]。"
        f"\n\n[FFB800]剩余：{{cvar(.TimerSlob{_rank}:0)}}[-]"
    )

for _rank, (_xp, _flavor) in enumerate(((10, "你有轻度痴呆。"), (20, "你已陷入痴呆。"), (30, "你笨得像块砖头。")), 1):
    PROJECTZ_LATE_EXACT[f"perkDementiaRank{_rank}LongDesc"] = f"{_flavor}获得的所有经验减少[DECEA3]{_xp}%[-]。\n\n[FFB800]剩余：{{cvar(.BooksReadWorse{_rank}:0)}}[-]"

_alarm_values = ((25, "5%", "7.5%", "10%"), (50, "8%", "12%", "16%"), (50, "10%", "15%", "25%"))
_alarm_flavor = ("僵尸在周围时，你勉强能压住恐慌，但恐惧仍会占据上风。", "你努力不在僵尸面前惊慌，却无法控制自己。", "被僵尸包围时，你已完全无法控制恐慌。")
for _rank, (_stress, _low, _mid, _high) in enumerate(_alarm_values, 1):
    PROJECTZ_LATE_EXACT[f"perkAlarmismRank{_rank}LongDesc"] = (
        f"{_alarm_flavor[_rank - 1]}周围有僵尸时，抗压能力消耗提高[DECEA3]{_stress}%[-]。对僵尸造成的伤害会随周围数量降低："
        f"少于4只时降低[DECEA3]{_low}[-]，4至10只时降低[DECEA3]{_mid}[-]，超过10只时降低[DECEA3]{_high}[-]。"
        f"\n\n[FFB800]剩余：{{cvar(.TimerAlarmism{_rank}:0)}}[-]"
    )

_pacifist_text = {
    1: "你不想杀死任何人，甚至不想杀僵尸。击杀时恢复的抗压能力减半，造成的所有伤害降低[DECEA3]10%[-]。",
    2: "你拒绝杀生，即使对象是僵尸。击杀时不再恢复抗压能力，造成的所有伤害降低[DECEA3]20%[-]。",
    3: "杀戮会给你带来压力。击杀时会损失抗压能力，造成的所有伤害降低[DECEA3]30%[-]。",
}
for _rank, _text in _pacifist_text.items(): PROJECTZ_LATE_EXACT[f"perkPacifistRank{_rank}LongDesc"] = f"{_text}\n\n[FFB800]剩余：{{cvar(.CounterPacifist{_rank}:0)}}[-]"

for _rank, (_move, _spread, _stress, _flavor) in enumerate(((5, 10, 10, "你有轻微的黑暗恐惧。"), (10, 25, 25, "你害怕黑暗。"), (15, 50, 50, "你非常害怕黑暗。")), 1):
    PROJECTZ_LATE_EXACT[f"perkFearDarkRank{_rank}LongDesc"] = (
        f"{_flavor}夜间的机动性降低[DECEA3]{_move}%[-]。没有额外光源时，武器散布提高[DECEA3]{_spread}%[-]，抗压能力消耗提高[DECEA3]{_stress}%[-]。"
        f"夜间使用任何手电筒、夜视仪或手持火把都算额外光源。\n\n[FFB800]剩余：{{cvar(.TimerFearDark{_rank}:0)}}[-]"
    )

for _rank, (_stress, _damage, _flavor) in enumerate(((20, 5, "你害怕僵尸。"), (50, 15, "你非常害怕僵尸。"), (100, 25, "你对僵尸有致命般的恐惧。")), 1):
    PROJECTZ_LATE_EXACT[f"perkFearZombieRank{_rank}LongDesc"] = f"{_flavor}受到僵尸伤害时，额外损失[DECEA3]{_stress}%[-]的抗压能力，对僵尸造成的伤害降低[DECEA3]{_damage}%[-]。\n\n[FFB800]剩余：{{cvar(.CounterFearZombie{_rank}:0)}}[-]"

for _rank, (_damage, _resist, _flavor) in enumerate(((10, 5, "你有轻微的火焰恐惧。"), (20, 10, "你害怕火焰。"), (30, 15, "你非常害怕火焰。")), 1):
    PROJECTZ_LATE_EXACT[f"perkFearFireRank{_rank}LongDesc"] = f"{_flavor}受到的火焰伤害提高[DECEA3]{_damage}%[-]，火焰抗性降低[DECEA3]{_resist}%[-]。\n\n[FFB800]剩余：{{cvar(.TimerFearFire{_rank}:0)}}[-]"

for _rank, (_damage, _recovery, _terrible, _flavor) in enumerate(((10, 10, 50, "你有轻微的疼痛恐惧。"), (25, 25, 100, "你中度惧怕疼痛。"), (50, 50, 200, "疼痛会使你陷入恐慌。")), 1):
    PROJECTZ_LATE_EXACT[f"perkFearPainRank{_rank}LongDesc"] = (
        f"{_flavor}任何肢体受伤效果生效时，受到的所有伤害提高[DECEA3]{_damage}%[-]，伤势恢复速度降低[DECEA3]{_recovery}%[-]。"
        f"[DECEA3]可怕伤口[-]生效时，受到的伤害额外提高[DECEA3]{_terrible}%[-]。\n\n[FFB800]剩余：{{cvar(.TimerFearPain{_rank}:0)}}[-]"
    )

for _rank, (_xp, _stress, _flavor) in enumerate(((5, 5, "你有轻微的社交恐惧。"), (10, 15, "你害怕与人相处。"), (20, 30, "你非常害怕与人相处。")), 1):
    PROJECTZ_LATE_EXACT[f"perkSocialPhobiaRank{_rank}LongDesc"] = f"{_flavor}身处队伍中时，获得的经验减少[DECEA3]{_xp}%[-]，抗压能力恢复速度降低[DECEA3]{_stress}%[-]。\n\n[FFB800]剩余：{{cvar(.TimerSocialPhobia{_rank}:0)}}[-]"

for _rank, (_quality, _drop, _flavor) in enumerate(((5, 5, "运气已经弃你而去。"), (10, 10, "你是个倒霉蛋。"), (15, 15, "你基本不知道好运为何物。")), 1):
    PROJECTZ_LATE_EXACT[f"perkUnluckerRank{_rank}LongDesc"] = f"{_flavor}战利品品质降低[DECEA3]{_quality}%[-]，僵尸掉落战利品的概率降低[DECEA3]{_drop}%[-]。\n\n[FFB800]剩余：{{cvar(.CounterUnlucker{_rank}:0)}}[-]"

for _rank, (_drain, _regen, _flavor) in enumerate((("10%", "25%", "你保持着良好的卫生习惯。"), ("25%", "50%", "你无法忍受脏乱。"), ("50%", "2倍", "你是个真正的洁癖者。")), 1):
    PROJECTZ_LATE_EXACT[f"perkCleanFreakRank{_rank}LongDesc"] = f"{_flavor}卫生值消耗速度降低[DECEA3]{_drain}[-]，恢复速度提高[DECEA3]{_regen}[-]。\n\n[FFB800]剩余：{{cvar(.TimerCleanFreak{_rank - 1}:0)}}[-]"

for _rank, (_bonus, _flavor) in enumerate(((10, "你的魅力会吸引其他幸存者。"), (15, "你非常有魅力。"), (25, "你的魅力像磁铁一样吸引着人们。")), 1):
    PROJECTZ_LATE_EXACT[f"perkCharismaticRank{_rank}LongDesc"] = (
        f"{_flavor}出售价格加成提高[DECEA3]{_bonus}%[-]，作为奖励获得的公爵币数量提高[DECEA3]{_bonus}%[-]。"
        f"\n\n[FFB800]剩余：{{cvar(.CounterCharismatic{_rank - 1}:0)}}[-]"
    )

for _rank in range(1, 6):
    PROJECTZ_LATE_EXACT[f"perkSiphoningStrikesRank{_rank}LongDesc"] = (
        f"初尝战斗的鲜血令你精神振奋。\n近战击杀有[DECEA3]{{cvar(.HealthStealChance:0)}}%[-]的概率恢复{4 * _rank}点生命值。"
    )

PROJECTZ_LATE_EXACT.update({
    "perkAlarmismDesc": "[FFB800]每秒[-]，若周围有超过7只僵尸，计数器减少[DECEA3]1[-]。若连续[DECEA3]15[-]秒未受到伤害，计数器的下降速度提高至[DECEA3]4倍[-]。",
    "perkAllergySuffererDesc": "[FFB800]每秒[-]，身处森林生态区时计数器减少[DECEA3]1[-]；正受到过敏影响时再额外减少[DECEA3]1[-]。",
    "perkBrittleBonesDesc": "[FFB800]每秒[-]，受到维生素影响时计数器减少[DECEA3]1[-]。每吃1个鸡蛋减少[DECEA3]50[-]；每吃1份新鲜蔬菜减少[DECEA3]6[-]；每吃1份浆果或蘑菇减少[DECEA3]12[-]；超级玉米减少[DECEA3]25[-]；辐射蘑菇减少[DECEA3]3[-]。",
    "perkCharismaticDesc": "[FFB800]完成任务[-]——在商人处每完成1项任务，计数器减少[DECEA3]1[-]。",
    "perkCleanFreakDesc": "[FFB800]每秒[-]，卫生值高于75%时计数器减少[DECEA3]1[-]，高于95%时再额外减少[DECEA3]1[-]。使用普通清洁巾减少[DECEA3]10[-]，使用湿巾减少[DECEA3]20[-]。",
    "perkClumsyDesc": "[FFB800]每次击中[-]——屠宰死去的动物或僵尸尸体时，每次击中都会使计数器减少[DECEA3]1[-]。",
    "perkDementiaDesc": "[FFB800]阅读[-]书籍、杂志或配方时，计数器减少[DECEA3]1[-]。",
    "perkDistrophiaDesc": "[FFB800]每秒[-]，食物消化效果生效时计数器减少[DECEA3]1[-]；若此时正在舒适的床上休息，则减少[DECEA3]2[-]；进行力量训练时减少[DECEA3]4[-]。",
    "perkFearDarkDesc": "[FFB800]每秒[-]，夜间计数器减少[DECEA3]1[-]；拥有额外光源时再额外减少[DECEA3]1[-]。",
    "perkFearFireDesc": "[FFB800]每秒[-]，身处焦土生态区、靠近篝火、手持火把，或受到Boss燃烧效果时，计数器减少[DECEA3]1[-]；正在燃烧时减少[DECEA3]6[-]。[FFB800]每次受击[-]——受到吞噬者、其仆从或电火花攻击时，计数器减少[DECEA3]10[-]。",
    "perkFearPainDesc": "[FFB800]每秒[-]，受到已处理骨折或伤口影响时，计数器减少[DECEA3]1[-][DECEA3]（伤势不叠加）[-]；受到扭伤或开放性伤口影响时，减少[DECEA3]2[-][DECEA3]（伤势可叠加）[-]；肢体开放性骨折时，减少[DECEA3]3[-][DECEA3]（伤势可叠加）[-]。开放性骨折或脱臼时受到类固醇影响，再额外减少[DECEA3]1[-]。",
    "perkFearZombieDesc": "[FFB800]击杀[-]任意僵尸都会使计数器减少[DECEA3]1[-]。",
    "perkObesityDesc": "饱食也要适度。如果过重已影响行动，求生将变得异常困难。",
    "perkPacifistDesc": "[FFB800]每次受击[-]——受到僵尸攻击时，计数器减少[DECEA3]1[-]。",
    "perkSchumacherDesc": "[FFB800]每秒[-]，只要身处载具中，计数器就会减少[DECEA3]1[-]。",
    "perkSlobDesc": "[FFB800]每秒[-]，受到轻度、中度或重度不卫生效果影响时，计数器分别减少[DECEA3]1、2、3[-]。卫生值低于5%时额外减少[DECEA3]2[-]；使用普通清洁巾减少[DECEA3]10[-]，使用湿巾减少[DECEA3]20[-]。",
    "perkSocialPhobiaDesc": "[FFB800]每秒[-]，只要身处队伍中，计数器就会减少[DECEA3]1[-]。",
    "perkUnluckerDesc": "[FFB800]击杀[-]任意僵尸都会使计数器减少[DECEA3]1[-]。",
    "perkWeakImmunityDesc": "你的免疫系统较弱，感染或中毒的概率很高。",
    "perkWeaklingDesc": "[FFB800]每秒[-]降低计数器：超重时减少[DECEA3]1[-]，进行力量训练时减少[DECEA3]4[-]，普通攻击时减少[DECEA3]2[-]，使用武器或工具发动蓄力攻击时减少[DECEA3]4[-]。",
})

_chain_warning = "\\n\\n[DECEA3]警告！取消此任务会中断整条任务链，只有管理员才能重新开启。[-]"
_survival_chapters = {
    4: "比管制武器更好", 5: "更好的枪械", 6: "强大火力", 7: "自卫",
    8: "最佳范例", 9: "绞肉机", 10: "呼唤其名", 11: "废土传奇",
}
for _chapter, _title in _survival_chapters.items():
    PROJECTZ_LATE_EXACT[f"note{_chapter + 1}_startQuestDesc"] = f"开启[FF6666]生存[-]任务第[FF6666]{_chapter}章——{_title}[-]。" + _chain_warning
PROJECTZ_LATE_EXACT["note13_startQuestDesc"] = "开启[FF6666]生存[-]任务第[FF6666]12章——最终猎杀（开始）[-]。" + _chain_warning
PROJECTZ_LATE_EXACT["note14_startQuestDesc"] = "开启[FF6666]生存[-]任务第[FF6666]12章——最终猎杀（完成）[-]。" + _chain_warning
PROJECTZ_LATE_EXACT["note4_startQuest"] = "[DECEA3]任务[-] 生存[FF6666]第3章[-]——不错的奖励"
PROJECTZ_LATE_EXACT["startQuest4Name"] = PROJECTZ_LATE_EXACT["note4_startQuest"]
for _chapter in range(1, 11):
    PROJECTZ_LATE_EXACT[f"bookRareSamples{_chapter}Desc"] = f"开启[FFB800]稀有样本[-]任务第[FFB800]{_chapter}章[-]。" + _chain_warning


def translate_projectz_late_cleanup() -> None:
    path = ROOT / "01-ProjectZ/Config/Localization.csv"; lines = path.read_text(encoding="utf-8-sig").splitlines(keepends=True)
    header = next(csv.reader([lines[0]])); columns = {name.lower(): i for i, name in enumerate(header)}; e = columns["english"]; z = columns["schinese"]
    changed = 0
    replacements = (
        ("[FF6666]«Boost»[-]", "[FF6666]«强化»[-]"), ("[FF6666]Boost[-]", "[FF6666]强化[-]"),
        ("《Boost》", "《强化》"), ("“Boost”", "“强化”"), ("[FFB800]unique[-]", "[FFB800]独特[-]"),
        ("[F0B18A]unique[-]", "[F0B18A]独特[-]"), ("[FFB800]Unique[-]", "[FFB800]独特[-]"),
        ("[FAFF63]Rare[-]", "[FAFF63]稀有[-]"), ("[DECEA3]Improved[-]", "[DECEA3]改良型[-]"),
        ("[8692FF]«Rogue»[-]", "[8692FF]«游荡者»[-]"), ("[8692FF]Gatherer[-]", "[8692FF]采集者[-]"),
        ("[8692FF]«Nomad»[-]", "[8692FF]«游牧者»[-]"), ("[8692FF]Nerd[-]", "[8692FF]书呆子[-]"),
        ("[8692FF]Raider[-]", "[8692FF]掠夺者[-]"), ("Rescuer", "救援者"),
        ("The Boss is nearby", "Boss就在附近"), ("The small Boss is nearby", "小型Boss就在附近"),
        ("The 娜迦蛇后 is on the hunt", "娜迦蛇后正在狩猎"), ("秘法师 is on the hunt", "秘法师正在狩猎"),
        ("木乃伊 has acquired physical form", "木乃伊已进入实体形态"), ("木乃伊 has acquired ghostly form", "木乃伊已进入幽灵形态"),
        ("The 木乃伊 steals your health", "木乃伊正在汲取你的生命"), ("RUN", "快逃"),
        ("Gargul", "石像魔"), ("Damage", "伤害"), ("Speed", "速度"),
        ("[5AFF75]Small[-]", "[5AFF75]小型[-]"), ("[FFB800]Big[-]", "[FFB800]大型[-]"),
        ("Armor", "护甲"), ("Boomstick", "霰弹枪专家"), ("Mother Lode", "矿脉之母"),
        ("Fortitude", "坚韧"), ("Poisoned Blades", "毒刃"), ("Rad Remover", "辐射清除器"),
        ("Master", "大师"), ("Shotguns", "霰弹枪"), ("Shotgun", "霰弹枪"), ("Rifles", "步枪"),
        ("Rifle", "步枪"), ("Bows", "弓弩"), ("Archery", "箭术"), ("Robotics", "机器人学"),
        ("Workstations", "工作台"), ("Tools", "工具"), ("Traps", "陷阱"), ("Clubs", "棍棒"),
        ("Sledgehammers", "大锤"), ("Spears", "长矛"), ("Blades", "刀刃"), ("Knuckles", "拳套"),
        ("Kitchen", "烹饪"), ("Food", "食物"), ("Medicines", "医药"), ("Chemist", "化学家"),
        ("Seeds", "种子"), ("Farmer", "农夫"), ("AArmor", "护甲"), (" Lv.", " 等级"),
        ("iron", "铁矿"), ("lead", "铅矿"), ("clay", "黏土"), ("reactor", "反应堆型"),
        ("Reactor", "反应堆型"), ("Auger", "螺旋钻"), ("JetPack", "喷气背包"), ("Electro", "电能"),
        ("LSS", "LSS"), ("Defender", "防御者"), ("Bites", "片"),
        ("Fort 片", "强骨片"), ("Recog", "认知药"), ("Miniboss", "小型Boss"), ("miniBoss", "小型Boss"),
        ("minibosses", "小型Boss"), ("bosses", "Boss"), ("boss", "Boss"),
        ("[DECEA3]Quest[-]", "[DECEA3]任务[-]"), ("Quest", "任务"), ("quest", "任务"),
        ("Hunting for", "猎杀"), ("Get", "取得"), ("Unlocked", "已解锁"), ("Expert", "专家"),
        ("level", "等级"), ("Activates", "激活"), ("summon", "召唤"), ("Expeditions", "远征"),
        ("Expedition", "远征"), ("Wild", "怀尔德"), ("Leshy", "莱希"), ("Adel", "阿黛尔"),
        ("Bob", "鲍勃"), ("Jen", "珍"), ("Rect", "雷克特"), ("Janitors", "清洁工感染者"),
        ("Businessmen", "商人感染者"), ("Bikers", "摩托车手感染者"), ("Moes", "莫感染者"),
        ("Tourists", "游客感染者"), ("Soldiers", "士兵感染者"), ("Cops", "警察感染者"),
        ("Demolishers", "爆破感染者"), ("Rabbits", "兔子"), ("Snakes", "蛇"), ("Wolves", "狼"),
        ("Coyotes", "郊狼"), ("Boars", "野猪"), ("Bears", "熊"), ("Mountain Lions", "美洲狮"),
        ("vultures", "秃鹫"), ("lumberjacks", "伐木工感染者"), (" any 僵尸", "任意僵尸"),
        ("Yautja", "铁血战士"), ("Zinger", "Zinger"), ("Combistick", "组合长矛"), ("Flugen", "电弧者"),
        ("[DECEA3]Elixir:[-]", "[DECEA3]灵药：[-]"), ("Elixir", "灵药"),
        ("Handy Land", "巧手天地"), ("Scrapping 4 Fun", "快乐拆解"), ("Knife Guy", "刀锋达人"),
        ("Big Hitters", "重击高手"), ("Get Hammered", "重锤出击"), ("Sharp Sticks", "尖锐长棍"),
        ("Shotgun Weekly", "霰弹枪周刊"), ("Tech Planet", "科技星球"),
        ("[FFA463]sealed[-]", "[FFA463]密封[-]"), ("reactors", "反应堆"), ("Hop", "啤酒花"),
        ("[FFB800]MAJOR[-]", "[FFB800]高级[-]"), ("MAJOR", "高级"), ("CHIEF", "酋长"),
        ("Mini BOSS", "小型Boss"), ("Mini Boss", "小型Boss"), ("迷你 BOSS", "小型Boss"),
        ("小BOSS", "小型Boss"), ("BOSS", "Boss"), ("MINION", "仆从"), ("ULTRA", "终极"),
        ("DEFENDER", "防御者"), ("ACTIVATOR", "激活者"), ("MUTANT HOG", "变异野猪"),
        ("ELITE", "精英"), ("INFRNAL", "炼狱"), ("CHARGED", "充能"),
        ("Increased passive skill:", "被动技能提升："), ("Techno Fan", "科技爱好者"),
        ("statZombieDamage", "僵尸伤害"), ("to zombies (non-elite)", "对非精英僵尸的伤害"),
        ("Reduces target 伤害", "降低目标造成的伤害"), ("伤害 to elite zombies", "对精英僵尸的伤害"),
        ("伤害 to any surface", "对任意表面的伤害"), ("方块伤害 (salvage)", "拆解方块伤害"),
        ("Pummel Pete", "重击皮特"), ("Miner 69'er", "69号矿工"), ("Gunslinger", "枪手"),
        ("Big Hitter", "重击高手"), ("Get Hammered", "重锤出击"), ("Shotgun Weekly", "霰弹枪周刊"),
        ("Wiring 101", "电路入门"), ("Electrical Traps", "电击陷阱"), ("Forge Ahead", "锻造前进"),
        ("Clear Gaze", "清明视野"), ("Stimulant", "兴奋剂"), ("Daily", "每日"), ("Premium", "高级"),
        ("Plunderer", "掠夺者"), ("Protector", "守护者"), ("Nomad", "游牧者"),
        ("Hot Harry", "烈焰哈里"), ("Ancient Yeti", "远古雪人"), ("Bear Daddy", "巨熊之父"),
        ("Burning Flesh", "燃烧之肉"), ("Devourer", "吞噬者"), ("Nagaina", "娜迦蛇后"),
        ("Cholera", "霍乱"), ("Mystic", "秘法师"), ("Mummy", "木乃伊"), ("Bitch", "悍妇"),
        ("Shocker", "电击者"), ("Veteran", "老兵"), ("Carrier", "母体"), ("Bull", "蛮牛"),
        ("SHOCKER", "电击者"), ("VETERAN", "老兵"), ("CARRIER", "母体"), ("BULL", "蛮牛"),
        ("ANCIENT YETI", "远古雪人"), ("Loot", "战利品："),
        ("Savage Country", "野蛮国度"), ("Desert Vulture", "沙漠秃鹫"),
        ("Fireproof Protection", "防火保护"), ("Firestarter", "纵火者"), ("URANIUM", "贫铀"),
        ("Unkillable", "不毁"), ("Avenger", "复仇者"), ("Berserk", "狂暴"),
        ("Guardian", "守护者"), ("Surgeon", "外科医生"), ("Crusher", "粉碎者"),
        ("Masterpiece", "杰作"), ("Crisis", "危机"), ("Breeze", "疾风"), ("Tesla", "特斯拉"),
        ("AArsonist", "纵火者"), ("Destructor", "毁灭者"), ("Flugegeheimen", "电弧者"),
        ("Maus", "鼠王"), ("Indiana", "印第安纳"), ("Combistick", "组合长矛"),
        ("Unique", "独特"), ("unique", "独特"), ("Improved", "改良型"), ("Legendary", "传奇"),
        ("AxeRare", "稀有斧"), ("ShovelRare", "稀有铲"), ("Rare", "稀有"), ("rare", "稀有"),
        ("Rapidfire", "速射"), ("Apple", "苹果"), ("Awl", "尖锥"), ("Metalist", "金属专家"),
        ("Paul Bunyan", "伐木巨匠"), ("Cripple", "致残"), ("Bipod", "两脚架"),
        ("Light", "轻型"), ("Support", "支援"), ("Active Life Support", "主动生命维持"),
        ("Small", "小型"), ("Medium", "中型"), ("LLarge", "大型"), ("Large", "大型"),
        ("Forge", "锻炉"), ("Lead", "铅"), ("Coal", "煤"), ("Clay", "黏土"),
        ("Deco", "装饰"), ("Selection", "选择"), ("SETS", "套装"),
        ("AMMO", "弹药"), ("BONUS", "奖励"), ("ABILITY", "能力"), ("Failed", "失败"),
        ("magnum", "马格南"), ("infested", "感染区"), ("steel", "钢制"),
        ("Zombie Expert", "僵尸专家"), ("Zombie", "僵尸"),
        ("Cop", "警察感染者"), ("Boe", "博伊"), ("Shamway", "沙姆威食品"),
        ("Darlene", "达琳"), ("Marlene", "玛琳"), ("Joe", "乔"), ("Steve", "史蒂夫"),
        (" any 僵尸", "任意僵尸"), ("Feature", "特性"),
        ("[Except: Regular]", "[普通弹药除外]"), ("[Except：Regular]", "[普通弹药除外]"),
        (" nInstalled", "\n已安装"), ("OFF", "关闭"),
        ("取得 Hammered", "重锤出击"), ("霰弹枪 Weekly", "霰弹枪周刊"),
        ("Electrical 陷阱", "电击陷阱"), ("大师piece", "杰作"),
        ("weapons", "武器"), ("armor", "护甲"), ("Frequency", "频繁"),
        ("Fortbites", "强骨片"), ("Spark", "电火花"), ("Scav", "斯卡夫"), ("Zone", "佐恩"),
        ("蛮牛dog", "斗牛犬"), ("Barbarian", "野蛮人战斧"),
        ("A Club", "一根棍棒"), ("Spear", "长矛"), ("BIBA", "比巴"),
        ("DEVOURER", "吞噬者"), ("GARGUL", "石像魔"), ("miniboss's", "小型Boss的"),
        ("miniboss", "小型Boss"), ("小老板", "小型Boss"), ("母狗", "悍妇"),
        ("的伤害减少增加", "造成的伤害降低"), ("数量增加通过", "数量提高"),
        ("显着", "显著"), ("传入伤害", "受到的伤害"), ("弱理智值", "抗压能力"),
        ("每秒会消耗额外的抗压能力", "每秒额外消耗少量抗压能力"),
        ("运行！！！", "快逃！"), ("运行!!!", "快逃！"), ("修改的质量", "改装件品质"),
        ("改装的质量", "改装件品质"), ("母矿脉", "矿脉之母"), ("防弹衣", "胸甲"),
        ("修饰符", "改装件"), ("小兵", "仆从"),
        ("这种原始的临时武器也可用于切除动物的内脏。", "这种原始武器也可以用来屠宰动物。"),
        ("常规攻击会造成[DECEA3]1[-]流血伤口和至少[DECEA3]2[-]的力量攻击。", "普通攻击造成[DECEA3]1[-]层流血，蓄力攻击至少造成[DECEA3]2[-]层流血。"),
        ("指关节套可以保护您的双手，并在挥杆时增加一些重量。", "拳套既能保护双手，也能让每一拳更有分量。"),
        ("一根木棍。适合打破膝盖和头骨。", "一根木棍，适合敲碎膝盖和头骨。"),
        ("自制的枪，发射猎枪弹。", "一把使用霰弹的自制枪械。"),
        ("修复套件（改进）", "改良型修理包"), ("维修套件（改进）", "改良型修理包"),
        ("修理套件（改进）", "改良型修理包"),
        ("修复套件", "修理包"), ("维修套件", "修理包"), ("修理套件", "修理包"),
        ("第二次冷却时间", "秒冷却时间"), ("党员", "队伍成员"),
        ("目标不是老板", "目标不是Boss"), ("增加健康", "提高生命上限"),
        ("长生不老药", "灵药"), ("培养改装质量", "提高改装件品质"),
        ("开发改装质量", "提高改装件品质"), ("改造质量", "改装件品质"),
        ("老板", "Boss"), ("额外的弱理智", "少量额外抗压能力"),
        ("保持活力", "活下来"),
        ("Boss定期要求支援", "Boss会周期性呼叫增援"),
        ("Boss生气了", "Boss已狂暴"), ("Boss偷走生命值", "Boss正在汲取生命"),
        ("Boss呼吁兄弟情谊", "Boss正在召集战友"),
        ("您", "你"), ("十字弓", "弩"), ("灵丹妙药", "灵药"),
        ("耐用性", "耐久度"), ("主要统计数据", "主要属性"), ("基础统计数据", "基础属性"),
        ("基本统计数据", "基础属性"), ("所有基础统计数据", "所有基础属性"),
        ("通过杀死尽可能多的这些怪物来找到它。", "尽可能多地猎杀这种怪物，找出它的弱点。"),
        ("[DECEA3]到目前为止杀死：", "[DECEA3]当前击杀数："),
        ("修复需要改良型修理包", "修理需要改良型修理包"),
        ("维修需要改良型修理包", "修理需要改良型修理包"),
        ("[DECEA3]U用于", "[DECEA3]用于"), ("统计数据", "属性"),
        ("增加了", "提高"), ("均已增加", "均得到提升"), ("已增加", "得到提升"),
        ("荒地", "废土"), ("小头目", "小型Boss"), ("头目", "Boss"), ("航母", "母体"),
        ("加倍翻倍", "翻倍"), ("武器传播", "武器散布"), ("运行速度", "奔跑速度"),
        ("收到的治疗效果", "受到的治疗效果"), ("暴击伤害治疗速度", "重伤恢复速度"),
        ("块损坏", "方块伤害"), ("工具退化", "工具耐久损耗"), ("武器退化", "武器耐久损耗"),
        ("改造的质量", "改装件品质"), ("改装质量", "改装件品质"),
        ("技能开发改装件品质", "技能可提高改装件品质"),
        ("修理包（传奇）", "传奇修理包"), ("装甲制作套件（改进）", "改良型护甲制作套件"),
        ("俱乐部熟练", "棍棒熟练"), ("安装在俱乐部中", "安装在棍棒上"),
        ("俱乐部爱好者", "棍棒爱好者"), ("俱乐部零件", "棍棒零件"),
        ("一个好的俱乐部", "一根好棍棒"), ("钢铁俱乐部", "钢制棍棒"),
        ("使用俱乐部", "使用棍棒"), ("俱乐部捆绑包", "棍棒包"), ("该俱乐部", "这根球棒"),
        ("箭头和螺栓", "箭与弩矢"), ("箭和螺栓", "箭与弩矢"),
        ("例如，你可以拍摄很多次。", "例如，你可以多开几枪。"),
        ("物品的质量", "物品品质"), ("战利品质量", "战利品品质"), ("掠夺质量", "战利品品质"),
        ("流动性", "机动性"), ("力量储备", "精力储备"), ("性格特质", "特质"),
        ("生物群落", "生态区"), ("[DECEA3]I当", "[DECEA3]当"),
        ("推出[71FFE7]", "启动[71FFE7]"), ("上线[71FFE7]", "启动[71FFE7]"), ("推出[C27E53]", "启动[C27E53]"),
        ("交易者", "商人"), ("修复需要传奇修理包", "修理需要传奇修理包"),
        ("小捆绑包", "小型礼包"), ("中捆绑包", "中型礼包"), ("大捆绑包", "大型礼包"), ("捆绑包", "礼包"),
        ("[DECEA3]《改进版》[-]", "[DECEA3]改良型[-]"), ("[DECEA3]《改进型》[-]", "[DECEA3]改良型[-]"),
        ("[DECEA3]《改进》[-]", "[DECEA3]改良型[-]"), ("[DECEA3]改进的[-]", "[DECEA3]改良型[-]"),
        ("[DECEA3]改进型[-]", "[DECEA3]改良型[-]"), ("[DECEA3]改进[-]", "[DECEA3]改良型[-]"),
        ("改进型", "改良型"), ("改进的工具", "改良型工具"),
        ("自动刀塔", "自动炮塔"), ("指节钢", "钢制拳套"), ("钢指关节", "钢制拳套"),
        ("俄歇", "螺旋钻"), ("弯刀", "砍刀"), ("交换机", "开关"), ("波纹管", "风箱"),
        ("装甲制作套件", "护甲制作套件"), ("执行者装甲套装", "执法者护甲套装"),
        ("轻型装甲", "轻甲"), ("中型装甲", "中甲"), ("重型装甲", "重甲"),
        ("合同", "合约"), ("成分[71FFE7]", "材料 [71FFE7]"), ("模组", "改装件"),
        ("大妈妈", "胖妇"),
    )
    for index in range(1, len(lines)):
        line = lines[index]; ending = "\r\n" if line.endswith("\r\n") else "\n" if line.endswith("\n") else ""; row = next(csv.reader([line.removesuffix(ending)]))
        if len(row) <= z: continue
        updated = PROJECTZ_LATE_EXACT.get(row[0], row[z])
        # Project Z encodes display line breaks as the two characters ``\n``.
        # Never emit physical newlines inside a record because later game-side
        # line-oriented localization loading expects one record per file line.
        updated = updated.replace("\r\n", "\\n").replace("\n", "\\n")
        # Localization variables are game code, not visible prose. Mask them before
        # doing any textual cleanup, then restore the canonical spellings from the
        # English source so previous mixed-language passes cannot corrupt cvar names.
        source_vars = re.findall(r"\{[^{}]+\}|%[A-Za-z]|\$\([^)]*\)", row[e])
        translated_vars = re.findall(r"\{[^{}]+\}|%[A-Za-z]|\$\([^)]*\)", updated)
        if len(source_vars) == len(translated_vars):
            var_index = iter(range(len(translated_vars)))
            updated = re.sub(
                r"\{[^{}]+\}|%[A-Za-z]|\$\([^)]*\)",
                lambda _match: f"\x00PZVAR{next(var_index)}\x00",
                updated,
            )
        for source, target in replacements: updated = updated.replace(source, target)
        for var_number, source_var in enumerate(source_vars):
            updated = updated.replace(f"\x00PZVAR{var_number}\x00", source_var)
        updated = updated.replace("  ", " ")
        if updated == row[z]: continue
        row[z] = updated; output = io.StringIO(); csv.writer(output, lineterminator=ending, quoting=csv.QUOTE_MINIMAL).writerow(row); lines[index] = output.getvalue(); changed += 1
    with path.open("w", encoding="utf-8-sig", newline="") as handle: handle.write("".join(lines))
    print(f"01-ProjectZ/Config/Localization.csv: cleaned {changed} mixed entries")


translate_projectz_late_cleanup()


TIER_UNLOCK_PHRASES = {
    # Workstations and materials.
    "Bullet casing bundle (1000)": "弹壳包（1000）",
    "Bundle buckshot (1000)": "鹿弹包（1000）",
    "Bullet tip bundle (1000)": "弹头包（1000）",
    "Empty Water Bottle": "空水瓶", "Kitchen Stove": "厨房炉灶",
    "Dew Collector": "露水收集器", "Canning Tools": "罐装工具",
    "Small Parts": "小型零件", "Cement Mixer": "水泥搅拌机",
    "potassium nitrate": "硝酸钾", "oil shale": "油页岩",
    "resource packer": "资源打包器", "alarm system": "警报系统",
    "spare parts": "备用零件", "Autominer": "自动采矿机",
    "Workbench": "工作台", "Forge": "锻炉", "Bellows": "风箱",
    "Anvil": "铁砧", "Honey Extractor": "蜂蜜提取器",
    "Brood Box": "育雏箱", "Water Filter": "滤水器",
    "Chemistry Station": "化学工作站", "Chemical Station": "化学工作站",
    "Chemical flasks": "化学烧瓶", "processing accelerator": "处理加速器",
    "compost bin": "堆肥箱", "Recycler": "回收站", "Smoker": "烟熏炉",
    "Crucible": "坩埚", "Tools": "工具", "iron": "铁矿", "lead": "铅矿",
    "coal": "煤矿", "clay": "黏土矿",

    # Electrical equipment, traps and tools.
    "Charging station": "充电站", "Impact Driver": "冲击起子",
    "industrial engine": "工业发动机", "solar bank": "太阳能电池组",
    "Electric Timer Relay": "定时电线继电器", "Electric Wire Relay": "电线继电器",
    "Motion Sensor": "运动传感器", "Electric Fence Post": "电围栏柱",
    "Shotgun Auto Turret": "霰弹枪自动炮塔", "Auto Turret": "自动炮塔",
    "Blade Trap": "刀片陷阱", "Power Bank": "移动电源", "Generator": "发电机",
    "JetPack Mod": "喷气背包改装件", "Increased Battery Mod": "扩容电池改装件",
    "Advanced Electronics Mod": "高级电子改装件",
    "Reactor tool booster Mod": "反应堆工具强化改装件",
    "Reactor accelerator Mod": "反应堆加速改装件", "Electro Mod": "电能改装件",
    "chainsaw": "链锯", "auger": "螺旋钻", "Switch": "开关",

    # Vehicles and armor.
    "Vehicle Advanced Fuel System Mod": "载具高级燃油系统改装件",
    "Vehicle Reserve Fuel Tank Mod": "载具备用油箱改装件",
    "Vehicle Super Charger Mod": "载具机械增压器改装件",
    "Vehicle Terrible Plow Mod": "载具凶悍撞犁改装件",
    "Vehicle Terrible Armor Mod": "载具凶悍护甲改装件",
    "Vehicle Fuel Saver Mod": "载具节油器改装件", "Vehicle Armor Mod": "载具护甲改装件",
    "Armor Crafting Kit": "护甲制作套件", "Armor Quad Pocket Mod": "护甲四联口袋改装件",
    "Armor Headset Mod": "护甲耳机改装件", "«Universal» Shoes Mod": "通用鞋具改装件",
    "Stabilizer of stability Mod": "稳定装置改装件", "Reliable Winding Mod": "可靠上链改装件",
    "Nerd Bonus Mod": "书呆子加成改装件", "Ok Google Mod": "语音助手改装件",
    "Best Shoes Mod": "全地形鞋具改装件", "Radar Mod": "雷达改装件",
    "[DECEA3]Head[-] Mod": "[DECEA3]头部[-]改装件",
    "[DECEA3]Chest[-] Mod": "[DECEA3]胸部[-]改装件",
    "[DECEA3]Hands[-] Mod": "[DECEA3]手部[-]改装件",
    "[DECEA3]Feet[-] Mod": "[DECEA3]足部[-]改装件",

    # Medicine and weapon upgrades.
    "Medication «ANTIRAD»": "抗辐射药剂", "Bonus storage": "额外储物空间",
    "Vitamins": "维生素", "Antidote": "解毒剂", "Painkillers": "止痛药",
    "Poisoned Blades Mod": "毒刃改装件", "Machine Gun Parts Mod": "机枪零件改装件",
    "Drum Magazine Mod": "弹鼓改装件", "Gun Booster Mod": "枪械强化改装件",
    "Shotgun Parts Mod": "霰弹枪零件改装件", "Fore Grip Mod": "前握把改装件",
    "Cripple 'Em Mod": "致残改装件", "Hand Weapons": "手枪类武器",
    "Pistol Parts Mod": "手枪零件改装件", "Magazine Extender Mod": "扩容弹匣改装件",
    "Rad Remover Mod": "辐射清除改装件", "Rifle Parts Mod": "步枪零件改装件",
    "Bipod Mod": "两脚架改装件", "Silencer Mod": "消音器改装件",
    "Damage Accumulator Mod": "伤害蓄积器改装件", "Turret Parts Mod": "炮塔零件改装件",
    "Reactor Amplifier Mod": "反应堆增幅器改装件", "Heavy Tip Mod": "加重弹头改装件",
    "Huge Supply Mod": "巨型弹仓改装件", "Heavy Melee Weapon": "重型近战武器",
    "Light Melee Weapon": "轻型近战武器",
    "Diamond Reinforcement Mod Heavy": "重型武器钻石加固改装件",
    "Diamond Reinforcement Mod Light": "轻型武器钻石加固改装件",
    "Hunter Mod Heavy": "重型武器猎手改装件", "Terrible Spikes Mod Heavy": "重型武器凶悍尖刺改装件",
    "Hunter Mod": "猎手改装件", "Terrible Spikes Mod": "凶悍尖刺改装件",
    "Ergonomic Handle Mod": "人体工学握把改装件", "Burning Shaft Mod": "燃烧握柄改装件",
    "Improved Armor": "改良型护甲", "Weapon": "武器",

    # Farming and high-tier food.
    "Goldenrod and Chrysanthemum": "一枝黄花与菊花", "Advanced selection": "高级精选",
    "Farmer Station": "农夫工作站", "Hydroponic Station": "水培工作站",
    "Super Elixir: Quest Runner": "超级灵药：任务达人",
    "Super Elixir: Melee Master": "超级灵药：近战大师",
    "Super Elixir: Master Miner": "超级灵药：采矿大师",
    "Super Elixir: Master of Trade": "超级灵药：贸易大师",
    "Super Elixir: Horde Expert": "超级灵药：尸潮专家",
    "Fertilizer": "肥料", "Seedling": "幼苗", "Selection": "精选",
    "Cotton": "棉花", "Goldenrod": "一枝黄花", "Chrysanthemum": "菊花",
    "Coffee": "咖啡", "Yucca": "丝兰", "Aloe": "芦荟", "Blueberry": "蓝莓",
    "Hop": "啤酒花", "Pumpkin": "南瓜", "Mushroom": "蘑菇", "Corn": "玉米",
    "Potato": "马铃薯",
}


TIER_UNLOCK_STYLES = {
    "[DECEA3]Improved[-]": "[DECEA3]改良型[-]",
    "[DECEA3]«Experimental»[-]": "[DECEA3]实验型[-]",
    "[DECEA3]Experimental[-]": "[DECEA3]实验型[-]",
    "[FF6666]«Experimental»[-]": "[FF6666]实验型[-]",
    "[FFB800]Unique[-]": "[FFB800]独特[-]",
    "[FFB800]«Rare»[-]": "[FFB800]稀有[-]",
    "[FFB800]reactor[-]": "[FFB800]反应堆型[-]",
    "[FFB800]«Reactor»[-]": "[FFB800]反应堆型[-]",
    "[FFB800]«Reactor» Auger[-]": "[FFB800]反应堆型螺旋钻[-]",
    "[FFB800]«Reactor» Impact Driver[-]": "[FFB800]反应堆型冲击起子[-]",
    "[FFB800]«Portable» Reactor[-]": "[FFB800]便携式反应堆[-]",
    "[DECEA3]«Industrial»[-]": "[DECEA3]工业型[-]",
    "[FF6666]Boosts[-]": "[FF6666]强化[-]",
    "[F0B18A]Legendary[-]": "[F0B18A]传奇[-]",
}


def translate_projectz_tier_unlocks() -> None:
    """Rebuild every perk-screen tier unlock from its English source text."""
    path = ROOT / "01-ProjectZ/Config/Localization.csv"
    lines = path.read_text(encoding="utf-8-sig").splitlines(keepends=True)
    header = next(csv.reader([lines[0]])); columns = {name.lower(): i for i, name in enumerate(header)}
    e, z = columns["english"], columns["schinese"]; changed = 0; unresolved = []
    replacements = sorted(TIER_UNLOCK_PHRASES.items(), key=lambda pair: len(pair[0]), reverse=True)
    styles = sorted(TIER_UNLOCK_STYLES.items(), key=lambda pair: len(pair[0]), reverse=True)
    for index in range(1, len(lines)):
        line = lines[index]; ending = "\r\n" if line.endswith("\r\n") else "\n" if line.endswith("\n") else ""
        row = next(csv.reader([line.removesuffix(ending)])); match = re.fullmatch(r"Tier\s*(\d+):\s*(.+)", row[e])
        if not match: continue
        tier, translated = match.groups()
        for source, target in styles: translated = translated.replace(source, target)
        for source, target in replacements: translated = translated.replace(source, target)
        translated = translated.replace("Auto Turret.44", ".44口径自动炮塔")
        translated = translated.replace("自动炮塔 7.62 mm", "7.62毫米自动炮塔")
        translated = translated.replace("自动炮塔 9 mm", "9毫米自动炮塔")
        translated = translated.replace(", ", "、").replace(" mm", "毫米")
        translated = translated.replace("[-] ", "[-]").replace(" [", "[")
        for style in ("[DECEA3]改良型[-]", "[FF6666]实验型[-]"):
            for weapon in ("9毫米自动炮塔", ".44口径自动炮塔", "7.62毫米自动炮塔", "霰弹枪自动炮塔"):
                translated = translated.replace(f"{weapon}{style}", f"{style}{weapon}")
        translated = re.sub(r"\s+([-.,，])", r"\1", translated)
        translated = re.sub(r"\s{2,}", " ", translated).strip()
        updated = f"第{tier}级：{translated}"
        residue = re.sub(r"\[[0-9A-Fa-f-]+\]", "", translated)
        residue = re.sub(r"(?:LSS|ANTIRAD|Defender-Z|mm)\b", "", residue)
        if re.search(r"[A-Za-z]{2,}", residue): unresolved.append((row[0], residue))
        if row[z] == updated: continue
        row[z] = updated; output = io.StringIO(); csv.writer(output, lineterminator=ending, quoting=csv.QUOTE_MINIMAL).writerow(row)
        lines[index] = output.getvalue(); changed += 1
    if unresolved:
        raise RuntimeError(f"Untranslated perk tier text: {unresolved[:8]}")
    with path.open("w", encoding="utf-8-sig", newline="") as handle: handle.write("".join(lines))
    print(f"01-ProjectZ/Config/Localization.csv: rebuilt {changed} tier unlock entries")


translate_projectz_tier_unlocks()


PROJECTZ_FINAL_DISPLAY = {
    "bossMajor": "[FFB800]小型Boss[-]少校",
    "bossBrigadier": "[FFB800]小型Boss[-]准将",
    "bossChief": "[FFB800]小型Boss[-]首领",
    "bossAuthority": "[FFB800]小型Boss[-]大佬",
    "bossProfessor": "[FFB800]小型Boss[-]教授",
    "bossAlex": "[FFB800]小型Boss[-]亚历克斯",
    "bossAlexHalf": "[FFB800]小型Boss[-]亚历克斯之影",
    "bossBiba": "[FFB800]小型Boss[-]比巴",
    "bossDevourer": "[FFB800]小型Boss[-]吞噬者",
    "DevourerMinion": "[94CBFF]仆从[-]吞噬者",
    "bossBearDaddy": "[FFB800]小型Boss[-]熊王老爹",
    "BearDaddyMinion": "[94CBFF]仆从[-]熊王老爹",
    "bossGargul": "[FFB800]小型Boss[-]石像魔",
    "GargulMinion": "[94CBFF]仆从[-]石像魔",
    "bossNagaina": "[FFB800]小型Boss[-]娜迦蛇后",
    "NagainaMinion": "[94CBFF]仆从[-]娜迦蛇后",
    "bossBitch": "[FFB800]小型Boss[-]悍妇",
    "bossMystic": "[FFB800]小型Boss[-]秘法师",
    "bossBurningFlesh": "[FFB800]小型Boss[-]燃烧之肉",
    "bossCholera": "[FFB800]小型Boss[-]霍乱体",
    "BossMutantHog": "[FFB800]Boss[-]突变野猪",
    "BossHotHarry": "[FFB800]Boss[-]烈焰哈里",
    "zombieHotHarryMinion": "[94CBFF]仆从[-]烈焰哈里",
    "bossBull": "[FFB800]Boss[-]蛮牛",
    "bossShocker": "[FFB800]Boss[-]电击者",
    "bossVeteran": "[FFB800]Boss[-]老兵",
    "bossAncientYeti": "[FFB800]Boss[-]远古雪人",
    "bossProjectZ": "[E092FF]终极Boss[-]Project Z",
    "bossCarrier": "[FF6666]Z型Boss[-]母体",
    "zombieCarrierDef": "[94CBFF]守卫者[-]母体",
    "zombieCarrierActive": "[94CBFF]激活者[-]母体",
    "smallMiniBossLootContainer": "[DECEA3]小型Boss[-]战利品",
    "MiniBossLootContainer": "[DECEA3]小型Boss[-]战利品",
    "ChargedEliteLootContainer": "充能战利品",
    "zombieFatHawaiianChargedElite": "[71FFFF]充能精英[-]游客",
    "buffNomadSetBonusDesc": "这是招待帐篷外来客的最高规格。\\n\\n霰弹枪造成的伤害提高{cvar(.armorNomadFSBDisplay:0)}%\\n可抵抗[DECEA3]1[-]种主动辐射效果。",
    "meleeWpnClubT4SteelClubImpDesc": "击杀敌人获得的经验提高[DECEA3]25%[-]。\\n[DECEA3]僵尸：[-]医生，我的头怎么了？\\n[DECEA3]医生：[-]没什么，你根本就没有头。\\n\\n[DECEA3]修理需要改良型修理包。[-]",
    "meleeWpnSpearT4SteelSpearImpDesc": "击杀敌人获得的经验提高[DECEA3]25%[-]。\\n想吃什么口味，就串什么口味。\\n\\n[DECEA3]修理需要改良型修理包。[-]",
    "armorAssassinHelmetDesc": "中甲头盔\\n敌人根本察觉不到你的到来。潜行时造成更多伤害。\\n\\n全套装加成：提高狙击步枪造成的伤害。",
    "buffNerdDeskDesc": "你正在学习。\\n手持书籍、杂志或配方时，每[DECEA3]3[-]秒获得少量经验。\\n[DECEA3]智力大师[-]达到4级或以上时，获得的经验翻倍。\\n阅读杂志时，额外获得1点技能点的几率提高[DECEA3]5%[-]。",
    "buffNerdTableDesc": "你正在进行高级学习。\\n手持书籍、杂志或配方时，每[DECEA3]3[-]秒获得少量经验。\\n[DECEA3]智力大师[-]达到4级或以上时，获得的经验翻倍。\\n阅读杂志时，额外获得1点技能点的几率提高[DECEA3]10%[-]。",
    "modMeleeErgonomicGripImpDesc": "改良型人体工学握把：耐力消耗降低[DECEA3]15%[-]，近战攻击速度提高[DECEA3]30%[-]；同时改善弓与弩的操控性和射速。\\n\\n[DECEA3]矿脉之母[-]或[DECEA3]箭术[-]技能可提高改装件品质。",
    "modMeleeErgonomicGripUniqueDesc": "独特人体工学握把：耐力消耗降低[DECEA3]25%[-]，近战攻击速度提高[DECEA3]50%[-]；同时改善弓与弩的操控性和射速。",
    "buffXPBonusTooltip": "获得的经验提高5%。",
    "buffRoughGroundBaseIncDesc": "在崎岖不平的地面上行走已经很困难，更别说奔跑了。\\n\\n步行速度降低[DECEA3]50%[-]，奔跑速度降低[DECEA3]15%[-]。\\n\\n[DECEA3]穿上任意鞋类即可改善行动能力。[-]",
    "modGunBoosterExpDesc": "实验型改装件。自动武器射速提高[DECEA3]25%[-]。\\n\\n按[DECEA3][ F ][-]启用后，射速额外提高[DECEA3]25%[-]，但武器耐久损耗速度也会提高[DECEA3]25%[-]。",
    "modIrradiationResistImpDesc": "遭受辐射后，懂得如何净化装备才能保住性命。该套件包含净化与恢复所需的一切。装备后，辐射恢复速度会根据当前辐射程度提高：低于[DECEA3]25%[-]时提高[DECEA3]50%[-]；低于[DECEA3]50%[-]时提高[DECEA3]100%[-]；低于[DECEA3]75%[-]时提高[DECEA3]150%[-]；达到临界辐射程度时提高[DECEA3]200%[-]。只有在未持续受到主动辐射时才会恢复。\\n\\n[DECEA3]安装于胸甲。[-]",
    "modIrradiationResistUniqueDesc": "科学家能在最危险区域长时间活动的秘密终于揭晓：他们懂得如何从辐射中正确恢复。该套件包含净化与恢复所需的一切。携带后，辐射恢复速度会根据当前辐射程度提高：低于[DECEA3]25%[-]时提高[DECEA3]200%[-]；低于[DECEA3]50%[-]时提高[DECEA3]300%[-]；低于[DECEA3]75%[-]时提高[DECEA3]400%[-]；达到临界辐射程度时提高[DECEA3]500%[-]。只有在未持续受到主动辐射时才会恢复。\\n\\n[DECEA3]安装于胸甲。[-]",
    "CallBullDesc": "激活召唤[FFB800]蛮牛[-]的任务。\\n\\n[DECEA3]“僵尸专家”达到5级后解锁。[-]",
    "CallShockerDesc": "激活召唤[FFB800]电击者[-]的任务。\\n\\n[DECEA3]“僵尸专家”达到5级后解锁。[-]",
    "CallVeteranDesc": "激活召唤[FFB800]老兵[-]的任务。\\n\\n[DECEA3]“僵尸专家”达到5级后解锁。[-]",
    "CallCarrierDesc": "激活召唤[C65F5F]母体[-]的任务。\\n\\n[DECEA3]“僵尸专家”达到7级后解锁。[-]",
    "LunchIntelligenceT3Desc": "一份丰盛套餐，能显著增强你对周围环境的感知。\\n\\n食用后获得：\\n- 耐力与生命上限[DECEA3]+40[-]\\n- “感知”属性等级[DECEA3]+3[-]\\n- “废土”被动技能[DECEA3]+2[-]\\n- 战利品品质[DECEA3]+20[-]\\n- 狙击步枪与爆炸物伤害[DECEA3]+25%[-]\\n- 长矛伤害[DECEA3]+50%[-]\\n\\n立即恢复一半饱食度与水分。",
    "buffIntelligenceT3Desc": "一份丰盛套餐，能显著增强你对周围环境的感知。\\n\\n食用后获得：\\n- 耐力与生命上限[DECEA3]+40[-]\\n- “感知”属性等级[DECEA3]+3[-]\\n- “废土”被动技能[DECEA3]+2[-]\\n- 战利品品质[DECEA3]+20[-]\\n- 狙击步枪与爆炸物伤害[DECEA3]+25%[-]\\n- 长矛伤害[DECEA3]+50%[-]\\n- 身处废土时温度抗性[DECEA3]+10[-]\\n\\n立即恢复一半饱食度与水分。",
    "questRewardPistolBundleT2Desc": "一把马格南手枪和一些.44口径弹药",
    "BuffGargulAOEIncDesc": "他们曾围着篝火低声谈论它。如今，它来了。利爪投下长长的阴影，呼吸中散发着腐烂肺脏的恶臭。你感觉自己的理智正在背叛你——心脏仿佛跳到了嗓子眼，手指渐渐麻木，太阳穴里只剩一个念头反复撞击……\\n快逃，趁一切还来得及。\\n\\n[FFB800]特殊属性：[-]\\n石像魔从不独自飞行，遇到危险时会召唤同类。\\n待在它附近会逐渐陷入疯狂：理智会一秒一秒地流失。\\n\\n快跑，趁双腿还听使唤。",
    "buffDestroyedStoneBaseIncDesc": "脚下满是垃圾，走路时最好看清落脚处。\\n\\n移动速度降低[DECEA3]25%[-]，跳跃高度降低[DECEA3]15%[-]，奔跑时的耐力消耗提高[DECEA3]50%[-]。\\n\\n警告：在危险地面上奔跑时，有[DECEA3]25%[-]几率刺伤脚部。\\n\\n[DECEA3]装备《鞋底防护》改装件可改善机动性。[-]",
    "expReconnaissance_StartDesc": "«队伍已经集结，装备也已送达。我们随时可以出发。我现在就派出探险队。任务完成后，你可以在任意商人处领取他们找到的物资。»\\n[DECEA3]——怀尔德[-]\\n\\n成功率：[DECEA3]100%[-]\\n耗时：[DECEA3]45分钟[-]",
    "expActivity_StartDesc": "«队伍已经集结，装备也已送达。我们随时可以出发。我现在就派出探险队。任务完成后，你可以在任意商人处领取他们找到的物资。»\\n[DECEA3]——怀尔德[-]\\n\\n成功率：[DECEA3]80%[-]\\n耗时：[DECEA3]60分钟[-]",
    "expInfection_StartDesc": "«队伍已经集结，装备也已送达。我们随时可以出发。我现在就派出探险队。任务完成后，你可以在任意商人处领取他们找到的物资。»\\n[DECEA3]——怀尔德[-]\\n\\n成功率：[DECEA3]60%[-]\\n耗时：[DECEA3]90分钟[-]",
    "expDeadzone_StartDesc": "«队伍已经集结，装备也已送达。我们随时可以出发。我现在就派出探险队。任务完成后，你可以在任意商人处领取他们找到的物资。»\\n[DECEA3]——怀尔德[-]\\n\\n成功率：[DECEA3]40%[-]\\n耗时：[DECEA3]120分钟[-]",
    "Ingredients_WorkstationT2": "材料 [71FFE7]合约：[-]改良型工作台",
    "Cont_WorkstationT2": "[71FFE7]合约：[-]改良型工作台",
    "Quest_WorkstationT2Name": "[71FFE7]合约：[-]改良型工作台",
    "Cont_WorkstationT4": "[71FFE7]合约：[-]改良型锻炉",
    "Quest_WorkstationT4Name": "[71FFE7]合约：[-]改良型锻炉",
    "bowsT1": "改良型弓弩",
    "BetterWeapon_ImpModsMelee": "[DECEA3]改良型[-]近战武器改装件",
    "BetterWeapon_ImpModsRange": "[DECEA3]改良型[-]远程武器改装件",
    "BetterWeapon_UniqueModsMelee": "[FFB800]独特[-]近战武器改装件",
    "MasterArmorImp": "[DECEA3]改良型[-]护甲",
    "MasterArmorUniq": "[FFB800]独特[-]护甲",
    "exp_ArmorBundleRanger": "游骑兵护甲套装",
    "exp_ArmorBundleCommando": "突击队护甲套装",
    "modArmorHeadset": "[DECEA3]改良型[-]护甲耳机改装件",
    "recycler": "回收站",
    "modStabilityBooster": "[FFB800]独特[-]稳定装置改装件",
    "modOkGoogle": "[FFB800]独特[-]语音助手改装件",
    "questRangeT2_Pistol": "[FF6666]任务[-]马格南手枪",
    "RangeT2_PistolName": "[DECEA3]获得[-]马格南手枪",
    "SkillPointBundle5": "[DECEA3]5[-]技能点礼包",
    "SkillPointBundle25": "[DECEA3]25[-]技能点礼包",
    "Reward_MeleeT1_Spear": "铁矛礼包", "Reward_MeleeT1_Club": "棒球棒礼包",
    "Reward_MeleeT1_Sledgehammer": "铁制大锤礼包", "Reward_MeleeT1_Knuckles": "铁制拳套礼包",
    "Reward_RangeT1_MachineGun": "AK-47礼包", "Reward_MeleeT2_Club": "钢制棍棒礼包",
    "Reward_MeleeT2_Sledgehammer": "钢制大锤礼包", "Reward_MeleeT2_Knuckles": "钢制拳套礼包",
    "Reward_MeleeT2_Knife": "砍刀礼包", "Reward_MeleeT2_StunBaton": "电击棒礼包",
    "Reward_RangeT2_Shotgun": "泵动霰弹枪礼包", "Reward_RangeT2_Archery": "铁弩礼包",
    "Reward_RangeT2_Pistol": "马格南手枪礼包", "Reward_RangeT3_Rifle": "狙击步枪礼包",
    "Reward_RangeT3_Shotgun": "自动霰弹枪礼包", "Reward_RangeT3_MachineGun": "M60礼包",
    "Reward_RangeT3_Crossbow": "复合弩礼包", "Reward_RangeT3_SMG": "SMG-5礼包",
    "Reward_RangeT3_DesertVulture": "沙漠秃鹫礼包",
    "questRewardMachineGunBundleT1": "AK-47礼包", "questRewardPistolBundleT2": "马格南手枪礼包",
    "questRewardShotgunBundleT2": "泵动霰弹枪礼包", "questRewardPistolBundleT3": "SMG-5礼包",
    "questRewardShotgunBundleT3": "自动霰弹枪礼包", "questRewardRifleBundletT3": "狙击步枪礼包",
    "questRewardMachineGunBundleT3": "M60礼包",
    "questRewardKnucklesImpT4": "[DECEA3]改良型[-]钢制拳套礼包",
    "questRewardClubImpBundleT4": "[DECEA3]改良型[-]钢制棍棒礼包",
    "questRewardSpearImpBundleT4": "[DECEA3]改良型[-]钢矛礼包",
    "questRewardPlasmaBatonBundletT4": "[DECEA3]改良型[-]等离子电击棒礼包",
    "questRewardMacheteImpBundleT4": "[DECEA3]改良型[-]砍刀礼包",
    "questRewardSledgehammerImpT4": "[DECEA3]改良型[-]钢制大锤礼包",
    "questRewardSledgehammerDestructorT6": "[FFB800]毁灭者[-]大锤礼包",
    "questRewardPistolBundleT5": "[DECEA3]改良型[-]SMG-5礼包",
    "questRewardShotgunBundleT5": "[DECEA3]改良型[-]自动霰弹枪礼包",
    "questRewardRifleBundletT5": "[DECEA3]改良型[-]狙击步枪礼包",
    "questRewardMachineGunBundleT5": "[DECEA3]改良型[-]M60礼包",
    "questRewardMacheteIndianaT6": "[FFB800]印第安纳[-]砍刀礼包",
    "questRewardAxeBarbarianT6": "[FFB800]野蛮人[-]斧礼包",
    "questRewardPistolBundleT6": "[FFB800]毒刺[-]SMG-5礼包",
    "questRewardShotgunBundleT6": "[FFB800]抹除者[-]自动霰弹枪礼包",
    "questRewardRifleBundletT6": "[FFB800]高斯[-]狙击步枪礼包",
    "questRewardMachineGunBundleT6": "[FFB800]斗牛犬[-]机枪礼包",
    "questRewardPistolBundleT7": "[F0B18A]传奇[-][FFB800]毒刺[-]SMG-5礼包",
    "questRewardShotgunBundleT7": "[F0B18A]传奇[-][FFB800]抹除者[-]自动霰弹枪礼包",
    "questRewardRifleBundletT7": "[F0B18A]传奇[-][FFB800]高斯[-]狙击步枪礼包",
    "questRewardMachineGunBundleT7": "[F0B18A]传奇[-][FFB800]斗牛犬[-]机枪礼包",
    "modEnhancedSpearDesc": "改良型护甲专用强化改装件，安装于手套。\\n\\n穿戴全套改良型护甲时，提高长矛熟练度。",
    "modEnhancedClubDesc": "改良型护甲专用强化改装件，安装于手套。\\n\\n穿戴全套改良型护甲时，提高棍棒熟练度。",
    "modEnhancedSledgehammerDesc": "改良型护甲专用强化改装件，安装于手套。\\n\\n穿戴全套改良型护甲时，提高大锤熟练度。",
    "modEnhancedKnucklesDesc": "改良型护甲专用强化改装件，安装于手套。\\n\\n穿戴全套改良型护甲时，提高拳套熟练度。",
    "modEnhancedKnifeDesc": "改良型护甲专用强化改装件，安装于手套。\\n\\n穿戴全套改良型护甲时，提高刀具与砍刀熟练度。",
    "modEnhancedStunBatonDesc": "改良型护甲专用强化改装件，安装于手套。\\n\\n穿戴全套改良型护甲时，提高电击棒熟练度。",
    "toolBellowsImpDesc": "改良型锻炉专用铁匠风箱，使冶炼速度提高[DECEA3]75%[-]。",
    "toolAnvilImpDesc": "改良型锻炉专用铁砧，使制作速度提高[DECEA3]75%[-]。",
    "UniqueModsRangeBundleDesc": "内含一个随机的[FFB800]独特[-]远程武器改装件。",
    "MeleeDestructor": "毁灭者", "UniqueDestructorBundle": "[F0B18A]传奇[-][FFB800]《毁灭者》[-]",
    "buffDestructorReloadName": "毁灭者装填", "buffBikerSetBonus": "摩托骑手",
    "buffBikerSemiSetBonusTooltip": "摩托骑手半套装加成已生效", "ObjectiveBiker_key": "摩托骑手",
    "modMeleeTemperedBladeDesc": "这种刀刃改装件可提高方块伤害并降低耐久损耗。\\n\\n[DECEA3]高级工程[-]技能可提高改装件品质。",
    "modMeleeStructuralBraceDesc": "这种改装件可降低近战武器、工具与弓的耐久损耗。\\n\\n[DECEA3]矿脉之母[-]技能可提高改装件品质。",
    "modMeleeDiamondTipDesc": "这种改装件可降低手持工具与刀刃武器的耐久损耗。\\n\\n[DECEA3]高级工程[-]技能可提高改装件品质。",
    "Quest_RepairT2Offer": "«妥善修理能延长任何装备的使用寿命。消灭一些[DECEA3]僵尸[-]，从它们身上寻找修理零件。收集齐所需资源，准备好后回来找我。作为报酬，你会获得一批改良型修理包、传奇零件或实用资源。要是愿意，我还可以分享几本有关弹药和护甲的杂志。»\\n\\n[DECEA3]——休[-]",
    "Quest_RepairT3Offer": "«彻底检修能大幅延长装备的使用寿命。消灭一些[DECEA3]僵尸[-]，从它们身上寻找修理零件。收集齐所需资源，准备好后回来找我。作为报酬，你会获得一批传奇修理包、传奇零件或实用资源。要是愿意，我还可以分享几本有关弹药和护甲的杂志。»\\n\\n[DECEA3]——休[-]",
    "Quest_StupidBet4Offer": "«我一直都说，人们对脱衣舞俱乐部的需求一点不比药品或警察少。年轻时我也误闯过一家，那里的舞娘可真够特别。我喝了点酒，打算和她们一起跳舞，于是脱掉衣服去搭讪。正上头时，我才发现其中一人胯下还藏着个大家伙。原来她们是来交换学习、顺便兼职的越南“学生”……真够吓人的。最后保安很快就把光着身子的我扔到了街上。\\n那家俱乐部就在污染区中心，不过这些家伙早已散布到附近。总之，我敢打赌你绝不会想看她们的表演。把遇到的都干掉，我不会亏待你。»\\n\\n[DECEA3]——醉酒大师[-]\\n\\n[DECEA3]不穿戴任何装备，使用近战武器击杀8名脱衣舞娘和2名摩托骑手。[-]",
    "Quest_MedicineT1Offer": "«很高兴看到你身体还不错——真的是这样吗？不管怎样，我知道该怎么改善。去消灭几名[DECEA3]护士[-]，仔细搜查尸体；她们身上或许带着有用的药品。处理妥当后回来告诉我。»\\n\\n[DECEA3]——珍[-]",
    "Quest_ArmorT1Offer": "«想要一套好护甲，就得准备好亲自收集材料。猎杀一些[DECEA3]兔子[-]或[DECEA3]蛇[-]并剥取材料，收集齐所需资源后回来找我。作为报酬，你会获得大量丝线和实用资源。要是愿意，我还可以分享几本有关弹药与护甲的杂志。»\\n\\n[DECEA3]——休[-]",
    "Quest_ArmorT2Offer": "«坚固的护甲需要更好的材料。猎杀一些[DECEA3]狼[-]、[DECEA3]郊狼[-]或[DECEA3]野猪[-]并剥取材料，收集齐所需资源后回来找我。作为报酬，你会获得一批护甲制作套件和实用资源。要是愿意，我还可以分享几本有关弹药与护甲的杂志。»\\n\\n[DECEA3]——休[-]",
    "Quest_ArmorT3Offer": "«稀有护甲需要大量稀有材料。猎杀一些[DECEA3]熊[-]或[DECEA3]美洲狮[-]并剥取材料，收集齐所需资源后回来找我。作为报酬，你会获得一批改良型护甲制作套件和实用资源。要是愿意，我还可以分享几本有关装备与护甲的杂志。»\\n\\n[DECEA3]——休[-]",
    "PortableReactorDesc": "实验型能源技术，也是功率最强、续航最久的能源。[DECEA3]连钢铁侠都可以歇一歇了。[-]\\n\\n用于制作反应堆工具并为大功率设备供能。",
    "AutoMinerIronHasItemText": "{0} 开启{1}（[CFEDFF]铁矿储量[-]）",
    "AutoMinerClayHasItemText": "{0} 开启{1}（[CFEDFF]黏土储量[-]）",
    "MasterArmorDesc": "制作技能[DECEA3]护甲 等级100[-]可让你制作[DECEA3]护甲大师[-]杂志。\\n阅读这些杂志可提高所制作护甲的品质，并解锁高级护甲改装件。",
    "modGunMeleeRadRemoverUniqueDesc": "独特改装件。可暂时封锁所有敌人的再生与辐射能力，并阻止Boss恢复生命值。",
    "modGunCrippleEmUniqueDesc": "独特改装件。命中目标时更容易致残或打断肢体，并必定降低目标的防御与伤害输出。",
    "modGunForegripUniqueDesc": "独特改装件。显著提高腰射或移动射击时的操控性与精准度。",
    "modGunBipodUniqueDesc": "独特改装件。显著提高瞄准射击时的精准度与操控性。",
    "modGunSoundSuppressorSilencerUniqueDesc": "独特改装件。显著降低枪声，最大射程提高[DECEA3]10%[-]，潜行伤害加成提高[DECEA3]150%[-]。",
    "modGunBoosterUniqueDesc": "独特改装件。自动武器射速提高[DECEA3]50%[-]。\\n\\n按[DECEA3][ F ][-]启用后，射速额外提高[DECEA3]50%[-]，但武器故障率也会提高[DECEA3]50%[-]。",
    "modVehicleArmorUniqueDesc": "独特改装件。显著降低载具正面撞击物体时受到的伤害，并大幅提高对方块与生物的撞击伤害。\\n可安装在[DECEA3]摩托车[-]或[DECEA3]旋翼机[-]上。",
    "BetterWeaponMeleeImpModsOffer": "«近战武器改装件吗？还算合理，虽然这东西确实挑人。使用任意近战武器消灭[FF6666]500只僵尸[-]，我就给你一个适配该武器的[DECEA3]改良型[-]改装件。» [DECEA3]——怀尔德[-]",
    "BetterWeaponRangeImpModsOffer": "«远程武器改装件吗？还算合理，虽然这东西确实挑人。使用任意远程武器消灭[FF6666]500只僵尸[-]，我就给你一个适配该武器的[DECEA3]改良型[-]改装件。» [DECEA3]——怀尔德[-]",
    "BetterWeaponUniqueModsOffer": "[DECEA3]独特改装件[-]\\n\\n«优质改装件总能强化武器，而独特改装件更能让它威力倍增。消灭[FF6666]1500只僵尸[-]，我就告诉你如何取得近战或远程武器的独特改装件，由你选择。» [DECEA3]——怀尔德[-]",
    "ResCollector6Offer": "[FAFF63]第6章：实用加成[-]\\n\\n«就连受感染的建筑工也可能带着工具零件，见到就解决掉！消灭足够数量后收集资源，并交给任意商人。你可以任选一种反应堆工具充电站，还会获得一个随机的独特工具改装件。» [DECEA3]——休[-]",
    "Quest_AmmoT3Offer": "«想搜刮大量实用资源，就得准备迎战一大波尸潮，自然也需要大量弹药。消灭一些[DECEA3]士兵[-]和[DECEA3]精英僵尸[-]。完成后，我会给你大量火药和所需口径的弹药。»\\n\\n[DECEA3]——乔尔[-]",
    "Quest_WeaponClubOffer": "«一根好棍棒足以让任何闹事者安静下来。使用棍棒消灭一些[DECEA3]僵尸[-]。完成后，我很乐意分给你一些棍棒零件和几本有趣的杂志。»\\n\\n[DECEA3]——乔尔[-]",
    "Quest_WeaponBatonOffer": "«这类电击棒不只能烧灼目标，还能把它彻底电熟。使用电击棒消灭一些[DECEA3]僵尸[-]。完成后，我很乐意分给你一些电击棒零件和几本有趣的杂志。»\\n\\n[DECEA3]——乔尔[-]",
    "resourceBulletBundleDesc": "一包压缩整齐的弹头，使用后可拆包取出。",
    "Ingredients_WorkstationT4": "材料 [71FFE7]合约：[-]改良型锻炉",
    "Cont_WorkstationT4Desc": "启动[71FFE7]合约：[-]改良型锻炉\\n\\n完成合约后，你将获得一座改良型锻炉。",
    "Quest_WorkstationT4Offer": "«好铁匠向来抢手，但光有手艺还不够，你还需要一座像样的锻炉。先让我看看你的双手到底有多大本事。收集齐足够资源后，我可以分你一座锻炉。»\\n\\n[DECEA3]——达·芬奇[-]",
    "perkIntellectMasteryRank2LongDesc": "在战利品中找到书籍、杂志或设计图的几率提高[DECEA3]25%[-]，并可制作杂志增效剂。",
    "AutoMinerIron": "[CFEDFF]铁矿[-]自动采矿机", "bossCarrier": "[FF6666]Z型Boss[-]母体",
    "cntHardenedChestSecureStorage": "[DECEA3]储物：[-]强化储物箱",
    "MasterBuilderT3-4": "[DECEA3]储物：[-]强化储物箱",
    "armorRescuerOutfit": "[FFB800]救援者[-]胸甲", "armorRescuerGloves": "[FFB800]救援者[-]手套",
    "armorRescuerBoots": "[FFB800]救援者[-]腿甲",
    "armorPredatorOutfit": "[FFB800]捕食者[-]胸甲", "armorPredatorGloves": "[FFB800]捕食者[-]手套",
    "armorPredatorBoots": "[FFB800]捕食者[-]腿甲",
    "armorPlundererOutfit": "[FFB800]掠夺者[-]胸甲", "armorPlundererGloves": "[FFB800]掠夺者[-]手套",
    "armorPlundererBoots": "[FFB800]掠夺者[-]腿甲",
    "armorSonnyOutfit": "[FFB800]桑尼[-]胸甲", "armorSonnyGloves": "[FFB800]桑尼[-]手套",
    "armorSonnyBoots": "[FFB800]桑尼[-]腿甲",
    "armorMausOutfit": "[FFB800]鼠王[-]胸甲", "armorMausGloves": "[FFB800]鼠王[-]手套",
    "armorMausBoots": "[FFB800]鼠王[-]腿甲",
    "modRareArmorARResistOutfit": "守卫者-Z改装件：[DECEA3]胸部[-]",
    "modArmorLSSOutfit": "LSS-1改装件：[DECEA3]胸部[-]",
    "modRareArmorARResistHelmet": "守卫者-Z改装件：[DECEA3]头部[-]",
    "modRareArmorARResistGloves": "守卫者-Z改装件：[DECEA3]手部[-]",
    "modRareArmorARResistBoots": "守卫者-Z改装件：[DECEA3]足部[-]",
    "modArmorLSSHelmet": "LSS-1改装件：[DECEA3]头部[-]",
    "modArmorLSSGloves": "LSS-1改装件：[DECEA3]手部[-]",
    "modArmorLSSBoots": "LSS-1改装件：[DECEA3]足部[-]",
    "modRareArmorARResistGlovesDesc": "实验型手臂护甲涂层，可抵御大多数负面效果。\\n物理防护[DECEA3]+2[-]\\n辐射伤害[DECEA3]-10%[-]\\n温度抗性[DECEA3]+5[-]\\n提高辐射恢复速度\\n\\n全套防护改装件：\\n[DECEA3]可抵抗2种主动辐射效果。\\n免疫任何风暴第二阶段的伤害与负面效果。[-]",
    "perkCombosMeleeDesc": "使用近战武器时有几率触发连击，完成连击后会根据难度获得随机加成。触发时有[DECEA3]50%[-]几率升级为进阶连击；若成功，还会再以[DECEA3]50%[-]几率升级为困难连击。连击越难，加成越高。",
    "perkGoodSowingDesc": "正确播种是丰收的关键。提高收获的资源数量，并使收获时有几率获得额外种子。",
    "perkGoodSowingRank2LongDesc": "花园就是你的第二个家。\\n收获时额外获得[DECEA3]1[-]颗种子和[DECEA3]1[-]份资源，找到种子的几率提高[DECEA3]10%[-]。\\n可以重新种植经过普通精选培育的作物。\\n减少水培工作站制作幼苗与肥料所需的作物和肥料数量。",
    "perkGoodSowingRank3LongDesc": "你已掌握耕作的诀窍。\\n收获时额外获得[DECEA3]1[-]颗种子和[DECEA3]2[-]份资源，找到种子的几率提高[DECEA3]20%[-]。\\n可以重新种植经过普通精选或高级精选培育的作物。\\n减少水培工作站制作幼苗与肥料所需的作物和肥料数量。",
    "perkSledgeSagaSavageReaperLongDesc": "犰狳防御：使用蓄力攻击击杀敌人后，躲避敌人攻击的几率提高[DECEA3]10%[-]，持续[DECEA3]3[-]秒。",
    "perkPackMuleRank3LongDesc": "你清楚每件东西放在哪里。\\n额外携带4件物品而不受负重影响。\\n背包内物品的制作速度提高[DECEA3]20%[-]。\\n受到攻击时，背包有[DECEA3]9%[-]几率免疫物理伤害。",
    "perkPackMuleRank4LongDesc": "二等兵，你刚刚通过检查。\\n额外携带4件物品而不受负重影响。\\n背包内物品的制作速度提高[DECEA3]25%[-]。\\n受到攻击时，背包有[DECEA3]12%[-]几率免疫物理伤害。",
    "perkIdealMetabolismRank1LongDesc": "吃什么并不重要，关键是按时进食。\\n食物与水的消耗降低[DECEA3]6%[-]，新陈代谢正面效果的持续时间提高[DECEA3]5%[-]。",
    "perkIdealMetabolismRank2LongDesc": "有时候，最好先看看食物的保质期。\\n食物与水的消耗降低[DECEA3]12%[-]，新陈代谢正面效果的持续时间提高[DECEA3]10%[-]。",
    "perkIdealMetabolismRank3LongDesc": "你知道该如何均衡饮食。\\n食物与水的消耗降低[DECEA3]18%[-]，新陈代谢正面效果的持续时间提高[DECEA3]15%[-]。",
    "perkIdealMetabolismRank4LongDesc": "只吃健康食品。\\n食物与水的消耗降低[DECEA3]24%[-]，新陈代谢正面效果的持续时间提高[DECEA3]20%[-]。",
    "perkIdealMetabolismRank5LongDesc": "你的身体运转得像一块昂贵的精密腕表。\\n食物与水的消耗降低[DECEA3]30%[-]，新陈代谢正面效果的持续时间提高[DECEA3]25%[-]。",
    "perkDaringAdventurerRank1LongDesc": "每天出生入死也有好处。\\n商人提供的物品更好，获得的公爵币提高[DECEA3]5%[-]。可在广播电台发布[DECEA3]第3级[-]任务，并组织探险队探索周边地区。",
    "perkDaringAdventurerRank2LongDesc": "商人都知道你办事可靠。\\n商人提供的物品进一步改善，获得的公爵币提高[DECEA3]10%[-]。可在广播电台发布[DECEA3]第4级[-]任务，并组织探险队调查僵尸活动加剧的地区。",
    "perkDaringAdventurerRank3LongDesc": "你已经以雇佣兵的身份打响名号。\\n商人提供的物品更好，获得的公爵币提高[DECEA3]15%[-]。可在广播电台发布[DECEA3]第5级[-]任务，并组织探险队寻找感染源。",
    "perkDaringAdventurerRank4LongDesc": "你就是勇敢的冒险家！\\n商人提供的物品更好，获得的公爵币提高[DECEA3]20%[-]。可在广播电台发布[DECEA3]第6级[-]任务，并组织探险队探索死区。",
    "perkStoneUpStoneRank2LongDesc": "敲敲敲……\\n方块伤害和采矿资源获取量提高[DECEA3]20%[-]。",
}

PROJECTZ_FINAL_DISPLAY.update(PROJECTZ_ADDITIONAL_FINAL_DISPLAY)


def translate_projectz_final_display() -> None:
    path = ROOT / "01-ProjectZ/Config/Localization.csv"
    lines = path.read_text(encoding="utf-8-sig").splitlines(keepends=True)
    header = next(csv.reader([lines[0]])); z = {name.lower(): i for i, name in enumerate(header)}["schinese"]; changed = 0
    for index in range(1, len(lines)):
        line = lines[index]; ending = "\r\n" if line.endswith("\r\n") else "\n" if line.endswith("\n") else ""
        row = next(csv.reader([line.removesuffix(ending)])); translated = PROJECTZ_FINAL_DISPLAY.get(row[0])
        if translated is None or row[z] == translated: continue
        row[z] = translated; output = io.StringIO(); csv.writer(output, lineterminator=ending, quoting=csv.QUOTE_MINIMAL).writerow(row)
        lines[index] = output.getvalue(); changed += 1
    with path.open("w", encoding="utf-8-sig", newline="") as handle: handle.write("".join(lines))
    print(f"01-ProjectZ/Config/Localization.csv: finalized {changed} visible names/descriptions")


translate_projectz_final_display()


def polish_projectz_machine_phrases() -> None:
    """Remove high-confidence literal-translation residue without touching game tokens."""
    path = ROOT / "01-ProjectZ/Config/Localization.csv"
    lines = path.read_text(encoding="utf-8-sig").splitlines(keepends=True)
    header = next(csv.reader([lines[0]])); columns = {name.lower(): i for i, name in enumerate(header)}
    e, z = columns["english"], columns["schinese"]
    replacements = (
        ("改进锻造", "改良型锻炉"), ("锻造厂", "锻炉"), ("砧座", "铁砧"),
        ("远程战斗修改", "远程武器改装件"), ("析构函数", "毁灭者"),
        ("骑自行车的人", "摩托骑手"), ("装甲靴", "护甲靴"),
        ("等待充值完成", "等待充能完成"), ("方方块", "方块"),
        ("降低了退化", "降低耐久损耗"), ("降低了降解", "降低耐久损耗"),
        ("降解速度", "耐久损耗速度"), ("炮塔退化速度增加", "炮塔耐久损耗速度提高"),
        ("可以向支持人员索取此类弹药", "可以通过支援补给申请这类弹药"),
        ("这种类型的弹药可以向支持人员索取，也可以在 Boss 战利品中找到。", "这类弹药可通过支援补给申请，也可能从Boss战利品中获得。"),
        ("小鸡们", "舞娘们"), ("改进的装甲套件", "改良型护甲套件"),
        ("改进的修理包", "改良型修理包"), ("改进的电气部件", "改良型电气零件"),
        ("改进的机械零件", "改良型机械零件"),
        ("[DECEA3]L 大型", "[DECEA3]大型"), ("[DECEA3]L大", "[DECEA3]大"),
        ("救助操作", "拾荒行动"), ("打开包装", "拆包"),
        ("奖励刀伤", "刀具伤害加成"), ("-刀具伤害", "- 刀具伤害"),
        ("杂志助推器", "杂志增效剂"), ("自动挖矿机", "自动采矿机"),
        ("支持请求", "支援申请"), ("大支持案例", "大型支援补给箱"),
        ("大案例", "大型补给箱"), ("创建反应堆工具", "制作反应堆工具"),
        ("用于创建", "用于制作"), ("才可创建", "才可制作"),
        ("盔甲", "护甲"), ("目标装甲", "目标护甲"), ("装甲类型", "护甲类型"),
        ("装甲套装", "护甲套装"), ("装甲制作套件", "护甲制作套件"),
        ("全套实验性", "全套实验型"), ("重装甲的组成部分", "重甲部件"),
        ("L 大型补给箱", "大型补给箱"), ("案例", "补给箱"),
        ("大量线索", "大量丝线"), ("部落", "尸潮"),
        ("在尸潮中睡觉", "在尸潮来袭时睡过头"), ("遇到了太多的尸潮", "遭遇了规模过大的尸潮"),
        ("改进的露水收集器", "改良型露水收集器"),
        ("水过滤器（改进版）", "滤水器改装件"),
        ("改进的化学站", "改良型化学工作站"), ("化学站", "化学工作站"),
        ("改进模型", "改良型号"), ("改进弹", "改良型弹药"),
        ("改进的炮塔弹药", "改良型炮塔弹药"),
        ("最好的例子", "最佳典范"), ("强大而坚固的", "坚固耐用"),
        ("资源收集器", "资源收集者"), ("恢复[DECEA3]100[-]健康状况", "恢复[DECEA3]100[-]生命值"),
        ("实验性修改。", "实验型改装件。"), ("实验修改。", "实验型改装件。"),
        ("独特的修改。", "独特改装件。"), ("随机独特且罕见的修改。", "随机的独特与稀有改装件。"),
        ("高级修改", "高级改装件"), ("改进的修改", "改良型改装件"),
        ("[FFB800]独特[-]修改", "[FFB800]独特[-]改装件"),
        ("进行修改", "进行改装"), ("及其修改", "及其改装件"), ("新的修改", "新改装件"),
        ("[DECEA3]6-th[-]射击", "第[DECEA3]6[-]发射击"),
        ("[DECEA3]4-th[-]射击", "第[DECEA3]4[-]发射击"),
        ("他们的尸体", "尸体"), ("通过此修改", "安装此改装件后"),
        ("对所有近战武器和工具的修改。", "适用于所有近战武器与工具的改装件。"),
        ("修改器", "改装件"), ("独特的修改", "独特改装件"), ("独特修改", "独特改装件"),
        ("[DECEA3]改良型[-]修改", "[DECEA3]改良型[-]改装件"),
        ("可靠的修改", "可靠改装件"), ("对近战武器的修改", "近战武器改装件"),
        ("对远程武器的修改", "远程武器改装件"), ("良好的修改", "优质改装件"),
        ("修改加成", "改装件加成"), ("头饰模型", "头部改装件"),
        ("头盔改装件", "头部改装件"), ("从臀部或移动时开枪", "腰射或移动射击"),
        ("射击体积", "枪声"), ("武器击穿率", "武器故障率"),
        ("创建[DECEA3]等级", "发布[DECEA3]等级"),
        ("创建一个真正的掩体", "建造一座真正的地堡"),
        ("创建个人肖像", "设定个人特质组合"), ("旧配方的创建速度", "旧配方的制作速度"),
        ("物品创建速度", "物品制作速度"), ("创建", "制作"), ("创造", "制作"),
        ("回收器", "回收站"), ("机械零件组", "机械零件套装"),
        ("机械零件集", "机械零件套装"), ("电气零件集", "电气零件套装"),
        ("关键效果", "重伤状态"), ("精力储备的中等恢复", "中等幅度恢复精力储备"),
        ("该设备还可用于培训", "该设备也可用于训练"),
        ("通过回收小家电获得的一堆资源", "回收小型家电所得的资源包"),
        ("通过处理小单位获得的一组资源", "回收小型机械物品所得的资源包"),
        ("从回收中型家用电器中获得的一组资源", "回收中型家电所得的资源包"),
        ("回收大型家用电器获得的一捆资源", "回收大型家电所得的资源包"),
        ("通过回收破损汽车获得的一堆资源", "回收报废汽车所得的资源包"),
        ("通过处理大型单位获得的一组资源", "回收大型机械物品所得的资源包"),
        ("用它来拆包。", "使用后可拆包取得内容。"), ("技能级别", "技能等级"),
        ("一旦你收集了足够的物品", "读够一定数量的杂志后"),
        ("收集足够数量的物品后", "读够一定数量的杂志后"),
        ("收集了足够数量的文章后", "读够一定数量的杂志后"),
        ("收集了足够的文章后", "读够一定数量的杂志后"),
        ("小文章", "短文"), ("工厂武器", "制式武器"), ("武器工厂模型", "制式武器"),
        ("大师[-]弹匣", "大师[-]杂志"), ("工具 大师", "工具大师"), ("Lv.", "等级"),
        ("弹匣助推器", "杂志增效剂"), ("一套弹匣", "一套杂志"),
        ("接线 101", "电路入门"),
        ("《手枪弹匣》", "《手枪杂志》"), ("《爆炸弹匣》", "《爆炸杂志》"),
        ("装甲", "护甲"), ("中腿护甲", "中型腿甲"),
        ("治疗恢复健康恢复", "治疗效果与生命恢复"),
        ("治疗的健康恢复率", "治疗恢复量"), ("健康恢复率", "生命恢复速度"),
        ("健康恢复", "生命值恢复"), ("健康状况", "生命值"),
        ("恢复健康", "恢复生命值"), ("损失健康", "损失生命值"),
        ("（健康，耐力", "（生命值、耐力"), ("（健康、耐力", "（生命值、耐力"),
        ("健康因", "生命值降低"), ("你的健康只会增长", "你的生命上限也会不断提高"),
        ("自动炮塔的生命值", "自动炮塔的耐久度"), ("机器人锤和自动炮塔的生命值", "机器人锤与自动炮塔的耐久度"),
        ("[DECEA3]面包店：[-] «健康吧»", "[DECEA3]烘焙：[-]《生命能量棒》"),
        ("武器重装速度", "武器装填速度"), ("重装速度", "装填速度"),
        ("鼠式重装", "鼠王装填"), ("守护者重装", "守护者装填"),
        ("隐形伤害", "潜行伤害"), ("[DECEA3]铁矿[-]近战武器", "[DECEA3]铁制[-]近战武器"),
        ("指节、刀或警棍", "拳套、刀具或电击棒"),
        ("指关节裹带", "拳击绷带"), ("指关节包扎", "拳击绷带"),
        ("铁指关节", "铁制拳套"), ("指关节铁", "铁制拳套"),
        ("指关节零件", "拳套零件"), ("指关节伤害", "拳套伤害"),
        ("指关节爱好者", "拳套爱好者"), ("使用指关节", "使用拳套"),
        ("指关节", "拳套"), ("鼠标爪", "鼠爪"),
        ("[DECEA3]- 破碎机[-]", "[DECEA3]- 粉碎者[-]"),
        ("[DECEA3]- 眩晕器[-]", "[DECEA3]- 震慑[-]"),
        ("[DECEA3]- 经验丰富[-]", "[DECEA3]- 老练[-]"),
        ("[DECEA3]- 通用[-]", "[DECEA3]- 全能[-]"),
        ("[DECEA3]- 不可杀死[-]", "[DECEA3]- 不毁[-]"),
        ("[FFB800]《破坏者》[-]", "[FFB800]《毁灭者》[-]"),
        ("等离子警棍", "等离子电击棒"),
        ("改良锻造品", "改良型锻炉"), ("改良锻造", "改良型锻炉"),
        ("适当的锻造", "像样的锻炉"), ("一个熔炉", "一座锻炉"),
        ("《锻造前进》", "《锻造未来》"),
        ("战利品产量", "战利品获取量"),
        ("Zinger", "毒刺"), ("Eraser", "抹除者"), ("Gaus", "高斯"),
        ("Sonny", "桑尼"), ("Predator", "捕食者"), ("URANUS", "贫铀"),
        ("RadProtect", "辐射防护"),
        ("Defender-Z", "守卫者-Z"), ("ANTIRAD", "抗辐射"), ("Magnum", "马格南"),
        ("岩石破碎机", "岩石粉碎机"), ("不可杀死", "不毁"), ("破碎机", "粉碎者"),
        ("方便[-]", "称手[-]"), ("《方便》", "《称手》"), ("“方便”", "“称手”"),
        ("微风", "疾风"), ("纵火犯", "纵火者"), ("印第安纳州", "印第安纳"),
        ("鼠式护甲", "鼠王护甲"), ("球杆", "棍棒"), ("橡皮擦", "抹除者"),
        ("破坏者", "毁灭者"), ("独特的 毒刺 模型", "独特的毒刺型号"),
        ("毒刺 模型", "毒刺型号"), ("“经验丰富”", "“老练”"),
        ("[DECEA3]加成 游戏中：[-]", "[DECEA3]游戏内加成：[-]"),
        ("[DECEA3]加成 在游戏中：[-]", "[DECEA3]游戏内加成：[-]"),
        ("游戏中的[DECEA3]加成：[-]", "[DECEA3]游戏内加成：[-]"),
        ("移动性", "机动性"), ("最大奖金", "最高加成"), ("当前奖金", "当前加成"),
        ("步枪 大师", "步枪大师"), ("霰弹枪 大师", "霰弹枪大师"),
        ("工作台 大师", "工作站大师"), ("农夫 大师", "农业大师"),
        ("每杀死一个[DECEA3]50[-]敌人", "每击杀[DECEA3]50[-]个敌人"),
        ("奖励就会增加 1 级", "熟练加成提升1级"),
        ("允许你开发熟练的武器操作", "可通过使用武器提升熟练加成"),
        ("每级增加伤害和攻击速度[DECEA3]1%[-]当前等级", "每级使伤害和攻击速度提高[DECEA3]1%[-]。\\n当前等级"),
        ("杀死[DECEA3]20[-]敌人", "击杀[DECEA3]20[-]个敌人"),
        ("最小值：1，最大值：6", "最少1层，最多6层"),
        ("法杖总是收费的", "电击棒始终保持充能"),
        ("称为 电弧者", "称为“电弧者”"),
        ("大量僵尸集中是一个福音。别忘了带一些纸巾；会湿的。尽管如此，并不能保证他们会提供帮助。", "成群的僵尸正合你意。别忘了带上纸巾——场面会很血腥，不过纸巾恐怕也帮不上多少忙。"),
        ("僵尸大量集中只会对你有利。别忘了带餐巾纸，它会湿的。虽然他们不会帮忙并不是事实。", "成群的僵尸正合你意。别忘了带上纸巾——场面会很血腥，不过纸巾恐怕也帮不上多少忙。"),
        ("并且还提高[DECEA3]25%[-]杀死敌人时有几率恢复[DECEA3]25[-]生命值", "击杀敌人时还会有[DECEA3]25%[-]几率恢复[DECEA3]25[-]点生命值"),
        ("并且还增加一个[DECEA3]25%[-]杀死敌人时有机会恢复[DECEA3]25[-]生命值", "击杀敌人时还会有[DECEA3]25%[-]几率恢复[DECEA3]25[-]点生命值"),
        ("并且还增加[DECEA3]25%[-]的几率杀死敌人时恢复[DECEA3]25[-]生命值", "击杀敌人时还会有[DECEA3]25%[-]几率恢复[DECEA3]25[-]点生命值"),
        ("并且还增加[DECEA3]25%[-]杀死敌人时有几率恢复[DECEA3]25[-]生命值", "击杀敌人时还会有[DECEA3]25%[-]几率恢复[DECEA3]25[-]点生命值"),
        ("每层近战战斗中，你方所有成员都会造成[DECEA3]5%[-]更多伤害和攻击速度更快", "每层效果使所有队伍成员的近战伤害与攻击速度提高[DECEA3]5%[-]"),
        ("治疗效果与生命恢复增加", "治疗恢复量提高"),
        ("授予对 1 种主动辐射效应的抵抗力", "可抵抗1种主动辐射效果"),
        ("赋予对 1 种主动辐射效应和触电的抵抗力", "可抵抗1种主动辐射效果，并免疫触电"),
        ("赋予对[DECEA3]1[-]主动辐射效应的抵抗力", "可抵抗[DECEA3]1[-]种主动辐射效果"),
        ("授予对[DECEA3]1[-]主动辐射效应的抵抗力", "可抵抗[DECEA3]1[-]种主动辐射效果"),
        ("[DECEA3]功能[-]", "[DECEA3]特性：[-]"),
        ("添加主动能力", "获得主动能力"), ("授予主动能力", "获得主动能力"),
        ("对于盟友来说，持续的被动效果", "队伍成员持续获得被动效果"),
        ("对于盟友，持续的被动效果", "队伍成员持续获得被动效果"),
        ("护甲元素的一些奖励适用于所有队伍成员。", "部分护甲加成会作用于所有队伍成员。"),
        ("所有奖金均适用于每位队伍成员", "所有加成都作用于每位队伍成员"),
        ("生命池", "生命上限"), ("耐力池", "耐力上限"),
        ("健康值也会增加", "生命上限也会提高"),
        ("[DECEA3]- 野生[-]", "[DECEA3]——怀尔德[-]"),
        ("[DECEA3]- 怀尔德[-]", "[DECEA3]——怀尔德[-]"),
        ("[DECEA3]-怀尔德[-]", "[DECEA3]——怀尔德[-]"),
        ("[DECEA3]-醉大师[-]", "[DECEA3]——醉酒大师[-]"),
        ("任何一方都离不开你", "任何队伍都少不了你"),
        ("你的队伍成员受益于以下奖励", "你的队伍成员获得以下加成"),
        ("承载能力", "负重上限"), ("装弹速度", "装填速度"),
        ("救援者 的", "救援者的"),
        ("激活还会消除大多数负面控制效果并应用", "使用时还会移除大多数控制类负面效果，并施加"),
        ("每分钟，有[DECEA3]20%[-]机会消除伤口的任何负面影响", "每分钟有[DECEA3]20%[-]几率移除所有伤势类负面效果"),
        ("会对敌人施加电死效果", "会使攻击者陷入触电状态"),
        ("所有团队成员", "所有队伍成员"),
        ("艾莉拥有全套", "盟友拥有全套"),
        ("杀死敌人获得的经验值增加", "击杀敌人获得的经验提高"),
        ("杀死敌人获得的经验提高", "击杀敌人获得的经验提高"),
        ("杀死对手获得的经验提高", "击杀敌人获得的经验提高"),
        ("如果你的生命值低于[DECEA3]200[-]，杀死敌人将恢复[DECEA3]50[-]生命值；如果低于[DECEA3]100[-]，它将恢复[DECEA3]100[-]生命值", "生命值低于[DECEA3]200[-]时，击杀敌人会恢复[DECEA3]50[-]点生命值；低于[DECEA3]100[-]时则恢复[DECEA3]100[-]点生命值"),
        ("疯狂的伤害，但攻击速度较低", "伤害极高，但攻击速度较低"),
        ("强力攻击", "蓄力攻击"),
        ("他独特的战斗策略使他能够压制甚至强大的对手：如果敌人承受蓄力攻击，他们将被击倒在地，如果他们试图站起来（除非目标是 Boss），他们将被击倒。", "这种武器的独特战术足以压制强敌：若敌人扛住蓄力攻击，便会被击倒；若其试图起身（Boss除外），还会再次倒地。"),
        ("他特殊的战斗策略使他能够压制甚至强大的对手：如果敌人承受住蓄力攻击，他们将被击倒在地，如果他们试图站起来（如果目标不是Boss），他们将受到第二次打击。", "这种武器的独特战术足以压制强敌：若敌人扛住蓄力攻击，便会被击倒；若其试图起身（Boss除外），还会再次倒地。"),
        ("攻击时体力消耗减少", "攻击时耐力消耗降低"),
        ("当你进行蓄力攻击时，你对目标造成的任何伤害都会增加", "发动蓄力攻击后，你对目标造成的所有伤害提高"),
        ("用于修理，需要改良型护甲制作套件", "修理需要改良型护甲制作套件"),
        ("赋予对一种主动辐射效应的抵抗力", "可抵抗1种主动辐射效果"),
        ("没有坚不可摧的护甲之类的东西——你只需要一个更大的武器。这种怪物的强力挥击让人无法防御。", "世上没有坚不可摧的护甲，只是你的武器还不够大。这柄怪物般的大锤一旦挥下，根本无从抵挡。"),
        ("情报大师", "智力大师"),
        ("没人知道是谁组装了这样的装置，但许多人将其称为“电弧者”。它的制作者显然富有想象力；理智是另一回事。", "如今已无人知道是谁组装了这套装置，许多人称它为“电弧者”。制作者显然想象力十足，至于是否理智就不好说了。"),
        ("无论智力大师技能的水平如何，电击棒始终保持充能", "无论“智力大师”技能等级如何，电击棒都会始终保持充能"),
        ("造成即时伤害的几率增加到", "造成即时伤害的几率提高至"),
        ("伤害本身增加到", "即时伤害提高至"),
        ("伤害本身增加至", "即时伤害提高至"),
        ("移动速度提高[DECEA3]50%[-]，攻击更加频繁", "移动速度提高[DECEA3]50%[-]，攻击速度提高[DECEA3]60%[-]"),
        ("移动速度加快了[DECEA3]50%[-]，攻击变得更加频繁", "移动速度提高[DECEA3]50%[-]，攻击速度提高[DECEA3]60%[-]"),
        ("0.44 马格南", ".44马格南"), ("0.44马格南", ".44马格南"),
        ("0.44 口径", ".44口径"), ("0.44口径", ".44口径"),
        ("独特的斗牛犬模型", "独特型“斗牛犬”"),
        ("实验性斗牛犬模型", "实验型“斗牛犬”"),
        ("当你击中敌人时，会导致他们流血并减慢他们的速度[DECEA3]6[-]秒", "命中敌人会使其流血并减速，持续[DECEA3]6[-]秒"),
        ("当击中敌人时，它会导致他们流血并减慢他们的速度[DECEA3]6[-]秒", "命中敌人会使其流血并减速，持续[DECEA3]6[-]秒"),
        ("按照你的条件与任何尸潮谈判的最佳方式", "和尸潮谈判，就该按你的规矩来"),
        ("火力会降低敌人的移动速度和攻击速度", "燃烧会降低敌人的移动速度和攻击速度"),
        ("增加射速和伤害[DECEA3]25%[-]持续", "使射速和伤害提高[DECEA3]25%[-]，持续"),
        ("它是最强大的，但发出的噪音也最大。驾驭这个坏男孩，让克林特感到骄傲。", "它威力最强，动静也最大。拿起这个大家伙，让克林特也为你骄傲。"),
        ("[FF6666]第二章. 罪犯[-]", "[FF6666]第2章：袭击者[-]"),
    )
    changed = 0
    for index in range(1, len(lines)):
        line = lines[index]; ending = "\r\n" if line.endswith("\r\n") else "\n" if line.endswith("\n") else ""
        row = next(csv.reader([line.removesuffix(ending)])); updated = row[z]
        source_vars = re.findall(r"\{[^{}]+\}|%[A-Za-z]|\$\([^)]*\)", row[e])
        translated_vars = re.findall(r"\{[^{}]+\}|%[A-Za-z]|\$\([^)]*\)", updated)
        if len(source_vars) == len(translated_vars):
            var_numbers = iter(range(len(translated_vars)))
            updated = re.sub(
                r"\{[^{}]+\}|%[A-Za-z]|\$\([^)]*\)",
                lambda _match: f"\x00FINALVAR{next(var_numbers)}\x00",
                updated,
            )
        for source, target in replacements: updated = updated.replace(source, target)
        updated = re.sub(r"第\s+(\d+)\s+章", r"第\1章", updated)
        updated = re.sub(r"(第\d+章)[.。]\s*", r"\1：", updated)
        updated = re.sub(r"”(?=\\n|\s*\[DECEA3\]|$)", "»", updated)
        for var_number, source_var in enumerate(source_vars):
            updated = updated.replace(f"\x00FINALVAR{var_number}\x00", source_var)
        if updated == row[z]: continue
        row[z] = updated; output = io.StringIO(); csv.writer(output, lineterminator=ending, quoting=csv.QUOTE_MINIMAL).writerow(row)
        lines[index] = output.getvalue(); changed += 1
    with path.open("w", encoding="utf-8-sig", newline="") as handle: handle.write("".join(lines))
    print(f"01-ProjectZ/Config/Localization.csv: polished {changed} literal phrases")


polish_projectz_machine_phrases()


def sync_projectz_final_override_names() -> None:
    """Keep the last-loaded ownership labels aligned with the reviewed Project Z names."""
    source_path = ROOT / "01-ProjectZ/Config/Localization.csv"
    with source_path.open("r", encoding="utf-8-sig", newline="") as handle:
        reader = csv.reader(handle); header = next(reader); columns = {name.lower(): i for i, name in enumerate(header)}
        source_z = columns["schinese"]
        reviewed_names = {
            row[0]: row[source_z]
            for row in reader
            if len(row) > source_z and row[1] == "items" and "\\n" not in row[source_z] and row[source_z].strip()
        }

    path = ROOT / "98-AECxProjectZ_Tweaks/Config/Localization.csv"
    lines = path.read_text(encoding="utf-8-sig").splitlines(keepends=True)
    header = next(csv.reader([lines[0]])); columns = {name.lower(): i for i, name in enumerate(header)}
    e, z, context = columns["english"], columns["schinese"], columns["context / alternate text"]
    prefix = "[F87C63][Project Z][-] "
    changed = 0
    for index in range(1, len(lines)):
        line = lines[index]; ending = "\r\n" if line.endswith("\r\n") else "\n" if line.endswith("\n") else ""
        row = next(csv.reader([line.removesuffix(ending)]))
        reviewed = reviewed_names.get(row[0]) if len(row) > max(e, z, context) else None
        if (
            reviewed is None or row[1] != "items" or prefix not in row[e]
            or row[context] != "Ownership-only display-name override."
        ):
            continue
        translated = reviewed if reviewed.startswith(prefix) else prefix + reviewed
        if row[z] == translated:
            continue
        row[z] = translated; output = io.StringIO(); csv.writer(output, lineterminator=ending, quoting=csv.QUOTE_MINIMAL).writerow(row)
        lines[index] = output.getvalue(); changed += 1
    with path.open("w", encoding="utf-8-sig", newline="") as handle: handle.write("".join(lines))
    print(f"98-AECxProjectZ_Tweaks/Config/Localization.csv: synced {changed} reviewed Project Z item names")


sync_projectz_final_override_names()


VEHICLE_FINAL_EXACT = {
    "aecProjectDiscordNoteDesc": "[FFFFFF]如有任何模组问题或需求，请加入AEC PROJECT Discord。[-]",
    "aecIronBastionChassis": "钢铁堡垒底盘",
    "AECIronBastionVehiclePlaceable": "钢铁堡垒",
    "aecClassicServiceTruckMoPowerChassis": "经典服务卡车（莫尔电力）底盘",
    "AECClassicServiceTruckMoPowerVehiclePlaceable": "经典服务卡车（莫尔电力）",
    "aecClassicServiceTruckWorkingStiffChassis": "经典服务卡车（结实工具）底盘",
    "AECClassicServiceTruckWorkingStiffVehiclePlaceable": "经典服务卡车（结实工具）",
    "aecMiniRocketChassis": "迷你火箭车底盘",
    "AECMiniRocketVehiclePlaceable": "迷你火箭车",
}


def translate_vehicle_final_cleanup() -> None:
    path = ROOT / "07-AEC-Vehicles-NoMicrocraft/Config/Localization.csv"
    lines = path.read_text(encoding="utf-8-sig").splitlines(keepends=True)
    header = next(csv.reader([lines[0]])); columns = {name.lower(): i for i, name in enumerate(header)}; z = columns["schinese"]
    changed = 0
    for index in range(1, len(lines)):
        line = lines[index]; ending = "\r\n" if line.endswith("\r\n") else "\n" if line.endswith("\n") else ""; row = next(csv.reader([line.removesuffix(ending)]))
        translated = VEHICLE_FINAL_EXACT.get(row[0])
        if translated is None or row[z] == translated:
            continue
        row[z] = translated; output = io.StringIO(); csv.writer(output, lineterminator=ending, quoting=csv.QUOTE_MINIMAL).writerow(row); lines[index] = output.getvalue(); changed += 1
    with path.open("w", encoding="utf-8-sig", newline="") as handle: handle.write("".join(lines))
    print(f"07-AEC-Vehicles-NoMicrocraft/Config/Localization.csv: finalized {changed} vehicle entries")


translate_vehicle_final_cleanup()


def normalize_projectz_csv_records() -> None:
    """Keep Project Z's one-physical-line-per-record localization convention."""
    path = ROOT / "01-ProjectZ/Config/Localization.csv"
    with path.open(encoding="utf-8-sig", newline="") as handle:
        records = list(csv.reader(handle))
    width = len(records[0]); normalized = []
    for row in records:
        if len(row) != width:
            # Remove orphan continuation records left by a prior physical-newline
            # write; every legitimate localization record has exactly 7 columns.
            continue
        normalized.append([cell.replace("\r\n", "\\n").replace("\n", "\\n") for cell in row])
    with path.open("w", encoding="utf-8-sig", newline="") as handle:
        csv.writer(handle, lineterminator="\n", quoting=csv.QUOTE_MINIMAL).writerows(normalized)
    print(f"01-ProjectZ/Config/Localization.csv: normalized {len(normalized)} CSV records")


normalize_projectz_csv_records()


PZ_TARGET_NAMES = {
    "Elite Zombies": "精英僵尸", "Devourer": "吞噬者", "Nagaina": "娜迦蛇后",
    "Gargul": "石像魔", "Bear Daddy": "巨熊之父", "Bitch": "悍妇",
    "Mystic": "秘法师", "Burning Flesh": "燃烧之肉", "Cholera": "霍乱",
    "Bull": "蛮牛", "Hot Harry": "烈焰哈里", "Mummy": "木乃伊",
    "Shocker": "电击者", "Veteran": "老兵", "Ancient Yeti": "远古雪人",
    "Carrier": "母体", "zombies": "僵尸", "all zombies": "所有僵尸",
}


def projectz_stat_clause_zh(clause):
    value = r"(\[DECEA3\][^[]+?\[-\])"
    direct = (
        (rf"Headshot damage increased by {value}$", "爆头伤害提高{}"),
        (rf"Headshot experience increased by {value}$", "爆头击杀经验提高{}"),
        (rf"Ranged weapon damage increased by {value}$", "远程武器伤害提高{}"),
        (rf"Weapon breakdown rate decreased by {value}$", "武器耐久损耗降低{}"),
        (rf"Melee weapon damage increased by {value}$", "近战武器伤害提高{}"),
        (rf"Melee kill experience increased by {value}$", "近战击杀经验提高{}"),
        (rf"Chance to restore health for the Siphoning Strikes skill increased by {value}$", "“汲取打击”的生命恢复概率提高{}"),
        (rf"Amount of «Mutation Samples» obtained increased by {value}$", "获得的“变异样本”数量提高{}"),
        (rf"Damage to zombies increased by {value}$", "对僵尸造成的伤害提高{}"),
        (rf"Experience for killing zombies increased by {value}$", "击杀僵尸获得的经验提高{}"),
        (rf"Loot drop chance increased by {value}$", "战利品掉落概率提高{}"),
        (rf"Character's game stage increased by {value}$", "角色游戏阶段提高{}"),
        (rf"Experience gained from killing zombies at night is increased by {value}$", "夜间击杀僵尸获得的经验提高{}"),
        (rf"Loot quality at night is increased by {value}$", "夜间战利品品质提高{}"),
        (rf"Character's game stage at night is increased by {value}$", "角色夜间游戏阶段提高{}"),
        (rf"Sneak damage to zombies is increased by {value}$", "对僵尸造成的潜行伤害提高{}"),
        (rf"Experience gained in the forest increased by {value}$", "在森林中获得的经验提高{}"),
        (rf"Wood production increased by {value}$", "木材采集量提高{}"),
        (rf"Clay production increased by {value}$", "黏土采集量提高{}"),
        (rf"Experience gained in the burnt biome increased by {value}$", "在焦土生态区获得的经验提高{}"),
        (rf"Potassium Nitrate production increased by {value}$", "硝酸钾采集量提高{}"),
        (rf"Experience gained in the desert increased by {value}$", "在沙漠中获得的经验提高{}"),
        (rf"Iron and shale production increased by {value}$", "铁矿和油页岩采集量提高{}"),
        (rf"Experience gained in the winter increased by {value}$", "在雪原中获得的经验提高{}"),
        (rf"Coal production increased by {value}$", "煤炭采集量提高{}"),
        (rf"Experience gained in the wasteland increased by {value}$", "在废土中获得的经验提高{}"),
        (rf"Lead production increased by {value}$", "铅矿采集量提高{}"),
        (rf"Resistance to radiation damage increased by {value}$", "辐射伤害抗性提高{}"),
        (rf"Damage to minibosses increased by {value}$", "对小型Boss造成的伤害提高{}"),
        (rf"Damage to bosses increased by {value}$", "对Boss造成的伤害提高{}"),
    )
    for pattern, template in direct:
        match = re.fullmatch(pattern, clause)
        if match:
            return template.format(match.group(1))
    match = re.fullmatch(rf"Damage to (.+?)'s minions increased by {value}", clause)
    if match:
        return f"对{PZ_TARGET_NAMES.get(match.group(1), match.group(1))}仆从造成的伤害提高{match.group(2)}"
    match = re.fullmatch(rf"Damage to (.+?) increased by {value}", clause)
    if match:
        return f"对{PZ_TARGET_NAMES.get(match.group(1), match.group(1))}造成的伤害提高{match.group(2)}"
    match = re.fullmatch(rf"Damage taken from (.+?) decreased by {value}", clause)
    if match:
        return f"受到{PZ_TARGET_NAMES.get(match.group(1), match.group(1))}的伤害降低{match.group(2)}"
    match = re.fullmatch(rf"Damage reduction from (.+?) increased by {value}", clause)
    if match:
        return f"受到{PZ_TARGET_NAMES.get(match.group(1), match.group(1))}的伤害降低{match.group(2)}"
    return None


def translate_projectz_progression_stats() -> None:
    path = ROOT / "01-ProjectZ/Config/Localization.csv"; lines = path.read_text(encoding="utf-8-sig").splitlines(keepends=True)
    header = next(csv.reader([lines[0]])); columns = {name.lower(): i for i, name in enumerate(header)}; e = columns["english"]; z = columns["schinese"]
    headers = {
        "[DECEA3]BONUSES:[-]": "[DECEA3]加成：[-]",
        "[DECEA3]FOREST BONUSES:[-]": "[DECEA3]森林奖励：[-]",
        "[DECEA3]BURNT BIOME BONUSES:[-]": "[DECEA3]焦土奖励：[-]",
        "[DECEA3]DESERT BONUSES:[-]": "[DECEA3]沙漠奖励：[-]",
        "[DECEA3]WINTER BONUSES:[-]": "[DECEA3]雪原奖励：[-]",
        "[DECEA3]WASTELAND BONUSES:[-]": "[DECEA3]废土奖励：[-]",
    }
    changed = 0
    for index in range(1, len(lines)):
        line = lines[index]; ending = "\r\n" if line.endswith("\r\n") else "\n" if line.endswith("\n") else ""; row = next(csv.reader([line.removesuffix(ending)]))
        if len(row) <= z or not row[0].endswith("LongDesc"):
            continue
        english_parts = row[e].split("\\n\\n", 1); first = english_parts[0]
        if "\\n" not in first:
            continue
        source_header, source_clauses = first.split("\\n", 1)
        translated_header = headers.get(source_header)
        if translated_header is None:
            continue
        clauses = [projectz_stat_clause_zh(part.strip()) for part in source_clauses.split(", ")]
        if not clauses or any(part is None for part in clauses):
            continue
        translated = translated_header + "\\n" + "，".join(clauses) + "。"
        chinese_parts = row[z].split("\\n\\n", 1)
        if len(chinese_parts) == 2:
            translated += "\\n\\n" + chinese_parts[1]
        if translated == row[z]:
            continue
        row[z] = translated; output = io.StringIO(); csv.writer(output, lineterminator=ending, quoting=csv.QUOTE_MINIMAL).writerow(row); lines[index] = output.getvalue(); changed += 1
    with path.open("w", encoding="utf-8-sig", newline="") as handle: handle.write("".join(lines))
    print(f"01-ProjectZ/Config/Localization.csv: rebuilt {changed} progression descriptions")


translate_projectz_progression_stats()


V9202_STEP_TEXT = {}


def add_v9202_step(keys, heading, intro, objective):
    warning = "[FF8A65]重要提示[-]\\n请勿取消这个一次性任务，也不要丢弃关联章节。任务链遗失后可能需要管理员才能恢复。\\n\\n"
    value = f"{heading}\\n{intro}\\n\\n[5ECFFF]目标[-]\\n{objective}\\n\\n{warning}"
    for key in keys:
        V9202_STEP_TEXT[key] = value


add_v9202_step(("startQuest1Offer", "note1_startQuestDesc"), "[F87C63][Project Z][-] [FF6666]生存——序章[-]", "Project Z以一段简短的生存引导开场，而不是直接进入终局。它会交代角色的起点，并依次发放三章生存任务。", "完成序章。下一章是[FFD866]自我救治[-]。")
add_v9202_step(("startQuest2Offer", "note2_startQuestDesc"), "[F87C63][Project Z][-] [FF6666]生存1/3——自我救治[-]", "你在负伤状态下醒来。本章是一段简短的急救流程：撑过最初时刻，取得刀具并准备继续上路。", "等待这段流程结束。你会获得一条急救绷带和下一章。")
add_v9202_step(("startQuest3Offer", "note3_startQuestDesc"), "[F87C63][Project Z][-] [FF6666]生存2/3——袭击者[-]", "附近的声响并非错觉。任务会生成一名敌人，作为你醒来后的第一场考验。", "击杀生成的感染者，并取得留言的最后一章。")
add_v9202_step(("startQuest4Offer", "note4_startQuestDesc"), "[F87C63][Project Z][-] [FF6666]生存3/3——寻找商人[-]", "最后一张纸条让你前往任意商人处。与商人见面后，引导正式结束，整合包的自由发展路线随之开放。", "找到任意商人并与其交谈。除初始补给外，你还会获得[FFD866]整合包发展指南[-]和Project Z武器任务链的第一章。")

resource_titles = ("铁制工具", "钢制工具", "电动工具", "改良型电动工具", "反应堆工具", "充电站")
resource_objectives = (
    "击杀[FFD866]5名工人系感染者[-]，收集[FFD866]250份废铁[-]、[FFD866]50份木材[-]和[FFD866]5根短铁管[-]，制作对应的材料包并交给商人。奖励可任选一件铁制工具。",
    "击杀[FFD866]10名工人系感染者[-]，收集[FFD866]50份锻铁[-]、[FFD866]50份木材[-]和[FFD866]15个通用部件[-]，制作对应的材料包并交给商人。奖励可任选一件钢制工具。",
    "击杀[FFD866]15名工人系感染者[-]，收集[FFD866]50份锻钢[-]、[FFD866]25个机械零件[-]和[FFD866]25个通用部件[-]，制作对应的材料包并交给商人。奖励可任选一件入门级电动工具。",
    "击杀[FFD866]20名工人系感染者[-]，收集[FFD866]100份锻钢[-]、[FFD866]25个机械零件[-]、[FFD866]25个电气零件[-]和[FFD866]25个通用部件[-]，制作对应的材料包并交给商人。奖励可任选一件改良型电动工具。",
    "击杀[FFD866]25名工人系感染者[-]，收集[FFD866]15份超强合金[-]、[FFD866]10个改良型电气零件[-]和[FFD866]50个通用部件[-]，制作对应的材料包并交给商人。奖励可任选反应堆螺旋钻或反应堆冲击起子。",
    "击杀[FFD866]50名工人系感染者[-]，收集[FFD866]15份贵金属[-]、[FFD866]10个改良型电气零件[-]和[FFD866]50个通用部件[-]，制作对应的材料包并交给商人。奖励可任选一座反应堆工具充电站。",
)
for number, (title, objective) in enumerate(zip(resource_titles, resource_objectives), 1):
    add_v9202_step(
        (f"ResCollector{number}Offer", f"Chapter{number}_resCollectorDesc"),
        f"[F87C63][Project Z][-] [FAFF63]资源收集者{number}/6——{title}[-]",
        "这条路线围绕工人感染者展开，但[FFD866]整个工人感染者家族[-]都计入目标，包括凶暴、辐射以及AEC终局整合变体。完成猎杀后，收集材料并准备交付包。",
        objective,
    )

add_v9202_step(("NewHome1Offer", "Chapter1_newHomeDesc"), "[F87C63][Project Z][-] [FF7E2F]新家1/4——庇护所[-]", "从简易木制庇护所开始。重点不是建筑造型，而是熟悉批量建造和升级流程。", "放置[FFD866]100个框架方块[-]，升级[FFD866]100个框架[-]，然后返回商人处。")
add_v9202_step(("NewHome2Offer", "Chapter2_newHomeDesc"), "[F87C63][Project Z][-] [FF7E2F]新家2/4——坚固墙体[-]", "木材足以起步，却挡不住真正的围攻。下一步是更坚固的砖石结构与入口。", "升级[FFD866]100个木质方块[-]、一扇木门和一个木制舱门，然后返回商人处。")
add_v9202_step(("NewHome3Offer", "Chapter3_newHomeDesc"), "[F87C63][Project Z][-] [FF7E2F]新家3/4——地堡[-]", "现在要把基地升级为混凝土结构并装上重型入口，这一级防御用于抵挡更危险的袭击。", "升级[FFD866]100个鹅卵石方块[-]、[FFD866]2个铁制舱门[-]和[FFD866]2扇铁门[-]，然后返回商人处。")
add_v9202_step(("NewHome4Offer", "Chapter4_newHomeDesc"), "[F87C63][Project Z][-] [FF7E2F]新家4/4——防空洞[-]", "最后一章会把防御升级到钢结构和电动入口，至此完成Project Z的基础建造路线。", "升级[FFD866]100个混凝土方块[-]，并放置[FFD866]2扇电动金库门[-]和[FFD866]2个电动金库舱门[-]。")

weapon_steps = (
    (("BetterWeaponMeleeT1Offer", "BetterWeapon_MeleeT1Desc"), "第一座里程碑", "第一项武器里程碑不限制武器类型，只衡量总体战斗经验。完成后会开启远程路线，并可在轻型与重型近战路线中选择。", "击杀[FFD866]75名感染者[-]并返回商人处。"),
    (("BetterWeaponMeleeLightT1Offer", "BetterWeapon_MeleeLightT1Desc"), "轻型近战I", "这是轻型近战奖励路线。目标不限制具体武器，使用任何能让你活下来的手段即可。", "击杀[FFD866]50名感染者[-]，并从拳套、刀具或机器人雪橇中选择奖励。"),
    (("BetterWeaponMeleeHeavyT1Offer", "BetterWeapon_MeleeHeavyT1Desc"), "重型近战I", "这是重型近战奖励路线。击杀不必使用长矛、棍棒或大锤完成。", "击杀[FFD866]50名感染者[-]，并从长矛、棍棒或大锤中选择奖励。"),
    (("BetterWeaponRangeT1Offer", "BetterWeapon_RangeT1Desc"), "远程武器I", "第一项远程里程碑同样计算普通击杀，不限制具体武器类别。奖励是初级远程武器套装之一。", "击杀[FFD866]100名感染者[-]，返回商人处并选择一套远程奖励。"),
    (("BetterWeaponMeleeT2Offer", "BetterWeapon_MeleeT2Desc"), "钢制武器里程碑", "第二项近战里程碑仍衡量总体战斗进度，随后开启更强的轻型、重型近战奖励以及下一章远程路线。", "击杀[FFD866]150名感染者[-]并返回商人处。"),
    (("BetterWeaponMeleeLightT2Offer", "BetterWeapon_MeleeLightT2Desc"), "轻型近战II", "第二条轻型近战路线通向下一武器等级。与此前一样，击杀计数不限制武器类别。", "击杀[FFD866]100名感染者[-]，并从钢制拳套、砍刀或电击棒中选择奖励。"),
    (("BetterWeaponMeleeHeavyT2Offer", "BetterWeapon_MeleeHeavyT2Desc"), "重型近战II", "第二条重型近战路线通向钢制武器。你可用任意方式完成击杀，所选路线只决定奖励。", "击杀[FFD866]100名感染者[-]，并从钢矛、钢棍或钢制大锤中选择奖励。"),
    (("BetterWeaponRangeT2Offer", "BetterWeapon_RangeT2Desc"), "远程武器II", "第二项远程里程碑会提高奖励等级，并通往军事武器里程碑。击杀计数不限制武器类别。", "击杀[FFD866]250名感染者[-]，然后选择一套远程奖励。"),
    (("BetterWeaponRangeT3Offer", "BetterWeapon_RangeT3Desc"), "军事武器里程碑", "任务将大量击杀拆分为连续三个阶段，总计450次击杀；不限制手持武器。", "依次完成三个阶段，每阶段[FFD866]击杀150名感染者[-]，然后返回商人处。"),
    (("BetterWeaponImpModsOffer", "BetterWeapon_ImpModsDesc"), "改良型改装件", "完成武器等级路线后，发展方向转向改装件。这项通用里程碑不限制武器，并会开启近战或远程改装专精选择。", "完成三个阶段，每阶段[FFD866]击杀250名感染者[-]，共750名；随后选择改良型近战或远程改装路线。"),
    (("BetterWeaponMeleeImpModsOffer", "BetterWeapon_ImpModsMeleeDesc"), "改良型近战改装件", "这项专精只会在你手持近战武器时计算击杀。", "手持近战武器完成两个阶段，每阶段[FFD866]击杀250名感染者[-]，然后返回商人处。"),
    (("BetterWeaponRangeImpModsOffer", "BetterWeapon_ImpModsRangeDesc"), "改良型远程改装件", "这项专精只会在你手持有效远程武器时计算击杀。", "手持远程武器完成两个阶段，每阶段[FFD866]击杀250名感染者[-]，然后返回商人处。"),
    (("BetterWeaponUniqueModsOffer", "BetterWeapon_UniqueModsDesc"), "独特改装件", "最后一项通用改装里程碑是一场漫长的耐力考验。它不限制武器类别，完成后会开启独特近战或远程改装选择。", "完成六个阶段，每阶段[FFD866]击杀250名感染者[-]，共1,500名；随后选择专精路线。"),
    (("BetterWeaponMeleeUniqueModsOffer", "BetterWeapon_UniqueModsMeleeDesc"), "独特近战改装件", "最终近战专精仍要求击杀时手持近战武器，是成型战斗流派的后期目标。", "手持近战武器完成四个阶段，每阶段[FFD866]击杀250名感染者[-]。"),
    (("BetterWeaponRangeUniqueModsOffer", "BetterWeapon_UniqueModsRangeDesc"), "独特远程改装件", "最终远程专精要求手持有效远程武器；使用其他武器完成的击杀不会推进当前阶段。", "手持远程武器完成四个阶段，每阶段[FFD866]击杀250名感染者[-]。"),
    (("BetterWeaponLastHunt1Offer", "BetterWeapon_LastHunt1Desc"), "最终猎杀I", "普通感染者已不足以证明实力。第一场最终猎杀会把目标提升到小型Boss，以检验你的终局准备。", "击杀[FFD866]50只小型Boss[-]并返回商人处。"),
    (("BetterWeaponLastHunt2Offer", "BetterWeapon_LastHunt2Desc"), "最终猎杀II", "武器任务链的最后一项里程碑以完整Boss为目标，完成后Project Z的主要武器发展路线便告结束。", "击杀[FFD866]25只大型Boss[-]并返回商人处。"),
)
for keys, title, intro, objective in weapon_steps:
    add_v9202_step(keys, f"[F87C63][Project Z][-] [DECEA3]更好武器——{title}[-]", intro, objective)

add_v9202_step(("aecVehProgQuest1Offer",), "[FFD700][AEC][-] [55C05A]车库1/2——最终装配[-]", "AEC最终装配台用于制造底盘、修理包和完整载具。这条简短路线会介绍两种专用载具工作台。", "制造[FFD866]AEC最终装配台[-]。完成后会获得车库指南第二章。")
add_v9202_step(("aecVehProgQuest2Offer",), "[FFD700][AEC][-] [55C05A]车库2/2——载具改装[-]", "第二座工作台负责载具升级，包括燃油效率、照明、伤害、速度、扭矩、油箱容量及其他改装。", "制造[FFD866]AEC载具改装工作台[-]。")
V9202_STEP_TEXT["note4_startQuest"] = "[F87C63][Project Z][-] [DECEA3]任务[-] 生存[FF6666]第3章[-]——不错的奖励"


def translate_98_v9202_steps() -> None:
    path = ROOT / "98-AECxProjectZ_Tweaks/Config/Localization.csv"; lines = path.read_text(encoding="utf-8-sig").splitlines(keepends=True)
    header = next(csv.reader([lines[0]])); columns = {name.lower(): i for i, name in enumerate(header)}; z = columns["schinese"]
    changed = 0
    for index in range(1, len(lines)):
        line = lines[index]; ending = "\r\n" if line.endswith("\r\n") else "\n" if line.endswith("\n") else ""; row = next(csv.reader([line.removesuffix(ending)]))
        translated = V9202_STEP_TEXT.get(row[0])
        if translated is None or row[z] == translated:
            continue
        row[z] = translated; output = io.StringIO(); csv.writer(output, lineterminator=ending, quoting=csv.QUOTE_MINIMAL).writerow(row); lines[index] = output.getvalue(); changed += 1
    with path.open("w", encoding="utf-8-sig", newline="") as handle: handle.write("".join(lines))
    print(f"98-AECxProjectZ_Tweaks/Config/Localization.csv: rebuilt {changed} current quest/chapter texts")


translate_98_v9202_steps()


RARE_PROPERTY_NAMES = {
    "Crusher": "粉碎者", "Knockdown": "击倒", "Fast hands": "快手", "Experienced": "老练",
    "Apple": "苹果", "Universal": "通用", "Vampire": "吸血鬼", "Unkillable": "坚不可摧",
    "Stable": "稳定", "Sweeper": "横扫", "Snowstorm": "暴风雪", "Rapidfire": "速射",
    "Robin Hood": "罗宾汉", "Ninja": "忍者", "Respect": "声望", "Convenient": "称手",
    "Awl": "尖锥", "Champion": "冠军", "Stunner": "震慑", "Butcher": "屠夫",
    "Lumberjack": "伐木工", "Miner": "矿工", "Metalist": "金属专家", "Mason": "石匠",
    "Digger": "掘土者", "Scavenger": "拾荒者",
}

RARE_EFFECT_EXACT = {
    "Increased damage but decreased attack speed.": "伤害提高，但攻击速度降低。",
    "Weapon damage increased.": "武器伤害提高。", "Damage increased.": "伤害提高。",
    "Hitting an enemy knocks them down.": "命中敌人时将其击倒。",
    "The number of installed modifications and the magazine size have been increased.": "可安装的改装件数量和弹匣容量均有所提高。",
    "The number of installed modifications and the rate of fire have been increased.": "可安装的改装件数量和射速均有所提高。",
    "The number of installed modifications has been increased.": "可安装的改装件数量有所提高。",
    "Increased damage and number of installed modifications.": "伤害与可安装的改装件数量均有所提高。",
    "This weapon is almost indestructible when used.": "使用时几乎不会损失耐久。",
    "Increased aiming speed and reduced weapon dispersion.": "提高瞄准速度并降低武器散布。",
    "Improved usability and reduced weapon spread.": "操控性更好，武器散布更低。",
    "When hitting enemies, significantly reduces their mobility. The effect weakens over time.": "命中敌人时大幅降低其机动性，效果会随时间逐渐减弱。",
    "Rate of fire increased, but damage and magazine size decreased.": "射速提高，但伤害与弹匣容量降低。",
    "[DECEA3]10%[-] bonus on item sales.": "出售物品时售价提高[DECEA3]10%[-]。",
    "Decreased stamina consumption on hits and increased attack speed.": "降低攻击的耐力消耗，并提高攻击速度。",
}


def rare_effect_zh(effect):
    alt = ""
    if "\\nHold reload to use alternate ammo." in effect:
        effect = effect.replace("\\nHold reload to use alternate ammo.", ""); alt = "\\n按住装填键可切换特殊弹药。"
    if effect in RARE_EFFECT_EXACT:
        return RARE_EFFECT_EXACT[effect] + alt
    v = r"(\[DECEA3\][^[]+?\[-\]|\d+)"
    patterns = (
        (rf"Normal attack damage and power attack damage are increased by {v}\.", "普通攻击与蓄力攻击伤害提高{}。"),
        (rf"Has a {v} chance to knock down an enemy on hit\.", "命中敌人时有{}概率将其击倒。"),
        (rf"Power attacks have a {v} chance to knock down the enemy\.", "蓄力攻击有{}概率将敌人击倒。"),
        (rf"Reload speed increased by {v} times\.?", "装填速度提高{}倍。"),
        (rf"Experience gained from headshot kills(?: has been)? increased by {v}\.", "爆头击杀获得的经验提高{}。"),
        (rf"Experience gained on kill increased by {v}\.", "击杀获得的经验提高{}。"),
        (rf"Experience gained from kills(?: with this weapon)?(?: has been)? increased by {v}\.", "使用此武器击杀获得的经验提高{}。"),
        (rf"Experience gained from headshots with this weapon has been increased by {v}\.", "使用此武器爆头击杀获得的经验提高{}。"),
        (rf"Headshot damage increased by {v}\.", "爆头伤害提高{}。"),
        (rf"When you kill an enemy, you restore {v} health\.", "击杀敌人时恢复{}点生命。"),
        (rf"Sneak attacks deal {v} more damage\.", "潜行攻击额外造成{}伤害。"),
        (rf"Power attack damage increased by {v}\. Power attacks have a {v} chance to knock back all nearby enemies\.", "蓄力攻击伤害提高{}，并有{}概率击退附近所有敌人。"),
        (rf"Damage increased\. Normal attacks have a {v} chance to knock back all nearby enemies\.", "伤害提高；普通攻击有{}概率击退附近所有敌人。"),
        (rf"Normal attacks have a {v} chance to knock back the enemy\.", "普通攻击有{}概率击退敌人。"),
        (rf"{v} bonus when butchering animal corpses or collecting mutation samples\.", "屠宰动物尸体或采集变异样本时，收获提高{}。"),
        (rf"Wood harvesting and power attack block damage increased by {v}\.", "木材采集量与蓄力攻击的方块伤害提高{}。"),
        (rf"Wood production increased by {v}\.", "木材采集量提高{}。"),
        (rf"Resource production increased by {v}\.", "资源采集量提高{}。"),
        (rf"Damage to iron increased by {v} and the amount of resources mined by {v}\.", "对铁质方块的伤害提高{}，采集资源量提高{}。"),
        (rf"Damage to stone increased by {v} and the amount of resources mined by {v}\.", "对石质方块的伤害提高{}，采集资源量提高{}。"),
        (rf"Damage to clay increased by {v} and the amount of resources mined by {v}\.", "对土质方块的伤害提高{}，采集资源量提高{}。"),
        (rf"The amount of resources and rare alloys obtained from disassembling has been increased by {v}\.", "拆解获得的资源与稀有合金提高{}。"),
        (rf"{v} chance to deal devastating damage and knockback on enemy headshots\.", "爆头时有{}概率造成毁灭性伤害并击退敌人。"),
        (rf"The Power Attack has a {v} chance to inflict terrible wounds on the enemy and reduce their damage resistance\.", "蓄力攻击有{}概率对敌人造成重创并降低其伤害抗性。"),
    )
    for entry in patterns:
        pattern = entry[0]
        template = entry[1]
        match = re.fullmatch(pattern, effect)
        if match:
            return template.format(*match.groups()) + alt
    match = re.fullmatch(rf"When you kill an enemy, you restore {v} health and {v} stamina\.", effect)
    if match:
        return f"击杀敌人时恢复{match.group(1)}点生命与{match.group(2)}点耐力。" + alt
    match = re.fullmatch(rf"When you kill an enemy, you restore {v} health\.?(?: and)? {v} stamina\.", effect)
    if match:
        return f"击杀敌人时恢复{match.group(1)}点生命与{match.group(2)}点耐力。" + alt
    match = re.fullmatch(rf"A normal attack on an enemy restores {v} health unit, a strengthened attack restores {v}\.", effect)
    if match:
        return f"普通攻击恢复{match.group(1)}点生命，蓄力攻击恢复{match.group(2)}点生命。" + alt
    match = re.fullmatch(rf"When hitting an enemy in the head, damage is increased by {v} and reload speed by {v} for {v} seconds\.", effect)
    if match:
        return f"爆头后伤害提高{match.group(1)}、装填速度提高{match.group(2)}，持续{match.group(3)}秒。" + alt
    return None


def translate_projectz_rare_properties() -> None:
    path = ROOT / "01-ProjectZ/Config/Localization.csv"; lines = path.read_text(encoding="utf-8-sig").splitlines(keepends=True)
    header = next(csv.reader([lines[0]])); columns = {name.lower(): i for i, name in enumerate(header)}; e = columns["english"]; z = columns["schinese"]
    scrap_names = {"Iron": "铁", "Handgun Parts": "手枪零件", "Small Stone": "小石头", "Bow / Crossbow Parts": "弓弩零件", "Steel Tool parts": "钢制工具零件", "Steel Tool Parts": "钢制工具零件", "Rifle parts": "步枪零件", "Shotgun Parts": "霰弹枪零件", "Machine Gun Parts": "机枪零件", "Motor Tool Parts": "动力工具零件", "Wood": "木材", "Steel Club Parts": "钢棍零件", "Steel Sledgehammer parts": "钢制大锤零件", "Cloth": "布料", "Steel Knuckles parts": "钢制拳套零件", "Machete parts": "砍刀零件", "Stun Baton parts": "电击棒零件", "Steel Spear parts": "钢矛零件", "Bone": "骨头", "obtain bones": "骨头"}
    changed = 0
    for index in range(1, len(lines)):
        line = lines[index]; ending = "\r\n" if line.endswith("\r\n") else "\n" if line.endswith("\n") else ""; row = next(csv.reader([line.removesuffix(ending)]))
        if len(row) <= z or "Rare" not in row[0] or not row[0].endswith("Desc"):
            continue
        match = re.search(r"Has rare property \[FAFF63\]«([^»]+)»\[-\]\\n(.*?)(?=\\n\\nRepair|$)", row[e])
        if not match:
            continue
        effect = rare_effect_zh(match.group(2))
        property_name = RARE_PROPERTY_NAMES.get(match.group(1))
        marker = row[z].rfind("[FAFF63]")
        if effect is None or property_name is None or marker < 0:
            continue
        cut = row[z].rfind("\\n\\n", 0, marker)
        base = row[z][:cut if cut >= 0 else marker].rstrip()
        translated = f"{base}\\n\\n具有稀有属性[FAFF63]“{property_name}”[-]\\n{effect}"
        repair = re.search(r"Repair with (?:a )?([^.]*)\.", row[e])
        if repair:
            repair_names = {"Repair Kit": "修理包", "Small Stone": "小石头", "Short Iron Pipe": "短铁管", "Wood": "木材", "Cloth": "布料", "Bone": "骨头"}
            repair_name = repair_names.get(repair.group(1), repair.group(1))
            translated += f"\\n\\n使用{repair_name}修理。"
        scrap = re.search(r"Scrap to ([^.]*)\.", row[e])
        if scrap:
            translated += f"\\n拆解可获得{scrap_names.get(scrap.group(1), scrap.group(1))}。"
        if translated == row[z]:
            continue
        row[z] = translated; output = io.StringIO(); csv.writer(output, lineterminator=ending, quoting=csv.QUOTE_MINIMAL).writerow(row); lines[index] = output.getvalue(); changed += 1
    with path.open("w", encoding="utf-8-sig", newline="") as handle: handle.write("".join(lines))
    print(f"01-ProjectZ/Config/Localization.csv: rebuilt {changed} rare-property descriptions")


translate_projectz_rare_properties()


def polish_final_mixed_visible_text() -> None:
    """Remove obvious English fragments left inside the Simplified Chinese display text."""
    paths = sorted(ROOT.glob("*/Config/Localization.csv"))
    literal_replacements = (
        ("Mutation Sample Cache", "变异样本储备箱"), ("Supply Cache", "补给储备箱"),
        ("War Cache", "战争储备箱"), ("Boss Cache", "首领储备箱"), ("Final Cache", "最终储备箱"),
        ("Loot Bag", "战利品袋"), ("Burning Claws", "燃烧利爪"),
        ("\\nBoss", "\\n首领"), ("\\nBOSS", "\\n首领"),
        ("爆破狂人", "呆呆"), ("毁灭领主", "末日领主"), ("电魔", "电能恶魔"),
        ("爆裂鹰", "爆破之鹰"), ("行刑者", "刽子手"), ("突变样本", "变异样本"),
        ("Driftjack", "漂移杰克"), ("Nitrojack", "氮气杰克"),
        ("Trailhunter", "寻径猎手"), ("Hellglide", "地狱滑翔者"),
    )
    word_replacements = (
        ("BOSS", "首领"), ("Boss", "首领"), ("Minion", "仆从"),
        ("Bundle", "礼包"), ("Cache", "储备箱"), ("Claws", "利爪"),
        ("Fortress", "要塞"), ("Hazard", "危险度"), ("Contract", "契约"),
        ("Horde", "尸潮"), ("Assault", "突袭"), ("Defense", "防守"),
        ("Hunt", "猎杀"), ("Siege", "围攻"), ("Patrol", "巡逻"),
        ("Gauntlet", "试炼"), ("Waves", "波"), ("Wave", "波"),
    )
    for path in paths:
        lines = path.read_text(encoding="utf-8-sig").splitlines(keepends=True)
        header = next(csv.reader([lines[0]])); columns = {name.lower(): i for i, name in enumerate(header)}; z = columns["schinese"]
        changed = 0
        for index in range(1, len(lines)):
            line = lines[index]; ending = "\r\n" if line.endswith("\r\n") else "\n" if line.endswith("\n") else ""
            row = next(csv.reader([line.removesuffix(ending)]))
            if len(row) <= z or not row[z]:
                continue
            updated = row[z]
            for source, target in literal_replacements:
                updated = updated.replace(source, target)
            for source, target in word_replacements:
                updated = re.sub(rf"(?<![A-Za-z]){source}(?![A-Za-z])", target, updated)
            updated = updated.replace("随从", "仆从")
            updated = re.sub(r"\s+([：，。；！？])", r"\1", updated)
            updated = re.sub(r"(?<=[\u4e00-\u9fff]) +(?=[\u4e00-\u9fff])", "", updated)
            if updated == row[z]:
                continue
            row[z] = updated; output = io.StringIO(); csv.writer(output, lineterminator=ending, quoting=csv.QUOTE_MINIMAL).writerow(row)
            lines[index] = output.getvalue(); changed += 1
        with path.open("w", encoding="utf-8-sig", newline="") as handle: handle.write("".join(lines))
        print(f"{path.relative_to(ROOT)}: polished {changed} final mixed-language entries")


polish_final_mixed_visible_text()
