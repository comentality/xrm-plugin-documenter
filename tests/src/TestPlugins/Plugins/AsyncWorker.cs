using System;
using Microsoft.Xrm.Sdk;
using XrmTools.Meta.Attributes;

namespace TestPlugins
{
    /// <summary>
    /// Every named argument at once - Name, ExecutionOrder, Description, AsyncAutoDelete -
    /// on a step long enough that the emitter has to break it onto one argument per line.
    /// </summary>
    public class AsyncWorker : IPlugin
    {
        public void Execute(IServiceProvider serviceProvider)
        {
        }
    }
}
