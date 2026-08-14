using System;
using Microsoft.Xrm.Sdk;
using XrmTools.Meta.Attributes;

namespace Contoso.Crm
{
    /// <summary>
    /// An ordinary class in the second assembly, and the first of the three that prove the
    /// class list stays grouped by assembly.
    ///
    /// The environment hands types back sorted by full type name, and Alpha, Bravo and
    /// Charlie alternate between Contoso.Crm.Plugins and Contoso.Crm.Orphan when sorted
    /// that way. The list and the preview regroup them by assembly regardless, so each
    /// assembly heading has to appear exactly once.
    /// </summary>
    public class Alpha : IPlugin
    {
        public void Execute(IServiceProvider serviceProvider)
        {
        }
    }
}
