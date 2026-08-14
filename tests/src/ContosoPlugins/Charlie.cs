using System;
using Microsoft.Xrm.Sdk;
using XrmTools.Meta.Attributes;

namespace Contoso.Crm
{
    /// <summary>
    /// The far side of the interleave: sorted by type name this class follows
    /// Contoso.Crm.Bravo, which belongs to a different assembly, so a list built straight
    /// from the query order would show the Contoso.Crm.Plugins heading twice.
    /// </summary>
    public class Charlie : IPlugin
    {
        public void Execute(IServiceProvider serviceProvider)
        {
        }
    }
}
