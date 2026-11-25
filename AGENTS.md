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

## 2. AI 协作工作流（硬规则）

> 2026-11-25 定案。本节约束的是**代理之间怎么分工、谁能动手、谁只能验收**；
> 与第 1 节「先问用户」并列，两者冲突时以更严的那条为准。

- **主代理（lead）只负责设计与验收**：出方案、拆任务、给每个子代理划定写域、验收结果。
  **主代理不得自己写实现**——C#、XML、贴图、脚本、数据文件都算实现，全部派给子代理去做。
  主代理亲手写实现，等于既当运动员又当裁判，写域与验收都会失控。
- **方案必须先经用户确认**，确认之后才允许派子代理开工。
  用户没点头之前，主代理只能做**只读调研与出方案**，不得边讨论边改文件。
- **子代理不得 commit / push / 打 tag / 部署**。交付物只有「改好的文件 + 中文报告」，
  版本库里的事轮不到子代理碰。
- **验收标准由主代理亲自执行**：`py -3 scripts\check_all.py` 全绿 + 抽查关键数值与字段
  （`<MarketValue>`、`researchPrerequisites`、配方材料、`harvestYield` 这类）。
  **不采信子代理的自述**——子代理说「已改好、已校验」只作为线索，不作为结论。
- **授权边界（2026-11-25 修订）**：**本地 commit 由主代理自行决定**——验收通过即可提交，不必再问用户；
  **部署到 `Mods/[GZ]Botania` 也可自行做**，那是「交给用户进游戏验证」的常规动作，用户已授权；
  但**发布性质**的部署（随版本发布一起、要发给别人的那次）不算常规动作，按下面这条走。
- **push 到 GitHub / 打 tag / 发 Release 必须等用户明确指令**。验收通过后可自行 commit 与部署（供用户测试），
  但 push / tag / Release 一步都不能自作主张。旧说法「本地提交也必须先问用户」**已作废**（见 §9）。
- 主代理在等待期间**只允许做只读验收**：跑 `check_all.py` / `balance_audit.py`、抽查文件、看 diff。
- **有任何不确定、或与用户原话冲突**：先停下来问用户，不要自行发挥，不要「顺手优化」。
- 用户说「没提到的东西不要改」时，**严格按字面执行写域**：只改被点名的文件与章节，
  多改一行都是违规，包括顺手改掉的格式与注释。

### 2.1 派单前置闸门（2026-11-25 立 · **本节最高优先级，压过上面所有条款**）

- **只有当用户当前这条消息里出现明确批准词**（「确认」「可以」「行」「做」「按这个来」「就这样」）时，
  主代理才允许派子代理干活。
- 其余一切输入 —— **疑问句、反问、纠正、吐槽、补充需求、指出问题** —— 一律**只回答，不得派单**。
  用户说「这里是不是不对」是在提问，不是让你开工；用户说「这个改一下」即使听起来像命令，
  只要不是批准词，也先确认再动。
- 主代理准备派单时，必须在回复里**明确写出「我准备派单：<任务一句话>」，然后等用户下一条消息**；
  禁止在同一轮里「说完方案顺手派单」。
- 已犯过三次同类错误（把用户的纠正 / 反问误读成批准），此规则是**硬性闸门**：
  任何「我觉得用户应该会同意的」「方案都讨论完了，顺手开工吧」都**不构成例外**。

## 3. 框架式设计（强制）

新增任何内容前先问自己：**这套逻辑是否已有可复用的基类/组件？**

- 设备/功能花/耗魔建筑 → 继承或组合 `Source/Framework/ManaDeviceCore.cs`（`CompManaDevice` + `ManaDeviceCore`）
- 已有的复用件：`ManaSourceUtil`、`CompManaStorage`、`CompRadiusRing`（+`MapComponent_Rings`）、`PlaceWorker_RadiusFromComp`、`BotaniaDebug`、**`BotaniaOverlays.DrawRadiusRingSafe`（范围环绘制）**
- 一个功能 = 一个 30~60 行的子类 + 一段 XML；如果又要抄一遍"找魔力源 / 定时结算 / 存档 / inspect"，说明框架没用好
- **带作用范围的建筑必须画范围**：放置预览环 + 选中常驻环，缺一视为未完成

## 4. DLC 与其它模组兼容（强制）

- 引用 DLC 内容一律用 `MayRequire="Ludeon.RimWorld.Xxx"` 门住，不许硬依赖。
- **优先"继承/组合原版类 + 只改 Def"**，不要复制原版逻辑；复制会漏掉 DLC 与原版更新。
- 不要继承 Core 里易被其它模组修改的中间抽象模板（如 `AutocannonTurret`）：自建抽象基类，把字段写全。
- 不修改原版 Def（不 Patch 原版），只在需要时用 `PatchOperationAdd` 追加少量内容。
- 改动前评估：Harmony 补丁冲突、同 defName 冲突、存档字段平铺同名冲突。

## 5. 机制与贴图先参考原版（强制）

- **机制**：先读 `<游戏目录>/Data/{Core,Royalty,Ideology,Biotech,Anomaly,Odyssey>/Defs/` 与 decomp 源码，找到原版同类实现（例如自动炮塔就参考 `Turret_AutoMortar`），照它的字段与流程做，不要凭原版经验臆造。
- **API**：写之前先验证（decomp 读源码，或写临时 `_ProbeN.cs` 编译探针，用完即删）。
- **贴图**：先导出原版同类贴图做参考（`_dev/_botania-dev/texture_dump_request.txt` → 进游戏 → `_dev/_botania-dev/vanilla_textures/`），尺寸、锚点、留白、配色都对齐原版，再做 Botania 风格化。

## 6. 组件生命周期铁律（0.8.4 精灵灯事故后新增）

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

## 7. 存档兼容

- 本引擎存档把组件字段**平铺在 Thing 节点下**（没有 `<comps><li Class>` 分组），因此：
  - 换组件类只要 `defName` 不变、XML comps 指向存在的新类即可，老档不会崩；
  - **Scribe 键必须带前缀**（如 `botaniaManaLampHasMana`），否则同一件东西上多个组件会互相覆盖；
  - 迁移时沿用旧键可保住玩家可见状态。

## 8. 工程与验证

- 源码仓库：`_dev/Botania-src`；发布载荷：`Mods/[GZ]Botania`（部署用 `deploy.ps1`，不要把工具塞进载荷）。
- 开发工具与笔记一律放 `_dev/_botania-dev/`。
- **交付前只需一条命令**：`python scripts/check_all.py`（**编译 + 6 个静态检查，共 7 项**）。
  六项依次是 `validate_mod` / `check_comp_fields` / `check_xml_fields` / `check_def_refs` /
  `check_component_rules` / `balance_audit`；全绿只是**验收的前提**。验收通过后主代理可自行 commit
  并部署到 `Mods/[GZ]Botania` 供用户测试；push / 打 tag / 发 Release 仍等用户指令（见 §2 / §9）。
  单项检查仍可直接跑 `_dev/_botania-dev/` 下的脚本。
- **新增物品 / 建筑只需一条命令**：`python _dev/_botania-dev/scaffold_apply.py item|building Botania_XXX --label-zh ...`
  它会生成 Def + 贴图 + 中文 DefInjected，并自动跑 `check_all.py`；检查不过就照报错改。
  花 / 设备 / 炮塔这类要写 C# 的，用 `_dev/_botania-dev/new_content.py`（三个模板 + 自检）。
- 详细流程与字段含义见 `docs/新增内容速查.md`。
- **无法自行启动游戏**：编译与静态校验只能证明"不会崩"，行为必须由用户回传日志/截图确认。日志：`%USERPROFILE%\AppData\LocalLow\Ludeon Studios\RimWorld by Ludeon Studios\Logs\Player.log`。

## 9. 版本与推送

- **本地提交由主代理自行决定**（验收通过即可 commit，不必再问用户）；
  **push 到 GitHub / 打 tag / 发 Release 必须等用户明确指令**。
  部署到 `Mods/[GZ]Botania` 属于「交给用户进游戏验证」的常规动作，可自行做；
  **随版本发布一起的那次部署**按 push / tag 的规则等指令。
- 分支流程：`feat/xxx` → PR → CI 通过 → **用户说"合并"才合并** → 打 tag。
- 版本号遵循语义化版本，发布前 `scripts/check_version.py` + `scripts/validate_xml.py` 必须通过。
