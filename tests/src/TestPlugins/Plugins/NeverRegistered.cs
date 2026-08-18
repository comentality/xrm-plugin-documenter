using System;
using Microsoft.Xrm.Sdk;
using XrmTools.Meta.Attributes;

namespace TestPlugins
{
    /// <summary>
    /// Present in the assembly and registered as a plugin type, but with no steps against
    /// it. The tool lists types by their steps, so this one must never appear - and
    /// this file must come back from a run byte for byte identical.
    /// </summary>
    public class NeverRegistered : IPlugin
    {
        public void Execute(IServiceProvider serviceProvider)
        {
        }
    }
}
