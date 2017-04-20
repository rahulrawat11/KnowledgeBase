using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Neo4j.Driver.V1;

namespace HolyNoodle.KnowledgeBase.Test
{
    public class Entity : IEntity
    {
        //public int Id { get; set; }
        public int Age { get; set; }
        public double Weight { get; set; }
        public string Name { get; set; }
        public List<string> list { get; set; }
        public List<Entity> Children { get; set; }
        public Entity InARelationship { get; set; }
        public INode Node { get; set; }
    }
    class Program
    {
        static void Main(string[] args)
        {
            var entity = new Entity();
            entity.Age = 30;
            entity.Name = "Kevin";
            entity.Weight = 60.2;
            entity.list = new List<string> { "tutu", "titi" };
            entity.Children = new List<Entity>
            {
                new Entity
                {
                    Age = 11,
                    Name = "Tomy",
                    Weight = 35.7
                },
                new Entity
                {
                    Age = 14,
                    Name = "Noa",
                    Weight = 40.8
                },
            };
            entity.InARelationship = new Entity
            {
                Name = "Claire",
                Age = 42,
                Weight = 25.4,
                //Reference the "Kevin" entity
                InARelationship = entity,
                //reference the "Kevin"'s children entity, because they got exactly the same children
                Children = entity.Children
            };

            var em = new EntityManager();
            em.CreateEntity(entity);

            var queryBuilder = em.GetQueryBuilder();
            var result = queryBuilder.Clause("Name", "Tomy").Execute<Entity>();
        }
    }
}