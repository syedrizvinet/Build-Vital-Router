using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace VitalRouter.SourceGenerator;

class TypeMeta
{
    public TypeDeclarationSyntax Syntax { get; }
    public INamedTypeSymbol Symbol { get; }
    public AttributeData RoutingAttribute { get; }
    public string TypeName { get; }
    public string FullTypeName { get; }

    public InterceptorMeta[] DefaultInterceptorMetas { get; }
    public IReadOnlyList<InterceptorMeta> AllInterceptorMetas { get; }
    public IReadOnlyList<RouteMethodMeta> RouteMethodMetas => routeMethodMetas;
    public IReadOnlyList<IMethodSymbol> NonRoutableMethodSymbols => nonRoutableMethodSymbols;

    readonly ReferenceSymbols references;
    readonly List<RouteMethodMeta> routeMethodMetas = [];
    readonly List<IMethodSymbol> nonRoutableMethodSymbols = [];

    public TypeMeta(
        TypeDeclarationSyntax syntax,
        INamedTypeSymbol symbol,
        AttributeData routingAttribute,
        ReferenceSymbols references)
    {
        Syntax = syntax;
        Symbol = symbol;
        this.references = references;

        TypeName = symbol.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat);
        FullTypeName = symbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

        RoutingAttribute = routingAttribute;

        var interceptorMetas = new List<InterceptorMeta>();
        foreach (var attr in symbol.GetAttributes())
        {
            if (SymbolEqualityComparer.Default.Equals(attr.AttributeClass, references.FilterAttribute) &&
                attr.ConstructorArguments is [{ Kind: TypedConstantKind.Type }, ..])
            {
                interceptorMetas.Add(new InterceptorMeta(attr, (INamedTypeSymbol)attr.ConstructorArguments[0].Value!));
            }
            else if (attr.AttributeClass is { IsGenericType: true } x && SymbolEqualityComparer.Default.Equals(x.ConstructedFrom, references.FilterAttributeGeneric))
            {
                var filterType = attr.AttributeClass!.TypeArguments[0];
                interceptorMetas.Add(new InterceptorMeta(attr, (INamedTypeSymbol)filterType));
            }
        }

        DefaultInterceptorMetas = interceptorMetas.ToArray();

        CollectMembers();

        AllInterceptorMetas = DefaultInterceptorMetas
            .Concat(RouteMethodMetas.SelectMany(x => x.InterceptorMetas))
            .Distinct(InterceptorMetaEqualityComparer.Instance)
            .ToArray();
    }

    public bool IsPartial()
    {
        return Syntax.Modifiers.Any(m => m.IsKind(SyntaxKind.PartialKeyword));
    }

    public bool IsNested()
    {
        return Syntax.Parent is TypeDeclarationSyntax;
    }

    void CollectMembers()
    {
        var i = 0;
        foreach (var member in Symbol.GetAllMembers())
        {
            if (member is IMethodSymbol { IsStatic: false } method)
            {
                var hasRouteAttribute = method.ContainsAttribute(references.RouteAttribute);
                if (method.DeclaredAccessibility != Accessibility.Public && !hasRouteAttribute)
                    continue;

                if (method.DeclaredAccessibility != Accessibility.Public &&
                    !method.ContainsAttribute(references.RouteAttribute))
                    continue;

                if (method.Parameters.Length is <= 0 or >= 3)
                    continue;

                IParameterSymbol? commandParam = null;
                IParameterSymbol? cancellationTokenParam = null;
                IParameterSymbol? publishContextParam = null;

                foreach (var param in method.Parameters)
                {
                    if (param.Type.AllInterfaces.Any(x => SymbolEqualityComparer.Default.Equals(x, references.CommandInterface)))
                    {
                        commandParam = param;
                    }
                    else if (SymbolEqualityComparer.Default.Equals(param.Type, references.CancellationTokenType))
                    {
                        cancellationTokenParam = param;
                    }
                    else if (SymbolEqualityComparer.Default.Equals(param.Type, references.PublishContextType))
                    {
                        publishContextParam = param;
                    }
                }

                if (commandParam is null)
                {
                    continue;
                }
                if (method.Parameters.Length >= 2 && cancellationTokenParam is null && publishContextParam is null)
                {
                    continue;
                }

                // sync
                if (method is { ReturnsVoid: true })
                {
                    routeMethodMetas.Add(new RouteMethodMeta(method, commandParam, cancellationTokenParam, publishContextParam, i++, references));
                }
                // async
                else if (SymbolEqualityComparer.Default.Equals(method.ReturnType, references.UniTaskType) ||
                         SymbolEqualityComparer.Default.Equals(method.ReturnType, references.AwaitableType) ||
                         SymbolEqualityComparer.Default.Equals(method.ReturnType, references.TaskType) ||
                         SymbolEqualityComparer.Default.Equals(method.ReturnType, references.ValueTaskType))
                {
                    routeMethodMetas.Add(new RouteMethodMeta(method, commandParam, cancellationTokenParam, publishContextParam, i++, references));
                }
                // not routable
                else
                {
                    if (SymbolEqualityComparer.Default.Equals(method.ContainingType, Symbol))
                    {
                        nonRoutableMethodSymbols.Add(method);
                    }
                }
            }
        }
    }
}