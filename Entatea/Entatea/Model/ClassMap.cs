﻿using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel;
using System.Dynamic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;

using Humanizer;
using Entatea.Predicate;

namespace Entatea.Model
{
    public class ClassMap
    {
        public ClassMap(Type type)
        {
            this.Name = type.Name;
            this.Schema = string.Empty;
            this.TableName = type.Name.Pluralize(false);

            // override schema and table name if table attribute present
            dynamic tableAttribute = ClassAttributeHelper.GetTableAttribute(type);
            if (tableAttribute != null)
            {
                Schema = tableAttribute.Schema;
                TableName = tableAttribute.Name;
            }

            // load the property mappings
            // null property mappings have the NotMapped attribute and so should just be ignored
            this.AllProperties = type.GetProperties(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)
                                     .Select(pi => PropertyMap.LoadPropertyMap(pi))
                                     .Where(x => x != null)
                                     .ToDictionary(kvp => kvp.PropertyName, kvp => kvp);

            // setup our helper collections of property maps
            this.SelectProperties = this.AllProperties.Values.ToList();

            // all key properties
            this.AllKeys = this.AllProperties.Values.Where(x => x.IsKey).ToList();

            // check whether we have more than one identity key
            if (this.AllKeys.Count(x => x.KeyType == KeyType.Identity) > 1)
            {
                throw new ArgumentException("Type can only define a single Identity key property.");
            }

            // if we have an identity key then we should have no other keys
            if (this.HasIdentityKey && this.AllKeys.Count() > 1)
            {
                throw new ArgumentException("Type can only define a single key when using an Identity key property.");
            }

            // check whether we have more than one sequential key
            if (this.AllKeys.Count(x => x.KeyType == KeyType.Sequential) > 1)
            {
                throw new ArgumentException("Type can only define a single Sequential key property.");
            }

            this.SequentialKey = this.AllKeys.SingleOrDefault(x => x.KeyType == KeyType.Sequential);

            // check whether we have a soft delete column
            if (this.AllProperties.Values.Where(x => x.IsSoftDelete).Count() > 1)
            {
                throw new ArgumentException("Type can only define a single soft delete column.");
            }

            // set the soft delete property
            this.SoftDeleteProperty = this.AllProperties.Values.SingleOrDefault(x => x.IsSoftDelete);

            // set the required properties
            this.RequiredProperties = this.AllProperties.Values.Where(x => x.IsRequired).ToList();

            // set the insertable properties
            this.InsertableProperties = this.AllProperties.Values.Where(x => x.KeyType != KeyType.Identity)
                                                                        .ToList();

            // set the updateable properties
            this.UpdateableProperties = this.AllProperties.Values.Where(x => x.KeyType == KeyType.NotAKey &&
                                                                                    x.IsEditable &&
                                                                                    !x.IsSoftDelete &&
                                                                                    !x.IsDateStamp)
                                                                        .ToList();

            // set the date stamp properties
            this.DateStampProperties = this.AllProperties.Values.Where(x => x.IsDateStamp).ToList();

            // set the default sort order
            this.DefaultSortOrder = this.AllKeys.Select(x => new { Key = x.ColumnName, Value = SortOrder.Ascending })
                                                .ToDictionary(x => x.Key, x => x.Value);
        }

        public string Name { get; private set; }

        public string Schema { get; private set; }

        public string TableName { get; private set; }


        public IDictionary<string, PropertyMap> AllProperties { get; private set; }

        public IList<PropertyMap> AllKeys { get; private set; }

        public PropertyMap SequentialKey { get; private set; }

        public bool HasSequentialKey
        {
            get { return this.SequentialKey != null; }
        }

        public IEnumerable<PropertyMap> AssignedKeys
        {
            get { return this.AllKeys.Where(x => x.KeyType == KeyType.Assigned); }
        }

        public bool HasAssignedKeys
        {
            get { return this.AssignedKeys.Count() > 0; }
        }

        public PropertyMap IdentityKey
        {
            get { return this.AllKeys.Where(x => x.KeyType == KeyType.Identity).SingleOrDefault(); }
        }

        public bool HasIdentityKey
        {
            get { return this.IdentityKey != null; }
        }

        public IList<PropertyMap> SelectProperties { get; private set; }

        public IList<PropertyMap> RequiredProperties { get; private set; }

        public IList<PropertyMap> InsertableProperties { get; private set; }

        public IList<PropertyMap> UpdateableProperties { get; private set; }

        public IList<PropertyMap> DateStampProperties { get; private set; }

        public PropertyMap SoftDeleteProperty { get; private set; }

        public bool IsSoftDelete
        {
            get { return this.SoftDeleteProperty != null; }
        }

        /// <summary>
        /// Gets a dictionary indexed by property name paired with the default sort order (Ascending).
        /// </summary>
        public IDictionary<string, SortOrder> DefaultSortOrder { get; private set; }

        /// <summary>
        /// Validates the key properties.
        /// Converts to a dictionary when value type is passed and we have an identity key.
        /// </summary>
        /// <param name="id">The identifier.</param>
        /// <exception cref="ArgumentException">Thrown if a key property is not passed on the object.</exception>
        /// <returns>The validated key property bag.</returns>
        public IList<IPredicate> ValidateKeyProperties<T>(object id) where T : class
        {
            return ValidateKeyProperties<T>(id, this.AllKeys);
        }

        /// <summary>
        /// Validates the assigned key properties are present.
        /// </summary>
        /// <param name="id">The identifier.</param>
        /// <exception cref="ArgumentException">Thrown if a key property is not passed on the object.</exception>
        /// <returns>The validated key property bag.</returns>
        public IList<IPredicate> ValidateAssignedKeyProperties<T>(object id) where T : class
        {
            if (this.AssignedKeys.Count() == 0)
            {
                return new List<IPredicate>();
            }

            return ValidateKeyProperties<T>(id, this.AssignedKeys);
        }

        private IList<IPredicate> ValidateKeyProperties<T>(object propertyBag, IEnumerable<PropertyMap> properties) where T : class
        {
            if (propertyBag == null)
            {
                throw new ArgumentException("Passed object is null.");
            }

            // if we have a single key on our dto and passed id is the same type as the key, return the predicate
            if (this.AllKeys.Count() == 1 && this.AllKeys.Single().PropertyInfo.PropertyType == propertyBag.GetType())
            {
                return new List<IPredicate>()
                {
                    Predicate.PredicateBuilder.Equal<T>(this.AllKeys.Single().PropertyName, propertyBag)
                };
            }

            // trim out unrequired key properties
            IDictionary<string, object> keysDictionary = this.CoalesceKeyToDictionary(propertyBag);
            foreach (string key in keysDictionary.Keys)
            {
                if (properties.Any(x => x.PropertyName == key) == false)
                {
                    keysDictionary.Remove(key);
                }
            }

            return this.ValidateWhereProperties<T>(keysDictionary);
        }

        /// <summary>
        /// Coalesces a dictionary from a property bag.
        /// </summary>
        /// <param name="propertyBag">The property bag.</param>
        /// <returns>Dictionary representing the objects properties.</returns>
        /// <exception cref="ArgumentException">
        /// Passed property bag is null
        /// or
        /// Failed to find property {propertyInfo.Name}.
        /// </exception>
        public IDictionary<string, object> CoalesceToDictionary(object propertyBag)
        {
            if (propertyBag == null)
            {
                return new Dictionary<string, object>();
            }

            // if we have an expando object or dictionary already then return it
            if (propertyBag is IDictionary<string, object>)
            {
                IDictionary<string, object> dict = propertyBag as IDictionary<string, object>;
                foreach (string key in dict.Keys)
                {
                    if (!this.AllProperties.TryGetValue(key, out PropertyMap pm))
                    {
                        throw new ArgumentException($"Failed to find property {key}.");
                    }
                }

                return dict;
            }

            IDictionary<string, object> obj = new Dictionary<string, object>();
            PropertyInfo[] propertyInfos = propertyBag.GetType().GetProperties();
            foreach (PropertyInfo propertyInfo in propertyInfos)
            {
                if (!this.AllProperties.TryGetValue(propertyInfo.Name, out PropertyMap pm))
                {
                    throw new ArgumentException($"Failed to find property {propertyInfo.Name}.");
                }

                obj.Add(propertyInfo.Name, propertyInfo.GetValue(propertyBag));
            }

            return obj;
        }

        /// <summary>
        /// Coalesces a dictionary representing the primary key.
        /// </summary>
        /// <param name="propertyBag">The property bag.</param>
        /// <returns>Dictionary representing the primary key.</returns>
        /// <exception cref="ArgumentException">
        /// Passed property bag is null
        /// or
        /// Failed to find key property {propertyMap.Property}.
        /// </exception>
        public IDictionary<string, object> CoalesceKeyToDictionary(object propertyBag)
        {
            if (propertyBag == null)
            {
                throw new ArgumentException("Passed property bag is null.");
            }

            // if we have a single key on our dto and passed id is the same type as the key, return the dictionary
            if (this.AllKeys.Count() == 1 && this.AllKeys.Single().PropertyInfo.PropertyType == propertyBag.GetType())
            {
                return new Dictionary<string, object>() { { this.AllKeys.Single().PropertyName, propertyBag } };
            }

            // otherwise, check we have all key properties
            IDictionary<string, object> key = new Dictionary<string, object>();
            PropertyInfo[] propertyInfos = propertyBag.GetType().GetProperties();
            foreach (PropertyMap propertyMap in this.AllKeys)
            {
                PropertyInfo pi = propertyInfos.Where(x => x.Name == propertyMap.PropertyName).SingleOrDefault();
                if (pi == null)
                {
                    throw new ArgumentException($"Failed to find key property {propertyMap.PropertyName}.");
                }

                key.Add(propertyMap.PropertyName, pi.GetValue(propertyBag));
            }

            return key;
        }

        /// <summary>
        /// Coalesces a dictionary representing the requested sort orders.
        /// If no sort orders are passed then return a default ordering (ascending for all key fields).
        /// </summary>
        /// <param name="sortOrders">The sort orders.</param>
        /// <returns>Dictionary representing the sort orders (indexed by column name).</returns>
        /// <exception cref="ArgumentException">
        /// Failed to find key property {propertyMap.Property}.
        /// or
        /// Must pass a sort order value.
        /// </exception>
        public IDictionary<string, SortOrder> CoalesceSortOrderDictionary(object sortOrders)
        {
            // only attempt to coalesce if we don't have a null reference
            if (sortOrders != null)
            {
                // initialise our dictionary
                IDictionary<string, SortOrder> sortOrdersDict = new Dictionary<string, SortOrder>();

                // if we were passed a dictionary of the same type then return it as long as it has one or more values
                if (sortOrders is IDictionary<string, SortOrder>)
                {
                    sortOrdersDict = sortOrders as IDictionary<string, SortOrder>;
                }
                else
                {
                    // coalesce the object
                    foreach (PropertyInfo propertyInfo in sortOrders.GetType().GetProperties())
                    {
                        if (propertyInfo.PropertyType != typeof(SortOrder))
                        {
                            throw new ArgumentException($"Must pass a valid sort order value.");
                        }

                        sortOrdersDict.Add(propertyInfo.Name, (SortOrder)propertyInfo.GetValue(sortOrders));
                    }
                }

                // validate and return
                if (sortOrdersDict.Any())
                {
                    return sortOrdersDict;
                }
            }

            return this.DefaultSortOrder;
        }

        /// <summary>
        /// Validates the where properties.
        /// </summary>
        /// <param name="whereConditions">The where conditions.</param>
        /// <returns>A list of validated property maps.</returns>
        /// <exception cref="ArgumentException">
        /// Please pass where conditions.
        /// or
        /// Please specify at least one property for a WHERE condition.
        /// or
        /// Failed to find property {property.Name}.
        /// </exception>
        public IList<IPredicate> ValidateWhereProperties<T>(object whereConditions) where T : class
        {
            // check we have some conditions to create
            IDictionary<string, object> whereDict = this.CoalesceToDictionary(whereConditions);
            if (whereDict.Count == 0)
            {
                return new List<IPredicate>();
            }

            // setup our list of property mappings that we will create the where clause from
            List<IPredicate> whereOperations = new List<IPredicate>();
            foreach (string propertyName in whereDict.Keys)
            {
                PropertyMap propertyMap = this.SelectProperties
                                              .SingleOrDefault(x => x.PropertyName == propertyName);

                if (propertyMap == null)
                {
                    throw new ArgumentException($"Failed to find property {propertyName}.");
                }

                // check that the values passed are of the correct type
                object value = whereDict[propertyName];
                Type valueType = value.GetType();

                if (propertyMap.PropertyInfo.PropertyType.IsAssignableFrom(valueType))
                {
                    IPredicate op = Predicate.PredicateBuilder.Equal<T>(propertyName, whereDict[propertyName]);
                    whereOperations.Add(op);
                }
                else
                {
                    IPredicate op = Predicate.PredicateBuilder.In<T>(propertyName, (System.Collections.IEnumerable)whereDict[propertyName]);
                    whereOperations.Add(op);
                }
            }

            return whereOperations;
        }
    }
}