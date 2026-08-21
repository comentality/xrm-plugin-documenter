using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Metadata;
using Microsoft.Xrm.Sdk.Query;
using PluginStepCodegen.Harness;
using PluginStepCodegen.Logic;

namespace PluginStepCodegen.SlowHarness
{
    /// <summary>One round trip, as it happened.</summary>
    public class Call
    {
        /// <summary>Position among every call of the run, from 1.</summary>
        public int Index;

        /// <summary>Position among the calls for this table, from 1. What a scenario scripts against.</summary>
        public int Nth;

        /// <summary>The table asked about, or "metadata" for the columns request.</summary>
        public string Entity;

        /// <summary>The ids the In condition named, empty for a query that has none.</summary>
        public List<Guid> Ids = new List<Guid>();

        public DateTime Started;
        public DateTime Ended;

        public override string ToString()
        {
            return "#" + Index + " " + Entity
                   + (Ids.Count == 0 ? string.Empty : " (" + Ids.Count + " ids)")
                   + " " + (int)(Ended - Started).TotalMilliseconds + "ms";
        }
    }

    /// <summary>
    /// The environment, at a distance. Answers the four queries and the one metadata request
    /// <see cref="RegistrationQuery"/> makes, out of an in-memory registration that a scenario is
    /// free to change mid-flight, and takes as long over each as the scenario says it should.
    ///
    /// It is a fake environment rather than a fake <see cref="RegistrationQuery"/> on purpose:
    /// the query code is what turns a slow link into four sequential round trips, and a shim
    /// above it would hide exactly the thing being measured.
    ///
    /// Every call is logged - which table, how many ids, when it started and when it ended - and
    /// the log is the assertion surface for the questions latency actually raises: was the same
    /// assembly asked for twice, was anything asked at all after the user gave up, did the answer
    /// that landed last belong to the question asked last.
    /// </summary>
    public class SlowService : IOrganizationService
    {
        private readonly object _lock = new object();
        private readonly List<Call> _calls = new List<Call>();
        private int _index;
        private readonly Dictionary<string, int> _perEntity = new Dictionary<string, int>();

        /// <summary>
        /// The registration this environment holds. Mutable, and mutated by the scenarios that
        /// are about a refresh: a class that appears between two fetches is the only way to tell
        /// which of the two answers the tool ended up believing.
        /// </summary>
        public readonly List<AssemblyInfo> Assemblies;
        public readonly Dictionary<Guid, List<PluginTypeInfo>> Types;

        /// <summary>How long this call should take. The whole point of the harness.</summary>
        public Func<Call, int> Latency = call => 0;

        /// <summary>What this call should throw instead of answering, or null to answer.</summary>
        public Func<Call, Exception> Fails = call => null;

        public SlowService(List<AssemblyInfo> assemblies, Dictionary<Guid, List<PluginTypeInfo>> types)
        {
            Assemblies = assemblies;
            Types = types;
        }

        /// <summary>The sample environment, every assembly's types registered against it.</summary>
        public static SlowService Sampled()
        {
            var assemblies = Sample.Assemblies();
            var types = assemblies.ToDictionary(a => a.Id, a => Sample.Types(a));
            return new SlowService(assemblies, types);
        }

        public List<Call> Log()
        {
            lock (_lock) return _calls.ToList();
        }

        public List<Call> Log(string entity)
        {
            return Log().Where(c => c.Entity == entity).ToList();
        }

        /// <summary>
        /// Whether two calls ever overlapped in time. A tool that asks one question at a time
        /// puts one round trip on the wire at a time, and this is how that is checked rather
        /// than assumed.
        /// </summary>
        public bool Overlapped()
        {
            var log = Log().OrderBy(c => c.Started).ToList();
            for (var i = 1; i < log.Count; i++)
            {
                if (log[i].Started < log[i - 1].Ended) return true;
            }

            return false;
        }

        private Call Begin(string entity, IEnumerable<Guid> ids)
        {
            var call = new Call { Entity = entity, Started = DateTime.UtcNow };
            if (ids != null) call.Ids.AddRange(ids);

            lock (_lock)
            {
                call.Index = ++_index;
                int nth;
                _perEntity.TryGetValue(entity, out nth);
                _perEntity[entity] = call.Nth = nth + 1;
                _calls.Add(call);
            }

            return call;
        }

        /// <summary>
        /// The answer as it stood when the question arrived, and then the wait.
        ///
        /// That order is load bearing rather than tidy. A query is settled where the data is, and
        /// only the answer travels; a fake that slept first and read afterwards would hand a
        /// question asked three seconds ago the registration as it is now - which is precisely
        /// the confusion the refresh scenario exists to catch, quietly resolved in the harness's
        /// favour. A failure waits just as long: a link that is going to time out takes as long
        /// about it as one that is going to work.
        /// </summary>
        private T Answer<T>(Call call, Func<T> answer)
        {
            var failure = Fails(call);
            var result = failure == null ? answer() : default(T);

            var delay = Latency(call);
            if (delay > 0) Thread.Sleep(delay);

            lock (_lock) call.Ended = DateTime.UtcNow;
            if (failure != null) throw failure;
            return result;
        }

        public EntityCollection RetrieveMultiple(QueryBase query)
        {
            var q = query as QueryExpression;
            if (q == null) throw new NotSupportedException("The tool only issues QueryExpressions.");

            switch (q.EntityName)
            {
                case "pluginassembly":
                    return Answer(Begin(q.EntityName, null), () => Collect(AssemblyRows()));

                case "plugintype":
                {
                    var ids = In(q, "pluginassemblyid");
                    return Answer(Begin(q.EntityName, ids), () => Collect(TypeRows(ids)));
                }

                case "sdkmessageprocessingstep":
                {
                    var ids = In(q, "plugintypeid");
                    return Answer(Begin(q.EntityName, ids), () => Collect(StepRows(ids)));
                }

                case "sdkmessageprocessingstepimage":
                {
                    var ids = In(q, "sdkmessageprocessingstepid");
                    return Answer(Begin(q.EntityName, ids), () => Collect(ImageRows(ids)));
                }
            }

            throw new NotSupportedException("Nothing in the tool queries " + q.EntityName + ".");
        }

        public OrganizationResponse Execute(OrganizationRequest request)
        {
            if (!(request is RetrieveMetadataChangesRequest))
            {
                throw new NotSupportedException("Nothing in the tool executes " + request.RequestName + ".");
            }

            // The columns are garnish on the comment and the tool already treats a miss as one -
            // every requested name comes back with an empty entry. What the harness is after here
            // is the round trip, which is real whatever the answer is.
            return Answer(Begin("metadata", null), () =>
            {
                var response = new RetrieveMetadataChangesResponse();
                response.Results = new ParameterCollection
                {
                    { "EntityMetadata", new EntityMetadataCollection() }
                };
                return (OrganizationResponse)response;
            });
        }

        private static List<Guid> In(QueryExpression query, string attribute)
        {
            var condition = query.Criteria.Conditions.FirstOrDefault(c => c.AttributeName == attribute);
            return condition == null
                ? new List<Guid>()
                : condition.Values.OfType<Guid>().ToList();
        }

        private static EntityCollection Collect(IEnumerable<Entity> rows)
        {
            // One page. Paging is the query code's business and is exercised by the real
            // environment; what this harness varies is how long a page takes to arrive.
            return new EntityCollection(rows.ToList()) { MoreRecords = false };
        }

        private IEnumerable<Entity> AssemblyRows()
        {
            lock (_lock)
            {
                return Assemblies.Select(a =>
                {
                    var e = new Entity("pluginassembly", a.Id);
                    e["pluginassemblyid"] = a.Id;
                    e["name"] = a.Name;
                    e["publickeytoken"] = a.PublicKeyToken;
                    e["isolationmode"] = new OptionSetValue(a.IsolationMode);
                    e["ismanaged"] = a.IsManaged;
                    return e;
                })
                    .OrderBy(e => e.GetAttributeValue<string>("name"), StringComparer.Ordinal)
                    .ToList();
            }
        }

        private IEnumerable<Entity> TypeRows(List<Guid> assemblyIds)
        {
            lock (_lock)
            {
                return Registered(assemblyIds).Select(t =>
                {
                    var e = new Entity("plugintype", t.Id);
                    e["plugintypeid"] = t.Id;
                    e["typename"] = t.TypeName;
                    e["friendlyname"] = t.FriendlyName;
                    e["description"] = t.Description;
                    e["pluginassemblyid"] = new EntityReference("pluginassembly", t.AssemblyId);
                    return e;
                }).ToList();
            }
        }

        private IEnumerable<Entity> StepRows(List<Guid> typeIds)
        {
            lock (_lock)
            {
                var wanted = new HashSet<Guid>(typeIds);
                return Registered(null)
                    .Where(t => wanted.Contains(t.Id))
                    .SelectMany(t => t.Steps.Select(s =>
                    {
                        var e = new Entity("sdkmessageprocessingstep", s.Id);
                        e["sdkmessageprocessingstepid"] = s.Id;
                        e["name"] = s.Name;
                        e["stage"] = new OptionSetValue(s.Stage);
                        e["mode"] = new OptionSetValue(s.Mode);
                        e["rank"] = s.Rank;
                        e["filteringattributes"] = s.FilteringAttributes;
                        e["configuration"] = s.Configuration;
                        e["description"] = s.Description;
                        e["asyncautodelete"] = s.AsyncAutoDelete;
                        e["statecode"] = new OptionSetValue(s.IsDisabled ? 1 : 0);
                        e["plugintypeid"] = new EntityReference("plugintype", t.Id);
                        // The three links the step query carries, as the aliases it reads back.
                        e["msg.name"] = new AliasedValue("sdkmessage", "name", s.MessageName);
                        if (s.PrimaryEntityName != null)
                        {
                            e["flt.primaryobjecttypecode"] =
                                new AliasedValue("sdkmessagefilter", "primaryobjecttypecode", s.PrimaryEntityName);
                        }

                        if (s.ImpersonatingUser != null)
                        {
                            e["usr.fullname"] = new AliasedValue("systemuser", "fullname", s.ImpersonatingUser);
                        }

                        return e;
                    })).ToList();
            }
        }

        private IEnumerable<Entity> ImageRows(List<Guid> stepIds)
        {
            lock (_lock)
            {
                var wanted = new HashSet<Guid>(stepIds);
                return Registered(null)
                    .SelectMany(t => t.Steps)
                    .Where(s => wanted.Contains(s.Id))
                    .SelectMany(s => s.Images.Select(i =>
                    {
                        var e = new Entity("sdkmessageprocessingstepimage", Guid.NewGuid());
                        e["imagetype"] = new OptionSetValue(i.ImageType);
                        e["entityalias"] = i.EntityAlias;
                        e["attributes"] = i.Attributes;
                        e["name"] = i.Name;
                        e["messagepropertyname"] = i.MessagePropertyName;
                        e["sdkmessageprocessingstepid"] = new EntityReference("sdkmessageprocessingstep", s.Id);
                        return e;
                    }))
                    .OrderBy(e => e.GetAttributeValue<OptionSetValue>("imagetype").Value)
                    .ToList();
            }
        }

        /// <summary>Every registered type, or only those of the named assemblies.</summary>
        private IEnumerable<PluginTypeInfo> Registered(List<Guid> assemblyIds)
        {
            var pairs = assemblyIds == null
                ? Types
                : Types.Where(p => assemblyIds.Contains(p.Key)).ToDictionary(p => p.Key, p => p.Value);
            return pairs.Values.SelectMany(t => t).ToList();
        }

        public Guid Create(Entity entity) { throw new NotSupportedException("The tool never writes."); }
        public void Update(Entity entity) { throw new NotSupportedException("The tool never writes."); }
        public void Delete(string entityName, Guid id) { throw new NotSupportedException("The tool never writes."); }
        public void Associate(string entityName, Guid entityId, Relationship relationship, EntityReferenceCollection relatedEntities) { throw new NotSupportedException("The tool never writes."); }
        public void Disassociate(string entityName, Guid entityId, Relationship relationship, EntityReferenceCollection relatedEntities) { throw new NotSupportedException("The tool never writes."); }
        public Entity Retrieve(string entityName, Guid id, Microsoft.Xrm.Sdk.Query.ColumnSet columnSet) { throw new NotSupportedException("The tool retrieves in bulk only."); }
    }
}
