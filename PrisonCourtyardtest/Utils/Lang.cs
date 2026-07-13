namespace PrisonCourtyardtest.Utils;

public class DLCLang
{
    public readonly Serilog.ILogger GetLogger;

    public DLCLang(EntryPoint.ModInitializer levelinit)
    {
        GetLogger = levelinit.Logger;
        GetLogger.Information("Language Module initialisation commences");
        ModCore.Modules.GetText.Instance.RegisterMod("PrisonCourtyardtestLang");
    }
}
