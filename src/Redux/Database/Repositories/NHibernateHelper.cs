// TODO-M1: NHibernate removed - NHibernateHelper stubbed out
using System;

namespace Redux.Database.Repositories
{
    public class NHibernateHelper
    {
        public static bool BuildSessionFactory()
        {
            // TODO-M1: NHibernate removed - no-op stub
            return false;
        }

        public static object OpenSession()
        {
            throw new NotImplementedException("TODO-M1: NHibernate removed - use Dapper repositories instead");
        }
    }
}
