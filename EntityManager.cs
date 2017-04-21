using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Linq.Expressions;
using System.Net.NetworkInformation;
using System.Text;
using System.Threading.Tasks;
using Neo4j.Driver.V1;
using HolyNoodle.Utility;
using System.Dynamic;

namespace HolyNoodle.KnowledgeBase
{
    public class EntityManager
    {
        private IDriver _db;
        public Configuration Configuration { get; internal set; }

        public EntityManager(string uri = "bolt://localhost:7687", string login = "neo4j", string passwd = "lolilol", string entityTypeName = "Entity", string valueTypeName = "Value")
        {
            try
            {
                _db = GraphDatabase.Driver(uri, AuthTokens.Basic(login, passwd));
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.Message);
            }

            Configuration = new Configuration
            {
                EntityTypeName = entityTypeName,
                ValueTypeName = valueTypeName
            };
        }

        public void UpdateEntity(IEntity entity, bool impactSubEntities = false)
        {
            try
            {
                using (var session = _db.Session())
                {
                    var databaseEntity = GetQueryBuilder().ById(entity.Node.Id).Execute().FirstOrDefault() as IDictionary<string, object>;

                    if (databaseEntity == null) return;

                    PropertyDescriptorCollection properties = TypeDescriptor.GetProperties(entity);
                    var entityInterfaceProperties = typeof(IEntity).GetProperties();
                    foreach (PropertyDescriptor prop in properties)
                    {
                        //if the property comes from the IEntity interface, just skip it
                        if (entityInterfaceProperties.Any(p => p.Name == prop.Name)) continue;
                        string name = prop.Name;
                        var value = prop.GetValue(entity);
                        var propType = prop.PropertyType;

                        if (!databaseEntity.ContainsKey(name))
                        {
                            //TODO GET node and value
                        }
                        else
                        {
                            if (ReflexionHelper.IsValueType(propType) && databaseEntity[name] != value)
                            {
                                var valueNode = GetNode(Configuration.ValueTypeName, value);
                                var oldValueNode = GetNode(Configuration.ValueTypeName, databaseEntity[name]);

                                //Remove old relationship
                                RemoveRelationshipById(entity.Node.Id, oldValueNode.Id, name);
                                //Add new relationship
                                AddRelationshipById(entity.Node.Id, valueNode.Id, name);
                            }
                            //It's an referenced type
                            else
                            {
                                //We are on an object
                                //Is it an IEnumerable ?
                                if (ReflexionHelper.IsEnumarable(propType))
                                {
                                    var list = value as IEnumerable;
                                    if (list == null) continue;
                                    var listGenericType = ReflexionHelper.GetGenericTypeDefintion(list.GetType());
                                    //Is the collection generic type is a value type (or a string)
                                    if (ReflexionHelper.IsValueType(listGenericType))
                                    {
                                        foreach (var item in list)
                                        {
                                            //TODO
                                        }
                                    }
                                    //The collection seems to carry entities
                                    else
                                    {
                                        foreach (var item in list)
                                        {
                                            //TODO
                                        }
                                    }
                                }
                                //It's an entity
                                else
                                {
                                    var oldEntityInterface = databaseEntity[name] as IEntity;
                                    var entityInterface = value as IEntity;
                                    if (oldEntityInterface != null && oldEntityInterface.Node != null && entityInterface != null)
                                    {
                                        //Remove old relationship
                                        RemoveRelationshipById(entity.Node.Id, oldEntityInterface.Node.Id, name);
                                        //Add new relationship
                                        AddRelationshipById(entity.Node.Id, entityInterface.Node.Id, name);
                                    }
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                throw new Exception("Error while updating entity", ex);
            }
        }

        public INode CreateEntity(object entity = null)
        {
            try
            {
                using (var session = _db.Session())
                {
                    //Create the entity node
                    var res = InsertEntity();
                    var entityNode = GetResultFromCypherRequest("entity", res);

                    if (entity != null)
                    {
                        var dico = new Dictionary<string, List<INode>>();
                        if (entity is IEntity)
                        {
                            ((IEntity)entity).Node = entityNode;
                        }
                        //Loop over the properties of the object passed in parameters
                        PropertyDescriptorCollection properties = TypeDescriptor.GetProperties(entity);
                        var entityInterfaceProperties = typeof(IEntity).GetProperties();
                        foreach (PropertyDescriptor prop in properties)
                        {
                            //if the property comes from the IEntity interface, just skip it
                            if (entityInterfaceProperties.Any(p => p.Name == prop.Name)) continue;

                            var value = prop.GetValue(entity);
                            if (value == null) continue; //don't create the relation if there is no value in it

                            var propType = prop.PropertyType;
                            string name = prop.Name;

                            //Should be true all the time but manage to prevent errors
                            if (!dico.ContainsKey(name))
                            {
                                dico.Add(name, new List<INode>());
                            }

                            //If it's a value type (or a string !!)
                            if (ReflexionHelper.IsValueType(propType))
                            {
                                dico[name].Add(GetNode(Configuration.ValueTypeName, value));
                            }
                            //It's an referenced type
                            else
                            {
                                //We are on an object
                                //Is it an IEnumerable ?
                                if (ReflexionHelper.IsEnumarable(propType))
                                {
                                    var list = value as IEnumerable;
                                    var listGenericType = ReflexionHelper.GetGenericTypeDefintion(list.GetType());
                                    //Is the collection generic type is a value type (or a string)
                                    if (ReflexionHelper.IsValueType(listGenericType))
                                    {
                                        foreach (var item in list)
                                        {
                                            dico[name].Add(GetNode(Configuration.ValueTypeName, item));
                                        }
                                    }
                                    //The collection seems to carry entities
                                    else
                                    {
                                        foreach (var item in list)
                                        {
                                            var entityInterface = item as IEntity;
                                            if (entityInterface != null && entityInterface.Node != null)
                                            {
                                                dico[name].Add(entityInterface.Node);
                                            }
                                            else
                                            {
                                                dico[name].Add(CreateEntity(item));
                                            }
                                        }
                                    }
                                }
                                //It's an entity
                                else
                                {
                                    var entityInterface = value as IEntity;
                                    if (entityInterface != null && entityInterface.Node != null)
                                    {
                                        dico[name].Add(entityInterface.Node);
                                    }
                                    else
                                    {
                                        dico[name].Add(CreateEntity(value));
                                    }
                                }
                            }
                        }

                        //Once all the nodes have been retrieved
                        //Create the relationship between the entity and the target of the relationship
                        foreach (var item in dico)
                        {
                            foreach (var value in item.Value)
                            {
                                AddRelationshipById(entityNode.Id, value.Id, item.Key);
                            }
                        }

                        return entityNode;
                    }
                }
            }
            catch (Exception ex)
            {
                throw new Exception("Error while inserting the entity", ex);
            }

            return null;
        }

        public static string CypherFormat(string s)
        {
            return s.Replace(" ", "_").Replace("°", "");
        }

        private IStatementResult InsertEntity()
        {
            using (var session = _db.Session())
            {
                return session.Run(new Statement($"CREATE (entity:{Configuration.EntityTypeName} {{createdDate:{{tick}}}}) RETURN entity", new Dictionary<string, object>
                {
                    {"tick", DateTime.Now.Ticks}
                }));
            }
        }

        // On peut enlever les retours des methodes d'ajouts de relation puisque la cypher request ne retourne rien
        private void AddRelationshipById(long srcId, long destId, string propertyName)
        {
            using (var session = _db.Session())
            {
                session.Run(new Statement("MATCH (entity) WHERE ID(entity)={srcId} MATCH (node) WHERE ID(node)={destId} CREATE (entity)-[:" + CypherFormat(propertyName) + "]->(node)", new Dictionary<string, object>
                {
                    { "srcId", srcId},
                    { "destId", destId}
                }));
            }
        }

        private void RemoveRelationshipById(long srcId, long destId, string propertyName)
        {
            using (var session = _db.Session())
            {
                session.Run(new Statement($"MATCH (entity)-[r:{propertyName}]->(value) WHERE ID(entity) = {{pEntityId}} and ID(value) = {{pValueId}} DELETE r", new Dictionary<string, object>
                {
                    { "pEntityId", srcId },
                    { "pValueId", destId }
                }));
            }
        }

        private INode GetResultFromCypherRequest(string nodeName, IStatementResult result)
        {
            INode node = null;

            foreach (var r in result)
            {
                if (r.Values.ContainsKey(nodeName))
                    node = r.Values[nodeName].As<INode>();
            }
            return node;
        }

        private INode GetNode(string nodeType, object value)
        {
            using (var session = _db.Session())
            {
                var result = session.Run(new Statement("MATCH (node:" + nodeType + " {name:{value}}) RETURN node", new Dictionary<string, object>
                {
                    {"value", value}
                }));

                var node = GetResultFromCypherRequest("node", result);

                if (node == null)
                {
                    var creatResult = session.Run(new Statement("CREATE (node:" + nodeType + " {name:{value}, type:{type}}) RETURN node", new Dictionary<string, object>
                {
                    {"value", value },
                    {"type", value.GetType().ToString() }
                }));
                    node = GetResultFromCypherRequest("node", creatResult);
                }
                return node;
            }
        }

        public CypherQueryBuilder GetQueryBuilder()
        {
            return new CypherQueryBuilder(_db.Session(), Configuration);
        }

        public IEnumerable<IEntity> GetEntities(int depth = 0, int maxDeph = 1, params Predicate<object>[] predicates)
        {
            if (depth == maxDeph)
                return null;
            return null;
        }

        public void TransformPredicate(Predicate<dynamic> predicate)
        {

        }
    }


    public static class IEntityExtensions
    {
        public static void Update(this IEntity entity, EntityManager em, bool impactSubEntities = false)
        {
            em.UpdateEntity(entity, impactSubEntities);
        }

        public static T Populate<T>(this IEntity entity, EntityManager em) where T : IEntity
        {
            using (var qb = em.GetQueryBuilder().ById(entity.Node.Id))
            {
                var entityType = typeof(T);
                var method = ReflexionHelper.GetMethod(qb.GetType(), "Execute", true);
                var result = (IEnumerable)method.MakeGenericMethod(entityType).Invoke(qb, null);
                foreach (var r in result)
                    return (T)r;                
            }
            return (T)(object)null;
        }
    }

    public class Configuration
    {
        public string EntityTypeName { get; set; }
        public string ValueTypeName { get; set; }
    }
}
