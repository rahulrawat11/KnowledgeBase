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

namespace HolyNoodle.KnowledgeBase
{
    public class EntityManager
    {
        private IDriver _db;

        public EntityManager(string uri = "bolt://localhost:7687", string login = "neo4j", string passwd = "lolilol")
        {
            try
            {
                _db = GraphDatabase.Driver(uri, AuthTokens.Basic(login, passwd));
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.Message);
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

                            string name = prop.Name;
                            var value = prop.GetValue(entity);
                            var propType = prop.PropertyType;

                            if (value == null) continue; //don't create the relation if there is no value in it

                            //Should be true all the time but manage to prevent errors
                            if (!dico.ContainsKey(name))
                            {
                                dico.Add(name, new List<INode>());
                            }

                            //If it's a value type (or a string !!)
                            if (ReflexionHelper.IsValueType(propType))
                            {
                                dico[name].Add(GetNode("VALUE", value));
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
                                    if(ReflexionHelper.IsValueType(listGenericType))
                                    {
                                        foreach(var item in list)
                                        {
                                            dico[name].Add(GetNode("VALUE", item));
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
            return _db.Session().Run(new Statement("CREATE (entity:Entity {createdDate:{tick}}) RETURN entity", new Dictionary<string, object>
            {
                {"tick", DateTime.Now.Ticks}
            }));
        }

        // On peut enlever les retours des methodes d'ajouts de relation puisque la cypher request ne retourne rien
        private void AddRelationshipById(long srcId, long destId, string propertyName)
        {
            _db.Session().Run(new Statement("MATCH (entity) WHERE ID(entity)={srcId} MATCH (node) WHERE ID(node)={destId} CREATE (entity)-[:" + CypherFormat(propertyName) + "]->(node)", new Dictionary<string, object>
            {
                { "srcId", srcId},
                { "destId", destId}
            }));
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
            var result = _db.Session().Run(new Statement("MATCH (node:" + nodeType + " {name:{value}}) RETURN node", new Dictionary<string, object>
                {
                    {"value", value}
                }));

            var node = GetResultFromCypherRequest("node", result);

            if (node == null)
            {
                var creatResult = _db.Session().Run(new Statement("CREATE (node:" + nodeType + " {name:{value}}) RETURN node", new Dictionary<string, object>
                {
                    {"value", value}
                }));
                node = GetResultFromCypherRequest("node", creatResult);
            }
            return node;
        }

        public void GetEntity(Predicate<dynamic> predicate, int depth)
        {

        }

        public void TransformPredicate(Predicate<dynamic> predicate)
        {

        }
    }
}
