﻿using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Text;
using MetaPrograms;
using MetaPrograms.Statements;

namespace MetaPrograms.JavaScript.Writer
{
    public static class JavaScriptStatementWriter
    {
        private static readonly Dictionary<Type, Action<CodeTextBuilder, AbstractStatement>> WriterByStatementType =
            new Dictionary<Type, Action<CodeTextBuilder, AbstractStatement>> {
                [typeof(BlockStatement)] = (c, s) => WriteBlock(c, (BlockStatement)s),
                [typeof(ExpressionStatement)] = (c, s) => WriteExpression(c, (ExpressionStatement)s),
                [typeof(VariableDeclarationStatement)] = (c, s) => WriteVariableDeclaration(c, (VariableDeclarationStatement)s),
                [typeof(ReturnStatement)] = (c, s) => WriteReturn(c, (ReturnStatement)s),
                [typeof(IfStatement)] = (c, s) => WriteIf(c, (IfStatement)s)
            };

        private static void WriteBlock(CodeTextBuilder code, BlockStatement block)
        {
            foreach (var statement in block.Statements)
            {
                WriteStatementLine(code, statement);
            }
        }

        public static void WriteStatementLine(CodeTextBuilder code, AbstractStatement statement)
        {
            WriteStatement(code, statement);
            
            if (!(statement is BlockStatement))
            {
                code.WriteLine(";");
            }
        }

        public static void WriteStatement(CodeTextBuilder code, AbstractStatement statement)
        {
            if (WriterByStatementType.TryGetValue(statement.GetType(), out var writer))
            {
                writer(code, statement);
            }
            else
            {
                throw new NotSupportedException(
                    $"Statement of type '{statement.GetType().Name}' is not supported by {nameof(JavaScriptStatementWriter)}.");
            }
        }

        private static void WriteExpression(CodeTextBuilder code, ExpressionStatement statement)
        {
            JavaScriptExpressionWriter.WriteExpression(code, statement.Expression);
        }

        private static void WriteVariableDeclaration(CodeTextBuilder code, VariableDeclarationStatement statement)
        {
            code.Write(statement.Variable.IsFinal ? "const " : "let ");
            code.Write(statement.Variable.Name);

            if (statement.InitialValue != null)
            {
                code.Write(" = ");
                JavaScriptExpressionWriter.WriteExpression(code, statement.InitialValue);
            }
        }

        private static void WriteReturn(CodeTextBuilder code, ReturnStatement statement)
        {
            code.Write("return");

            if (statement.Expression != null)
            {
                code.Write(" ");
                JavaScriptExpressionWriter.WriteExpression(code, statement.Expression);
            }
        }
        
        private static void WriteIf(CodeTextBuilder code, IfStatement statement)
        {
            code.Write("if (");
            JavaScriptExpressionWriter.WriteExpression(code, statement.Condition);
            code.WriteLine(") ");

            WriteBlockInsideBraces(code, statement.ThenBlock);

            if (statement.ElseBlock != null)
            {
                WriteBlockInsideBraces(code, statement.ElseBlock);
            }
        }

        private static void WriteBlockInsideBraces(CodeTextBuilder code, BlockStatement block)
        {
            code.WriteListStart(opener: "{", closer: "}", separator: "", newLine: true);
            
            if (block != null)
            {
                WriteBlock(code, block);
            }
            
            code.WriteListEnd();
        }
    }
}
