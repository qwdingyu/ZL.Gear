using System.Collections.Generic;

namespace ZL.Gear.Core.Services
{
    public interface IGearProfileService
    {
        Dictionary<string, object> LoadDeviceRoles();
    }
}
