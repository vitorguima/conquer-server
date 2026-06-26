using Redux.Database.Repositories;

namespace Redux.Database
{
    public class ServerDatabase
    {
        public static ConquerDataContext Context { get; private set; }

        public static bool InitializeSql()
        {
            Context = new ConquerDataContext();
            // TODO-M1: NHibernate removed - NHibernateHelper.BuildSessionFactory() removed
            // TODO-M1: NHibernate removed - ResetLoginTokens/ResetOnlineCharacters not called (Dapper replacements in later tasks)
            return true;
        }
    }
}
