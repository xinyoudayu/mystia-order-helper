# 夜雀点单助手（Mystia Order Helper）

《东方夜雀食堂》的 BepInEx 插件：把在场客人的点单、菜谱命中的喜好标签、**菜 + 酒** 的最佳搭配直接算好，摆在料理台和酒水面板旁边。点推荐项就能直接开做，不用再一格格翻菜谱。

- 游戏版本：0.29.4（Unity 2021.3.28f1 / IL2CPP）
- 前置：BepInEx 6.0.0-be.785（IL2CPP x64）
- 当前版本：**v1.4.9**

## 功能

### F8 主面板

每位在场的稀客 / 常客一块，显示名字、桌位、点单原文、剩余单数，以及**菜 + 酒搭配**推荐（最多 3 组）：

```
◆ 魂魄妖梦（稀客）  桌位 5  剩1单
点菜：我想要家常的料理
点酒：酒水标签 12
    [菜 + 酒 搭配]
    → 豆腐锅[煮锅]＋清酒
       粉评 6分  ｜ 净分 菜3 酒3
```

- **净分** = 命中的喜好标签数 − 命中的厌恶标签数（菜、酒分开算，负数说明点了会掉评价）
- **合计分** = 菜净分 + 酒净分，游戏按合计分给评价：**≥4 粉评**、3 橙评、2 绿评、1 紫评、否则黑评
- 稀客只在**能凑出粉评**时才推荐组合；凑不出来就降级显示“命中标签最多的那一菜一酒”
- 常客按精确菜名 / 酒名推荐，不受粉评限制

### 料理台旁 / 酒水面板旁

打开厨具面板或酒水柜时，旁边会出现对应的推荐框：

- 料理框列出**当前厨具能做的**、且命中的菜，标题写清厨具名（特殊厨具会显示“三位一体”这类名字）
- 酒水框列出有货的酒水
- 每行标注 `(净N分) [粉评 合计6分] 可做/缺料`，缺料会写缺哪种食材、差多少
- 已经上过菜、正在等下一轮的客人不会出现在推荐框里
- 点推荐项 → 自动填食材 → 自动开做（可在 F9 里关掉一键制作）

### F9 控制面板

- 字号 12–32
- 常客是否显示
- 一键制作开关
- 三个窗口（主面板 / 料理框 / 酒水框）的位置与大小：`←→↑↓` 移动、`宽± / 高±` 缩放、`默认` 复位
- 导出客人日志、恢复默认

位置和大小会自动存进配置，下次进游戏沿用；拖动窗口本身也会记住新位置。

## 安装

1. 确认游戏已装 BepInEx 6.0.0-be.785（IL2CPP 版），`<游戏目录>\BepInEx\core\BepInEx.Unity.IL2CPP.dll` 存在
2. 从 [Releases](../../releases) 下载最新 zip
3. 解压，把 `BepInEx\plugins\MystiaOrderHelper-vX.Y.Z.dll` 放进 `<游戏目录>\BepInEx\plugins\`
4. 启动游戏，日志里出现 `夜雀点单助手 vX.Y.Z 已加载` 即成功
5. 按 F8 开主面板，F9 开控制面板

卸载：删掉 plugins 里那个 dll 和 `BepInEx\config\local.mystia.orderhelper.cfg`。

## 更新

仓库根目录带一个更新脚本，双击 **`更新.bat`** 即可：

```
update.ps1           检查 GitHub Releases → 有新版就下载并替换 dll
update.ps1 -Check    只检查，不安装
更新.bat             双击运行（转调 update.ps1，结束后停住让你看结果）
```

脚本会：读已装版本 → 查 releases/latest → 比对 → 下载 zip → 把旧 dll 归档到 `BepInEx\plugins\_backup\` → 换上新 dll。游戏正在运行时会提示先退出。

## 从源码构建

需要 .NET SDK（Roslyn 编译器）与本机 BepInEx 的 interop 程序集：

```powershell
# 改 build.ps1 顶部的 $GamePath 指向你的游戏目录
powershell.exe -NoProfile -ExecutionPolicy Bypass -File build.ps1
# 产物：dist\MystiaOrderHelper-vX.Y.Z.dll
```

注意：

- 不要用 `dotnet build` / MSBuild（本项目没有 csproj，走 Roslyn `csc.dll` 直编）
- 版本号要改三处：`src/OrderHelper/OrderHelperPlugin.cs` 的 `Version`、`build.ps1` 的 `$version`、文档
- `build.ps1` / `game.ps1` 的 `$GamePath` 默认值是作者机器上的路径，请改成你自己的
- `.ps1` 请保持 ASCII 或 GBK 保存（PowerShell 5.1 按系统 ANSI 读脚本，UTF-8 中文会乱码）

## 配置

`<游戏目录>\BepInEx\config\local.mystia.orderhelper.cfg`：

```ini
[General]
ToggleKey = F8
SettingsKey = F9

[Display]
FontSize = 20
ShowNormalGuests = false

[Behaviour]
EnableOneClickCook = true

[Layout]
MainX = 40
MainW = 560
CookerHintX = 1904
...
```

改配置要在游戏退出后进行，否则游戏关闭时会把内存里的值写回去。

## 评分规则备忘

游戏本身对稀客的喜好标签计分：命中 1 个喜好 +1、命中 1 个厌恶 −1，菜与酒相加后定评价档位。本插件的“净分 / 合计分 / 粉评”就是把这套规则显式算出来，不改变游戏判定。

标签 id 为负数不是合法标签，而是“这张单没有这一项”：`foodRequest = −1` 表示还没点单，`beverageRequest = −1` 表示酒水不限。

## 目录结构

```
src\OrderHelper\        插件源码（9 个 .cs）
docs\                  侦察记录
HANDOFF.md             交接文档：现状、API 地图、已知问题
MOD制作经验文档.md       踩坑全集（IL2CPP、IMGUI、Harmony、构建）
项目报告.md             需求、架构、验收
build.ps1              构建脚本
game.ps1               启动 / 关闭游戏
update.ps1             检查并安装更新
```

## 免责

个人自用插件，按现状提供。改游戏行为的部分（自动填食材、自动开做）请自行判断风险；与其它修改料理台的插件可能冲突，遇到问题先只留本插件复现。

## 许可

MIT，见 [LICENSE](LICENSE)。
