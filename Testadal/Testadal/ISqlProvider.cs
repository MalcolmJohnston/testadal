﻿using System.Collections.Generic;

using Testadal.Predicate;

namespace Testadal
{
    public interface ISqlProvider
    {
        string GetSelectCountSql<T>(IEnumerable<IPredicate> whereConditions) where T : class;

        string GetSelectAllSql<T>() where T : class;

        string GetSelectByIdSql<T>() where T : class;

        string GetSelectWhereSql<T>(IEnumerable<IPredicate> whereConditions) where T : class;

        string GetSelectWhereSql<T>(IEnumerable<IPredicate> whereConditions, object sortOrders, int firstRow, int lastRow) where T : class;

        string GetInsertSql<T>() where T : class;

        string GetUpdateSql<T>(object updateProperties) where T : class;

        string GetDeleteByIdSql<T>() where T : class;

        string GetDeleteWhereSql<T>(IEnumerable<IPredicate> whereConditions) where T : class;

        string GetSelectNextIdSql<T>() where T : class;
    }
}
