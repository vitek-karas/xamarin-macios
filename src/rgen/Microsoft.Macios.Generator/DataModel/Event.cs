using System;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.Macios.Generator.Extensions;

namespace Microsoft.Macios.Generator.DataModel;

readonly struct Event : IEquatable<Event> {

	/// <summary>
	/// Name of the property.
	/// </summary>
	public string Name { get; } = string.Empty;

	/// <summary>
	/// String representation of the event type.
	/// </summary>
	public string Type { get; } = string.Empty;

	/// <summary>
	/// Get the attributes added to the member.
	/// </summary>
	public ImmutableArray<AttributeCodeChange> Attributes { get; } = [];

	/// <summary>
	/// Get the modifiers of the event.
	/// </summary>
	public ImmutableArray<SyntaxToken> Modifiers { get; } = [];

	/// <summary>
	/// Get the list of accessor changes of the event.
	/// </summary>
	public ImmutableArray<Accessor> Accessors { get; } = [];

	internal Event (string name, string type, ImmutableArray<AttributeCodeChange> attributes,
		ImmutableArray<SyntaxToken> modifiers, ImmutableArray<Accessor> accessors)
	{
		Name = name;
		Type = type;
		Attributes = attributes;
		Modifiers = modifiers;
		Accessors = accessors;
	}

	/// <inheritdoc />
	public bool Equals (Event other)
	{
		// this could be a large && but ifs are more readable
		if (Name != other.Name)
			return false;
		if (Type != other.Type)
			return false;
		var attrsComparer = new AttributesEqualityComparer ();
		if (!attrsComparer.Equals (Attributes, other.Attributes))
			return false;

		var modifiersComparer = new ModifiersComparer ();
		if (!modifiersComparer.Equals (Modifiers, other.Modifiers))
			return false;

		var accessorComparer = new AccessorsEqualityComparer ();
		return accessorComparer.Equals (Accessors, other.Accessors);
	}

	/// <inheritdoc />
	public override bool Equals (object? obj)
	{
		return obj is Event other && Equals (other);
	}

	/// <inheritdoc />
	public override int GetHashCode ()
	{
		return HashCode.Combine (Name, Type, Attributes, Modifiers, Accessors);
	}

	public static bool operator == (Event left, Event right)
	{
		return left.Equals (right);
	}

	public static bool operator != (Event left, Event right)
	{
		return !left.Equals (right);
	}

	public static bool TryCreate (EventDeclarationSyntax declaration, SemanticModel semanticModel,
		[NotNullWhen (true)] out Event? change)
	{
		var memberName = declaration.Identifier.ToFullString ().Trim ();
		// get the symbol from the property declaration
		if (semanticModel.GetDeclaredSymbol (declaration) is not IEventSymbol eventSymbol) {
			change = null;
			return false;
		}

		var type = eventSymbol.Type.ToDisplayString ().Trim ();
		var attributes = declaration.GetAttributeCodeChanges (semanticModel);
		ImmutableArray<Accessor> accessorCodeChanges = [];
		if (declaration.AccessorList is not null && declaration.AccessorList.Accessors.Count > 0) {
			// calculate any possible changes in the accessors of the property
			var accessorsBucket = ImmutableArray.CreateBuilder<Accessor> ();
			foreach (var accessor in declaration.AccessorList.Accessors) {
				var kind = accessor.Kind ().ToAccessorKind ();
				var accessorAttributeChanges = accessor.GetAttributeCodeChanges (semanticModel);
				accessorsBucket.Add (new (kind, accessorAttributeChanges, [.. accessor.Modifiers]));
			}

			accessorCodeChanges = accessorsBucket.ToImmutable ();
		}

		change = new (memberName, type, attributes, [.. declaration.Modifiers], accessorCodeChanges);
		return true;
	}

	/// <inheritdoc />
	public override string ToString ()
	{
		var sb = new StringBuilder ($"Name: {Name}, Type: {Type}, Attributes: [");
		sb.AppendJoin (",", Attributes);
		sb.Append ("], Modifiers: [");
		sb.AppendJoin (",", Modifiers.Select (x => x.Text));
		sb.Append ("], Accessors: [");
		sb.AppendJoin (",", Accessors);
		sb.Append (']');
		return sb.ToString ();
	}
}