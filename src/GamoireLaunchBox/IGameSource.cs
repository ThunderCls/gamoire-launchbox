using System.Collections.Generic;

namespace GamoireLaunchBox
{
    public interface IGameSource
    {
        IReadOnlyList<GameFacts> All();

        GameFacts? ById(string id);
    }
}
