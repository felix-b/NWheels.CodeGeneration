﻿using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Net.Mime;
using System.Reflection;
using System.Xml.Linq;
using MetaPrograms.Expressions;
using MetaPrograms.Members;
using MetaPrograms.Statements;
using static MetaPrograms.CodeGeneratorContext;

// ReSharper disable InconsistentNaming

namespace MetaPrograms.Fluent
{
    public static class Generator
    {
        public static void NAMESPACE(string name, Action body)
        {
            using (GetContextOrThrow().PushState(new NamespaceContext(name)))
            {
                body();
            }
        }

        public static ModuleMember MODULE(IdentifierName name, Action body)
        {
            return MODULE(folderPath: null, name, body);
        }

        public static ModuleMember MODULE(string[] folderPath, IdentifierName name, Action body)
        {
            var context = GetContextOrThrow();
            var module = new ModuleMember() {
                FolderPath = folderPath ?? new string[0],
                Name = name,
                Status = MemberStatus.Incomplete,
                Visibility = MemberVisibility.Public,
                GloalBlock = new BlockStatement()
            };

            using (context.PushState(module))
            {
                using (context.PushState(new BlockContext(module.GloalBlock)))
                {
                    body?.Invoke();
                }
            }

            module.Status = MemberStatus.Generator;
            return module;
        }

        public static IFluentImport IMPORT => new FluentImport();

        public static FluentVisibility PUBLIC => new FluentVisibility(MemberVisibility.Public);
        public static FluentVisibility EXPORT => new FluentVisibility(MemberVisibility.Public);
        public static FluentVisibility PRIVATE => new FluentVisibility(MemberVisibility.Private);
        public static FluentVisibility PROTECTED => new FluentVisibility(MemberVisibility.Protected);
        public static FluentVisibility INTERNAL => new FluentVisibility(MemberVisibility.Internal);

        public static void ATTRIBUTE<T>(params object[] constructorArgumentsAndBody)
        {
            ATTRIBUTE(GetContextOrThrow().FindType<T>(), constructorArgumentsAndBody);
        }

        public static void ATTRIBUTE(TypeMember type, params object[] constructorArgumentsAndBody)
        {
            var context = GetContextOrThrow();
            var attribute = FluentHelpers.BuildAttribute(context, type, constructorArgumentsAndBody);

            if (context.TryPeekState<ParameterContext>(out var parameterContext))
            {
                parameterContext.Parameter.Attributes.Add(attribute);
            }
            else
            {
                var member = context.GetCurrentMember();
                member.Attributes.Add(attribute);
            }
        }

        public static IdentifierName ID(params object[] anyFragments)
        {
            return new IdentifierName(anyFragments);
        }

        public static void NAMED(string name, object value)
        {
            var context = GetContextOrThrow();

            context
                .PeekStateOrThrow<AttributeContext>().Attribute
                .PropertyValues.Add(new NamedPropertyValue(
                    name, 
                    context.GetConstantExpression(value)));
        }

        public static void EXTENDS<T>()
        {
            EXTENDS(GetContextOrThrow().FindType<T>());
        }

        public static void EXTENDS(TypeMember type)
        {
            var descendantType = GetContextOrThrow().PeekStateOrThrow<TypeMember>();
            descendantType.BaseType = type;
        }

        public static void IMPLEMENTS<T>()
        {
            IMPLEMENTS(GetContextOrThrow().FindType<T>());
        }

        public static void IMPLEMENTS(TypeMember type)
        {
            var descendantType = GetContextOrThrow().PeekStateOrThrow<TypeMember>();
            descendantType.Interfaces.Add(type);
        }

        public static void PARAMETER<T>(string name, out MethodParameter @ref, Action body = null)
        {
            PARAMETER(GetContextOrThrow().FindType<T>(), name, out @ref, body);
        }

        public static void PARAMETER(string name, out MethodParameter @ref, Action body = null)
        {
            PARAMETER(type: null, name, out @ref, body);
        }

        public static void PARAMETER(TupleExpression tuple, Action body = null)
        {
            PARAMETER(type: null, name: null, out var @ref, body, tuple);
        }

        public static void PARAMETER(TypeMember type, string name, out MethodParameter @ref, Action body = null, TupleExpression tuple = null)
        {
            var context = GetContextOrThrow();
            var function = context.LookupStateOrThrow<IFunctionContext>();// (MethodMemberBase)context.GetCurrentMember();

            var newParameter = new MethodParameter {
                Name = name,
                Tuple = tuple,
                Position = function.Signature.Parameters.Count,
                Type = type
            }; 
            
            var parameterContext = new ParameterContext(newParameter);

            using (context.PushState(parameterContext))
            {
                body?.Invoke();
            }

            function.Signature.Parameters.Add(newParameter);
            @ref = newParameter;
        }

        public static void LOCAL(string name, out LocalVariable @ref, AbstractExpression initialValue = null)
        {
            LOCAL(type: null, name, out @ref, initialValue);
        }

        public static void LOCAL(TypeMember type, string name, out LocalVariable @ref, AbstractExpression initialValue = null)
        {
            @ref = new LocalVariable {
                Name = name,
                Type = type
            };
            
            var block = BlockContext.GetBlockOrThrow();
            block.AddLocal(@ref);
            block.AppendStatement(new VariableDeclarationStatement {
                Variable = @ref,
                InitialValue = PopExpression(initialValue)
            });
        }

        public static void LOCAL<T>(string name, out LocalVariable @ref, AbstractExpression initialValue = null)
        {
            var type = GetContextOrThrow().FindType<T>();
            LOCAL(type, name, out @ref, initialValue);
        }
        
        public static void FINAL(string name, out LocalVariable @ref, AbstractExpression value)
        {
            FINAL(type: null, name, out @ref, value);
        }

        public static void FINAL(TypeMember type, string name, out LocalVariable @ref, AbstractExpression value)
        {
            @ref = new LocalVariable {
                Name = name,
                Type = type,
                IsFinal = true
            };

            var block = BlockContext.GetBlockOrThrow();
            block.AddLocal(@ref);
            block.AppendStatement(new VariableDeclarationStatement {
                Variable = @ref, 
                InitialValue = PopExpression(value)
            });
        }

        public static void FINAL<T>(string name, out LocalVariable @ref, AbstractExpression value)
        {
            var type = GetContextOrThrow().FindType<T>();
            FINAL(type, name, out @ref, value);
        }

        public static void RAW(string code)
        {
            var block = BlockContext.GetBlockOrThrow();

            block.AppendStatement(new RawCodeStatement {
                Code = code
            });
        }

        public static void LOADRAW(string embeddedResourcePath)
        {
            var assembly = Assembly.GetCallingAssembly();
            var fullResourcePath = $"{assembly.GetName().Name}.{embeddedResourcePath}";
            var resource = assembly.GetManifestResourceStream(fullResourcePath);

            if (resource == null)
            {
                throw new ArgumentException(
                    $"Embedded resource not found: {fullResourcePath}", 
                    nameof(embeddedResourcePath));
            }

            var code = new StreamReader(resource).ReadToEnd();
            RAW(code);
        }

        public static LocalVariableExpression USE(string name) 
            => PushExpression(new LocalVariableExpression {
                VariableName = name
            });

        public static LocalVariableExpression USE(LocalVariable variable) 
            => PushExpression(new LocalVariableExpression {
                Type = variable.Type,
                Variable = variable
            });

        public static LocalVariableExpression USE(MethodParameter parameter) 
            => PushExpression(new LocalVariableExpression {
                Type = parameter.Type,
                VariableName = parameter.Name
            });

        public static TupleExpression TUPLE(string name1, out LocalVariable var1)
        {
            var tuple = MakeTuple(new[] { name1 }, types: null, out var variables);
            var1 = variables[0];
            return tuple;
        }

        public static TupleExpression TUPLE(string name1, TypeMember type1, out LocalVariable var1)
        {
            var tuple = MakeTuple(
                new[] { name1 }, 
                types: new[] { type1 }, 
                out var variables);
            
            var1 = variables[0];
            return tuple;
        }

        public static void GET() => throw new NotImplementedException();
        public static void GET(Action body) => throw new NotImplementedException();
        public static void SET(Action<LocalVariable> body) => throw new NotImplementedException();

        public static void ARGUMENT(AbstractExpression value) 
            => GetContextOrThrow().PeekStateOrThrow<InvocationContext>().AddArgument(value);

        public static void ARGUMENT_BYREF(AbstractExpression value)
            => GetContextOrThrow().PeekStateOrThrow<InvocationContext>().AddArgument(value, MethodParameterModifier.Ref);

        public static void ARGUMENT_OUT(AbstractExpression value)
            => GetContextOrThrow().PeekStateOrThrow<InvocationContext>().AddArgument(value, MethodParameterModifier.Out);

        public static AbstractExpression AWAIT(AbstractExpression promiseExpression)
            => PushExpression(new AwaitExpression {
                Expression = PopExpression(promiseExpression),
                Type = promiseExpression.Type
            });

        public static FluentStatement DO 
            => new FluentStatement();

        public static ThisExpression THIS 
            => new ThisExpression {
                Type = GetContextOrThrow().GetCurrentType()
            };

        public static AbstractExpression DOT(this AbstractExpression target, AbstractMember member)
            => PushExpression(new MemberExpression {
                Target = PopExpression(target),
                Member = member
            });

        public static AbstractExpression DOT(this AbstractExpression target, IdentifierName memberName)
            => PushExpression(new MemberExpression {
                Target = PopExpression(target),
                MemberName = memberName
            });

        public static AbstractExpression DOT(this LocalVariable target, AbstractMember member)
            => PushExpression(new MemberExpression {
                Type = target.Type,
                Target = PopExpression(target.AsExpression()),
                Member = member
            });

        public static AbstractExpression DOT(this LocalVariable target, IdentifierName memberName) 
            => PushExpression(new MemberExpression {
                Type = target.Type,
                Target = PopExpression(target.AsExpression()),
                MemberName = memberName
            });

        public static AbstractExpression DOT(this MethodParameter target, AbstractMember member)
            => PushExpression(new MemberExpression {
                Type = target.Type,
                Target = PopExpression(target.AsExpression()),
                Member = member
            });

        public static AbstractExpression DOT(this MethodParameter target, IdentifierName memberName)
            => PushExpression(new MemberExpression {
                Type = target.Type,
                Target = target.AsExpression(),
                MemberName = memberName
            });

        public static AbstractExpression NOT(AbstractExpression value)
            => PushExpression(new UnaryExpression {
                Type = GetContextOrThrow().FindType<bool>(),
                Operator = UnaryOperator.LogicalNot,
                Operand = PopExpression(value) 
            });

        public static AbstractExpression NEW<T>(params object[] constructorArguments)
        {
            //PopArguments

            var context = GetContextOrThrow();
            var type = context.FindType<T>();
            
            return PushExpression(new NewObjectExpression {
                Type = type,
                ConstructorCall = new MethodCallExpression {
                    Type = type,
                    Arguments = constructorArguments
                        .Select(value => new Argument {
                            Expression = PopExpression(context.GetConstantExpression(value))
                        })
                        .ToList()
                }
            });
        }

        public static AbstractExpression NEW(TypeMember type, params object[] constructorArguments) => throw new NotImplementedException();

        public static AbstractExpression NEWARRAY(params AbstractExpression[] items)
        {
            return NEWARRAY(null, items);
        }

        public static AbstractExpression NEWARRAY(TypeMember elementType, params AbstractExpression[] items) 
        {
            var context = GetContextOrThrow();
            
            return PushExpression(new NewArrayExpression {
                //TODO: populate Type
                ElementType = elementType,
                Length = AbstractExpression.FromValue(items.Length),
                DimensionInitializerValues = new List<List<AbstractExpression>> {
                    items.Select(PopExpression).ToList()
                }
            });
        }
        
        public static ObjectInitializerExpression INITOBJECT(params (string key, AbstractExpression value)[] initializers)
            => PushExpression(new ObjectInitializerExpression {
                PropertyValues = initializers.Select(init => 
                    new NamedPropertyValue(init.key, PopExpression(init.value))
                ).ToList() 
            });

        public static ObjectInitializerExpression INITOBJECT(Action body)
        {
            var initializerContext = new ObjectInitializerContext();

            using (GetContextOrThrow().PushState(initializerContext))
            {
                body?.Invoke();
            }

            return PushExpression(new ObjectInitializerExpression {
                PropertyValues = initializerContext.PropertyValues
            });
        }

        public static void KEY(string name, AbstractExpression value)
        {
            var initializerContext = GetContextOrThrow().PeekStateOrThrow<ObjectInitializerContext>();
            initializerContext.Add(name, PopExpression(value));
        }

        public static InterpolatedStringExpression INTERPOLATE(params object[] parts)
        {
            var expressionParts = parts.Select(CreatePart);

            return new InterpolatedStringExpression {
                Parts = expressionParts.ToList()
            };

            InterpolatedStringExpression.Part CreatePart(object obj)
            {
                switch (obj)
                {
                    case string text:
                        return new InterpolatedStringExpression.TextPart {
                            Text = text
                        };
                    case AbstractExpression expression:
                        return new InterpolatedStringExpression.InterpolationPart {
                            Value = PopExpression(expression)
                        };
                    default:
                        throw new ArgumentException($"Unexpected element for interpolated string.", nameof(parts));
                }
            }
        }
        
        public static void IIF(AbstractExpression condition, AbstractExpression whenTrue, AbstractExpression whenFalse)
        {
            PushExpression(new ConditionalExpression {
                Condition = PopExpression(condition),
                WhenTrue = PopExpression(whenTrue),
                WhenFalse = PopExpression(whenFalse)
            });
        }

        public static XmlExpression XML(XElement xml)
            => PushExpression(new XmlExpression {
                Xml = xml
            });
        
        public static NullExpression NULL
            => new NullExpression();
        
        public static AbstractExpression ASSIGN(this AbstractExpression target, AbstractExpression value) 
            => PushExpression(new AssignmentExpression {
                Left =(IAssignable)PopExpression(target),
                Right = PopExpression(value) 
            });

        public static AbstractExpression ASSIGN(this MemberExpression member, AbstractExpression value) 
            => PushExpression(new AssignmentExpression {
                Left = member,
                Right = PopExpression(value) 
            });

        public static AbstractExpression ASSIGN(this FieldMember target, AbstractExpression value)
            => PushExpression(new AssignmentExpression {
                Left = target.AsThisMemberExpression(),
                Right = PopExpression(value) 
            });

        public static AbstractExpression ASSIGN(this LocalVariable target, AbstractExpression value)
            => PushExpression(new AssignmentExpression {
                Left = target,
                Right = PopExpression(value) 
            });

        public static AbstractExpression EQ(this AbstractExpression left, AbstractExpression right)
            => PushExpression(new BinaryExpression {
                Left = PopExpression(left),
                Right = PopExpression(right),
                Operator = BinaryOperator.Equal
            });
        
        public static AbstractExpression INVOKE(this AbstractExpression expression, params AbstractExpression[] arguments)
            => INVOKE(expression, arguments.Select(arg => new Argument {
                Expression = arg 
            }));

        public static AbstractExpression INVOKE(this AbstractExpression expression, IEnumerable<Argument> arguments)
        {
            PopExpression(expression);
            
            if (expression is MemberExpression memberExpression)
            {
                var target = memberExpression.Target;
                var method = memberExpression.Member as MethodMember;

                return PushExpression(new MethodCallExpression {
                    Target = target,
                    Method = method,
                    MethodName = memberExpression.MemberName,
                    Type = method?.ReturnType,
                    Arguments = PopArguments().ToList(),
                });
            }

            return PushExpression(new DelegateInvocationExpression {
                Delegate = expression,
                Type = expression.Type,
                Arguments = PopArguments().ToList()
            });

            IEnumerable<Argument> PopArguments() => 
                arguments.Select(arg => {
                    PopExpression(arg.Expression);
                    return arg;
                });
        }

        public static AbstractExpression INVOKE(this AbstractExpression expression, Action body)
        {
            var invocation = new InvocationContext();

            using (GetContextOrThrow().PushState(invocation))
            {
                body?.Invoke();
            }

            return INVOKE(expression, invocation.GetArguments());
        }

        public static FluentAsyncLambda ASYNC => new FluentAsyncLambda();
        
        public static AnonymousDelegateExpression LAMBDA(Action bodyNoArgs)
            => PushExpression(CreateAnonymousDelegate(
                bodyNoArgs,
                parameters => bodyNoArgs?.Invoke()));

        public static AnonymousDelegateExpression LAMBDA(Action<MethodParameter> body1Arg)
            => CreateAnonymousDelegate(
                body1Arg,
                parameters => body1Arg?.Invoke(parameters[0]));

        public static AnonymousDelegateExpression LAMBDA(Action<MethodParameter, MethodParameter> body2Args)
            => CreateAnonymousDelegate(
                body2Args,
                parameters => body2Args?.Invoke(parameters[0], parameters[1]));

        public static AnonymousDelegateExpression LAMBDA(Action<MethodParameter, MethodParameter, MethodParameter> body3Args)
            => CreateAnonymousDelegate(
                body3Args,
                parameters => body3Args?.Invoke(parameters[0], parameters[1], parameters[2]));

        public static AbstractExpression TYPED(object value)
        {
            var context = GetContextOrThrow();

            return PushExpression(AbstractExpression.FromValue(value, resolveType: type => {
                if (context.TryFindMember<TypeMember>(type, out var typeRef))
                {
                    return typeRef;
                }
                return null;
            }));
        }

        public static AbstractExpression ANY(object value)
        {
            return PushExpression(AbstractExpression.FromValue(value, resolveType: t => null));
        }

        internal static T PushExpression<T>(T expression) 
            where T : AbstractExpression
        {
            return BlockContext.Push(expression);
        }
        
        internal static AbstractExpression PopExpression(AbstractExpression expression)
        {
            if (expression == null)
            {
                return null;
            }

            return BlockContext.Pop(expression);
        }

        internal static TupleExpression MakeTuple(string[] names, TypeMember[] types, out LocalVariable[] variables)
        {
            variables = names.Select((name, index) => new LocalVariable {
                Name = name,
                Type = types?[index]
            }).ToArray();

            return new TupleExpression(variables);
        }

        internal static AnonymousDelegateExpression CreateAnonymousDelegate(Delegate body, Action<MethodParameter[]> invokeBody, bool isAsync = false)
        {
            var context = GetContextOrThrow();
            
            var parameters = body.Method.GetParameters().Select((info, index) => new MethodParameter {
                Position = index,
                Name = info.Name,
                Type = context.FindType(info.ParameterType)
            }).ToArray();

            var lambda = new AnonymousDelegateExpression {
                Signature = new MethodSignature(),
                Body = new BlockStatement()
            };

            lambda.Signature.IsAsync = isAsync;
            lambda.Signature.Parameters.AddRange(parameters);

            using (context.PushState(lambda))
            {
                using (context.PushState(new BlockContext(lambda.Body)))
                {
                    invokeBody(parameters);
                }
            }

            return lambda;
        }
    }
}
