using System;
using Microsoft.Xrm.Sdk;

namespace Contoso.Crm.Empty
{
    /// <summary>The second stepless type, so the assembly is empty of steps rather than empty of types.</summary>
    public class Spare : IPlugin
    {
        public void Execute(IServiceProvider serviceProvider)
        {
        }
    }
}
