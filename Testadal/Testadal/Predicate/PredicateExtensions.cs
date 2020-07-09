﻿using Humanizer;
using System;
using System.Collections.Generic;
using System.Linq;
using Testadal.Model;
using Testadal.SqlBuilder;

namespace Testadal.Predicate
{
    public static class PredicateExtensions
    {
        public static string GetColumnName<T>(this ISqlBuilder sqlBuilder, string propertyName) where T : class
        {
            ClassMap map = ClassMapper.GetClassMap<T>();
            if (map == null)
            {
                throw new NullReferenceException($"Map was not found for {typeof(T)}");
            }

            PropertyMap propertyMap = map.AllProperties[propertyName];
            if (propertyMap == null)
            {
                throw new NullReferenceException($"{propertyName} was not found for {typeof(T)}");
            }

            return sqlBuilder.Encapsulate(propertyMap.ColumnName);
        }

        public static IDictionary<string, object> GetParameters(this IList<IPredicate> predicates)
        {
            int parameterIndex = 0;
            Dictionary<string, object> parameters = new Dictionary<string, object>();
            
            foreach(IPredicate predicate in predicates)
            {
                var kvps = predicate.GetParameters(parameterIndex, out int parameterCount).ToList();
                kvps.ForEach(x => parameters.Add(x.Key, x.Value));

                parameterIndex += parameterCount;
            }

            return parameters;
        }
    }
}
