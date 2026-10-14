using dc.ui;
using HaxeProxy.Runtime;
using ModCore.Menu;
using ModCore.Storage;
using ModCore.Utilities;

namespace MobDeathSplit;

public sealed partial class MobDeathSplitMain : IModMenu
{
    private static readonly Config<SplitSettings> Settings = new("MobDeathSplit");

    public string GetName() => "怪物死亡分裂";

    public string GetSubText() => "每次死亡生成两只；血量与分裂代数可调整";

    public void BuildMenu(Options options)
    {
        // 采用 ZoomVision 的 IModMenu 和原版 OptionsBase 控件组织方式。
        var menu = (OptionsBase)options;
        ((Text)menu.title).set_text("怪物死亡分裂设置".AsHaxeString());
        menu.createScroller(0.0);

        AddSlider(menu, "分裂怪血量（最初母体最大血量 %）",
            SplitPolicy.NormalizeHealthPercent(Settings.Value.ChildHealthPercent),
            1, SplitPolicy.MinHealthPercent, SplitPolicy.MaxHealthPercent, value =>
            {
                Settings.Value.ChildHealthPercent = SplitPolicy.NormalizeHealthPercent(value);
                Settings.Save();
            });

        AddSlider(menu, "普通怪分裂代数上限（0 = 无限）",
            SplitPolicy.NormalizeGenerations(Settings.Value.MaxGenerations),
            1, 0, SplitPolicy.MaxMenuGenerations, value =>
            {
                Settings.Value.MaxGenerations = SplitPolicy.NormalizeGenerations((int)System.Math.Round(value));
                Settings.Save();
            });

        AddSlider(menu, "自爆蝙蝠分裂代数上限（0 = 无限）",
            SplitPolicy.NormalizeGenerations(Settings.Value.KamikazeMaxGenerations),
            1, 0, SplitPolicy.MaxMenuGenerations, value =>
            {
                Settings.Value.KamikazeMaxGenerations = SplitPolicy.NormalizeGenerations((int)System.Math.Round(value));
                Settings.Save();
            });

        menu.updateScroller();
    }

    private static void AddSlider(OptionsBase menu, string label, double current,
        double step, double min, double max, Action<double> update)
    {
        menu.addSliderWidget(label.AsHaxeString(), (HlAction<double>)(value => update(value)),
            current, Ref<double>.In(step), menu.scrollerFlow,
            Ref<bool>.In(false), Ref<bool>.In(true), Ref<double>.In(min),
            Ref<double>.In(max), null, Ref<int>.In(0));
    }
}
