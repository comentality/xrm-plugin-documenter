using System;
using Microsoft.Xrm.Sdk;
using XrmTools.Meta.Attributes;

namespace TestPlugins.Alpha
{
    /// <summary>
    /// Half of the ambiguity fixture. This is the class that is actually registered, but
    /// TestPlugins.Beta.Duplicate declares the same short name in a sibling file, and the
    /// documenter matches files by short name only. It has to report the pair as ambiguous
    /// and skip both rather than pick one - so neither file may be modified by a run.
    /// </summary>
    public class Duplicate : IPlugin
    {
        public void Execute(IServiceProvider serviceProvider)
        {
        }
    }
}
