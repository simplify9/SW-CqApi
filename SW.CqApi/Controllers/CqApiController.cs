using System;
using Microsoft.AspNetCore.Mvc;

namespace SW.CqApi
{
    /// <summary>
    /// Kept only so <c>AddApplicationPart(typeof(CqApiController).Assembly)</c> in existing test
    /// hosts still compiles. CqApi 10 has no controller; call <c>endpoints.MapCqApi()</c>.
    /// </summary>
    [NonController]
    [Obsolete("CqApi 10 has no controller. Map handlers with endpoints.MapCqApi().")]
    public sealed class CqApiController
    {
        private CqApiController() { }
    }
}
