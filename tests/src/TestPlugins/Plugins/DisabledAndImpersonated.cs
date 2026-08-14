using System;
using Microsoft.Xrm.Sdk;
using XrmTools.Meta.Attributes;

namespace TestPlugins
{
    /// <summary>
    /// The two facts only the summary comment can carry: a step left in the Disabled state,
    /// and a step running in another user's context. Neither may appear in attribute mode,
    /// where both steps have to look exactly like ordinary ones.
    /// </summary>
    public class DisabledAndImpersonated : IPlugin
    {
        public void Execute(IServiceProvider serviceProvider)
        {
        }
    }
}
