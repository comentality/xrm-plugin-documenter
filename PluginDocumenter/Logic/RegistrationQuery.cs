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
        /// <summary>
        /// Every assembly the environment will admit to, Microsoft's included. Telling those apart
        /// is <see cref="AssemblyInfo.IsMicrosoft"/>'s job and the caller's decision; there is no
        /// server side filter that does it, which is why this no longer pretends to have one.
        /// </summary>
        public static List<AssemblyInfo> GetAssemblies(IOrganizationService service)
        {
            var query = new QueryExpression("pluginassembly")
            {
                ColumnSet = new ColumnSet("pluginassemblyid", "name", "isolationmode", "publickeytoken"),
                Criteria =
                {
                    // Internal plumbing the platform registers for itself. Steps on these are not
                    // shown by the registration tool either, so there is nothing here to document.
                    Conditions =
                    {
                        new ConditionExpression("ishidden", ConditionOperator.Equal, false)
                    }
                },
                Orders = { new OrderExpression("name", OrderType.Ascending) }
            };

            return service.RetrieveMultiple(query).Entities
                .Select(e => new AssemblyInfo
                {
                    Id = e.Id,
                    Name = e.GetAttributeValue<string>("name"),
                    PublicKeyToken = e.GetAttributeValue<string>("publickeytoken"),
                    IsolationMode = GetOptionSet(e, "isolationmode", 2)
                })
                .ToList();
        }

        /// <summary>
        /// Returns every plugin type in the given assemblies that has at least one registered step,
        /// with its steps and each step's images attached.
        ///
        /// Several assemblies at once, because a project that ships one assembly per plugin has as
        /// many assemblies as another has classes, and documenting it one assembly at a time is not
        /// a workflow. The work per assembly is the same either way: the ids are batched into the
        /// same three queries.
        /// </summary>
        public static List<PluginTypeInfo> GetPluginTypes(IOrganizationService service, IList<Guid> assemblyIds)
        {
            var types = GetTypes(service, assemblyIds);
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

        private static List<PluginTypeInfo> GetTypes(IOrganizationService service, IList<Guid> assemblyIds)
        {
            return Retrieve(service, assemblyIds, ids => new QueryExpression("plugintype")
            {
                ColumnSet = new ColumnSet("plugintypeid", "typename", "friendlyname", "description", "pluginassemblyid"),
                Criteria =
                {
                    Conditions =
                    {
                        new ConditionExpression("pluginassemblyid", ConditionOperator.In, ids)
                    }
                }
            })
                .Select(e => new PluginTypeInfo
                {
                    Id = e.Id,
                    AssemblyId = GetLookupId(e, "pluginassemblyid"),
                    TypeName = e.GetAttributeValue<string>("typename"),
                    FriendlyName = e.GetAttributeValue<string>("friendlyname"),
                    Description = e.GetAttributeValue<string>("description")
                })
                .ToList();
        }

        /// <summary>Returns each step paired with the id of the plugin type it belongs to.</summary>
        private static List<KeyValuePair<PluginStepInfo, Guid>> GetSteps(IOrganizationService service, List<Guid> typeIds)
        {
            return Retrieve(service, typeIds, ids => BuildStepQuery(ids))
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
                    GetLookupId(e, "plugintypeid")))
                .ToList();
        }

        private static QueryExpression BuildStepQuery(object[] typeIds)
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
                        new ConditionExpression("plugintypeid", ConditionOperator.In, typeIds)
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
            return query;
        }

        /// <summary>Returns each image paired with the id of the step it belongs to.</summary>
        private static List<KeyValuePair<PluginImageInfo, Guid>> GetImages(IOrganizationService service, List<Guid> stepIds)
        {
            return Retrieve(service, stepIds, ids => new QueryExpression("sdkmessageprocessingstepimage")
            {
                ColumnSet = new ColumnSet(
                    "sdkmessageprocessingstepimageid", "imagetype", "entityalias",
                    "attributes", "name", "messagepropertyname", "sdkmessageprocessingstepid"),
                Criteria =
                {
                    Conditions =
                    {
                        new ConditionExpression("sdkmessageprocessingstepid", ConditionOperator.In, ids)
                    }
                },
                Orders = { new OrderExpression("imagetype", OrderType.Ascending) }
            })
                .Select(e => new KeyValuePair<PluginImageInfo, Guid>(
                    new PluginImageInfo
                    {
                        ImageType = GetOptionSet(e, "imagetype", 0),
                        EntityAlias = e.GetAttributeValue<string>("entityalias"),
                        Attributes = e.GetAttributeValue<string>("attributes"),
                        Name = e.GetAttributeValue<string>("name"),
                        MessagePropertyName = e.GetAttributeValue<string>("messagepropertyname")
                    },
                    GetLookupId(e, "sdkmessageprocessingstepid")))
                .ToList();
        }

        /// <summary>
        /// Ids per request. Documenting a whole solution can put thousands of ids in front of these
        /// queries, and neither an In list nor a single page will take them all, so both are split.
        /// </summary>
        private const int IdsPerQuery = 250;

        private static List<Entity> Retrieve(IOrganizationService service, IList<Guid> ids, Func<object[], QueryExpression> build)
        {
            var results = new List<Entity>();
            for (var offset = 0; offset < ids.Count; offset += IdsPerQuery)
            {
                var chunk = ids.Skip(offset).Take(IdsPerQuery).Cast<object>().ToArray();
                var query = build(chunk);
                query.PageInfo = new PagingInfo { Count = 5000, PageNumber = 1 };

                while (true)
                {
                    var page = service.RetrieveMultiple(query);
                    results.AddRange(page.Entities);
                    if (!page.MoreRecords)
                    {
                        break;
                    }

                    query.PageInfo.PageNumber++;
                    query.PageInfo.PagingCookie = page.PagingCookie;
                }
            }

            return results;
        }

        private static Guid GetLookupId(Entity e, string name)
        {
            var value = e.GetAttributeValue<EntityReference>(name);
            return value == null ? Guid.Empty : value.Id;
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
