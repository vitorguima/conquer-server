// TODO-M1: NHibernate removed - using NHibernate.Criterion;
// TODO-M1: NHibernate removed - using NHibernate.SqlCommand;
using System;
using System.Collections.Generic;
using Redux.Database.Domain;

namespace Redux.Database.Repositories
{
    public class TaskRepository : Repository<uint, DbTask>
    {
        public IList<DbTask> GetTasksByPlayerUID(uint uit)
        {
            throw new NotImplementedException("TODO-M1: NHibernate removed");
        }
    }
}

