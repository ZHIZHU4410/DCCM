using dc.en;
using dc.en.mob;
using dc.pr;

namespace MobDeathSplit;

/// <summary>
/// 捕获死亡前的基础属性；新怪满血生成，不继承死亡状态、仇恨、燃烧或中毒等临时效果。
/// 不共享父怪的技能对象、掉落列表或其他可变对象，防止子怪间互相影响。
/// </summary>
internal sealed class MobSnapshot(Mob source, SplitLineage lineage)
{
    public readonly int NextGeneration = lineage.Generation < int.MaxValue ? lineage.Generation + 1 : int.MaxValue;
    public readonly int OriginalMaxLife = lineage.OriginalMaxLife;
    public readonly bool IsKamikaze = source is BatKamikazeTemplate;
    public readonly Level Level = source._level;
    public readonly dc.String Kind = source.type;
    public readonly int Cx = source.cx, Cy = source.cy;
    public readonly double Xr = source.xr, Yr = source.yr;
    public readonly int Brutality = source.brutalityTier, Tactic = source.tacticTier,
        Survival = source.survivalTier, LifeTier = source.trueLifeTier;
    public readonly bool Elite = source.elite;
    public readonly dc.String? EliteSkill = source.eliteSkillId;
    public readonly int MaxLife = source.maxLife, BonusLife = source.bonusLife;
    public readonly double ScaleX = source.sprScaleX, ScaleY = source.sprScaleY,
        Height = source.hei, Radius = source.radius, Weight = source.baseWeight,
        BumpResistance = source.bumpResistance, PowerDistance = source.powerDistMul,
        SoftCapMin = source.dmgSoftCapMin, SoftCapMax = source.dmgSoftCapMax,
        MoveSpeed = source.baseMoveSpeedMul, MovePause = source.baseMovePauseMul,
        WalkSpeed = source.moveWalkSpeed, WalkPause = source.moveWalkPause,
        GlobalCooldown = source.multGlobalCD, BreachThreshold = source.breachDamageThreshold,
        BreachStun = source.breachStunDuration, BreachReduction = source.breachDamageReduction;

    public void Apply(Mob child, int childHealth)
    {
        child.eliteSkillId = EliteSkill;
        if (Elite)
        {
            // 原版负责精英技能、抗性和外观；属性随后恢复为父怪值，避免再次加强。
            child.setElite(false);
        }

        child.setBrutalityTier(Brutality);
        child.setTacticTier(Tactic);
        child.setSurvivalTier(Survival);
        child.trueLifeTier = LifeTier;
        child.maxLife = childHealth;
        child.bonusLife = BonusLife;
        child.life = childHealth;
        child.oldLife = childHealth;
        // 工厂及精英初始化可能使用不同的初始血量，同步刷新血条上限。
        if (child.lifeBar != null)
            child.lifeBar.init(childHealth, null);
        child.sprScaleX = ScaleX;
        child.sprScaleY = ScaleY;
        child.hei = Height;
        child.radius = Radius;
        child.baseWeight = Weight;
        child.bumpResistance = BumpResistance;
        child.powerDistMul = PowerDistance;
        child.dmgSoftCapMin = SoftCapMin;
        child.dmgSoftCapMax = SoftCapMax;
        child.baseMoveSpeedMul = MoveSpeed;
        child.baseMovePauseMul = MovePause;
        child.moveWalkSpeed = WalkSpeed;
        child.moveWalkPause = WalkPause;
        child.multGlobalCD = GlobalCooldown;
        child.breachDamageThreshold = BreachThreshold;
        child.breachStunDuration = BreachStun;
        child.breachDamageReduction = BreachReduction;
        child.setPosCase(Cx, Cy, Xr, Yr);
    }
}
