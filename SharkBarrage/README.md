# SharkBarrage —— 深渊之口 · 鲨鱼弹幕

把 **Shark**（Maw of the Deep / 深渊之口，DLC「女王与海」）改成"每次攻击都泼一屏鲨鱼"。

| # | 需求 | 实现 |
|---|---|---|
| ① | **删掉前两下平a**，每次攻击直接丢鲨鱼 | Hook `Weapon.get_cycle` → 对 Shark 恒返回 `2` |
| ② | 普通丢 **20** 只 / 传奇词条丢 **40** 只 | Hook `Shark.throwShark` 整段重写（原版 1 / 3） |
| ③ | 飞行距离 普通 **×2** / 传奇 **×5** | 每只 `shark.maxDist *= 倍数` |
| ④ | 全部**无视墙体** | 每只 `shark.ignoreWalls = true` |
| ⑤ | 扇形散射（默认 150°） | 以瞄准角为中心对称铺开，`FanAngleDeg` 可调 |
| ⑥ | **攻速 ×3** | 数据补丁：`strikeChain[*]` 的 `charge/3`、`lockCtrlAfter/3`、`animSpd*3` |

---

## 1. 原版是怎么走的

### 三段连击

`dc.tool.weap.Shark.onExecute()`（`GamePseudocode/dc.tool.weap/Shark.cs:53`）：

```csharp
bool flag = base.onExecute();          // Weapon.onExecute()：executeAffixes / onionSkin / onWeaponExecute
viewport.bumpDir(dir, 0.5);
int c = get_cycle();
if (c == 1)      { ...震屏 + breakBreakableGround + groundStones... }   // 第 2 下
else if (c >= 2) { owner.spr.get_anim().playCustomSequence("AtkSharkC_NOSHARK", 16, 32, null);
                   throwShark();  return true; }                        // 第 3 下 ← 丢鲨鱼
// c == 0 落下来走普通的"遍历敌方队伍 canHit → hitFromWeapon"挥砍        // 第 1 下
```

CDB 侧（`res/data.cdb` 的 `weapon` 表，`item == "Shark"`）：

| strikeChain | animId | fxId | power | area |
| --- | --- | --- | --- | --- |
| `[0]` | `AtkSharkA` | `fxSharkA` | `[135]` | 5×0 |
| `[1]` | `AtkSharkB` | `fxSharkB` | `[150]` | 4×0 |
| `[2]` | `AtkSharkC` | —（用自定义序列 `AtkSharkC_NOSHARK`） | `[120]` | 全 0 |

### throwShark()

`GamePseudocode/dc.tool.weap/Shark.cs:412`：

```csharp
double aim = getTargetAng(11 * 0.4, 0.2, null);        // 瞄准最近的敌人
AttackData atk = _AttackUtils.createFromHeroWeapon(this, null);
int n = 1;
if (item.hasAffix("TripleBullets")) n += 2;            // ← 传奇 = 3
double step = owner.dir != 1 ? 0.15 : -0.15;           // 每只错开 0.15rad ≈ 8.6°
for (int i = 0; i < n; i++)
{
    if (!consumeAmmo()) break;
    var shark = new dc.en.bu.Shark(hero, atk, aim + i * step, 1.1 + rnd * 0.1);
    shark.init();
    shark.tail = new BulletTail.Line(glowColor, 0.7, null);
    shark.shootFromWeapon(this, ..., ..., ...);
    fx.shoot(get_shootX(), get_shootY(), aim, 15297046);
}
```

### 距离和穿墙在哪

`dc.en.bu.Shark` 自己**完全不碰** `maxDist` / `ignoreWalls`，
两个值都来自基类构造 `dc.en._Bullet.__inst_construct__`（`GamePseudocode/dc.en/_Bullet.cs:159-189`）：

```csharp
arg1.maxDist = 11.0 * 24.0;                     // = 264px（11 格）
arg1.radius  = 24.0 * (isHero ? 0.66 : 0.4);    // 英雄子弹 ≈ 15.84px
arg1.ignoreWalls = false;                       // ← 默认撞墙
```

所以这两个就是我们能直接改的开关。

### 顺带确认：弹药不会限制数量

`Shark` 在 CDB 里的 `commonProps` 是 `{}` —— **没有 `ammo` 字段**。
`Weapon.consumeAmmo()`（`GamePseudocode/dc.tool/Weapon.cs:896-907`）：

```csharp
int? ammo = inventItem._itemData.commonProps.ammo;
flag2 = (object)ammo != null;
if (!flag2) return true;          // ← 没有 ammo 字段 → 永远 true，不会 break
int ammo2 = item.ammo;
if (0 >= ammo2) return false;
```

所以原版那个 `if (!consumeAmmo()) break;` 对鲨鱼是恒真的，20/40 只都能一次丢出来。

## 2. 本模组的两个改动

### ① 删掉前两下 —— 把 `get_cycle()` 钉死在 2

```csharp
private int OnGetCycle(Hook_Weapon.orig_get_cycle orig, dc.tool.Weapon self)
{
    int cycle = orig(self);
    if (!cfg.EnableMod || !cfg.AlwaysThrow) return cycle;
    if (self == null || self.destroyed || self.owner == null) return cycle;
    if (self is Shark) return 2;          // ← 只对武器 Shark（注意和 dc.en.bu.Shark 同名）
    return cycle;
}
```

这样 `Shark.onExecute()` 里 `c == 1` / `c == 0` 的普通挥砍分支永远不会走，
每次攻击都直接落到 `c >= 2` 那条 → 播 `AtkSharkC_NOSHARK` + `throwShark()`。

> 为什么改 cycle 而不是"让前两下空挥"：cycle 同时还决定 `get_curSkillInf()` 取哪一段
> strikeChain（蓄力 0.54s / 伤害 / `animId` / `area`），恒定 2 正好让这三样全用第三下那套，
> 连 `Weapon.onExecute()` 里的 `areas[get_cycle()]` 也跟着对上，不用另外打补丁。

### ② 重写 `throwShark()`

Hook `Hook_Shark.throwShark`，**不再调用 orig**，改成：

```csharp
double aim   = self.getTargetAng(4.4, (double?)0.2, null);          // 和原版同一套瞄准
bool legend  = self.item.hasAffix("TripleBullets");
int  count   = legend ? LegendSharkCount : SharkCount;              // 20 / 40
double range = legend ? LegendRangeMult  : RangeMult;               // ×2 / ×5

// 以瞄准角为中心对称铺开
double step  = (FanAngleDeg * PI / 180) / (count - 1);
double start = -step * (count - 1) / 2.0;

for (int i = 0; i < count; i++) {
    var shark = new dc.en.bu.Shark(hero, atk, aim + start + step * i, 1.1 + rnd * 0.1);
    shark.init();
    shark.tail = new BulletTail.Line(glow, 0.7, null);
    shark.shootFromWeapon(self, Ref<bool>.Null, Ref<double>.Null, Ref<double>.Null);
    shark.maxDist *= range;                  // ← ③ 距离
    shark.ignoreWalls = true;                // ← ④ 穿墙
    shark.ignoreOneWays = true;
}
```

**扇形角度为什么要改**：原版是每只固定错开 `0.15rad`，3 只总共 17°。
直接拿来铺 20 只就是 `19 × 0.15 = 2.85rad ≈ 163°`，40 只更是整个圆 —— 完全不像"一次齐射"。
所以改成**总角度固定**，只数越多每只越密。

但 60° 铺 20 只时每只只差 3.2°，出膛还是一大坨糊在一起 —— **默认给到 150°**（每只 7.9°），
飞出去会明显散成一把扇子。嫌太散/太密就改 `FanAngleDeg`。

写法参考仓库里已验证的用法：
`ThrowableStuff`（`stuff.shootFromWeapon(self, Ref<bool>.Null, Ref<double>.Null, Ref<double>.Null)`）、
`ChronoBlade`（`new dc.en.bu.Saw(hero, atk, ang, 0.5)`）。

### ③ 攻速 ×3 —— 走数据补丁

"攻速"由 strikeChain 里三个字段决定（`res/data.cdb` 的 `weapon` 表，`item == "Shark"`）：

| 字段 | 含义 | 原版 `[2]` | 现在 |
| --- | --- | --- | --- |
| `charge` | 出招前摇（秒）—— 按下去到命中要等多久 | `0.54` | **`0.18`** |
| `lockCtrlAfter` | 命中后锁控制时长 —— 也决定能不能马上接下一段 | `0.35` | **`0.1167`** |
| `animSpd` | 动画播放倍速（`[0]`/`[1]` 是 `1.3`，`[2]` 没写 = `1.0`） | `1.0` | **`3.0`** |

由 `patch_shark_cdb.py` 生成（顶部 `ATTACK_SPEED_MULT = 3.0`），走 res.pak 数据补丁：

```
patch_shark_cdb.py  -> SharkBarrage/data.cdb
  -> cdb diff + pak unpack  -> Assets/data.cdb_/weapon/Shark.json
  -> PackAssets             -> res.pak -> coremod/mods/SharkBarrage/
  -> IOnAfterLoadingAssets 里 FsPak.loadPak() 手动挂载
```

> **⚠ 为什么必须手动挂载**：MDK 只把 `res.pak` **复制**到 mods 目录，DCCM 不会自动挂它。
> 不挂的话 `CDBManager` 读不到 `data.cdb_` 补丁，攻速就不会 ×3（其余四项是运行时的，照常生效）。
> 日志里应出现 `[SharkBarrage] res.pak 已挂载，攻速 x3 补丁生效`。

## 3. 文件

```
SharkBarrage/
├── patch_shark_cdb.py               # Python：攻速 x3 -> SharkBarrage/data.cdb
├── README.md
└── SharkBarrage/                    # csproj 目录
    ├── SharkBarrage.csproj          # MDK：cdb diff -> Assets -> res.pak -> 自动安装
    ├── SharkBarrageMain.cs          # 两个 Hook + res.pak 挂载
    ├── SharkBarrageConfig.cs        # 配置
    ├── data.cdb                     # 脚本生成的修改版 CDB（构建输入）
    └── Assets/data.cdb_/weapon/Shark.json   # 差异补丁（打进 res.pak）
```

数量 / 距离 / 穿墙 / 散射都是**运行时**改的（`new dc.en.bu.Shark(...)` + 改 `maxDist`/`ignoreWalls`），
不需要动数据表；只有**攻速**走 CDB 补丁。所以 `res/` 与 `GamePseudocode/` 全程只读。

## 4. 配置

`coremod/config/SharkBarrage.json`（首次运行自动生成）：

| 字段 | 默认 | 作用 |
| --- | --- | --- |
| `EnableMod` | `true` | 总开关（关掉完全回原版） |
| `AlwaysThrow` | `true` | 删掉前两下，每次攻击都直接丢鲨鱼 |
| `SharkCount` | `20` | 普通词条一次丢几只（原版 1） |
| `LegendSharkCount` | `40` | 传奇词缀 `TripleBullets` 一次丢几只（原版 3） |
| `RangeMult` | `2.0` | 普通飞行距离倍数（原版 264px） |
| `LegendRangeMult` | `5.0` | 传奇飞行距离倍数 |
| `IgnoreWalls` | `true` | 无视墙体 |
| `FanAngleDeg` | `150.0` | 扇形总角度（度）。20 只用 60° 会糊成一坨，所以默认给到 150° |
| `PlayMuzzleFx` | `true` | 枪口特效（20 只叠 20 次太吵，只放一次） |

改完直接生效，**不用重新编译**（配置在每次丢鲨鱼时读取）。

> **攻速不在这个配置里** —— 它在 `patch_shark_cdb.py` 顶部的 `ATTACK_SPEED_MULT`，
> 改完要 `python patch_shark_cdb.py` + `dotnet build`（因为是数据补丁）。

## 5. 构建

```powershell
# 改过攻速倍率才需要重跑
python SharkBarrage/patch_shark_cdb.py

dotnet build SharkBarrage/SharkBarrage/SharkBarrage.csproj -c Debug
```

## 6. 日志核对

```
[SharkBarrage] 已加载：每次攻击直接丢鲨鱼（删前两下）=20 只 / 传奇 40 只、距离 ×2 / 传奇 ×5、无视墙体=True、扇角 150°
[SharkBarrage] res.pak 已挂载，攻速 x3 补丁生效: ...\mods\SharkBarrage\res.pak
[SharkBarrage] 丢出 20 只鲨鱼（普通）：瞄准角 0.123、扇角 150°、每只间隔 0.1378rad、距离 ×2（单只 528px）、无视墙体=True
[SharkBarrage] 丢出 40 只鲨鱼（传奇）：瞄准角 0.123、扇角 150°、每只间隔 0.0671rad、距离 ×5（单只 1320px）、无视墙体=True
```

## 7. 注意

* **传奇判定**用的是原版那一个词缀：`item.hasAffix("TripleBullets")`
  （CDB 里 `item/Shark.legendAffixes = [{affix: "TripleBullets"}]`）。
  所以必须拿到**传奇**的深渊之口才会走 40 只 / ×5 那一档。
* 只有 Shark 这把武器被改；别的武器/别的子弹不受影响
  （`get_cycle` 那个 Hook 对所有武器都会触发，但里面 `self is Shark` 直接挡掉了）。
* 20 只鲨鱼同时飞，是真的会掉帧 —— 嫌卡就把 `SharkCount` 调小，或把 `FanAngleDeg` 调大让它们散开。
* 鲨鱼本身的行为（缠绕 Root / 流血 Bleed / DoT / 撞墙掉弹药）完全没动，只改了数量、距离、穿墙、攻速。