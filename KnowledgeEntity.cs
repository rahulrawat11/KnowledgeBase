using HolyNoodle.Utility.DAL;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace HolyNoodle.KnowledgeBase
{
    public class KnowledgeEntity
    {
        public KnowledgeEntity()
        {
            Properties = new Dictionary<string, List<KnowledgeEntityRelationship>>();
        }

        public string Id { get; internal set; }
        public IDictionary<string, List<KnowledgeEntityRelationship>> Properties { get; set; }

        public T MapToObject<T>() where T : IDalObject
        {
            var type = typeof(T);
            var result = Activator.CreateInstance(type) as T;

            if (result == null) throw new Exception("Type " + type.FullName + " could not be instanciate");

            foreach (var property in Properties)
            {
                var propertyInfo = type.GetProperty(property.Key);
                if (propertyInfo != null)
                {
                    propertyInfo.SetValue(result, GetRelationship(property.Key));
                }
            }
            return result;
        }

        public void MapFromObject<T>(T obj) where T : IDalObject
        {
            var type = typeof(T);
            foreach (var propertyInfo in type.GetProperties())
            {
                var attribute = propertyInfo.GetCustomAttributes<DalAttribute>().FirstOrDefault();
                if (attribute != null)
                {
                    var values = new List<KnowledgeEntityRelationship>();
                    AddRelationship(attribute.DBName != null ? attribute.DBName : propertyInfo.Name, propertyInfo.GetValue(obj));
                }
            }
        }

        public void AddRelationship(string key, object value = null)
        {
            if (string.IsNullOrEmpty(key) || value == null) return;

            key = KnowledgeBase.CypherFormat(key);
            if (!Properties.ContainsKey(key))
            {
                Properties.Add(key, new List<KnowledgeEntityRelationship>());
            }
            var first = Properties[key].FirstOrDefault(r => r.Value.Equals(value));
            if (first != null)
            {
                //    first.Weight++;
                //    first.WeightChanged = true;
            }
            else
            {
                Properties[key].Add(new KnowledgeEntityRelationship { Value = value });
            }
        }

        public object GetRelationship(string key)
        {
            key = KnowledgeBase.CypherFormat(key);
            if (!Properties.ContainsKey(key))
            {
                return string.Empty;
            }
            return Properties[key][0].Value;
        }

        public List<KnowledgeEntityRelationship> GetRelationships(string key)
        {
            key = KnowledgeBase.CypherFormat(key);
            if (!Properties.ContainsKey(key))
            {
                return new List<KnowledgeEntityRelationship>();
            }
            return Properties[key];
        }

        private bool IsGenericList(Type type)
        {
            if (type == null)
            {
                throw new ArgumentNullException("type");
            }
            foreach (Type @interface in type.GetInterfaces())
            {
                if (@interface.IsGenericType)
                {
                    if (@interface.GetGenericTypeDefinition() == typeof(ICollection<>))
                    {
                        // if needed, you can also return the type used as generic argument
                        return true;
                    }
                }
            }
            return false;
        }
    }

    public class KnowledgeEntityRelationship
    {
        public bool IsFromDatabase { get; internal set; }
       
        public object Value { get; set; }
    }
}
