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

namespace HolyNoodle.KnowledgeBase
{
    public class ManageEntity
    {
        private IDriver _db;

        public ManageEntity(string uri = "bolt://localhost:7687", string login = "neo4j", string passwd = "lolilol")
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

        private void AddCollection(List<object> collection)
        {
            foreach (var element in collection)
            {
                // TODO: check entity
                if (element is ICollection)
                {
                    AddCollection((List<object>)element);
                }

                GetNode("Value", element);
            }
        }

        private void AddRelationship(List<object> collection, long id, string relation)
        {
            foreach (var element in collection)
            {
                if (element is ICollection)
                {
                    AddRelationship((List<object>)element, id, relation);
                }
                var valuetypeNode = element.GetType().ToString();
                var statement = AddRelationshipById(id, "Value", new KeyValuePair<string, object>(relation, element));
                var res = GetResultFromCypherRequest("node", statement);
                var typevalueNode = GetNode("Type", valuetypeNode);
                AddRelationshipByValue(valuetypeNode, "Type", element.ToString());
            }
        }

        public void CreateEntity(dynamic entity = null)
        {
            try
            {
                using (var session = _db.Session())
                {
                    var res = InsertEntity();

                    long id = GetResultFromCypherRequest("entity", res).Id;
                    var dico = new Dictionary<string, List<object>>();

                    if (entity != null)
                    {
                        PropertyDescriptorCollection properties = TypeDescriptor.GetProperties(entity);
                        foreach (PropertyDescriptor prop in properties)
                        {
                            string name = prop.Name;
                            var value = prop.GetValue(entity);
                            if (value is ICollection)
                            {
                                dico.Add(name, new List<object>(value));
                            }
                            else
                            {
                                var valueList = new List<object>();
                                valueList.Add(value);
                                dico.Add(name, valueList);
                            }
                        }

                        foreach (var item in dico)
                        {
                            AddCollection(item.Value);
                            AddRelationship(item.Value, id, item.Key);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                //
            }

        }

        private IStatementResult InsertEntity()
        {
            return _db.Session().Run(new Statement("CREATE (entity:Entity {createdDate:{tick}}) RETURN entity", new Dictionary<string, object>
                    {
                        {"tick", DateTime.Now.Ticks}
                    }));
        }

        // On peut enlever les retours des methodes d'ajouts de relation puisque la cypher request ne retourne rien
        private IStatementResult AddRelationshipById(long id, string nodeType, KeyValuePair<string, object> item)
        {
            return _db.Session().Run(new Statement("MATCH (entity) WHERE ID(entity)={entityId} MATCH (node:" + nodeType + " {name:{value}}) CREATE (entity)-[:"+ item.Key.ToUpper() + "]->(node)", new Dictionary<string, object>
                                    {
                                        { "entityId", id},
                                        {"nodeType", nodeType},
                                        { "value", item.Value}
                                    }));
        }

        private IStatementResult AddRelationshipByValue(string valuetypeNode, string nodeType, string value)
        {
            return _db.Session().Run(new Statement("Match (valuenode:Value {name:{value}}) MATCH(node:" + nodeType + " {name:{typename}} CREATE (valuenode)-[:VALUETYPE]->(node) ", new Dictionary<string, object>
                                        {
                                            { "value", value},
                                            {"nodeType", nodeType},
                                            { "typename", valuetypeNode},
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

        private dynamic GetNode(string nodeType, object value)
        {
            var result = _db.Session().Run(new Statement("MATCH (node:" + nodeType + " {name:{value}}) RETURN node", new Dictionary<string, object>
                {
                    {"value", value},
                    {"type", nodeType}
                }));

            var node = GetResultFromCypherRequest("node", result);


            if (node == null)
            {
                var creatResult = _db.Session().Run(new Statement("CREATE (node:" + nodeType + " {name:{value}}) RETURN node", new Dictionary<string, object>
                {
                    {"type", nodeType},
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
