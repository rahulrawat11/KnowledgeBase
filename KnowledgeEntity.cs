using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

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

        public T MapToObject<T>(KnowledgeEntity entity, T obj)
        {
            var type = typeof(T);
            foreach(var property in entity.Properties)
            {
                
            }
            return obj;
        }

        public void MapFromObject<T>(T obj)
        {

        }

        public void AddRelationship(string key, object value)
        {
            key = KnowledgeBase.CypherFormat(key);
            if (!Properties.ContainsKey(key))
            {
                Properties.Add(key, new List<KnowledgeEntityRelationship>());
            }
            Properties[key].Add(new KnowledgeEntityRelationship { Value = value });
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
    }

    public class KnowledgeEntityRelationship
    {
        public bool IsFromDatabase { get; internal set; }
        public long Start { get; set; }
        public long End { get; set; }
        public long Weight { get; set; }
        public object Value { get; set; }
    }
}
