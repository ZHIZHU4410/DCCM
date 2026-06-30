using dc.en;
using dc.pr;
using HaxeProxy.Runtime;

namespace PrisonCorruptDepthstest.EntryPoint;

public class EntityManager
{
    public static Serilog.ILogger GetLogger = null!;

    public EntityManager(ModInitializer entry)
    {
        GetLogger = entry.Logger;
        GetLogger.Information("Entity Manager initialisation commences");
        // No custom mob hooks needed — this mod uses vanilla mobs via CDB mob table.
        // Hook registration is here for future custom entity additions.
    }
}
