# 五条悟 · GojoLimitless（无下限）

Dead Cells（v35 / DCCM）能力模组。把《咒术回战》五条悟的能力用**游戏原有的机制**拼出来：
不新增任何战斗贴图/动画，招式全部复用原版的 affect、FX 与攻击管线。

> `GamePseudocode/`（反编译伪代码）与 `res/`（游戏资源）只读引用，本模组**不修改**其中任何文件。

---

## 一、能力清单

| 能力 | 默认键 | 效果 | 复用的原版机制 |
|---|---|---|---|---|
| **无下限**（被动） | — | — | 屏障吸收一切来犯攻击；半径内敌人被迟滞；敌方子弹直接被湮灭；青色呼吸光圈 | `Hook_Entity.applyAttackResult` 改 `finalDmg`、`Hook_Mob.getMoveSpeedMul` 压移速、`Bullet.vanish()`、`EntityLight` |
| **术式顺转·苍** | `J` | **控制招**：大范围强牵引（吸向自身）+ 低伤害 | `bdx/bdy` 凹凸位移 + `AttackUtils.createFromHero/hit` |
| **术式反转·赤** | `K` | **击飞招**：大范围强斥力 + 上抛 + 中伤害 | 同上（方向反转）+ 额外 `bdy` 上抛 |
| **虚式·茈** | `L` | 面向的锥形长距贯穿，超高伤害 + 击退 | 锥形判定 + 原版贯穿光带 `Fx.longHitLine` |
| **领域展开·无量空处** | `U` | 大范围定身 + 持续伤害 + 短暂无敌 | affect `28`（定身）、affect `48`（无敌）、`Hook_Mob.getMoveSpeedMul` |
| **强制开启**（测试用） | `O` | 门槛开着时才需要；默认不需要 | — |
| **模组菜单** | `F10` | 覆盖层菜单（不暂停也能开）；原生页在「选项 → 模组」 | 反射调 `Game.modalPause` 真暂停 |

> **默认不需要任何道具 —— 按键即用。** 招式纯靠按键触发，全部复用原版管线。

### 按键：模组自己存键名（v7 定稿）

按键由**模组自己存、直接轮询 `GetAsyncKeyState`**（`GojoKeys` + `Input`）。
默认键全部挑死亡细胞里原本空着的：

| 功能 | 默认键 | 说明 |
|---|---|---|
| 术式顺转·苍 | `J` | |
| 术式反转·赤 | `K` | |
| 虚式·茈 | `L` | |
| 领域展开·无量空处 | `U` | |
| 强制开启 | `O` | |
| 覆盖层菜单 | `F10` | |

可用按键：`J K L U I O H`、`A`-`Z`、`0`-`9`、`F1`-`F12`、`Space`、方向键、标点、`0xNN`；
留空 = 不绑。**两个菜单都能改键**：点到那一行按 Enter（或 →），再按下要绑的键即可。

> **为什么不用"读原版动作绑定"**（v5/v6 试过，已放弃）：
> 通过 `dc.Boot.ME.controller.get_bindings()` / `hero.controller.parent` 都**能读到**
> 绑定（日志实证 `CAM_LEFT=74`），但**按键检测不动** —— 诊断日志里所有动作恒为"松开"，
> 招式的 `Pressed()` 永远返回 false。排查成本高于收益，所以退回模组自己轮询，
> 这条链路在 v1~v4 和仓库里其它模组上都是验证可用的。
>
> 迁移：首次加载会自动读一次游戏自己的 `save/dc_options.json`，把 v5/v6 里存的动作名
> 换算回**当时那个动作实际绑的键**，尽量保住你已经习惯的键位。


### 苍 / 赤 的定位（P0 调整）

原来的默认值是"两个都是高伤害 AOE"，读起来像两个一样的技能。现在按需求拆开：

| | 伤害 | 半径 | 力度 | 冷却 | 定位 |
|---|---|---|---|---|---|
| **苍** | 150（低） | 12 格（大） | 1.10（**越远吸得越狠**，外圈 ×2） | 3.0s | 把人拽过来，方便平砍 / 接赤 |
| **赤** | 220（中） | 10 格 | 2.20（约苍的 2 倍，**非 Boss 额外上抛**） | 3.0s | 把贴身的一圈直接掀飞 |

**Boss / 精英**（`dc.en.mob.Boss` 子类，或 `Mob.elite`）受到的牵引与击退额外乘
`BossPullPushFactor`（默认 **0.40**），再叠加原版 `getBumpResistanceFactor()` —— 所以 Boss 只会被轻微拖动，
不会被当小怪推着走。这个系数也能在菜单「战斗与全局」页里调（0 ~ 1）。

### 连招：苍 + 赤 → 茈

先用「苍」（默认 `J`），**3.0 秒内**按「赤」（默认 `K`），
不会打出「赤」而是直接接出**虚式「茈」** ——这就是原作里"苍与赤相撞"的演出。
如果「茈」还在冷却，就退回普通「赤」。
窗口内 HUD 会显示 `COMBO: K -> 虚式·茈`（键名跟着配置走，改完键这里也变）。

### 无下限（被动）的三层判定

1. **屏障吸收** —— 敌人打向你的攻击先过"咒力储量"：
   * 单次吸收上限 = 储量上限的 35%（不会一发被吸干）
   * 储量够 → `finalDmg` 清零，这一击**完全不生效**
   * 储量被打穿 → 这一发实打实落在身上，并触发 0.6 秒"碎裂停顿"（期间回充降到 35%）
   * 小怪连续输出基本无伤，Boss 的大伤害能打穿 —— 这就是"无限接近但永远到不了"的数值化
2. **迟滞领域** —— 半径内敌人按"距离越近越慢"被压低**移动速度**（贴脸只剩 25%），
   离开后按 `SlowLinger` 平滑恢复。实现见下面「实现要点 → 2」。
3. **湮灭弹幕** —— 飞进半径的敌方子弹直接消失（`Bullet.vanish()`，弹药回收/尾巴特效照常）。

### 呼吸光圈（P0 新增）

`EntityLight` 挂在英雄身上，每帧：

* **半径** 按 0.9 Hz 的正弦呼吸，幅度 ±20% × `AuraBreath`
* **强度** 同步呼吸；被挡下时闪到 2.2，碎裂时压到 0.25
* **颜色** 按优先级：
  碎裂闪红 → 领域期间**紫黑**（`0x6A4CFF`）→ 咒力低于 `AuraLowReserveRatio`（默认 30%）转**红** → 平时**青蓝**
* 整段可以在菜单里关（`显示呼吸光圈`）或调幅度

---

## 一·二、内置 HUD

左上角 8 行（位置 / 行高 / 文字缩放 / 开关都在菜单「界面与系统」页）：

```
GOJO  LIMITLESS
INF  [########....]  312/420
J    术式顺转·苍        [READY]
L    术式反转·赤        [##..........] 1.4s
M    虚式·茈            [............] 12.8s
R    领域展开·无量空处   [READY]
SLOWED FOES 7
COMBO: L -> 虚式·茈
```

* 咒力条：`#` 是剩余、`.` 是空的；低于 30% 变红、60% 变橙
* 招式行：就绪显示 `[READY]`（按招配色：苍蓝 / 赤红 / 茈紫 / 领域紫），
  冷却中显示进度条 + 秒数；被菜单关掉的招显示 `[off]`
* 最左边的键名来自配置（菜单里改完键，这里立刻跟着变）

### HUD 的定位与缩放（v6 定稿）

20 行 × 2 个 `dc.h2d.Text`（1px 阴影 + 正文）**各自直接挂在 `level.root` 的 UI 层**，
每行用**绝对坐标**定位 —— 和 `KillSwapWeapon` / `CameraMod` 一样，是仓库里验证过能渲染的写法。

> **踩坑记录**：中间试过"整块 HUD 挂一个 `dc.h2d.Object` 父容器、只缩放容器"的方案，
> 实机诊断（日志 `HUD 布局诊断`）证明在这套 hashlink 代理下**走不通**：
> ```
> 容器 x=28 y=116 scaleX=0.85 posChanged=True
> 行0 y=0     absY=0
> 行1 y=0.938 absY=0      ← 局部 y 分开了，引擎算出的 absY 全是 0
> ```
> 子节点的绝对变换不会被重算（`posChanged` 置在容器上也不管用），20 行全叠在同一处。

缩放与行距（这才是"放大后叠层"的正解）：

* 每行文字自己 `scaleX/scaleY = scale`；
* **行距 = `HudLineHeight` × `HudScale` × `HudRowSpacing`** —— 字号变大时行距同比例变大，
  所以放大不会让行互相压住（旧写法行距写死，字号 ×3 就有 36px 高、16px 行距，必然叠）；
* 阴影偏移固定 1px 屏幕像素，放大后仍是细边，不会糊成一片；
* `HudRowSpacing`（默认 **1.35**）就是"把每行距离拉开"的旋钮，菜单里可调。

文字对象只在**关卡变化**时建一次，之后只清空文字 / 切 `visible`，从不 `removeChild` ——
所以"拖动缩放滑条时出现残影"那条老路也彻底断了。
* 状态行：领域进行中显示 `DOMAIN ACTIVE 3.2s`，否则显示被迟滞的敌人数
* 连招行：只在连招窗口内出现

---

## 一·三、设置入口（两个，读写同一份配置）

### A) 游戏原生「选项 → 模组 → GojoLimitless」（推荐）

和 `ZoomVision` / `CameraMod` / `EchoVision` 一样实现 `ModCore.Menu.IModMenu`，
所以它会作为**独立一页**出现在游戏自己的选项菜单里，用原版控件、原版操作方式：

* 总开关
* **无下限** —— 启用 / 屏障半径 / 咒力储量 / 储量每级 / 回充 / 吸收上限 / 破裂停顿 / 破裂回充 / 迟滞倍率 / 迟滞尾巴 / 湮灭子弹 / 光圈开关 / 呼吸幅度 / 变红阈值
* **苍 / 赤 / 茈** —— 启用 / 冷却 / 伤害 / 半径 / 力度 / 持续
* **领域展开** —— 启用 / 冷却 / 每跳伤害 / 半径 / 力度 / 持续 / 移速倍率 / 跳间隔 / 领域内无敌
* **战斗** —— Boss 牵引击退系数 / 连招窗口
* **按键** —— 6 行**只读显示**：告诉你每个功能对应哪个原版动作、那个动作现在是哪个键。
  想改键请去**原版「选项 → 控制」**（见下）
* **界面** —— HUD 开关 / X / Y / 行高 / 文字缩放 / 日志行数 / 详细日志

改任何一项都会立刻写盘，并同步生效。

### B) 游戏内覆盖层菜单（默认 `H`）

不依赖原版选项界面，**游戏里随时能开**（打开时会尝试真暂停）。
同样是全部数值 + 「恢复全部默认值」。适合"打一半想调个手感"。

> 两个入口共用 `coremod/config/GojoLimitless.json`，所以从哪边改都一样。
> 覆盖层菜单打开时 HUD 会自动收起，避免文字叠在一起。

### 按键怎么工作（v7：模组自己存 + 自己轮询）

1. 配置里的 `"Key"` 存的是**键名**（`"J"` / `"F10"` / `"Space"` / `"0x51"` …）；
2. 每帧 `GojoKeys.Bind()` 把它解析成 VK 码，**顺手把键加进 `Input` 的跟踪集**
   （所以菜单里改完键当帧就能用，不会出现"新键第一次按被吃掉"）；
3. 触发判定 = `Input.Pressed()`，内部是 `GetAsyncKeyState` 的**边沿触发**
   （只在按下的那一帧为 true）。

于是：

* **改键就在模组自己的菜单里改**：点到那一行按 `Enter`（或 `→`），再按下要绑的键；
* 也支持直接编辑 `coremod/config/GojoLimitless.json` 里的 `"Key"`，换关 / 重开一局生效；
* 不依赖任何游戏内部状态，所以不存在"游戏还没初始化好所以读不到"的问题。

#### 为什么默认是 J / K / L / U / O

实测原版默认键盘表（`save/dc_options.json`）之后挑的 —— 这几个键在原版里**都是空的**，
不会和跳跃 / 攻击 / 技能抢输入。

| 功能 | 默认键 | 为什么 |
|---|---|---|
| 苍 | `J` | 原版空着 |
| 赤 | `K` | 原版空着 |
| 茈 | `L` | 原版空着 |
| 领域展开 | `U` | 原版空着 |
| 强制开启 | `O` | 原版空着 |
| 覆盖层菜单 | `F10` | 原版没用到功能键区 |

---

## 一·四、覆盖层菜单速查（默认 `F10`）

打开时用反射调原版 `Game.modalPause` **真暂停游戏**，所以可以慢慢调。

| 操作 | 键 |
|---|---|
| 选择条目 | `Up` / `Down`（按住连发） |
| 数值 -/+ | `Left` / `Right`（按住 `Left Ctrl` 是 10 倍步长） |
| 开关项取反 / 按键项进捕获 / 动作项执行 | `Enter` |
| 上一页 / 下一页 | `PageUp` / `PageDown` |
| 保存并关闭 | `Esc` |
| 捕获中取消 | `Backspace` / `Esc` |

> 数值改动会在关闭菜单时统一写盘（避免按住方向键时每秒写十几次文件）。

---

## 二、安装

模组已经构建好，`bin/Debug/net10.0/output/GojoLimitless/` 里那几个文件就是全部内容
（`GojoLimitless.dll`、`modinfo.json`，`GojoLimitless.pdb` 可选）。

把它们复制到：

```
<游戏目录>\coremod\mods\GojoLimitless\
```

即最终结构：

```
coremod/
├── config/
│   └── GojoLimitless.json      ← 首次启动自动生成（可手改）
└── mods/
    └── GojoLimitless/
        ├── GojoLimitless.dll
        ├── GojoLimitless.pdb     （可选，调试符号）
        └── modinfo.json
```

重启游戏即可。启动日志（`coremod/logs/`）里会看到：

```
[GojoLimitless] 招式已注册: 无下限[被动] 术式顺转·苍[J] 术式反转·赤[K] 虚式·茈[L] 领域展开·无量空处[U]
[GojoLimitless] 已加载 —— 术式顺转·苍(J) / 术式反转·赤(K) / 虚式·茈(L) / 领域展开·无量空处(U)
[GojoLimitless] 菜单键 F10 / 强制开启键 O；按键是模组自己轮询的，可在菜单或配置里改

[GojoLimitless] HUD 已创建（池 20 行 × 2，逐行绝对定位，字体 font12）
```

> 如果撞键了，上面最后那条会变成两行，第二行是
> `检测到按键冲突 —— 按一下会同时触发两个功能…`，点名是哪个键被哪些动作共用。

---

## 三、按键（v7：模组自己存键名）

配置里的 `"Key"` 存的是**键名**，运行时由 `GojoKeys` 解析成 VK 码并轮询。

| 功能 | 配置默认值 | 默认键 |
|---|---|---|
| 术式顺转·苍 | `"J"` | `J` |
| 术式反转·赤 | `"K"` | `K` |
| 虚式·茈 | `"L"` | `L` |
| 领域展开·无量空处 | `"U"` | `U` |
| 强制开启 | `"O"` | `O` |
| 覆盖层菜单 | `"F10"` | `F10` |

可用写法：`J K L U I O H`、`A`-`Z`、`0`-`9`、`F1`-`F12`、`Space`、`Tab`、`Enter`、
`Esc`、方向键、`NumPad0`-`9`、标点（`Minus` / `Equals` / `Comma` …）、或十六进制 `0x51`。
留空 = 不绑键。

**改键两种方式**：菜单里点那一行按 `Enter` 再按新键；或直接编辑
`coremod/config/GojoLimitless.json`。

> 从 v5/v6 升上来时，配置里存的是动作名（`cam_left` 之类）。首次加载会自动读一次
> 游戏自己的 `save/dc_options.json`，把它换算成**当时那个动作实际绑的键**，
> 尽量保住你已经习惯的键位（换算结果会写进日志）。

---

## 四、配置

首次启动生成 `coremod/config/GojoLimitless.json`。**改文件的两种生效方式**：
换个关（`Hook_Hero.init` 会重读），或者开一次覆盖层菜单再 `Esc`（也会重读）。
菜单里改的值会立刻写盘。

顶层结构：

```jsonc
{
  "ConfigVersion": 7,
  "Infinity": { "Enabled": true, "Radius": 6, "Reserve": 420, ... },
  "Blue":   { "Enabled": true, "Key": "J", "Cooldown": 3,   "Damage": 150,  "Radius": 12, "Power": 1.1,  "Duration": 0 },
  "Red":    { "Enabled": true, "Key": "K", "Cooldown": 3,   "Damage": 220,  "Radius": 10, "Power": 2.2,  "Duration": 0 },
  "Purple": { "Enabled": true, "Key": "L", "Cooldown": 14,  "Damage": 4200, "Radius": 26, "Power": 1.1,  "Duration": 0 },
  "Domain": { "Enabled": true, "Key": "U", "Cooldown": 40,  "Damage": 900,  "Radius": 22, "Power": 0.3,  "Duration": 5,
              "SlowMultiplier": 0.2, "TickInterval": 0.5, "Invincible": true },
  "BossPullPushFactor": 0.4,
  "ComboWindow": 2.5,
  "Ui": { "ModEnabled": true, "HudEnabled": true, "MenuKey": "F10",
          "ForceEnableKey": "O", "HudX": 12, "HudY": 276, "HudLineHeight": 16,
          "HudScale": 1.0, "HudRowSpacing": 1.35, "HudShowLog": false, "HudLogLines": 4, ... }
}
```

**版本与默认值**：`ConfigVersion` 变更时会自动把缺失字段补成默认值并写回；
手改文件写出越界值（负数、NaN、超大数、无效动作名）会被夹回合法区间，不会炸游戏。
菜单最后一页有「恢复全部默认值」。

> 伤害数值都是**不吃英雄属性加成**的固定值（`useHeroScaling = false`，
> 因为 `scaleMobValueToTier` 在 tier 1 是恒等映射），所以不会因为卷轴堆叠而爆炸。

---

## 五、目录结构

```
GojoLimitless/
├── README.md
├── workshop_description.txt
└── GojoLimitless/                     ← 内层项目目录
    ├── GojoLimitless.csproj           ← 只有 MDK 一个包引用，无自定义 MSBuild Target
    ├── GojoLimitlessMain.cs           ← 入口：生命周期、屏障拦截、每帧调度
    ├── GojoConfig.cs                  ← 配置模型 + Config<T> 持久化 + 版本迁移 + 越界夹取
    ├── KeyNames.cs / GojoKeys.cs      ← 键名 ↔ 虚拟键码（模组自己存键位）
    ├── GojoMenu.cs                    ← 覆盖层菜单（暂停游戏 + 逐行绝对定位 + 原生导航键）
    ├── GojoHud.cs                     ← 左上角 HUD（逐行绝对定位 + 咒力条 / 冷却条 / 连招提示）
    ├── GojoUtil.cs                    ← 坐标/选怪/伤害/affect/位移/Boss 重量 工具
    ├── GojoFx.cs                      ← 表现层（只调原版 FX）
    ├── GojoPalette.cs                 ← 咒术配色
    ├── Input.cs                       ← GetAsyncKeyState 边沿触发 + 跟踪集由原版绑定驱动
    ├── Log.cs                         ← 日志出口
    ├── Abilities/
    │   ├── GojoAbilityBase.cs         ← 招式基类（配置读值 / 冷却 / 触发 / 总开关门槛）
    │   ├── GojoHub.cs                 ← 招式调度 + 总开关刷新 + 连招提示
    │   ├── InfinityAbility.cs         ← 无下限（被动 + 呼吸光圈）
    │   ├── SlowAura.cs                ← 迟滞领域（hook Mob.getMoveSpeedMul，热路径零分配）
    │   ├── BlueAbility.cs             ← 术式顺转·苍
    │   ├── RedAbility.cs              ← 术式反转·赤
    │   ├── PurpleAbility.cs           ← 虚式·茈
    │   └── UnlimitedVoidAbility.cs    ← 领域展开·无量空处
    └── GojoModMenu.cs                 ← 游戏原生「选项 → 模组」页（IModMenu）
```

> **本模组不含任何 `res.pak` / cdb 数据补丁 / 自建道具。** v4 起把原来的
> 「Limitless」道具连同 `make_gojo_assets.py` 那套资源生成链路一起删掉了
> （csproj 里 `GenerateDiffCDB` / `GenerateSinglePakFile` 都是 `false`），
> 招式纯靠原版动作触发、全部复用原版 affect / FX / 攻击管线。

---

## 六、构建

```bash
cd GojoLimitless/GojoLimitless
dotnet build                    # AutoInstallMod=true 时自动装到 coremod/mods/GojoLimitless/
```

> 如果只想构建不安装（比如没有写 `coremod/mods` 的权限）：
> `dotnet build -p:AutoInstallMod=false`，产物在 `bin/Debug/net10.0/output/GojoLimitless/`。
>
> `bin/.../output/GojoLimitless/` 里只需要 `GojoLimitless.dll` + `modinfo.json`（可选 `.pdb`）。

---

## 七、实现要点

### 1. 屏障为什么要改 `finalDmg`

`Entity.applyAttackResult(AttackData a)` 的执行顺序是：

```
life>0 检查 → lastAtkData 赋值 → 读 a.finalDmg → 减伤/affect 处理 → 扣血
```

**一进函数就读 `finalDmg`**，所以在 `orig` 之前把它清零 = 这一击完全没发生
（`0 >= a.finalDmg` 会直接 return）。

清零之后本模组**照常放行 `orig`**（而不是自己 return）—— 这样 `Hero.applyAttackResult`
（`activeSkillsManager.onTryApplyAttackResult` 等）仍然能看到"有人打过我"，
不会把英雄侧的技能反应整条吞掉。

入口选 `Hook_Entity.applyAttackResult` 与仓库里 `DamageAuraBoost` / `AutoParry` /
`Katana` / `KillAllScrolls` 完全一致。

### 1.5 为什么用两条心跳驱动

| 回调 | 暂停时会不会触发 | 用途 |
|---|---|---|
| `IOnFrameUpdate.OnFrameUpdate` | **会**（模组框架直接回调，不依赖 Hook 织入） | 菜单、HUD、按键刷新 |
| `IOnHeroUpdate.OnHeroUpdate` | 不会（游戏暂停 → 英雄不更新） | 保底驱动 + 常规玩法 |

菜单打开时会 `modalPause` 真暂停，如果输入只挂在 `IOnHeroUpdate` 上，
**一打开菜单就再也收不到输入了**。所以菜单挂在 `IOnFrameUpdate` 上。
两条都会走同一个 `TickAll`，用"同一个 dt 只处理一次"去重，避免同一帧跑两遍。

### 2. 减速为什么不能走 affect（以及怎么做的）

最初想用原版 affect `133`（`Mob.get_slowFactor()` 读它的层数）。读了反编译代码之后发现两条路都堵死：

```csharp
// Entity.setAffectS —— 层数是由 **value 参数** 决定的，不是调用次数
num15 = (int)num;                       // num = val
while (num2 < num15 - 1) { ...push 同一条 affect... }

// 而且 133 还有一条"桶非空就直接 return"的早退
if (x == 133) { if (0 < count(affect23)) return; }

// Mob.get_slowPerStack / get_slowFactor
double slowPerStack = 1.0 / thawMaxStacks;   // thawMaxStacks = 99999
num = 1.0 - slowPerStack * countAffect(133);
```

就算把 99999 层硬叠上去，每层也只减 0.001% 移速；而 133 真正的用途是**冰冻/解冻计数器**
（叠满会 `thawStackExplosion()`）。

所以迟滞改成 hook 原版唯一的移速乘数出口：

```csharp
public static double HookGetMoveSpeedMul(Hook_Mob.orig_getMoveSpeedMul orig, Mob self)
{
    double baseVal = orig(self);
    if (!GojoHub.AbilitiesActive) return baseVal;              // 未激活时零开销
    if (Affected.TryGetValue(self, out Entry e) && e.Ttl > 0)  // 一次字典查找
        return baseVal * e.Mul;                                // 0.02 ~ 1.0
    return baseVal;
}
```

`Mob.getMoveSpeedMul()` 被 `MobWalk`、`Fly`、`Demon`、`Bomber`、`Harpy` … 几乎所有
行走/飞行/突进都乘过一遍，所以一个 hook 就能覆盖绝大多数敌人。倍率按距离平方衰减：
贴脸 ×`SlowMinMultiplier`（默认 0.25），到半径边缘回到 ×1.0；领域展开期间是 ×0.20。

> **性能红线**：`HookGetMoveSpeedMul` 会被**每个敌人每一帧**调用多次，
> 所以它内部**只做一次字典查找 + 一次乘法**：不读配置、不分配对象、不打日志、
> 不遍历敌人表。`SlowAura.Affected` 用 `Dictionary<Mob, Entry>` 且 `Entry` 是
> **struct**（避免元组装箱），过期的条目由一个复用的 `List<Mob>` 收集后统一移除。
> 标记工作全部由「无下限」每 0.1 秒做一次。
>
> 只压**移动**，不动攻击/施法速度：原版除 affect 133 外没有干净的攻速乘数出口，
> 强行改会牵动冰冻机制，得不偿失。

### 2.5 Boss / 精英为什么要单独打折

原版 `Entity.getBumpResistanceFactor()` 对 Boss 的抗性并不高，所以「苍/赤」这种大力度位移
会把 Boss 当小怪推着走。现在 <code>GojoUtil.WeightFactorFor(mob)</code> 判断
`mob.elite` 或 `mob is dc.en.mob.Boss`，命中就再乘一道 `BossPullPushFactor`（默认 0.40）：

```csharp
double f = power * (1.0 - mob.getBumpResistanceFactor()) * resistFactor;
```

两道抗性相乘 → Boss 只会被轻微拖动；小怪照常被吸/被掀飞。
单次给的速度上限也从 4 提到 8 格/帧（「赤」的击飞需要更大的瞬时速度才看得出来）。

### 3~6. 【历史存档】自建道具「Limitless」相关的坑（代码已删除，仅留作记录）

> ⚠️ **下面 3~6 节描述的道具 / `res.pak` / cdb 数据补丁在 v4 起已全部删除**，
> 对应的 `make_gojo_assets.py`、`Assets/`、`InfinityAbility.FindLimitless`、
> `FsPak` 加载都不在代码里了。留着是因为踩坑过程本身有参考价值（尤其是
> "分组靠位置反解"和 "`FsPak` 在 `ModCore.Modules` 而不是 `dc.hxd.fs`"这两条），
> **但不要照着它去找现在的实现** —— 现在没有道具这回事。

#### 3. 道具判定的坑：`Inventory.hasItem` 对自定 id 恒为 false

`Inventory.hasItem(String k)` 的实现是逐个物品比较
`InventItemKind`（`new InventItemKind.Active(id)` 这种枚举包装对象）与 `String` 的相等
（`Inventory.cs:4585-4634`）——两者永远不相等。所以**不能用它**判断"有没有 Limitless"，
本模组改成直接扫 `inventory.items` 并读 `InventItem._itemData.id`
（`InfinityAbility.FindLimitless`，0.25 秒节流一次）。

### 4. 道具是"按位置反解分组"的

`item` 表的分组由行在表里的**位置**反推（`separators` 数组）。新行必须插进
`Power` 段（`[26, 78)`）**内部**，DCCMTool 输出的补丁才会带
`__separator_group_Name = "Power"` / `group = 3`；追加到表尾会被算成
`BossRushStatueUnlock(16)`，力量道具会被背包系统直接无视。
`make_gojo_assets.py` 里插在 `26 + 4 = 30` 这个位置。

另外 `item.tags` 必须**留空**：一旦带上 `PassivePower` 标签，
`_PassivePower.create()` 会因为找不到 `Limitless` 的构造分支而**抛异常**
（`_PassivePower.cs:305-317`）。留空则两条会走到它的路径都被 `hasTag("PassivePower")` 挡掉，
使用这个道具只是"什么都不发生"（`spawnPowerSkill` 返回 false）。

#### 5. `InventItem.fromItem` 对未知 id 会抛，不是返回 null

```csharp
// dc.tool._InventItem.cs:660-666
var d = Data.Class.item.byId.get(k);
if (d == null) throw "Invalid ItemKind: " + k;
```

所以自动发道具之前先查 `Data.Class.item.byId`（`ItemExists`），
而不是靠 `fromItem` 返回 null 来判断补丁有没有生效。

#### 6. `FsPak` 在哪 —— 以及为什么之前三轮都没加载成功

原版挂 pak 的写法是：

```csharp
ModCore.Modules.FsPak.Instance.FileSystem.loadPak(path.AsHaxeString());
```

关键点：**`FsPak` 在 `ModCore.Modules` 命名空间里，不在 `dc.hxd.fs`**。

前几轮失败的过程（都留在日志里，值得记一笔）：

1. 先写成 `using dc.hxd.fs;` + `FsPak.Instance...` → `CS0234: 命名空间"dc.hxd.fs"中不存在类型"FsPak"`。
2. 以为是"投影里没有"，改成反射，只找 `GetProperty("Instance")` →
   运行期打印 `FsPak.Instance 为空，无法加载 res.pak`（日志实证）。
   原因：`ModCore.Module<TModule>` 家族的单例成员不是那个形状的静态属性。
3. 又改成"属性 + `ME` + 方法"三条路都试 → 仍然是 `取不到 FsPak 单例（属性/方法都试过）`。
4. 最后直接写全限定名试编译：**`ModCore.Modules.FsPak.Instance` 编译通过** ——
   其他模组 `using dc;` 能写，是因为 `dc` 下没有这个名字、编译器回退找到了 ModCore 那个类。

现在就是一行真实类型调用，零反射：

```csharp
ModCore.Modules.FsPak fsPak = ModCore.Modules.FsPak.Instance;
dc.hxd.fmt.pak.FileSystem fs = fsPak.FileSystem;
fs.loadPak(GojoUtil.Hs(pakPath));
```

加载后会顺手查一次 `Data.Class.item.byId`，把"补丁到底生效没有"直接写进日志。

### 7. 原生选项页的两个坑（实机踩出来的）

**滑条的 `button` 参数是精灵名前缀，不是说明文字。**
反编译 `OptionsBase.addSliderWidget`：

```csharp
if (button == null) button = "sliderButton";
obj  = "" + button + "Off";     // → 去 ui 图集取 "sliderButtonOff"
obj2 = "" + button + "On";
new HSprite(Assets.Class.ui, obj, ...);
```

把中文说明当 `button` 传进去，它就去图集里找
`"敌人进入这个范围会被迟滞、子弹会被湮灭Off"` 这张精灵 → 找不到 → hashlink 抛异常 →
**每一个滑条都没建出来**（日志：`创建滑条控件失败: xxx` + `Uncaught hashlink exception`）。
所以 `button` 必须传 `null`（原版滑条也没有副标题位置，说明只能折进标题）。

**重绑定不能把"激活控件的那一次按键"当成新绑定。**
原版选项页里"激活控件"和"确认"都是回车，而我们的捕获是全局轮询 `GetAsyncKeyState`，
所以一点「重新绑定」就立刻把那次回车/随后的方向键吃了进去
（日志实证：`重绑定 苍 = ENTER` / `= DOWN` / `= UP`，配置里真的被写成了 `"Blue.Key": "DOWN"`）。

修法：进入捕获后先无视 `0.35s`，并要求**所有被跟踪的键都松开过一次**才开始收键；
同时把"激活控件的那一次按键"记下来单独排除。

### 8. 按键方案的两次反复（结论：模组自己硬绑）

DCCM 没有"注册模组热键"的公开 API。仓库里所有模组都走
`user32!GetAsyncKeyState` + 自己存键名 —— **本模组最终也回到这条路**。

中间试过 v5/v6 的"直接读原版动作绑定"，**读得到但用不了**，记录如下以免以后重复踩：

```csharp
dc.tool.Controller c = dc.Boot.Class.ME.controller;   // 或 hero.controller.parent
dc.tool.BindingProfiles bp = c.get_bindings();        // 自动按 useCustomBindings 选 normal/custom
int vk = bp.primary.getDyn(action);                   // 键盘键码，-1 = 没绑
```

反编译源码里查证过、**确实成立**的事实（`GamePseudocode/`）：

* 动作就是 `int` `0..37`，**没有枚举**；对应表只在 `dc/Options.cs:2285` 的 switch 里。
* 绑定存在 `dc.tool.BindingProfiles` 的六个 `ArrayBytes_Int`：
  键盘 `primary` / `secondary` / `third`，手柄 `padA` / `padB` / `padC`。
* 键码是 heaps `hxd.Key` 码，**实测就是 Win32 VK 码**（A-Z=65-90、F1-F12=112-123、
  方向键 37-40、空格 32…）。
* `padIsPressed(int)` 收的是**手柄按钮码**而不是动作码 —— 最容易踩的坑。

实机结果：**值读出来了**（诊断日志 `CAM_LEFT=74`、`HEAL=82`、`MAP_FOCUS=72`），
但 `Input.Pressed(该键码)` **恒为 false**，诊断行一直是"松开"，招式打不出来。
排查成本已经超过收益，所以 v7 退回硬绑。

v7 的做法（现在生效）：

```csharp
int vk = GojoKeys.Bind(cfg.Key, 0);   // 解析键名 + 把键加进跟踪集
bool pressed = Input.Pressed(vk);      // GetAsyncKeyState 边沿触发
```

好处：不依赖任何游戏内部状态，不存在"还没初始化好所以读不到"；
代价：键位由模组自己存，改键在模组菜单里改。

### 9. 菜单为什么能"暂停游戏还收得到输入"

`Game.modalPause(ref bool)` 会把 Game 进程整个 `paused = true`，于是
`IOnHeroUpdate` 不再回调。菜单自己挂在 `IOnFrameUpdate`（模组框架直接回调，
不受 `Process.paused` 影响）上，所以暂停后输入照常。

`modalPause` 带 `ref` 参数、签名容易和编译期投影对不上，所以和 `FsPak` 一样走**反射**：
找不到方法就退化成"不暂停"，菜单照常可用（只是不能慢慢调了）。`resume()` 用来解暂停。

### 10. HUD / 菜单的渲染方式（逐行绝对定位）

基础还是原版字体 + 原版 UI 层：

```csharp
Font font = dc.Assets.Class.font12;                         // 原版字体
var root = new dc.h2d.Object((dc.h2d.Object)null);          // 整块 UI 的唯一锚点
root.x = x; root.y = y; root.scaleX = s; root.scaleY = s;   // 位置/缩放只设在它身上
level.root.addChildAt(root, dc.Const.Class.ROOT_DP_CTX_UI); // 挂到 UI 层
var t = new dc.h2d.Text(font, root);                        // 每行挂在锚点下，用局部坐标
```

关键点（`GamePseudocode/dc.h2d/Object.cs` 查证）：

* `Object.__inst_construct__` 在 `parent != null` 时会 `parent.addChild(this)`，
  所以 `new Text(font, root)` 自动挂进容器；传 `null` 则不挂（锚点自己就是 `null`）。
* `Object.drawRec` 开头 `if (!visible) return;` → **容器不可见 = 整棵子树跳过**，
  隐藏/显示只要写一个字段。
* `calcAbsPos` 会把父容器的 `scale` 乘进来，所以行内文字**不要**再自己设 `scaleX/Y`
  —— 否则 1px 阴影偏移会被放大，放大后行与行咬在一起（就是老的"叠层"）。
* `removeChild(容器)` = 整棵树一起摘掉，不可能漏摘。

另外：**只在文本内容真的变了**的时候才 `set_text`（每帧重设会触发排版重算），
文字对象只在关卡变化时建一次，之后只清空 / 切 `visible`。

---

## 八、已知限制

* 招式的**视觉**全部复用原版 FX（时间扭曲球 / 贯穿光带 / 小型爆发），
  没有做"苍·赤·茈"各自的独立粒子——那需要新贴图与新 Spine 动画。
* 无下限是**单一全局状态**（只有英雄一个），不区分"哪个单位在放"。
* 迟滞只压**移动速度**，不压攻击/施法速度（原因见「实现要点 → 2」）。
* 领域展开期间给的是原版 affect `48` 无敌（0.6 × 持续时间），不是全程无敌。
* 屏障吸收只在 `applyAttackResult` 这一层——如果某个伤害绕过了原版攻击管线
  （极少数脚本伤害），它不会被挡住。
* HUD / 菜单用的是**等宽假设**的字符排版（`font12`），中文标签对齐靠空格填充；
  不同分辨率下可能略有偏移，可用菜单里的 `HUD X/Y/行高` 调。
* 菜单是**文本覆盖层**，不是原版控件，所以没有鼠标操作、没有滚动条。
* Boss 只会被轻微推动（`BossPullPushFactor`），这是刻意的。

---

## 九、验证记录

* `dotnet build` 全绿（net10.0），产物 `GojoLimitless.dll` + `modinfo.json`
  （**没有** `res.pak` —— v4 起道具与数据补丁已删除）。
* 静态核对过原版按键链路（`GamePseudocode/`）：`dc/Options.cs:2285`（动作表）、
  `dc.tool/Controller.cs:70`（`get_bindings`）、`dc.tool/BindingProfiles.cs`（六个数组）、
  `dc.hxd/_Key.cs`（`isDown`/`isPressed` 键码空间）、`dc.h2d/Object.cs:1780`（`drawRec` 可见性剪枝）、
  `dc.h2d/_Object.cs:68`（构造时 `parent.addChild`）。
* 静态校验过产物 DLL 的元数据：`IOnFrameUpdate` / `IOnHeroUpdate` /
  `Hook_Mob.getMoveSpeedMul` / 全部招式类 / 菜单的中文标签都在。
* 逐条核对过用到的原版 API（`createLight`/`dispose`、`Bullet.vanish`、`bdx/bdy`、
  `AttackUtils.createFromHero/hit`、`Fx.timeDistorsionStart/longHitLine/smallIceExplosion`、
  `Hero._level.entities/fx`、`Hook_Entity.applyAttackResult`、`Hook_Hero.init`、
  `Hook_Mob.getMoveSpeedMul`、`dc.Assets.font12`、`Const.ROOT_DP_CTX_UI`、`dc.h2d.Text`），
  签名与语义均已确认。
* **未在游戏内实机运行验证** —— 本仓库环境无法启动游戏。请以首启日志
  （`coremod/logs/`）里有没有 `[GojoLimitless]` 那几行为准。

### 实机自检清单

启动游戏后按顺序核对（对应需求里的「实机验证」项）：

| # | 检查项 | 期望 |
|---|---|---|
| 1 | 启动日志 | 「招式已注册 …」「已加载 —— …」「HUD 已创建（逐行绝对定位 …）」 |
| 2 | HUD | 左上角 8 行文字，咒力条满格，状态行 `ALL SYSTEMS NOMINAL` |
| 3 | `F`（动作 `activate`） | 标题变 `[FORCED]` |
| 4 | `J` `L` `M` `R` | 各自出招，HUD 对应行进冷却并倒数 |
| 5 | 连招 | `J` 后 3 秒内按 `L`，HUD 出现 `COMBO: L -> 虚式·茈`，并打出茈 |
| 6 | **改键** | 菜单「界面与系统 → 触发键」按 Enter 再按 `H` → HUD 左侧「苍」那行键名变成 H，按 H 能放「苍」 |
| 7 | 按键列表 | HUD 左侧键名与配置一致（默认 J / K / L / U） |
| 8 | 「选项 → 模组 → GojoLimitless」 | 页面出现；按键区 6 行点一下进入捕获，按一个非修饰键即可改绑（Esc 取消） |
| 9 | 滑条 | 能上下调值；日志**没有** `创建滑条控件失败` / `Uncaught hashlink exception` |
| 10 | **HUD 放大不叠层** | 把 `HUD 文字大小` 拉到 2.5~3.0：文字整体等比变大，**行距同步变大、行与行不重叠**，阴影仍是 1px 细边而不是跟着变粗；把滑块来回拖，屏幕上**不会**出现残影 |
| 11 | 弹幕湮灭 | 让远程怪射你，子弹进光圈就消失，日志「湮灭弹幕累计 25 发」 |
| 12 | 切关 / 死亡 / 重开 | HUD 重建、储量复位、配置重载、无残留文字 |
| 13 | Boss | 对 Boss 用「苍/赤」只轻微推动（对比小怪被明显吸/掀） |
| 14 | `H`（动作 `map_focus`） | 覆盖层菜单打开时**游戏真的暂停**（日志 `菜单已打开（游戏已暂停）`），且 HUD 自动收起 |
| 15 | 日志尾部 | 无 `[GojoLimitless] … 异常` 刷屏 |
