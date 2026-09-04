using System.Reflection;

namespace QuotesApi.Tests.Architecture;

// Lightweight, hand-rolled architecture tests (no NetArchTest/ArchUnitNET
// dependency — the project doesn't need one for four namespace-shaped
// checks). Everything here compiles into a single QuotesApi.dll, as a
// modular monolith should; these tests are what actually enforces the
// module boundaries that C# access modifiers alone cannot, by scanning
// each module's public/private member signatures (base types, interfaces,
// fields, properties, method parameters/return types, constructor
// parameters) for references into namespaces it must not touch.
public class ModuleBoundaryTests
{
    private static readonly Assembly QuotesApiAssembly = typeof(Program).Assembly;

    [Fact]
    public void Domain_DoesNotDependOn_EntityFrameworkCore()
    {
        AssertNoNamespaceDependency(
            "QuotesApi.Modules.Collections.Domain",
            "Microsoft.EntityFrameworkCore");
    }

    [Fact]
    public void Domain_DoesNotDependOn_AspNetCoreOrHttp()
    {
        AssertNoNamespaceDependency(
            "QuotesApi.Modules.Collections.Domain",
            "Microsoft.AspNetCore",
            "System.Net.Http");
    }

    [Fact]
    public void Domain_DoesNotDependOn_ResilienceOrMessagingInfrastructure()
    {
        AssertNoNamespaceDependency(
            "QuotesApi.Modules.Collections.Domain",
            "Polly",
            "StackExchange.Redis",
            "Azure.Messaging.ServiceBus",
            "Microsoft.Data.Sqlite");
    }

    [Fact]
    public void Domain_DoesNotDependOnOuterLayersOfItsOwnModule()
    {
        // The dependency direction is Presentation -> Application -> Domain.
        // Domain must not reach back out to its own module's Application,
        // Infrastructure, Presentation, or even its own Contracts layer.
        AssertNoNamespaceDependency(
            "QuotesApi.Modules.Collections.Domain",
            "QuotesApi.Modules.Collections.Application",
            "QuotesApi.Modules.Collections.Infrastructure",
            "QuotesApi.Modules.Collections.Presentation",
            "QuotesApi.Modules.Collections.Contracts");
    }

    [Fact]
    public void Collections_DoesNotDependOn_QuotesRepositoryOrEntityDirectly()
    {
        // Collections must call through Modules.Quotes.Contracts.IQuoteCatalog
        // only — never QuotesApi.Repositories.IQuoteRepository, and never the
        // QuotesApi.Models.Quote entity itself.
        AssertNoNamespaceDependency(
            "QuotesApi.Modules.Collections",
            "QuotesApi.Repositories");

        AssertNoTypeDependency(
            "QuotesApi.Modules.Collections",
            "QuotesApi.Models.Quote",
            "QuotesApi.Models.QuoteCreationResult");
    }

    [Fact]
    public void Notifications_DoesNotDependOn_CollectionsDomainApplicationOrInfrastructure()
    {
        // Notifications may depend on Collections.Contracts (the public
        // integration events) and nothing else belonging to Collections.
        AssertNoNamespaceDependency(
            "QuotesApi.Modules.Notifications",
            "QuotesApi.Modules.Collections.Domain",
            "QuotesApi.Modules.Collections.Application",
            "QuotesApi.Modules.Collections.Infrastructure",
            "QuotesApi.Modules.Collections.Presentation");
    }

    [Fact]
    public void Notifications_ReactsThroughCollectionsContractsEvents()
    {
        // Positive control for the test above: Notifications really is wired
        // to Collections' public events, not accidentally decoupled from
        // everything.
        var referencesCollectionsContracts = TypesInNamespace("QuotesApi.Modules.Notifications")
            .SelectMany(ReferencedTypes)
            .SelectMany(Unwrap)
            .Any(t => InNamespace(t, "QuotesApi.Modules.Collections.Contracts"));

        Assert.True(
            referencesCollectionsContracts,
            "Expected at least one Notifications type to depend on QuotesApi.Modules.Collections.Contracts.");
    }

    [Fact]
    public void CollectionsContracts_DoNotExposeDomainOrInfrastructureTypes()
    {
        // The DTOs/events other modules (and Presentation) are handed back
        // must never leak the EF-mapped aggregate or its persistence.
        AssertNoNamespaceDependency(
            "QuotesApi.Modules.Collections.Contracts",
            "QuotesApi.Modules.Collections.Domain",
            "QuotesApi.Modules.Collections.Infrastructure");
    }

    [Fact]
    public void CollectionsPresentation_DoesNotDependOnDomainOrInfrastructureDirectly()
    {
        // Presentation only talks to Application command/query handlers —
        // it never constructs or inspects the aggregate or the EF repository
        // itself.
        AssertNoNamespaceDependency(
            "QuotesApi.Modules.Collections.Presentation",
            "QuotesApi.Modules.Collections.Domain",
            "QuotesApi.Modules.Collections.Infrastructure");
    }

    // ----------------------------------------------------------------
    // Reflection helpers
    // ----------------------------------------------------------------

    private static void AssertNoNamespaceDependency(string fromNamespace, params string[] forbiddenNamespacePrefixes)
    {
        var offenders = TypesInNamespace(fromNamespace)
            .SelectMany(type => ReferencedTypes(type).SelectMany(Unwrap).Select(referenced => (type, referenced)))
            .Where(pair => forbiddenNamespacePrefixes.Any(forbidden => InNamespace(pair.referenced, forbidden)))
            .Select(pair => $"{pair.type.FullName} -> {pair.referenced.FullName}")
            .Distinct()
            .ToList();

        Assert.True(
            offenders.Count == 0,
            $"'{fromNamespace}' must not depend on [{string.Join(", ", forbiddenNamespacePrefixes)}], but found:\n" +
            string.Join("\n", offenders));
    }

    private static void AssertNoTypeDependency(string fromNamespace, params string[] forbiddenFullTypeNames)
    {
        var offenders = TypesInNamespace(fromNamespace)
            .SelectMany(type => ReferencedTypes(type).SelectMany(Unwrap).Select(referenced => (type, referenced)))
            .Where(pair => forbiddenFullTypeNames.Contains(pair.referenced.FullName))
            .Select(pair => $"{pair.type.FullName} -> {pair.referenced.FullName}")
            .Distinct()
            .ToList();

        Assert.True(
            offenders.Count == 0,
            $"'{fromNamespace}' must not depend on [{string.Join(", ", forbiddenFullTypeNames)}], but found:\n" +
            string.Join("\n", offenders));
    }

    private static bool InNamespace(Type type, string prefix) =>
        type.Namespace is not null && (type.Namespace == prefix || type.Namespace.StartsWith(prefix + "."));

    private static IEnumerable<Type> TypesInNamespace(string prefix) =>
        QuotesApiAssembly.GetTypes().Where(t => InNamespace(t, prefix));

    // Every referenced type a member signature can mention: base type,
    // implemented interfaces, field types, property types, and
    // constructor/method parameter + return types.
    private static IEnumerable<Type> ReferencedTypes(Type type)
    {
        const BindingFlags flags =
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance |
            BindingFlags.Static | BindingFlags.DeclaredOnly;

        if (type.BaseType is not null)
            yield return type.BaseType;

        foreach (var i in type.GetInterfaces())
            yield return i;

        foreach (var field in type.GetFields(flags))
            yield return field.FieldType;

        foreach (var property in type.GetProperties(flags))
            yield return property.PropertyType;

        foreach (var method in type.GetMethods(flags))
        {
            yield return method.ReturnType;

            foreach (var parameter in method.GetParameters())
                yield return parameter.ParameterType;
        }

        foreach (var ctor in type.GetConstructors(flags))
        foreach (var parameter in ctor.GetParameters())
            yield return parameter.ParameterType;
    }

    // Unwraps generic arguments and array element types (e.g.
    // Task<CollectionDto>, IReadOnlyList<QuoteMembershipDto>) so a forbidden
    // type hidden inside a generic still gets caught.
    private static IEnumerable<Type> Unwrap(Type type)
    {
        yield return type;

        if (type.IsGenericType)
        {
            foreach (var argument in type.GetGenericArguments())
            foreach (var nested in Unwrap(argument))
                yield return nested;
        }

        if (type.IsArray && type.GetElementType() is { } elementType)
        {
            foreach (var nested in Unwrap(elementType))
                yield return nested;
        }
    }
}
