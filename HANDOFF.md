# 交接文档：夜雀点单助手（Mystia Order Helper）

> **先读 `MOD制作经验文档.md`**：里面是全部踩坑记录、游戏 API 地图、构建流程和续作指南；本文件只讲项目现状与交接事项。

- 交接时间：2026-10-22 22:55（v1.5.0：修酒水净分显示 + 清理死代码 + 包内附自动更新脚本）
- 当前版本：**v1.5.0**（`dist\MystiaOrderHelper-v1.5.0.dll`，安装包 `夜雀点单助手-v1.5.0.zip`）
- 环境：Windows，游戏装在 `<游戏目录>`（Steam 库路径因人而异）
- 本阶段收尾点：v1.5.0 已打包（zip 内含自动更新脚本），待用户实机复验

---

## 1 现在能做什么（已交付）

**主面板（F8 开/关）**

- `在场客人：N`，逐位显示：`◆ 名字（稀客/常客） 桌位 X  剩N单`
- 每位客人：`点菜：…`、`点酒：…`
- `[菜 + 酒 搭配]` 若干条，每条两行：
  - `→ 菜名[厨具]＋酒名`（缺料/缺货会标出来）
  - `粉评 5分  差N分  ｜  净分 菜2 酒3`
- 菜与酒按「合计分」配对排序；**稀客只列能粉评（合计 ≥4）的组合**，常客不受此限
- 稀客凑不出粉评时显示 `[凑不出粉评 · 命中标签最多]`，下面分别给一条最佳菜 / 最佳酒水
- 不同客人之间一条较粗较亮、占满整行的分割线
- 顶部按钮：`控制面板（F9）`，旁边显示一键制作当前状态
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
├─ 项目报告.md              需求/技术方案/验收（R2 版，含 0.5.x-0.7.x 变更记录见 §12）
├─ HANDOFF.md               本文档
├─ build.ps1                编译（可 -Install 直接装）
├─ game.ps1                 启停/重启/状态：start | stop | restart | status
├─ dev.ps1                  构建并重启（无 -Force 时若游戏在跑则拒绝）
├─ start.bat / stop.bat / dev.bat
├─ dist\                    编译产物 MystiaOrderHelper-v1.5.0.dll
├─ backup\                  旧插件与旧版本备份（old-plugins\ 等，不随安装包发布）
├─ docs\
│   ├─ M0-侦察结果.md        IL2CPP 类型侦察原始结论
│   └─ probe-session-0.4.2.log  面板结构探测日志（收藏 8 道菜的来源）
└─ src\OrderHelper\         全部源码（dll 约 83 KB）
    ├─ OrderHelperPlugin.cs     BepInPlugin 入口、配置项、宿主 GameObject、组件注入
    ├─ OverlayBehaviour.cs      IMGUI 主面板 + 两个旁挂推荐框 + 拖动/字体/分割线/布局设置
    ├─ GuestCollector.cs        每帧收集客人、桌位、菜品/酒水候选与评分
    ├─ RecipeIndex.cs           食谱索引、解锁过滤、标签匹配、备料可用性
    ├─ CookerPanelWatcher.cs    厨具面板开关检测、食材直填与点击、按钮映射、调试转储
    ├─ BeveragePanelWatcher.cs  酒水面板开关检测与推荐渲染
    ├─ PanelClicker.cs          调用游戏按钮回调 / 模拟点击的公共工具
    ├─ Probes.cs                Harmony 补丁集中注册（新增补丁必须在这里登记！）
    └─ Diagnostics.cs           客人快照日志、控制面板里的“导出客人信息”
```

---

## 3 构建与安装（照抄即可）

### 3.1 编译

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "<项目目录>\build.ps1"
```

产物：`dist\MystiaOrderHelper-v1.5.0.dll`（约 82.5 KB）。

注意事项（都是踩过的坑）：

1. 本机 **不能用 MSBuild/dotnet build**：NuGet 路径（`%APPDATA%\NuGet`、`D:\temp`、`.templateengine`）被拒。只能直接调 `csc.dll`。
2. 编译器：`<便携 .NET 目录>\sdk\6.0.428\Roslyn\bincore\csc.dll`，用同目录 `dotnet.exe exec` 跑。
3. **csc 不能直接写 E:**，所以 `build.ps1` 先编到 `...\.tmp\orderhelper\`，再拷进 `dist\`。
4. 直接 `& build.ps1` 会被 PowerShell 执行策略拒绝（UnauthorizedAccess），必须包 `powershell.exe -ExecutionPolicy Bypass -File`。
5. 源码必须是 **UTF-8**，csc 参数带 `-codepage:65001`；改 `build.ps1` 本身时注意 PowerShell 5.1 按 GBK 读无 BOM 脚本，脚本里别放中文。

### 3.2 安装（游戏必须先关，dll 会被锁）

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "<项目目录>\game.ps1" stop
# 等进程真的退出
# 旧 dll 先归档（别直接删），再放新 dll
New-Item -ItemType Directory -Force "<项目目录>\backup\old-plugins" | Out-Null
Get-ChildItem "<游戏目录>\BepInEx\plugins" -Filter "MystiaOrderHelper-v*.dll" |
    Where-Object { $_.Name -ne "MystiaOrderHelper-v1.5.0.dll" } |
    Move-Item -Destination "<项目目录>\backup\old-plugins" -Force
Copy-Item "<项目目录>\dist\MystiaOrderHelper-v1.5.0.dll" "<游戏目录>\BepInEx\plugins\" -Force
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "<项目目录>\game.ps1" start
```

验证加载：

```powershell
Select-String -Path "<游戏目录>\BepInEx\LogOutput.log" -Pattern "夜雀点单助手 v1\.4\.9" -Encoding UTF8 | Select-Object -Last 1
```

期望看到：`夜雀点单助手 v1.5.0 已加载（F8 主面板，F9 控制面板）`。

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

---

## 6 下次继续的建议顺序

1. ~~酒水直填~~ 已完成（v1.3.1）；~~推荐框位置记忆~~ 已完成（v1.4.9，三个窗口通用）；~~打包发布~~ 已完成（`夜雀点单助手-v1.5.0.zip`）；~~包内附更新脚本~~ 已完成（v1.5.0）。
2. **等用户实机复验 v1.5.0**：配对显示、净分（含酒水净分已修正）、稀客只推粉评、已上菜客人剔除、布局记忆、双击 `更新.bat` 能识别已是最新。
3. 性能 K7：改为事件驱动收集（客人入座/离店补丁 + 点单补丁），去掉 0.5s 一次的全场扫描。
4. 字号 K4：字号变化后窗口自适应（注意不能覆盖用户自定义宽高，需要「手动改过就锁住」的标记）。
5. 交互：推荐列表支持键盘上下选择；窗口最小化。
6. ~~下一版打包时把更新器一起塞进 zip~~ 已完成（v1.5.0）：`update.ps1` + `更新.bat` 放在 zip 根（与 `BepInEx\` 同级，`%~dp0update.ps1` 才能找到），安装说明已补「解压到游戏根目录」与「更新后多出 `BepInEx\plugins\_backup\`」两段。注意：已装 v1.4.9 的人要手动换一次包，之后才能双击更新。
7. GitHub 侧（2026-10-22）：仓库 `xinyoudayu/mystia-order-helper`（Public），`v1.4.9` 的 tag 与 Release 资产 `MystiaOrderHelper-v1.4.9.zip`（39930 字节）已发布；`update.ps1` 的 5 条分支（全新装/升级/已最新/`-Check`/404）均已实测通过。**v1.5.0 待发布**：`git tag v1.5.0` + Release 上传 `夜雀点单助手-v1.5.0.zip`（资产名同样必须 ASCII，建议 `MystiaOrderHelper-v1.5.0.zip`）。
8. 坑（2026-10-22 修）：`update.ps1` 原来在 `-Repo` 为空时用 `& git ...` 推导仓库名，玩家机器没装 git 会因 `$ErrorActionPreference='Stop'` 直接崩（`CommandNotFoundException`）；已改为 `$Repo` 默认写死 `xinyoudayu/mystia-order-helper`，git 推导降级为「有 git 且 `.git` 存在才尝试」的可选兜底。

---

## 7 15 分钟快速上手自测

1. `game.ps1 start`，进游戏 → 进营业场景
2. 按 F8 应出现主面板，客人列表带分割线；按 F9 应出现居中的控制面板（字体/常客/一键制作）
3. 打开一个厨具（如煮锅）→ 屏幕右侧出现暖色 `【料理】推荐（煮锅）`
4. 点其中一道菜 → `LogOutput.log` 出现 `直填食材 … 选中>0 可制作组合>0`，面板里出成品
5. 打开酒水页 → 右侧出现冷色 `【酒水】推荐`
6. 点推荐菜若无效，先看日志是 `点击未找到元素`（格子不存在，题目本身在 4.4 已解释）还是 `模拟点击` 失败

---

## 8 版本轨迹（本阶段）

| 版本 | 关键变化 |
|---|---|
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
| v1.5.0 | ①修主面板配对的「酒水净分」显示错误：原先把 `drink.LikeHits`（命中数）当净分，踩厌恶标签时虚高，改用 `Net(drink)`；②删掉 `Find`/`FindBeverages` 里永不生效的 `RequestHit` 过滤（`RequestHit` 默认 true、只有 `FromPanel` 会写，两处过滤恒等于原列表），行为不变、去掉误导；③安装包内附 `update.ps1` + `更新.bat`（自动更新改用 GitHub Releases，不依赖 git）。|
| v1.4.9 | ①推荐显示重做为「菜 + 酒 配对」：每条给出菜（含厨具）+ 酒水 + 评级 + 合计分 + 「净X分」；②稀客只推能粉评（合计 ≥4）的组合，凑不出则分列「命中标签最多的菜 / 酒水」；常客不限；③修 `Score` 覆盖后不重排导致的「下面粉评上面绿评」；④修厌恶标签读取失败（`IEnumerable<int>` 强转 bug），厌恶扣分终于生效；⑤稀客 tag 负值语义查清：`-1` = 无此项 → 未点单显示「（尚未点单）」、酒水不限按喜好推荐；⑥已上菜（`IsFullfilled`）的客人不再进推荐框；⑦缺料文案改食材名 `缺食材：牛肉(0/2)`；⑧三个窗口（主面板/料理框/酒水框）位置与大小可在 F9 控制面板调 + 拖动 + 写入 `[Layout]` 记忆 + 退出时兜底保存；⑨三位一体候选补算评价（修「空评级 合计0分」） |

## 9 经验文档

- MOD制作经验文档.md：全部踩坑与修复记录（工具链、IL2CPP、IMGUI、Harmony、面板内部结构、评分公式、调试流程），**续作前必读**。
