# -*- coding: utf-8 -*-
"""把 dist 里编译好的 dll 打成一个可以直接发给别人的发布包。

用法：
    python tools/make-release.py               打包当前版本
    python tools/make-release.py --check       只校验（不改任何文件）
    python tools/make-release.py --selftest    自检剧本本身的判据

包内结构（与 v1.5.0 起的惯例一致，解压到游戏根目录即可）：
    安装说明.txt
    更新.bat
    update.ps1
    BepInEx/plugins/MystiaOrderHelper-v<版本>.dll

版本号不在这里写死：从 OrderHelperPlugin.cs 取，再和 build.ps1 对一遍。
对不上就报错退出 —— 版本号三处不同步是本项目已经犯过的错。
"""

import argparse
import hashlib
import io
import os
import re
import shutil
import sys
import zipfile

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
NAME = "MystiaOrderHelper"
PACKAGING = os.path.join(ROOT, "packaging")   # 包内带的更新脚本原件放这里
RELEASE = os.path.join(ROOT, "release")       # zip 一律落在 release\

# 每个版本"玩家看得懂"的变化。打包时取当前版本 + 前两个历史版本。
# 加新版本时只需要在这里补一段，安装说明会自动生成。
NOTES = {
    "1.6.7": [
        "修卡顿。问题出在两处，都在后台扫描上：",
        "① 头像的全场扫图原来 12 秒做一次，每次卡 200~290ms——这是最明显的那种「顿一下」。",
        "   现在改成 45 秒一次、整局最多 5 次；并且同一张图只读一次名字，扫图本身也快了一截。",
        "② 头像分片扫描的时间预算原来形同虚设（一批 64 张才看一次表，一批就能撞到 40ms），",
        "   现在 8 张一查，单次扫描从 46ms 压到几毫秒；两次扫描也不会再在同一拍里各跑一遍。",
        "③ 常规采集从 0.5 秒一次降到 0.8 秒一次。客人点单、上菜晚 0.3 秒看到没感觉；",
        "   而切「刷好感 / 按预算推荐」、开关厨具面板改成事件触发，仍然立刻生效。",
        "推荐逻辑、排序、界面外观都没有改动。",
    ],
    "1.6.6": [
        "修「三位一体」这类特殊厨具的推荐框：切了刷好感/按预算，料理框里的菜仍然不动。",
        "原因有两个，都只在这种厨具上出现：",
        "① 特殊厨具的候选是另外重新算的一批菜（不是面板里那一批），插件按「同一个对象」去比对，",
        "永远认不出是同一道菜，同步逻辑静默失效——现在改成按菜谱 ID 认；",
        "② 特殊厨具只显示前 4 道，主面板推荐的那道如果排在池子第 10 名就会被截掉——",
        "现在先把主面板搭配里的菜挑出来占位，再补其余。",
        "普通厨具和酒水柜不受影响（上一版已同步）。",
    ],
    "1.6.5": [
        "把「刷好感 / 按预算」的口径同步到料理台和酒水柜旁边的推荐框——之前切了开关只有主面板会变。",
        "① 料理推荐框：菜的先后顺序改成跟主面板的最佳搭配一致（主面板推荐的那几道排最前）；",
        "② 酒水推荐框：主面板搭配里用的那几杯排最前，刷好感时剩下的粉评酒按价格从低到高",
        "（同样能粉评，越便宜越先上，省下的钱能让她多吃一单）；",
        "③ 顺手修掉一个更隐蔽的问题：打开料理台 / 酒水柜时游戏会把时间压到 0，",
        "插件原来的刷新计时用的是游戏内时间，一进柜子就停摆——所以在柜子界面切换开关，",
        "主面板看着变了、推荐框却一直显示旧数据。现在改用真实时间，暂停中也会刷新。",
    ],
    "1.6.4": [
        "补上「刷稀客好感」的开关按钮——上一版只能在配置文件里改，游戏里找不到。",
        "① 主面板顶部多一个「刷好感：开/关」，营业中随手点一下就切，不用按 F9 把画面暂停；",
        "② 控制面板（F9）新增【推荐口径】区：按预算推荐、刷稀客好感两个开关都在这里，附一行说明；",
        "③ 「恢复默认」也会把这两项一起复位（按预算推荐开、刷好感关）。",
        "刷好感只对稀客生效，并且要靠「按预算推荐」读她的钱包才留得出后面几单的钱；",
        "那项关着时控制面板会明确提示「刷好感不会起作用」。",
    ],
    "1.6.3": [
        "新增「刷稀客好感」选项（配置 [Behaviour] FavorFarm，默认关）。",
        "想让某位稀客吃满所有单（最多 5 单）刷好感时打开它，推荐会变成「先保粉评，再省钱」：",
        "① 这一单的上限 = 她手上现的钱 − 后面每单的「最低粉评价」，先把后面几单的钱留出来；",
        "② 同样能粉评时挑更便宜的酒，而不是更贵的；③ 净分够 4 分（粉评线）之后不再追更高分，",
        "同分挑便宜的菜——多出来的分数不会多给好感，却更贵。",
        "只对稀客生效，常客不受影响；关掉就是原来的口径（能花就花、利润优先）。",
        "状态行会标「刷好感」，搭配最后一行照旧显示价格与预算余。",
    ],
    "1.6.2": [
        "预算的算法改了：不再把客人的钱按「还能点几单」平均分。",
        "均分会把第一单压得太死（预算 1000 却只敢花 500），反而错过本来吃得起的好菜。",
        "现在的规则只有一条：还有单没上时，这一单最多花他手上现的钱；",
        "只有最后一单才允许小幅超支（总预算的一成以内），也就是「预算 1000，第一单 800、最后一单 300」这种搭配是可行的。",
        "搭配最后一行照旧写「价X 预算余Y」；最后一单花掉了那点余量会写「略超Z（余量内）」，",
        "真的超过上限才写「⚠超Z，可能黑评」。",
    ],
    "1.6.1": [
        "新增「按客人的预算配酒」：客人身上带的钱有限，搭配价格远超预算时他付不起，会直接给黑评。",
        "现在每位客人都会读一次钱包，算出「这一单最多花多少」＝剩余的钱 ÷ 他还剩几单，",
        "付不起的酒不再推荐；付得起的里面先保证粉评，再挑利润高、库存多的。",
        "搭配下面多一行「价X 预算余Y」；万一超了会写「⚠超Z，可能黑评」，由你自己决定要不要冒这个险。",
        "客人详情和推荐框标题也会显示剩余预算，酒水按钮上带单价，付不起的标「⚠超预算」。",
        "读不到客人的钱时这个功能自动失效，推荐和以前完全一样。",
        "不想要：配置 [Behaviour] RespectFund = false。",
    ],
    "1.6.0": [
        "推荐算法换了口径：",
        "① 食材推荐不再掺酒水的 tag 命中——菜的评级和排序只看这道菜自己（含加料）命中多少 tag；",
        "② 加料目标固定为「单单食材就粉评」（4 分），不再拿酒的分数去抵，所以能加料就真的会加料；",
        "③ 食材排序：先按命中 tag 数，命中数一样再比利润率；",
        "④ 酒水排序：先保证「配上这道菜还能粉评」，这一档里按利润、再按库存数量。",
        "另外修掉 v1.5.9 里加料仍然一份都加不进去的问题：旧口径把酒分算进了目标，",
        "酒常有 2~3 分，菜自己早就够分，规划器就一份都不挑（实测 16 次一键制作全是 加料=0）。",
    ],
    "1.5.9": [
        "修 v1.5.7 的加料「一份都加不进去」：上一版会为了「避免顺手凑成另一道菜」把加料撤回，",
        "可这个判断条件过宽（锅里只要有米，任何以米开头的短配方都算命中），实测一局撤回 85 份、",
        "15 次一键制作没有一次带上加料。",
        "现在不再做这个判断：加料后锅里如果也能做出别的菜，面板会照常全部列出来，由你自己选做哪道；",
        "只有当加料把食材组合搞得一道菜都匹配不上时，才会自动退掉最后一味。",
        "黑暗料理仍然会挡（加料不得引入菜品禁止的味道）。",
        "想确认加料有没有真的进锅，可看日志里的「游戏接受的加料=N」，N 大于 0 就是成了。",
    ],
    "1.5.8": [
        "本次只有「测量工具」升级，游戏内表现和 v1.5.7 完全一样：",
        "性能日志拆出了更细的步骤——采集分成 枚举/指纹/重算/文字/评分子项，头像分成 快照/扫描/全量/合成，",
        "绘制分成 皮肤/主面板/推荐框/控制面板，哪一段慢一眼能看出来",
        "每 30 秒多两行：一行「计数」（缓存命中几次、重算几次、加料被挡几次），",
        "一行「最慢三次」（只列超过 6ms 的段，带帧号），偶发尖峰不再被平均值盖住",
        "长帧日志会写上「上帧最慢=某步骤 Xms」；如果那一段只有零点几毫秒，说明这一卡不是插件造成的",
    ],
    "1.5.7": [
        "加料推荐不再只顾加分，会先检查「这锅加下去会不会出事」，冲突的食材一律不推荐：",
        "① 加料不得引入菜品禁止的 tag（游戏数据里的 BanTags）——这种组合会变成黑暗料理，"
        "以前插件只看 tag 能加多少分，完全没管这一层",
        "② 加料后如果正好又凑成另一道菜，会主动撤回最后一两份料，"
        "免得锅里同时满足两道菜、手快点错做成了别的菜",
        "被冲突挡掉、最后没加料时，主面板会写「加料会冲突，已跳过」，不再让人以为是插件漏了",
        "不想要这层检查：[Behaviour] AvoidDarkCooking = false（退回 v1.5.6 的纯 tag 贪心）",
    ],
    "1.5.6": [
        "性能优化（本次只有内部改动，功能和界面跟 v1.5.5 完全一样）：",
        "客人数据加了「没变化就不重算」的缓存——以前每 0.5 秒把所有客人的名字、喜好标签、"
        "推荐菜重算一遍，实测 3.6–5.6ms，在 60fps 下就是每半秒顿一下；现在只有点单状态真的变了才重算",
        "头像扫描从「每 2 秒把全场几千个 Image 过一遍」（实测峰值 55–90ms）改成"
        "「分片扫描 + 快照缓存」，每次只处理一小段，开销封顶",
        "诊断用的「客人巡查」默认关闭（它每 5 秒要读一遍所有控制器状态，实测 8–26ms，是最重的一项）；"
        "需要排查时在配置里把 [Diagnostics] GuestAudit 打开，间隔也从 5 秒放宽到 30 秒",
        "顺手清掉几处每帧产生垃圾的地方（每帧拼字符串、每个头像两个 GUILayoutOption 等）",
    ],
    "1.5.5": [
        "推荐算法加入「加料」：除了菜谱自带的标签，追加食材的标签也计入评价。"
        "以前一道菜分不够就只能差几分，现在会自动帮你算出「加哪几样料能凑到粉评」",
        "加料有上限：一锅最多 5 个食材（含菜谱自带的），菜谱本身要 5 个食材的菜不加料",
        "同样能粉评时按利润排序：利润 = 菜品售价 − 食材成本（菜谱食材 + 加料），"
        "加料不改变售价，所以能少加就少加，不会为了凑分白烧食材",
        "同时看食材余量：优先用你库存充裕的食材，不把稀有食材吃到见底",
        "主面板每个搭配多一行「｜ 加料 猪肉、洋葱 ｜ 赚38 余12份」，"
        "推荐框的明细行也会标出要加的料",
        "不想要这套逻辑就把 [Behaviour] ConsiderExtras 改成 false（退回只看菜谱标签）",
        "含 v1.5.4 的进游戏版本提示（每次启动弹一次，可关）",
    ],
    "1.5.4": [
        "新增进游戏后的版本提示：启动约 2 秒后屏幕上方居中弹出一张小卡片，"
        "写着「夜雀点单助手 v版本号」，停留几秒自动淡出，每次启动只弹一次"
        "（不想要就把 [Display] ShowVersionNotice 改成 false）",
        "其余功能、推荐逻辑与 v1.5.3 完全一致",
    ],
    "1.5.3": [
        "全新皮肤：背景、按钮、滚动条改用游戏自带的订单小票素材（和风纸条 + 墨色描边），"
        "主面板、控制面板、料理框、酒水框四处统一；想回旧外观把 [Display] VanillaSkin 改成 false",
        "主面板改版：上半是头像横排（一行放不下自动换行），点谁在下面展开谁的文字，"
        "不用再一屏堆满文字；客人离场后自动切到还在场的下一位",
        "稀客头像：直接从游戏立绘里取（身体层 + 表情层合成，按眼睛位置裁成头肩特写），"
        "常客显示名字首字占位；也可以自己放图覆盖，见「自定义头像」",
        "还没点单的客人不再出现在面板和推荐框里（以前会显示「（尚未点单）」占着位置）",
        "修复：稀客名字过长时被截断只显示一半（给控件设了固定高度，超出的部分被静默裁掉）",
        "推荐逻辑、评分与 v1.5.2 完全一致，本版只改外观与显示范围",
    ],
    "1.5.2": [
        "修复「稀客影像」不进推荐框：用露易兹的符卡「传奇的格列佛」把没吃饱的稀客拍成旅者博客、"
        "当日结束后投影回来的影像，现在会被正常识别并给出推荐（此前被当成已离店客人过滤掉了）",
        "营业中每 0.5 秒的推荐计算提速：菜名、标签、食材、厨具这些整局不变的数据改成查一次就缓存，"
        "不再每个客人、每次刷新都重新从游戏里取一遍（客人越多差别越明显）",
        "新增「客人巡查」日志：每 5 秒一行（内容有变化才写），能看到每位客人的控制器类型、点单状态、"
        "桌位、在店/已离店，排查「客人没出现在推荐框」时直接看它",
    ],
    "1.5.1": [
        "修复营业中偶发卡顿：面板上的文字原来是每帧重新拼的（游戏每帧会把界面画两遍），"
        "改成每 0.5 秒算一次、平时直接显示；客人越多越明显",
        "定时导出客人候选（配置项 SnapshotLog）改为默认关闭 —— 它每 30 秒写几十行，本身也会拖慢",
        "新增性能日志（配置项 PerfLog，默认开）：每 30 秒一行「性能30s」，记录采集/绘制耗时与长帧",
    ],
    "1.5.0": [
        "修复主面板配对的酒水净分显示：以前显示的是「命中数」，踩到厌恶标签时会虚高，"
        "现在和菜品一样显示真正的净分（命中喜好 − 命中厌恶）",
        "清理两处永不生效的旧过滤代码（行为无变化，只是不再误导后续维护）",
        "压缩包内附带自动更新脚本（update.ps1 + 更新.bat），装好后可双击一键升级",
    ],
}

TEMPLATE = """夜雀点单助手 v{VERSION} —— 安装说明
==================================================

【这是什么】
东方夜雀食堂（Touhou Mystia Izakaya）的点单辅助插件：
屏幕上一眼看到每位客人要什么、推荐做什么菜配什么酒、评分够不够粉评，点一下直接开做。

【前置要求】
1. 游戏：东方夜雀食堂（Steam 版），已能正常运行
2. BepInEx 6.0.0-be.785（IL2CPP 版，游戏目录下有 winhttp.dll）
3. 建议同时安装 MetaMystia v0.29.4（本插件与它共存，不改动它的任何文件）

【安装步骤】
1. 先完全退出游戏（插件 dll 被占用时替换会失败）
2. 把压缩包里的所有内容解压到游戏根目录（和 winhttp.dll 同一个文件夹）。
   解压后应该长这样：
    游戏目录\\BepInEx\\plugins\\{DLL}
    游戏目录\\update.ps1
    游戏目录\\更新.bat
    游戏目录\\安装说明.txt
3. 启动游戏，用记事本打开 游戏目录\\BepInEx\\LogOutput.log，
   看到「夜雀点单助手 v{VERSION} 已加载（F8 主面板，F9 控制面板）」即安装成功

【以后怎么更新】
双击游戏目录下的「更新.bat」，脚本会连 GitHub 检查新版本并自动替换 dll，
旧版本会留一份在 BepInEx\\plugins\\_backup\\ 里（不会删除）。
只想看看有没有新版、不安装：命令行执行 update.ps1 -Check

【卸载】
删掉 BepInEx\\plugins\\{DLL} 即可，不会影响存档。
不需要自动更新的话，把 update.ps1 和 更新.bat 一起删掉也行。

【从旧版本升级】
把 BepInEx\\plugins\\ 里旧的 MystiaOrderHelper-v*.dll 删掉，只留 {DLL} 这一个。
同一个插件存在两个 dll 会同时加载、互相打架（日志里会出现两遍「已加载」）。
配置文件不用删：新版本会沿用旧配置，新增项自动补默认值。

【按键】
F8   打开 / 关闭「点单助手」主面板（进入营业场景会自动打开）
F9   打开 / 关闭「控制面板」（打开时暂停游戏时间，右键单击关闭）
鼠标 拖动任意窗口的标题栏可以移动窗口，松手自动记住位置

【主面板怎么看】
上半是**头像排**，每位在场客人一个头像：稀客显示游戏自带的立绘头像，常客显示名字首个字。
点任意一个头像，下半展开这位客人的完整信息（默认自动选中第一位稀客）：

  ◆ 名字（稀客/常客） 桌位 X  剩N单
  点菜：…（客人要什么）
  点酒：…
  [菜 + 酒 搭配]
      → 菜品名[厨具]＋酒水名
        粉评 5分  ｜  净分 菜2 酒3

· 粉评 / 橙评 / 绿评 / 紫评 / 黑评 = 游戏里的评价等级，粉评是最高
· 「合计分」= 菜命中喜好的标签数 + 酒命中喜好的标签数，合计 4 分以上才可能粉评
· 「净分」= 命中喜好 − 命中厌恶，负数说明这道菜/这瓶酒会让客人扣分
· 稀客：凑不出粉评的组合不会列出来，改为单独给出「命中标签最多的菜」和「最好的酒水」
· 常客：照旧全部列出，不受粉评限制
· **还没点单的客人不会出现在面板和推荐框里**（v1.5.3 起）：不再显示「（尚未点单）」这类占位行，
  等 TA 点完单才会出现。客人已上完菜在等下一单时同样不显示。

【推荐框】
打开任意厨具（煮锅/烤架/油锅/蒸锅/料理台/三位一体等），屏幕右侧弹出「【料理】推荐（厨具名）」；
打开酒水面板弹出「【酒水】推荐」。两个框顶部也有一排头像，同样点一下就切换看谁。
点推荐条目 = 直接把食材放上料理台并开始做（可在控制面板关闭）。

【自定义头像（可选）】
游戏里自动抓的立绘已经够用，但如果你觉得某个客人的头像不好看、或者抓错了人，可以自己放图覆盖。
目录：游戏目录\\BepInEx\\config\\orderhelper-portraits\\
（第一次运行插件时自动创建，里面带一份「说明.txt」）

· 文件名 = 面板上显示的名字，例如「茨木华扇.png」「博丽灵梦.png」
· 名字不必一字不差：先精确匹配，再繁简归一，再双向包含
  （`咲夜.png` 能命中「十六夜咲夜」，`茨木华扇.png` 能命中「华扇」）
· 任意尺寸，会自动按中心裁成正方形并缩到 48×48；建议用正方形、96 或 128 更清晰
· 建议只截头像本身，不要带周围的面板文字
· 优先级最高：放了图就一定用它，盖过所有自动识别

【控制面板（F9）能调什么】
· 字号 12–32
· 是否显示常客
· 一键制作 开/关（关闭后点推荐不再放食材）
· 三个窗口（主面板 / 料理推荐框 / 酒水推荐框）的位置和大小：
  方向键微调位置，宽/高按钮微调尺寸，「默认」按钮恢复发布时的默认布局
· 导出客人信息到日志（手动触发一次，排查问题用）
· 恢复默认

【配置记忆】
所有改动都会写进 BepInEx\\config\\local.mystia.orderhelper.cfg，下次进游戏自动沿用。
注意：如果要手动编辑这个文件，请先关掉游戏，否则退出游戏时会被插件回写覆盖。

【想回到旧外观】
配置文件里有 [Display] VanillaSkin = true / false
改成 false 并重启游戏，就退回旧版的灰盒外观（头像、布局仍是新的），
用来判断「某个问题是不是新版皮肤引入的」。

【诊断与日志】
配置文件 BepInEx\\config\\local.mystia.orderhelper.cfg 的 [Diagnostics] 段（改完要重启游戏）：

  PerfLog     = true    每 30 秒一行「性能30s」：采集 / 绘制的平均与峰值耗时、长帧条数
  GuestAudit  = true    每 5 秒一行「巡查」：场上每位客人的类型、点单状态、桌位、在店/已离店
  SnapshotLog = false   每 30 秒把每位客人的候选菜全部写进日志（很长，只在推荐不对时打开）
  UiProbe     = false   UI 素材侦察，会扫全场并往 config 写 dump 文件，只在改 UI 时开

日志都在 BepInEx\\LogOutput.log。嫌日志长可以把 PerfLog / GuestAudit 关掉，不影响任何功能。

【版本变化】
{CHANGES}

【常见问题】
Q: 装好后按 F8 没反应？
A: 先看 LogOutput.log 有没有「已加载」；没有则是 BepInEx 前置没装好，与本插件无关。

Q: 界面还是灰底方块，没有纸条和头像？
A: ①确认 plugins 里只有当前版本一个 dll；②检查配置 [Display] VanillaSkin 是不是被改成了 false；
   ③素材是运行时从游戏场景里抓的，刚进游戏的头一两秒可能还没抓到，等一下就好。
   如果始终没有，把 LogOutput.log 发给作者，里面会有「缺少素材」的记录。

Q: 某个客人的头像明显是别人，或者是空白？
A: 插件按名字匹配立绘，冷门角色或不同译名可能对不上。最稳的办法是自己放一张图到
   BepInEx\\config\\orderhelper-portraits\\（见上面「自定义头像」）。
   抓不到头像时会退回名字首字占位，不会留空白。

Q: 头像只显示半个头、或者头发把脸挡住了？
A: 取景是按立绘的眼睛位置自动估的，个别立绘会偏。同样用「自定义头像」覆盖最省事。

Q: 推荐框跑到屏幕外了？
A: F9 打开控制面板，点对应窗口那一行的「默认」按钮复位。

Q: 菜名后面的「缺食材：…」是什么意思？
A: 仓库里材料不够。括号里是「现有/需要」。

Q: 客人站在那儿却没出现在推荐框里？
A: 常见原因：还没点单（v1.5.3 起不显示）、已经上过菜在等下一单、正在离店、或者是特殊实体。
   想确认就打开 GuestAudit，看 BepInEx\\LogOutput.log 里带「巡查」的那几行。

Q: 点插件面板上的按钮时，背后的厨具/酒水面板也被打开了？
A: 这是已知问题，本版**没有**做防误触。游戏的点击判定不走 Unity 的 UI 事件系统，
   插件无法在它之外拦住。点击面板时请尽量避免碰到面板之外的游戏物件。
   （曾尝试在游戏的面板打开入口拦截，会导致游戏卡死，已放弃。）

Q: 双击「更新.bat」闪一下就没了 / 提示找不到 git？
A: 不会。更新脚本不依赖 git，用的是 GitHub Releases 接口；出错时窗口会停住显示原因。

Q: 更新提示「无法访问 GitHub」？
A: 网络问题或代理拦截。可以手动下载新版 zip，把里面的 dll 覆盖到 BepInEx\\plugins\\ 即可。
"""


def read_text(path):
    with io.open(path, encoding="utf-8-sig") as f:
        return f.read()


def find_version():
    """从两个地方各取一次版本号，必须一致。"""
    cs_path = os.path.join(ROOT, "src", "OrderHelper", "OrderHelperPlugin.cs")
    ps_path = os.path.join(ROOT, "build.ps1")
    cs = read_text(cs_path)
    ps = read_text(ps_path)

    m1 = re.search(r'Version\s*=\s*"([\d.]+)"', cs)
    m2 = re.search(r"\$version\s*=\s*'([\d.]+)'", ps)
    if not m1:
        raise SystemExit("在 OrderHelperPlugin.cs 里找不到 Version 常量")
    if not m2:
        raise SystemExit("在 build.ps1 里找不到 $version")
    if m1.group(1) != m2.group(1):
        raise SystemExit(
            "版本号不同步：OrderHelperPlugin.cs=%s，build.ps1=%s" % (m1.group(1), m2.group(1))
        )
    return m1.group(1)


def render_changes(version):
    """当前版本 + 前两个版本的说明，新的在前。"""
    def key(v):
        return tuple(int(x) for x in v.split("."))

    have = sorted(NOTES, key=key, reverse=True)
    if version not in NOTES:
        raise SystemExit(
            "NOTES 里没有 v%s 的变化说明，先补一段再打包（现有：%s）"
            % (version, ", ".join(have))
        )
    picked = [v for v in have if key(v) <= key(version)][:3]
    out = []
    for v in picked:
        out.append("【v%s 主要变化】" % v)
        for line in NOTES[v]:
            out.append("· " + line)
        out.append("")
    return "\n".join(out).rstrip()


def render_notes(version, dll):
    text = TEMPLATE.replace("{VERSION}", version).replace("{DLL}", dll).replace(
        "{CHANGES}", render_changes(version)
    )
    left = re.findall(r"\{[A-Z]+\}", text)
    if left:
        raise SystemExit("安装说明里还有没替换的占位符：%s" % left)
    return text


def md5(path):
    h = hashlib.md5()
    with open(path, "rb") as f:
        for chunk in iter(lambda: f.read(1 << 20), b""):
            h.update(chunk)
    return h.hexdigest()


def build(version, check_only=False, game_path=None):
    dll_name = "%s-v%s.dll" % (NAME, version)
    dll = os.path.join(ROOT, "dist", dll_name)
    if not os.path.isfile(dll):
        raise SystemExit("找不到编译产物：%s（先跑 build.ps1）" % dll)
    with open(dll, "rb") as f:
        head = f.read(2)
    if head != b"MZ":
        raise SystemExit("dist 里的文件不是 PE 文件，编译可能失败了")

    for extra in ("update.ps1", "更新.bat"):
        if not os.path.isfile(os.path.join(PACKAGING, extra)):
            raise SystemExit("packaging 目录缺少 %s，包会不完整" % extra)

    notes = render_notes(version, dll_name)
    print("版本      : v%s" % version)
    print("dll       : %s (%d KB, md5 %s)" % (dll_name, os.path.getsize(dll) // 1024, md5(dll)[:12]))
    print("安装说明  : %d 字符 / %d 行" % (len(notes), notes.count("\n") + 1))

    # 打的是不是当前装机的那份？不一致只警告，不拦。
    if game_path:
        installed_dir = os.path.join(game_path, "BepInEx", "plugins")
        if os.path.isdir(installed_dir):
            same = [f for f in os.listdir(installed_dir) if f.lower() == dll_name.lower()]
            if same:
                p = os.path.join(installed_dir, same[0])
                if md5(p) == md5(dll):
                    print("装机对比  : 一致（打的就是现在游戏里跑的那份）")
                else:
                    print("装机对比  : ！不同（dist 与 BepInEx\\plugins 里的不是同一个文件）")

    if check_only:
        print("\n--check：校验通过，没有写任何文件。")
        return 0

    zip_cn = os.path.join(RELEASE, "夜雀点单助手-v%s.zip" % version)
    zip_en = os.path.join(RELEASE, "%s-v%s.zip" % (NAME, version))

    if not os.path.isdir(RELEASE):
        os.makedirs(RELEASE)

    with zipfile.ZipFile(zip_cn, "w", zipfile.ZIP_DEFLATED) as z:
        z.writestr("安装说明.txt", notes.encode("utf-8-sig"), zipfile.ZIP_DEFLATED)
        z.write(os.path.join(PACKAGING, "update.ps1"), "update.ps1")
        z.write(os.path.join(PACKAGING, "更新.bat"), "更新.bat")
        z.write(dll, "BepInEx/plugins/" + dll_name)

    verify(zip_cn, dll, dll_name)

    shutil.copyfile(zip_cn, zip_en)  # GitHub Release 的资产名必须是 ASCII
    print("\n产物：")
    print("  %s  (%d 字节)" % (os.path.basename(zip_cn), os.path.getsize(zip_cn)))
    print("  %s  (%d 字节)  ← 上传 GitHub Release 用这个名字" % (os.path.basename(zip_en), os.path.getsize(zip_en)))
    return 0


def verify(zip_path, dll, dll_name):
    """打完必须回读一遍：条目对不对、CRC 有没有坏、dll 是不是原来那份。"""
    with zipfile.ZipFile(zip_path) as z:
        bad = z.testzip()
        if bad:
            raise SystemExit("压缩包损坏：%s" % bad)
        items = z.namelist()
        expect = ["安装说明.txt", "update.ps1", "更新.bat", "BepInEx/plugins/" + dll_name]
        missing = [x for x in expect if x not in items]
        if missing:
            raise SystemExit("压缩包缺内容：%s" % missing)
        got = hashlib.md5(z.read("BepInEx/plugins/" + dll_name)).hexdigest()
        if got != md5(dll):
            raise SystemExit("包里的 dll 与 dist 不一致（md5 %s）" % got)
        text = z.read("安装说明.txt").decode("utf-8-sig")
        if "v" + dll_name.split("-v")[1].split(".dll")[0] not in text:
            raise SystemExit("安装说明里的版本号没对上")
    print("校验      : %d 个条目 / CRC 正常 / dll md5 与 dist 一致" % len(items))


def selftest():
    items = []

    # 1 版本解析：两边一致时能取到
    v = find_version()
    items.append(("从 cs 与 build.ps1 取到同一个版本号", bool(re.match(r"^\d+\.\d+\.\d+$", v))))

    # 2 变化说明只取当前与前两个，顺序是新的在前
    ch = render_changes(v)
    heads = re.findall(r"【v([\d.]+) 主要变化】", ch)
    items.append(("变化说明最多 3 段", len(heads) == 3))
    items.append(("变化说明按版本倒序", heads == sorted(heads, key=lambda s: [int(x) for x in s.split(".")], reverse=True)))
    items.append(("变化说明以当前版本开头", heads[0] == v))

    # 3 占位符全部替换
    text = render_notes(v, "MystiaOrderHelper-v%s.dll" % v)
    items.append(("没有残留占位符", not re.search(r"\{[A-Z]+\}", text)))
    items.append(("说明里写了正确的 dll 名", "MystiaOrderHelper-v%s.dll" % v in text))
    items.append(("说明里写了正确的版本号", ("v" + v) in text.splitlines()[0]))

    # 4 NOTES 的排序函数不会被 1.5.10 这种版本骗（按数字比，不按字符串）
    k = lambda s: [int(x) for x in s.split(".")]
    items.append(("版本比较按数字", k("1.5.10") > k("1.5.9")))

    # 5 GBK 的 更新.bat 不能被当成 UTF-8 读（读法错了会抛异常，说明检测有效）
    p = os.path.join(PACKAGING, "更新.bat")
    if os.path.isfile(p):
        try:
            io.open(p, encoding="utf-8").read()
            items.append(("更新.bat 检测为非 UTF-8", False))
        except UnicodeDecodeError:
            items.append(("更新.bat 检测为非 UTF-8", True))

    ok = True
    for label, passed in items:
        print(("  ✅ " if passed else "  ❌ ") + label)
        ok = ok and passed
    print("自检 %d 项：%s" % (len(items), "全过" if ok else "有失败，别信它"))
    return 0 if ok else 1


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--check", action="store_true", help="只校验，不出包")
    ap.add_argument("--selftest", action="store_true", help="自检脚本判据")
    ap.add_argument("--game", default=r"D:\SteamLibrary\steamapps\common\Touhou Mystia Izakaya",
                    help="游戏目录（用来对比装机版本，不给就跳过）")
    a = ap.parse_args()

    if a.selftest:
        return selftest()

    game = a.game if os.path.isdir(a.game) else None
    return build(find_version(), check_only=a.check, game_path=game)


if __name__ == "__main__":
    sys.exit(main())
