using System;
using Microsoft.Xrm.Sdk;
using XrmTools.Meta.Attributes;

namespace TestPlugins
{
    /// <summary>
    /// The generic plugin: one class registered against many tables.
    ///
    /// It stamps the same column on every table it is registered for, which is why it has
    /// a create and an update step on each of five - the shape a plugin repository has far
    /// more of than it has one-table plugins, and the one that decides how the steps are
    /// ordered. Ordered by stage, ten steps interleave five tables into a list nobody can
    /// read; ordered by table, every table's registrations sit together and the execution
    /// order is kept where it means something, inside one table.
    ///
    /// Registered scrambled, so a run has the sort to do. Its annotation steps also run
    /// out of message-name order - the update is PreOperation and the create
    /// PostOperation - which is what pins that a table's own steps keep running order
    /// rather than being alphabetised along with the tables.
    /// </summary>
    public class StatusDateStamper : IPlugin
    {
        public void Execute(IServiceProvider serviceProvider)
        {
        }
    }
}
