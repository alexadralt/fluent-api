using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Text;

namespace ObjectPrinting
{
    public class PrintingConfig<TOwner>
    {
        private readonly List<Type> _excludedTypes = new();
        private readonly List<MemberInfo> _excludedProperties = new();
        private readonly Dictionary<Type, Delegate> _typeConverters = new();
        private readonly Dictionary<MemberInfo, Delegate> _propertyConverters = new();
        internal CultureInfo DoubleCultureInfo { get; set; } = CultureInfo.CurrentCulture;
        internal CultureInfo FloatCultureInfo { get; set; } = CultureInfo.CurrentCulture;
        internal CultureInfo DateTimeCultureInfo { get; set; } = CultureInfo.CurrentCulture;
        internal int MaxStringLength { get; set; } = int.MaxValue;
        internal int MaxRecursionDepth { get; set; } = 16;

        public string PrintToString(TOwner obj)
        {
            return PrintToString(obj, 0);
        }

        public PrintingConfig<TOwner> WithMaxRecursionDepth(int maxRecursionDepth)
        {
            if (maxRecursionDepth < 0)
                throw new ArgumentOutOfRangeException($"{nameof(maxRecursionDepth)} must not be less than 0");
            MaxRecursionDepth = maxRecursionDepth;
            return this;
        }

        internal void AddTypeConverter<TParam>(Type type, Func<TParam, string?> converter)
        {
            _typeConverters.Add(type, converter);
        }

        internal void AddPropertyConverter<TParam>(Func<TParam, string> converter, MemberInfo propertyInfo)
        {
            _propertyConverters.Add(propertyInfo, converter);
        }

        public PrintingConfig<TOwner> ExceptType<T>()
        {
            _excludedTypes.Add(typeof(T));
            return this;
        }

        public PrintingConfig<TOwner> ExceptProperty(Expression<Func<TOwner, object>> propertyExpression)
        {
            if (propertyExpression == null)
                throw new ArgumentNullException($"{nameof(propertyExpression)} cannot be null");

            _excludedProperties.Add(GetMemberInfo(propertyExpression));
            return this;
        }

        public ITypeSerializer<TParam, TOwner> ForType<TParam>()
        {
            return new TypeSerializerImpl<TParam, TOwner>(this);
        }

        public IPropertySerializer<TOwner, TProperty> ForProperty<TProperty>(
            Expression<Func<TOwner, TProperty>> propertyExpression)
        {
            if (propertyExpression == null)
                throw new ArgumentNullException($"{nameof(propertyExpression)} cannot be null");

            return new PropertySerializerImpl<TOwner, TProperty>(this, GetMemberInfo(propertyExpression));
        }

        private static MemberInfo GetMemberInfo<TProperty>(Expression<Func<TOwner, TProperty>> propertyExpression)
        {
            if (propertyExpression.Body is MemberExpression memberExpression)
                return memberExpression.Member;
            
            if (propertyExpression.Body is UnaryExpression unaryExpression
                     && unaryExpression.Operand is MemberExpression unaryMemberExpression)
                return unaryMemberExpression.Member;
            
            throw new ArgumentException("Expression does not refer to a property or field.");
        }

        private string PrintToString(object obj, int nestingLevel)
        {
            if (obj == null)
                return "null" + Environment.NewLine;

            var finalTypes = new[]
            {
                typeof(int), typeof(double), typeof(float), typeof(string),
                typeof(DateTime), typeof(TimeSpan)
            };
            if (finalTypes.Contains(obj.GetType()))
                return SerializeFinalType(obj);
            
            if (nestingLevel > MaxRecursionDepth)
                return "null" + Environment.NewLine;

            var indentation = new string('\t', nestingLevel + 1);
            var sb = new StringBuilder();
            var type = obj.GetType();
            sb.AppendLine($"{type.Name}:");

            if (obj is IEnumerable enumerable)
                return SerializeEnumerable(sb, enumerable, nestingLevel);
            
            foreach (var propertyInfo in type.GetProperties())
            {
                if (!_excludedProperties.Contains(propertyInfo) && !_excludedTypes.Contains(propertyInfo.PropertyType))
                {
                    var valueString = GetValueString(propertyInfo, obj, nestingLevel);
                    sb.Append($"{indentation}{propertyInfo.Name} = {valueString}");
                }
            }
            return sb.ToString();
        }

        private string SerializeEnumerable(StringBuilder sb, IEnumerable enumerable, int nestingLevel)
        {
            var bracketIndentation = new string('\t', nestingLevel);
            sb.AppendLine($"{bracketIndentation}[");
            foreach (var element in enumerable)
            {
                sb.Append($"{bracketIndentation}-\t");
                var valueString = String.Empty;
                if (_typeConverters.TryGetValue(element.GetType(), out var typeConverter))
                    valueString =
                        $"{typeConverter.DynamicInvoke(element) as string ?? "null"}{Environment.NewLine}";
                else
                    valueString = PrintToString(element, nestingLevel + 1);
                sb.Append($"{valueString}");
            }
            sb.AppendLine($"{bracketIndentation}]");
            return sb.ToString();
        }

        private string SerializeFinalType(object obj)
        {
            if (obj is string stringValue)
                return string.Concat(
                    stringValue.AsSpan(0, Math.Min(MaxStringLength, stringValue.Length)),
                    Environment.NewLine);
                
            if (obj is double doubleValue)
                return doubleValue.ToString(DoubleCultureInfo) + Environment.NewLine;
                
            if (obj is float floatValue)
                return floatValue.ToString(FloatCultureInfo) + Environment.NewLine;
                
            if (obj is DateTime dateTimeValue)
                return dateTimeValue.ToString(DateTimeCultureInfo) + Environment.NewLine;
                
            return obj + Environment.NewLine;
        }

        private string GetValueString(PropertyInfo propertyInfo, object obj, int nestingLevel)
        {
            var propertyValue = propertyInfo.GetValue(obj);
            if (propertyValue == null || !TryConvert(propertyInfo, propertyValue, out var valueString))
                valueString = PrintToString(propertyValue, nestingLevel + 1);
            return valueString;
        }

        private bool TryConvert(PropertyInfo propertyInfo, object? propertyValue, out string value)
        {
            value = String.Empty;
            if (_propertyConverters.TryGetValue(propertyInfo, out var converter))
                value = $"{converter.DynamicInvoke(propertyValue) as string ?? "null"}{Environment.NewLine}";
            else if (_typeConverters.TryGetValue(propertyInfo.PropertyType, out var typeConverter))
                value = $"{typeConverter.DynamicInvoke(propertyValue) as string ?? "null"}{Environment.NewLine}";
            return value != String.Empty;
        }
    }
}