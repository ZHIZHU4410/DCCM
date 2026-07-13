using dc._Data;

namespace PrisonCourtyardtest.Utils;

public class RoomGroup
{
    public readonly Serilog.ILogger GetLogger;

    public RoomGroup(EntryPoint.ModInitializer entry)
    {
        GetLogger = entry.Logger;
        GetLogger.Information("Room Group initialisation commences");
        // No custom room groups needed — uses vanilla room groups
    }
}
