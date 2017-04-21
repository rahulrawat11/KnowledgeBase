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
            if (exp.NodeType == ExpressionType.Call)
            {
                CreatePath(((MethodCallExpression)exp).Object, depth + 1);
            }
            if (exp.NodeType == ExpressionType.Convert)
            {
                CreatePath(((UnaryExpression)exp).Operand, depth + 1);
            }
        }

        public CypherQueryBuilder Where<T>(Expression<Func<T, bool>> lambda)
        {
            _lastChainName = "";
            dynamic value = null;
            var ope = "";
            if (lambda.Body.NodeType == ExpressionType.Call)
            {
                var methodExpression = (MethodCallExpression)lambda.Body;
                CreatePath(methodExpression.Object);
                switch (methodExpression.Method.Name)
                {
                    //Like
                    case "Contains":
                        ope = "=~";
                        value = ".*" + ((ConstantExpression)methodExpression.Arguments.First()).Value.ToString() + ".*";
                        break;
                    case "StartWith":
                        ope = "=~";
                        value = ((ConstantExpression)methodExpression.Arguments.First()).Value.ToString() + ".*";
                        break;
                    case "EndWidth":
                        ope = "=~";
                        value = ".*" + ((ConstantExpression)methodExpression.Arguments.First()).Value.ToString();
                        break;
                }
            }
            else
            {
                var binaryExpression = (BinaryExpression)lambda.Body;
                CreatePath(binaryExpression.Left);
                value = binaryExpression.Right;
                switch (binaryExpression.NodeType)
                {
                    case ExpressionType.Equal:
                        ope = "=";
                        break;
                    case ExpressionType.LessThan:
                        ope = "<";
                        break;
                    case ExpressionType.LessThanOrEqual:
                        ope = "<=";
                        break;
                    case ExpressionType.GreaterThan:
                        ope = "<=";
                        break;
                    case ExpressionType.GreaterThanOrEqual:
                        ope = "<=";
                        break;
                }
            }

            _query.Append($" WHERE {_lastChainName}.name {ope} {{pValue{_clauseNumber}}}");
            _clausesParameters.Add($"pValue{_clauseNumber}", value);
            ++_clauseNumber;
            return this;
        }
        public CypherQueryBuilder Where<T>(Expression<Func<T, IEntity>> lambda, long nodeId)
        {
            _lastChainName = "";
            CreatePath(lambda.Body);

            _query.Append($" WHERE ID({_lastChainName}).name = {{pValue{_clauseNumber}}}");
            _clausesParameters.Add($"pValue{_clauseNumber}", nodeId);
            ++_clauseNumber;
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
