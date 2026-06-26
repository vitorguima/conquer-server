// TODO-M1: NHibernate removed - using NHibernate.Criterion;
// TODO-M1: NHibernate removed - using NHibernate.SqlCommand;
using Redux.Database.Domain;
using System;

namespace Redux.Database.Repositories
{
    public class ShopItemRepository : Repository<uint, DbShopItem>
    {
        public DbShopItem GetShopItem(ushort _shopID, uint _itemID)
        {
            throw new NotImplementedException("TODO-M1: NHibernate removed");
        }
    }
}

