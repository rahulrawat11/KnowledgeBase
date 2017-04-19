using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace HolyNoodle.KnowledgeBase.Test
{
    public class Entity
    {
        //public int Id { get; set; }
        public int Age { get; set; }
        public float Weight { get; set; }
        public List<string> list { get; set; }
    }
    class Program
    {
        static void Main(string[] args)
        {
            var entity = new Entity();
            entity.Age = 4;
            //entity.Name = "lal";
            entity.Weight = 60.2f;
            entity.list = new List<string>();
            entity.list.Add("tutu");
            entity.list.Add("titi");

            var em = new ManageEntity();
            em.CreateEntity(entity);
        }
    }
}

