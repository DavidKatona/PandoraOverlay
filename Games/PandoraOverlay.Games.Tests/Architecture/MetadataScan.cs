using System.Collections.Immutable;
using System.IO;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;

namespace PandoraOverlay.Games.Tests;

/// <summary>A type as the metadata names it: its namespace, and its name with any enclosing types first ("Environment+SpecialFolder").</summary>
internal readonly record struct TypeName(string Namespace, string Name)
{
    /// <summary>"System.IO.File", "System.Environment+SpecialFolder"; a type in no namespace is its name alone.</summary>
    public string FullName => Namespace.Length == 0 ? Name : Namespace + "." + Name;

    public override string ToString() => FullName;
}

/// <summary>A member another assembly is asked for: the full name of the type it is on, and its own name ("get_UtcNow", ".ctor").</summary>
internal readonly record struct MemberName(string Type, string Member)
{
    public override string ToString() => Type + "::" + Member;
}

/// <summary>What one built assembly reaches outside itself, as <see cref="MetadataScan.Read"/> found it.</summary>
/// <param name="Name">The assembly's own simple name.</param>
/// <param name="AssemblyReferences">The simple names of the assemblies it references.</param>
/// <param name="TypeReferences">Every type it names from another assembly.</param>
/// <param name="MemberReferences">Every method or field it calls or reads on a type from another assembly.</param>
/// <param name="HasPInvoke">It declares a native call: a method with an import (a <c>[DllImport]</c>, or the stub a <c>[LibraryImport]</c> generates), or any module reference.</param>
internal sealed record ScannedAssembly(
    string Name,
    IReadOnlyList<string> AssemblyReferences,
    IReadOnlyList<TypeName> TypeReferences,
    IReadOnlyList<MemberName> MemberReferences,
    bool HasPInvoke);

/// <summary>
/// Reads what a built assembly reaches for, from its metadata alone (A29):
/// the tables the compiler wrote, not reflection over loaded types. Nothing is
/// loaded or run, so a scanned game's code never executes, and a reference the
/// code only names (a <c>typeof</c>, a parameter's type, a method never called)
/// counts as much as a call: what the compiler had to write down is what the
/// assembly can do. <c>System.Reflection.Metadata</c> and
/// <c>System.Reflection.PortableExecutable</c> are in the framework: no package
/// (A29, and the games' "no package" rule, A2).
/// <para>
/// What the metadata does not hold is not seen: a type used only in a view's
/// XAML lives in the compiled BAML, an embedded resource, unless the XAML names
/// the element (a field) or wires an event. The source scans read the XAML
/// itself for what matters there (<see cref="SourceScanTests"/>).
/// </para>
/// </summary>
internal static class MetadataScan
{
    /// <summary>Opens the assembly at <paramref name="path"/> with a PE reader and lists its references. Throws when the file is not a .NET assembly: a scan that read nothing must not pass.</summary>
    public static ScannedAssembly Read(string path)
    {
        using var stream = File.OpenRead(path);
        using var pe = new PEReader(stream);
        var md = pe.GetMetadataReader();

        var name = md.GetString(md.GetAssemblyDefinition().Name);

        var assemblies = md.AssemblyReferences
            .Select(h => md.GetString(md.GetAssemblyReference(h).Name))
            .Distinct(StringComparer.Ordinal)
            .ToList();

        var types = md.TypeReferences
            .Select(h => NameOf(md, h))
            .Distinct()
            .ToList();

        var provider = new TypeNames();
        var members = md.MemberReferences
            .Select(h =>
            {
                var member = md.GetMemberReference(h);
                return new MemberName(ParentName(md, member.Parent, provider), md.GetString(member.Name));
            })
            .Distinct()
            .ToList();

        // A native call is a method marked as an import (PinvokeImpl: what
        // [DllImport] compiles to, the attribute itself leaving no row of its
        // own), or a reference to another module, which is what an import
        // names its library by.
        var pinvoke = md.GetTableRowCount(TableIndex.ModuleRef) > 0
            || md.MethodDefinitions.Any(h => (md.GetMethodDefinition(h).Attributes & MethodAttributes.PinvokeImpl) != 0);

        return new ScannedAssembly(name, assemblies, types, members, pinvoke);
    }

    /// <summary>A type reference's namespace and name; a nested one takes its outermost type's namespace and reads "Outer+Inner".</summary>
    private static TypeName NameOf(MetadataReader md, TypeReferenceHandle handle)
    {
        var reference = md.GetTypeReference(handle);
        var name = md.GetString(reference.Name);
        if (reference.ResolutionScope.Kind == HandleKind.TypeReference)
        {
            // Nested: the metadata gives it no namespace of its own; it is
            // "in" the type that encloses it.
            var outer = NameOf(md, (TypeReferenceHandle)reference.ResolutionScope);
            return new TypeName(outer.Namespace, outer.Name + "+" + name);
        }
        return new TypeName(md.GetString(reference.Namespace), name);
    }

    private static TypeName NameOf(MetadataReader md, TypeDefinitionHandle handle)
    {
        var definition = md.GetTypeDefinition(handle);
        var name = md.GetString(definition.Name);
        var declaring = definition.GetDeclaringType();
        if (!declaring.IsNil)
        {
            var outer = NameOf(md, declaring);
            return new TypeName(outer.Namespace, outer.Name + "+" + name);
        }
        return new TypeName(md.GetString(definition.Namespace), name);
    }

    /// <summary>
    /// The full name of the type a member reference is on. A generic type's
    /// member (<c>List&lt;int&gt;.Add</c>) is on a type specification: it reads
    /// as the generic type itself ("System.Collections.Generic.List`1"), whose
    /// arguments are type references of their own and are checked as such.
    /// </summary>
    private static string ParentName(MetadataReader md, EntityHandle parent, TypeNames provider) => parent.Kind switch
    {
        HandleKind.TypeReference => NameOf(md, (TypeReferenceHandle)parent).FullName,
        HandleKind.TypeDefinition => NameOf(md, (TypeDefinitionHandle)parent).FullName,
        HandleKind.TypeSpecification => md.GetTypeSpecification((TypeSpecificationHandle)parent).DecodeSignature(provider, null),
        // A vararg call site's parent is the method it calls: the member is on that method's type.
        HandleKind.MethodDefinition => NameOf(md, md.GetMethodDefinition((MethodDefinitionHandle)parent).GetDeclaringType()).FullName,
        // A global function in another module: no type at all (and a module reference is a native call above).
        _ => "",
    };

    /// <summary>Names the types in a signature, for <see cref="ParentName"/>: a generic instantiation is named by its generic type.</summary>
    private sealed class TypeNames : ISignatureTypeProvider<string, object?>
    {
        public string GetPrimitiveType(PrimitiveTypeCode typeCode) => "System." + typeCode;
        public string GetTypeFromDefinition(MetadataReader reader, TypeDefinitionHandle handle, byte rawTypeKind) => NameOf(reader, handle).FullName;
        public string GetTypeFromReference(MetadataReader reader, TypeReferenceHandle handle, byte rawTypeKind) => NameOf(reader, handle).FullName;
        public string GetTypeFromSpecification(MetadataReader reader, object? genericContext, TypeSpecificationHandle handle, byte rawTypeKind) =>
            reader.GetTypeSpecification(handle).DecodeSignature(this, genericContext);
        public string GetSZArrayType(string elementType) => elementType + "[]";
        public string GetArrayType(string elementType, ArrayShape shape) => elementType + "[" + new string(',', Math.Max(0, shape.Rank - 1)) + "]";
        public string GetByReferenceType(string elementType) => elementType + "&";
        public string GetPointerType(string elementType) => elementType + "*";
        public string GetPinnedType(string elementType) => elementType;
        public string GetModifiedType(string modifier, string unmodifiedType, bool isRequired) => unmodifiedType;
        public string GetGenericInstantiation(string genericType, ImmutableArray<string> typeArguments) => genericType;
        public string GetGenericTypeParameter(object? genericContext, int index) => "!" + index;
        public string GetGenericMethodParameter(object? genericContext, int index) => "!!" + index;
        public string GetFunctionPointerType(MethodSignature<string> signature) => "method*";
    }
}
