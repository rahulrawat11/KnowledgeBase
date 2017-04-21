using HolyNoodle.Utility;
using Neo4j.Driver.V1;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Dynamic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

namespace HolyNoodle.KnowledgeBase
{
    public class CypherQueryBuilder : IDisposable
    {
        private StringBuilder _query;
        private int _clauseNumber;
        private Dictionary<string, object> _clausesParameters;
        private ISession _session;
        private string _lastChainName = "";
        public Configuration Configuration { get; internal set; }

        public CypherQueryBuilder(ISession session, Configuration configuration)
        {
            _session = session;
            _query = new StringBuilder();
            _clausesParameters = new Dictionary<string, object>();
            Configuration = configuration;
        }

        private void CreateClause(object value)
        {
            _query.Append($" MATCH (value{_clauseNumber}:");
            if (ReflexionHelper.IsValueType(value.GetType()))
            {
                _query.Append($"{Configuration.ValueTypeName} {{name:{{pValue{_clauseNumber}}}}})");
                _clausesParameters.Add($"pValue{_clauseNumber}", value);
            }
            else
            {
                _query.Append($"{Configuration.EntityTypeName}) WHERE ID(value{_clauseNumber}) = {{pValue{_clauseNumber}}}");
                _clausesParameters.Add($"pValue{_clauseNumber}", ((IEntity)value).Node.Id);
            }

            //Clause number is here to chain clauses and have unique variable and parameter name
            ++_clauseNumber;
        }
        private void CreatePath(Expression exp, int depth = 0)
        {
            if (exp.NodeType == ExpressionType.MemberAccess)
            {
                var memberExpression = ((MemberExpression)exp);
                CreatePath(memberExpression.Expression, depth + 1);
                if (_lastChainName == string.Empty) _lastChainName = "entity";
                _query.Append($" MATCH ({_lastChainName})-[:{memberExpression.Member.Name}]->(children{depth}_{_clauseNumber})");
                _lastChainName = $"children{depth}_{_clauseNumber}";
            }
            if(exp.NodeType == ExpressionType.Call)
            {
                CreatePath(((MethodCallExpression)exp).Object, depth + 1);
            }
            if (exp.NodeType == ExpressionType.Convert)
            {
                CreatePath(((UnaryExpression)exp).Operand, depth + 1);
            }
        }

        public CypherQueryBuilder LowerThan<T>(Expression<Func<T, object>> lambda, object value)
        {
            _lastChainName = "";
            CreatePath(lambda.Body);
            _query.Append($"WHERE {_lastChainName}.name < {{pValue}}");

            return this;
        }

        /// <summary>
        /// Chaining the clauses needs to stay simple.
        /// Here for each clause we seek for the value node.
        /// Then we link it to the searched entity.
        /// At the execution time, the expected behavior is the following :
        /// Match all the value nodes to filter on
        /// then look for entities that are linked to them
        /// </summary>
        /// <param name="propertyName">Name of the property you want to filter on</param>
        /// <param name="value">value you are seeking for this property</param>
        /// <returns></returns>
        public CypherQueryBuilder Equals<T>(Expression<Func<T, object>> lambda, object value)
        {
            _lastChainName = string.Empty;
            CreatePath(lambda.Body);

            //Chaining the clauses needs to stay simple.
            //Here for each clause we seek for the value node
            //then we link it to the searched entity
            //At the execution time, the expected behavior is the following
            //Match all the value nodes to filter on
            //then look for entities that are linked to them

            var valueType = value.GetType();
            //If value is value type
            if (ReflexionHelper.IsValueType(valueType))
            {
                _query.Append($"WHERE {_lastChainName}.name = {{pValue{_clauseNumber}}}");
                _clausesParameters.Add($"pValue{_clauseNumber}", value);
            }
            else if (value is IEntity)
            {
                _query.Append($"WHERE ID({_lastChainName}) = {{pValue{_clauseNumber}}}");
                _clausesParameters.Add($"pValue{_clauseNumber}", ((IEntity)value).Node.Id);
            }

            //return this for chaining purpose
            return this;
        }

        public CypherQueryBuilder ById(long nodeId)
        {
            _query = new StringBuilder("MATCH (entity) WHERE ID(entity) = {pId}");
            if (!_clausesParameters.ContainsKey("pId"))
            {
                _clausesParameters.Add("pId", nodeId);
            }
            return this;
        }

        public override string ToString()
        {
            return _query.ToString();
        }

        public void ExecuteNonQuery()
        {
            _session.Run(_query.ToString(), _clausesParameters);
        }

        public T ExecuteScalar<T>()
        {
            return (T)(object)null;
        }

        private IStatementResult ExecuteQuery()
        {
            //We append the expected return information in the right format
            //Here we match all the nodes related to an entity
            return _session.Run(_query.ToString() + "MATCH (entity)-[rel]->(value) RETURN entity, rel, value", _clausesParameters);
        }

        /// <summary>
        /// Execute the query on the session.
        /// Returns a list of the entities found
        /// </summary>
        /// <returns>List of the dynamic entities</returns>
        public IEnumerable<dynamic> Execute()
        {
            var result = ExecuteQuery();
            //Declaring a dictionnary because we get from neo4j a table of all entities with all the relationship
            //So, in order to not have to manage the order of the result, I store the instance of the entity in this object
            //retrieved by the entity node id
            var entities = new Dictionary<long, dynamic>();
            foreach (var r in result)
            {
                //Get the node and create the object in the dictionary if not already exists
                var entityNode = r.Values["entity"] as INode;
                if (!entities.ContainsKey(entityNode.Id))
                {
                    entities.Add(entityNode.Id, new ExpandoObject());
                    entities[entityNode.Id].Node = entityNode;
                }

                //Get the instance
                var instance = entities[entityNode.Id] as IDictionary<string, object>;
                //Get the relationship
                var relation = r.Values["rel"] as IRelationship;
                //Get the value node
                var valueNode = r.Values["value"] as INode;
                //Set the property on the instance of the object
                if (!instance.ContainsKey(relation.Type))
                {
                    if (valueNode.Labels.Contains("name"))
                    {
                        instance.Add(relation.Type, valueNode.Properties["name"]);
                    }
                    else
                    {
                        var value = new EntityNode();
                        value.Node = valueNode;
                        instance.Add(relation.Type, value);
                    }
                }
                else
                {
                    var targetedType = instance[relation.Type].GetType();
                    if (!ReflexionHelper.IsEnumarable(targetedType))
                    {
                        var constructedListType = typeof(List<>).MakeGenericType(targetedType);
                        var list = Activator.CreateInstance(constructedListType) as IList;
                        list.Add(instance[relation.Type]);
                        instance[relation.Type] = list;
                    }

                    if (valueNode.Labels.Contains("name"))
                    {
                        (instance[relation.Type] as IList).Add(valueNode.Properties["name"]);
                    }
                    else
                    {
                        var value = new EntityNode();
                        value.Node = valueNode;
                        (instance[relation.Type] as IList).Add(value);
                    }
                }
            }
            return entities.Values;
        }

        /// <summary>
        /// Execute the query on the session.
        /// Returns a list of the entities found
        /// </summary>
        /// <typeparam name="T">Type of the entity you want to get</typeparam>
        /// <returns>List of the strongly typed in T entities</returns>
        public IEnumerable<T> Execute<T>()
        {
            var result = ExecuteQuery();
            //Declaring a dictionnary because we get from neo4j a table of all entities with all the relationship
            //So, in order to not have to manage the order of the result, I store the instance of the entity in this object
            //retrieved by the entity node id
            var entities = new Dictionary<long, T>();
            foreach (var r in result)
            {
                //Get the node and create the object in the dictionary if not already exists
                var entityNode = r.Values["entity"] as INode;
                if (!entities.ContainsKey(entityNode.Id))
                {
                    var tempInstance = (T)Activator.CreateInstance(typeof(T));
                    entities.Add(entityNode.Id, tempInstance);
                    if (tempInstance is IEntity)
                    {
                        ((IEntity)tempInstance).Node = entityNode;
                    }
                }

                //Get the instance
                var instance = entities[entityNode.Id];
                //Get the relationship
                var relation = r.Values["rel"] as IRelationship;
                //Get the property related to the property
                var property = typeof(T).GetProperty(relation.Type);
                //If the property exists in the expected result object
                if (property != null)
                {
                    //Get the value node
                    var valueNode = r.Values["value"] as INode;
                    if (ReflexionHelper.IsValueType(property.PropertyType))
                    {
                        //Set the property on the instance of the object
                        //ChangeType is meant to prevent flaw in casting system  (Int64 to Int32 casts for example)
                        property.SetValue(instance, Convert.ChangeType(valueNode.Properties["name"], property.PropertyType));
                    }
                    else
                    {
                        if (ReflexionHelper.IsEnumarable(property.PropertyType))
                        {
                            var genericType = ReflexionHelper.GetGenericTypeDefintion(property.PropertyType);
                            if (property.GetValue(instance) == null)
                            {
                                var constructedListType = typeof(List<>).MakeGenericType(genericType);
                                property.SetValue(instance, Activator.CreateInstance(constructedListType));
                            }

                            if (ReflexionHelper.IsValueType(genericType))
                            {
                                (property.GetValue(instance) as IList).Add(valueNode.Properties["name"]);
                            }
                            else
                            {
                                var subentity = (IEntity)Activator.CreateInstance(genericType);
                                subentity.Node = valueNode;
                                (property.GetValue(instance) as IList).Add(subentity);
                            }
                        }
                        else
                        {
                            var subentity = (IEntity)Activator.CreateInstance(property.PropertyType);
                            subentity.Node = valueNode;
                            property.SetValue(instance, subentity);
                        }
                    }
                }
            }
            return entities.Values;
        }

        public void Dispose()
        {
            try
            {
                _session.Dispose();
            }
            finally
            {
                _query = null;
                _clausesParameters = null;
                _clauseNumber = 0;
            }
        }
    }
}
