# UI 侦察报告（批0）

- 日期：2026-10-23
- 方式：侦察版 v1.5.3（UiProbe，只读）装机，游戏内 F10 触发；场景 `Work`，Image 组件 5020 / 5118 个，Sprite 519 种
- 产物：`docs/reference/ui-samples/`（12 张样例 PNG + 完整 dump `ui-dump-2026-10-23.txt`）
- 触发说明：F10 手动可重复；`[Diagnostics] UiProbe=true` 时首个客人进店 3 秒后自动跑一次

## 一、订单卡结构（最重要成果）

```
Canvas/MainWindow/Front_Frame/Order/Order/Order(Clone)/DampingFleld/MovingField/
├─ OrderBG_Normal    sprite=OrderReceipt_BG_0  32x66  border=(10,6,10,14)  ← 普通态底图（九宫格）
├─ OrderBG_Current   sprite=OrderReceipt_BG_1  32x66  border=(10,6,10,14)  ← 选中态底图（九宫格）
└─ ElementField/
   ├─ SpecialObject          sprite=CharBG      48x48            ← 稀客头像底衬（白圆角方块，被运行时染色）
   │   ├─ Char               sprite=<名>Main_0, 1   64x64 packed ← 头像本体：Q版半身像素小人
   │   ├─ Face               sprite=<名>Eyes_0, 1   64x64 packed ← 眼神图层（眨眼动画，叠在 Char 上）
   │   └─ Front              sprite=CharFrame   48x48            ← 白色圆角描边框（运行时染色）
   ├─ NormalObject_Food/Visual   sprite=Foods_N(Clone)     26x26 显示 52x52
   └─ NormalObject_Bev/Visual    sprite=Beverages_N(Clone) 26x26 显示 52x52
```

**每张订单卡同时包含 SpecialObject 和 NormalObject 两套，游戏按客人类型切换显隐。**

推论：**稀客订单卡有人物立绘，常客订单卡只有菜/酒图标，没有人像。**
（此前截图里两张卡都带头像，说明那两位都是稀客。）

## 二、关键素材规格（实测）

| 素材 | sprite 名 | 原始尺寸 | 所在图集 | 说明 |
|---|---|---|---|---|
| 头像本体 | `<角色名>Main_0, 1` | 44x64（rect 64x64） | Core_SpecialAndDaySceneNPC 2048² BC7 packed | Q版半身，透明底 |
| 眼神图层 | `<角色名>Eyes_0, 1` | 24x13 | 同上 | 叠加层，可省略 |
| 头像底衬 | CharBG | 48x48 | WorkSceneSustainedPannel_Atlas | 白圆角方块，运行时 Image.color 染色 |
| 头像描边 | CharFrame | 48x48 | 同上 | 白色圆角框，运行时染色 |
| 卡片底图 | OrderReceipt_BG_0/1 | 32x66 | Work_Atlas 2048x1024 | 白纸+顶部色条+圆角描边，**border 非零 → 可 9-slice** |
| 完成勾 | Check | 32x24 | Common_Controls_Atlas | 订单完成标记 |
| 图鉴立绘卡 | Button_Album_Guest_95x126_1 | 93x122 | NoteBook_MainPannel_Atlas | 图鉴里的客人卡（备用头像来源） |

样例图肉眼结论：
- 头像是**像素风 Q 版半身人**（不是圆头像、不是纯脸），64→44px 缩小显示只会更清晰，**不会糊**；
- 原图**没有圆角也没有预染颜色**——圆角来自 CharBG/CharFrame 两层，颜色是运行时 Image.color 染的（导出来是白色）。IMGUI 里用 `GUI.color` 染同样两层即可 1:1 复刻原版观感。

## 三、技术决策（由侦察直接敲定）

1. **画法**：所有 UI sprite 都是图集切片（packed），不能整张贴。
   `GUI.DrawTextureWithTexCoords(rect, sp.texture, new Rect(TRx/TexW, TRy/TexH, TRw/TexW, TRh/TexH))`。
   textureRect 的 y 轴与 GUI 的 y 轴相反（Unity 纹理原点在左下），画的时候要翻转：`uv.y = 1 - (TRy+TRh)/TexH`。
2. **头像叠层顺序**（同 rect）：`CharBG（染色）→ Main（头像）→ Eyes（可选）→ Frame（染色）`。
3. **皮肤**：面板底/按钮直接用 `OrderReceipt_BG` 做 9-slice；顶部色条天然是标题栏。白底素材靠 GUI.color 调色，不需要打包任何图片。
4. **头像来源**：稀客条目用订单卡 `SpecialObject/Char` 的 Sprite（在游戏生成卡片瞬间缓存）；常客没有头像 → 条目改用菜/酒图标（`NormalObject_*/Visual`），或占位框。
5. **点击跳转**：`Event.current` + `GUI.Button` 透明覆盖在头像 rect 上即可。

## 四、踩过的坑（已写进 UiProbe 注释，发版前同步经验文档）

1. `Texture2D.EncodeToPNG` 在新版 Unity 不在 Texture2D 上，在 `ImageConversion`（ImageConversionModule.dll）→ build.ps1 必须加 `UnityEngine.ImageConversionModule.dll` 引用，否则 CS1061。
2. 本机互操作里 `RenderTexture.GetTemporary` 的 5 参（带 `RenderTextureMipMap` 枚举）重载不存在 → 用 3 参版。
3. **导出必须在扫描那一趟里做**：先扫完再按 sprite 名回头找，会命中另一个同名失活实例，它的图集没加载，`sp.texture` 返回 null 或宽高 0，`GetTemporary` 直接抛异常，一张都导不出。
4. 失活对象的图集贴图常是未加载状态（texture 宽高 0）→ 导出前必须检查 `src.width > 0`。
5. 首次编译侦察版时还发现：dump 里 5020 个 Image 中大量是失活池化对象/图鉴面板，`FindObjectsOfTypeAll` 是唯一正确姿势。

## 五、对原计划的修订

- 头像批次工作量**下降**：不需要自己画边框/圆角（游戏有现成的 CharBG/CharFrame），不需要翻 GuestIconManager 的私有映射表。
- 常客条目没有头像可用 → 布局方案改为：稀客条目带头像列，常客条目显示菜/酒图标列（与原版订单卡信息一致）。
- 皮肤批次材料**全部落实**：九宫格底图 + 可染色描边 + 顶部色条，零美术资源。
