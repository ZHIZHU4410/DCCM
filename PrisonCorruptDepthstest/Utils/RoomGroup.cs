using dc._Data;
using PrisonCorruptDepthstest.EntryPoint;

namespace PrisonCorruptDepthstest.Utils;

public class RoomGroup
{
    public readonly Serilog.ILogger GetLogger;

    public RoomGroup(ModInitializer entry)
    {
        GetLogger = entry.Logger;
        GetLogger.Information("Room Group initialisation commences");
        // No custom room groups needed — uses vanilla room groups
    }
}
