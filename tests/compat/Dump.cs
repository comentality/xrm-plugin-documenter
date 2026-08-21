//------------------------------------------------------------------------------
// The probe compat.ps1 builds twice: once against the real XrmTools.Meta.Attributes
// package, once against the XrmToolsMetaAttributes.cs this tool writes. Both builds
// compile the same generated corpus, and both print the same three sections. If the
// two printouts differ, the definitions file is not the package's equal and a project
// that swapped one for the other would behave differently.
//
// Nothing here knows what the corpus contains. It reads whatever ended up in namespace
// Corpus, which is exactly what AttributeEmitter produced on this run.
//------------------------------------------------------------------------------

using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Text;

internal static class Dump
{
    private static void Main()
    {
        var output = new StringBuilder();
        var assembly = typeof(Dump).Assembly;

        // DECLARED: which constructor the compiler bound each attribute to, with the
        // arguments as written. Proves the emitted syntax picked the overload it meant
        // to, and that [Image] still follows its [Step] once the compiler is done.
        output.AppendLine("== DECLARED");
        foreach (var type in Corpus(assembly))
        {
            output.AppendLine(type.Name);
            foreach (var data in type.GetCustomAttributesData().Where(d => IsOurs(d.AttributeType)))
            {
                output.AppendLine("    " + Declared(data));
            }
        }

        // EVALUATED: the attribute objects the runtime actually builds, every readable
        // property of them. This is where a default that drifted shows up - the emitter
        // leaves ExecutionOrder out when the rank is 1 because the attribute defaults it
        // to 1, and nothing but constructing one proves that is still true.
        output.AppendLine();
        output.AppendLine("== EVALUATED");
        foreach (var type in Corpus(assembly))
        {
            output.AppendLine(type.Name);
            foreach (var attribute in type.GetCustomAttributes(false).Where(a => IsOurs(a.GetType())))
            {
                output.AppendLine("    " + Evaluated(attribute));
            }
        }

        // SURFACE: every type the definitions bring into the compilation. Compared as a
        // report rather than an assertion - upstream growing a property is news, not a
        // failure, and the two sections above are what has to match.
        output.AppendLine();
        output.AppendLine("== SURFACE");
        foreach (var type in Surface(assembly))
        {
            output.AppendLine(Describe(type));
            foreach (var member in Members(type))
            {
                output.AppendLine("    " + member);
            }
        }

        Console.Out.Write(output.ToString());
    }

    private static IEnumerable<Type> Corpus(Assembly assembly)
    {
        return Types(assembly)
            .Where(t => t.Namespace == "Corpus")
            .OrderBy(t => t.Name, StringComparer.Ordinal);
    }

    private static IEnumerable<Type> Surface(Assembly assembly)
    {
        return Types(assembly)
            .Where(t => t.Namespace == "XrmTools.Meta.Attributes")
            .OrderBy(t => t.Name, StringComparer.Ordinal);
    }

    /// <summary>
    /// The package brings in types whose dependencies a bare probe does not reference,
    /// and one of those turns GetTypes() into an exception rather than a list. What
    /// loaded is still worth reading.
    /// </summary>
    private static IEnumerable<Type> Types(Assembly assembly)
    {
        try
        {
            return assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException e)
        {
            return e.Types.Where(t => t != null);
        }
    }

    private static bool IsOurs(Type type)
    {
        return type != null && type.Namespace == "XrmTools.Meta.Attributes";
    }

    private static string Declared(CustomAttributeData data)
    {
        var positional = data.ConstructorArguments.Select(a => Value(a.ArgumentType, a.Value));

        // Named arguments keep metadata order, which is the order they were written in.
        // Sorted here so the comparison is about the values, not about an ordering
        // neither the emitter nor the compiler promises.
        var named = data.NamedArguments
            .OrderBy(a => a.MemberName, StringComparer.Ordinal)
            .Select(a => a.MemberName + " = " + Value(a.TypedValue.ArgumentType, a.TypedValue.Value));

        return data.AttributeType.Name + "(" + string.Join(", ", positional.Concat(named)) + ")";
    }

    private static string Evaluated(object attribute)
    {
        var properties = attribute.GetType()
            .GetProperties(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
            .Where(p => p.CanRead && p.GetIndexParameters().Length == 0)
            .Where(p => p.DeclaringType != typeof(Attribute))
            .OrderBy(p => p.Name, StringComparer.Ordinal)
            .Select(p => p.Name + " = " + Read(attribute, p));

        return attribute.GetType().Name + " { " + string.Join(", ", properties) + " }";
    }

    private static string Read(object attribute, PropertyInfo property)
    {
        try
        {
            return Value(property.PropertyType, property.GetValue(attribute, null));
        }
        catch (TargetInvocationException e)
        {
            return "<threw " + e.InnerException.GetType().Name + ">";
        }
    }

    /// <summary>
    /// Enums are rendered as their number rather than their member name on purpose. The
    /// two definitions do not have to agree on what stage 50 is called for a step
    /// registered at stage 50 to mean the same thing, and the emitter writes that one as
    /// a cast for exactly that reason. A name that differs belongs in the surface report.
    /// </summary>
    private static string Value(Type declared, object value)
    {
        if (value == null)
        {
            return "null";
        }

        var type = Nullable.GetUnderlyingType(declared) ?? declared;
        if (type.IsEnum || (value.GetType().IsEnum))
        {
            return type.Name + ":" + Convert.ToInt64(value, CultureInfo.InvariantCulture)
                .ToString(CultureInfo.InvariantCulture);
        }

        if (value is string text)
        {
            return Quote(text);
        }

        if (value is bool flag)
        {
            return flag ? "true" : "false";
        }

        if (value is IEnumerable list && !(value is string))
        {
            return "[" + string.Join(", ", list.Cast<object>().Select(i => Value(i?.GetType(), i))) + "]";
        }

        return Convert.ToString(value, CultureInfo.InvariantCulture);
    }

    private static string Quote(string text)
    {
        var sb = new StringBuilder("\"");
        foreach (var c in text)
        {
            switch (c)
            {
                case '\\': sb.Append("\\\\"); break;
                case '"': sb.Append("\\\""); break;
                case '\r': sb.Append("\\r"); break;
                case '\n': sb.Append("\\n"); break;
                case '\t': sb.Append("\\t"); break;
                default: sb.Append(c); break;
            }
        }

        return sb.Append('"').ToString();
    }

    private static string Describe(Type type)
    {
        var accessibility = type.IsPublic || type.IsNestedPublic ? "public" : "internal";
        var kind = type.IsEnum ? "enum" : type.IsInterface ? "interface" : "class";
        return accessibility + " " + kind + " " + type.Name;
    }

    private static IEnumerable<string> Members(Type type)
    {
        if (type.IsEnum)
        {
            return Enum.GetValues(type)
                .Cast<object>()
                .Select(v => new
                {
                    Name = Enum.GetName(type, v),
                    Number = Convert.ToInt64(v, CultureInfo.InvariantCulture)
                })
                .OrderBy(m => m.Number)
                .ThenBy(m => m.Name, StringComparer.Ordinal)
                .Select(m => m.Name + " = " + m.Number.ToString(CultureInfo.InvariantCulture));
        }

        var constructors = type
            .GetConstructors(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
            .Select(c => ".ctor(" + string.Join(", ", c.GetParameters().Select(p => Name(p.ParameterType))) + ")");

        var properties = type
            .GetProperties(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
            .Where(p => p.DeclaringType == type)
            .Select(p => Name(p.PropertyType) + " " + p.Name
                         + " { " + (p.CanRead ? "get; " : "") + (p.CanWrite ? "set; " : "") + "}");

        return constructors.OrderBy(c => c, StringComparer.Ordinal)
            .Concat(properties.OrderBy(p => p, StringComparer.Ordinal));
    }

    private static string Name(Type type)
    {
        var underlying = Nullable.GetUnderlyingType(type);
        return underlying != null ? Name(underlying) + "?" : type.Name;
    }
}
