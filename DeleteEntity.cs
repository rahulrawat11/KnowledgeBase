using HolyNoodle.Utility.DAL;
using System;
using System.Collections.Generic;
using System.Dynamic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace HolyNoodle.KnowledgeBase
{
    public class DeleteEntity : IDbProcedure
    {
        public string Procedure
        {
            get
            {
                return "DELETE";
            }
        }

        [Dal("EntityId")]
        public string EntityId { get; set; }
    }

    public class Person
    {
        public string id { get; set; }
        public string Name { get; set; }
        public int Age { get; set; }
        public ICollection<Person> Children { get; set; }
        public Person Partner { get; set; }
    }
}
