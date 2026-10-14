# RainbowCrit — 全局彩色暴击

把《死亡细胞》里 **Hotline Miami 服装专属的「彩色暴击数字」** 做成全局特性：
**任何服装**打出的暴击，伤害数字都会变成 Hotline Miami 风格的流动彩虹字。

## 一、原版特性是怎么实现的（反编译定位）

`GamePseudocode/dc/Entity.cs` → `popDamage(AttackData a)`：

```
皮肤判断：HotlineMiamiChicken / HotlineMiamiHorse / HotlineMiamiOwl
    ↓ 命中
if (a.hasTag(2))                     // 2 = 暴击标记 Crit
    _PopDamageHotline.create(this, a, dmgIdx, big, {chars:"numbers", font:"hotline"})
else
    _PopDamage.create(...)           // 普通伤害数字
```

`GamePseudocode/dc/ui/popd/_PopDamageHotline.cs` 里：

* 每个数字是个 `dc.ui.Text`，字体用 `fonts/exported/hotline_20|30|45.fnt`
  （`dc/_Assets.cs` 里 `font == "hotline"` 分支）；
* 主数字上挂了 `dc.shader.HotlineText`（`dc/shader/_HotlineText.cs`），
  这个着色器按 `time / pos` 循环取色 —— 这就是「彩色暴击」的来源。

所以本质上这是一个 **只对三套 Hotline Miami 服装开放的 if 分支**。
本模组做的事情就是把这个分支解放到全局。

## 二、实现方式

`RainbowCritMain.cs` 钩住 `Hook_Entity.popDamage`：

1. 判断 `a.hasTag(2)`（暴击）且「伤害来源属于玩家」；
2. 满足则自己调用原版同一条链路
   `PopDamageHotline.Class.create(self, a, self.dmgIdx, big:true, {chars:"numbers", font:"hotline"})`，
   然后 **不调用 `orig`**（原版这条分支同样只创建一个数字，避免数字重复）；
3. 不满足则原样 `orig(self, a)` 交回原版。

同时照抄了原版 `popDamage` 后半段的两个限流冷却（`cd.fastCheck` 的
`356515840` / `272629760`，0.3s / 0.4s）和 `dmgIdx` 自增，
避免高频暴击把彩虹字叠爆（原版同屏上限 64 个，达到上限时回退原版数字）。

## 三、配置

`coremod/config/RainbowCrit.json`（首次运行自动生成），或游戏内
**选项 → 模组 → 全局彩色暴击**：

| 项 | 默认 | 说明 |
|---|---|---|
| `EnableMod` | `true` | 总开关，关掉完全恢复原版 |
| `PlayerCritsOnly` | `true` | 只美化玩家打出的暴击；关掉则怪物暴击玩家时也是彩虹字 |
| `DisableRainbow` | `false` | 调试开关：开启后不接管，走原版数字，便于对比 |

## 四、构建 / 安装

```powershell
cd RainbowCrit\RainbowCrit
dotnet build -c Debug          # AutoInstallMod=true，自动装到 coremod\mods\RainbowCrit
```

Release 构建需要手动把 `bin\Release\net10.0\output\RainbowCrit\` 里的文件
复制到 `D:\steama\steamapps\common\Dead Cells\coremod\mods\RainbowCrit\`。

装好之后 **重启游戏** 才会加载。

## 五、依赖说明

* 只依赖 DCCM 框架本身（`Hook_Entity` / `HaxeProxy`），**不依赖 CoreLibrary**。
* 与 `MCDcrit`（暴击骷髅动画）互不冲突：那个钩的是 `Hook_Fx.critical`，本模组钩的是 `Hook_Entity.popDamage`。
* 如果装了 CoreLibrary：它的 `CustomPopDamage` 也会整体接管 `popDamage`。
  两者同时启用时后者谁生效取决于 Hook 链顺序，建议二选一。
