// TODO-M1: NHibernate removed - Repository stubbed out; Dapper replacements in separate files
using System.Collections.Generic;
using System.Linq;
using System;
namespace Redux.Database.Repositories
{
    public class Repository<TKey, TValue> : IRepository<TKey, TValue>
        where TValue : class
    {
        public TValue GetById(TKey id)
        {
            throw new NotImplementedException("TODO-M1: NHibernate removed");
        }

        public void Add(TValue obj)
        {
            throw new NotImplementedException("TODO-M1: NHibernate removed");
        }

        public void Update(TValue obj)
        {
            throw new NotImplementedException("TODO-M1: NHibernate removed");
        }

        public void Remove(TValue obj)
        {
            throw new NotImplementedException("TODO-M1: NHibernate removed");
        }

        public void AddOrUpdate(TValue obj)
        {
            throw new NotImplementedException("TODO-M1: NHibernate removed");
        }

        public void Add(ICollection<TValue> collection)
        {
            foreach (var obj in collection) Add(obj);
        }

        public void Update(ICollection<TValue> collection)
        {
            foreach (var obj in collection) Update(obj);
        }

        public void Remove(ICollection<TValue> collection)
        {
            foreach (var obj in collection) Remove(obj);
        }

        public void AddOrUpdate(ICollection<TValue> collection)
        {
            foreach (var obj in collection) AddOrUpdate(obj);
        }

        public void Add(IQueryable<TValue> queryable)
        {
            foreach (var obj in queryable) Add(obj);
        }

        public void Update(IQueryable<TValue> queryable)
        {
            foreach (var obj in queryable) Update(obj);
        }

        public void Remove(IQueryable<TValue> queryable)
        {
            foreach (var obj in queryable) Remove(obj);
        }

        public void AddOrUpdate(IQueryable<TValue> queryable)
        {
            foreach (var obj in queryable) AddOrUpdate(obj);
        }
    }
}

