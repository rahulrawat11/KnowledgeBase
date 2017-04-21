using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Neo4j.Driver.V1;

namespace HolyNoodle.KnowledgeBase
{
    public class EntityNode : IEntity
    {
        public INode Node { get; set; }
    }
}
