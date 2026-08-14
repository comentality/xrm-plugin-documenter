using System;
using Microsoft.Xrm.Sdk;
using XrmTools.Meta.Attributes;

namespace TestPlugins
{
    /// <summary>
    /// Ordering and wrapping. Six steps registered in deliberately scrambled order across
    /// four stages, both modes and several messages, so the documenter has to sort them by
    /// stage, then rank, then message before writing. One of them filters on enough columns
    /// to force the summary emitter to wrap the list onto continuation lines.
    /// </summary>
    public class WideRegistration : IPlugin
    {
        public void Execute(IServiceProvider serviceProvider)
        {
        }
    }
}
