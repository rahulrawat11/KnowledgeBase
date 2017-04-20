using HolyNoodle.Utility;
using Neo4j.Driver.V1;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace HolyNoodle.KnowledgeBase
{
    public class CypherQueryBuilder
    {
        private StringBuilder _query;
        private int _clauseNumber;
        private Dictionary<string, object> _clausesParameters;
        private ISession _session;
        public Configuration Configuration { get; internal set; }

        public CypherQueryBuilder(ISession session, Configuration configuration)
        {
            _session = session;
            _query = new StringBuilder();
            _clausesParameters = new Dictionary<string, object>();
            Configuration = configuration;
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
        public CypherQueryBuilder Clause(string propertyName, object value)
        {
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
                //Match the node then add the value to parameters
                _query.Append($" MATCH (value{_clauseNumber}:{Configuration.ValueTypeName} {{name:{{pValue{_clauseNumber}}}}})");
                _clausesParameters.Add($"pValue{_clauseNumber}", value);
            }
            else
            {
                //It's a non value type !
                //If it's an enumerable
                if (ReflexionHelper.IsEnumarable(valueType))
                {
                    var listGenericType = ReflexionHelper.GetGenericTypeDefintion(valueType);
                    //If list geniric type is value type
                    if (ReflexionHelper.IsValueType(listGenericType))
                    {
                        //Match the node then add the value to parameters
                        _query.Append($" MATCH (value{_clauseNumber}:{Configuration.ValueTypeName} {{name:{{pValue{_clauseNumber}}}}})");
                        _clausesParameters.Add($"pValue{_clauseNumber}", value);
                    }
                    else
                    {
                        //Manage list of subentities
                    }
                }
                else
                {
                    //TODO manage subentity
                }
            }
            //Convergence to the central entity
            _query.Append($" MATCH (entity:{Configuration.EntityTypeName})-[rel{_clauseNumber}:{propertyName}]->(value{_clauseNumber})");

            //Clause number is here to chain clauses and have unique variable and parameter name
            ++_clauseNumber;

            //return this for chaining purpose
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

        //TODO Same for no type
        /// <summary>
        /// Execute the query on the session.
        /// Returns a list of the entities found
        /// </summary>
        /// <typeparam name="T">Type of the entity you want to get</typeparam>
        /// <returns>List of the strongly typed in T entities</returns>
        public IEnumerable<T> Execute<T>()
        {
            //We append the expected return information in the right format
            //Here we match all the nodes related to an entity
            _query.Append("MATCH (entity)-[rel]->(value) RETURN entity, rel, value");
            var result = _session.Run(_query.ToString(), _clausesParameters);
            //Declaring a dictionnary because we get from neo4j a table of all entities with all the relationship
            //So, in order to not have to manage the order of the result, I store the instance of the entity in this object
            //retrieved by the entity node id
            var entities = new Dictionary<long, T>();
            foreach(var r in result)
            {
                //Get the node and create the object in the dictionary if not already exists
                var entity = r.Values["entity"] as INode;
                if (!entities.ContainsKey(entity.Id))
                {
                    entities.Add(entity.Id, (T)Activator.CreateInstance(typeof(T)));
                }

                //Get the instance
                var instance = entities[entity.Id];
                //Get the relationship
                var relation = r.Values["rel"] as IRelationship;
                //Get the property related to the property
                var property = typeof(T).GetProperty(relation.Type);
                //If the property exists in the expected result object
                if(property != null)
                {
                    //Get the value node
                    var valueNode = r.Values["value"] as INode;
                    //Set the property on the instance of the object
                    //ChangeType is meant to prevent flaw in casting system  (Int64 to Int32 casts for example)
                    property.SetValue(instance, Convert.ChangeType(valueNode.Properties["name"], property.PropertyType));
                }
            }
            return entities.Values;
        }

    }
}
