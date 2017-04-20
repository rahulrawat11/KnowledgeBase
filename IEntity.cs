using Neo4j.Driver.V1;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

namespace HolyNoodle.KnowledgeBase
{
    public interface IEntity
    {
        [SkipProperty]
        long EntityId { get; set; }
        [SkipProperty]
        INode Node { get; set; }
    }

    public class SkipPropertyAttribute : Attribute
    {
    }

    public static class TypeExtensions
    {
        public static PropertyInfo[] GetFilteredProperties(this Type type)
        {
            return type.GetProperties().Where(pi => pi.GetCustomAttributes(typeof(SkipPropertyAttribute), true).Length == 0).ToArray();
        }
    }
}
