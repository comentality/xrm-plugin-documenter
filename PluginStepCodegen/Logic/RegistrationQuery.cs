using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Metadata.Query;
using Microsoft.Xrm.Sdk.Query;

namespace PluginStepCodegen.Logic
{
    /// <summary>
    /// Reads plugin assemblies, types, steps and images from the connected environment.
    /// </summary>
    public static class RegistrationQuery
    {
        /// <summary>
        /// Every assembly the environment will admit to, Microsoft's and every other shipped one
        /// included. Which of them to show is the caller's decision, made from
        /// <see cref="AssemblyInfo.IsMicrosoft"/> and <see cref="AssemblyInfo.IsManaged"/>; there is
        /// no server side filter that does it, which is why this no longer pretends to have one.
        /// Filtering here would also cost the counts the switches carry.
        /// </summary>
        public static List<AssemblyInfo> GetAssemblies(IOrganizationService service)
        {
            var query = new QueryExpression("pluginassembly")
            {
                ColumnSet = new ColumnSet("pluginassemblyid", "name", "isolationmode", "publickeytoken", "ismanaged"),
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
                    IsolationMode = GetOptionSet(e, "isolationmode", 2),
                    IsManaged = e.GetAttributeValue<bool?>("ismanaged") ?? false
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
        ///
        /// <paramref name="cancelled"/> is asked between round trips and between pages, which is
        /// as often as it can be: a query already on the wire cannot be recalled. That is still
        /// most of the wait on a link where the wait is the round trips rather than the rows -
        /// three of the four here happen after the first. A run that stops early returns nothing
        /// rather than a half list, because the caller that cancelled is not going to read it and
        /// the one that did not must never be handed a partial answer as a whole one.
        /// </summary>
        public static List<PluginTypeInfo> GetPluginTypes(IOrganizationService service, IList<Guid> assemblyIds,
            Func<bool> cancelled = null)
        {
            var stop = cancelled ?? (() => false);
            var nothing = new List<PluginTypeInfo>();

            var types = GetTypes(service, assemblyIds, stop);
            if (types.Count == 0)
            {
                return stop() ? nothing : types;
            }

            var byId = types.ToDictionary(t => t.Id);
            var steps = GetSteps(service, byId.Keys.ToList(), stop);
            if (stop())
            {
                return nothing;
            }

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
                foreach (var image in GetImages(service, stepsById.Keys.ToList(), stop))
                {
                    PluginStepInfo step;
                    if (stepsById.TryGetValue(image.Value, out step))
                    {
                        step.Images.Add(image.Key);
                    }
                }
            }

            if (stop())
            {
                return nothing;
            }

            // Steps in execution order, so the emitted attributes read the way they run -
            // which is the order Xrm Tools reads them back in, and not ours to rearrange.
            // The table is the last tiebreak rather than a grouping: a generic plugin has
            // a dozen steps at the same stage and rank whose message names tie too, and
            // without it their order would be whatever the query happened to return.
            // Regrouping for a reader is the summary comment's business, not this one's.
            foreach (var type in types)
            {
                type.Steps = type.Steps
                    .OrderBy(s => s.Stage)
                    .ThenBy(s => s.Rank)
                    .ThenBy(s => s.MessageName, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(s => s.PrimaryEntityName, StringComparer.OrdinalIgnoreCase)
                    .ToList();
            }

            return types.Where(t => t.Steps.Count > 0).OrderBy(t => t.TypeName).ToList();
        }

        private static List<PluginTypeInfo> GetTypes(IOrganizationService service, IList<Guid> assemblyIds, Func<bool> stop)
        {
            return Retrieve(service, assemblyIds, stop, ids => new QueryExpression("plugintype")
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
        private static List<KeyValuePair<PluginStepInfo, Guid>> GetSteps(IOrganizationService service, List<Guid> typeIds, Func<bool> stop)
        {
            return Retrieve(service, typeIds, stop, ids => BuildStepQuery(ids))
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
        private static List<KeyValuePair<PluginImageInfo, Guid>> GetImages(IOrganizationService service, List<Guid> stepIds, Func<bool> stop)
        {
            return Retrieve(service, stepIds, stop, ids => new QueryExpression("sdkmessageprocessingstepimage")
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
        /// The current column list of each named entity, keyed by logical name, in one metadata
        /// round trip that asks only for attribute names and one flag rather than the megabytes a
        /// full <c>RetrieveEntityRequest</c> per entity would pull.
        ///
        /// Companion attributes - the <c>_name</c> and <c>yominame</c> shadows a lookup or a
        /// picklist casts, <c>entityimage_url</c> and friends - are dropped by
        /// <c>AttributeOf</c>, because no registration picker offers them and every one kept
        /// would surface as a phantom "except".
        ///
        /// Every requested name gets an entry, empty for an entity the environment does not
        /// know, so the caller can cache the answer and never ask twice.
        /// </summary>
        public static Dictionary<string, EntityColumnsInfo> GetEntityColumns(IOrganizationService service, ICollection<string> entityNames)
        {
            var result = new Dictionary<string, EntityColumnsInfo>(StringComparer.OrdinalIgnoreCase);
            if (entityNames.Count == 0)
            {
                return result;
            }

            var query = new EntityQueryExpression
            {
                Criteria = new MetadataFilterExpression(LogicalOperator.And)
                {
                    Conditions =
                    {
                        new MetadataConditionExpression("LogicalName", MetadataConditionOperator.In, entityNames.ToArray())
                    }
                },
                Properties = new MetadataPropertiesExpression("LogicalName", "Attributes"),
                AttributeQuery = new AttributeQueryExpression
                {
                    Properties = new MetadataPropertiesExpression("LogicalName", "AttributeOf", "IsValidForUpdate")
                }
            };

            var response = (RetrieveMetadataChangesResponse)service.Execute(new RetrieveMetadataChangesRequest { Query = query });
            foreach (var entity in response.EntityMetadata)
            {
                var info = new EntityColumnsInfo();
                foreach (var attribute in entity.Attributes
                             .Where(a => a.AttributeOf == null)
                             .OrderBy(a => a.LogicalName, StringComparer.Ordinal))
                {
                    info.ImageColumns.Add(attribute.LogicalName);
                    if (attribute.IsValidForUpdate.GetValueOrDefault())
                    {
                        info.FilterColumns.Add(attribute.LogicalName);
                    }
                }

                result[entity.LogicalName] = info;
            }

            foreach (var name in entityNames.Where(n => !result.ContainsKey(n)))
            {
                result[name] = new EntityColumnsInfo();
            }

            return result;
        }

        /// <summary>
        /// Ids per request. Documenting a whole solution can put thousands of ids in front of these
        /// queries, and neither an In list nor a single page will take them all, so both are split.
        /// </summary>
        private const int IdsPerQuery = 250;

        private static List<Entity> Retrieve(IOrganizationService service, IList<Guid> ids, Func<bool> stop,
            Func<object[], QueryExpression> build)
        {
            var results = new List<Entity>();
            for (var offset = 0; offset < ids.Count; offset += IdsPerQuery)
            {
                if (stop())
                {
                    break;
                }

                var chunk = ids.Skip(offset).Take(IdsPerQuery).Cast<object>().ToArray();
                var query = build(chunk);
                query.PageInfo = new PagingInfo { Count = 5000, PageNumber = 1 };

                while (true)
                {
                    var page = service.RetrieveMultiple(query);
                    results.AddRange(page.Entities);
                    if (!page.MoreRecords || stop())
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
