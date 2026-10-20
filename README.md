# 植物魔法 Botania · RimWorld 1.7 移植

[![release](https://img.shields.io/github/v/release/moxuanag/Rimworld-Botania?label=release)](https://github.com/moxuanag/Rimworld-Botania/releases)
[![license](https://img.shields.io/badge/license-MIT-blue)](LICENSE)

把《我的世界》的 **Botania（植物魔法）** 移植到环世界 1.7 的非官方模组。
魔力自成一体的能量体系，不接入原版电力网，但可以经**魔力通量场**换回普通电力。

> 本仓库只存放**模组源文件**。成品由 GitHub Actions 在打 tag 时自动打包，
> 到 [Releases](https://github.com/moxuanag/Rimworld-Botania/releases) 下载 `Rimworld-Botania-x.y.z.zip` 即可。

## 内容一览

| 系统 | 说明 |
|---|---|
| 神秘花 | 16 色，野生生长于温带/寒带/热带/沼泽/荒漠/苔原，可播种；采集得同色花瓣，不伤根会重新生长 |
| 材料 | 16 色花瓣 → 魔力尘 → 魔力钢锭 / 活木（两者都是可用建材，带专属形容词） |
| 魔力池 | 3×3，储 100 000 魔力；每 4.2 秒与紧贴的池子把差值对半分，自动拉平 |
| 火红莲 | 吃木头 / 活木 / 化合燃料，约 30 魔力/秒；附近没有可接收的池子就不空烧 |
| 魔力中继塔 | 从池子或上游中继塔抽魔力存进自身小缓冲，把供给范围向外延伸，**每跳损耗 10%** |
| 魔力通量场 | 从附近魔力池或中继塔抽魔力，输出 1000 W；抽不到就停转 |
| 白雏菊 | 把相邻的原木 → 活木、石块 → 活石，每 5 秒一次、耗 20 魔力；**不需要小人**，可随时关闭 |
| 活石 | 石类建材（`Stony` / `Bricks` 外观），比花岗岩更耐打，也能用配方合成 |

**玩法闭环**：采花 → 研磨魔力尘 → 白雏菊把木头/石块变活木/活石 → 锻造魔力钢 → 建魔力池与火红莲 → 中继塔把魔力送远 → 通量场变电。

## 安装

1. 到 [Releases](https://github.com/moxuanag/Rimworld-Botania/releases) 下载最新 zip；
2. 解压出 `Botania` 文件夹，放进 `RimWorld/Mods/`；
3. 在模组列表启用「植物魔法 Botania」，排序放在 Core 之后即可（模组只依赖 Core）。

手动安装（开发用）：

```powershell
git clone https://github.com/moxuanag/Rimworld-Botania.git "RimWorld/Mods/[GZ]Botania"
```

## 关键数值

| 项 | 值 |
|---|---|
| 魔力池容量 | 100 000（3×3） |
| 池子均摊 | 每 250 tick（≈4.2 秒）与紧贴的池子对半分差值 |
| 火红莲 | 1 单位燃料 = 3000 魔力，燃料每秒消耗 0.01，约 30 魔力/秒 |
| 通量场 | 输出 1000 W，耗 1.5 魔力/秒 |
| 中继塔 | 缓冲 3000，半径 6 格，搬运 10 魔力/秒，每跳损耗 10% |
| 白雏菊 | 每 5 秒转化 1 个相邻材料，耗 20 魔力（≈4 魔力/秒）；一个火红莲约供 7 株 |
| 中继塔造价 | 5 魔力钢 + 10 活木 + 1 零部件 |

## 从源码构建

需要 .NET SDK 与**本机安装的 RimWorld 1.7**（`Source/Botania.csproj` 通过相对路径
`..\..\Assembly-CSharp.dll` 引用游戏程序集，所以仓库必须放在 `Mods\<任意名>\` 下）。

```powershell
cd Source
dotnet build -c Release
# 产物：../Assemblies/Botania.dll
```

改完 Def/代码后建议跑一遍自检（可选，脚本在开发者的 `_dev` 目录，不进仓库）：

```powershell
python validate_mod.py
```

## 版本管理

- 采用 **语义化版本** `MAJOR.MINOR.PATCH`，当前 `0.1.0`；
- `About/About.xml` 的 `<modVersion>` 是唯一版本来源，**必须与 git tag 一致**（CI 会校验，不一致直接失败）；
- tag 形如 `v0.1.0`，推 tag 即触发自动打包 + 发布 Release。

发布流程：

```powershell
# 1. 改版本号（About/About.xml 的 <modVersion>）+ 更新 CHANGELOG.md
# 2. 提交
git add -A
git commit -m "feat: 魔力中继塔"
# 3. 打 tag 并推送
git tag -a v0.1.0 -m "Botania RimWorld 0.1.0"
git push origin main
git push origin v0.1.0
# 4. GitHub Actions 自动产出 zip 并附到 Release
```

历史版本见 [CHANGELOG.md](CHANGELOG.md)。

## 目录结构

```
About/           模组元数据（name / packageId / modVersion）
Assemblies/      编译好的 Botania.dll
Defs/            ThingDef / RecipeDef / ResearchProjectDef 等
Languages/       中文（ChineseSimplified (简体中文)）与英文 Keyed
Patches/         生物群系追加野生花
Source/          C# 源码（CompManaStorage / CompManaRelay / CompManaFluxfield / PlaceWorker…）
Textures/        全部程序化生成的 PNG
scripts/         CI 用的校验脚本
```

> ⚠️ 语言目录名必须是 `ChineseSimplified (简体中文)`（与引擎语言身份一致）。
> 写成裸 `ChineseSimplified` 会导致**整包翻译不加载**，游戏里全是英文。

## 已知限制与后续

- **花药台 / 法杖**：Botania 前期的「花瓣 + 水 → 成花」与森林法杖尚未实现，
  目前花瓣可以通过配方直接研磨成魔力尘；
- **全图抽取**：计划作为后期科技解锁，接口已收敛在 `Source/IManaSource.cs` 的 `ManaSourceUtil.FindNearest`；
- 16 色花目前只有颜色差异，未来考虑给主色加差异化功能；
- 火红莲单产偏高，等出现更多耗魔设备后再按总账调；
- 「魔力通量场」是否改名「魔力发电机」仍在讨论。

## 版权与致谢

- Botania 原作及其名称、设定归 **Vazkii** 所有；本项目是面向 RimWorld 的非官方移植，与原作者无关。
- 本仓库的 C# 代码、XML 定义与全部贴图（程序化生成）为本项目原创，以 [MIT](LICENSE) 授权。
