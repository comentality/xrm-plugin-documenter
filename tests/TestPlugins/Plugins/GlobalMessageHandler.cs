using System;
using Microsoft.Xrm.Sdk;
using XrmTools.Meta.Attributes;

namespace TestPlugins
{
    /// <summary>
    /// Registered against a message with no primary entity, which Dataverse stores as the
    /// filter "none". The entity has to vanish from both outputs: the constructor overload
    /// without it, and a summary line with no "of &lt;entity&gt;".
    /// </summary>
    public class GlobalMessageHandler : IPlugin
    {
        public void Execute(IServiceProvider serviceProvider)
        {
        }
    }
}
