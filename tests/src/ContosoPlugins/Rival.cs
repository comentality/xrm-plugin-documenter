using System;
using Microsoft.Xrm.Sdk;
using XrmTools.Meta.Attributes;

namespace Contoso.Crm
{
    /// <summary>
    /// The other half of the cross assembly collision fixture; TestPlugins.Rival is in
    /// TestPlugins\Plugins\Rival.cs. Both are registered, from different assemblies, and
    /// the namespaces settle it: a run writes Contoso.Crm.Rival's registration here and
    /// TestPlugins.Rival's never lands in this file.
    /// </summary>
    public class Rival : IPlugin
    {
        public void Execute(IServiceProvider serviceProvider)
        {
        }
    }
}
