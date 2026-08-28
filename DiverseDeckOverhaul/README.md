# DiverseDeckOverhaul

死亡细胞《万智牌组 DiverseDeck》强化模组（仅改动 4 张共享技能位的卡牌，不触碰其他内容）。

## 功能

1. **切牌 CD = 0** —— 通过 **data.cdb 数据补丁**将四张牌的 `castCD` 改为 0（Juggernaut/Catalyst/Electro/Watcher），切牌后新牌无冷却。补丁以 `res.pak` 随模组安装，游戏启动时合并。
2. **电球五颜六色** —— DiverseDeckElectro 的电球按彩虹色盘着色（红橙黄绿青蓝紫品红白粉，循环分配）。
3. **初始 5 颗电球** —— 切到 Electro 牌时直接召唤 5 颗（原版只有 1 颗），另加历史击杀奖励。
4. **每击杀 10 个敌人 +1 颗** —— 击杀数按英雄累计（跨切牌、跨关卡保留）；击杀时若 Electro 在场立即加球，否则切回 Electro 时按历史击杀数补发。
5. **五圈电球**（从内到外）：
   - 第一圈 **10** 颗（半径与原版一致，distance=2.5 格）
   - 第二圈 **18** 颗（半径 ×1.6）
   - 第三圈 **34** 颗（半径 ×2.2）
   - 第四圈 **58** 颗（半径 ×2.8）
   - 第五圈 **无上限**（半径 ×3.4，容纳 120 颗之后的全部电球）
   - 每圈电球在圈内均匀分布；已满的圈固定槽位，加球只影响更外圈、不挤动内圈；
     外圈位置**每帧按轨道角度直接重算**（不依赖精灵坐标/对象引用），不会闪一下后与内圈重合。
6. **伤害翻倍** —— 电球触伤（power=8）、闪电连发（power2=55）、感电 DOT（dps=4）全部 ×2（运行时内存中修改该物品 props，不改 data.cdb 里的伤害值）。

所有电球改为沿**完整圆周均匀分布**（原版只分布在约 114° 的扇形内），视觉上呈现真正的两圈环绕。

## 数据补丁（data.cdb / res.pak）

- `patch_deck_cdb.py`：基于 MDK v35 模板生成 `data.cdb`（仅四张牌 `castCD=0`）。
- 构建时（仿照 DamageAuraBoost.csproj）：
  `DCCMTool cdb diff`（对比游戏模板）→ diff.pak → `pak unpack` → `Assets/data.cdb_/item/DiverseDeck*.json` → MDK `PackAssetsIntoPak` 打成 `res.pak` 自动安装。
- **启动时加载**：模组在 `IOnAfterLoadingAssets` 中把 `res.pak` 挂载进 `FsPak`（`FsPak.Instance.FileSystem.loadPak`），
  游戏的 `CDBManager` 在首次关卡生成时合并 `data.cdb_` 补丁。若未加载 res.pak，切牌 CD 不会生效。

修改其他卡牌数据时：编辑 `patch_deck_cdb.py` 的 `DECK_IDS` / 修改项 → 重跑脚本 → 重新 `dotnet build`。

## 可调参数（DiverseDeckOverhaulMain.cs 顶部常量）

| 常量 | 默认 | 说明 |
|---|---|---|
| `START_BALLS` | 5 | 初始电球数 |
| `KILLS_PER_BALL` | 10 | 每击杀 N 个敌人 +1 球 |
| `RING_CAPS` | {10,18,34,58} | 前四圈容量（第五圈无上限） |
| `RING_RADII` | {1.0,1.6,2.2,2.8,3.4} | 五圈半径倍数（相对原版 2.5 格） |
| `DAMAGE_MULT` | 2.0 | 电球/闪电/DOT 伤害倍率 |

## 说明

- 保留原版机制：电球触敌自动放电；按 Electro 牌触发闪电连发并消耗电球（原版行为未改动）。
- 击杀奖励不受原版 limit（3 颗）/ 传奇 legMaxOrbs（5 颗）上限限制，可叠满第一~四圈（10+18+34+58=120 颗）后继续进入第五圈（无上限）。
- 若不想让击杀数在切走 Electro 时累积，删掉 `OnElectroInit` 中的 `bonus` 计算即可。

## 构建 / 安装

```bat
dotnet build        :: Debug 下自动安装到 coremod/mods/DiverseDeckOverhaul（含 res.pak）
```

手动安装：把 `bin\Debug\net10.0\output\DiverseDeckOverhaul\` 下的文件（dll + modinfo.json + res.pak）复制到
`<游戏根目录>\coremod\mods\DiverseDeckOverhaul\`，启动游戏使用
`<游戏根目录>\coremod\core\host\startup\DeadCellsModding.exe`。
