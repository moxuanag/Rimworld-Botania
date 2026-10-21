---
name: GZ-RimworldModDevSkill
description: 给 RimWorld 1.7（Waylith/Brutal 自定义引擎、net10、无 Harmony）写模组时的提速与避坑手册。当需要新增/修改 RimWorld 模组的 C# Comp、Ability、Gizmo、Verb、Def XML、贴图、本地化，或排查「写了但游戏里没反应/UI 消失/组件丢失」这类问题时使用。
whenToUse: 在 RimWorld 模组仓库里开发新功能、改 Def、加 C# 类、修游戏内行为不符预期的问题之前。
---

# RimWorld 1.7 模组开发（自定义引擎版）

本机环境是**改过的引擎**（`Waylith.dll` + `Brutal.*`、CoreCLR、net10），**不是原版 Unity/Mono**，
并且**没有 Harmony**。原版的写法有一半不能照搬。以下每条都是实测结论，不是推测。

## 铁律

1. **先验证 API，再写实现。** 不要按原版经验写。用「编译探针」确认：
   写一个临时 `_ProbeN.cs` 调目标成员 → `dotnet build` → `CS0115`（不能重写）/`CS1061`（没这个成员）/
   `CS0246`（没这个类型）就是答案。**探针用完立刻删掉**，别留进发布 DLL。
2. **先读 `Data/` 再动手。** 游戏内容全在 `<游戏目录>/Data/{Core,Royalty,Ideology,Biotech,Anomaly,Odyssey}/Defs/`。
   要抄结构、查字段名、找原版同类实现，都在这里。比试错快一个数量级。
3. **一次只改用户要求的东西。** 不顺手加功能；交互方式有歧义先问一句。
4. **没法启动游戏。** 只有两种验证手段：编译 + 静态校验脚本 + 用户回传日志。所以每个改动都要能让用户一眼看出成败。

## 关键结论表（实测，踩过坑）

| 想做的事 | 正确做法 | 不要做 |
|---|---|---|
| 给**装备**加 gizmo | `CompEquippable.CompGetEquippedGizmosExtra()` | `ThingComp.CompGetGizmosExtra()` 只在**物品自己被选中**时显示，拿在手里不显示 |
| 自定义词条组件 | `<li Class="CompProperties_X"/>` 或原版写法 `<li><compClass>CompX</compClass></li>` | 不存在 `CompProperties_Equippable`，`<li Class>` 只认 CompProperties 子类 |
| 换掉父模板的 comps | **只追加**，让父模板的照常继承 | `<comps Inherit="False">` 会丢掉 Styleable/CompArt/CompQuality，引发 `IdeoRelic ... missing CompStyleable` |
| 覆盖技能按钮样式 | `AbilityDef` 的 `<gizmoClass>` 指一个 `Command_Ability` 子类 | `Ability.GetGizmo()` 不是 virtual，改不动 |
| 技能选点时画范围 | 自定义 `Verb : Verb_CastAbility`，重写 `DrawHighlight`，在 `AbilityDef/verbProperties/verbClass` 里指定 | `Ability.DrawHighlight` **不可重写**（`CS0115`） |
| 画范围圈实现 | `GenDraw.DrawRadiusRing(cell, radius, color)` + `GenDraw.DrawTargetHighlight(target)` | **绝对不要**在自己的 `Verb.DrawHighlight` 里调 `base.DrawHighlight()` —— 父类依赖 `ability` 状态，一抛异常整个 GUI 挂掉（表现为**点技能后 UI 消失**） |
| 每帧绘制代码 | 整体 `try/catch` + `Log.ErrorOnce(...)` 兜底 | 让异常冒到 GUI 层 |
| 建筑**常驻**范围显示 | 新建 `MapComponent` 子类，用 `MapComponentDraw()` 里遍历 `Find.Selector.SelectedObjects` | 建筑的 `Draw` 不一定调 `Comp.PostDraw` |
| Gizmo 悬停判定 | 在 `GizmoOnGUI` 里记下 `Rect`，`GizmoUpdateOnMouseover` 里用 `Mouse.IsOver(rect)` 再画 | 该回调对**整排** gizmo 都会触发，不是只对悬停的那个 |
| 改植物成长度后贴图 | 改完 `plant.Growth` 要手动 `map.mapDrawer.MapMeshDirty(pos, MapMeshFlagDefOf.Things)` | 这版引擎改 `Growth` **不会**自动重刷网格，成熟度 100% 还是幼苗贴图 |
| 静态贴图字段 | 类上标 `[StaticConstructorOnStartup]` | 否则报 "probably needs a StaticConstructorOnStartup attribute" |
| 给小人加技能 | 轮询 `MapComponent`（每 60 tick 扫 `SpawnedPawnsInFaction`）后 `pawn.abilities.GainAbility(def)` | 装备变化通知在这版引擎里不派发，别指望 `Notify_Equipped` |
| 让小人「走过去再做事」 | **不要**自定义 JobDef + JobDriver（实测不生效，白花两轮） | 直接用 `JobMaker.MakeJob(JobDefOf.Goto, cell)`，或干脆不做，只提示「超出范围」 |
| 武器开火前校验 | `Verb : Verb_Shoot` 重写 `OrderForceTarget` + `CanHitTargetFrom` | 原版超射程是静默丢弃，玩家会觉得「没反应」 |
| 物品 tick | Def 里 `<tickerType>Normal</tickerType>` 才会跑 `CompTick` | 漏了则放在地上的物品永远不 tick（自充能之类全失效） |
| 物品初始状态 | 用「读取时懒初始化」`EnsureInitialized()` + 存档标记位 | `ThingComp` 没有 `PostMake()` 可重写（`CS0115`） |

## 其它 API 事实

- `Pawn_PsychicEntropyTracker` 存在，但只有 `CurrentPsyfocus` 可用；没有 `Gizmo_PsychicEntropy` 类型，别照着抄。
- `Ability_EffectRadius` 是个普通 StatDef（原版 `AgonyPulse` 写 2.9），但**这版 `Ability` 上取不到**：
  `Ability.GetStatValue` / `Ability.EffectRadius` 都不存在。半径只能在自己代码里给。
- 引擎里确实有 `drawHighlightWithLineOfSight` 字段 + `Verb.DrawHighlight`，说明范围绘制走 Verb 这条路。
- 路径可达性：`Pawn.CanReach(cell, PathEndMode.Touch, Danger.Deadly, false, false)`、`JobMaker.MakeJob(JobDefOf.Goto, cell)`、`Toils_Goto` 都可用。

## Def XML 规范

- **原版 Def 是一层层继承的**，改之前先看父模板。例：
  `BaseWeapon`(Forbiddable + CompEquippable + Styleable) → `BaseGun`(+CompArt) → `BaseGunWithQuality`(+CompQuality) → `BaseMakeableGun` → `BaseHumanMakeableGun`。
- 抽象基类写法：`<ThingDef Abstract="True" Name="BaseX">`；继承：`ParentName="BaseX"`。
- comps 两种等价写法：`<li Class="CompProperties_Foo"/>` 与 `<li><compClass>CompFoo</compClass></li>`。
- 校验脚本会拦「缺少 Class」，但 `compClass` 写法是原版合法的，别当错误。

## 本地化

- 语言身份 = **文件夹名**，中文是 `Languages/ChineseSimplified (简体中文)/`。
- 两类：`Keyed/`（`Botania.XXX` 键）与 `DefInjected/<DefType>/`（Def 的 label/description 字段）。
- **只能注入 Def 上真实存在的字段**：给没有 `description` 的 ThingDef 注入 `xxx.description` 会报
  `Translation data for language ... has N errors`。
- `"键".Translate(a, b)` 的参数个数必须和文案里的 `{0}` `{1}` 一致。

## 工程与验证流程

1. 改 C# → `dotnet build -c Release`（在 `Source/` 下）。**游戏开着时 DLL 被锁**，构建会失败或产出旧文件，
   必须确认 `Assemblies/*.dll` 的写入时间晚于最新 `.cs`。
2. 跑校验脚本（`_dev/<mod>-dev/validate_mod.py`）：检查 Def 交叉引用、本地化覆盖、DefInjected 字段存在性、
   DLL 内类型清单、贴图尺寸/透明度统计。
3. 只有这两步都通过才提交。
4. 提交：`git commit -F <消息文件>`。**不要把带括号/引号的消息直接写在 `-m` 里**（会报 outside repository）。
5. 遵守仓库的推送策略（本项目：本地提交，问过才推送）。

## 脚本与文件操作注意事项

- 路径里有 `[` `]`（如 `Mods\[GZ]Botania`）时，PowerShell 的 `-LiteralPath` 对**相对路径**会抛
  `ArgumentException`；要用 `Get-ChildItem -LiteralPath $dir -Filter ... | ForEach-Object { Remove-Item -LiteralPath $_.FullName }`。
- 写 XML 一律 UTF-8；批量改 XML 用 Python 脚本（`read_text(encoding='utf-8')` → 断言旧串存在 → 替换 → 写回），
  比手改安全。中文注释乱了就是编码问题的征兆。
- `pwsh -Command` 里内联长 Python 字符串容易被 PowerShell 吃掉引号（`SyntaxError: unterminated string`），
  **把脚本写成 .py 文件再执行**。

## 排查「写了但没反应」的顺序

1. 日志里搜 `Could not find class X` / `Some verbs were null after loading` → 类名/Def 没对上。
2. 搜 `Could not find type named X` → 程序集里没有这个类型（DLL 旧了，或没编译进去）。
3. 搜 `Config error in <DefName>` → Def XML 字段/组件配置错。
4. 功能在但绘制在错的地方 → 回想上表：是不是用了「物品自身的 gizmo」而不是「装备的 gizmo」。
5. UI 直接消失 → 几乎一定是某段 `Draw*` 每帧抛异常，加 `try/catch` + `Log.ErrorOnce` 定位。

## 一个模组的最小骨架

```
Mods/MyMod/
  About/About.xml            packageId / modVersion / supportedVersions
  About/Preview.png
  Defs/*/*.xml
  Source/MyMod.csproj        net10.0 classlib，引用 ../../Assembly-CSharp.dll
  Source/*.cs
  Assemblies/MyMod.dll       构建产物，和 Source 同版本
  Languages/ChineseSimplified (简体中文)/{Keyed,DefInjected}/
  Textures/
```
