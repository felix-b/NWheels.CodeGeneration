﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml.Linq;
using MetaPrograms;
using MetaPrograms.Expressions;

namespace MetaPrograms.JavaScript.Writer
{
    public static class JavaScriptExpressionWriter
    {
        private static readonly Dictionary<Type, Action<CodeTextBuilder, AbstractExpression>> WriterByExpressionType =
            new Dictionary<Type, Action<CodeTextBuilder, AbstractExpression>> {
                [typeof(NullExpression)] = (c, e) => WriteNull(c, (NullExpression)e),
                [typeof(NewArrayExpression)] = (c, e) => WriteNewArray(c, (NewArrayExpression)e),
                [typeof(ConstantExpression)] = (c, e) => WriteConstant(c, (ConstantExpression)e),
                [typeof(TupleExpression)] = (c, e) => WriteTuple(c, (TupleExpression)e),
                [typeof(LocalVariableExpression)] = (c, e) => WriteVariable(c, (LocalVariableExpression)e),
                [typeof(ParameterExpression)] = (c, e) => WriteParameter(c, (ParameterExpression)e),
                [typeof(ThisExpression)] = (c, e) => WriteThis(c, (ThisExpression)e),
                [typeof(MemberExpression)] = (c, e) => WriteMember(c, (MemberExpression)e),
                [typeof(AssignmentExpression)] = (c, e) => WriteAssignment(c, (AssignmentExpression)e),
                [typeof(DelegateInvocationExpression)] = (c, e) => WriteDelegateInvocation(c, (DelegateInvocationExpression)e),
                [typeof(AnonymousDelegateExpression)] = (c, e) => WriteLambda(c, (AnonymousDelegateExpression)e),
                [typeof(MethodCallExpression)] = (c, e) => WriteMethodCall(c, (MethodCallExpression)e),
                [typeof(ObjectInitializerExpression)] = (c, e) => WriteObjectInitializer(c, (ObjectInitializerExpression)e),
                [typeof(AwaitExpression)] = (c, e) => WriteAwait(c, (AwaitExpression)e),
                [typeof(ConditionalExpression)] = (c, e) => WriteConditional(c, (ConditionalExpression)e),
                [typeof(BinaryExpression)] = (c, e) => WriteBinary(c, (BinaryExpression)e),
                [typeof(XmlExpression)] = (c, e) => WriteJsx(c, (XmlExpression)e)
            };

        private static readonly Dictionary<BinaryOperator, string> BinarySyntaxByOperator =
            new Dictionary<BinaryOperator, string>() {
                {BinaryOperator.Add, "+"},
                {BinaryOperator.Subtract, "-"},
                {BinaryOperator.Multiply, "*"},
                {BinaryOperator.Divide, "/"},
                {BinaryOperator.Modulus, "%"},
                {BinaryOperator.LogicalAnd, "&&"},
                {BinaryOperator.LogicalOr, "||"},
                {BinaryOperator.BitwiseAnd, "&"},
                {BinaryOperator.BitwiseOr, "|"},
                {BinaryOperator.BitwiseXor, "^"},
                {BinaryOperator.LeftShift, "<<"},
                {BinaryOperator.RightShift, ">>"},
                {BinaryOperator.Equal, "==="},
                {BinaryOperator.NotEqual, "!=="},
                {BinaryOperator.GreaterThan, ">"},
                {BinaryOperator.LessThan, "<"},
                {BinaryOperator.GreaterThanOrEqual, ">="},
                {BinaryOperator.LessThanOrEqual, "<="},
                {BinaryOperator.NullCoalesce, "||"}
            };

        public static void WriteExpression(CodeTextBuilder code, AbstractExpression expression)
        {
            if (WriterByExpressionType.TryGetValue(expression.GetType(), out var writer))
            {
                writer(code, expression);
            }
            else
            {
                throw new NotSupportedException(
                    $"Expression of type '{expression.GetType().Name}' is not supported by {nameof(JavaScriptExpressionWriter)}.");
            }
        }

        private static void WriteNull(CodeTextBuilder code, NullExpression expression)
        {
            JavaScriptLiteralWriter.WriteLiteral(code, null);
        }

        private static void WriteNewArray(CodeTextBuilder code, NewArrayExpression expression)
        {
            code.WriteListStart(opener: "[", closer: "]", separator: ",", newLine: true);

            if (expression.DimensionInitializerValues != null)
            {
                foreach (var item in expression.DimensionInitializerValues.SelectMany(x => x))
                {
                    code.WriteListItem();
                    WriteExpression(code, item);
                }
            }
            
            code.WriteListEnd();
        }

        public static void WriteConstant(CodeTextBuilder code, ConstantExpression constant)
        {
            JavaScriptLiteralWriter.WriteLiteral(code, constant.Value);
        }

        public static void WriteTuple(CodeTextBuilder code, TupleExpression tuple)
        {
            var variableListText = string.Join(", ", tuple.Variables.Select(v => v.Name));
            code.Write($"{{ {variableListText} }}");
        }

        public static void WriteVariable(CodeTextBuilder code, LocalVariableExpression variable)
        {
            var variableName = variable.VariableName ?? variable.Variable.Name;
            code.Write(ToCamelCase(variableName));
        }

        public static void WriteParameter(CodeTextBuilder code, ParameterExpression expression)
        {
            if (expression.Parameter.Tuple != null)
            {
                WriteTuple(code, expression.Parameter.Tuple);
            }
            else
            {
                code.Write(ToCamelCase(expression.Parameter.Name));
            }
        }

        public static string ToCamelCase(IdentifierName identifier)
        {
            return identifier.GetSealedOrCased(
                CasingStyle.Camel, 
                sealLanguage: LanguageInfo.Entries.JavaScript());
        }

        private static void WriteThis(CodeTextBuilder code, ThisExpression expression)
        {
            code.Write("this");
        }

        private static void WriteMember(CodeTextBuilder code, MemberExpression expression)
        {
            if (expression.Target != null)
            {
                WriteExpression(code, expression.Target);
                code.Write(".");
            }

            code.Write(ToCamelCase(expression.MemberName ?? expression.Member.Name));
        }

        private static void WriteAssignment(CodeTextBuilder code, AssignmentExpression expression)
        {
            WriteExpression(code, expression.Left.AsExpression());
            code.Write(" = ");
            WriteExpression(code, expression.Right);
        }

        private static void WriteLambda(CodeTextBuilder code, AnonymousDelegateExpression expression)
        {
            JavaScriptFunctionWriter.WriteArrowFunction(code, expression.Signature, expression.Body);
        }

        private static void WriteDelegateInvocation(CodeTextBuilder code, DelegateInvocationExpression expression)
        {
            WriteExpression(code, expression.Delegate);
            
            code.WriteListStart(opener: "(", separator: ", ", closer: ")");

            foreach (var argument in expression.Arguments)
            {
                code.WriteListItem();
                WriteExpression(code, argument.Expression);
            }
            
            code.WriteListEnd();
        }

        private static void WriteMethodCall(CodeTextBuilder code, MethodCallExpression call)
        {
            if (call.Target != null)
            {
                WriteExpression(code, call.Target);
                code.Write(".");
            }

            code.Write(ToCamelCase(call.MethodName ?? call.Method.Name));
            code.WriteListStart(opener: "(", separator: ", ", closer: ")");

            foreach (var argument in call.Arguments)
            {
                code.WriteListItem();
                WriteExpression(code, argument.Expression);
            }

            code.WriteListEnd();
        }
        
        private static void WriteObjectInitializer(CodeTextBuilder code, ObjectInitializerExpression expression)
        {
            if (expression.PropertyValues == null || expression.PropertyValues.Count == 0)
            {
                code.Write("{ }");
                return;
            }
            
            code.WriteListStart(opener: "{", closer: "}", separator: ",", newLine: true);

            foreach (var keyValue in expression.PropertyValues)
            {
                code.WriteListItem();
                code.Write(keyValue.Name.GetSealedOrCased(CasingStyle.Camel, sealLanguage: LanguageInfo.Entries.JavaScript()));

                if (keyValue.Value is IAssignable assignable && assignable.Name == keyValue.Name)
                {
                    continue;
                }

                code.Write(": ");
                WriteExpression(code, keyValue.Value);
            }
            
            code.WriteListEnd();
        }

        private static void WriteAwait(CodeTextBuilder code, AwaitExpression expression)
        {
            code.Write("await ");
            WriteExpression(code, expression.Expression);
        }

        private static void WriteConditional(CodeTextBuilder code, ConditionalExpression expression)
        {
            code.Write("(");
            WriteExpression(code, expression.Condition);
            code.Write(" ? ");
            WriteExpression(code, expression.WhenTrue);
            code.Write(" : ");
            WriteExpression(code, expression.WhenFalse);
            code.Write(")");
        }

        private static void WriteBinary(CodeTextBuilder code, BinaryExpression expression)
        {
            WriteExpression(code, expression.Left);
            code.Write($" {BinarySyntaxByOperator[expression.Operator]} ");
            WriteExpression(code, expression.Right);
        }


        private static void WriteJsx(CodeTextBuilder code, XmlExpression expression)
        {
            if (expression.Xml == null)
            {
                code.Write("null");
                return;
            }

            var xmlText = new StringBuilder();
            var jsxWriter = new JsxCodeWriter(code);
            
            code.WriteListStart(opener: "(", separator: "", closer: ")", newLine: true);
            code.WriteListItem();

            jsxWriter.Write(expression.Xml);
            
            code.WriteListEnd();
        }
    }
}
