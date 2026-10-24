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
5. 日志是唯一可靠信号：`<游戏目录>\BepInEx\LogOutput.log`（**读法见 §11.3，别手工整读**）。

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
| 用到 `EncodeToPNG` 编译不过 | CS1061 `Texture2D` 没有这个方法 | 它在 `UnityEngine.ImageConversionModule.dll`，要加进 `build.ps1` 的 modules 列表 |
| PowerShell `Add-Type` 被拦 | `Command blocked for security: Add-Type compiles and loads .NET code at runtime` | **本机跑不了 Mono.Cecil 那条命令（§11.2）**；改用二进制字符串搜索看签名，或换 dnSpy/ILSpy |
| Bash 里调 `powershell` 被拒 | `Invoking PowerShell from Bash bypasses PowerShell security checks` | 用 PowerShell 工具直接跑，不要在 Bash 里套一层 |

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
| 弹窗「做好了但永远不出现」 | 写了绘制代码、日志也正常，屏幕上看不到 | **检查它画在 `OnGUI` 的哪个位置**。本项目 `OnGUI` 开头有一句「没有任何面板就 `return`」，面板默认不显示，画在这句之后的任何东西都不会被执行。一次性提示（版本号/公告）必须放在早退之前（v1.5.4 的 `VersionToast.Draw()`） |
| 淡入淡出/倒计时卡住不动 | 提示一直挂在屏幕上不淡出，或卡在半透明 | **计时用 `Time.realtimeSinceStartup`，别用 `Time.time`**。打开厨具/酒水面板时插件会把 `timeScale` 设成 0，`Time.time` 会冻住（v1.5.4 踩过） |
| 中文字体变方块 | 默认字体无中文 | 从 `TMP_Settings.defaultFontAsset.sourceFontFile` 取字体（本游戏是 **JYHPHZ**），兜底 `Microsoft YaHei` |
| 字号改不动 | `GUI.skin.fontSize` 无效 | 每个 `GUIStyle` 都设 `fontSize`，或用 `GUI.skin.font = font` + 统一 `GUIStyle` 实例 |
| 鼠标滚轮没反应 | 列表滚动不动 | `GUI.BeginScrollView` 自带滚轮；若被游戏吃掉，读 `Input.mouseScrollDelta.y` 手动加 |
| 点击穿透/被挡 | 点不到按钮 | 面板画在 `GUI.depth = -100`；不要禁用整个 EventSystem（会冻结光标，见 §9） |
| 面板位置漂移 | 换分辨率后出屏 | 每帧 `ClampRectToScreen()` |

### 4.1 面板布局约定（本项目）

- 主面板：`ShowNormalGuests` 开关、`字体 -/+`、`控制面板(F9)`、`一键制作` 状态、今日流行、每位客人一段。
- 推荐框：料理在左、酒水在右（默认叠在屏幕右侧，可拖动）；特殊厨具（三位一体）只显示料理块。
- 每隔 5 秒把 `推荐框客人` 状态写日志（名字/rem/all/点单状态），排查"客人不刷新"用。

### 4.2 性能：每帧分配是 IMGUI 插件的隐形税

**IMGUI 的 `OnGUI` 每帧会被调两次**（`EventType.Layout` 和 `EventType.Repaint`）。所以 `OnGUI` 里写一句字符串插值，实际是每帧算两遍。本插件在营业场景会自动 `Show()`，面板基本全程开着 —— 这笔税是全程在付的。

实测（1.8 MB 日志、414 个 300 帧窗口）：营业中位 **150 帧/秒**，但有 15 个窗口掉到 40 帧以下，最慢 23 帧。日志量、MetaMystia 刷屏都不是原因（慢窗口的日志行数反而更少），原因在每帧的分配：

| 反例（v1.5.0 及以前） | 每帧成本 |
|---|---|
| `GUILayout.Label($"◆ {guest.Name}（{guest.Kind}）桌…")` | 6 位客人 × 2 遍 = 12 个字符串 |
| `GUILayout.Label("今日流行｜菜：" + PopularInfo.FoodText)` | `FoodText` 是属性，每次访问 `new StringBuilder` + 一串字符串 |
| `matched.FindAll(d => d.TotalPoints >= 4)` | 每位稀客每帧一个 List |
| `var drinkable = new List<DishCandidate>()` + `AddRange` | 每位客人每帧一个 List |
| `dish.ButtonLabel + "\n" + dish.DetailText` | 每道菜每帧一个字符串（6 人 × 4 道 × 2 遍 ≈ 48 个） |

**修法（v1.5.1）：数据 0.5 秒才变，文本就跟着数据生成一次。**

- `GuestView.BuildLines()`：在 `Collect` 里把标题行、点菜行、点酒行、推荐框标题、无酒水行全部定稿，`OnGUI` 只 `Label(字段)`。
- `DishCandidate.FullLabel`：`ButtonLabel + "\n" + DetailText` 在 `PrepareLabels` 里一次拼好。
- `PopularInfo`：`FoodText`/`BevText`/`BothText`/`FoodLine`/`BevLine` 全在 `Refresh()` 里算，属性只返回值。
- 筛选项（粉评过滤、可推酒水）挪到 `MatchesFor` 的缓存和 `ApplyRatings` 里，`OnGUI` 不再 `FindAll`/`new List`。
- 标题类字符串（窗口标题、料理框标题）按"内容不变就不重建"缓存。

**两条测量经验**（值得沿用）：

1. **心跳只能算平均值，会骗人**。日志里 `心跳 frame=N` 每 300 帧一条，配上 MetaMystia 的时间戳就能反推帧率 —— 但它给的是"这 300 帧的平均"，**分不清"一次 10 秒长卡"和"持续 12 秒 25 帧"**。要分清必须记**单帧耗时**：v1.5.1 的 `Perf.BeginFrame` 就干这个（>150ms 记一条长帧，每分钟最多 20 条）。
2. **先排除"不是我的锅"**。白天场景的慢帧全在 MetaMystia `SwapMap` / `GetMerchantData`（换图加载），跟本插件无关；营业中掉帧才有嫌疑。**按场景分组统计**（`guests=0` / `guests>0`、`visible=True/False`）比看总平均有用得多。

### 4.3 第二层税：IL2CPP 属性访问（v1.5.2）

v1.5.1 把每帧分配清掉之后，帧率稳在 **173–179 帧/秒、0 条长帧**（长帧全部 `guests=0`，是加载期），卡顿基本消失。但探针把第二个热点照出来了：**采集**便宜得反常地贵 —— 而且成本严格随客人数线性增长：

| 在场客人 | 采集 平均 | 采集 峰值 |
|---|---|---|
| 0 人 | 0.02ms | 0.1ms |
| 1 人 | 1.78ms | 20.1ms |
| 2 人 | 5.97ms | 73.7ms |
| 3 人 | 15.4ms | 101.0ms（全局峰值） |
| 4–5 人 | 23–37ms | 95.4ms |

每位客人每 0.5 秒约 **7ms**。对比：绘制每 30 秒 1.1 万次调用、平均 0.1ms —— 说明"画"已经不值钱了，"算"才是。

**根因不是算法，是 interop 取值次数**。`RecipeIndex.Find` 第一段要遍历整张已解锁食谱表（几百条），每条菜谱原本要做：

- `DataBaseCore.Recipes.TryGetValue` + `DataBaseCore.Foods.TryGetValue` —— 2 次穿进 IL2CPP 的字典查询；
- `food.Tags` —— 返回一个数组，**每次访问都 marshal + 分配**；而它被调用了 `1 + 喜好数 + 厌恶数` 次（请求标签一次、`CountTags` 里每个喜好/厌恶标签各一次）；
- `food.Text.Name` —— 字符串 marshal，每条菜谱一次。

饮品那边更糟：每次调用都 `GetEnumerator()` 遍历整个 IL2CPP 字典，每个 `MoveNext` 都要把 `KeyValuePair` marshal 出来。

**修法：静态数据按 id 复制成纯 C# 副本，热路径上一次 IL2CPP 都不进。**

| 缓存 | 内容 | 失效时机 |
|---|---|---|
| `FoodCache[id]` | `Name` + `Tags`（复制成 `int[]`） | 场景切换 |
| `RecipeCache[recipeId]` | `Recipe`、`FoodId`、`Food`、`Ingredients`（复制成 `int[]`）、`Cooker` 中文名 | 场景切换 |
| `BevCache[id]` / `_bevIds` | 同上 + 整份饮品 id 表 | 场景切换 |
| `_recipeIds` | 已解锁菜谱 id 表（数量变了才重建） | 数量变化 / 场景切换 |
| `IngredientNameCache[id]` | 食材显示名 | 场景切换 |

改完之后第一段循环是纯 C#：字典命中 → `int[]` 标签比较 → 拼候选，第二段的"可做性"判定也用缓存的 `int[]` 食材表，只有查库存（`RunTimeStorage.Ingredients`）还进 IL2CPP，而它只对入围的 ≤24 条做。

**两个必须记住的坑：**

1. **缓存了 wrapper 就必须能失效**。`Recipe` / `Sellable` 是 IL2CPP 对象的托管壳，缓存住等于延长它的生命周期；换场景后数据表可能重建、指针可能被复用，**不清就是野指针**。所以 `RecipeIndex.Invalidate()` 挂在 `OnSceneLoaded` 里，和 `GuestCollector.ClearLeft()` 挨着。
2. **"静态"要能证明**。菜名、标签、食材、厨具类型在整局里不变，才可以缓存；库存、客人状态、面板内容每帧都可能变，**不能**缓存。判断标准：这个东西在游戏里有没有"会改它"的入口。

> **实测结论（2026-10-23，用户跑完整一局 + 截图）**：改动有效。整局 fps 稳定 **171–179**；采集峰值 **15.1ms 只出现一次**（首位客人进店时的冷缓存），此后 **7–11ms**，全部低于 16.7ms 的 60 帧预算；绘制均值 0.1ms。全程 4 条长帧里 3 条在启动期，游玩中仅 1 条 160ms（`guests=3`，大概率是游戏自身开销）。**卡顿问题确认已解决**。

### 4.4 怎么读性能探针：`tools/perf-report.py`

探针只写两类行，`perf-report.py` 把它们排成窗口表：

```text
性能30s | 长帧累计 3 | frame=88200 | 采集 60次 均3.10ms 峰18.0ms | 绘制 9000次 均0.42ms 峰9.8ms
长帧 312ms frame=61300 guests=6
```

- **窗口表**：每个「性能30s」一行，`≈fps` 用相邻两窗口的帧差 / 30 估算（dump 间隔约 30 秒，有轻微漂移，所以标 ≈）。
- **长帧表**：按毫秒排序，并按 `guests` 分组 —— 全在 `guests=0` 就是加载期，跟插件无关。
- **峰值才是判据**：单帧 16.7ms 是 60 帧的预算。平均值低而峰值高 = "偶尔顿一下"；两者都高 = "持续低帧"。这两种的手感完全不同，修法也不同。
- 先跑 `--selftest`（11 项）。它专门验证"没有窗口时不瞎编"——拿旧日志跑必须老实说没数据。

### 4.5 从游戏里取 UI 素材（v1.5.3 皮肤 / 头像）

**目标**：把 IMGUI 的灰盒换成游戏自己的和风纸条质感、给稀客贴真立绘，**且不打包任何美术资源**——素材全部运行时从场景里抓。

先看 `docs\UI侦察报告.md`（素材清单与样例图）和 `docs\UI美化开发计划.md`（批次与进度）。这里只记**踩过的坑**。

**三条设计纪律（写新取图代码前先读）**：

1. **贴图必须是自己的副本**。不持有游戏的 `Sprite` / `Texture` / `Image`，而是把图集里那一小块**拷成我们自己的 `Texture2D`**。好处有二：跨场景不失效（§4.3 坑 1 的野指针规则对自持有对象不适用，也就**不用挂 `OnSceneLoaded` 清缓存**），并且**绕开了 `DrawTextureWithTexCoords` 的 UV 翻转**（图集切片按 `textureRect` 算 UV，y 轴原点在左下，最容易画倒）。
2. **一次扫描、全程缓存；拿不到才重试，且有次数上限**。枚举全场 `Image` 是几千个对象，绝不留进每帧路径。`UiSkin` 上限 6 次、`GuestPortraits` 上限 20 次（3 秒节流），到点就停并写一条日志说明缺什么。
3. **取不到就降级，不许静默**。皮肤取不到 → 退回灰盒；头像取不到 → 退回首字占位。两种情况都要在日志里点名缺哪个素材。

**坑清单（按被坑的顺序）**：

| 坑 | 现象 | 修法 |
|---|---|---|
| `Texture2D.EncodeToPNG` 编译不过（CS1061） | 新版 Unity 把它移出去了 | 它在 **`UnityEngine.ImageConversionModule.dll`**，`build.ps1` 的 modules 里必须加上这个引用 |
| `RenderTexture.GetTemporary` 5 参重载不存在 | CS1501 | 用最稳的 3 参版 `GetTemporary(w, h, 0)` |
| 按名字"回头再找"导不出图 | `sp.texture` 为 null / 宽高为 0 | **必须边扫边导**：扫到 `Image` 当场取 `sprite.texture` 拷副本。按名字回找会命中已失活的同名实例，它的图集还没加载 |
| 图集贴图关了 Read/Write | `GetPixels` 抛异常 | 先 `Graphics.Blit` 到 `RenderTexture`，`ReadPixels` 拿可读副本，再按 `textureRect` 切片 |
| **IMGUI 不缩放 `GUIContent` 里的图片** | 64×64 立绘塞进 48px 按钮 → 溢出去盖住旁边的文字 | 在拷贝阶段就用 GPU 缩到显示尺寸（Blit 到小 RT 再读回），绘制时按原始尺寸画正好铺满 |
| il2cpp 的 `GUIContent` 只剩 `string` 构造重载 | 传 `Texture` 编译不过（CS1503） | `var gc = new GUIContent(); gc.image = tex;` |
| il2cpp 的 `RectOffset` 没有 4 参构造 | CS1729 | `new RectOffset()` 再逐字段赋值（默认全 0，正好适合"不要 padding"的场景） |
| 九宫格 border 顺序写反 | 边角拉伸变形 | sprite 的 `border = (left, bottom, right, top)`，`RectOffset` 是 **(left, right, top, bottom)** —— 别照抄 |
| 两行文本塞进固定高度按钮 | **稀客名字显示一半** | IMGUI 超出部分直接裁掉，不会撑开。拆成两条独立文本、不设固定高度（见下） |
| `UiProbe` 默认开着 | 正式版每次开局全场景扫一遍并写文件，很重 | 配置默认值改 **false**；F10 手动触发保留 |

**"字显示不全"这类问题的通用排查法**：IMGUI 里凡是"给控件设了固定 `Height`"，内容超出就**静默裁剪**，不会报错也不撑开。看到文字缺一半，先查是不是固定高度 + 多行/长文本的组合。修法是拆行 + 让布局自己撑高 + `wordWrap = true`。

**凡是"靠名字认对象"的功能，必须留一条用户可改的兜底（v1.5.3 第三轮踩到）**：

- 症状：绝大多数角色能自动对上头像，唯独茨木华扇一直占位。原因是匹配靠「立绘内部名里含显示名的字」，
  而游戏内部命名不一定守规矩——实测见过带编号前缀的 `11_咲夜Main_0, 1`，也有日文异体（`華`/`覚`/`霧`）。
  这类名字无论加多少别名都猜不全，**而且是"永远差几个"的状态**。
- 错误方向：让用户截图、由 AI 看图认人。截图里**没有内部名**，认出来也没法挂到自动抓取上；
  真要走这条路只能整张图当头像用，等于放弃"运行时零资源"的前提。
- 正确做法：**分两层**——① 自动匹配跑默认路径；② 暴露一张 `config\xxx-portraits.txt` 映射表
  （`内部名 = 显示名`，Load 时读一次，查询顺序里排在模糊匹配之前），改一行存盘重开即生效。
  再加一个 `F11 导出内部名清单`（标 `[有]/[缺]`），用户看到 `[缺] 華扇` 就知道填什么。
- 通用结论：**"猜"的逻辑一定要能被"填"覆盖**，并且诊断工具要能把猜不到的那部分**原样吐出来**。
  这样每遇到一个新角色，成本是"改一行配置"，而不是"改代码重新发版"。

**IMGUI 和游戏 uGUI 是两套互不相干的输入系统（v1.5.3 第四轮踩到）**：

- 症状：点我们面板上的按钮，游戏同时也打开了厨具/酒水/垃圾箱——**一次点击被两边都收到**。
- 原因：IMGUI（`OnGUI`）活在 Unity 的**旧版 GUI 事件流**里，对 `EventSystem` / `GraphicRaycaster`
  **完全不可见**。游戏的 uGUI 按钮在同一帧照常接收同一次鼠标按下，IMGUI 没有任何 API 能把事件"吃掉"
  （`Event.Use()` 只影响 IMGUI 自己的事件队列）。
- 解法：**在游戏 uGUI 之上盖一块跟着鼠标走的透明 `Image`，只在我们面板矩形内把 `raycastTarget` 打开**。
  它挡住 `GraphicRaycaster` 的射线 → `EventSystem` 认为指针在这块图上 → 游戏按钮收不到点击；
  而我们自己的 IMGUI 按钮不走 EventSystem，照常响应。指针一离开面板就关掉，游戏恢复正常点击。
- 实现要点：
  - 独立 `Canvas`（`ScreenSpaceOverlay` + `sortingOrder = 32000`）+ **必须加 `GraphicRaycaster`**，否则射线根本看不到它。
  - 挡板只要 8×8，每帧 `rt.position = Input.mousePosition`（overlay canvas 下就是屏幕像素、左下原点）。
  - `Image` 的 color 用全透明 `(0,0,0,0)` 也能挡——`Image.Raycast` 只看 rect 和 `raycastTarget`，
    颜色 alpha 只在设了 `alphaHitTestMinimumThreshold > 0` 时才参与。
  - 面板矩形由 `OnGUI` 每帧发布（`BeginFrame` → 画到哪块 `SetRect` 哪块），`Update` 用上一帧的值。
  - 配一个开关 `[Behaviour] ClickGuard`，出事能一键退回"会误触但能用"。
- 局限：只挡 **uGUI**。如果游戏的点击走 `Physics2D.OverlapPoint` 之类的世界射线，这条路不通——
  所以要在挡住时打一条诊断日志，能立刻分清「没挡住」还是「挡住游戏走的是另一条通道」。

**⚠️ 不要在 `OnPanelOpen` 上 `Prefix return false`（2026-10-23 20:40 事故，本项目已整体放弃防误触）**

- 先说明为什么试过：挡板日志打了 `点击防误触：指针在面板内已遮挡` 而**厨具面板照样打开**，
  说明挡板本身工作正常，但游戏根本没走 `GraphicRaycaster`——它在别处自己判断了这次点击。
  所以"盖一层透明 UI"只对**真正用 EventSystem 的控件**有效，对自写点击逻辑的游戏无解。
- 于是改成在 `WorkSceneCookingSelectionPannel.OnPanelOpen` / `WorkSceneStoragePannel.OnPanelOpen`
  上加 `HarmonyPrefix`，命中就 `return false`。**看起来干净，实际上炸了。**
- **根因：Prefix 返回 false 只跳过「原方法体」，Postfix 仍然会执行。**
  于是 Postfix 里的 `CookerPanelWatcher.SetOpen(__instance)` 拿到的 `__instance` 是**没被初始化过的对象**：
  - 日志证据：`cooker='Lorem ipsum is'`（Unity 默认占位文本）、`rect=(0,0,2560,1440)`、`special=True`；
  - 插件据此认为"面板已打开"，连锁执行 暂停 InputSystem 动作 / 禁止角色移动 / `timeScale=0` / 打开控制面板；
  - 玩家侧表现就是**游戏卡死、点不动、面板乱弹**。
- **通用规则：Prefix 拦下一个方法时，任何"依赖该方法已经跑完"的 Postfix、状态登记、事件转发都必须一起跳过。**
  正确写法是在 Prefix 里设一个 `__runOriginal` 之外的静态标记，让 Postfix 先查标记直接 return；
  或者干脆别在这个入口拦（本项目最终选择：功能不做）。
- 另一条同样重要：**"优雅地阻止"比"先让它发生再逆转"危险得多**，因为逆转代码面对的是半初始化状态。
  想拦截时优先选"根本不让它进入流程"，但前提是**该对象的后续登记也一起不执行**。
- 教训：**诊断日志要能区分"机制没生效"和"机制生效了但方向错"**。
  这次因为先写了"已遮挡"日志，才能一眼判定不是挡板坏了，而是通道不对；也是因为留下了
  `cooker=...` 的实例转储日志，才能在事故后立刻定位到"Postfix 拿到了半初始化实例"。

**"未点单的客人"要从列表里剔掉，而不是标个占位文本（v1.5.3 第五轮）**：

- 原来的做法是给未点单的人 `Food = "（尚未点单）"` 照样列出来，结果面板上挂着一堆没有单的客人，
  既占位置，又容易被当成"有菜可做"。
- 正确边界：面板/推荐框只显示 **`!Served && !NotOrderedYet`** 的客人（已上完菜的和还没下单的都不算）。
- 连带要改的是**所有用 `_guests.Count` 做空判断的地方**——空态文案、推荐框的早退、
  头像条的数据源。只改过滤不改这些，会出现"列表空了但仍然画一屏推荐框"或"头像条还显示没单的人"。
- 空态文案要分两种：一个客人都没有 vs 有客人但都没点单。前者是环境问题，后者是正常等待，
  合并成一句话会让玩家以为插件坏了。

**给玩家一个「放图就生效」的目录，往往比任何自动识别都可靠（v1.5.3）**：

- 自动匹配再聪明也是在**猜**：猜名字、猜取景、猜哪两个图层该叠。玩家一眼就知道那是谁，
  让他自己放一张图，成本是「截一张图 + 改个文件名」，收益是**零歧义**。
- 设计要点：目录固定（`config\<mod>-portraits\`）、**文件名就是显示名**（不要让玩家去查内部 ID）、
  自动生成 `说明.txt`、任意尺寸自动裁方缩放到目标大小、**查询优先级排在所有自动逻辑之前**。
- 什么时候值得做：① 自动识别的失败率不低且无法收敛；② 素材本身是游戏里现成可见的画面（截得到）；
  ③ 玩家愿意为「准确」付出一点手工。三条都成立时，这个目录比继续调匹配算法划算得多。

**取图的两个性能细节**：

- **图集整图副本按实例 ID 缓存**（`GuestPortraits`）：2048² 的 Blit + ReadPixels 挺贵，同一张图集只做一次，之后所有切片从副本里 `GetPixels`，几乎免费。
- **拷贝贴图要 `hideFlags = HideFlags.HideAndDontSave`**，否则场景切换时会被回收。

**外部素材的"名字对不上"必须有兜底，否则整批图等于白放（v1.5.3 第五轮）**：

- 症状：官方素材包（游戏根目录自带 `东方夜雀食堂 头像&表情包`）里 800 多张图**文件名只有编号**
  （`本体/33-01.png`），一个角色名都没有；就算我认出来是谁，只要写成「茨木华扇」而面板显示的是「华扇」
  或「茨木華扇」，`File.Exists` 直接不命中，玩家只会看到「放了图没反应」。
- 解法：**外部目录的查表做三级**——① 精确 → ② 繁简/异体归一（`華→华`）→ ③ **双向包含**
  （`咲夜.png` 命中 `十六夜咲夜`，`茨木华扇.png` 命中 `华扇`）。三者都在读盘那一次建好的字典上做，
  零额外 IO；命中后写回缓存，下次 O(1)。
- 通用结论：**玩家提供的资源，命名权在玩家手里，代码只能去适配**。任何"文件名即主键"的设计，
  都要假设键会差一两个字（全名/简称/繁体/带后缀），否则等于把正确性押在玩家猜得中、以及我们的译名和游戏一致上。
- 顺带：素材包批处理走脚本（`tools\import_portraits.py`），**抠背景用四角泛洪**（取边缘最常见色 + 容差 26，
  粉底/绿底/蓝底都能干净分离），裁到不透明内容外框再补方形，最后缩到 128——比给玩家一堆带底色块的图强。
  另配 `preview_portraits.py` 出效果图、`make_todo_sheets.py` 出「还没认出来」的编号图，
  让"识别"这件纯人工的事变成**照图填表**。

**降级开关**：`[Display] VanillaSkin = true`（默认）。关掉即完全回到 v1.5.2 的灰盒外观——**任何换皮改动都必须能被这个开关一键还原**，这是接进正式版的前提。

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

// 加料 / 定价（v1.5.5 用到，都靠 tools\dump-api.ps1 查出来的）
TradableObjectBase.Level / BaseValue / TrueValue / Tags / RawTags   // Ingredient 与 Sellable 都继承它
Ingredient.Tags           // 食材标签（int[]），加料时并入菜品标签参与评价
Sellable.Tags / AdditiveTags / Modifier / Altered / HasModifier     // AdditiveTags 就是「加料带来的标签」
Sellable.AddToTags(int tag)                                        // 游戏加料时逐个并标签，重复 tag 只算一次
Cooker.AdditiveNextOrderProb / GetExtraAdditiveNextOrderProb(Sellable)  // 加料影响的是「续单概率」，不是售价
Cooker.OnEvaluate(int, int, GuestGroupController, Sellable) → int   // 游戏自己的评价入口

// 防黑暗料理（v1.5.7）
Sellable.BanTags          // int[]，这道菜「禁止出现的 tag」。加料引入 BanTags 里的 tag 会做废
                          //   ⚠ 语义是**按字段名推断**的，基础数据里可能普遍为空；判断依据见下
Sellable.AdditiveTags     // List<int>，**运行时**「加料之后实际获得的 tag」，基础数据通常是空
                          //   ⚠ 千万不要当「可加料白名单」用，会把正常加料全砍掉（v1.5.7 初稿踩过）
DataBaseCore.TagRules     // Dictionary<int, int[]>，tag 规则/互斥表——最可能是黑暗料理的底层依据。
                          //   目前只 dump 不用（语义未证实），格式：key=tag，value=与该 tag 相关的一组 tag
Sellable 构造签名          // (id, baseValue, level, tags, banTags, type, additiveTags, isCollab)
                          //   banTags 是构造参数之一 → 它是**基础数据**，不是运行时算出来的
MatchedCookCombo          // (Recipe recipe, int[] modifiers) + GetResult(Cooker) → Sellable
                          //   modifiers = 加料 id；GetResult 是「给定这锅料，游戏认为产出什么」的唯一入口，
                          //   v1.5.7 仍未调用（需要真实 Cooker 且可能读全局状态），先用数据表复刻判定

// 还没搞清、暂时别碰：
//   WorkSceneCookingSelectionPannel.MatchedCookCombo.Modifiers（数组）语义不明
//   —— 不知道它装的是食材 id 还是价值，所以 v1.5.5 **没有**调用 GetResult，
//   而是自己按 Ingredient.Tags 复刻标签合并。要接这个方法必须先做一次实机采样验证。
//   DataBaseCore.TagRules 同理：结构清楚（Dictionary<int,int[]>），但「value 是互斥 tag 还是继承 tag」
//   没有证据，v1.5.7 只 dump 不打判定。**不确定的规则宁可不接，误杀比漏判更难被发现。**

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

### 8.3 「客人没出现在推荐框」的三道闸门

推荐算错是一回事，**客人根本不在列表里**是另一回事，排查方向完全不同。`GuestCollector.Collect` 里只有三道过滤，按顺序：

1. **类型闸门**：`controller.TryCast<SpecialGuestsController>() != null` 才算稀客；否则当常客，`ShowNormalGuests=false` 时直接 `continue`。
2. **在场闸门**：`controller.HaveNotLeft()` 为假 → 跳过。
3. **离店闸门**：`MarkLeft` 在 `GuestGroupController.MoveToSpawn` / `FlyToSpawn` 的 Postfix 里记下指针，进过黑名单就永不再显示。

事故（v1.5.2 修）：**露易兹的符卡「传奇的格列佛」**——她召唤魔法相机，把"没吃饱且预算没花完"的稀客拍成「旅者博客」，当日结束后**投影出这位客人的「稀客影像」**再次光临（喜好与原客一致、可用符卡、预算与续单沿用离店时的）。这种影像很可能是复用同一个 `GuestGroupController`，而本尊离店时已经 `MarkLeft` 过 → **影像被第 3 道闸门永久挡住**，面板里完全没有这个人。而且 `Left` 是 `HashSet<IntPtr>` 且**从不清空**，IL2CPP 指针一旦被复用，还会误伤无关的新客人。

修法三条，缺一不可：

- 黑名单从 `HashSet<IntPtr>` 改成 `Dictionary<IntPtr, (时间, 订单数)>`；
- **可撤销**：控制器当前 `AllOrdersCount` 与记下时不同 = 这是新的一次到店（影像回来了），撤销标记并写一条巡查日志；
- **场景切换清空**（`ClearLeft()` 挂在 `OnSceneLoaded`），并给标记加 10 分钟 TTL，防止残留。

另外新增**客人巡查**（`[Diagnostics] GuestAudit`，默认开，每 5 秒一次，内容变了才写）：

```text
[OrderHelper] 巡查 控制器 2 个（ShowNormalGuests=False）
[OrderHelper] 巡查 上白泽慧音|SpecialGuestsController|稀客=是|在等|桌3|剩4/1|在店=是|黑名单=否
```

它一次回答"场上到底有几个控制器、各自什么类型、被哪道闸门挡住"。**遇到"看不到某位客人"先看这段，不要先怀疑推荐算法。**

教训：**排除法要有闸门清单**。三道过滤都写在同一个 `while` 循环里，任何一道命中都是静默 `continue`，界面上完全看不出来。凡是"静默跳过"，就必须有一条日志能说出"跳过了谁、为什么"。

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
& powershell -NoProfile -ExecutionPolicy Bypass -File scripts\game.ps1 start
# 4) 验证版本
Select-String -Path '<游戏>\BepInEx\LogOutput.log' -Pattern 'Loading \[夜雀' -Encoding UTF8 | Select-Object -Last 1
# 5) 让用户复现 → 过滤日志
Select-String -Path '<游戏>\BepInEx\LogOutput.log' -Pattern '推荐框|厨具面板|异常' -Encoding UTF8 | Select-Object -Last 40
```

- 需要"等用户测完关游戏再读日志"时，用后台作业盯进程（`scripts\game.ps1` 或 pwsh 循环），别反复轮询。
- **不要替用户关游戏**；让用户自己退到桌面，再装。
- 让用户复现之前，先挂上日志哨兵（§11.3），复现完直接看"新增了什么"，不用在海量旧日志里翻。

### 11.2 用 Mono.Cecil 查游戏 API（非常有用）

> **v1.5.5 起不用手搓了**：项目里已经有 `tools\dump-api.ps1`（编译 `tools\ApiDump.cs` 成独立 exe 再跑，
> 绕开 `Add-Type` 的安全拦截）。三种用法：
>
> ```powershell
> .\tools\dump-api.ps1 type GameData.Core.Collections.Ingredient     # 列字段/属性/方法
> .\tools\dump-api.ps1 find Additive ExtraIngredient 加料             # 按类型名/成员名找
> .\tools\dump-api.ps1 enum CookerType                               # 列枚举值
> ```
>
> 查别的程序集加 `-Assembly xxx.dll`（默认 `Assembly-CSharp.dll`）。它只读，不改游戏文件。

老办法（知道要写什么时也行）：

```powershell
Add-Type -Path '<游戏>\BepInEx\core\Mono.Cecil.dll'
$a=[Mono.Cecil.AssemblyDefinition]::ReadAssembly('<游戏>\BepInEx\interop\Assembly-CSharp.dll')
$t=$a.MainModule.GetType('NightScene.UI.CookingUtility.WorkSceneCookingSelectionPannel')
$t.Fields  | ForEach-Object { "$($_.FieldType.Name) $($_.Name)" }
$t.Methods | Where-Object { $_.Name -notmatch 'get_|set_|k__' } | ForEach-Object { $_.Name }
```

类型全名要写对（面板是 `NightScene.UI.CookingUtility.WorkSceneCookingSelectionPannel`，注意 `Pannel` 拼写）。

> **本机跑不了这条**：`Add-Type` 会被安全策略拦（`Add-Type compiles and loads .NET code at runtime`）。
> 替代方案（v1.5.3 用过，够用）：**对 interop 的 `Assembly-CSharp.dll` 做二进制字符串搜索**，能拿到方法签名与字段名，但**拿不到归属类**：
>
> ```bash
> python -c "
> import re
> data=open('<游戏>\\\\BepInEx\\\\interop\\\\Assembly-CSharp.dll','rb').read()
> strs={m.group().decode('ascii') for m in re.finditer(rb'[ -~]{6,140}', data)}
> for s in sorted(x for x in strs if '关键词' in x)[:40]: print(s)
> "
> ```
>
> 签名里的 `NativeMethodInfoPtr_XXX_Public_Static_返回类型_参数类型_0` 能读出完整签名（例如 `GetSellabeBGSprite_Public_Static_Sprite_Sellable_0` = `static Sprite GetSellabeBGSprite(Sellable)`）。要知道它挂在哪个类上，只能换 dnSpy / ILSpy，或让用户在自己的 PowerShell 里跑上面那条 Mono.Cecil 命令。

### 11.3 日志哨兵：只读新增行（每轮排查先挂它）

`LogOutput.log` 玩一轮就 **1.8 MB** 以上（MetaMystia 联机插件会刷屏）。手工 `Select-String` / `tail` 有四个坑，**每一个都会让你得出错误结论**：

| 坑 | 现象 | 后果 |
|---|---|---|
| 每次重读全文件 | 输出里混着上一轮的行 | 以为"这次复现了"，其实是旧记录 |
| `tail -f` 盯文件末尾 | 日志被清空/轮转后就停住 | 哨兵看着在跑，其实早就瞎了 |
| 边写边抓 | 抓到半行 `直填食材 选中=` | 关键词匹配不上 → 漏报，看起来像"功能没跑" |
| 同一句异常刷几百遍 | 954 条一模一样的行 | 淹掉真正的 3 条新问题 |

工具：**`tools/watch-log.py`**（Python 3，只读日志；不碰游戏进程、不写游戏目录）。

```bash
python tools/watch-log.py                         # 挂上，只看新增行（默认 120 分钟）
python tools/watch-log.py --minutes 30
python tools/watch-log.py --scan                  # 复盘：扫一遍全文就退出
python tools/watch-log.py --exclude MetaMystia    # 屏蔽联机插件的刷屏
python tools/watch-log.py --selftest              # 自检 6 项，全过再用它的结论
```

四条机制——**换游戏、换语言重写时，必须保留的正是这四条**：

1. **基线**：启动时 `seek(0, SEEK_END)` 记住字节位置，之前的行不算"新的"。
2. **增量**：每轮从上次的字节位置继续读，`位置 = f.tell()`，不重读旧内容。
3. **半行缓冲**：按字节读、`rfind(b"\n")` 切；最后一段没有换行就留到下一轮再判。**按字节切、再整体 decode**，中文才不会被劈成半个字。
4. **轮转检测**：文件比记住的位置还小 = 被清空/轮转 → 位置拨回 0，否则永远读到空、就此卡死。

关键词配置（本项目的默认值，**换游戏必须整套换掉**）：

| 关键词 | 为什么要它 |
|---|---|
| `[Warning` `[Error` `[Fatal` | BepInEx 与 interop 的告警 / 异常 |
| `挂补丁` | 启动时能数出补丁数：`已挂补丁` N 条，以及 `挂补丁失败` |
| `失败` `异常` | 插件自己打的中文失败信息 |
| `直填食材` | 一键制作的成功信号：`选中=N 可制作组合=M` |
| `推荐框客人` | 每 5 秒的状态行，判断"客人不刷新" |

**反面教训**：不要把 `OrderHelper` 这种"来源名"写进关键词。它每 5 秒一条心跳，实测 1.8 MB 日志里命中 **6978 条**，等于没过滤。**抓信号，不抓来源。**

**签名归一比"去重"管用**：同一句异常的时间戳不同，`set` 去重压不掉。把 `23:05:38.411`、`0x2AD1F3489A0`、`frame=206359` 换成占位符再统计——实测 1025 条命中压成 **66 类**，其中 `PeekOrders threw ... Stack empty` 一类就占 954 条。**看到"几百条错误"先归一，再看是一件事还是几百件事。**

**它抓到的第一个真问题**（2026-10-23 首次上机）：

```text
×954   [Error:MetaMystia] [<时间>] [TraceLog] └──✕─ PeekOrders threw
       Il2CppException: System.InvalidOperationException: Stack empty.
```

客人还没点单时 `PeekOrders()` 会抛 `Stack empty`。本项目 `GuestCollector.cs:175` 每 0.5 秒对每位在场客人调一次，`catch` 掉了所以界面上看不出来（`view.Food` 随后被覆写成"（尚未点单）"），但 MetaMystia 的 trace 补丁把每一次抛出都记成 `[Error]` —— 手工 grep 时这 954 行混在 1.8 MB 里根本不会有人注意到。**"是否由本项目的轮询触发"尚未单独验证**（MetaMystia 自己也在调 `PeekOrders`），要动它先做 A/B：停掉轮询再看条数是否归零。

### 11.4 用哨兵时必须守的两条纪律

1. **先自检判据，再用判据下结论。** "按关键词过滤"这种判据本身也会错（踩过的例子：`in` 判断写错，报"失败"的样子和"数据错"一模一样）。`--selftest` 就是为此存在的：基线不误报、新行能抓到、半行不报补全才报、中文不碎、轮转不卡死。**6 项全过，再拿它的输出当证据。**
2. **"没有新增告警" ≠ "功能正常"。** 哨兵只证明"没有新的坏事"；功能好不好要看**成功信号**（`直填食材 选中=4 可制作组合=3`、`推荐框客人` 状态行、面板上真的出了推荐）。两样都看，才叫验证过。

### 11.5 性能问题排查流程（v1.6.7 实战，照抄这套顺序）

玩家说"卡"是最模糊的反馈，**不要一上来就读 OnGUI 找字符串拼接**。本项目三次修卡顿（v1.5.1 / v1.5.6 / v1.6.7）都是同一个流程：

**第 0 步：先问清楚是"持续低帧"还是"偶尔顿一下"**。这两个的诊断方向完全相反：
- 持续低帧 → 每帧开销超标（OnGUI 分配、每帧遍历、每帧 interop 读字段）
- 偶尔顿一下 → 某处周期性/一次性的大开销（`FindObjectsOfTypeAll`、GC、资源加载）

**第 1 步：读 `帧率30s` 那一行，判断是不是插件的问题。**

| 现象 | 结论 |
|---|---|
| 60fps 以上占 99%+，但玩家说卡 | **不是持续低帧**，去查尖峰，别改 OnGUI |
| 20~60fps 占比明显 | 每帧开销超标，查 OnGUI / Update 里的每帧工作 |
| 某个 30s 区间突然大幅劣化 | 对照那一刻的日志（换场景 / 大量客人 / 头像扫描） |

**第 2 步：读 `长帧` 行里的「上帧最慢=」。** 这一行是**关键埋点**：它把"这帧为什么卡"直接归因到插件自己的一段。
- `上帧最慢=采集 411.9ms` → 就是我这段造成的
- `上帧最慢=插件内无记录`（而帧还是卡）→ **不是插件**，是游戏加载 / GC / 别的插件，别硬改自己的代码

**第 3 步：读 `最慢三次`，把粗粒度的那一段拆到具体函数。** `采集` 会拆成 `采集·枚举 / 采集·指纹 / 采集·重算 / 头像 / 采集·评分 / 采集·加料`，`头像` 再拆成 `头像·扫描 / 头像·快照 / 头像·全量 / 头像·合成`。**没有这层拆分就只能瞎猜**——v1.6.7 就是靠 `头像·全量 287.9ms` 一步定位到 `FindObjectsOfTypeAll<Sprite>()`。

**第 4 步：改之前先算"这一刀能省多少"。** 用「次数 × 单次耗时 ÷ 30 秒」估它占的 CPU 比例：
- 占比高但单次小 → 降频 or 加缓存
- 单次大但次数少 → 拉长间隔、加总次数上限、或改成增量
- 单次大且频繁 → 换算法（这是唯一需要动业务逻辑的情况）

**这次实测的判据表，下次可以直接对照：**

| 段 | 健康值 | 危险信号 |
|---|---|---|
| `绘制` | 均 0.1ms 级、峰 <10ms | 峰 >20ms（OnGUI 里在分配/拼串） |
| `采集` | 均 <5ms、峰 <50ms | 峰 >150ms（有一次性原生调用） |
| `头像·扫描` | 均个位数、峰 <20ms | 均 >30ms（时间预算没生效） |
| `头像·全量` | 不该出现，或 <50ms | 200ms+ = `FindObjectsOfTypeAll` 正在扫全场 |
| `采集·枚举` | 均 <5ms | 孤立一次 200ms+ 多为 il2cpp 元数据初始化 / GC，不是每次都这样 |

**四条反复踩到的反模式（写代码时直接避开）：**

1. **时间预算检查的粒度太粗。** `if ((i & 63) == 0 && 超时) break;` 看着有预算，实际一批 64 个就能吃掉 40ms。粒度要 ≤8，而且**别用位与做检查点**（游标不是从 0 开始对齐时，有些切片永远撞不上检查点，预算永久失效）。
2. **同一个 tick 里有多个入口各自扫描。** `RefreshIfNeeded`（补缺的）和 `PrefetchTick`（预加载）都对，叠加就是双倍开销。用一个 `_lastBuildAt` 时间戳互斥。
3. **同一个原生对象被反复读取。** `sprite.name` 这种属性每次都要跨 il2cpp 封送字符串，而图集切片会被成百上千个 Image 共享。按 `IntPtr` 去重，只读一次。
4. **靠提高轮询频率来保证响应性。** 正确做法是**轮询降频 + 事件通道**：常规 0.8 秒一拍足够，玩家操作（切开关、开关面板）走 `RequestRefresh()` 立刻重算。v1.6.7 这么做之后，响应反而比原来 0.5 秒轮询更快。

---

## 12. 版本历史（问题 → 修法对照）

| 版本 | 解决/引入的问题 | 修法 |
|---|---|---|
| **v1.6.7** | 用户说「游戏有些卡顿」。插件自带 `Perf` 探针，日志先给了答案：`帧率30s` 99.5% 帧 ≥60fps（**不是**持续低帧，所以不要往 OnGUI 找），`长帧 … 上帧最慢=采集 411.9ms`，`最慢三次 … 头像·全量 287.9ms/196.7ms`。真凶是 `Resources.FindObjectsOfTypeAll<Sprite>()`——**原生一次性调用，中途停不下来**，一次 177~288ms，而 `DeepScanInterval=12f` 让它每 12 秒来一发，就是玩家说的「偶尔顿一下」 | ①`DeepScanInterval` 12s→45s，新增 `MaxDeepRuns=5`（捞不到的图整局都捞不到，无限重试纯白卡）；②`BudgetCheckEvery`=8 替代 `(i & 63) == 0`——`_imgCursor` 是 `i+1`，位与对齐会错位，有些切片永远撞不上检查点，3ms 预算形同虚设（实测单次扫描均 46ms）；③`Take` 加 `SeenSprites`（`HashSet<IntPtr>`）：图集切片被成百上千个 Image 共享引用，`sprite.name` 每次都要跨 il2cpp 封送字符串，不去重等于把最贵的操作重复几十遍；④`PrefetchTick` 加 `_lastBuildAt < 0.4f` 互斥，`RefreshIfNeeded` 与它不再同一拍各扫一遍，另加 `_prefetchGap` 起始 2f；⑤采集节拍 `0.5f`→`CollectInterval=0.8f`，新增 `OverlayBehaviour.RequestRefresh()`，由 `InvalidateCache`（切开关）和 `CookerPanelWatcher.SetOpen/SetClosed`（开关柜子）触发——**轮询降频 + 事件保响应**；⑥`views.RemoveAll(v => …)` 换静态 `Predicate<GuestView>`，常驻节拍不该每拍 new lambda。**三条教训**：①**先读探针再改代码**：`长帧` 的「上帧最慢=」和 `最慢三次` 这两处埋点是这次唯一能定位的依据，没有它们只能在 OnGUI 里瞎猜；②**「写了预算」≠「预算生效」**：任何按时间预算切片的循环，检查间隔必须远小于预算值，还要小心位运算对齐；③**同一 tick 有多个扫描入口必须互斥**——两个入口单独看都对，叠加就是双倍开销 |
| **v1.6.6** | v1.6.5 把推荐框同步好了，但用户实测「**三位一体**这类特殊厨具还是不能直接切换」。两个都只在这种厨具上出现的原因：①特殊厨具的候选不是复用 `guest.Candidates`，而是 `MatchedFor` 里用 `RecipeIndex.Find` **重新生成一批 `DishCandidate`**——同一道菜是两个实例，`List.Contains`（引用比较）永远认不出，`OrderLikePairs` 静默失效；②特殊厨具的循环 `matched.Count >= 4` 就 break，主面板搭配里的那道若排在池子第 10 名**根本进不了列表** | ①新增 `SameDish(a,b)`：优先比 `Recipe.FoodID`，没有菜谱信息才退回比名字；`FindSame(list, want)` 返回列表里**真正那个实例**（它的 `Plan`/评分才是算好的），`OrderLikePairs` 全部改用它；②`MatchedFor` 的特殊厨具分支改成两段：先按 `guest.Pairs` 顺序把池子里的对应菜挑出来占位（`Supports` 过滤 + `FindSame` 判重），再用池子其余菜补满 4 道。**两条教训**：①**跨来源的对象比较要用业务主键，不能用引用**——`RecipeIndex` 每次调用都新建候选对象，缓存/集合里的实例跟它永远不是同一个，`Contains` 会静默返回 false（不报错、不抛异常，只是功能不生效）；②**「只取前 N」要放在挑完重点之后**——先截断再筛选，等于把最该显示的候选挤出去，而且这种 bug 只在「池子很长」的特殊厨具上看得到 |
| **v1.6.5** | 切了「刷好感」之后，主面板的推荐变了，可**料理台和酒水柜旁边的推荐框纹丝不动**。两个独立原因叠在一起：①主面板读 `Pairs`（已按当前口径排序），推荐框读 `Candidates` / `HintDrinks`，各自维护一套旧顺序；②采集节拍用的是 `Time.time`，而打开料理台/酒水柜时游戏把 `timeScale` 压到 0，进柜子那一刻起采集完全停摆，推荐框永远显示旧快照 | ①`OverlayBehaviour.OrderLikePairs`：`MatchesFor` 建缓存时把 `guest.Pairs` 里出现过的菜按主面板顺序前置，其余保持原序（推荐框还要按厨具过滤，主面板那几道本厨具做不了的自然不在列表里）；②`GuestCollector.MovePairDrinksFirst`：酒水框把 `Pairs` 里的 `Drink` 提前，`FavorOn` 时其余粉评酒按 `PriceOf` 升序（普通口径保持利润顺序）；③采集节拍 `Time.time` → `Time.unscaledTime`。**两条教训**：①**同一功能有多个显示面时，让次一级去依附「主结果」的顺序，别重写一遍排序**——三处各自排序必然漂移，主面板已经是权威答案；②**节流一律用 `unscaledTime`**：只要游戏会在某个界面把 `timeScale` 压到 0，那个界面上所有基于 `Time.time` 的刷新都会静默停摆，而且日志里看不出来（没有任何报错，只是数据不动） |
| **v1.6.4** | v1.6.3 的刷好感算法做完了、装机也对了，用户进游戏却说「**没看到刷好感按钮**」——功能只存在于 `BepInEx\config\local.mystia.orderhelper.cfg` 的 `FavorFarm` 一行里，玩家不可能为了切一个开关退出游戏手改文本 | 纯 UI 入口补齐，算法一行没改：①`OrderHelperPlugin` 加 `SetFavorFarm(bool)` / `SetRespectFund(bool)`（改 `ConfigEntry.Value` + `SaveConfig()`，与既有 `SetOneClickCook` 同款）；②主面板顶部按钮行在「一键制作：开」后加「刷好感：开/关」，营业中一点即切，文案由 `FavorLabel()` 返回**字面量**（OnGUI 每帧两趟，拼串就是白送垃圾）；③控制面板（F9）新增【推荐口径】区：「按预算推荐」「刷稀客好感」两个开关 + 说明，并且「刷好感开、预算关」时明确写「不会起作用」；④「恢复默认」一并复位这两项。**三条教训**：①**配置项 ≠ 已交付**——玩家能点到的才算功能，cfg 只是持久化；验收时问「游戏里哪个位置能点到它」；②**常用开关给两处入口**——主面板随手切（可能被拖到屏幕外/被 F8 隐藏）、F9 控制面板集中管理，缺一个都会有人说「找不到」；③**依赖关系要在 UI 上说破**——刷好感依赖预算读钱包，不写这句玩家只会觉得「开了没反应」 |
| **v1.6.3** | 想刷某位稀客的好感时，需要她**吃满所有单（最多 5 单）**，但 v1.6.2 的口径是「能花就花、利润优先」：第一单往往把钱花得差不多，后面几单付不起 → 她提前走 / 黑评，好感刷不起来 | 新增开关 `[Behaviour] FavorFarm`（默认关，只对稀客生效）：①`FavorOn` 判定（稀客 + 配置开 + 钱读得到）；②`CapForOrder`：这一单上限 = 现钱 − `CheapestPinkPrice` ×（剩单 − 1），剩下的是「保底钱」，算出的上限写回 `view.BudgetCap` 让推荐框/按钮同口径；③`CheapestPinkPrice`：遍历「可做的菜 × 粉评酒」找最便宜的「菜+酒 ≥ 4」组合当保底价；④`PickDrink` 加 `cap`/`favor` 入参，刷好感时同粉评**挑更便宜的酒**；⑤排序：净分**封顶 4**（粉评线以上没收益），再「是否超上限 → 价格升序 → 分 → 利润」；⑥状态行加「刷好感」标记；⑦日志 `刷好感预算`（最多 8 条）。**三条教训**：①**目标相反的功能要用显式开关**——「刷好感（省钱吃满单）」和「赚钱（挑贵的）」不可能同一套排序同时最优，别想着自动判断；②**留钱要按「最低可接受组合」留，不能按均价留**——菜价 300~900 差三倍，均分会让贵的那单直接做不出粉评，按最低粉评留才能「保底 + 余额随便花」；③**在粉评线以上堆分是浪费**——4 分就够粉评，更高的净分不会多给好感，通常还更贵，所以刷好感时把净分截到 4 再比价 |
| **v1.6.2** | v1.6.1 把客人的钱按「还剩几单」平均分，**第一单被压得太死**（预算 1000 只敢花 500），本来吃得起的好菜推不出来；而实际情况是「第一单 800 + 最后一单 300」这种花法完全可行 | ①删掉 `GuestView.FundShare`（均分），换成 `FundBase`（同一客人观测到的最大资金 = 本次到店总预算，`Dictionary<string,int>` 按客人名记，`ClearLeft` 换场景清空）+ `BudgetCap`；②新增 `CapOf`：**还有单没上 → 上限 = 手上现钱**（保证这一单一定付得起），**最后一单 → 上限 = 现钱 + 余量（总预算 10%，夹 30~300）**；③`PickDrink` 的软顶从「均分」改成「现钱的 80%」，只作「同样粉评时优先给后面几单留钱」的偏好，不再硬砍；④`DishPair` 增 `Cap`，`OverShare` 换 `OverCash`，`Line4` 分三档：`预算余Y` / `略超Z（余量内）` / `⚠超Z，可能黑评`；⑤`FilterHintDrinks` 与酒水按钮的 `room` 统一用 `BudgetCap`；⑥日志加 `预算变化：客人=X 资金 a→b`（资金变少 = `GetFund` 确实是「剩余」）。**三条教训**：①**「平均」是最容易想到也最容易过头的方案**——均分优化的是「每单都别花光」，代价是「每单都不敢花」，而真实约束只是「最后一单别大幅超」；②**硬顶和偏好必须分开**：能保证付得起的只有硬顶，偏好只该在同样粉评的候选之间起作用，混成一条权重就会退化成「预算内最贵」＝永远选最贵那杯；③**缓存类的预算状态要跟着场景清**——按客人名记的总预算不清会把上一晚的钱包当成今晚的上限 |
| **v1.6.1** | 推荐只顾「这杯酒能凑粉评、卖得贵」，完全不看客人带多少钱 → 搭配价格远超预算，客人付不起，直接黑评 | ①`GuestGroupController.GetFund` 读客人剩余钱（另有 `MaxFundCarry`），`OrderStamp` 带上 `Fund` 让钱一变就重算；②预算 = **剩余钱 ÷ 剩余单数**（钱是整晚的，第一单花光后面全付不起）；③`PickDrink` 先按「菜价+酒价 ≤ 剩余钱」过滤，再在付得起的里面挑粉评 → 别动用后面单的钱 → 售价 → 库存；付得起的没有粉评时退回**最便宜的粉评**并标超支；④`DishCandidate.Price`（售价）与 `Profit`（利润）分开，判断付不付得起必须用售价；⑤界面加 `pair.Line4`「价X 预算余Y ⚠超Z，可能黑评」、酒水按钮标单价+`⚠超预算`；⑥配置 `[Behaviour] RespectFund`，读不到钱自动失效。**三条教训**：①**「付得起」和「赚得多」方向相反**——按利润降序挑必然选最贵那杯，预算必须当**前置过滤器**而不是排序里的一条权重；②**预算是整晚的不是一单的**，只拿剩余钱当上限仍会把钱花在第一个单上，必须按剩余单数均摊；③**读不到就别拦**——字段读失败时整块降级回老行为，别让一个读不到的字段把所有推荐清空（和 `BanTags` 那次「空表=不拦」同理） |
| **v1.6.0** | 加料仍然一份都加不进去（v1.5.9 装机后实测 16 次一键制作全 `加料=0`）。根因不是撤回闸门，而是**加料目标被酒分抵掉了**：`target = 4 − 酒分`，酒常有 2~3 分，菜自己早够分 → 规划器一份都不挑。同时用户要求换推荐口径 | ①**加料 target 固定 = 4**（单单食材就粉评），不再减酒分；②**菜排序改按命中标签数（净分）**，同分才比利润，酒分不参与菜的排序；③**酒**：`FindBeverages` 补 `Profit`（售价）与 `MinRemain`（库存瓶数），排序先保证「配菜能粉评」，同档按利润→数量；`BuildPairs` 配酒同口径；④评级显示仍按「菜+酒合计」（游戏口径）。**两条教训**：①**同一份数据要区分「显示口径」和「排序口径」**——显示给玩家的粉评必须和游戏结算一致（菜+酒合计），但推荐排序可以只看菜自己；v1.5.x 把两者混在一起，导致「菜早够分所以不加料」；②**`加料=0` 时先看 target 而不是先怀疑传递链**——日志里 `加料核算样例` 已经显示出唯一一次加料发生在 `需3`（酒分=1）的时候，`需` 越小越不加料，这就是答案 |
| **v1.5.9** | **v1.5.7 的「加料会凑成另一道菜」闸门把加料功能整个搞挂了**：一局日志实测撤回 85 份加料、74 次撤到「加料=无」、15 次一键制作一次都没带上料。该闸门判定条件「另一道菜的配方是锅里食材的子集」几乎恒成立——锅里有米，所有以米开头的短配方都命中 | ①**删掉这道闸门**（`DropConflictingExtras`/`MatchesAnotherRecipe`/`IsSubsetOf`/`BuildUsage`/`RecomputeHits` 一并清掉），规划阶段只留 BanTags 黑暗料理；同材料两道菜由玩家在面板自选。②制作时读回游戏自己的判定：`MatchedCookCombo` 暴露了 `Recipe` + `Modifiers`，`MatchingSelected` 填完就能读 `m_OutputInstances`，只有「一个组合都匹配不上」才退最后一味。③日志加 `游戏接受的加料=N` 作为「加料真进锅」的铁证。**三条教训**：①**别在规划阶段猜游戏规则**——游戏已经把判定结果（Recipe+Modifiers）暴露出来了，能读回来验证就别自制启发式；②**写闸门前先想「正常情况会不会也命中」**——「子集」这种条件在食材组合里几乎恒真，等于关掉功能；③**静默漏检比误判更危险**：`IsSubsetOf` 边判边扣、中途 return false 不还原，会让后面所有菜谱都判不中，表面看只是「没检测到冲突」，实际结果无从复现 |
| **v1.5.8** | 性能探针细化（功能零变化）：采集/头像/绘制各自拆成子段，每 30 秒输出「计数」与「最慢三次」，长帧带归因，新增帧间隔直方图；配套 `tools\run-report.py` 出报告 | **三条方法论**：①**均值会骗人**——v1.5.3 采集中位均 0.81ms 看着很健康，但 p95 5.36ms、单次峰 119.2ms、1/4 的窗口出现 >20ms 峰，玩家的「卡」全在尖峰里；②**尖峰要记「最慢三次 + 帧号」**，否则只知道慢、不知道慢在哪一段、哪一帧；③**长帧必须带归因**——长帧日志写「上帧最慢=某段 Xms」，若那段只有零点几毫秒，就能立刻排除插件、转向场景加载/GC/别的 mod，省掉一轮盲改 |
| **v1.5.7** | 加料推荐加装「防黑暗料理」：①加料引入的新 tag 命中 `Sellable.BanTags` 就不选；②加料后若同时能凑成另一道菜，按「后加的先去」撤回；被挡完时面板写「加料会冲突，已跳过」。配置 `[Behaviour] AvoidDarkCooking` | **四条教训**：①**字段名不等于语义，但这是最便宜的起点**——`banTags` 只是按名字推断的「禁用 tag」，必须配一条样例日志（`加料冲突表样例`）让人能核对，否则错了没人发现；②**`AdditiveTags` 是「加料之后获得的 tag」，不是白名单**——初稿拿它做许可表，会把正常加料全砍掉；③**改了结果就要连带回滚账本**——撤回加料后 `used`（成本/余量）和命中数必须用最终食材重算，否则面板上的利润还是没撤回之前的数；④**闸门只看「新引入」的 tag**——菜品本来就有的味道不该算成加料的锅 |
| **v1.5.6** | 性能优化（功能零变化）：采集从「每 0.5 秒全量重算」改为「指纹没变就不重算」；头像扫描从「每 2 秒枚举全场 Image」改为「快照 + 分片 + 整轮扫完才合成」；巡查默认关 | **三条教训**：①**先量再改**——`性能30s` 一行就能看出真凶：绘制均 0.1ms（OnGUI 完全不是问题）、采集均 3.6–5.6ms 每 0.5 秒、巡查均 8–26ms 每 5 秒，凭直觉去优化 OnGUI 会白干；②**先用便宜指纹判断要不要重算**——几个 int（桌位/剩单/单子状态）比对，比穿一次 interop 读名字+标签+推荐便宜两个数量级，缓存对象按 IL2CPP 指针存、再加名字比对防指针复用；③**分片扫描要有「整轮扫完」闸门**——身体层和表情层可能落在不同切片，没扫完就合成会得到没有表情的头像 |
| **v1.5.5** | 推荐算法计入「加料」：菜谱 tag + 加料食材 tag 合并算分，够粉评的前提下按利润与食材余量排序 | 新增 `ExtraPlanner.cs` + 配置 `[Behaviour] ConsiderExtras`。**三条游戏规则**：①一锅最多 **5 个食材、含菜谱自带的**（加料槽 = 5 − 菜谱食材数）；②**加料不改变售价**，只多消耗食材 → 能少加就少加，目标分 = 4 − 酒分，够分即停；③加料 tag 与菜品 tag 合并、重复 tag 只算一次 → 只挑「能带来新喜好标签」的食材。**教训**：写推荐/评分前先用 `tools\dump-api.ps1` 把 `Ingredient.Tags`、`Sellable.AdditiveTags`、`TrueValue` 查清楚，别靠猜；拿不准的数值要打一行样例日志（`加料核算样例`）留证据 |
| **v1.5.4** | 进游戏后的版本提示（屏幕上方居中小卡片，淡入/停留/淡出，每次启动只弹一次），其余功能与 v1.5.3 一致 | 新增 `VersionToast.cs` + 配置 `[Display] ShowVersionNotice`。**两条教训**：①**弹窗必须画在 `OnGUI`「没有任何窗口就 return」那句之前**——面板默认不显示时，画在后面的东西永远不会出现；②**计时用 `realtimeSinceStartup`，不要用 `Time.time`**——打开厨具/酒水面板时插件会把 `timeScale` 设成 0，`Time.time` 冻住会让提示永远淡不出去 |
| **v1.5.3** | UI 美化三批（皮肤/布局/头像）+ 官方头像素材包入库 + 未点单过滤，见 `docs\UI美化开发计划.md`。①**皮肤**：IMGUI 灰盒换成游戏自带的和风纸条（`OrderReceipt_BG`，天然九宫格），零美术资源、运行时从场景抓；②**布局**：主面板改成「顶部头像横排 + 脚下展开选中一位」，点头像切换；③**头像**：稀客贴真立绘（`角色名Main_0,1` 身体层 + `Eyes` 表情层合成、按眼睛位置裁头肩特写），常客首字占位（游戏本就不给常客做头像）；④**素材包**：从游戏自带 `东方夜雀食堂 头像&表情包` 批量导入 88 个文件名 / 85 个角色；⑤**未点单的客人不再显示**；⑥**防误触尝试失败后整体放弃** | 新增 `UiSkin.cs`（皮肤：拷素材 → GUIStyle.border 九宫格 → 四处 Box 换皮 + 按钮/滚动条纸片化 + 分割线改墨色）、`GuestPortraits.cs`（头像：图集整图副本按实例缓存 + 拷贝时缩到 48×48 + 缺谁扫谁节流 + 三级名字匹配）、`MouseGuard.cs`（8×8 挡板，游戏侧拦截已移除）；降级开关 `[Display] VanillaSkin`；`UiProbe` 默认关。**主要教训**：①IMGUI 给控件设固定 `Height` 后内容超出是**静默裁剪**（表现"名字显示一半"），拆成两条独立文本让布局自己撑高；②IMGUI **不缩放** `GUIContent` 里的图片，必须在拷贝阶段就缩好；③il2cpp 的 `GUIContent`/`RectOffset` 构造重载被裁剪；④**Harmony Prefix 返回 false 时 Postfix 仍执行**，拿半初始化实例登记状态会让游戏卡死（事故详情见 §4.5）；⑤"靠猜名字认对象"的功能必须留用户可填的兜底（详见 §4.5） |
| v1.5.2 | 「稀客影像」不进推荐框（露易兹符卡「传奇的格列佛」投影回来的稀客，面板里完全没有） | 离店黑名单太硬：`HashSet<IntPtr>` 一旦记下就永久隐藏，而影像复用同一个控制器 → 被自己的"离店"标记挡住；且集合从不清空，指针复用会误伤新客人。改成 `Dictionary<IntPtr,(时间,订单数)>`：订单数变了就撤销标记、换场景清空、10 分钟 TTL；新增客人巡查日志（`GuestAudit`） | 
| 0.3.x | 面板画不出、不能拖 | 放弃 GUILayout.Window，改 GUI.Box+BeginArea+手写拖动 |
| 0.4.x | 字体、滚动、常客开关、厨具旁提示 | 自绘分割线、TMP 字体、滚动视图 |
| 0.5.x | 厨具面板日志缺失 | `Probes.Install` 补齐两个 Patch 类 |
| 0.5.2 | 提示框挡料理区 | 挪到屏幕右侧 |
| 0.6.x | 推荐菜点击无效 | 改用 `UIButtonSimple` 组件调回调 |
| 0.7.x | 推荐菜不在面板里 → 点不到 | **注入 `m_RecipeInstances` + `UpdateRecipeField`** |
| 0.7.5-0.7.6 | `OnRecipeElementSelected` 无效 | 放弃该路径，改直填 `selectedIngredients` |
| 0.7.8 | 分割线透明 | 自建 1×1 纹理 |
| v1.5.2 | 「稀客影像」不进推荐框（露易兹符卡「传奇的格列佛」投影回来的稀客，面板里完全没有） | 离店黑名单太硬：`HashSet<IntPtr>` 一旦记下就永久隐藏，而影像复用同一个控制器 → 被自己的"离店"标记挡住；且集合从不清空，指针复用会误伤新客人。改成 `Dictionary<IntPtr,(时间,订单数)>`：订单数变了就撤销标记、换场景清空、10 分钟 TTL；新增客人巡查日志（`GuestAudit`） | 
| v1.5.2 | 采集贵：4 位客人时平均 30.8ms、峰值 101ms（严格随客人数线性增长，约 7ms/人） | 第一段循环里每条菜谱都要 2 次 IL2CPP 字典查询 + `food.Tags`（每次 marshal 数组，还被调用 1+喜好+厌恶 次）+ `food.Text.Name`（marshal 字符串）；饮品每次调用都枚举整个 IL2CPP 字典。改成按 id 缓存静态数据（名字/标签/食材/厨具）成纯 C# 副本 + 缓存已解锁菜谱 id 表和饮品 id 表；`Invalidate()` 挂到 `OnSceneLoaded`。**教训：静态数据不要每 0.5 秒重新穿 interop 取** |
| v1.5.1 | 「偶尔卡顿」：`OnGUI` 每帧两遍、面板全程开着，却仍在现拼字符串/`FindAll`/`new List` | 展示文本与筛选结果全部改到收集阶段（0.5 秒一次）预生成：`GuestView.BuildLines`、`DishCandidate.FullLabel`、`PopularInfo.*Text`、`GuestView.HintDrinks`；`SnapshotLog` 默认关；新增 `Perf` 探针（长帧 + 每 30 秒耗时汇总）。**教训：IMGUI 里"每帧拼一次字符串"= 每秒上万次分配，客人越多越明显** |
| v1.5.0 | 酒水净分显示虚高 | 配对行里把 `drink.LikeHits`（命中数）当净分用了，改成 `LikeHits - HateHits`；教训：同一行里菜和酒用了两套取值方式，改一个忘一个 |
| v1.5.0 | `RequestHit` 过滤是死代码 | 该字段默认 `true` 且只有 `FromPanel` 会写，`Find`/`FindBeverages` 里的 `FindAll(c => c.RequestHit)` 恒等于原列表，删除；教训：布尔字段默认值决定「过滤」还是「恒等」，写过滤前先看初始值 |
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
**v1.5.2 已实测通过**：整局 171–179 帧、采集峰值 15.1ms（仅冷缓存一次）、稀客影像能进推荐框 —— 卡顿与稀客识别两个问题均已关闭（判据与数字见 §4.3 末尾）。

**已知遗留**：
1. 菜格点击（`OnRecipeElementSelected` / `importedRecipe`）在普通厨具上时好时坏，当前用"直填食材"绕过；如要修，优先看 `MatchingSelected` 与 `UpdateOutputField` 的调用时机。
2. ~~推荐框位置不记忆（重启回默认位置）~~ v1.4.9 已修（`[Layout]` 段，三个窗口通用）。
3. 三位一体只显示料理块，酒水模块走独立面板。
4. 暂停只对"日记/料理台"面板守卫，其他面板可能仍能误触。
5. ~~稀客负 tag 语义未查清（K3）~~ 已查清：`-1` = 这张单没有这一项。**仍未做**事件驱动收集，还是 0.5s 轮询（K7）。
6. 字号变大后容器不跟着变宽（K4）：曾按字号重算宽高，与用户自定义宽高冲突，已回退。
7. ~~**v1.5.2 的效果待实测**~~ **已实测通过**（2026-10-23）：采集峰值 15.1ms（仅冷缓存一次），此后 7–11ms，不再随客人数线性增长。已关闭。
8. **稀客影像只能"看见"，还不能"确认"**：v1.5.2 让被离店标记挡住的影像能重新显示，但当时没有日志证据证明影像复用的就是同一个控制器（属于推断）。下次带符卡跑一局，看 `巡查` 日志里有没有「撤销离店标记」这一行即可确认；如果没有，说明影像走的是别的路径，要回到 §8.3 的三道闸门逐个排。
9. **v1.5.3（UI 美化）已出包**，md5 `4A0C4CFB`，包在 `release\夜雀点单助手-v1.5.3.zip`；稳定版 v1.5.2（md5 `8A5B554D`）备份在 `backup\recon-2026-10-23\`。**只差一次用户的整局复测**（外观 / 头像认人 / 布局 / 未点单过滤 / 性能 K12）。
10. **点击防误触已彻底放弃**（2026-10-23 事故，见 §4.5）。不要再在 `OnPanelOpen` 上加 Harmony Prefix 拦面板。
    玩家侧表现仍是"点插件面板可能同时开厨具"，安装说明里已如实写明这是已知限制。
11. **批4（菜品/酒水图标）未做**，技术路径已查清：程序集里有 `GetSellabeBGSprite(Sellable)→Sprite` 静态方法与主图集 `IngredientItemBadgeBeverageFoodCookerIzakayaSpriteAtlas`（详见 `docs\UI美化开发计划.md` §3）。开工第一步是确认该方法的归属类。
12. **UI 相关遗留小瑕疵**：滚动条是纸片纯色（可用但不够精致）、按钮条整体偏深、常客头像是首字占位、字号 ± 后窗口不跟着变宽（K4）。都留给批5。
13. **头像还有 8 格没认出来**：`本体/06-01`、`本体/27-01`，以及 DLC3/003、075、087、DLC5/03-01、08-01、10-01。
    不影响使用（认不出会退回实时抓取的立绘）。补的办法：`tools\命名表.txt` 里把 `?` 改成名字，重跑 `import_portraits.py`。
14. **工具限制**：本机 PowerShell 的 `Add-Type` 被安全策略拦截（`Add-Type compiles and loads .NET code at runtime`），§11.2 那条 Mono.Cecil 命令**在本机跑不了**，查程序集要换工具（dnSpy / ILSpy / 让用户手动跑）或改用二进制字符串搜索（能拿到方法签名与字段名，但拿不到归属类）。

**续作入口**：`docs\HANDOFF.md` 有逐版时间线与文件清单；`docs\UI美化开发计划.md` 有 UI 批次的进度与下一步；代码入口 `src\OrderHelper\OrderHelperPlugin.cs`（Load/配置）→ `OverlayBehaviour.cs`（全部 UI 与交互）→ `CookerPanelWatcher.cs`（面板挂钩/注入/直填）→ `GuestCollector.cs`（客人采集 + 巡查）→ `RecipeIndex.cs`（推荐算法 + 静态数据缓存）→ `UiSkin.cs`（皮肤）、`GuestPortraits.cs`（头像）、`UiProbe.cs`（UI 侦察，默认关）；排查入口 `tools\watch-log.py`（日志哨兵，§11.3）、`tools\perf-report.py`（性能窗口表，§4.4）。

---

## 14. 新功能自检清单

- [ ] 新补丁类是否已加进 `Probes.Install()`，并有 `[Probe] 已挂补丁` 日志？
- [ ] 版本号三处是否同步（源码/`build.ps1`/文档）？
- [ ] 游戏退出后才 `-Install`？
- [ ] 所有游戏调用是否 try/catch 兜底？
- [ ] 日志里能否看到新功能的"每 5 秒状态行"，方便用户复现时定位？
- [ ] `.ps1` 是否保持 ASCII、`.cs` 是否 UTF-8？
- [ ] 推荐算法改动是否用 §8 的评分公式验证，且不把"点单标签不符"排进前列？
- [ ] 让用户复现前是否先挂 `tools/watch-log.py`（§11.3），而不是事后整读 1.8 MB 日志？
- [ ] `OnGUI` 里是否还有每帧现拼的字符串、`FindAll`、`new List`（§4.2）？加了新面板尤其要查
- [ ] 新增日志是否想清楚频率：每帧？每 5 秒？每 30 秒几百行？同步写日志会直接变成卡顿
- [ ] 结论是否建立在"新增告警 + 成功信号"两样都有（§11.4 第 2 条），而不是只看其中一样？
- [ ] 有没有把"静态数据"每 0.5 秒重新穿 interop 取一遍（§4.3）？`Tags`/`Text.Name`/`Ingredients` 都是数组或字符串，marshal 一次就分配一次
- [ ] 缓存了 IL2CPP 的 wrapper 或数据，有没有对应的 `Invalidate()` 挂到场景切换（§4.3 坑 1）？
- [ ] "客人看不到"先查三道闸门（§8.3 类型/在场/离店），别先怀疑推荐算法；静默 `continue` 处是否有日志？
- [ ] 是否让用户实测（而不是只看日志下结论）？
- [ ] UI 改动：有没有给控件设**固定 `Height`** 却塞了多行/长文本？（超出会被静默裁剪，表现"字显示一半"，§4.5）
- [ ] UI 改动：新贴的图有没有在**拷贝阶段就缩到显示尺寸**？IMGUI 不缩放 `GUIContent` 里的图片（§4.5）
- [ ] UI 改动：取素材的扫描有没有**次数上限 + 节流**，取不到是否降级并写日志？（§4.5 纪律 2、3）
- [ ] UI 改动：`[Display] VanillaSkin = false` 时外观是否完整回退？（降级开关必须能一键还原）
- [ ] 新增贴图是否 `hideFlags = HideFlags.HideAndDontSave`（否则换场景被回收，§4.5）？

---

## 15. 打包发布（一条命令，别手抄安装说明）

```bash
python tools/make-release.py --selftest   # 先验判据（9 项）
python tools/make-release.py --check      # 只校验：版本号一致？dll 在？说明没缺占位符？
python tools/make-release.py             # 出包
```

产物落在 `release\`：

```
夜雀点单助手-v1.5.2.zip       本地发人用的中文名
MystiaOrderHelper-v1.5.2.zip  GitHub Release 上传用的 ASCII 名（同一份内容）
```

包内固定四样，解压到游戏根目录即用（`update.ps1` 靠 `%~dp0` 找自己，**必须和 `BepInEx\` 同级**）：

```
安装说明.txt
update.ps1
更新.bat
BepInEx\plugins\MystiaOrderHelper-v1.5.2.dll
```

### 15.1 版本号现在有四处，脚本只帮你对两处

| 位置 | 谁改 |
|---|---|
| `src\OrderHelper\OrderHelperPlugin.cs` 的 `Version` | 手动 |
| `build.ps1` 的 `$version` | 手动（脚本会校验这两处一致，不一致直接拒跑） |
| `tools\make-release.py` 的 `NOTES["新版本"]` | 手动，**不补就拒绝打包**（防止"包发了但说明里没有这次的变化"） |
| `docs\HANDOFF.md` / `README.md` 的当前版本 | 手动（prose，脚本不管） |

`NOTES` 只维护"玩家看得懂的一句话"，安装说明里的【版本变化】由脚本自动拼（当前版本 + 前两个历史版本），这样不用每次手抄上一版的内容。

### 15.2 打包时踩过的坑

- **安装说明要用 UTF-8 BOM 写**。无 BOM 的 UTF-8 在旧记事本里是乱码；脚本用 `utf-8-sig` 写、读时也按 `utf-8-sig` 读。
- **`更新.bat` 是 GBK，不能碰**。PowerShell 5.1 按系统 ANSI 读脚本，bat 里的中文若是 UTF-8 会变乱码；脚本只做二进制复制，并自检"它确实不是 UTF-8"。
- **zip 里的中文文件名靠 UTF-8 标志位**。Python `zipfile` 写非 ASCII 名会自动置 flag bit 11，Win10+ 资源管理器能正确解压；不要再手工 ZIP。
- **GitHub 的 Release 资产名必须是 ASCII**。中文名只用于本地分发，上传用 `MystiaOrderHelper-vX.Y.Z.zip`（`update.ps1` 按 `releases/latest` 找资产，名字乱选会让更新器 404）。
- **打包前先确认 dll 就是装机那份**。脚本会比对 `BepInEx\plugins\` 里的同名文件 md5，不一致只警告不拦截（因为编译器非确定性：同一份代码两次编译 md5 会不同，见 §2）。
- **打完必须回读校验**。脚本自己 `testzip()` + 逐条目比对 + 解开 dll 算 md5，防止"包看起来生成了、里面其实是上一版"。

### 15.3 发一版的标准顺序

1. 改代码 → `build.ps1` 编译 → `-Install` 装机 → 用户实测通过
2. 补 `NOTES["新版本"]`、同步 HANDOFF/README 的版本行
3. `make-release.py --check` → `make-release.py`
4. GitHub：`git tag vX.Y.Z` → Release 上传 `MystiaOrderHelper-vX.Y.Z.zip`
5. 已经在用旧版的人：双击 `更新.bat` 就能拿到（不用重新下整包）

---

## 16. 许可证与致谢

本插件仅供学习交流，不修改游戏资源文件，不涉及破解。感谢 BepInEx、IL2CPPInterop、HarmonyLib 社区，以及 MetaMystia 项目提供的联机环境。
