using System;
using Microsoft.Xrm.Sdk;
using XrmTools.Meta.Attributes;

namespace TestPlugins.Beta
{
    /// <summary>
    /// The other half of the ambiguity fixture. Never registered; it exists only so that
    /// the short name Duplicate resolves to two files under the selected folder.
    /// </summary>
    public class Duplicate : IPlugin
    {
        public void Execute(IServiceProvider serviceProvider)
        {
        }
    }
}
