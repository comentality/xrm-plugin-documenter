using System;
using Microsoft.Xrm.Sdk;
using XrmTools.Meta.Attributes;

namespace TestPlugins
{
    /// <summary>
    /// Half of the cross assembly collision fixture, the other half being
    /// Contoso.Crm.Rival in ContosoPlugins\Rival.cs. Two assemblies, two files, two
    /// registrations, one short name - and the namespaces settle it: each registration
    /// is written into its own file, never the other's.
    /// </summary>
    public class Rival : IPlugin
    {
        public void Execute(IServiceProvider serviceProvider)
        {
        }
    }
}
