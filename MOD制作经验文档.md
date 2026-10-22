# 东方夜雀食堂（Touhou Mystia's Izakaya）Mod 制作经验文档

> 版本基线：夜雀点单助手 v1.4.6 ｜ 首次撰写：2026-10-22 ｜ 适用对象：后续接手本项目的 AI / 人类开发者
>
> 本文档汇总了从零做一个 BepInEx 插件型 mod 过程中遇到的**全部问题、根因、修法和验证方式**。
> 读完本文 + `HANDOFF.md` + `项目报告.md`，应当能不重复踩坑地继续开发。

---

## 0. 先读这一段（最重要）

1. **这是一个 IL2CPP + BepInEx 6 的插件项目**，不是资源包 mod。改的是游戏内存里的运行时对象，不是 pak 文件。
2. **所有"游戏 API 调用"都必须当成可能抛异常**。IL2CPP 的 interop 里大量方法只是 `il2cpp_runtime_invoke` 桩，字段名对不上、方法被裁剪、结构体传参失败都会在运行时炸，编译期完全发现不了。规范：**每个游戏调用都包 try/catch，出错只记警告，不崩游戏。**
3. **"补丁没生效"优先怀疑注册**：Harmony 补丁类写了不等于挂上了，`Probes.Install()` 里逐个 `TryPatch` 漏一个就是静默无效（我们为此丢了一整轮日志）。
4. **游戏运行时 dll 被占用**，装插件必须先让游戏完全退出；`开游戏 → 装插件` 的顺序永远会失败并报 `UnauthorizedAccessException`。
5. 日志是唯一可靠信号：`<游戏目录>\BepInEx\LogOutput.log`。

---

## 1. 环境事实（不要重新探测，直接用）

| 项 | 值 |
|---|---|
| 游戏安装 | `<游戏目录>` |
| Steam AppID | 1584090 |
| buildid | 23158340 |
| Unity | 2021.3.28f1（IL2CPP） |
| BepInEx | 6.0.0-be.785（加载器是 `winhttp.dll`） |
| 游戏版本（运行时） | 0.29.4 |
| 插件目录 | `<游戏>\BepInEx\plugins\` |
| 配置目录 | `<游戏>\BepInEx\config\` |
| 日志 | `<游戏>\BepInEx\LogOutput.log` |
| interop 程序集 | `<游戏>\BepInEx\interop\`（Assembly-CSharp.dll 等） |
| 项目目录 | `<项目目录>` |
| 已装第三方 mod | MetaMystia v0.29.4（联机，`MetaMystia-v0.29.4.dll`）、ResourceExample |

**游戏里没有官方 Mod 加载接口**（没有像 Stardew 的 SMAPI 那种），BepInEx 是唯一入口。资源包（ResourceEx）只能加食材/菜谱，**不能改 UI 逻辑**。

---

## 2. 工具链与构建（踩坑最多的地方）

机器上**原本没有 .NET SDK**，且 MSBuild / NuGet 都不可用（`%AppData%\Roaming\NuGet`、`D:\temp`、`.templateengine` 目录被沙箱拒绝访问）。

最终方案：**只用 Roslyn 的 `csc.dll` 直接编译**，不碰 MSBuild / NuGet / 项目文件。

### 2.1 依赖位置

- .NET 6.0.428 便携版：`<便携 .NET 目录>\`
  - 编译器：`.dotnet\sdk\6.0.428\Roslyn\bincore\csc.dll`
  - 引用包：`.dotnet\packs\Microsoft.NETCore.App.Ref\6.0.36\ref\net6.0\`（**注意路径里是 6.0.36，不是 6.0.0**，手工拼路径时踩过）
- BepInEx 核心 dll：`<游戏>\BepInEx\core\`（**排除非托管的 `dobby.dll`**）
- interop：`<游戏>\BepInEx\interop\` 下的 Assembly-CSharp / UnityEngine 系 dll

### 2.2 编译命令（build.ps1 已封装）

```powershell
& powershell -NoProfile -ExecutionPolicy Bypass -File build.ps1            # 只编译
& powershell -NoProfile -ExecutionPolicy Bypass -File build.ps1 -Install   # 编译并装进游戏（游戏必须已退出）
```

`build.ps1` 内部：收集 `src\OrderHelper\*.cs` → `csc.dll` 编译 → 输出到 `dist\MystiaOrderHelper-v<版本>.dll` → 若 `-Install` 则**先删旧 `MystiaOrderHelper-v*.dll`** 再复制（避免同时加载两份）。

### 2.3 工具链坑清单

| 坑 | 现象 | 修法 |
|---|---|---|
| csc 不能写 `E:\` | 编译输出 `Access is denied` | **输出到 C 盘暂存目录再复制过去**；build.ps1 已处理 |
| PowerShell 5.1 把 `.ps1` 当 GBK 读 | 中文注释/字符串报 `TerminatorExpectedAtEndOfString` | **`.ps1` 里只写 ASCII**（脚本注释也用英文） |
| C# 源码里有中文 | 编译乱码 / 语法错 | csc 加 `-codepage:65001`，源文件存 UTF-8；写文件用 `write` 工具而不是 PowerShell 重定向 |
| 手拼 csc 参数时 ref 路径写错 | “找不到 System.Runtime” 等一大堆 | 别手拼，直接用 `build.ps1` |
| 游戏运行中安装 | `UnauthorizedAccessException` | 先 `game.ps1 stop`，确认进程消失再 `-Install` |
| 版本号三处不同步 | 日志显示旧版本号 | 每次升版同时改：`OrderHelperPlugin.Version`、`build.ps1` 的 `$version`、文档 |
| 沙箱偶发 ACL 故障 | 普通读写报 `sandbox-local windows-acl ... failed` | 重开会话/换策略；不要绕路重试 |

---

## 3. IL2CPP + BepInEx 插件骨架坑

### 3.1 MonoBehaviour 必须注册类型

```csharp
ClassInjector.RegisterTypeInIl2Cpp<OrderHelperPlugin>();   // 在 Load() 里，AddComponent 之前
ClassInjector.RegisterTypeInIl2Cpp<OverlayBehaviour>();
```

漏了会得到 `... is not registered` 或 `AddComponent` 直接崩。

### 3.2 场景切换会销毁宿主 GameObject

- `DontDestroyOnLoad` 在主场景之前调用**不可靠**，主场景加载时会连 DontDestroy 的对象一起清掉（本游戏实测如此）。
- 唯一可靠做法：**每次 `sceneLoaded` 重新建一个 GameObject 并 AddComponent**。

```csharp
SceneManager.add_sceneLoaded((Action<Scene, LoadSceneMode>)((s, m) => EnsureBehaviour()));
```

同时记住 `Time.frameCount==0` 时协程/Start 可能还没跑，全部逻辑放 `Update` + try/catch。

### 3.3 常见 IL2CPP 限制

| 限制 | 例子 | 写法 |
|---|---|---|
| `Il2CppSystem.Collections.Generic.List` 不是 `IEnumerable` | `new List<T>(il2cppList)` 编译不过 | `for (int i=0;i<list.Count;i++) copy.Add(list[i]);` |
| 结构体数组 | 流行标签是 `Il2CppStructArray<int>` | 用 `list[i]` 逐项读，或 `ToArray()` 后转 |
| `Vector3` 默认自动转换 | `new Vector3(x,y,z)` OK | 转回托管用 `(Vector3)v` |
| 方法被裁剪 / 不存在 | `MissingMethodException` | 全部 try/catch，失败降级 |
| 私有字段可读 | `panel.m_RecipeInstances` | interop 里字段是 public，直接访问 |

### 3.4 宿主 / 生命周期检查

- `OverlayBehaviour.Update` 里放心跳日志（`frame=..., visible=..., guests=...`），用于证明插件还活着、UI 逻辑在跑。
- **不要假设 OnGUI 每帧都调**（`Time.timeScale=0` 时 OnGUI 仍走，但协程不走）。

---

## 4. IMGUI 覆盖层坑

这是本插件的主体（F8 面板 + 随厨具弹出的推荐框），坑最多。

| 坑 | 现象 | 修法 |
|---|---|---|
| `GUILayout.Window` 崩溃 | `MissingMethodException: LayoutedWindow..ctor` | **不要用 GUILayout.Window**；用 `GUI.Box` + `GUI.BeginArea(rect)` + `GUILayout.*` 画内容 |
| 窗口不能拖 | 面板固定 | 自己在 OnGUI 里处理 `Event.current`：`MouseDown→DragWindow` 手动改 rect |
| `Texture2D.whiteTexture` 画不出 | 分割线透明 | 自建 1×1 `Texture2D`（`SetPixel` 白点 + `Apply`），`OnDestroy` 里 `Destroy` |
| 中文字体变方块 | 默认字体无中文 | 从 `TMP_Settings.defaultFontAsset.sourceFontFile` 取字体（本游戏是 **JYHPHZ**），兜底 `Microsoft YaHei` |
| 字号改不动 | `GUI.skin.fontSize` 无效 | 每个 `GUIStyle` 都设 `fontSize`，或用 `GUI.skin.font = font` + 统一 `GUIStyle` 实例 |
| 鼠标滚轮没反应 | 列表滚动不动 | `GUI.BeginScrollView` 自带滚轮；若被游戏吃掉，读 `Input.mouseScrollDelta.y` 手动加 |
| 点击穿透/被挡 | 点不到按钮 | 面板画在 `GUI.depth = -100`；不要禁用整个 EventSystem（会冻结光标，见 §9） |
| 面板位置漂移 | 换分辨率后出屏 | 每帧 `ClampRectToScreen()` |

### 4.1 面板布局约定（本项目）

- 主面板：`ShowNormalGuests` 开关、`字体 -/+`、`控制面板(F9)`、`一键制作` 状态、今日流行、每位客人一段。
- 推荐框：料理在左、酒水在右（默认叠在屏幕右侧，可拖动）；特殊厨具（三位一体）只显示料理块。
- 每隔 5 秒把 `推荐框客人` 状态写日志（名字/rem/all/点单状态），排查"客人不刷新"用。

---

## 5. Harmony 补丁坑（本项目的头号事故来源）

### 5.1 事故：补丁类写了但没挂

`CookerPanelOpenPatch` / `CookerPanelClosePatch` 写好、能编译、**但 `Probes.Install()` 里只 patch 了另外 3 个类**，结果整轮测试日志一条都没有，白追一轮。

**规范**：`Probes.Install()` 每加一个补丁类就加一行 `TryPatch(harmony, typeof(XxxPatch), "Target.Method", true);`，并在日志输出 `[Probe] 已挂补丁: Target.Method`。启动时数一下日志里补丁数量对不对。

### 5.2 补丁目标要确认签名

- `TryPatch(harmony, type, "Namespace.Class.Method", prefix)` 里方法名写错 → 静默失败。
- 用 Mono.Cecil 转储方法签名（见 §11.2）确认存在、参数个数/类型对得上。
- 私有方法可以 patch，`__instance`、`___字段名` 可用。
- 不要 patch 泛型方法、不要在补丁里做重活（每帧调用）。

### 5.3 本项目实际有效的钩子

| 目标 | 用途 |
|---|---|
| `Cooker.OnPanelOpen / OnPanelClose` | 知道玩家打开了哪个厨具面板（`CookerName` 从面板标题文本读） |
| `WorkSceneCookingSelectionPannel.OnRecipeElementEnabled(Recipe, UIElementCluster, UIButtonSimple)` | 拿到"菜谱→按钮"映射，后来发现格子是按需生成，开面板瞬间可能为 0 |
| `WorkSceneCookingSelectionPannel.OnIngElementEnabled(KeyValuePair<Ingredient,int>, UIElementCluster, UIButtonSimple)` | 拿到"食材→按钮"映射 |
| `WorkSceneCookingSelectionPannel.OnElementEnabled(KeyValuePair<Sellable,int>, ...)` | 酒水面板 |
| `CookSystemManager.CallCooker` (Prefix) | 控制面板打开时阻止"误触厨具" |

---

## 6. 游戏数据 API 地图（已验证可用）

```csharp
// 定义数据（静态）
DataBaseCore.Recipes      : Dictionary<int, Recipe>     // 全部菜谱，key=RecipeId
DataBaseCore.Foods        : Dictionary<int, Sellable>   // 菜品，key=FoodId
DataBaseCore.Beverages    : Dictionary<int, Sellable>
DataBaseCore.Ingredients  : Dictionary<int, Ingredient>
DataBaseCore.IngredientsRef : Dictionary<int, ...>      // RefIngredient

// 运行时（玩家当前存档）
RunTimeStorage.Recipes    : Dictionary<int, int>  // 已解锁菜谱
RunTimeStorage.Beverages  : Dictionary<int, int>  // 酒水库存
RunTimeStorage.Ingredients: Dictionary<int, int>  // 食材持有

// 菜谱结构
Recipe.FoodID, Recipe.CookerType(enum Pot/Grill/Fryer/Steamer/CuttingBoard/Empty/...), Recipe.Ingredients(List<Ingredient>)
Recipe.Food               // Sellable（名字在 .Text.Name）

// 客人
GuestsManager.Instance.AllPresentedGuestGroupController   // 在场客人
controller.PeekOrders()  → OrderBase
order.DeskCode, order.foodRequest, order.beverageRequest, order.IsFullfilled
order.TryCast<GuestsManager.SpecialOrder>() → RequestFoodTag / RequestBeverageTag
controller.TryCast<SpecialGuestsController>() != null     // 是否稀客
special.SpecialGuest      → SpecialGuest（LikeFoodTag/HateFoodTag/LikeBevTag/HateBevTag、GetFoodTagText、GetBevTagText）
GuestGroupController.HaveNotLeft()      // false = 已离场（过滤残留客人用）
controller.RemainOrderCount / AllOrdersCount / DeskCode / OnGetGuestName()

// 流行（每日刷新，存档持久）
RunTimePlayerData.PopLikeFoodTags / PopHateFoodTags / PopLikeBevTags / PopHateBevTags : Il2CppStructArray<int>
```

**关键语义**：
- 常客 `foodRequest` 是**菜品 ID**；稀客 `foodRequest` 是**标签 ID**（`RequestFoodTag`），标签可读文本用 `special.SpecialGuest.GetFoodTagText(tag)`。
- 稀客 `raw` 标签可能为负（表示"无此需求"），别当数组下标用。
- `Pop*` 标签是"流行"，与稀客喜好无关，评分时要分开算（本项目：流行只显示不参与推荐分）。
- **每日菜单** `DailyRecipes` 只有 8 道，≠ 玩家收藏 ≠ 面板可做的全部菜。

---

## 7. 料理面板 `WorkSceneCookingSelectionPannel` 内部结构（实锤）

```
WorkSceneCookingSelectionModule(Clone)
├─ Title/Text                       // 厨具名："煮锅"/"烤架"/"油锅"/"蒸锅"/"料理台"/"三位一体"
├─ BG/Ingredients/Content/Objects   // 食材格子（ItemStackElement，按口味分池，按需生成）
├─ BG/Viewport/Recipes (GridLayoutGroup)  // 菜谱格子，**开面板瞬间可能为空**，由 UpdateRecipeField 惰性填充
├─ BG/Viewport/Selected             // 已选食材
├─ BG/Viewport/Output               // 可制作组合（MatchedCookCombo）
└─ BG/SellableDescriberWindow_*     // 右侧/下侧详情窗（点菜格显示做法）
```

字段（interop 里是 public）：
- `m_RecipeInstances : List<Recipe>` —— 面板当前显示的菜谱（**三位一体这类特殊厨具是固定 15 道**）
- `m_Ingredient_MeatInstances / OtherInstances / SeaFoodInstances / VeggiesInsatance`
- `selectedIngredients : List<int>`（已选食材 ID）、`m_SelectedInstances : List<Ingredient>`
- `m_OutputInstances : List<MatchedCookCombo>`
- `importedRecipe`（普通厨具从菜单"导入"菜谱时用）、`MatchingSelected(out bool haveBadCombo)`

方法（私有，可反射/补丁）：
- `OnRecipeElementSelected(Recipe, UIElementCluster, UIButtonSimple)`
- `OnIngElementSelected(KeyValuePair<Ingredient,int>, UIElementCluster, UIButtonSimple)`
- `OnSelectedIngredientsSelected(Ingredient, ...)`（移除食材）
- `OnOutputSelected(MatchedCookCombo, ...)`（选成品）
- `UpdateRecipeField / UpdateSelectedField / UpdateOutputField`
- `TryReturnSelectedIngredients / Extract(Sellable)`

### 7.1 三条血泪结论

1. **点击菜格 / 调 `OnRecipeElementSelected` 都不可靠**：`importedRecipe` 在面板打开后赋值不生效（实测 0 效果），`OnIngElementSelected` 只会切到"选食材"页并高亮，不把食材放上料理台。
2. **唯一可靠的"一键制作"路径是直填**：
   ```csharp
   pannel.selectedIngredients.Clear();
   foreach (var ing in recipe.Ingredients.Select(i => i.RefIngredient(...)))
       pannel.selectedIngredients.Add(id);
   pannel.m_SelectedInstances.Clear();      // 然后重新 Add
   pannel.UpdateSelectedField();
   pannel.UpdateOutputField();
   ```
   实测日志 `直填食材 选中=4 可制作组合=3`，成就系统会记录"按推荐出餐"。
3. **面板要显示你的推荐菜，必须注入**：把候选 `Recipe` 写进 `m_RecipeInstances` 再 `UpdateRecipeField()`；否则面板里没有这道菜，点不到（普通厨具和特殊厨具都一样）。

---

## 8. 推荐算法与评价体系（项目核心业务逻辑）

### 8.1 游戏评价规则（稀客）

来源：[9game 顾客评分表](https://www.9game.cn/dongfangyequeshitang/6587724.html)

- 稀客每次点单：从**料理喜好 TAG** 和**酒水喜好 TAG** 里各抽 1 个作为要求（打乱）。
- **每命中 1 个料理喜好 TAG → +1 分；命中 1 个料理厌恶 TAG → -1 分。**
- 评级：`总分 ≥ 6 粉评`、`4~5 橙评`、`2~3 绿评`、`1 紫评`、`≤0 黑评`（酒水粉评需全中，即 3 个喜好 TAG 的饮料）。

### 8.2 本项目的推荐指数

```text
指数(0-100) = 50 + 10×命中喜好数(最多2) - 30×命中厌恶数 + 10×(能立刻做)
评级：≥90 粉评｜≥70 橙评｜≥50 绿评｜≥30 紫评｜否则黑评
```

- 稀客：候选菜必须**包含点单 TAG**（否则 `RequestHit=false`，排到最后，标注"点单标签不符"）。
- 常客：按精确菜品 ID 匹配。
- 酒水：按 `RequestBeverageTag` 匹配 + 库存判断"有货/缺货"。
- 特殊厨具（三位一体）：**只能做面板自带的那 15 道**，不能注入，推导逻辑要改成"从面板菜谱里挑"，否则永远空。

---

## 9. 操控 / 暂停 / 输入坑

| 目标 | 踩过的坑 | 最终做法 |
|---|---|---|
| F9 打开控制面板时暂停时间 | 设 `Time.timeScale=0` 全局生效，把厨具面板/日记也冻住 | 只在"Notebook 与 CookingSelection 都未打开"时暂停（v1.2.9 的守卫） |
| 暂停时防误触厨具 | 玩家还能点台面 | Harmony Prefix 挂 `CookSystemManager.CallCooker`，`SettingsVisible` 时 `return false` |
| 光标冻结 | 禁用整个 EventSystem / InputSystem 全部 Action → 软光标不动 | **不要全禁**；只改用 `Cursor.visible=true`，不碰指针动作 |
| 右键关闭控制面板 | 游戏没用右键 | `Input.GetMouseButtonDown(1)` + 面板 `visible` 判断 |
| 关闭后厨具打不开 | `ForbidMovement`/EventSystem 没恢复 | 关闭时恢复 `timeScale`、`Cursor`、各 Action；用 `PannelOpenContext` 判断是否真在面板里 |
| F8/F9 与游戏按键冲突 | 无冲突 | 旧 `Input.GetKeyDown(KeyCode.F8)` 即可（游戏同时装了 InputSystem，两者都收） |

---

## 10. 杂项坑

- **ResourceEx 加的菜谱默认是"未解锁"**：`RunTimePlayerData.CheckRecipeIsLocked()` 返回 true，不会进 `RunTimeStorage.Recipes`，推荐里看不到。要测就直接用游戏原生菜谱。
- **MetaMystia 会往日志里刷大量行**，`LogOutput.log` 可能上百 MB，读日志用 `Select-String -Encoding UTF8 -Pattern '...'` 过滤，别整读。
- **截图/日志里带 `\uXXXX`** 的是日志编码问题，用 `-Encoding UTF8` 读。
- **游戏"每日流行"标签文本本身是长句**（"我想要清淡的料理"），不是 bug。
- **特殊厨具识别**：`CookerName` 从面板 `Title/Text` 读；已知普通厨具 = 煮锅/烤架/油锅/蒸锅/料理台，其他都算特殊（三位一体、怪诞料理台等）。
- **客人残留**：客人走完离场动画后仍可能留在 `AllPresentedGuestGroupController` 里，且 `HaveNotLeft()` 不一定及时翻转。最可靠的是给 `GuestGroupController.MoveToSpawn / FlyToSpawn` 挂 Postfix，把 `controller.Pointer` 记进"已离开"集合；采集时同时用 `HaveNotLeft()` 和该集合过滤。

---

## 11. 调试与日志工作法（照这个流程做，省一半时间）

### 11.1 标准循环

```powershell
# 1) 编译
& powershell -NoProfile -ExecutionPolicy Bypass -File build.ps1
# 2) 等游戏退出后安装（必须！）
& powershell -NoProfile -ExecutionPolicy Bypass -File build.ps1 -Install
# 3) 启动
& powershell -NoProfile -ExecutionPolicy Bypass -File game.ps1 start
# 4) 验证版本
Select-String -Path '<游戏>\BepInEx\LogOutput.log' -Pattern 'Loading \[夜雀' -Encoding UTF8 | Select-Object -Last 1
# 5) 让用户复现 → 过滤日志
Select-String -Path '<游戏>\BepInEx\LogOutput.log' -Pattern '推荐框|厨具面板|异常' -Encoding UTF8 | Select-Object -Last 40
```

- 需要"等用户测完关游戏再读日志"时，用后台作业盯进程（`game.ps1` 或 pwsh 循环），别反复轮询。
- **不要替用户关游戏**；让用户自己退到桌面，再装。

### 11.2 用 Mono.Cecil 查游戏 API（非常有用）

```powershell
Add-Type -Path '<游戏>\BepInEx\core\Mono.Cecil.dll'
$a=[Mono.Cecil.AssemblyDefinition]::ReadAssembly('<游戏>\BepInEx\interop\Assembly-CSharp.dll')
$t=$a.MainModule.GetType('NightScene.UI.CookingUtility.WorkSceneCookingSelectionPannel')
$t.Fields  | ForEach-Object { "$($_.FieldType.Name) $($_.Name)" }
$t.Methods | Where-Object { $_.Name -notmatch 'get_|set_|k__' } | ForEach-Object { $_.Name }
```

类型全名要写对（面板是 `NightScene.UI.CookingUtility.WorkSceneCookingSelectionPannel`，注意 `Pannel` 拼写）。

---

## 12. 版本历史（问题 → 修法对照）

| 版本 | 解决/引入的问题 | 修法 |
|---|---|---|
| 0.3.x | 面板画不出、不能拖 | 放弃 GUILayout.Window，改 GUI.Box+BeginArea+手写拖动 |
| 0.4.x | 字体、滚动、常客开关、厨具旁提示 | 自绘分割线、TMP 字体、滚动视图 |
| 0.5.x | 厨具面板日志缺失 | `Probes.Install` 补齐两个 Patch 类 |
| 0.5.2 | 提示框挡料理区 | 挪到屏幕右侧 |
| 0.6.x | 推荐菜点击无效 | 改用 `UIButtonSimple` 组件调回调 |
| 0.7.x | 推荐菜不在面板里 → 点不到 | **注入 `m_RecipeInstances` + `UpdateRecipeField`** |
| 0.7.5-0.7.6 | `OnRecipeElementSelected` 无效 | 放弃该路径，改直填 `selectedIngredients` |
| 0.7.8 | 分割线透明 | 自建 1×1 纹理 |
| 1.0-1.1 | 操控太重 | F8 主面板 / F9 控制面板 |
| 1.2.x | 暂停连带冻 UI、光标冻结 | 收窄暂停守卫、只隐藏不冻结 |
| 1.3.x | 酒水点击错位 | `Extract(sellable)` 按对象取，不按列表索引 |
| 1.4.0-1.4.4 | 评分不准、三位一体无推荐 | 8 分制公式；特殊厨具读面板自带菜谱 |
| 1.4.5-1.4.6 | 离席客人残留 | `HaveNotLeft()` 过滤；按点单标签优先排序 |
| 1.4.7 | 离席客人仍残留（controller 不翻转） | 补丁挂 `MoveToSpawn/FlyToSpawn`，客人一走回门口就按指针标记"已离开" |
| 1.4.8 | 三位一体推荐不够"最符合"、未点单也推荐 | 特殊厨具复用普通厨具的 top4 推荐，只保留自己能做的；未点单不推 |
| 1.4.9 | 列表顺序与显示评价自相矛盾（下面粉评、上面绿评） | 覆盖 `Score` 之后必须按 `TotalPoints` 重排；**凡是"先排序、后改分"的代码，改完分一定要再排一次** |
| 1.4.9 | 三位一体推荐显示"空评级 + 合计0分" | 特殊厨具分支自己 `Find` 出来的候选没走 `ApplyRatings`，要显式补算；收藏评分逻辑为 `internal static ApplyRatingsToCandidates(菜, 酒)` 供两条路径共用 |
| 1.4.9 | 缺料提示 `缺食材 #123（有0/需2）` 看不懂 | 用 `DataBaseCore.RefIngredient(id).Text.Name` 换成食材名，并列出全部缺项 |
| 1.4.9 | 推荐框位置重启后丢失 | 三个窗口（主面板/料理框/酒水框）位置和大小统一写进 `[Layout]` 配置：拖动松手、按钮调整、退出时各存一次；启动时读回来再 `KeepOnScreen` 夹一次 |
| 1.4.9 | 试过「宽高按字号自动重算」 | **已回退**：会覆盖用户手动调过的宽高，两者冲突；要做得加「手动改过就锁住」的标记，当前未做（K4 仍开着） |
| 1.4.9 | 手改 `local.mystia.orderhelper.cfg` 后重启，改动被吞 | 因为 `OnDestroy` 会把运行时矩形再写回配置。**改配置必须先关游戏**；面板上的按钮和拖动没有这个问题 |
| 1.4.9 | 稀客需求 tag 出现 `-1`，当成标签 id 用会漏判 | 日志实证：`foodRequest=-1 且 DeskCode=-1` = 还没点单；已点单时 `beverageRequest=-1` = 酒水不限。判定改成「-1 不进 tag 匹配」，酒水按喜好推荐任意有货酒水 |
| 1.4.9 | 每 0.5s 刷一条 `Unable to cast ... IEnumerable\`1[int] to System.Collections.IEnumerable` | IL2CPP 的 `Il2CppSystem.Collections.Generic.IEnumerable<int>` 不能直接转 `System.Collections`。三段兜底：`TryCast<List<int>>` → `TryCast<Il2CppStructArray<int>>` → `GetEnumerator()` + `Cast<Il2CppSystem.Collections.IEnumerator>().MoveNext()` |
| 1.4.9 | 推荐显示「粉评」但排序乱七八糟 | 排序发生在 `ScoreOf` 阶段，之后 `Rate()` 又把 `Score` 覆盖成 95/80/60/40/10，顺序没跟着变。**凡是"先排序后改分"的代码，改完分必须再排一次**（现在按 `TotalPoints` 重排） |

---

## 13. 当前状态与可续做事项

**已稳定**：主面板（F8）、控制面板（F9）、「菜+酒」配对推荐与净分、食材直填、三位一体等特殊厨具推荐、流行显示、侧边栏拖动/滚动、已上菜与已离店客人过滤、三个窗口位置与大小可调 + 记忆。

**已知遗留**：
1. 菜格点击（`OnRecipeElementSelected` / `importedRecipe`）在普通厨具上时好时坏，当前用"直填食材"绕过；如要修，优先看 `MatchingSelected` 与 `UpdateOutputField` 的调用时机。
2. ~~推荐框位置不记忆（重启回默认位置）~~ v1.4.9 已修（`[Layout]` 段，三个窗口通用）。
3. 三位一体只显示料理块，酒水模块走独立面板。
4. 暂停只对"日记/料理台"面板守卫，其他面板可能仍能误触。
5. ~~稀客负 tag 语义未查清（K3）~~ 已查清：`-1` = 这张单没有这一项。**仍未做**事件驱动收集，还是 0.5s 轮询（K7）。
6. 字号变大后容器不跟着变宽（K4）：曾按字号重算宽高，与用户自定义宽高冲突，已回退。

**续作入口**：`HANDOFF.md` 有逐版时间线与文件清单；代码入口 `src\OrderHelper\OrderHelperPlugin.cs`（Load/配置）→ `OverlayBehaviour.cs`（全部 UI 与交互）→ `CookerPanelWatcher.cs`（面板挂钩/注入/直填）→ `GuestCollector.cs`（客人采集）→ `RecipeIndex.cs`（推荐算法）。

---

## 14. 新功能自检清单

- [ ] 新补丁类是否已加进 `Probes.Install()`，并有 `[Probe] 已挂补丁` 日志？
- [ ] 版本号三处是否同步（源码/`build.ps1`/文档）？
- [ ] 游戏退出后才 `-Install`？
- [ ] 所有游戏调用是否 try/catch 兜底？
- [ ] 日志里能否看到新功能的"每 5 秒状态行"，方便用户复现时定位？
- [ ] `.ps1` 是否保持 ASCII、`.cs` 是否 UTF-8？
- [ ] 推荐算法改动是否用 §8 的评分公式验证，且不把"点单标签不符"排进前列？
- [ ] 是否让用户实测（而不是只看日志下结论）？

---

## 15. 许可证与致谢

本插件仅供学习交流，不修改游戏资源文件，不涉及破解。感谢 BepInEx、IL2CPPInterop、HarmonyLib 社区，以及 MetaMystia 项目提供的联机环境。
