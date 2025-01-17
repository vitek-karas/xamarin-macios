using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml;

using NUnit.Framework;

using Mono.Cecil;
using Mono.Cecil.Cil;

using Xamarin.Tests;
using Xamarin.Utils;

#nullable enable

namespace Cecil.Tests {

	public static class Helper {

		static Dictionary<string, AssemblyDefinition> cache = new Dictionary<string, AssemblyDefinition> ();

		// make sure we load assemblies only once into memory
		public static AssemblyDefinition GetAssembly (string assembly, ReaderParameters? parameters = null, bool readSymbols = false)
		{
			Assert.That (assembly, Does.Exist, "Assembly existence");
			if (!cache.TryGetValue (assembly, out var ad)) {
				if (parameters is null) {
					var resolver = new DefaultAssemblyResolver ();
					parameters = new ReaderParameters () {
						AssemblyResolver = resolver,
						ReadSymbols = readSymbols,
					};
				}

				ad = AssemblyDefinition.ReadAssembly (assembly, parameters);
				cache.Add (assembly, ad);
			}
			return ad;
		}

		public static void AssertFailures (HashSet<string>? currentFailures, HashSet<string> knownFailures, string nameOfKnownFailureSet, string message)
		{
			AssertFailures<string> (currentFailures?.ToDictionary (v => v) ?? new Dictionary<string, string> (), knownFailures, nameOfKnownFailureSet, message, (v) => v);
		}

		public static void AssertFailures (Dictionary<string, string> currentFailures, HashSet<string> knownFailures, string nameOfKnownFailureSet, string message)
		{
			AssertFailures<string> (currentFailures, knownFailures, nameOfKnownFailureSet, message, (v) => v);
		}

		public static void AssertFailures<T> (Dictionary<string, T> currentFailures, HashSet<string> knownFailures, string nameOfKnownFailureSet, string message, Func<T, string> failureToString) where T : notnull, IComparable
		{
			var newFailures = currentFailures.Where (v => !knownFailures.Contains (v.Key)).Select (v => v.Value).ToArray ();
			var fixedFailures = knownFailures.Except (currentFailures.Select (v => v.Key).ToHashSet ());

			var printKnownFailures = newFailures.Any () || fixedFailures.Any ();
			if (printKnownFailures) {
				Console.WriteLine ($"Printing all failures as known failures because they seem out of date ({newFailures.Count ()} new failures, {fixedFailures.Count ()} fixed failures):");
				var lines = new List<string> ();
				lines.Add ($"\t\tstatic HashSet<string> {nameOfKnownFailureSet} = new HashSet<string> {{");
				foreach (var failure in currentFailures.OrderBy (v => v.Key))
					lines.Add ($"\t\t\t\"{failure.Key}\",");
				lines.Add ("\t\t};");

				if (!string.IsNullOrEmpty (Environment.GetEnvironmentVariable ("WRITE_KNOWN_FAILURES"))) {
					var cecilDir = Path.Combine (Configuration.SourceRoot, "tests", "cecil-tests");
					var writtenKnownFailures = false;
					foreach (var file in Directory.GetFiles (cecilDir, "*.cs")) {
						var content = File.ReadAllLines (file);
						var startIndex = Array.IndexOf (content, lines.First ());
						if (startIndex == -1)
							continue;

						var newLines = new List<string> ();
						var endIndex = Array.IndexOf (content, lines.Last (), startIndex);

						for (var i = 0; i < startIndex; i++)
							newLines.Add (content [i]);
						newLines.AddRange (lines);
						for (var i = endIndex + 1; i < content.Length; i++)
							newLines.Add (content [i]);
						File.WriteAllLines (file, newLines);

						Console.WriteLine ($"Updated {nameOfKnownFailureSet} in {file}.");
						writtenKnownFailures = true;
						break;
					}
					if (!writtenKnownFailures) {
						Console.WriteLine ($"Failed to update {nameOfKnownFailureSet}: {nameOfKnownFailureSet} not found.");
					}
				} else {
					Console.WriteLine (string.Join ("\n", lines));
				}
			}

			if (newFailures.Any ()) {
				Console.WriteLine ($"Printing {newFailures.Count ()} new failures with local paths for easy navigation:");
				foreach (var failure in newFailures.OrderBy (v => v))
					Console.WriteLine ($"    {failureToString (failure)}");
			}

			// Rather than doing an Assert.IsEmpty, which produces a horrendous error message, we'll do an Assert.Multiple which generates a 
			// nice enumerated output of all the failures.
			Assert.Multiple (() => {
				// fail for each of the new failures
				foreach (var failure in newFailures) {
					Assert.Fail (failure.ToString ());
				}

				// The list of known failures often doesn't separate based on platform, which means that we might not see all the known failures
				// unless we're currently building for all platforms. As such, only verify the list of known failures if we're building for all platforms.
				if (!Configuration.AnyIgnoredPlatforms ())
					Assert.IsEmpty (fixedFailures, $"Known failures that aren't failing anymore - remove these from the list of known failures: {message}");
			});

		}

		// Enumerates all the methods in the assembly, for all types (including nested types), potentially providing a custom filter function.
		public static IEnumerable<MethodDefinition> EnumerateMethods (this AssemblyDefinition assembly, Func<MethodDefinition, bool>? filter = null)
		{
			foreach (var type in EnumerateTypes (assembly)) {
				foreach (var method in type.EnumerateMethods (filter))
					yield return method;
			}
		}

		// Enumerates all the methods in the type, potentially providing a custom filter function.
		public static IEnumerable<MethodDefinition> EnumerateMethods (this TypeDefinition type, Func<MethodDefinition, bool>? filter = null)
		{
			if (!type.HasMethods)
				yield break;

			foreach (var method in type.Methods) {
				if (filter is null || filter (method))
					yield return method;
			}
		}
		// Enumerates all the properties in the assembly, for all types (including nested types), potentially providing a custom filter function.
		public static IEnumerable<PropertyDefinition> EnumerateProperties (this AssemblyDefinition assembly, Func<PropertyDefinition, bool>? filter = null)
		{
			foreach (var type in EnumerateTypes (assembly)) {
				if (!type.HasProperties)
					continue;

				foreach (var property in type.Properties) {
					if (filter is null || filter (property))
						yield return property;
				}
			}
		}

		// Enumerates all the properties in the type, potentially providing a custom filter function.
		public static IEnumerable<PropertyDefinition> EnumerateProperties (this TypeDefinition type, Func<PropertyDefinition, bool>? filter = null)
		{
			if (!type.HasProperties)
				yield break;

			foreach (var property in type.Properties) {
				if (filter is null || filter (property))
					yield return property;
			}
		}

		// Enumerates all the events in the assembly, for all types (including nested types), potentially providing a custom filter function.
		public static IEnumerable<EventDefinition> EnumerateEvents (this AssemblyDefinition assembly, Func<EventDefinition, bool>? filter = null)
		{
			foreach (var type in EnumerateTypes (assembly)) {
				foreach (var @event in type.EnumerateEvents (filter))
					yield return @event;
			}
		}

		// Enumerates all the events in the type, potentially providing a custom filter function.
		public static IEnumerable<EventDefinition> EnumerateEvents (this TypeDefinition type, Func<EventDefinition, bool>? filter = null)
		{
			if (!type.HasEvents)
				yield break;

			foreach (var @event in type.Events) {
				if (filter is null || filter (@event))
					yield return @event;
			}
		}

		// Recursively enumerates all the nested types for the given type, potentially providing a custom filter function.
		static IEnumerable<TypeDefinition> EnumerateNestedTypes (this TypeDefinition type, Func<TypeDefinition, bool>? filter)
		{
			if (!type.HasNestedTypes)
				yield break;

			foreach (var nestedType in type.NestedTypes) {
				foreach (var nn in EnumerateNestedTypes (nestedType, filter))
					yield return nn;

				if (filter is null || filter (nestedType))
					yield return nestedType;
			}
		}

		// Enumerates all the types in the assembly, including nested types, potentially providing a custom filter function.
		public static IEnumerable<TypeDefinition> EnumerateTypes (this AssemblyDefinition assembly, Func<TypeDefinition, bool>? filter = null)
		{
			foreach (var module in assembly.Modules) {
				if (!module.HasTypes)
					continue;

				foreach (var type in module.Types) {
					if (filter is null || filter (type))
						yield return type;

					foreach (var nestedType in EnumerateNestedTypes (type, filter))
						yield return nestedType;
				}
			}
		}

		// Enumerates all the fields in the assembly, for all types (including nested types), potentially providing a custom filter function.
		public static IEnumerable<FieldDefinition> EnumerateFields (this AssemblyDefinition assembly, Func<FieldDefinition, bool>? filter = null)
		{
			foreach (var type in EnumerateTypes (assembly)) {
				foreach (var field in type.EnumerateFields (filter))
					yield return field;
			}
		}

		// Enumerates all the fields in the type, potentially providing a custom filter function.
		public static IEnumerable<FieldDefinition> EnumerateFields (this TypeDefinition type, Func<FieldDefinition, bool>? filter = null)
		{
			if (!type.HasFields)
				yield break;

			foreach (var field in type.Fields) {
				if (filter is null || filter (field))
					yield return field;
			}
		}

		public static IEnumerable<ICustomAttributeProvider> EnumerateAttributeProviders (this AssemblyDefinition assembly, Func<ICustomAttributeProvider, bool>? filter = null)
		{
			if (filter is null || filter (assembly))
				yield return assembly;

			foreach (var module in assembly.Modules) {
				if (filter is null || filter (module))
					yield return module;
			}

			foreach (var item in assembly.EnumerateTypes (filter))
				yield return item;

			foreach (var item in assembly.EnumerateFields (filter))
				yield return item;

			foreach (var item in assembly.EnumerateMethods (filter))
				yield return item;

			foreach (var item in assembly.EnumerateProperties (filter))
				yield return item;

			foreach (var item in assembly.EnumerateEvents (filter))
				yield return item;
		}

		public static IEnumerable<MemberReference> EnumerateMembers (this AssemblyDefinition assembly, Func<MemberReference, bool>? filter = null)
		{
			foreach (var item in assembly.EnumerateTypes (filter))
				yield return item;

			foreach (var item in assembly.EnumerateFields (filter))
				yield return item;

			foreach (var item in assembly.EnumerateMethods (filter))
				yield return item;

			foreach (var item in assembly.EnumerateProperties (filter))
				yield return item;

			foreach (var item in assembly.EnumerateEvents (filter))
				yield return item;
		}

		public static bool IsPubliclyVisible (this TypeDefinition type)
		{
			if (type.IsNested) {
				if (type.IsNestedAssembly || type.IsNestedFamilyAndAssembly || type.IsNestedPrivate)
					return false;
				return IsPubliclyVisible (type.DeclaringType);
			}

			return type.IsPublic;
		}

		public static bool IsPubliclyVisible (this FieldDefinition field)
		{
			if (!IsPubliclyVisible (field.DeclaringType))
				return false;

			var visibility = field.Attributes & FieldAttributes.FieldAccessMask;
			switch (visibility) {
			case FieldAttributes.Private:
			case FieldAttributes.FamANDAssem:
			case FieldAttributes.Assembly:
				return false;
			case FieldAttributes.Family:
			case FieldAttributes.FamORAssem:
			case FieldAttributes.Public:
				return true;
			default:
				throw new NotImplementedException ($"Unknown visibility: {visibility}");
			}
		}

		public static bool IsPubliclyVisible (this MethodDefinition method)
		{
			if (!IsPubliclyVisible (method.DeclaringType))
				return false;

			var visibility = method.Attributes & MethodAttributes.MemberAccessMask;
			switch (visibility) {
			case MethodAttributes.Private:
			case MethodAttributes.FamANDAssem:
			case MethodAttributes.Assembly:
				return false;
			case MethodAttributes.Family:
			case MethodAttributes.FamORAssem:
			case MethodAttributes.Public:
				return true;
			default:
				throw new NotImplementedException ($"Unknown visibility: {visibility}");
			}
		}

		public static bool IsPubliclyVisible (this EventDefinition evt)
		{
			if (!IsPubliclyVisible (evt.DeclaringType))
				return false;

			var invokeMethod = evt.InvokeMethod;
			if (invokeMethod is not null && IsPubliclyVisible (invokeMethod))
				return true;
			var addMethod = evt.AddMethod;
			if (addMethod is not null && IsPubliclyVisible (addMethod))
				return true;
			var removeMethod = evt.RemoveMethod;
			if (removeMethod is not null && IsPubliclyVisible (removeMethod))
				return true;
			return false;
		}

		public static bool IsPubliclyVisible (this PropertyDefinition property)
		{
			if (!IsPubliclyVisible (property.DeclaringType))
				return false;

			var getter = property.GetMethod;
			if (getter is not null && IsPubliclyVisible (getter))
				return true;
			var setter = property.SetMethod;
			if (setter is not null && IsPubliclyVisible (setter))
				return true;
			return false;
		}

		public static bool IsPubliclyVisible (this MemberReference member)
		{
			return member switch {
				PropertyDefinition pd => IsPubliclyVisible (pd),
				EventDefinition ed => IsPubliclyVisible (ed),
				MethodDefinition md => IsPubliclyVisible (md),
				FieldDefinition fd => IsPubliclyVisible (fd),
				TypeDefinition td => IsPubliclyVisible (td),
				_ => throw new NotImplementedException (member.GetType ().FullName),
			};
		}

		public static IEnumerable<MemberReference> EnumeratePublicMembers (this AssemblyDefinition assembly, Func<MemberReference, bool>? filter = null)
		{
			var visibleFilter = (MemberReference mr) => {
				if (filter is not null && !filter (mr))
					return false;
				return IsPubliclyVisible (mr);
			};
			return EnumerateMembers (assembly, visibleFilter);
		}

		public static IEnumerable<ICustomAttributeProvider> EnumerateAttributeProviders (this TypeDefinition type, Func<ICustomAttributeProvider, bool>? filter = null)
		{
			// EnumerateNestedTypes will recurse, but we don't want to do that here.
			if (type.HasNestedTypes) {
				foreach (var item in type.NestedTypes)
					if (filter is null || filter (item))
						yield return item;
			}

			foreach (var item in type.EnumerateFields (filter))
				yield return item;

			foreach (var item in type.EnumerateMethods (filter))
				yield return item;

			foreach (var item in type.EnumerateProperties (filter))
				yield return item;

			foreach (var item in type.EnumerateEvents (filter))
				yield return item;
		}

		static IEnumerable<string> NetPlatformAssemblies => Configuration.GetRefLibraries ();

		static IList<AssemblyInfo>? net_platform_assembly_definitions;
		public static IEnumerable<AssemblyInfo> NetPlatformAssemblyDefinitions {
			get {
				if (net_platform_assembly_definitions is null) {
					net_platform_assembly_definitions = NetPlatformAssemblies
						.Select (v => new AssemblyInfo (v, GetAssembly (v, readSymbols: false)))
						.ToArray ();
				}
				return net_platform_assembly_definitions;
			}
		}

		static IEnumerable<string> NetPlatformImplementationAssemblies => Configuration.GetBaseLibraryImplementations ();

		static IList<AssemblyInfo>? net_platform_assembly_implemnetation_assembly_definitions;
		public static IEnumerable<AssemblyInfo> NetPlatformImplementationAssemblyDefinitions {
			get {
				if (net_platform_assembly_implemnetation_assembly_definitions is null) {
					net_platform_assembly_implemnetation_assembly_definitions = NetPlatformImplementationAssemblies
						.Select (v => new AssemblyInfo (v, GetAssembly (v, readSymbols: true)))
						.ToArray ();
				}
				return net_platform_assembly_implemnetation_assembly_definitions;
			}
		}

		static Dictionary<string, List<MappedApiInfo>>? mapped_net_api;
		public static Dictionary<string, List<MappedApiInfo>> MappedNetApi {
			get {
				Configuration.IgnoreIfAnyIgnoredPlatforms ();

				if (mapped_net_api is null) {
					mapped_net_api = new Dictionary<string, List<MappedApiInfo>> ();
					var assemblies = Helper.NetPlatformAssemblyDefinitions.ToArray ();
					foreach (var info in assemblies) {
						var assembly = info.Assembly;

						foreach (var api in assembly.EnumerateAttributeProviders ()) {
							var fullname = api.AsFullName ();
							if (!mapped_net_api.TryGetValue (fullname, out var list))
								mapped_net_api [fullname] = list = new List<MappedApiInfo> ();
							list.Add (new MappedApiInfo (info.Platform, api));

							// Verify that the Fullname is unique for each API
							if (list.Count > assemblies.Length)
								throw new InvalidOperationException ($"The key '{fullname}' was used for more than one given API.");
						}
					}
				}
				return mapped_net_api;
			}
		}

		// This method renders a string that sorts well - methods in the same
		// type are sorted next to eachother (the default MethodDefinition.FullName
		// implementation starts with the return type, so sorting the results
		// yields ugly results).
		public static string RenderMethod (this MethodDefinition method)
		{
			var sb = new StringBuilder ();
			sb.Append (method.DeclaringType.FullName);
			sb.Append ("::");
			sb.Append (method.Name);
			sb.Append ('(');
			if (method.HasParameters) {
				for (var i = 0; i < method.Parameters.Count; i++) {
					if (i > 0)
						sb.Append (',');
					var pType = method.Parameters [i].ParameterType;
					sb.Append (pType.FullName);
				}
			}
			sb.Append (')');
			if (method.IsOperator ()) {
				sb.Append ("->");
				sb.Append (method.ReturnType.FullName);
			}
			return sb.ToString ();
		}

		public static string RenderLocation (this IMemberDefinition? member, Instruction? instruction = null)
		{
			if (member is null)
				return string.Empty;

			if (member is PropertyDefinition pd) {
				if (pd.GetMethod is not null)
					return RenderLocation (pd.GetMethod);
				if (pd.SetMethod is not null)
					return RenderLocation (pd.SetMethod);
				return "<no location>";
			}

			if (member is TypeDefinition td && td.HasMethods)
				return RenderLocation (td.Methods.Where (v => v.HasBody).FirstOrDefault ());

			if (!(member is MethodDefinition method))
				return "<no location> ";

			if (method.DebugInformation.HasSequencePoints) {
				var seq = method.DebugInformation.SequencePoints [0];
				if (instruction is not null) {
					var instr = instruction;
					while (instr is not null) {
						var iseq = method.DebugInformation.GetSequencePoint (instr);
						if (iseq is not null) {
							seq = iseq;
							break;
						}
						instr = instr.Previous;
					}
				}
				return seq.Document.Url + ":" + seq.StartLine + " ";
			}
			return string.Empty;
		}

		public static string RenderLocation (this object provider)
		{
			if (provider is IMemberDefinition md)
				return RenderLocation (md);
			return string.Empty;
		}

		public static OSPlatformAttributes? GetAvailabilityAttributes (this ICustomAttributeProvider provider, ApplePlatform platform)
		{
			if (!provider.HasCustomAttributes)
				return null;

			OSPlatformAttributes? rv = null;

			foreach (var a in provider.CustomAttributes) {
				var attributeType = a.AttributeType;
				if (attributeType.Namespace != "System.Runtime.Versioning")
					continue;

				if (attributeType.FullName == "System.Runtime.Versioning.RequiresPreviewFeaturesAttribute")
					continue;

				if (!a.HasConstructorArguments)
					continue;

				if (a.ConstructorArguments.Count != 1 && a.ConstructorArguments.Count != 2)
					continue;

				if (!a.ConstructorArguments [0].Type.Is ("System", "String"))
					continue;

				var platformName = (string) a.ConstructorArguments [0].Value;
				if (!OSPlatformAttributeExtensions.TryParse (platformName, out ApplePlatform? attributePlatform, out var version))
					throw new InvalidOperationException ($"The API {provider.AsFullName ()} has an invalid OSPlatform attribute: {platformName}");

				if (attributePlatform != platform)
					continue;

				if (rv is null)
					rv = new OSPlatformAttributes (provider, platform);

				switch (attributeType.Name) {
				case "UnsupportedOSPlatformAttribute":
					rv.Unsupported = new OSPlatformAttribute (platform, platformName, version, a);
					break;
				case "SupportedOSPlatformAttribute":
					rv.Supported = new OSPlatformAttribute (platform, platformName, version, a);
					break;
				case "ObsoletedOSPlatformAttribute":
					rv.Obsoleted = new OSPlatformAttribute (platform, platformName, version, a);
					break;
				case "TargetPlatformAttribute":
				case "SupportedOSPlatformGuardAttribute":
					continue;
				default:
					throw new NotImplementedException (attributeType.FullName);
				}
			}

			return rv;
		}

		public static bool IsSubclassOf (this TypeDefinition? type, string @namespace, string name)
		{
			if (type is null)
				return false;

			if (type.Is (@namespace, name))
				return true;

			return IsSubclassOf (type.BaseType?.Resolve (), @namespace, name);
		}
	}

	public static class CompatExtensions {
		// cecil-tests is not NET5 yet, this is required to foreach over a dictionary
		public static void Deconstruct<T1, T2> (this KeyValuePair<T1, T2> tuple, out T1 key, out T2 value)
		{
			key = tuple.Key;
			value = tuple.Value;
		}
	}

	public static class Extensions {
		public static void LoadWithoutNetworkAccess (this XmlDocument doc, string filename)
		{
			using (var fs = new FileStream (filename, FileMode.Open, FileAccess.Read)) {
				var settings = new XmlReaderSettings () {
					XmlResolver = null,
					DtdProcessing = DtdProcessing.Parse,
				};
				using (var reader = XmlReader.Create (fs, settings)) {
					doc.Load (reader);
				}
			}
		}
	}

	public class AssemblyInfo {
		public AssemblyDefinition Assembly;
		public string Path;
		public ApplePlatform Platform;

		public AssemblyInfo (string path, AssemblyDefinition assembly)
		{
			Assembly = assembly;
			Path = path;
			Platform = Configuration.GetPlatform (path);
		}

		public override string ToString ()
		{
			// The returned text will show up in VSMac's unit test pad
			return Path.Replace (Configuration.RootPath, string.Empty).TrimStart ('/');
		}
	}

	public class MappedApiInfo {
		public ApplePlatform Platform;
		public ICustomAttributeProvider Api;

		public MappedApiInfo (ApplePlatform platform, ICustomAttributeProvider api)
		{
			Platform = platform;
			Api = api;
		}
	}

	public class OSPlatformAttribute {
		public ApplePlatform Platform;
		public string PlatformName;
		public Version? Version;
		public CustomAttribute Attribute;

		public OSPlatformAttribute (ApplePlatform platform, string platformName, Version? version, CustomAttribute attribute)
		{
			Platform = platform;
			PlatformName = platformName;
			Version = version;
			Attribute = attribute;
		}

		public string? Message {
			get {
				if (Attribute?.HasConstructorArguments != true)
					return null;
				if (Attribute.ConstructorArguments.Count < 2)
					return null;
				return Attribute.ConstructorArguments [1].Value as string;
			}
		}
	}

	public class OSPlatformAttributes {
		public ICustomAttributeProvider Api;
		public ApplePlatform Platform;
		public OSPlatformAttribute? Supported;
		public OSPlatformAttribute? Obsoleted;
		public OSPlatformAttribute? Unsupported;

		public OSPlatformAttributes (ICustomAttributeProvider api, ApplePlatform platform)
		{
			Api = api;
			Platform = platform;
		}
	}

	public record FailureWithMessageAndLocation : IComparable {
		public string Message { get; }
		public string Location { get; }

		public FailureWithMessageAndLocation (string message, string location)
		{
			Message = message;
			Location = location;
		}

		public override string ToString ()
		{
			if (string.IsNullOrEmpty (Location))
				return Message;
			return $"{Message} at {Location}";
		}

		public int CompareTo (object? obj)
		{
			if (obj is FailureWithMessageAndLocation other)
				return ToString ().CompareTo (other.ToString ());
			return -1;
		}
	}

}
