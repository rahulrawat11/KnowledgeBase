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
        public async Task<List<KnowledgeEntity>> Filter(KnowledgeEntity entity, List<string> filters)
        {
            var queryBuilder = new StringBuilder("MATCH (entity:" + ENTITY_NAME);
            var parameters = new Dictionary<string, object>();

            if (entity.Id != null)
            {
                queryBuilder.Append(" {id:{pid}}");
                parameters.Add("pid", entity.Id);
            }
            queryBuilder.Append(")-[key]->(property)");
            var whereCount = 0;
            foreach (var property in entity.Properties)
            {
                foreach (var relation in property.Value)
                {
                    if (relation.Value != null)
                    {
                        queryBuilder.Append(" MATCH (entity)-[:" + CypherFormat(property.Key) + "]->(:" + VALUE_NAME + " {value:{pvalue" + whereCount + "}})");
                        parameters.Add("pvalue" + whereCount, relation.Value);
                        ++whereCount;
                    }
                    else
                    {
                        queryBuilder.Append(" MATCH (entity)-[:" + CypherFormat(property.Key) + "]->()");
                    }
                }
            }

            var filterCount = 0;
            foreach (var filter in filters)
            {
                queryBuilder.Append(" OPTIONAL MATCH (entity)-[]->(target" + filterCount + ":Value) WHERE target" + filterCount + ".value =~ \"(?i).*" + filter + ".*\"");
                queryBuilder.Append(" WITH entity, key, property, target" + filterCount + " WHERE not target" + filterCount + " is null");
                ++filterCount;
            }

            queryBuilder.Append(" RETURN entity, type(key) as key, property.value as value, key as relInfo ORDER BY key.weight DESC");
            using (var session = _db.Session())
            {
                var results = session.Run(new Statement(queryBuilder.ToString(), parameters));
                var finalList = new Dictionary<string, KnowledgeEntity>();
                try
                {
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
                            //Start = relationInfo.Properties.ContainsKey("start") ? (long)relationInfo.Properties["start"] : 0,
                            //End = relationInfo.Properties.ContainsKey("end") ? (long)relationInfo.Properties["end"] : 0,
                            Weight = relationInfo.Properties.ContainsKey("weight") ? (long)relationInfo.Properties["weight"] : 0,
                            Value = result.Values["value"]
                        });
                    }
                }
                catch (Exception ex)
                {
                }
                return finalList.Values.ToList();
            }
        }
        public async Task<List<KnowledgeEntity>> Get(KnowledgeEntity entity)
        {

            var queryBuilder = new StringBuilder("MATCH (entity:" + ENTITY_NAME);
            var parameters = new Dictionary<string, object>();

            if (entity.Id != null)
            {
                queryBuilder.Append(" {id:{pid}}");
                parameters.Add("pid", entity.Id);
            }
            queryBuilder.Append(")-[key]->(property)");

            var whereCount = 0;

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
                        if (relation.Value == null)
                        {
                            queryBuilder.Append(" MATCH (entity)-[:" + CypherFormat(property.Key) + "]->()");
                        }
                        else
                        {
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

                    }
                    queryBuilder.Append(" AND (rel" + whereCount + ".weight >= " + relation.Weight + " OR rel" + whereCount + ".weight is null)");

                    //if (relation.Start > 0)
                    //{
                    //    queryBuilder.Append(" AND rel" + whereCount + ".start >= " + relation.Start);
                    //}
                    //if (relation.End > 0)
                    //{
                    //    queryBuilder.Append(" AND rel" + whereCount + ".end <= " + relation.End);
                    //}
                }
                ++whereCount;
            }
            queryBuilder.Append(" RETURN entity, type(key) as key, property.value as value, key as relInfo ORDER BY key.weight DESC");
            using (var session = _db.Session())
            {
                var results = session.Run(queryBuilder.ToString(), parameters);

                var finalList = new Dictionary<string, KnowledgeEntity>();
                try
                {
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
                            //Start = relationInfo.Properties.ContainsKey("start") ? (long)relationInfo.Properties["start"] : 0,
                            //End = relationInfo.Properties.ContainsKey("end") ? (long)relationInfo.Properties["end"] : 0,
                            Weight = relationInfo.Properties.ContainsKey("weight") ? (long)relationInfo.Properties["weight"] : 0,
                            Value = result.Values["value"]
                        });
                    }
                }
                catch (Exception ex)
                {
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
        public async Task<bool> SetTests(int numberOfNodes, List<string> properties, List<object> values)
        {
            var _db = new KnowledgeBase("bolt://localhost:7687/", "neo4j", "nightwish");
            var rand = new Random();
            for (var i = 0; i < numberOfNodes; ++i)
            {
                var entity = await _db.CreateEntity();
                for (var j = 0; j < properties.Count(); ++j)
                {
                    object value = null;
                    if (values[j].ToString() == "StringRandom")
                        value = rand.Next(0, 9999999).ToString();
                    else value = values[j];
                    entity.AddRelationship(properties[j], value);
                }
                await _db.UpdateEntity(entity);
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
                    session.Run(new Statement("CREATE INDEX ON: " + ENTITY_NAME + "(id)"));
                    session.Run(new Statement("CREATE INDEX ON: " + VALUE_NAME + "(value)"));
                    session.Run(new Statement("CREATE CONSTRAINT ON (entity:" + ENTITY_NAME + ") ASSERT entity.id IS UNIQUE"));
                    session.Run(new Statement("CREATE CONSTRAINT ON (value:" + VALUE_NAME + ") ASSERT value.value IS UNIQUE"));

                    session.Run(new Statement("CREATE (n:World) return n"));
                    return true;
                }
                catch
                {
                    return false;
                }
            }

        }
        public async Task<bool> DeleteEntity(KnowledgeEntity entity)
        {
            if (string.IsNullOrEmpty(entity.Id)) return false;

            using (var session = _db.Session())
            {
                session.Run("MATCH (entity:" + ENTITY_NAME + " {id: {pId})-[r]->() DELETE r, entity", new Dictionary<string, object>
                {
                    { "pId", entity.Id }
                });
            }
            return true;
        }
        #endregion

        #region Private Methods
        private async Task<bool> SetProperties(KnowledgeEntity entity, ISession session)
        {
            foreach (var property in entity.Properties.Where(p => p.Value.Any(r => (!r.IsFromDatabase || r.WeightChanged))))
            {
                foreach (var relationship in property.Value.Where(r => (!r.IsFromDatabase || r.WeightChanged)))
                {
                    var parameters = new Dictionary<string, object>();
                    var queryBuilder = new StringBuilder("MATCH (entity:" + ENTITY_NAME + " { id:{pid}}) ");
                    if (relationship.Value is KnowledgeEntity)
                    {
                        queryBuilder.Append(" MATCH (target:" + ENTITY_NAME + " { id:{ptargetId}}) ");
                        parameters.Add("ptargetId", ((KnowledgeEntity)relationship.Value).Id);
                    }
                    else
                    {
                        await GetValueNode(relationship.Value, session);
                        queryBuilder.Append(" MATCH (target:" + VALUE_NAME + " {value:{pvalue}})");
                        parameters.Add("pvalue", relationship.Value);
                    }

                    if (!relationship.IsFromDatabase)
                    {
                        queryBuilder.Append(" CREATE (entity)-[:" + CypherFormat(property.Key) + " {");
                        queryBuilder.Append("weight:{pweight}}]->(target)");
                    }
                    else
                    {
                        if (relationship.WeightChanged)
                        {
                            queryBuilder.Append(" MATCH (entity)-[r:" + CypherFormat(property.Key) + "]->(target)");
                            queryBuilder.Append(" SET r.weight = {pweight}");
                        }
                    }

                    parameters.Add("pid", entity.Id);
                    //parameters.Add("pstart", DateTime.Now.Ticks);
                    //parameters.Add("pend", 0);
                    parameters.Add("pweight", relationship.Weight);
                    session.Run(new Statement(queryBuilder.ToString(), parameters));
                    relationship.IsFromDatabase = true;
                    relationship.WeightChanged = false;
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
