using HolyNoodle.Utility.Dal;
using HolyNoodle.Utility.DAL;
using System;
using System.Collections.Generic;
using System.Configuration;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

namespace HolyNoodle.KnowledgeBase
{
    public class KnowledgeDb : IDb
    {
        private static string ConnectionString, ConnectionLogin, ConnectionPassword;
        private static bool UseCache;
        private static ICacheProvider CacheProvider;

        static KnowledgeDb()
        {
            ConnectionString = "";
            if (ConfigurationManager.AppSettings["holynoodle:DefaultConnectionString"] != null)
            {
                ConnectionString = ConfigurationManager.ConnectionStrings[ConfigurationManager.AppSettings["holynoodle:DefaultConnectionString"]].ConnectionString;
            }
            if (ConfigurationManager.AppSettings["holynoodle:KnowledgeLogin"] != null)
            {
                ConnectionLogin = ConfigurationManager.AppSettings["holynoodle:KnowledgeLogin"];
            }
            if (ConfigurationManager.AppSettings["holynoodle:KnowledgePassword"] != null)
            {
                ConnectionPassword = ConfigurationManager.AppSettings["holynoodle:KnowledgePassword"];
            }

            UseCache = false;
            if (ConfigurationManager.AppSettings["holynoodle:KnowledgeCacheProvider"] != null)
            {
                UseCache = true;
                var typeName = ConfigurationManager.AppSettings["holynoodle:KnowledgeCacheProvider"];
                var type = Assembly.GetExecutingAssembly().GetTypes().FirstOrDefault(t => t.FullName == typeName);
                if (type != null)
                {
                    try
                    {
                        CacheProvider = (ICacheProvider)Activator.CreateInstance(type);
                    }
                    catch
                    {
                        throw;
                    }
                }
                else
                {
                    throw new Exception("Type '" + ConfigurationManager.AppSettings["holynoodle:KnowledgeCacheProvider"] + "' not found.");
                }
            }
        }

        private string _connectionString, _connectionLogin, _connectionPassword;
        private KnowledgeBase _db;

        public KnowledgeDb()
        {
            _connectionString = ConnectionString;
            _connectionLogin = ConnectionLogin;
            _connectionPassword = ConnectionPassword;
        }

        public KnowledgeDb(string cs, string login, string password)
        {
            _connectionString = cs;
        }

        #region Private Methods
        private KnowledgeBase Connect(int deph = 0)
        {
            _db = new KnowledgeBase(_connectionString, _connectionLogin, _connectionPassword);
            _db.InitDatabase().Wait();
            return _db;
        }
        private KnowledgeEntity CreateEntity(IDbProcedure procedure)
        {
            var entity = new KnowledgeEntity();
            foreach (var property in procedure.GetType().GetProperties())
            {
                if (property.Name == "EntityId")
                {
                    entity.Id = property.GetValue(procedure) as string;
                    continue;
                }

                var dalAttribute = property.GetCustomAttribute(typeof(DalAttribute)) as DalAttribute;
                if (dalAttribute == null) continue;
                object value = property.GetValue(procedure);
                if (dalAttribute.Crypter != null)
                {
                    var instance = Activator.CreateInstance(dalAttribute.Crypter) as ICrypter;
                    value = instance.Crypt(value);
                }
                entity.AddRelationship(dalAttribute.DBName, value);
            }
            return entity;
        }
        private T Load<T>(KnowledgeEntity entity) where T : IDalObject
        {
            var type = typeof(T);
            var result = Activator.CreateInstance(type) as T;
            var objectProperties = type.GetProperties();

            foreach (var objectProperty in objectProperties)
            {
                var dalAttribute = objectProperty.GetCustomAttribute(typeof(DalAttribute)) as DalAttribute;
                if (dalAttribute == null) continue;

                if (dalAttribute.DBName == "EntityId")
                {
                    objectProperty.SetValue(result, entity.Id);
                    continue;
                }
                if (entity.Properties.ContainsKey(dalAttribute.DBName))
                {
                    object value = entity.Properties[dalAttribute.DBName];
                    if (dalAttribute.Crypter != null)
                    {
                        var instance = Activator.CreateInstance(dalAttribute.Crypter) as ICrypter;
                        value = instance.Decrypt(value);
                    }
                    objectProperty.SetValue(result, value);
                }
            }

            return result;
        }
        #endregion

        #region IDb Methods
        public List<T> Execute<T>(IDbProcedure procedure) where T : IDalObject
        {
            var entity = CreateEntity(procedure);
            var lookupTask = _db.Get(entity);
            lookupTask.Wait();
            return lookupTask.Result.Select(r => Load<T>(r)).ToList();
        }

        public void ExecuteNonQuery(IDbProcedure procedure)
        {
            var entity = CreateEntity(procedure);
            switch(procedure.Procedure)
            {
                case "DELETE":
                    _db.DeleteEntity(entity).Wait();
                    break;
                default:
                    _db.UpdateEntity(entity).Wait();
                    break;
            }
        }

        public object ExecuteScalar(IDbProcedure procedure)
        {
            throw new NotImplementedException("Need to be able to define how to perform the calculation in Cypher (wich i know but I can't spend time on it). Use Execute and calculate yourself for the moment. Sorry");
        }

        public Task<bool> RefreshAllBindings(List<IDalObject> objects)
        {
            throw new NotImplementedException("On a graph database structured as i structured this one, dependcies are everything and in every way possible. So loop are a problem in performance and accuracy of the result... Working on defining a deph (depeding on the procedure) in the query to prevent loop");
        }

        public Task<bool> RefreshBinding(IDalObject o, string propertyName)
        {
            throw new NotImplementedException("On a graph database structured as i structured this one, dependcies are everything and in every way possible. So loop are a problem in performance and accuracy of the result... Working on defining a deph (depeding on the procedure) in the query to prevent loop");
        }

        public Task<bool> RefreshBindings(IDalObject o)
        {
            throw new NotImplementedException("On a graph database structured as i structured this one, dependcies are everything and in every way possible. So loop are a problem in performance and accuracy of the result... Working on defining a deph (depeding on the procedure) in the query to prevent loop");
        }

        public Task<bool> RefreshBindings(List<IDalObject> objects, string name)
        {
            throw new NotImplementedException("On a graph database structured as i structured this one, dependcies are everything and in every way possible. So loop are a problem in performance and accuracy of the result... Working on defining a deph (depeding on the procedure) in the query to prevent loop");
        }
        #endregion

        public void ResetCache(IDbProcedure procedure)
        {
            if (CacheProvider != null)
            {
                CacheProvider.Cache(procedure, null);
            }
        }
    }
}
