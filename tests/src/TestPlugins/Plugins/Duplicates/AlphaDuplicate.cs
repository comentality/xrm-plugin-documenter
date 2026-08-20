using System;
using Microsoft.Xrm.Sdk;
using XrmTools.Meta.Attributes;

namespace TestPlugins.Alpha
{
    /// <summary>
    /// Half of the short-name-collision fixture. This is the class that is actually
    /// registered; TestPlugins.Beta.Duplicate declares the same short name in a sibling
    /// file, so the short name alone matches both. The registered namespace settles it:
    /// a run writes this file and may not touch the sibling.
    /// </summary>
    public class Duplicate : IPlugin
    {
        public void Execute(IServiceProvider serviceProvider)
        {
        }
    }
}
