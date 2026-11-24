# Botania 模组开发约定（给 AI 与本仓库协作者）

> 本文件是**长期有效的硬约定**。每次动手前先读一遍；与任何"顺手做法"冲突时，以本文件为准。

## 1. 交互方式

- 用户是最终裁决者。**有任何不确定、或认为用户方案有问题，先说明并让用户决定**，不要自行猜测推进。
- 用户说"先给方案"时：**只出方案，不写实现代码**。方案必须包含：参考依据、机制设计、兼容性分析、可能存在的问题、待确认事项。
- 交付物一律附 **中文名 ↔ defName ↔ 怎么测** 的表格。
- **不建额外的「验证清单」文件**：需要用户验的东西，直接在对话里列出「怎么测 / 期望值」。
  但**踩过的坑必须写进 `docs/植物魔法开发文档.md` → 踩坑记录**，不要只记在脑子里，
  更不要让用户反复提醒同一个问题。
- **任何「看得见的东西」（贴图、范围环、Gizmo、动画）交付前必须自问三件事**：
  ①引擎会不会根本没画（半径上限、渲染高度、贴图路径）②它会不会被挡住 ③用户 30 秒内怎么判断成功。
  编译通过 **不等于** 游戏里看得见。
- 画 overlay（范围环/连线/高亮）一律用 `Source/Framework/OverlayUtil.cs` 的
  `BotaniaOverlays.DrawRadiusRingSafe()`，它已处理「引擎半径上限 ~79 格」与
  「必须抬到 `AltitudeLayer.MetaOverlays` 才不被地形盖住」两个坑。
- 文档（`docs/`）与 `CHANGELOG.md` 必须同步更新。

## 2. 框架式设计（强制）

新增任何内容前先问自己：**这套逻辑是否已有可复用的基类/组件？**

- 设备/功能花/耗魔建筑 → 继承或组合 `Source/Framework/ManaDeviceCore.cs`（`CompManaDevice` + `ManaDeviceCore`）
- 已有的复用件：`ManaSourceUtil`、`CompManaStorage`、`CompRadiusRing`（+`MapComponent_Rings`）、`PlaceWorker_RadiusFromComp`、`BotaniaDebug`、**`BotaniaOverlays.DrawRadiusRingSafe`（范围环绘制）**
- 一个功能 = 一个 30~60 行的子类 + 一段 XML；如果又要抄一遍"找魔力源 / 定时结算 / 存档 / inspect"，说明框架没用好
- **带作用范围的建筑必须画范围**：放置预览环 + 选中常驻环，缺一视为未完成

## 3. DLC 与其它模组兼容（强制）

- 引用 DLC 内容一律用 `MayRequire="Ludeon.RimWorld.Xxx"` 门住，不许硬依赖。
- **优先"继承/组合原版类 + 只改 Def"**，不要复制原版逻辑；复制会漏掉 DLC 与原版更新。
- 不要继承 Core 里易被其它模组修改的中间抽象模板（如 `AutocannonTurret`）：自建抽象基类，把字段写全。
- 不修改原版 Def（不 Patch 原版），只在需要时用 `PatchOperationAdd` 追加少量内容。
- 改动前评估：Harmony 补丁冲突、同 defName 冲突、存档字段平铺同名冲突。

## 4. 机制与贴图先参考原版（强制）

- **机制**：先读 `<游戏目录>/Data/{Core,Royalty,Ideology,Biotech,Anomaly,Odyssey>/Defs/` 与 decomp 源码，找到原版同类实现（例如自动炮塔就参考 `Turret_AutoMortar`），照它的字段与流程做，不要凭原版经验臆造。
- **API**：写之前先验证（decomp 读源码，或写临时 `_ProbeN.cs` 编译探针，用完即删）。
- **贴图**：先导出原版同类贴图做参考（`_dev/_botania-dev/texture_dump_request.txt` → 进游戏 → `_dev/_botania-dev/vanilla_textures/`），尺寸、锚点、留白、配色都对齐原版，再做 Botania 风格化。

## 5. 组件生命周期铁律（0.8.4 精灵灯事故后新增）

1. **不直接写别的组件状态**（尤其 `CompFlickable.SwitchIsOn`、`DoFlick`、`ResetToOn`）。要让灯光响应，实现 `IThingGlower`：
   `CompFlickable` 有 `switchOnInt`（实际，灯光看它）与 `wantSwitchOn`（玩家意图，Gizmo 看它）两套状态，直接写会让按钮与实际相反、小人反复跑去拨灯。
2. **不在 `PostSpawnSetup` 里结算或刷新别的组件**：此时其它组件可能尚未初始化；留给下一个 tick（`nextWorkTick` 默认 0）。
3. **节流只放一处**：自建 tick 计数 与 `IsHashIntervalTick` 不得叠加（相位不同会永不触发）。
4. **状态变化后主动通知引擎**：`CompGlower.UpdateLit(map)`、`MapMeshDirty(pos, ...)`。
5. 提交前跑 `python _dev/_botania-dev/check_component_rules.py`。

**批量改 XML / C# 的替换纪律（0.8.4 炉心蕙事故后新增）**

- **禁止「首次匹配」式替换**：同名字段在多个组件里重复出现（`poolRadius`、`radius`、`bufferCapacity`…），
  `str.replace(old, new, 1)` 极易改到隔壁组件上，编译与语法校验都看不出，进游戏才报 XML error。
- 必须**带上下文整块匹配**（把 `<li Class="...` 到 `</li>` 一起放进 old/new），或按行号定位，
  并在写回后**回读校验**目标文件确实变成预期内容。
- 改完跑 `python _dev/_botania-dev/check_comp_fields.py`：它会比对 XML 组件字段与 C# 属性类字段。
- **`ParentName` 只能指向 `Name=` 属性，不是 `defName`**：原版 `BombSuper` 只有 defName、没有 Name=，
  写 `ParentName="BombSuper"` 会让整个 def 加载失败（`Could not find parent node named`）。
  要么改成引用 `<DamageDef Name="Bomb">` 这类真父节点，要么把字段抄全、写成自包含 def。
  改完跑 `python _dev/_botania-dev/check_def_refs.py`。
- **写 `<building>` / `<mote>` 块之前先查 decomp 的字段表**：本引擎与原版差别不小
  （例：`MoteProperties` 只有 `growthRate`，没有 `growRate` / `rotationRate`；
  `BuildingProperties.turretTopOffset` 是 **Vector2** 而不是 Vector3，写三元组会让整个 ThingDef 加载失败，
  连带子 def 消失、翻译注入报错）。改完跑 `python _dev/_botania-dev/check_xml_fields.py`。

## 6. 存档兼容

- 本引擎存档把组件字段**平铺在 Thing 节点下**（没有 `<comps><li Class>` 分组），因此：
  - 换组件类只要 `defName` 不变、XML comps 指向存在的新类即可，老档不会崩；
  - **Scribe 键必须带前缀**（如 `botaniaPixieLampHasMana`），否则同一件东西上多个组件会互相覆盖；
  - 迁移时沿用旧键可保住玩家可见状态。

## 7. 工程与验证

- 源码仓库：`_dev/Botania-src`；发布载荷：`Mods/[GZ]Botania`（部署用 `deploy.ps1`，不要把工具塞进载荷）。
- 开发工具与笔记一律放 `_dev/_botania-dev/`。
- **交付前只需一条命令**：`python scripts/check_all.py`（编译 + 5 个静态检查，全绿才允许部署/提交）。
  单项检查仍可直接跑 `_dev/_botania-dev/` 下的脚本。
- **新增物品 / 建筑只需一条命令**：`python _dev/_botania-dev/scaffold_apply.py item|building Botania_XXX --label-zh ...`
  它会生成 Def + 贴图 + 中文 DefInjected，并自动跑 `check_all.py`；检查不过就照报错改。
  花 / 设备 / 炮塔这类要写 C# 的，用 `_dev/_botania-dev/new_content.py`（三个模板 + 自检）。
- 详细流程与字段含义见 `docs/新增内容速查.md`。
- **无法自行启动游戏**：编译与静态校验只能证明"不会崩"，行为必须由用户回传日志/截图确认。日志：`%USERPROFILE%\AppData\LocalLow\Ludeon Studios\RimWorld by Ludeon Studios\Logs\Player.log`。

## 8. 版本与推送

- 本地提交可以；**推送/打 tag/发 Release 必须等用户明确指令**。
- 分支流程：`feat/xxx` → PR → CI 通过 → **用户说"合并"才合并** → 打 tag。
- 版本号遵循语义化版本，发布前 `scripts/check_version.py` + `scripts/validate_xml.py` 必须通过。
