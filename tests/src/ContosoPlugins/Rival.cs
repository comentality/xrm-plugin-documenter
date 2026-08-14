using System;
using Microsoft.Xrm.Sdk;
using XrmTools.Meta.Attributes;

namespace Contoso.Crm
{
    /// <summary>
    /// The other half of the cross assembly ambiguity fixture; TestPlugins.Rival is in
    /// TestPlugins\Plugins\Rival.cs. Both are registered, from different assemblies, and
    /// the documenter has to skip both: a run may not modify this file.
    /// </summary>
    public class Rival : IPlugin
    {
        public void Execute(IServiceProvider serviceProvider)
        {
        }
    }
}
