using System;
using Microsoft.Xrm.Sdk;

namespace Contoso.Crm
{
    /// <summary>
    /// The second missing file, so the report has to say "No matching .cs file (2)" rather
    /// than name a single unlucky class.
    /// </summary>
    public class Ghost : IPlugin
    {
        public void Execute(IServiceProvider serviceProvider)
        {
        }
    }
}
