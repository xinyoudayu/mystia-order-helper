# 交接文档：夜雀点单助手（Mystia Order Helper）

> **第一次接手本项目，先读 `接手文档.md`**：30 分钟上手版（读什么、守什么铁律、别碰什么、怎么回滚）。
> **要动代码，再读 `MOD制作经验文档.md`**：全部踩坑记录、游戏 API 地图、构建流程。本文件讲项目现状与版本轨迹。

- 交接时间：2026-10-24 22:40（v1.6.7：修卡顿——全场扫图降频 + 扫描预算真正生效 + 采集节拍 0.8s）
- 当前版本：**v1.6.7**（`dist\MystiaOrderHelper-v1.6.7.dll`，md5 `F7EA1E6A`，165376 字节，**已于 22:35 装机**到 `BepInEx\plugins\MystiaOrderHelper-v1.6.7.dll`，与 dist 逐字节一致）
  - 装机前的 v1.6.6 已备份到 `backup\installed-v1.6.6-20261024\`（md5 `1D550104`），要回滚拷回 plugins 并删掉 v1.6.7
  - 更早的 v1.6.5 已备份到 `backup\installed-v1.6.5-20261024\`（md5 `595C97BE`）
  - 更早的 v1.6.4 已备份到 `backup\installed-v1.6.4-20261024\`（md5 `B2571131`）
  - 更早的 v1.6.3 已备份到 `backup\installed-v1.6.3-20261024\`（md5 `06DDFD6A`）
  - 更早的 v1.6.2 已备份到 `backup\installed-v1.6.2-20261024\`（md5 `BE22B721`）
  - 更早的 v1.6.1 已备份到 `backup\installed-v1.6.1-20261024\`（md5 `4DBCD201`）
  - 更早的 v1.6.0 已备份到 `backup\installed-v1.6.0-20261024\`（md5 `B3FCED2A`）
  - 更早的 v1.5.9 已备份到 `backup\installed-v1.5.9-20261023\`
  - 更早的 v1.5.8 已备份到 `backup\installed-v1.5.8-20261023\`（md5 `F2236E38`）
  - 更早的 v1.5.7 已备份到 `backup\installed-v1.5.7-20261023\`（md5 `B3B195D8`），v1.5.6 在 `backup\installed-v1.5.6-20261023\`（md5 `2AEC292A`），v1.5.3 在 `backup\installed-v1.5.3-20261023\`（md5 `4A0C4CFB`）
  - 最新发布包：`release\夜雀点单助手-v1.6.7.zip`（85904 字节，4 个条目：安装说明.txt / update.ps1 / 更新.bat / BepInEx\plugins\MystiaOrderHelper-v1.6.7.dll）
  - 上传 GitHub Release 用 ASCII 名：`release\MystiaOrderHelper-v1.6.7.zip`（同一份内容，85904 字节）
  - 更早的发布包：`release\夜雀点单助手-v1.6.6.zip`（85467 字节）、`release\夜雀点单助手-v1.6.5.zip`（85192 字节）、`release\夜雀点单助手-v1.6.4.zip`（84654 字节）、`release\夜雀点单助手-v1.6.1.zip`（82992 字节）、`release\夜雀点单助手-v1.6.0.zip`（81164 字节）
  - 打包命令：`python tools\make-release.py`（`--check` 只校验三方版本一致 + 与装机版逐字节对比，不写文件）
- 历史版本存档（v1.5.4–v1.5.9 **均未出包**，只有 dist dll 与装机备份）：
  - v1.5.3 发布包：`release\夜雀点单助手-v1.5.3.zip`（71405 字节）
  - v1.5.4（版本提示）从未装进过游戏，已并入 v1.5.5；`dist\MystiaOrderHelper-v1.5.4.dll` 仅作历史
  - 稳定版 v1.5.2（md5 `8A5B554D`）已备份在 `backup\recon-2026-10-23\`，随时可拷回游戏目录回滚
  - v1.5.2 **已实测通过**：整局 171–179 帧、采集峰值 15.1ms（仅冷缓存一次）、稀客影像能进推荐框
  - v1.5.3 **未做整局性能复测**（仅确认单帧无长帧），见 K12
- 环境：Windows，游戏装在 `<游戏目录>`（Steam 库路径因人而异）
- 当前功能：①**未点单的客人不出现在面板/推荐框**（`_activeGuests` 同时过滤 `Served` 与 `NotOrderedYet`）
  ②官方素材包已导入 **88 个文件名 / 85 个角色**，仍有 8 格待认
  ③**防误触功能已放弃**（见下），`MouseGuard` 的 8×8 挡板仍保留但不再做游戏侧拦截
  ④**进游戏后弹一条版本提示**（v1.5.4）：屏幕上方居中小卡片，约 4 秒自动淡出，每次启动只弹一次，
     配置 `[Display] ShowVersionNotice` 可关掉
  ⑤**推荐算法计入加料**（v1.5.5 起，v1.6.0 改口径）：菜谱 tag + 加料食材 tag 一起算分，
     **加料目标固定 4 分**（单单食材就粉评，不再用酒分抵扣）；菜排序按命中 tag 数 → 利润率；
     酒排序按「配菜仍能粉评」→ 利润（售价）→ 库存瓶数；配置 `[Behaviour] ConsiderExtras` 可关掉
  ⑥**按客人预算配酒**（v1.6.1 起，v1.6.2 改口径）：读客人钱包。**不再均分**——还有单没上时这一单
     最多花他手上的现钱；只有最后一单允许小幅超支（本次到店总预算的 10%，夹在 30~300）。
     付不起的酒不推，付得起的先保粉评再比利润；界面标「价X 预算余Y」/「略超Z（余量内）」/「⚠超Z，可能黑评」；
     配置 `[Behaviour] RespectFund`
  ⑦**刷稀客好感**（v1.6.3 功能 / v1.6.4 补按钮）：`[Behaviour] FavorFarm = true` 时，对稀客改成「保粉评 + 省钱」——
     这一单上限 = 现钱 − 后面每单的最低粉评价（`CheapestPinkPrice`），同粉评挑便宜的酒，
     净分封顶 4 分后按价格排；目的是让她把 5 单吃完。状态行标「刷好感」。
     **开关位置**：主面板顶部「刷好感：开/关」+ 控制面板（F9）【推荐口径】区（还有「按预算推荐」开关）。
     v1.6.3 只能在 cfg 里改，玩家在游戏里找不到入口 —— 所以 v1.6.4 补了这两处 UI
  ⑧**推荐框与主面板同口径**（v1.6.5）：以前主面板按 `Pairs`（已按当前口径排好）显示，
     料理框/酒水框却各走一套旧顺序，切了「刷好感」只有主面板会变。现在：
     料理框的菜按主面板搭配的顺序前置（`OrderLikePairs`），酒水框把搭配里的酒提前
     （`MovePairDrinksFirst`），刷好感时其余粉评酒按价格升序；采集节拍从 `Time.time`
     改成 `Time.unscaledTime` —— 打开料理台/酒水柜时游戏会压 `timeScale=0`，
     旧计时一进柜子就停摆，推荐框会一直显示旧数据
  ⑨**特殊厨具也同步**（v1.6.6）：三位一体这类厨具的候选是 `RecipeIndex.Find` 另生成的一批，
     按引用比对永远认不出同一道菜 + 只取前 4 道会把主面板推荐的那道截掉；
     现在按菜谱 ID 认（`SameDish`/`FindSame`），并先让 `Pairs` 的菜占位再补其余
  ⑩**卡顿治理**（v1.6.7，见 K14）：全场扫图 `FindObjectsOfTypeAll<Sprite>` 从 12 秒一次
     改成 45 秒一次 + 整局最多 5 次（单次 200~290ms，是「偶尔顿一下」的真凶）；
     扫描预算检查粒度 64→8（单趟从 46ms 压到几毫秒）；`Take` 用 `SeenSprites` 去重，
     同一张图只封送一次名字；`RefreshIfNeeded` 与 `PrefetchTick` 同一次采集里不再各扫一遍；
     采集节拍 0.5s→0.8s，但切换开关 / 开关厨具面板走 `RequestRefresh()` 事件通道，仍然立刻生效
- **防误触为什么放弃**（2026-10-23 20:40 事故）：在 `OnPanelOpen` 加 `Prefix return false` 会跳过原方法，
  但 Harmony 的 **Postfix 照样执行**，于是对一个字段全是默认值的实例调了 `SetOpen()`；
  日志表现为 `cooker='Lorem ipsum is'`、`rect=(0,0,2560,1440)`，随后插件误判“面板已打开”，
  连锁触发暂停输入、禁止移动、`timeScale=0`，游戏卡死。修复方式是撤掉 Prefix 并把 `MouseGuard` 的死代码清掉。
  **教训：Prefix 拦下方法时，任何依赖该方法的 Postfix / 状态登记都必须一起跳过。**
- UI 批次进度与下一步：见 `docs\UI美化开发计划.md`（批0–批3.5 已完成并出包，批4 菜品/酒水图标待做，技术路径已查清）

---

## 1 现在能做什么（已交付）

**启动提示（v1.5.4）** —— 进游戏约 2 秒后，屏幕上方居中弹出「夜雀点单助手 v1.5.4」，
淡入 → 停留 → 淡出，约 4.5 秒后消失；只弹一次，不遮挡面板（面板默认在左上角）。

「只弹一次」= **每次启动游戏弹一次，之后整个进程内不再弹**：切场景、回标题、重开一天、反复开关面板都不会再来，
只有真正重开游戏才重新弹。实现是三个 static 开关（`_shown` 已放出去过 / `_finished` 已结束 / `_lastTickFrame` 同帧去重），
日志里一次启动应恰好出现一条「版本提示已显示」和一条「版本提示结束」。想让某个玩家彻底关掉，配置 `[Display] ShowVersionNotice = false`。

**加料推荐（v1.5.5，`ExtraPlanner.cs`）** —— 推荐不再只看菜谱自带 tag，也算加料的 tag

- 三条游戏规则决定了算法形状：①一锅**最多 5 个食材、含菜谱自带的**，所以加料槽 = `5 − 菜谱食材数`，
  菜谱本身要 5 个食材的菜加不了料；②**加料不改变售价**（售价只看菜品），加料只是多消耗食材 → 加得越多利润越低；
  ③加料的 tag 与菜品 tag **合并**参与评价，重复 tag 只算一次 → 一份食材只有带来**新**喜好标签才有价值
- 目标分 = `4 − 酒能拿的分`，**够分就停**，不多塞一份；只挑「净收益 > 0」的食材（带来厌恶 tag 会扣分）
- 同样能粉评时的排序：合计分 → 可做 → **利润（售价 − 菜谱食材成本 − 加料成本）** → **食材余量**
  （做完后最紧张那样还剩几份）→ 加料份数少的
- 主面板每个搭配多一行：`｜ 加料 猪肉、洋葱 ｜ 赚38 余12份`；推荐框明细行尾部追加 `加料：…(赚N)`
- **一键制作会把加料一起放进锅里**（`CookerPanelWatcher.SelectIngredients(recipe, extras)`）：
  有加料时跳过面板的「导入配方」通道（那条通道只认菜谱、会丢掉加料），改走直填 `selectedIngredients`
- 售价取 `Sellable.TrueValue`（取不到退 `BaseValue`），成本取 `Ingredient.TrueValue/BaseValue` 之和；
  首次算出一条会写日志 `加料核算样例：菜=… 售价=… 成本=… 利润=…`，**这是核对数值是否合理的唯一入口**
- 关掉：`[Behaviour] ConsiderExtras = false`（退回只看菜谱标签的旧行为）

**防黑暗料理（v1.5.7，`ExtraPlanner.WouldConflict` / `DropConflictingExtras`）** —— 加料不能只看加分，还得看会不会把菜做废

- **闸门 1（禁用 tag）**：加料**新引入**的 tag 只要落在菜品 `Sellable.BanTags` 里，这份食材直接不选。
  只看新引入的 tag —— 菜品本来就有的味道不算加料的锅
- **闸门 2（加成别的菜）**：游戏判定配方是「配方食材 ⊆ 锅里食材」，所以 `[A,B] + C` 若同时凑成配方 `D=[A,C]`，
  锅里会同时满足两道菜、面板让玩家择一，手快就做错。这里按「后加的先去」往回丢，直到当前菜是唯一匹配；
  **丢掉的料必须从 `used`（成本/余量）和命中数里一起回滚**（`BuildUsage` + `RecomputeHits`），否则利润按没丢算
- **刻意不看 `AdditiveTags`**：它是「加料之后这道菜**实际获得**的 tag」（运行时状态，基础数据上通常为空），
  **不是**「允许加的 tag 白名单」。拿它当白名单会把正常加料全砍掉。它只进诊断日志，用来核对语义
- 被挡住导致一份都没加成时，主面板第三行写 `｜ 加料会冲突，已跳过`，推荐框明细行同样标出，避免被误当成插件漏推荐
- `BanTags` 读取失败**只警告一次**（`WarnOnceBanTags`）：闸门悄悄失效比误杀更危险
- 关掉：`[Behaviour] AvoidDarkCooking = false`（退回 v1.5.6 的纯 tag 贪心）
- 实机核对入口：日志里 `加料冲突表样例：菜=… 菜品tag=[…] 禁用tag=[…] (AdditiveTags 仅参考=[…]) 挡住=N份`（整局最多 3 条），
  以及 `TagRules 样例：…`（游戏的 tag 规则表，本次没拿它做判定，先记录语义备用）
- **待实机确认**：`BanTags` 的语义是按字段名 + 数据结构推断的。如果日志里三条样例的「禁用tag」**全是空的**，
  说明这个字段没被游戏用来做加料判定，需要改用 `TagRules`；如果「禁用tag」里出现明显的正常 tag，
  说明语义理解错了。两种情况的处置都是先 `AvoidDarkCooking = false`，把日志发出来

**主面板（F8 开/关）** —— v1.5.3 起是「顶部头像排 + 下方展开一位」

- 顶部一排客人头像（稀客真立绘，常客名字首字），一行放不下自动换行
- 点头像切换，下方展开该客人的：`◆ 名字（稀客/常客） 桌位 X  剩N单`
- 展开内容：`点菜：…`、`点酒：…`、`[菜 + 酒 搭配]` 若干条，每条两行（需要加料时三行）：
  - `→ 菜名[厨具]＋酒名`（缺料/缺货会标出来）
  - `粉评 5分  差N分  ｜  净分 菜2 酒3`（净分已含加料贡献）
  - `｜ 加料 猪肉、洋葱 ｜ 赚38 余12份`（v1.5.5，只在真的要加料时出现）
  - `｜ 加料会冲突，已跳过`（v1.5.7，加料全被防黑暗料理闸门挡掉时出现，和上一行互斥）
- 菜与酒按「合计分」配对排序；**稀客只列能粉评（合计 ≥4）的组合**，常客不受此限
- 稀客凑不出粉评时显示 `[凑不出粉评 · 命中标签最多]`，下面分别给一条最佳菜 / 最佳酒水
- **还没点单的客人不进入这个列表**（也没有头像位）；客人离场后自动切到下一位在场者
- 顶部按钮：`控制面板（F9）`，旁边显示一键制作当前状态
- 皮肤：游戏自带订单小票纸（`[Display] VanillaSkin` 关掉退回灰盒）
- 面板可拖动；位置和大小可在控制面板里调，改完自动记住

**营业场景**

- 进入营业场景自动弹出 F8 面板（v0.4.3 起只在营业场景自动开，其它场景默认隐藏）

**厨具面板旁挂（v0.5.x 起）**

- 打开任意厨具面板（煮锅/烤架/油锅/蒸锅/料理台）自动在屏幕右侧弹出 `【料理】推荐（厨具名）`，暖色底
- 打开酒水/饮料面板自动弹出 `【酒水】推荐`，冷色底，位置在料理框右侧
- 两个框独立，不重叠
- **点击推荐项 = 直接开做**：走"直填食材"路线，把食材写进原生面板并触发重新匹配，面板里自动出成品

**其它**

- F9：打开 / 关闭「控制面板」：字体大小、常客开关、一键制作开关、三个窗口的布局、导出客人日志、恢复默认
- 评分：净分 = 命中喜好 TAG 数 − 命中厌恶 TAG 数；菜 + 酒合计 ≥4 且成对 → 粉评，3 → 橙评，2 → 绿评，1 → 紫评，≤0 → 黑评
- 推荐排序：合计分降序 → 食材/酒水是否齐备 → 原始契合分
- 桌位显示已修正（`Desk` 优先，回退 `CtrlDesk`）

---

**控制面板（F9，v1.1.0 新增）**

- 居中的设置窗口，可单独打开（主面板关着也能开）
- **打开即暂停游戏时间**（Time.timeScale=0，等效于按 Tab 暂停），关闭时恢复原值
- **右键任意位置关闭**（F9 再按一次也关）
- 打开期间禁用场景内全部 `EventSystem`（点击不会穿透到游戏 UI），关闭时恢复
- 打开期间对 `NightScene.Input.WorkScenePlayerInputGenerator` 调 `ForbidMovement()`，关闭时 `RecoverMovement()`，WASD 不再移动角色
- 打开期间禁用所有已启用的 InputSystem Action（`InputSystem.ListEnabledActions()`），关闭时恢复 —— 这才是挡住“点场景开厨具”的关键
- 字体大小：`－2` / `＋2`，范围 12–32，点完立刻生效
- 常客：显示 / 隐藏
- 一键制作：开启 / 关闭 —— 关闭后推荐框照常显示推荐，但点击不再放食材
- 导出客人信息到日志（原 F9 热键功能，现在只在这个按钮里）
- 恢复默认（字号 20、常客隐藏、一键制作开）
- 配置项：`[Display] FontSize`、`[Display] ShowNormalGuests`、`[Behaviour] EnableOneClickCook`；`[General] ToggleKey=F8`、`SettingsKey=F9`；`DumpKey` 已废弃，仅为兼容旧配置保留

---

## 2 文件与职责

```
<项目目录>\
├─ README.md                项目入口（功能说明、安装、构建、目录结构）
├─ LICENSE                  MIT
├─ build.ps1                编译（可 -Install 直接装）
├─ docs\                    全部文档与资料
│   ├─ 接手文档.md          **新接手先读这个**：30 分钟上手、五条铁律、别碰清单、回滚路径
│   ├─ HANDOFF.md           本文档（现状 + API 知识 + 版本轨迹，文中路径均相对项目根）
│   ├─ MOD制作经验文档.md    踩坑全集 + 构建/发布流程（续作前必读）
│   ├─ 项目报告.md           需求/技术方案/验收（R2 版，含变更记录见 §12）
│   ├─ M0-侦察结果.md        IL2CPP 类型侦察原始结论
│   ├─ UI侦察报告.md         UI 素材侦察：订单卡结构、sprite 命名、导出样例（批0 产出）
│   ├─ UI美化开发计划.md     UI 批次的目标 / 进度 / 下一步（批4 待做的路线与成本）
│   ├─ probe-session-0.4.2.log  面板结构探测日志（收藏 8 道菜的来源）
│   └─ reference\            外部参考资料（另一个游戏的 MOD 教学包，与本插件无关）
│       └─ ui-samples\      UI 侦察导出的素材样例图 + 完整 dump
├─ src\OrderHelper\         全部源码（dll 约 116 KB）
│   ├─ OrderHelperPlugin.cs     BepInPlugin 入口、配置项、宿主 GameObject、组件注入
│   ├─ OverlayBehaviour.cs      IMGUI 主面板 + 两个旁挂推荐框 + 拖动/字体/分割线/布局设置
│   ├─ GuestCollector.cs        每帧收集客人、桌位、菜品/酒水候选与评分、离店标记
│   ├─ RecipeIndex.cs           食谱索引、解锁过滤、标签匹配、静态数据缓存、备料可用性
│   ├─ UiSkin.cs                皮肤：抓游戏素材 → 九宫格纸条底 + 按钮/滚动条/文字配色（v1.5.3）
│   ├─ GuestPortraits.cs        头像：抓稀客立绘 → 拷成自己的贴图并按名字缓存 + 人工映射表（v1.5.3）
│   ├─ MouseGuard.cs            点击防误触的 uGUI 挡板（8×8 跟随鼠标，v1.5.3；游戏侧拦截已移除）
│   ├─ VersionToast.cs          进游戏后的版本提示（小卡片，自动淡出，v1.5.4）
│   ├─ UiProbe.cs               UI 侦察：扫全场 Image/Sprite 并导出样例（默认关，F10 手动）
│   ├─ CookerPanelWatcher.cs    厨具面板开关检测、食材直填与点击、按钮映射、调试转储
│   ├─ BeveragePanelWatcher.cs  酒水面板开关检测与推荐渲染
│   ├─ PanelClicker.cs          调用游戏按钮回调 / 模拟点击的公共工具
│   ├─ Probes.cs                Harmony 补丁集中注册（新增补丁必须在这里登记！）
│   ├─ Perf.cs                  性能探针：长帧记录 + 每 30 秒耗时汇总
│   └─ Diagnostics.cs           客人快照日志、控制面板里的“导出客人信息”
├─ tools\                   独立小工具（都可 python 直接跑，自带 --selftest）
│   ├─ watch-log.py         日志哨兵：只读新增行、半行缓冲、轮转检测
│   ├─ perf-report.py       「性能30s」窗口表 + 长帧归因
│   └─ make-release.py      打包：生成安装说明 + zip 到 release\
├─ scripts\                 开发辅助（本机用，不发布）
│   ├─ game.ps1             启停/重启/状态：start | stop | restart | status
│   ├─ dev.ps1              构建并装机（无 -Force 时若游戏在跑则拒绝）
│   └─ start.bat / stop.bat / dev.bat
├─ packaging\               发布包素材（会被打进 zip，解压到游戏根目录）
│   ├─ update.ps1           检查 GitHub Releases 并替换 dll
│   └─ 更新.bat             双击入口，转调 update.ps1
├─ release\                 所有发布过的 zip（MystiaOrderHelper-vX.Y.Z.zip 供 Release 上传）
│   └─ unpacked\            早期版本的解压目录
├─ dist\                    编译产物（当前版本在根，历史版本进 old\）
└─ backup\                  旧插件与旧版本备份、一次性脚本归档（scratch-*）
```

---

## 3 构建与安装（照抄即可）

### 3.1 编译

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "<项目目录>\build.ps1"
```

产物：`dist\MystiaOrderHelper-v1.5.3.dll`（约 133 KB）。

注意事项（都是踩过的坑）：

1. 本机 **不能用 MSBuild/dotnet build**：NuGet 路径（`%APPDATA%\NuGet`、`D:\temp`、`.templateengine`）被拒。只能直接调 `csc.dll`。
2. 编译器：`<便携 .NET 目录>\sdk\6.0.428\Roslyn\bincore\csc.dll`，用同目录 `dotnet.exe exec` 跑。
3. **csc 不能直接写 E:**，所以 `build.ps1` 先编到 `...\.tmp\orderhelper\`，再拷进 `dist\`。
4. 直接 `& build.ps1` 会被 PowerShell 执行策略拒绝（UnauthorizedAccess），必须包 `powershell.exe -ExecutionPolicy Bypass -File`。
5. 源码必须是 **UTF-8**，csc 参数带 `-codepage:65001`；改 `build.ps1` 本身时注意 PowerShell 5.1 按 GBK 读无 BOM 脚本，脚本里别放中文。

### 3.2 安装（游戏必须先关，dll 会被锁）

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "<项目目录>\scripts\game.ps1" stop
# 等进程真的退出
# 旧 dll 先归档（别直接删），再放新 dll
New-Item -ItemType Directory -Force "<项目目录>\backup\old-plugins" | Out-Null
Get-ChildItem "<游戏目录>\BepInEx\plugins" -Filter "MystiaOrderHelper-v*.dll" |
    Where-Object { $_.Name -ne "MystiaOrderHelper-v1.5.3.dll" } |
    Move-Item -Destination "<项目目录>\backup\old-plugins" -Force
Copy-Item "<项目目录>\dist\MystiaOrderHelper-v1.5.3.dll" "<游戏目录>\BepInEx\plugins\" -Force
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "<项目目录>\scripts\game.ps1" start
```

验证加载：

```powershell
Select-String -Path "<游戏目录>\BepInEx\LogOutput.log" -Pattern "夜雀点单助手 v1\.5\.3" -Encoding UTF8 | Select-Object -Last 1
```

期望看到：`夜雀点单助手 v1.5.3 已加载（F8 主面板，F9 控制面板）`。

> 版本号要同时改两处：`src\OrderHelper\OrderHelperPlugin.cs` 的 `Version` 和 `build.ps1` 的 `$version`。

---

## 4 运行时 / 游戏 API 知识（最重要的部分）

### 4.1 BepInEx / IL2CPP

- BepInEx 6 BE（6.0.0-be.785）+ Il2CppInterop；插件类型必须 `ClassInjector.RegisterTypeInIl2Cpp`，否则挂不上。
- 宿主 GameObject 在**每次场景切换都会重建**，靠 `SceneManager.sceneLoaded` 重建并重挂组件。
- 启动早期 `DontDestroyOnLoad` 用不了，别依赖它。

### 4.2 IMGUI 踩坑

- `GUILayout.Window` 在本作 IL2CPP 下直接抛 `MissingMethodException: LayoutedWindow..ctor` → 一律用 `GUI.Box` + `GUILayout.BeginArea` + 自己处理拖动（`Event.current`）。
- `Texture2D.whiteTexture` 在 IL2CPP 下画不出 → 自建 `new Texture2D(1,1)` + `SetPixel` + `Apply`。
- 字体：`TMP_Settings.defaultFontAsset.sourceFontFile`（本作是 JYHPHZ）→ `Font.CreateDynamicFontFromOSFont`，失败回退微软雅黑；字号走配置。
- **给控件设固定 `Height` 后，内容超出是静默裁剪**（v1.5.3 踩到：两行文本塞进 44px 按钮 → "稀客名字显示一半"）。拆成独立文本、不设固定高度、开 `wordWrap`。
- **IMGUI 不缩放 `GUIContent` 里的图片**：64×64 立绘塞进 48px 按钮会溢出去盖住文字 → 拷贝贴图时就缩到显示尺寸。

### 4.2.1 从游戏里取 UI 素材（v1.5.3 新增）

- 素材零打包，全部运行时从场景 `Image` 抓，按 sprite 名认：`OrderReceipt_BG_0/1`（面板纸条底，九宫格）、`CharBG`/`CharFrame`（头像底衬与描边）、`角色名Main_0, 1`（稀客立绘）。
- 图集贴图关了 Read/Write → `Graphics.Blit` 到 `RenderTexture` 拿可读副本，再按 `textureRect` 切片；**同一图集的整图副本按实例 ID 缓存**，只 Blit 一次。
- 拷贝出来的贴图是自己持有的 `Texture2D`（`HideFlags.HideAndDontSave`），**跨场景不失效，不用挂 `OnSceneLoaded`**，也绕开了图集 UV 翻转。
- 降级开关 `[Display] VanillaSkin`（默认开），关掉即完全回到 v1.5.2 的灰盒外观。
- 侦察开关 `[Diagnostics] UiProbe` **默认关**（会全场景扫一遍并写文件），改 UI 前临时开或按 F10 手动跑。
- 详见 `docs\UI侦察报告.md`（素材清单）与经验文档 §4.5（坑清单）。

**头像认不出来怎么办（不用改代码）**，优先级从高到低：

1. **自己放图（最稳）**：头像截图（PNG/JPG，文件名=面板显示名）丢进 `config\orderhelper-portraits\`，
   任意尺寸会自动裁方缩到 48×48；外面盖过一切自动识别。目录里有自动生成的 `说明.txt`。
   **名字不必一字不差**：先精确、再繁简归一、再双向包含（`咲夜.png` 命中 `十六夜咲夜`，`茨木华扇.png` 命中 `华扇`）。
2. `config\orderhelper-portraits.txt` 里加一行 `内部名 = 显示名`（如 `華扇 = 茨木华扇`），存盘重开生效。
3. 查内部名：游戏里按 **F11** 导出 `config\orderhelper-portraits-keys.txt`（标 `[有]/[缺]`），
   或按 F10 导出带内部名的原图。

**官方素材包批处理（`tools\`）**：游戏根目录自带 `东方夜雀食堂 头像&表情包 ~ Avatar Image`（本体 38 组 + DLC1-5 +
DLCMUSIC，每组多个表情，文件名只有编号、没有任何角色名）。
`tools\命名表.txt` 一行一条 `目录/文件名 = 面板显示名`（值写 `?` 表示待定），
`python tools\import_portraits.py` 会把确定的那些抠背景（四角泛洪）、裁到内容外框、补方形、缩到 128，
写进 `config\orderhelper-portraits\`；`--dry` 只演练。
`python tools\make_todo_sheets.py` 生成 `_todo_main.png` / `_todo_rest.png`，把还没定的条目连编号和猜测排成图，照着填表即可。
`python tools\preview_portraits.py` 生成 `_preview_rc.png`，一眼看导出的 88 张是否切头、残边。
**当前：已定 88 个文件名 / 85 个角色，待认 8 格**（编号见 `_todo_main.png` / `_todo_rest.png`）。

### 4.3 Harmony 补丁

- 所有补丁在 `Probes.cs` 的 `Install()` 里 `TryPatch`。**新增补丁类不登记就完全静默**（0.5.0 就是这样白测一轮）。
- 面板元素登记方法（`OnRecipeElementEnabled` / `OnIngElementEnabled`）**比 `OnPanelOpen` 早**，所以不要把"清空元素表"放在 `SetOpen` 里，否则永远拿不到按钮。

### 4.4 厨具面板（WorkSceneCookingSelectionPannel）

结构（`WorkSceneCookingSelectionModule(Clone)`）：

```
BG/Ingredients/Content/Objects   → 食材 ItemStackElement（带 Count#）
BG/Viewport/Recipes              → GridLayoutGroup，收藏菜谱格（通常只有 8 个）
BG/Selected/...                  → 已选食材格 ItemElement
BG/Output/...                    → 成品格 OutputElement
BG/SellableDescriberWindow_...   → 右侧详情窗（名字/描述），不是格子
```

关键结论：

- 菜谱格**只有收藏的 8 道**（实测：大江户船祭、海胆刺身、蔬菜专辑、蜜饯栗子、刺身拼盘、分子蛋、无意识妖怪慕斯、毛玉三色冰激凌）。稀客想吃的大多不在里面 → 靠"点菜谱格"必然失败。
- 面板打开后再改 `importedRecipe` / `hasImported` 无效（实测 `选中食材=0 可制作组合=0`）。
- **有效路线：直填食材**（v0.7.7 起）：
  1. `panel.selectedIngredients`（实测 `List<int>`，食材 ID）写入本菜所需食材
  2. 重建 `panel.m_SelectedInstances`（食材格）
  3. `panel.MatchingSelected(panel.m_OutputInstances, out bool haveUnObtained)` 重新匹配
  4. 依次刷新选中区 / 产出区 / 菜谱区 / 整体视觉
  - 实测日志：`直填食材 food=26 写入=4 选中=4 可制作组合=3 未解锁组合=False` → 面板里直接出成品。
- 点击后日志关键字：`直填食材`、`导入配方`、`点击元素`、`点击未找到元素`、`模拟点击`、`派发点击`。

### 4.5 酒水面板（WorkSceneStoragePannel）

- `PannelOpenContext.OpenType == SellableType.Drink` 即为酒水页。
- 有和厨具面板同构的 `OnIngElementEnabled/Selected`、`selectedIngredients`、`m_SelectedInstances` 等成员。
- v1.3.1 起点击已可用：`panel.Extract(sellable)` 按 Sellable 对象取酒，不走格子索引。

### 4.6 数据来源

- 食谱：`DataBaseCore.Recipes` + `RunTimeStorage.Recipes` 判定解锁（`RunTimePlayerData.CheckRecipeIsLocked`）。
- 菜品/酒水/食材：`DataBaseCore.Foods / Beverages / Ingredients`，运行时库存 `RunTimeStorage.Beverages`。
- 稀客：`SpecialGuestsController.SpecialGuest`，需求 `RequestFoodTag` / `RequestBeverageTag`，喜好 `LikeFoodTag / HateFoodTag / LikeBevTag / HateBevTag`。
  - **负数不是标签 id，而是"这张单没有这一项"**（v1.4.9 查清，日志实证）：`foodRequest = -1` 且 `order.DeskCode = -1` = 还没点单；已点单时 `beverageRequest = -1` = 酒水不限。匹配时 -1 一律不参与 tag 比较，酒水改为按喜好推荐任意有货酒水。
  - 厌恶/喜好集合是 `Il2CppSystem.Collections.Generic.IEnumerable<int>`，**不能直接强转 `System.Collections.IEnumerable`**（会抛 InvalidCastException 且每 0.5s 刷一条）；正确做法是 `TryCast<List<int>>` → `TryCast<Il2CppStructArray<int>>` → `GetEnumerator()` + `Cast<Il2CppSystem.Collections.IEnumerator>().MoveNext()` 三级兜底。
- 常客：`OrderBase.foodRequest` 是菜品 ID、`beverageRequest` 是酒水 ID。
- 厨具：`Recipe.CookerType`（Pot/Grill/Fryer/Steamer/CuttingBoard/Empty），中文名映射：煮锅/烤架/油锅/蒸锅/料理台。

---

## 5 已知问题 / 遗留

| 编号 | 内容 | 影响 |
|---|---|---|
| K1 | 菜谱格注入（`m_RecipeInstances` + `UpdateRecipeField`）未生效 | 已被「直填食材」替代，不再尝试 |
| K2 | ~~酒水面板点击未实现/未验证~~ v1.3.1 已修（`Extract(sellable)` 按对象取酒） | 已关闭 |
| K3 | ~~稀客 tag 负值语义不明~~ v1.4.9 查清：`-1` = 这张单没有这一项（未点单 / 酒水不限） | 已关闭 |
| K4 | 字号 ± 后容器不跟着重排（v1.4.9 试过「按字号重算宽高」，会覆盖用户自定义宽高，已回退） | **打开**：字号 >24 时建议手动加宽窗口 |
| K5 | ResourceEx 自定义菜（测试用 2000000001）未解锁 | 仅影响测试包，不影响正式 |
| K6 | ~~推荐框位置固定右对齐~~ v1.4.9 已改为可拖动 + 控制面板可调 + 配置记忆（三个窗口通用） | 已关闭 |
| K7 | 每帧 `FindObjectsOfType` 收集客人（0.5s 一次全量扫描） | 低配机可能有开销，未优化 |
| K8 | **点击面板仍可能误开背后的厨具/酒水面板**（防误触已放弃，见 K11） | 已知限制，不再尝试修 |
| K9 | 批4 菜品/酒水图标未做 | 路径已查清（`GetSellabeBGSprite(Sellable)→Sprite` + 主图集），待确认归属类，见 `UI美化开发计划.md` |
| K10 | UI 小瑕疵：滚动条纸片色不够精致、按钮条偏深、常客头像是首字占位 | 留给批5，不影响使用 |
| K11 | ~~点击防误触只挡 uGUI~~ **功能已放弃**（2026-10-23 事故，见头部「防误触为什么放弃」） | `MouseGuard` 的 8×8 挡板仍在（对真 uGUI 控件有效），但**没有任何游戏侧拦截**。不要再尝试在 `OnPanelOpen` 上 Prefix |
| K12 | ~~卡顿未做整局采样~~ **已采样并已优化（v1.5.6）**：上一局日志实测采集均 3.6–5.6ms/峰 89ms（每 0.5 秒）、巡查均 8–26ms/峰 64ms（每 5 秒）、绘制均 0.1ms。已改成客人数据指纹缓存 + 头像分片扫描 + 巡查默认关 | 复测看 `[Diagnostics] PerfLog` 的「采集」一栏：均 <1ms、峰 <10ms 就算达标；「巡查」一栏应该整行消失（默认关）。若采集仍偏高，下一刀是「事件驱动收集」——挂 `OnGuestSpawn` + 点单补丁，彻底去掉 0.5 秒轮询 |
| K13 | 头像仍有 8 格未认（`本体/06-01`、`本体/27-01` + DLC3/003/075/087 + DLC5/03-01/08-01/10-01） | 不影响使用（退回实时抓取的立绘）；补齐方式见 HANDOFF §4.2.1 |
| K14 | ~~用户反馈「游戏有些卡顿」~~ **已定位并优化（v1.6.7）**：日志证据是 `帧率30s` 99.5% 帧 ≥60fps（不是持续低帧）+ `最慢三次` 里 `头像·全量 287.9ms/196.7ms`（`FindObjectsOfTypeAll<Sprite>`，原生一次性调用，12 秒一次）。已改：深扫 45 秒一次 + 整局 5 次上限、预算检查 64→8、`SeenSprites` 去重、两个扫描入口互斥、采集节拍 0.8s + `RequestRefresh` 事件通道 | 复测看 `长帧` 条数与 `头像·全量` 单次耗时。**若 `采集·枚举` 仍有孤立 200ms+ 尖峰**，那是 il2cpp 元数据初始化或 GC，与本次改动无关；下一步可用 `GC.CollectionCount` 埋点确认，或把枚举结果按控制器指针缓存成静态表（避免每次走 `AllPresentedGuestGroupController` 的数组封送） |

---

## 6 下次继续的建议顺序

1. 已完成：~~酒水直填~~(v1.3.1)、~~推荐框位置记忆~~(v1.4.9)、~~包内附更新脚本~~(v1.5.0)、
   ~~UI 美化三批~~(v1.5.3)、~~官方素材包头像~~(v1.5.3，85 个角色)、~~未点单过滤~~(v1.5.3)。
2. **等用户整局复测 v1.6.7**（当前唯一阻塞项）：
   - **按钮在不在**：主面板顶部应有一枚「刷好感：关」，点一下变成「刷好感：开」；
     控制面板（F9）【推荐框】下面应有【推荐口径】区，里面「按预算推荐」「刷稀客好感」两个开关 + 两行说明；
   - **推荐框同步（v1.6.5/v1.6.6）**：切开关后打开料理台 / 酒水柜，侧边推荐框的头几项应与主面板一致
     （菜同一批、酒同一杯）；**在柜子界面里**点主面板开关也要能刷新（靠 `unscaledTime` 采集）；
     **特殊厨具（三位一体）也要验**：它的候选走另一条计算路径，v1.6.5 漏了，v1.6.6 已修；
   - **刷好感是否生效**：开关打开、客人是稀客时日志应出现 `刷好感预算：… 每单最低粉评=… 预留=… 本单上限=…`；
     状态行应标「刷好感」；没有这条说明 `RespectFund` 被关了、或客人不是稀客、或钱读不到；
   - **预算读没读到**：日志 `预算样例：客人=X … 资金=… 总预算=… 剩单=… 本单上限=…`，
     如果整局一条都没有，说明 `GetFund` 读不到，功能等于没生效；
   - **资金语义**：日志 `预算变化：客人=X 资金 a→b` 出现 = `GetFund` 是「剩余」（口径正确）；
     **一条都不出现** = 它是「初始额度」，那 `cap` 会一直等于总预算、预算拦不住累计超支，
     需要改成自己记账（按剩单数递减累加已花金额）——这是本功能最可能的返工点；
   - **最后一单余量**：`剩单=1` 时 `本单上限` 应比 `资金` 大一点点（总预算的 10%，30~300）；
   - **有没有降档**：日志 `预算降档：… 原本会选=A(x) 改选=B(y) 本单上限=…`，出现即功能生效；
   - **吃不吃得满 5 单**：这是刷好感的最终判据——看她是不是真的坐满 5 单才走；
   - 界面：普通单只有「价X 预算余Y」；最后一单花掉余量写「略超Z（余量内）」；
     超过上限才写「⚠超Z，可能黑评」。也顺手确认「价X」和游戏实收接近（差很多说明售价字段该换）；
   - **加料是否真的进锅**（v1.6.4 已实测确认能加）：日志 `直填食材 … 加料=N 游戏接受的加料=M`，M > 0 即成功；
   - 加料核算：`加料核算样例` 应出现「有加料」版本，看售价/成本/净分是否合理；
   - 排序：菜是否按命中 tag 数降序、同数才比利润；酒是否优先「配菜能粉评」再比利润/库存；
   - 外观：纸条皮肤、头像认人（华扇、因幡帝、萃香、橙）、未点单不显示（v1.5.3 遗留）；
   - **性能（v1.6.7 本次重点）**：整局后看两处——
     ① `最慢三次` 里 `头像·全量` 单次应不再 200ms+（新做了 `SeenSprites` 去重 + 45 秒间隔 + 5 次上限）；
     ② `长帧` 条数应从 9 条/5 分钟降到个位数以下甚至归零；
     ③ `采集` 次数 30 秒约 37 次（节拍 0.8s），`头像·扫描` 均值应从 46ms 掉到个位数；
     ④ 但要注意：**如果 `采集·枚举` 仍然出现 200ms+ 的孤立尖峰**，那是 il2cpp 元数据/GC，不是这次改的部分，
     需要另开一刀（目前判断影响很小，一次而已）。
3. ~~发布到 GitHub~~ **已完成（2026-10-24 22:40，v1.6.7）**：
   仓库 `xinyoudayu/mystia-order-helper`（Public），main 已推到 `d84ba62`，tag `v1.6.7`，
   Release：`https://github.com/xinyoudayu/mystia-order-helper/releases/tag/v1.6.7`
   （asset `MystiaOrderHelper-v1.6.7.zip`，85904 字节，`releases/latest` 已指向它，
   下载链接实测 HTTP 200）。v1.5.0–v1.6.6 作为**累积更新写进这一个 Release 的说明**，没有单独建 tag。
   **下次发版的四步**（照抄即可）：
   1. 改三处版本号（`OrderHelperPlugin.Version` / `build.ps1` / `make-release.py NOTES`）；
   2. `build.ps1 -Install` 装机 + 备份旧版；`python tools\make-release.py` 出包；
   3. `git add -A && git commit -F - <<'EOF' … EOF`，`git tag -a vX.Y.Z -m "…"`，`git push origin main && git push origin vX.Y.Z`；
   4. Release 用 API 建（本机没有 `gh`，走 `git credential fill` 拿 token + `api.github.com`）：
      先 `POST /repos/{owner}/{repo}/releases`（`tag_name` / `name` / `body` / `make_latest:true`），
      再 `POST uploads.github.com/repos/{owner}/{repo}/releases/{id}/assets?name=MystiaOrderHelper-vX.Y.Z.zip`。
   **资产名必须是 ASCII**（`update.ps1` 按 `MystiaOrderHelper-v*` 匹配），`draft` / `prerelease` 必须为 false，
   否则 `releases/latest` 不认、老用户点 `更新.bat` 会显示"已是最新"。
   > 注：发 Release 属于"改变远端可见状态"，动手前先跟用户确认。
4. 性能 K7：改为事件驱动收集（客人入座/离店补丁 + 点单补丁），去掉 0.8s 一次的轮询（v1.6.7 已把「开关类操作」改成事件，这里要改的是「客人到店/点单」）。
5. 字号 K4：字号变化后窗口自适应（注意不能覆盖用户自定义宽高，需要「手动改过就锁住」的标记）。
6. 交互：推荐列表支持键盘上下选择；窗口最小化。
7. 批4（菜品/酒水图标）：`GetSellabeBGSprite_Public_Static_Sprite_Sellable_0` 传 `Sellable` 返回 `Sprite`，
   主图集 `IngredientItemBadgeBeverageFoodCookerIzakayaSpriteAtlas`，存在 `Dictionary<int,Sprite>`；
   **待确认该方法归属哪个类**（需 dnSpy/ILSpy 反编译 Assembly-CSharp）。成本约 1–1.5 天。
8. 批5（收尾）：滚动条质感、按钮条配色、常客占位头像（K10）。
9. 坑（2026-10-22 修，仍然有效）：`update.ps1` 原来在 `-Repo` 为空时用 `& git ...` 推导仓库名，
   玩家机器没装 git 会因 `$ErrorActionPreference='Stop'` 直接崩；已改为 `$Repo` 默认写死
   `xinyoudayu/mystia-order-helper`，git 推导降级为可选兜底。

---

## 7 15 分钟快速上手自测

1. `game.ps1 start`，进游戏 → 进营业场景
2. 按 F8 应出现主面板：**纸片底 + 顶部一排头像**，下面展开一位客人的详情；
   点另一个头像应切换、文字跟着换；按 F9 出现居中的控制面板（字体/常客/一键制作）
3. 客人还没下单时，面板里**不该**出现这位客人；下单后应自动出现
4. 打开一个厨具（如煮锅）→ 屏幕右侧出现纸片色 `【料理】推荐（煮锅）`，顶部有头像排
5. 点其中一道菜 → `LogOutput.log` 出现 `直填食材 … 选中>0 可制作组合>0`，面板里出成品
6. 打开酒水页 → 右侧出现 `【酒水】推荐`
7. 点推荐菜若无效，先看日志是 `点击未找到元素`（格子不存在，4.4 已解释）还是 `模拟点击` 失败
8. 外观退回检查：把 `[Display] VanillaSkin` 改 false 重启，应回到灰盒外观（用来判断问题是不是皮肤引入的）

---

## 8 版本轨迹（本阶段）

| 版本 | 关键变化 | 教训 |
|---|---|---|
| **v1.6.7** | **修卡顿**（用户反馈「游戏有些卡顿」）。诊断手段：插件自带 `Perf` 探针，日志直接指认——`帧率30s` 显示 99.5% 帧都 ≥60fps（不是持续低帧），`长帧` 里 `上帧最慢=采集 411.9ms`，`最慢三次` 显示 `头像·全量 287.9ms / 196.7ms`。四处改动：①`DeepScanInterval` 12s→45s + `MaxDeepRuns=5`（`FindObjectsOfTypeAll<Sprite>` 是一次性原生调用、中途停不下来，实测 177~288ms，12 秒来一发就是固定周期顿挫）；②`BudgetCheckEvery` 8（原为 `(i & 63) == 0`，而 `_imgCursor` 是 `i+1`，位与对齐会错位，某些切片永远撞不上检查点 → 3ms 预算形同虚设，实测单次扫描均 46ms 峰 291ms）；③`Take` 加 `SeenSprites`（`HashSet<IntPtr>`）：图集切片被成百上千个 Image 共享引用，而 `sprite.name` 每次都要跨 il2cpp 封送字符串，不去重等于把最贵的操作做几十遍；④`PrefetchTick` 加 `now - _lastBuildAt < 0.4f` 互斥，两个入口不再同一拍各扫一遍；⑤采集节拍 `0.5f`→`CollectInterval=0.8f`，同时新增 `OverlayBehaviour.RequestRefresh()`，由 `InvalidateCache` 与 `CookerPanelWatcher.SetOpen/SetClosed` 触发，**把「轮询」和「事件」分开**。另外 `views.RemoveAll(v => …)` 换成静态 `Predicate` 委托（常驻节拍不该每拍 new lambda） | **先看探针再改代码**：这次没有猜，`长帧` 行的「上帧最慢=」直接给出了 `采集 411.9ms`，`最慢三次` 再拆到 `头像·全量`——没有这两个字段就只能在 OnGUI 里瞎猜。**「有预算」不等于「预算生效」**：`ScanImages` 一直带着 3ms 预算，但检查粒度是 64 张一批，一批就能吃掉 40ms，等于预算只是个摆设；凡是「按时间预算切片」的循环，检查间隔必须远小于预算本身。**同一个 tick 里有多个扫描入口，必须互斥**：`RefreshIfNeeded` 和 `PrefetchTick` 都调 `BuildPass`，谁也没错，叠加起来就是双倍开销。**降频 + 事件通道 > 单纯提频**：把轮询从 0.5s 降到 0.8s 会牺牲响应性，但把「玩家操作」这一类改成事件触发后，响应性反而比原来更好（点开关是下一帧就变，不用等 0.5s） |
| **v1.6.6** | **特殊厨具（三位一体）的推荐框也同步**（用户实测：普通厨具和酒水柜都好了，三位一体还是不能直接切换）。①`SameDish(a,b)`：优先比 `Recipe.FoodID`，没菜谱信息才比名字——特殊厨具的候选是 `RecipeIndex.Find` 另生成的一批实例，按引用比永远不相等；②`FindSame(list, want)`：返回列表里真正那个实例（`Plan`/评分都在它身上），`OrderLikePairs` 全部改用它；③`MatchedFor` 特殊厨具分支改两段：先按 `guest.Pairs` 顺序挑出池子里对应菜占位（`Supports` + `FindSame` 判重），再用其余菜补满 4 道——原来是「池子顺序取前 4」，主面板推荐的那道排在后面就被截掉了 | **跨来源的对象比较必须用业务主键**：`RecipeIndex.Find` 每次新建候选，`List.Contains`（引用比较）永远 false，而且不报错、不抛异常，只是功能静默不生效——这类 bug 只能靠「两条路径的差异」去推理，日志里看不出来。**「只取前 N」要在挑完重点之后**：先截断再筛选会把最该显示的候选挤出去 |
| **v1.6.5** | **推荐框与主面板同口径**（用户反馈：切了刷好感，主面板推荐变了，料理台/酒水柜旁边的推荐框纹丝不动）。①`OverlayBehaviour.OrderLikePairs`：`MatchesFor` 建缓存时把 `guest.Pairs` 里出现过的菜按主面板顺序前置，其余保持原序；②`GuestCollector.MovePairDrinksFirst`：酒水框把 `Pairs` 里的 `Drink` 提前，`FavorOn` 时其余粉评酒按 `PriceOf` 升序；③采集节拍 `Time.time` → `Time.unscaledTime`（打开料理台/酒水柜时游戏 `timeScale=0`，旧计时在柜子里完全停摆，推荐框永远显示进柜子那一刻的旧数据） | **同一个功能有多个显示面时，必须确认它们读的是同一份「已排序结果」**：主面板读 `Pairs`（按口径排好），推荐框读 `Candidates`/`HintDrinks`（各自旧顺序），三处各自维护排序就必然漂移；省事的做法是让次一级的显示面去找「主结果」的顺序，而不是重写一遍排序规则。**节流用 `Time.time` 是有条件的**：只要游戏会在某个界面把 `timeScale` 压到 0，那个界面上所有基于 `Time.time` 的刷新都会静默停摆——数据刷新一律用 `unscaledTime`，动画/冷却才用 `Time.time` |
| **v1.6.4** | **给「刷稀客好感」补 UI 入口**（v1.6.3 只留了 cfg 项，玩家在游戏里找不到）。①`OrderHelperPlugin` 新增 `SetFavorFarm(bool)` / `SetRespectFund(bool)`（改值 + `SaveConfig`，与既有的 `SetOneClickCook` 同款）；②主面板顶部按钮行在「一键制作：开」后面加「刷好感：开/关」，营业中一点即切（`FavorLabel()` 只返回字符串**字面量**，避免 OnGUI 每帧拼串）；③控制面板新增【推荐口径】区：「按预算推荐」「刷稀客好感」两个开关 + 两行说明，其中「刷好感开着但预算关着」时显示 `★ 现在「按预算推荐」是关的，刷好感不会起作用`；④「恢复默认」一并复位这两项（预算开、刷好感关）。**算法一行没改**——纯入口补齐 | **配置项不等于功能可用**：v1.6.3 的算法正确、装机也对，玩家却「没看到按钮」——因为入口只存在于 `BepInEx\config\*.cfg` 里，而玩家不会为了切一个开关退出游戏改文本。**判断一个功能是否真的交付，要看它在游戏里有没有手能点到的地方**，cfg 只是持久化，不是 UI。**新增玩家常用开关时，默认给两处**：一处随手点（主面板）、一处集中管理（F9 控制面板），因为主面板可能被拖到屏幕外或隐藏 |
| **v1.6.3** | **刷稀客好感开关**（`[Behaviour] FavorFarm`，默认关）。开关打开且是稀客时：①新增 `FavorOn(view)`（稀客 + 配钱读得到 + 配置开）；②新增 `CapForOrder`：这一单上限 = 现钱 − `CheapestPinkPrice` ×（剩单 − 1），并按「至少还能粉评」兜底，算出的上限写回 `view.BudgetCap` 让推荐框过滤与按钮标记同步；③`CheapestPinkPrice`：遍历可做的菜 × 粉评酒，取最便宜的「菜+酒 ≥ 4 分」组合；④`PickDrink` 增加 `cap`/`favor` 入参，刷好感时同样粉评**挑更便宜的酒**（普通模式仍是挑更赚的）；⑤`BuildPairs` 排序：净分**封顶 4 分**（粉评线以上不再加分），同分先看是否超上限、再按价格升序，最后才比利润；⑥状态行加「刷好感」；⑦日志 `刷好感预算：… 每单最低粉评=… 预留=… 本单上限=…`（最多 8 条） | **「刷好感」和「赚钱」是相反的目标**：同一套排序不可能同时最优——所以做成显式开关而不是自动判断；**给后面留钱要按「最低可接受组合」留，不能按均价留**：菜价差很大（300~900），均分会让贵的那单做不出粉评，按最低粉评留则「保底 + 余额随便花」；**净分在粉评线以上没有收益**：4 分就够粉评，再高的分不会多给好感却通常更贵，所以刷好感时把净分封顶到 4 再比价 |
| **v1.6.2** | **预算不再均分**。v1.6.1 的 `剩余钱 ÷ 剩余单数` 把第一单压得太死（预算 1000 只敢花 500），本来吃得起的好菜推不出来。现在：①`GuestView.FundShare` 删除，改为 `FundBase`（同一客人观测到的最大资金 = 本次到店总预算，`Dictionary<string,int>` 按客人名记，`ClearLeft` 换场景清空）与 `BudgetCap`；②新增 `CapOf(view)`：**还有单没上 → 上限 = 手上现钱**（保证这一单一定付得起）；**最后一单 → 上限 = 现钱 + 余量（`FundBase` 的 10%，夹 30~300）**；③`PickDrink` 的软顶从「均分」改成「现钱的 80%」，只作为「同样粉评时优先给后面几单留钱」的偏好，不再硬砍；④`DishPair.OverShare` 换成 `Cap` + `OverCash`（没超上限但花掉了最后一单余量）；`Line4` 三种说法：`预算余Y` / `略超Z（余量内）` / `⚠超Z，可能黑评`；⑤`FilterHintDrinks` 与酒水按钮的 `room` 一起改用 `BudgetCap`，三处口径统一；⑥日志新增 `预算变化：客人=X 资金 a→b`（资金变少 = `GetFund` 确实是「剩余」，最多 12 条），`预算样例` 改记「总预算 / 本单上限」 | **均分听着稳，其实副作用更大**：它优化的是「每一单都别花光」，代价是「每一单都不敢花」——按用户的真实场景（预算 1000 第一单 800、最后一单 300，总共 1100 也就超一成）均分完全没必要；**「上限」要比「排序里的权重」靠前**：能保证付得起的只有硬顶（`cap`），偏好（`soft`）只该在同样粉评的候选之间起作用，两者混成一条就会出现「预算内最贵」＝永远选最贵那杯；**换场景必须清账**：`FundBase` 按客人名缓存，不清会把上一晚的钱包当成今晚的上限 |
| **v1.6.1** | **按客人预算配酒**。①读 `GuestGroupController.GetFund`（客人身上剩的钱）与 `MaxFundCarry`，写进 `GuestView.Fund/FundMax/FundShare/FundKnown`，`OrderStamp` 带上 `Fund`（钱一变就重算）；②预算 = **剩余钱 ÷ 剩余单数**，避免第一单花光、后面几单都没钱付；③`PickDrink` 先把「菜价+酒价 > 剩余钱」的付不起酒剔掉，付得起的里面再按「粉评 → 别动用后面单的钱 → 利润 → 库存」挑；付得起的没有粉评时退回**最便宜的粉评**并在界面标超支（不偷偷降档）；④`DishCandidate.Price`（售价）与 `Profit`（利润）分开——判断付不付得起必须用售价；⑤新增 `pair.Line4`「价X 预算余Y ⚠超Z，可能黑评」、状态行与推荐框标题显示「预算余」、酒水按钮标单价与 `⚠超预算`；⑥配置 `[Behaviour] RespectFund`；⑦日志 `预算样例`（资金/上限/剩单/均分）与 `预算降档`（原本会选谁、改成谁） | **「付得起」和「赚得多」是两个方向**：v1.6.0 按利润（酒=售价）降序挑，等于永远选最贵那杯，钱包一瘪就是黑评——所以预算必须在**挑选之前**当过滤器，而不是排序里的一条权重；**预算是整晚的，不是一单的**：直接拿剩余钱当上限仍会把钱花在第一个单上，必须按剩余单数均摊（**v1.6.2 已推翻这条：改成只在最后一单留余量**）；**读不到就别拦**：`GetFund` 返回 0 或抛异常时整块失效退回老行为，宁可少一个功能，也不要因为一个字段读失败把所有酒都判成超预算 |
| **v1.6.0** | **推荐口径大改**（按用户要求）：①**菜的评级/排序不再掺酒水的 tag 命中**——排序主键改成「命中标签数（净分）」，同分才比利润率；②**加料目标固定 = 4 分（粉评线）**，不再用 `4 − 酒分` 抵扣，所以能加料就真的会加料（v1.5.9 实测 16 次一键制作全 `加料=0`，根因就是酒常有 2~3 分、菜自己早够分，规划器一份都不挑）；③**酒水**：先保证「配上这道菜还能粉评」（合计 ≥ 4），这一档里按利润 → 库存数量排；`FindBeverages` 给酒补了 `Profit`（= 售价）与 `MinRemain`（= 现有瓶数）；④`BuildPairs` 配酒同口径，Pairs 排序改 `菜净分 → 菜利润 → 合计分 → 可做 → 余量`。评级显示仍按「菜+酒合计」（游戏口径），只有排序改口径 | **同一份数据两套口径要写清楚**：显示给玩家的「粉评」必须按游戏结算（菜+酒），但**推荐排序**可以另立口径（只看菜自己），两者混用会出现「排序按合计、加料按合计」的双重偏差——v1.5.x 就是这样让规划器以为"菜已经够了"；**改 target 之前先看实测**：一局日志里 `加料=0` 出现 16 次，而规划器其实跑得很正常，差别只在 target 被酒分抵掉了 |
| **v1.5.9** | **撤掉「加料会凑成另一道菜」这道闸门**。v1.5.7 加的第二道闸门判定过宽（「另一道菜的配方是锅里食材的子集」几乎恒成立：锅里有米 → 所有米开头的短配方都命中），实测一局撤回 85 份加料、74 次撤到「加料=无」、15 次一键制作**一次都没带上料**。现在：①规划阶段只保留 BanTags 黑暗料理闸门，`DropConflictingExtras`/`MatchesAnotherRecipe`/`IsSubsetOf`/`BuildUsage`/`RecomputeHits` 全部删除；②同材料两道菜由玩家在面板自选，插件不替他砍料；③`SelectIngredients` 直填后读回游戏自己的 `m_OutputInstances`（`MatchedCookCombo` 有 `Recipe` + `Modifiers`），只有「一个组合都匹配不上」才退最后一味；④日志新增 `游戏接受的加料=N`（>0 即加料真进锅）、`同时可做=[…]（玩家自选）`、`加料核算样例` 分「有加料/无加料」两条 | **不要在规划阶段猜游戏规则**：`MatchedCookCombo` 已经暴露了游戏的判定结果（Recipe + Modifiers），能在制作时读回来验证，就不该在规划时用自制启发式去猜；**「另一个配方是子集」这种条件几乎恒真**，写闸门之前先想想「正常情况会不会也命中」；顺带修掉 `IsSubsetOf` 边判边扣不还原导致后续配方全部漏检的静默 bug |
| **v1.5.8** | **只改测量工具，游戏内表现与 v1.5.7 完全一致**。①探针拆细：采集 → 枚举/指纹/重算/文字/评分/找菜/找酒/加料/热门；头像 → 快照/扫描/全量/合成；绘制 → 皮肤/主面板/推荐框/控制面板。②每 30 秒新增两行：`计数30s`（命中/重算/快照重建/全量扫/加料被挡）与 `最慢三次`（只列 >6ms 的段，带帧号）。③长帧日志追加 `上帧最慢=<段> <ms>ms`。④新增 `帧率30s`：按 16.7/33.3/50/100ms 分桶统计帧间隔。⑤新增 `tools\run-report.py`：一条命令把整局日志压成一页报告，可存基准、可前后对比 | **平均值会骗人**：v1.5.3 的采集中位均只有 0.81ms，但 p95 是 5.36ms、单次峰 119.2ms、44/160 个窗口出现过 >20ms 峰——真正的卡顿全在尖峰里。所以探针必须记「最慢三次 + 帧号 + 长帧归因」，而不是只记均值；长帧归因还能把「插件拖的」和「场景加载/GC 拖的」分开 |
| **v1.5.7** | **加料防黑暗料理**：加料规划不再只看「能加几分」，先过两道闸门——①新引入的 tag 落在菜品 `Sellable.BanTags` 里就不选（会做成黑暗料理）；②加料后若同时还能凑成另一道菜，按「后加的先去」往回丢，避免锅里满足两道菜、手快点错。被挡住且一份没加成时，主面板写「加料会冲突，已跳过」。新增配置 `[Behaviour] AvoidDarkCooking`（默认 true）+ 诊断日志 `加料冲突表样例` / `TagRules 样例` | **丢料必须连带回滚账本**：`used`（成本/余量）和命中数都得按最终食材重算（`BuildUsage` + `RecomputeHits`），否则界面上的利润还是「没丢之前」的数；闸门只看**新引入**的 tag，菜品本来就有的味道不算加料的锅；**`AdditiveTags` 不是白名单**，初稿拿它当许可表会把正常加料全砍掉（已撤） |
| **v1.5.6** | **性能优化，功能与 v1.5.5 完全一致（只有内部改动）**。实测依据（上一局日志 `性能30s`）：采集每 0.5 秒一次、均 3.6–5.6ms、峰 89ms；巡查每 5 秒均 8–26ms、峰 64ms；绘制均 0.1ms（说明 OnGUI 不是问题）。四处改动：①**客人数据按控制器指针缓存**（`GuestCollector.ToViewCached` + `OrderStamp` 指纹 + 2 秒 TTL）：名字/喜好标签/推荐菜/文字只在点单状态真的变了才重算，`TryCast` 结果也缓存；②**头像扫描分片**（`GuestPortraits`）：全场 Image 快照缓存 4 秒 + 游标每次最多扫 900 个 + 身体层/表情层跨切片累积、整轮扫完才合成；③**巡查默认关**（`[Diagnostics] GuestAudit` 默认 false，间隔 5s→30s）；④清掉每帧垃圾（控制面板标题、头像的 GUILayoutOption、推荐框每 5 秒拼一次日志） | 关键在「便宜指纹先行」：先用几个 int 指纹判断要不要重算，别一上来就穿 interop 读名字和标签；头像必须等整轮扫完再合成，否则表情层可能落在下一个切片里、得到没表情的头像 |
| **v1.5.5** | **推荐算法计入加料**：菜谱自带 tag + 追加食材 tag 合并算分；一锅最多 5 个食材（含菜谱自带的），加料槽 = 5 − 菜谱食材数；目标分 = 4 − 酒分，够分就停；同样能粉评时按 **利润（售价 − 全部食材成本）** → **食材余量** → 加料份数 排序；新增 `ExtraPlanner.cs` + 配置 `[Behaviour] ConsiderExtras`；主面板搭配多一行「加料 / 赚N / 余N份」，推荐框明细行也标出加料。含 v1.5.4 的版本提示。**加料不改变售价**（用户确认） |
| **v1.5.4（未装机，已并入 v1.5.5）** | **进游戏版本提示**：启动约 2 秒后，屏幕上方居中弹出一张小卡片「夜雀点单助手 v1.5.5」，淡入 → 停留 3.4 秒 → 淡出，每次启动只弹一次。新增 `VersionToast.cs`（时间轴、富文本、纯色底图全部在 `Update` 里备好，`OnGUI` 只画）+ 配置 `[Display] ShowVersionNotice`。其余功能、推荐逻辑与 v1.5.3 一致。**坑**：`OverlayBehaviour.OnGUI` 必须在「无面板就早退」之前调用 `Draw()`，否则面板默认不显示时提示永远不出现；计时用 `realtimeSinceStartup`，因为开面板时游戏把 `timeScale` 设成 0、`Time.time` 会冻住 |
| **v1.5.3（已出包）** | **UI 美化 + 头像 + 未点单过滤**。①**皮肤**：IMGUI 灰盒 → 游戏自带的和风纸条（`OrderReceipt_BG` 九宫格），主面板/控制面板/料理框/酒水框四处换皮，按钮与滚动条纸片化、分割线改墨色，新增降级开关 `[Display] VanillaSkin`；②**布局**：主面板改「顶部头像横排 + 脚下展开选中一位」，点头像切换、默认落在第一位稀客、客人走了自动换人；修掉列表行文字被静默裁剪的 bug；③**头像**：稀客贴真立绘（`角色名Main_0,1` 身体层 + `Eyes` 表情层合成、按眼睛位置裁头肩特写），常客首字占位；新增外部头像目录 `config\orderhelper-portraits\`（放图即用，优先级最高）+ `config\orderhelper-portraits.txt` 人工映射表 + F11 列内部名；④**官方素材包批处理**：88 个文件名 / 85 个角色已入库（`tools\命名表.txt` → `import_portraits.py`）；⑤**未点单的客人不再出现在面板/推荐框**（连4处 `_guests.Count` 依赖一起改）；⑥防误触尝试失败后**整体放弃**（见头部事故记录）。新增 `UiSkin.cs` / `GuestPortraits.cs` / `MouseGuard.cs`；`UiProbe` 默认关闭。|
| 0.4.x | F8 主面板、字体、拖动、滚动、常客开关、营业场景自动弹出 |
| 0.5.0-0.5.3 | 厨具面板旁挂推荐框（定位/分割线/去重） |
| 0.6.x | 酒水推荐框、点击尝试（UIButtonSimple 回调） |
| 0.7.0-0.7.4 | 桌位修正、推荐指数、菜谱格注入（失败）、食材行显示 |
| 0.7.5-0.7.6 | 点推荐自动选食材（OnIngElementSelected 只跳转不落桌） |
| 0.7.7 | **直填食材成功**，点击真正可用；两框分离 |
| 0.7.8 | 分割线（自建纹理）、酒水/菜品小标题、客人间分割 |
| 0.7.9 | 酒水推荐段与菜品推荐段之间补分割线 |
| 0.7.10 | 两种分割线区分：客人间（高 3px / 不透明度 0.80 / 全宽）、酒水菜品间（高 1px / 0.35 / 缩进 22px） |
| **v1.0 基线** | 以 0.7.10 为第一版，源码/产物/安装包备份到 `backup\v1.0.0-基线-2026-10-22\` |
| v1.1.0 | 交互改版：F8 主面板、F9 控制面板；字体/常客/一键制作 开关集中到控制面板；新增配置 EnableOneClickCook |
| v1.2.0 | 控制面板打开即暂停时间（timeScale=0）、右键关闭；关闭/销毁自动恢复 |
| v1.2.1 | 控制面板打开时禁用场景内 EventSystem，防止点击穿透误触厨具/面板 |
| v1.2.2 | 控制面板打开时禁止角色移动（ForbidMovement），WASD 不再生效 |
| v1.3.0 | 每位客人的推荐改为「菜品在上、酒水在下」，两者之间保留细分割线；控制面板打开期间由 `CookSystemManager.CallCooker` 前缀拦截厨具点击（不依赖输入系统），彻底避免误触 |
| v1.2.3 | 打开时禁用 InputSystem 全部已启用 Action，彻底阻断点击/按键进入游戏；修复因暂停期间点击开厨具导致的 `禁止多次关闭` 面板卡死 |
| v1.2.4 | 暂停时不再禁用 EventSystem / InputSystem，只保持 Cursor.visible=true（修复光标冻结） |
| v1.2.5 | 关闭控制面板时精确恢复 timeScale，避免把游戏暂停状态带出错 |
| v1.2.6-1.2.9 | 暂停守卫逐步收窄（只在日记/料理台面板未打开时暂停），CallCooker 前缀拦截误触，关闭后厨具可正常使用 |
| v1.3.1 | 酒水直填错位修复：`Extract(sellable)` 按对象取酒，不按列表索引 |
| v1.4.0 | 按游戏真实 8 分制重写推荐评分（喜好/厌恶 TAG、粉评映射）；识别特殊厨具（三位一体等） |
| v1.4.1 | 今日流行显示（Pop*Tags）；推荐侧边栏可拖动、可用鼠标滚轮滚动 |
| v1.4.2 | 特殊厨具诊断日志（registered / 菜谱清单） |
| v1.4.3-1.4.4 | 三位一体推荐改读面板自带 15 道固定菜谱（FromPanel），不再按厨具名硬匹配 |
| v1.4.5-1.4.6 | 离席客人过滤（HaveNotLeft + RemainOrderCount 兜底）；特殊厨具按点单标签优先排序 |
| v1.4.7 | 离店客人补丁：GuestGroupController.MoveToSpawn / FlyToSpawn → 标记已离开，推荐框剔除 |
| v1.4.8 | 三位一体：复用普通厨具 top4 推荐，只保留其能做的；未点单不推荐；已打包 夜雀点单助手-v1.4.8.zip |
| v1.5.2 | ①「稀客影像」不进推荐框（露易兹符卡「传奇的格列佛」把没吃饱的稀客拍成旅者博客，当日结束后投影回来）：离店黑名单 `HashSet<IntPtr>` 记下就永久隐藏、且从不清空，影像复用同一控制器 → 被自己的离店标记挡住。改成 `Dictionary<IntPtr,(时间,订单数)>`：订单数变化即撤销标记、换场景 `ClearLeft()`、10 分钟 TTL；②新增客人巡查日志 `[Diagnostics] GuestAudit`（每 5 秒，内容变化才写：控制器类型/点单/桌位/是否被过滤），单独计时，不混进「采集」；③采集性能：4 位客人时平均 30.8ms、峰值 101ms（约 7ms/人），原因是第一段循环每条菜谱都要 2 次 IL2CPP 字典查询 + `food.Tags`（每次 marshal 数组）+ `Text.Name`（marshal 字符串），饮品还每次重枚举整个字典；改为按 id 缓存静态数据成纯 C# 副本（名字/标签/食材/厨具）+ 缓存菜谱 id 表与饮品 id 表，`RecipeIndex.Invalidate()` 挂到 `OnSceneLoaded`；④探针细分：采集拆成 `采集·找菜`/`采集·找酒`/`采集·评分`，新增 `巡查` 埋点；⑤新增 `tools/perf-report.py`（窗口表 + 长帧归因，自带 11 项自检）。|
| v1.5.1 | 性能优化（针对"偶尔卡顿"）：①`OnGUI` 每帧两遍（Layout+Repaint）原来都在现拼字符串 —— 客人标题/点菜行、热门流行两行、料理框标题、按钮整串文字，全部改成收集阶段（0.5 秒一次）预生成；②推荐框里每帧 `FindAll` + `new List`（粉评筛选）挪进 `MatchesFor` 的缓存；③酒水推荐框的"可推列表"在 `ApplyRatings` 里算好（`GuestView.HintDrinks`）；④`SnapshotLog` 改为 `[Diagnostics] SnapshotLog` 默认关（一次两百行同步写日志会顿），候选只列前 5；⑤新增 `Perf` 探针：单帧 >150ms 记一条长帧、每 30 秒一行「采集/绘制/注入」耗时汇总（`[Diagnostics] PerfLog`，默认开）。行为不变。|
| v1.5.0 | ①修主面板配对的「酒水净分」显示错误：原先把 `drink.LikeHits`（命中数）当净分，踩厌恶标签时虚高，改用 `Net(drink)`；②删掉 `Find`/`FindBeverages` 里永不生效的 `RequestHit` 过滤（`RequestHit` 默认 true、只有 `FromPanel` 会写，两处过滤恒等于原列表），行为不变、去掉误导；③安装包内附 `update.ps1` + `更新.bat`（自动更新改用 GitHub Releases，不依赖 git）。|
| v1.4.9 | ①推荐显示重做为「菜 + 酒 配对」：每条给出菜（含厨具）+ 酒水 + 评级 + 合计分 + 「净X分」；②稀客只推能粉评（合计 ≥4）的组合，凑不出则分列「命中标签最多的菜 / 酒水」；常客不限；③修 `Score` 覆盖后不重排导致的「下面粉评上面绿评」；④修厌恶标签读取失败（`IEnumerable<int>` 强转 bug），厌恶扣分终于生效；⑤稀客 tag 负值语义查清：`-1` = 无此项 → 未点单显示「（尚未点单）」、酒水不限按喜好推荐；⑥已上菜（`IsFullfilled`）的客人不再进推荐框；⑦缺料文案改食材名 `缺食材：牛肉(0/2)`；⑧三个窗口（主面板/料理框/酒水框）位置与大小可在 F9 控制面板调 + 拖动 + 写入 `[Layout]` 记忆 + 退出时兜底保存；⑨三位一体候选补算评价（修「空评级 合计0分」） |

## 9 经验文档

- MOD制作经验文档.md：全部踩坑与修复记录（工具链、IL2CPP、IMGUI、Harmony、面板内部结构、评分公式、调试流程），**续作前必读**。
  - §4.5「从游戏里取 UI 素材」是 v1.5.3 新增的一整节（坑清单 + 三条纪律）。
- UI侦察报告.md：UI 素材从哪来、订单卡结构、导出的 12 张样例图。
- UI美化开发计划.md：**UI 批次的目标、进度、下一步**（批4 的两条候选路线与成本），接着做 UI 时先读它。
