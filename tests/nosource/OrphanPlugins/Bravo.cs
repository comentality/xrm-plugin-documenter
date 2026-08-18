using System;
using Microsoft.Xrm.Sdk;

namespace Contoso.Crm
{
    /// <summary>
    /// Registered, with steps, and no .cs anywhere the tool will look. It also sits
    /// between Contoso.Crm.Alpha and Contoso.Crm.Charlie when types are sorted by name,
    /// which is what makes the class list interleave two assemblies before it regroups
    /// them.
    /// </summary>
    public class Bravo : IPlugin
    {
        public void Execute(IServiceProvider serviceProvider)
        {
        }
    }
}
