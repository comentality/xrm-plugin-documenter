using System;
using Microsoft.Xrm.Sdk;
using XrmTools.Meta.Attributes;

namespace TestPlugins
{
    /// <summary>
    /// Half of the cross assembly ambiguity fixture, the other half being
    /// Contoso.Crm.Rival in ContosoPlugins\Rival.cs. Two assemblies, two files, two
    /// registrations, and namespaces that would settle it in a moment - but the documenter
    /// matches on the short name alone, so it has to report both as ambiguous and modify
    /// neither file.
    /// </summary>
    public class Rival : IPlugin
    {
        public void Execute(IServiceProvider serviceProvider)
        {
        }
    }
}
