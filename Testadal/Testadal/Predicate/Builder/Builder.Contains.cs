﻿using System;
using System.Linq.Expressions;
using System.Reflection;

namespace Testadal.Predicate
{
    public static partial class PredicateBuilder
    {
        public static IFieldPredicate Contains<T>(Expression<Func<T, string>> expression, string value) where T : class
        {
            PropertyInfo propertyInfo = ReflectionHelper.GetProperty(expression) as PropertyInfo;
            return Field<T>(propertyInfo.Name, Operator.Contains, value);
        }
    }
}
