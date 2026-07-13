#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;

/// <summary>
/// Inspects compiled IL for the few wiring/negative contracts that cannot be exercised
/// without a live Fusion runner. This is intentionally preferable to source text:
/// formatting, comments, partial-file moves and local variable renames do not affect it.
/// </summary>
internal static class MdfCompiledCodePolicy
{
    private static readonly Dictionary<ushort, OpCode> OpCodesByValue = typeof(OpCodes)
        .GetFields(BindingFlags.Public | BindingFlags.Static)
        .Where(field => field.FieldType == typeof(OpCode))
        .Select(field => (OpCode)field.GetValue(null))
        .GroupBy(opCode => unchecked((ushort)opCode.Value))
        .ToDictionary(group => group.Key, group => group.First());

    internal static bool ReferencesMethod(Type rootType, Type declaringType, string methodName)
    {
        return EnumerateMethods(rootType).Any(method =>
            EnumerateReferencedMethods(method).Any(reference =>
                reference != null &&
                reference.DeclaringType == declaringType &&
                reference.Name == methodName));
    }

    internal static bool ReferencesMethod(MethodBase sourceMethod, Type declaringType, string methodName)
    {
        return EnumerateReferencedMethods(sourceMethod).Any(reference =>
            reference != null && reference.DeclaringType == declaringType && reference.Name == methodName);
    }

    internal static bool ReferencesAnyMethod(Type rootType, Type declaringType, params string[] methodNames)
    {
        return methodNames.Any(methodName => ReferencesMethod(rootType, declaringType, methodName));
    }

    internal static bool ContainsStringLiteral(Type rootType, string value)
    {
        return EnumerateMethods(rootType).Any(method => EnumerateStringLiterals(method).Contains(value));
    }

    internal static bool ContainsStringLiteral(MethodBase sourceMethod, string value)
    {
        return EnumerateStringLiterals(sourceMethod).Contains(value);
    }

    internal static bool ContainsStringLiteralFragment(MethodBase sourceMethod, string value)
    {
        return EnumerateStringLiterals(sourceMethod).Any(literal => literal != null && literal.Contains(value));
    }

    internal static bool ContainsStringLiteralFragment(Type rootType, string value)
    {
        return EnumerateMethods(rootType).Any(method =>
            EnumerateStringLiterals(method).Any(literal => literal != null && literal.Contains(value)));
    }

    internal static bool ReferencesField(Type rootType, Type declaringType, string fieldName)
    {
        return EnumerateMethods(rootType).Any(method =>
            EnumerateInstructions(method).Any(instruction =>
                instruction.Field != null &&
                instruction.Field.DeclaringType == declaringType &&
                instruction.Field.Name == fieldName));
    }

    internal static bool ReferencesField(MethodBase sourceMethod, Type declaringType, string fieldName)
    {
        return EnumerateInstructions(sourceMethod).Any(instruction =>
            instruction.Field != null &&
            instruction.Field.DeclaringType == declaringType &&
            instruction.Field.Name == fieldName);
    }

    internal static bool ContainsIsInstanceOf(Type rootType, Type targetType)
    {
        return EnumerateMethods(rootType).Any(method =>
            EnumerateInstructions(method).Any(instruction =>
                instruction.IsInstanceOfType == targetType));
    }

    internal static bool ContainsIsInstanceOf(MethodBase sourceMethod, Type targetType)
    {
        if (EnumerateInstructions(sourceMethod).Any(instruction => instruction.IsInstanceOfType == targetType))
        {
            return true;
        }

        var asyncStateMachine = sourceMethod.GetCustomAttribute<AsyncStateMachineAttribute>();
        MethodInfo moveNext = asyncStateMachine?.StateMachineType.GetMethod(
            "MoveNext",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        return moveNext != null &&
               EnumerateInstructions(moveNext).Any(instruction => instruction.IsInstanceOfType == targetType);
    }

    private static IEnumerable<MethodBase> EnumerateMethods(Type rootType)
    {
        const BindingFlags members = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
        foreach (Type type in EnumerateTypes(rootType))
        {
            foreach (MethodInfo method in type.GetMethods(members))
            {
                yield return method;
            }

            foreach (ConstructorInfo constructor in type.GetConstructors(members))
            {
                yield return constructor;
            }
        }
    }

    private static IEnumerable<Type> EnumerateTypes(Type type)
    {
        yield return type;
        foreach (Type nested in type.GetNestedTypes(BindingFlags.Public | BindingFlags.NonPublic))
        {
            foreach (Type descendant in EnumerateTypes(nested))
            {
                yield return descendant;
            }
        }
    }

    private static IEnumerable<MethodBase> EnumerateReferencedMethods(MethodBase method)
    {
        foreach (Instruction instruction in EnumerateInstructions(method))
        {
            if (instruction.Method != null)
            {
                yield return instruction.Method;
            }
        }
    }

    private static IEnumerable<string> EnumerateStringLiterals(MethodBase method)
    {
        foreach (Instruction instruction in EnumerateInstructions(method))
        {
            if (instruction.String != null)
            {
                yield return instruction.String;
            }
        }
    }

    private static IEnumerable<Instruction> EnumerateInstructions(MethodBase method)
    {
        MethodBody body;
        try
        {
            body = method.GetMethodBody();
        }
        catch
        {
            yield break;
        }

        byte[] bytes = body?.GetILAsByteArray();
        if (bytes == null)
        {
            yield break;
        }

        int position = 0;
        while (position < bytes.Length)
        {
            ushort value = bytes[position++];
            if (value == 0xFE && position < bytes.Length)
            {
                value = (ushort)(0xFE00 | bytes[position++]);
            }

            if (!OpCodesByValue.TryGetValue(value, out OpCode opCode))
            {
                yield break;
            }

            var instruction = new Instruction();
            int operandStart = position;
            int operandSize = GetOperandSize(opCode.OperandType, bytes, operandStart);
            if (operandStart + operandSize > bytes.Length)
            {
                yield break;
            }

            if (opCode.OperandType == OperandType.InlineString)
            {
                int token = BitConverter.ToInt32(bytes, operandStart);
                try
                {
                    instruction.String = method.Module.ResolveString(token);
                }
                catch
                {
                    // Invalid metadata should make the assertion fail naturally.
                }
            }
            else if (opCode.OperandType == OperandType.InlineMethod)
            {
                int token = BitConverter.ToInt32(bytes, operandStart);
                try
                {
                    Type[] typeArguments = method.DeclaringType?.IsGenericType == true
                        ? method.DeclaringType.GetGenericArguments()
                        : Type.EmptyTypes;
                    Type[] methodArguments = method.IsGenericMethod
                        ? method.GetGenericArguments()
                        : Type.EmptyTypes;
                    instruction.Method = method.Module.ResolveMethod(token, typeArguments, methodArguments);
                }
                catch
                {
                    // Invalid/unresolvable metadata is ignored and cannot create a false positive.
                }
            }
            else if (opCode.OperandType == OperandType.InlineField)
            {
                int token = BitConverter.ToInt32(bytes, operandStart);
                try
                {
                    Type[] typeArguments = method.DeclaringType?.IsGenericType == true
                        ? method.DeclaringType.GetGenericArguments()
                        : Type.EmptyTypes;
                    Type[] methodArguments = method.IsGenericMethod
                        ? method.GetGenericArguments()
                        : Type.EmptyTypes;
                    instruction.Field = method.Module.ResolveField(token, typeArguments, methodArguments);
                }
                catch
                {
                    // Invalid/unresolvable metadata is ignored and cannot create a false positive.
                }
            }
            else if (opCode == OpCodes.Isinst)
            {
                int token = BitConverter.ToInt32(bytes, operandStart);
                try
                {
                    Type[] typeArguments = method.DeclaringType?.IsGenericType == true
                        ? method.DeclaringType.GetGenericArguments()
                        : Type.EmptyTypes;
                    Type[] methodArguments = method.IsGenericMethod
                        ? method.GetGenericArguments()
                        : Type.EmptyTypes;
                    instruction.IsInstanceOfType = method.Module.ResolveType(token, typeArguments, methodArguments);
                }
                catch
                {
                    // Invalid/unresolvable metadata is ignored and cannot create a false positive.
                }
            }

            position += operandSize;
            yield return instruction;
        }
    }

    private static int GetOperandSize(OperandType operandType, byte[] bytes, int operandStart)
    {
        switch (operandType)
        {
            case OperandType.InlineNone:
                return 0;
            case OperandType.ShortInlineBrTarget:
            case OperandType.ShortInlineI:
            case OperandType.ShortInlineVar:
                return 1;
            case OperandType.InlineVar:
                return 2;
            case OperandType.InlineI:
            case OperandType.InlineBrTarget:
            case OperandType.InlineField:
            case OperandType.InlineMethod:
            case OperandType.InlineSig:
            case OperandType.InlineString:
            case OperandType.InlineTok:
            case OperandType.InlineType:
            case OperandType.ShortInlineR:
                return 4;
            case OperandType.InlineI8:
            case OperandType.InlineR:
                return 8;
            case OperandType.InlineSwitch:
                if (operandStart + 4 > bytes.Length)
                {
                    return bytes.Length;
                }

                int count = BitConverter.ToInt32(bytes, operandStart);
                return 4 + Math.Max(0, count) * 4;
            default:
                return 0;
        }
    }

    private sealed class Instruction
    {
        public MethodBase Method;
        public FieldInfo Field;
        public Type IsInstanceOfType;
        public string String;
    }
}
#endif
