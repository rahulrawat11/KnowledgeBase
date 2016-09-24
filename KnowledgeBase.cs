using Neo4j.Driver.V1;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace HolyNoodle.KnowledgeBase
{
    public class KnowledgeBase
    {
        public const string ENTITY_NAME = "Entity";
        public const string VALUE_NAME = "Value";

        private IDriver _db;

        public KnowledgeBase(string uri, string login, string password)
        {
            _db = GraphDatabase.Driver(uri, AuthTokens.Basic(login, password));
        }

        public static string CypherFormat(string s)
        {
            return s.Replace(" ", "_").Replace("°", "");
        }

        #region Public Methods
        public async Task<List<KnowledgeEntity>> Get(KnowledgeEntity entity)
        {
            using (var session = _db.Session())
            {
                var queryBuilder = new StringBuilder("MATCH (entity:" + ENTITY_NAME);
                if (entity.Id != null)
                {
                    queryBuilder.Append(" {id:{pid}}");
                }
                queryBuilder.Append(")-[key]->(property)");

                var whereCount = 0;
                var parameters = new Dictionary<string, object>();
                parameters.Add("pid", entity.Id);
                foreach (var property in entity.Properties)
                {
                    foreach (var relation in property.Value)
                    {
                        if (relation.Value is KnowledgeEntity)
                        {
                            queryBuilder.Append(" MATCH (entity)-[rel" + whereCount + ":" + CypherFormat(property.Key) + "]->(target" + whereCount + ":" + ENTITY_NAME + " {id:{pvalue" + whereCount + "}})");
                            parameters.Add("pvalue" + whereCount, ((KnowledgeEntity)relation.Value).Id);
                        }
                        else
                        {
                            queryBuilder.Append(" MATCH (entity)-[rel" + whereCount + ":" + CypherFormat(property.Key) + "]->(target" + whereCount + ":" + VALUE_NAME + ")");
                            if (relation.Value is string && ((string)relation.Value).IndexOf("*") > -1)
                            {
                                queryBuilder.Append(" WHERE target" + whereCount + ".value =~ {pvalue" + whereCount + "}");
                                parameters.Add("pvalue" + whereCount, ((string)relation.Value).Replace("*", ".*"));
                            }
                            else
                            {
                                queryBuilder.Append(" WHERE target" + whereCount + ".value = {pvalue" + whereCount + "}");
                                parameters.Add("pvalue" + whereCount, relation.Value);
                            }

                        }
                        queryBuilder.Append(" AND (rel" + whereCount + ".weight >= " + relation.Weight + " OR rel" + whereCount + ".weight is null)");

                        if (relation.Start > 0)
                        {
                            queryBuilder.Append(" AND rel" + whereCount + ".start >= " + relation.Start);
                        }
                        if (relation.End > 0)
                        {
                            queryBuilder.Append(" AND rel" + whereCount + ".end <= " + relation.End);
                        }
                    }
                    ++whereCount;
                }
                queryBuilder.Append(" RETURN entity, type(key) as key, property.value as value, key as relInfo ORDER BY key.weight DESC");

                var results = session.Run(queryBuilder.ToString(), parameters);

                var finalList = new Dictionary<string, KnowledgeEntity>();
                foreach (var result in results)
                {
                    var id = (result.Values["entity"] as INode).Properties["id"] as string;
                    var relationInfo = result.Values["relInfo"] as IRelationship;
                    if (!finalList.ContainsKey(id))
                    {
                        finalList.Add(id, new KnowledgeEntity { Id = id });
                    }
                    var resultEntity = finalList[id];

                    var key = result.Values["key"] as string;
                    if (!resultEntity.Properties.ContainsKey(key))
                    {
                        resultEntity.Properties.Add(key, new List<KnowledgeEntityRelationship>());
                    }

                    resultEntity.Properties[key].Add(new KnowledgeEntityRelationship
                    {
                        IsFromDatabase = true,
                        Start = relationInfo.Properties.ContainsKey("start") ? (long)relationInfo.Properties["start"] : 0,
                        End = relationInfo.Properties.ContainsKey("end") ? (long)relationInfo.Properties["end"] : 0,
                        Weight = relationInfo.Properties.ContainsKey("weight") ? (long)relationInfo.Properties["weight"] : 0,
                        Value = result.Values["value"]
                    });
                }
                return finalList.Values.ToList();
            }
        }
        public async Task<KnowledgeEntity> CreateEntity()
        {
            using (var session = _db.Session())
            {
                var newEntity = new KnowledgeEntity { Id = await CreateUniqueId(session) };
                session.Run(new Statement("CREATE (entity:" + ENTITY_NAME + " { id:{pid}}) RETURN entity", new Dictionary<string, object> { { "pid", newEntity.Id } }));
                return newEntity;
            }
        }
        public async Task<KnowledgeEntity> UpdateEntity(KnowledgeEntity entity)
        {
            using (var session = _db.Session())
            {
                var result = session.Run(new Statement("MATCH (entity:" + ENTITY_NAME + " {id:{pid}}) RETURN entity", new Dictionary<string, object> { { "pid", entity.Id } }));
                if (result.Any())
                {
                    await SetProperties(entity, session);
                    return entity;
                }
                return null;
            }
        }
        public async Task<bool> Execute(string query)
        {
            using (var session = _db.Session())
            {
                session.Run(new Statement(query));
            }
            return true;
        }
        public async Task<bool> InitDatabase()
        {
            using (var session = _db.Session())
            {
                var result = session.Run(new Statement("MATCH (n:World) return n"));
                if (result.Any()) return true;
                try
                {
                    session.Run(new Statement("CREATE INDEX ON: Entity(id)"));
                    session.Run(new Statement("CREATE INDEX ON: Value(value)"));
                    session.Run(new Statement("CREATE (n:World) return n"));
                    return true;
                }
                catch
                {
                    return false;
                }
            }

        }
        #endregion

        #region Private Methods
        private async Task<bool> SetProperties(KnowledgeEntity entity, ISession session)
        {
            foreach (var property in entity.Properties.Where(p => p.Value.Any(r => !r.IsFromDatabase && r.Value != null)))
            {
                foreach (var relationship in property.Value)
                {
                    var parameters = new Dictionary<string, object>();
                    var queryBuilder = new StringBuilder("MATCH (entity:" + ENTITY_NAME + " { id:{pid}}) ");
                    if (relationship.Value is KnowledgeEntity)
                    {
                        queryBuilder.Append("MATCH (target:" + ENTITY_NAME + " { id:{ptargetId}}) ");
                        parameters.Add("ptargetId", ((KnowledgeEntity)relationship.Value).Id);
                    }
                    else
                    {
                        await GetValueNode(relationship.Value, session);
                        queryBuilder.Append("MATCH (target:" + VALUE_NAME + " {value:{pvalue}})");
                        parameters.Add("pvalue", relationship.Value);
                    }

                    queryBuilder.Append("CREATE (entity)-[:" + CypherFormat(property.Key) + " {");
                    queryBuilder.Append("start:{pstart},");
                    queryBuilder.Append("end: {pend},");
                    queryBuilder.Append("weight:{pweight}}]->(target)");

                    parameters.Add("pid", entity.Id);
                    parameters.Add("pstart", DateTime.Now.Ticks);
                    parameters.Add("pend", 0);
                    parameters.Add("pweight", relationship.Weight);
                    session.Run(new Statement(queryBuilder.ToString(), parameters));
                }
            }
            return true;
        }

        private async Task<string> CreateUniqueId(ISession session)
        {
            var found = true;
            var id = "";
            while (found)
            {
                id = Guid.NewGuid().ToString() + DateTime.Now.Millisecond;
                found = (long)session.Run(new Statement("MATCH (:" + ENTITY_NAME + " {id:{pid}}) RETURN COUNT(*) as count", new Dictionary<string, object> { { "pid", id } })).First().Values["count"] > 0;
            }
            return id;
        }

        private async Task<INode> GetValueNode(object value, ISession session)
        {
            var result = session.Run(new Statement("MATCH (value:" + VALUE_NAME + " {value:{pvalue}}) RETURN value", new Dictionary<string, object> { { "pvalue", value } })).ToList();
            if (result.Any())
            {
                return result.First().Values["value"] as INode;
            }
            return session.Run(new Statement("CREATE (value:" + VALUE_NAME + " {value:{pvalue}}) RETURN value", new Dictionary<string, object> { { "pvalue", value } })).FirstOrDefault().Values["value"] as INode;
        }
        #endregion
    }
}
