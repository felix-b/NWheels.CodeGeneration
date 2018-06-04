﻿using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using MetaPrograms.CodeModel.Imperative.Expressions;
using MetaPrograms.CodeModel.Imperative.Members;

namespace MetaPrograms.CodeModel.Imperative
{
    public class CodeGeneratorContext : IDisposable
    {
        private static readonly AsyncLocal<CodeGeneratorContext> Current = new AsyncLocal<CodeGeneratorContext>();

        private readonly IClrTypeResolver _clrTypeResolver;
        private readonly Stack<object> _stateStack = new Stack<object>();
        private readonly List<IMemberRef> _generatedMembers = new List<IMemberRef>();
        
        public CodeGeneratorContext(ImperativeCodeModel codeModel, IClrTypeResolver clrTypeResolver)
        {
            if (Current.Value != null)
            {
                throw new InvalidOperationException(
                    "Another instance of CodeGeneratorContext is already associated with the current call context.");
            }

            _clrTypeResolver = clrTypeResolver;

            Current.Value = this;
            this.CodeModel = codeModel;
        }

        public void Dispose()
        {
            if (Current.Value == this)
            {
                Current.Value = null;
            }
        }

        public IDisposable PushState(object state)
        {
            if (state != null)
            {
                return new StackStateScope(_stateStack, state, pop: () => PopStateOrThrow(state.GetType()));
            }

            return null;
        }

        public TState PopStateOrThrow<TState>()
        {
            return (TState)PopStateOrThrow(typeof(TState));
        }

        public TState PeekStateOrThrow<TState>()
        {
            return (TState)PeekStateOrThrow(typeof(TState));
        }

        public bool TryPeekState<TState>(out TState state)
        {
            if (_stateStack.Count > 0 && _stateStack.Peek() is TState foundState)
            {
                state = foundState;
                return true;
            }

            state = default;
            return false;
        }

        public TState TryLookupState<TState>()
        {
            return _stateStack.OfType<TState>().FirstOrDefault();
        }

        public TState LookupStateOrThrow<TState>()
        {
            var state = TryLookupState<TState>();

            if (state != null)
            {
                return state;
            }

            throw new InvalidOperationException($"Could not find a {typeof(TState).Name} down the state stack.");
        }

        public TypeMember TryGetCurrentType()
        {
            return TryLookupState<MemberRef<TypeMember>>().Get();
        }

        public TypeMember GetCurrentType()
        {
            return LookupStateOrThrow<MemberRef<TypeMember>>().Get();
        }

        public TypeMemberBuilder GetCurrentTypeBuilder()
        {
            return LookupStateOrThrow<TypeMemberBuilder>();
        }

        public AbstractMember GetCurrentMember()
        {
            return LookupStateOrThrow<IMemberRef>().Get();
        }

        public void AddGeneratedMember<TMember>(MemberRef<TMember> member, bool isTopLevel)
            where TMember : AbstractMember
        {
            _generatedMembers.Add(member);
            this.CodeModel.Add(member, isTopLevel);
        }

        public bool TryFindMember<TMember>(object binding, out MemberRef<TMember> memberRef)
            where TMember : AbstractMember
        {
            if (CodeModel.MembersByBndings.TryGetValue(binding, out var untypedMemberRef))
            {
                memberRef = untypedMemberRef.AsRef<TMember>();
                return true;
            }

            memberRef = default;
            return false;
        }

        public TMember FindMemberOrThrow<TMember>(object binding)
            where TMember : AbstractMember
        {
            if (TryFindMember<TMember>(binding, out var member))
            {
                return member;
            }
            
            throw new KeyNotFoundException($"Could not find '{typeof(TMember).Name}' with binding '{binding}'.");
        }

        public MemberRef<TypeMember> FindType<T>()
        {
            return FindType(typeof(T));
        }

        public MemberRef<TypeMember> FindType(Type clrType)
        {
            MemberRef<TypeMember> typeRef;

            if (TryFindMember(binding: clrType, out typeRef))
            {
                if (typeRef.Get().Status == MemberStatus.Incomplete)
                {
                    _clrTypeResolver.Complete(typeRef, this.CodeModel);
                }
            }
            else 
            {
                typeRef = _clrTypeResolver.Resolve(clrType, this.CodeModel, distance: 0);
            }

            return typeRef;
        }

        public AbstractExpression GetConstantExpression(object value)
        {
            return AbstractExpression.FromValue(value, resolveType: this.FindType);
        }

        public ImperativeCodeModel CodeModel { get; }
        public IEnumerable<IMemberRef> GeneratedMembers => _generatedMembers;

        private object PopStateOrThrow(Type stateType)
        {
            var state = PeekStateOrThrow(stateType);

            _stateStack.Pop();

            if (state is IDisposable disposable)
            {
                disposable.Dispose();
            }
            
            Debug.WriteLine($"CODE GENERATOR CONTEXT >> PopStateOrThrow >> POP {state.GetType().Name} >> COUNT = {_stateStack.Count}");

            return state;
        }

        private object PeekStateOrThrow(Type stateType)
        {
            if (_stateStack.Count == 0)
            {
                throw new InvalidOperationException(
                    $"Code generator state stack mismatch: attempted to pop a {stateType.Name}, but the stack is empty.");
            }

            var stateOnTop = _stateStack.Peek();
            
            if (!stateType.IsInstanceOfType(stateOnTop))
            {
                throw new InvalidOperationException(
                    $"Code generator state stack mismatch: attempted to pop a {stateType.Name}, " +
                    $"but the top item is a {stateOnTop.GetType().Name}'.");
            }

            return stateOnTop;
        }

        public static CodeGeneratorContext CurrentContext => Current.Value;

        public static CodeGeneratorContext GetContextOrThrow() => 
            CurrentContext ?? 
            throw new InvalidOperationException(
                "No CodeGeneratorContext exists in the current call context. " +
                "Code generation operations require a current CodeGeneratorContext. " +
                "Instantiate CodeGeneratorContext before any code generation operations, and Dispose it afterwards.");

        private class StackStateScope : IDisposable
        {
            private readonly Stack<object> _stateStack;
            private readonly object _state;
            private readonly Action _pop;

            public StackStateScope(Stack<object> stateStack, object state, Action pop)
            {
                _stateStack = stateStack;
                _state = state;
                _pop = pop;
                _stateStack.Push(state);
                Debug.WriteLine($"CODE GENERATOR CONTEXT >> StackStateScope.ctor >> PUSH {_state.GetType().Name} >> COUNT = {_stateStack.Count}");
            }

            public void Dispose()
            {
                _pop();
                Debug.WriteLine($"CODE GENERATOR CONTEXT >> StackStateScope.Dispose >> POP {_state.GetType().Name} >> COUNT = {_stateStack.Count}");
            }
        }
    }
}