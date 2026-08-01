using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;

namespace PluginDocumenter.Logic
{
    /// <summary>
    /// Reads plugin assemblies, types, steps and images from the connected environment.
    /// </summary>
    public static class RegistrationQuery
    {
        public static List<AssemblyInfo> GetAssemblies(IOrganizationService service)
        {
            var query = new QueryExpression("pluginassembly")
            {
                ColumnSet = new ColumnSet("pluginassemblyid", "name", "isolationmode"),
                Criteria =
                {
                    // Exclude the Microsoft-shipped assemblies, which are never in the user's source.
                    Conditions =
                    {
                        new ConditionExpression("ishidden", ConditionOperator.Equal, false),
                        new ConditionExpression("customizationlevel", ConditionOperator.Equal, 1)
                    }
                },
                Orders = { new OrderExpression("name", OrderType.Ascending) }
            };

            return service.RetrieveMultiple(query).Entities
                .Select(e => new AssemblyInfo
                {
                    Id = e.Id,
                    Name = e.GetAttributeValue<string>("name"),
                    IsolationMode = GetOptionSet(e, "isolationmode", 2)
                })
                .ToList();
        }

        /// <summary>
        /// Returns every plugin type in the assembly that has at least one registered step,
        /// with its steps and each step's images attached.
        /// </summary>
        public static List<PluginTypeInfo> GetPluginTypes(IOrganizationService service, Guid assemblyId)
        {
            var types = GetTypes(service, assemblyId);
            if (types.Count == 0)
            {
                return types;
            }

            var byId = types.ToDictionary(t => t.Id);
            var steps = GetSteps(service, byId.Keys.ToList());

            var stepsById = new Dictionary<Guid, PluginStepInfo>();
            foreach (var pair in steps)
            {
                PluginTypeInfo type;
                if (!byId.TryGetValue(pair.Value, out type))
                {
                    continue;
                }

                type.Steps.Add(pair.Key);
                stepsById[pair.Key.Id] = pair.Key;
            }

            if (stepsById.Count > 0)
            {
                foreach (var image in GetImages(service, stepsById.Keys.ToList()))
                {
                    PluginStepInfo step;
                    if (stepsById.TryGetValue(image.Value, out step))
                    {
                        step.Images.Add(image.Key);
                    }
                }
            }

            // Steps in execution order, so the emitted attributes read the way they run.
            foreach (var type in types)
            {
                type.Steps = type.Steps
                    .OrderBy(s => s.Stage)
                    .ThenBy(s => s.Rank)
                    .ThenBy(s => s.MessageName)
                    .ToList();
            }

            return types.Where(t => t.Steps.Count > 0).OrderBy(t => t.TypeName).ToList();
        }

        private static List<PluginTypeInfo> GetTypes(IOrganizationService service, Guid assemblyId)
        {
            var query = new QueryExpression("plugintype")
            {
                ColumnSet = new ColumnSet("plugintypeid", "typename", "friendlyname", "description"),
                Criteria =
                {
                    Conditions =
                    {
                        new ConditionExpression("pluginassemblyid", ConditionOperator.Equal, assemblyId)
                    }
                }
            };

            return service.RetrieveMultiple(query).Entities
                .Select(e => new PluginTypeInfo
                {
                    Id = e.Id,
                    TypeName = e.GetAttributeValue<string>("typename"),
                    FriendlyName = e.GetAttributeValue<string>("friendlyname"),
                    Description = e.GetAttributeValue<string>("description")
                })
                .ToList();
        }

        /// <summary>Returns each step paired with the id of the plugin type it belongs to.</summary>
        private static List<KeyValuePair<PluginStepInfo, Guid>> GetSteps(IOrganizationService service, List<Guid> typeIds)
        {
            var query = new QueryExpression("sdkmessageprocessingstep")
            {
                ColumnSet = new ColumnSet(
                    "sdkmessageprocessingstepid", "name", "stage", "mode", "rank",
                    "filteringattributes", "configuration", "description",
                    "asyncautodelete", "statecode", "plugintypeid", "sdkmessageid", "sdkmessagefilterid"),
                Criteria =
                {
                    Conditions =
                    {
                        new ConditionExpression("plugintypeid", ConditionOperator.In, typeIds.Cast<object>().ToArray())
                    }
                }
            };

            // Message name lives on sdkmessage; the filtered entity on sdkmessagefilter.
            var message = new LinkEntity("sdkmessageprocessingstep", "sdkmessage", "sdkmessageid", "sdkmessageid", JoinOperator.Inner)
            {
                EntityAlias = "msg",
                Columns = new ColumnSet("name")
            };
            var filter = new LinkEntity("sdkmessageprocessingstep", "sdkmessagefilter", "sdkmessagefilterid", "sdkmessagefilterid", JoinOperator.LeftOuter)
            {
                EntityAlias = "flt",
                Columns = new ColumnSet("primaryobjecttypecode")
            };
            // "Run in User's Context". Null means the calling user, which is the default.
            var impersonated = new LinkEntity("sdkmessageprocessingstep", "systemuser", "impersonatinguserid", "systemuserid", JoinOperator.LeftOuter)
            {
                EntityAlias = "usr",
                Columns = new ColumnSet("fullname")
            };
            query.LinkEntities.Add(message);
            query.LinkEntities.Add(filter);
            query.LinkEntities.Add(impersonated);

            return service.RetrieveMultiple(query).Entities
                .Select(e => new KeyValuePair<PluginStepInfo, Guid>(
                    new PluginStepInfo
                    {
                        Id = e.Id,
                        Name = e.GetAttributeValue<string>("name"),
                        Stage = GetOptionSet(e, "stage", 40),
                        Mode = GetOptionSet(e, "mode", 0),
                        Rank = e.GetAttributeValue<int?>("rank") ?? 1,
                        FilteringAttributes = e.GetAttributeValue<string>("filteringattributes"),
                        Configuration = e.GetAttributeValue<string>("configuration"),
                        Description = e.GetAttributeValue<string>("description"),
                        AsyncAutoDelete = e.GetAttributeValue<bool?>("asyncautodelete") ?? false,
                        IsDisabled = GetOptionSet(e, "statecode", 0) == 1,
                        MessageName = GetAliased<string>(e, "msg.name"),
                        PrimaryEntityName = GetAliased<string>(e, "flt.primaryobjecttypecode"),
                        ImpersonatingUser = GetAliased<string>(e, "usr.fullname")
                    },
                    e.GetAttributeValue<EntityReference>("plugintypeid").Id))
                .ToList();
        }

        /// <summary>Returns each image paired with the id of the step it belongs to.</summary>
        private static List<KeyValuePair<PluginImageInfo, Guid>> GetImages(IOrganizationService service, List<Guid> stepIds)
        {
            var query = new QueryExpression("sdkmessageprocessingstepimage")
            {
                ColumnSet = new ColumnSet(
                    "sdkmessageprocessingstepimageid", "imagetype", "entityalias",
                    "attributes", "name", "messagepropertyname", "sdkmessageprocessingstepid"),
                Criteria =
                {
                    Conditions =
                    {
                        new ConditionExpression("sdkmessageprocessingstepid", ConditionOperator.In, stepIds.Cast<object>().ToArray())
                    }
                },
                Orders = { new OrderExpression("imagetype", OrderType.Ascending) }
            };

            return service.RetrieveMultiple(query).Entities
                .Select(e => new KeyValuePair<PluginImageInfo, Guid>(
                    new PluginImageInfo
                    {
                        ImageType = GetOptionSet(e, "imagetype", 0),
                        EntityAlias = e.GetAttributeValue<string>("entityalias"),
                        Attributes = e.GetAttributeValue<string>("attributes"),
                        Name = e.GetAttributeValue<string>("name"),
                        MessagePropertyName = e.GetAttributeValue<string>("messagepropertyname")
                    },
                    e.GetAttributeValue<EntityReference>("sdkmessageprocessingstepid").Id))
                .ToList();
        }

        private static int GetOptionSet(Entity e, string name, int fallback)
        {
            var value = e.GetAttributeValue<OptionSetValue>(name);
            return value == null ? fallback : value.Value;
        }

        private static T GetAliased<T>(Entity e, string name)
        {
            var value = e.GetAttributeValue<AliasedValue>(name);
            return value == null ? default(T) : (T)value.Value;
        }
    }
}
