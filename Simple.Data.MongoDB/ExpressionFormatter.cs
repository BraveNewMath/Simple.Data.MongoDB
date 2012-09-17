﻿using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

using MongoDB.Bson;
using MongoDB.Driver.Builders;
using MongoDB.Driver;

namespace Simple.Data.MongoDB
{
    internal class ExpressionFormatter : IExpressionFormatter
    {
        public static HashSet<string> _functions = new HashSet<string>(StringComparer.CurrentCultureIgnoreCase)
        {
            "like",
            "startswith",
            "contains",
            "endswith"
        };

        private readonly Dictionary<string, Func<SimpleReference, SimpleFunction, IMongoQuery>> _supportedFunctions;
            
        private readonly MongoAdapter _adapter;

        public ExpressionFormatter(MongoAdapter adapter)
        {
            _adapter = adapter;

            _supportedFunctions = new Dictionary<string, Func<SimpleReference, SimpleFunction, IMongoQuery>>(StringComparer.InvariantCultureIgnoreCase)
            {
                { "like", HandleLike },
                { "startswith", HandleStartsWith },
                { "contains", HandleContains },
                { "endswith", HandleEndsWith }
            };
        }

        public IMongoQuery Format(SimpleExpression expression)
        {
            switch (expression.Type)
            {
                case SimpleExpressionType.And:
                    return LogicalExpression(expression, (l, r) => Query.And(l, r));
                case SimpleExpressionType.Equal:
                    return EqualExpression(expression);
                case SimpleExpressionType.GreaterThan:
                    return BinaryExpression(expression, Query.GT);
                case SimpleExpressionType.GreaterThanOrEqual:
                    return BinaryExpression(expression, Query.GTE);
                case SimpleExpressionType.LessThan:
                    return BinaryExpression(expression, Query.LT);
                case SimpleExpressionType.LessThanOrEqual:
                    return BinaryExpression(expression, Query.LTE);
                case SimpleExpressionType.Function:
                    return FunctionExpression(expression);
                case SimpleExpressionType.NotEqual:
                    return NotEqualExpression(expression);
                case SimpleExpressionType.Or:
                    return LogicalExpression(expression, (l, r) => Query.Or(l, r));
            }

            throw new NotSupportedException();
        }

        private IMongoQuery BinaryExpression(SimpleExpression expression, Func<string, BsonValue, IMongoQuery> builder)
        {
            var fieldName = (string)FormatObject(expression.LeftOperand);
            var value = BsonValue.Create(FormatObject(expression.RightOperand));
            return builder(fieldName, value);
        }

        private IMongoQuery EqualExpression(SimpleExpression expression)
        {
            var fieldName = (string)FormatObject(expression.LeftOperand);
            var range = expression.RightOperand as IRange;
            if (range != null)
            {
                return Query.And(
                    Query.GTE(fieldName, BsonValue.Create(range.Start)),
                    Query.LTE(fieldName, BsonValue.Create(range.End)));
            }

            var list = expression.RightOperand as IEnumerable;
            if (list != null & expression.RightOperand.GetType() != typeof(string))
                return Query.In(fieldName, new BsonArray(list.OfType<object>()));

            return Query.EQ(fieldName, BsonValue.Create(FormatObject(expression.RightOperand)));
        }

        private IMongoQuery FunctionExpression(SimpleExpression expression)
        {
            var function = expression.RightOperand as SimpleFunction;
            if (function == null) throw new InvalidOperationException("Expected SimpleFunction as the right operand.");

            Func<SimpleReference, SimpleFunction, IMongoQuery> handler;
            if(!_supportedFunctions.TryGetValue(function.Name, out handler))
                throw new NotSupportedException(string.Format("Unknown function '{0}'.", function.Name));

            return handler((SimpleReference)expression.LeftOperand, function);
        }

        private IMongoQuery LogicalExpression(SimpleExpression expression, Func<IMongoQuery, IMongoQuery, IMongoQuery> builder)
        {
            return builder(
                Format((SimpleExpression)expression.LeftOperand),
                Format((SimpleExpression)expression.RightOperand));
        }

        private IMongoQuery NotEqualExpression(SimpleExpression expression)
        {
            var fieldName = (string)FormatObject(expression.LeftOperand);
            var range = expression.RightOperand as IRange;
            if (range != null)
            {
                return Query.Or(
                    Query.LTE(fieldName, BsonValue.Create(range.Start)),
                    Query.GTE(fieldName, BsonValue.Create(range.End)));
            }

            var list = expression.RightOperand as IEnumerable;
            if (list != null & expression.RightOperand.GetType() != typeof(string))
                return Query.NotIn(fieldName, new BsonArray(list.OfType<object>()));

            return Query.NE(fieldName, BsonValue.Create(FormatObject(expression.RightOperand)));
        }

        private object FormatObject(object operand)
        {
            var reference = operand as ObjectReference;
            if (!ReferenceEquals(reference, null))
            {
                return GetFullName(reference);
            }
            return operand;
        }

        private IMongoQuery HandleLike(SimpleReference reference, SimpleFunction function)
        {
            if (function.Args[0] is Regex)
                return Query.Matches((string)FormatObject(reference), new BsonRegularExpression((Regex)function.Args[0]));
            else if (function.Args[0] is string)
                return Query.Matches((string)FormatObject(reference), new BsonRegularExpression((string)FormatObject(function.Args[0])));

            throw new InvalidOperationException("Like can only be used with a string or Regex.");
        }

        private IMongoQuery HandleStartsWith(SimpleReference reference, SimpleFunction function)
        {
            if(!(function.Args[0] is string)) throw new InvalidOperationException("StartsWith can only be used with a string.");
         
            return Query.Matches((string)FormatObject(reference), new BsonRegularExpression("^" + (string)function.Args[0] + ".*"));
        }

        private IMongoQuery HandleContains(SimpleReference reference, SimpleFunction function)
        {
            if (!(function.Args[0] is string)) throw new InvalidOperationException("StartsWith can only be used with a string.");

            return Query.Matches((string)FormatObject(reference), new BsonRegularExpression("^.*" + (string)function.Args[0] + ".*$"));
        }

        private IMongoQuery HandleEndsWith(SimpleReference reference, SimpleFunction function)
        {
            if (!(function.Args[0] is string)) throw new InvalidOperationException("StartsWith can only be used with a string.");

            return Query.Matches((string)FormatObject(reference), new BsonRegularExpression(".*" + (string)function.Args[0] + "$"));
        }

        internal static string GetFullName(ObjectReference reference)
        {
            var names = new Stack<string>();
            string name;
            while (!ReferenceEquals(reference.GetOwner(), null))
            {
                name = reference.GetName();
                name = name == "Id" || name == "id" ? "_id" : name;
                names.Push(name);

                reference = reference.GetOwner();
            }

            return string.Join(".", names.ToArray());
        }
    }
}