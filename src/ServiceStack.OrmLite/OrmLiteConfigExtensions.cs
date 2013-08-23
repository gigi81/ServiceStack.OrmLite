//
// ServiceStack.OrmLite: Light-weight POCO ORM for .NET and Mono
//
// Authors:
//   Demis Bellot (demis.bellot@gmail.com)
//
// Copyright 2010 Liquidbit Ltd.
//
// Licensed under the same terms of ServiceStack: new BSD license.
//

using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Reflection;
using ServiceStack.DataAnnotations;
using ServiceStack.Net30.Collections.Concurrent;
using ServiceStack.Text;

namespace ServiceStack.OrmLite
{
    internal static class OrmLiteConfigExtensions
    {
        private static readonly ConcurrentDictionary<Type, ModelDefinition> _cache = new ConcurrentDictionary<Type, ModelDefinition>();

        private static bool IsNullableType(Type theType)
        {
            return (theType.IsGenericType && theType.GetGenericTypeDefinition() == typeof(Nullable<>));
        }

        internal static void ClearCache()
        {
            _cache.Clear();
        }

        internal static ModelDefinition Init(this Type modelType)
        {
            return modelType.GetModelDefinition();
        }

        internal static ModelDefinition GetModelDefinition(this Type modelType)
        {
            return _cache.GetOrAdd(modelType, CreateModelDefinition);
        }

        private static ModelDefinition CreateModelDefinition(Type modelType)
        {
            var modelAliasAttr = modelType.FirstAttribute<AliasAttribute>();
            var schemaAttr = modelType.FirstAttribute<SchemaAttribute>();
            var modelDef = new ModelDefinition
            {
                ModelType = modelType,
                Name = modelType.Name,
                Alias = modelAliasAttr != null ? modelAliasAttr.Name : null,
                Schema = schemaAttr != null ? schemaAttr.Name : null
            };

            modelDef.CompositeIndexes.AddRange(modelType.GetCustomAttributes(typeof(CompositeIndexAttribute), true).OfType<CompositeIndexAttribute>());

            var objProperties = modelType.GetProperties(BindingFlags.Public | BindingFlags.Instance);

            var hasPrimaryKey = CheckPrimaryKey(objProperties);

            for (var i = 0; i < objProperties.Length; i++)
            {
                var propertyInfo = objProperties[i];
                var sequenceAttr = propertyInfo.FirstAttribute<SequenceAttribute>();
                var computeAttr = propertyInfo.FirstAttribute<ComputeAttribute>();
                var pkAttribute = propertyInfo.FirstAttribute<PrimaryKeyAttribute>();
                var decimalAttribute = propertyInfo.FirstAttribute<DecimalLengthAttribute>();
                var belongToAttribute = propertyInfo.FirstAttribute<BelongToAttribute>();

                var isPrimaryKey = propertyInfo.Name == OrmLiteConfig.IdField || pkAttribute != null || (!hasPrimaryKey && (i == 0 /* first field */));

                var isNullableType = IsNullableType(propertyInfo.PropertyType);

                var isNullable = isNullableType || (!propertyInfo.PropertyType.IsValueType && propertyInfo.FirstAttribute<RequiredAttribute>() == null);

                var propertyType = isNullableType
                                       ? Nullable.GetUnderlyingType(propertyInfo.PropertyType)
                                       : propertyInfo.PropertyType;

                var aliasAttr = propertyInfo.FirstAttribute<AliasAttribute>();

                var indexAttr = propertyInfo.FirstAttribute<IndexAttribute>();
                var isIndex = indexAttr != null;
                var isUnique = isIndex && indexAttr.Unique;

                var stringLengthAttr = propertyInfo.FirstAttribute<StringLengthAttribute>();

                var defaultValueAttr = propertyInfo.FirstAttribute<DefaultAttribute>();

                var referencesAttr = propertyInfo.FirstAttribute<ReferencesAttribute>();
                var foreignKeyAttr = propertyInfo.FirstAttribute<ForeignKeyAttribute>();

                if (decimalAttribute != null && stringLengthAttr == null)
                    stringLengthAttr = new StringLengthAttribute(decimalAttribute.Precision);

                var fieldDefinition = new FieldDefinition
                {
                    Name              = propertyInfo.Name,
                    Alias             = aliasAttr != null ? aliasAttr.Name : null,
                    FieldType         = propertyType,
                    PropertyInfo      = propertyInfo,
                    IsNullable        = isNullable,
                    IsPrimaryKey      = isPrimaryKey,
                    AutoIncrement     = isPrimaryKey && propertyInfo.FirstAttribute<AutoIncrementAttribute>() != null,
                    IsIndexed         = isIndex,
                    IsUnique          = isUnique,
                    FieldLength       = stringLengthAttr != null ? stringLengthAttr.MaximumLength : (int?)null,
                    DefaultValue      = defaultValueAttr != null ? defaultValueAttr.DefaultValue : null,
                    ForeignKey        = GetForeignKeyConstraint(foreignKeyAttr, referencesAttr),
                    GetValueFn        = propertyInfo.GetPropertyGetterFn(),
                    SetValueFn        = propertyInfo.GetPropertySetterFn(),
                    Sequence          = sequenceAttr != null ? sequenceAttr.Name : string.Empty,
                    IsComputed        = computeAttr != null,
                    ComputeExpression = computeAttr != null ? computeAttr.Expression : string.Empty,
                    Scale             = decimalAttribute != null ? decimalAttribute.Scale : (int?)null,
                    BelongToModelName = belongToAttribute != null ? belongToAttribute.BelongToTableType.GetModelDefinition().ModelName : null,
                };

                if (propertyInfo.FirstAttribute<IgnoreAttribute>() != null)
                    modelDef.IgnoredFieldDefinitions.Add(fieldDefinition);
                else
                    modelDef.FieldDefinitions.Add(fieldDefinition);
            }

            modelDef.SqlSelectAllFromTable = "SELECT {0} FROM {1} ".Fmt(OrmLiteConfig.DialectProvider.GetColumnNames(modelDef),
                                                                        OrmLiteConfig.DialectProvider.GetQuotedTableName(modelDef));
            return modelDef;
        }

        private static bool CheckPrimaryKey(PropertyInfo[] objProperties)
        {
            return objProperties.Any(p => p.Name == OrmLiteConfig.IdField) ||
                   objProperties.Any(p => p.FirstAttribute<PrimaryKeyAttribute>() != null);
        }

        private static ForeignKeyConstraint GetForeignKeyConstraint(ForeignKeyAttribute foreignKeyAttr, ReferencesAttribute referencesAttr)
        {
            if (foreignKeyAttr != null)
                return new ForeignKeyConstraint(foreignKeyAttr.Type,
                                                foreignKeyAttr.OnDelete,
                                                foreignKeyAttr.OnUpdate,
                                                foreignKeyAttr.ForeignKeyName);

            if (referencesAttr != null)
                return new ForeignKeyConstraint(referencesAttr.Type);

            return null;
        }
    }
}