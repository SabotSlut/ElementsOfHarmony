using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Mono.Cecil;
using Mono.Cecil.Rocks;

namespace ElementsOfHarmony
{
    class Program
    {
        public static string[] Libraries;

        public static string[] TargetClasses;

        public static StreamWriter Html;

        public static HashSet<TypeReference> TypeReferences = new HashSet<TypeReference>();

        static int Main(string[] args)
        {
            TargetClasses = args;
            if (TargetClasses.Length <= 0)
            {
                Console.WriteLine("Warning: No Type filter specified. This will likely produce an HTML file that your browser will struggle with. It is recommended to run this program again, this time passing Type names as command-line arguments.");
            }
            else
            {
                Console.WriteLine("Searching for the following Types:");
                foreach (string search in args)
                {
                    Console.WriteLine($"    \"{search}\"");
                }
            }

            if (!File.Exists("Libraries.txt"))
            {
                Console.WriteLine("Please create a Libraries.txt file that contains the libraries you want to generate stubs for.");
                return 1;
            }

            Libraries = File.ReadAllLines("Libraries.txt");

            List<string> tmpLibraries = new List<string>(Libraries.Length);
            foreach (string library in Libraries)
            {
                if (string.IsNullOrWhiteSpace(library))
                {
                    continue;
                }

                if (library.TrimStart().StartsWith('#'))
                {
                    continue;
                }

                string lib = Path.GetFullPath(library);

                if (!File.Exists(library))
                {
                    Console.WriteLine($"File Not Found: {lib}");
                    continue;
                }

                tmpLibraries.Add(lib);
            }

            if (tmpLibraries.Count <= 0)
            {
                Console.WriteLine("No valid libraries found in Libraries.txt.");
                return 1;
            }

            Libraries = tmpLibraries.ToArray();

            Html = new StreamWriter("output.html");
            string style = @"
            .row1
            {
                background-color:rgb(238, 238, 238);
            }

            .row2
            {
                background-color:rgb(255, 255, 255);
            }

            .head1
            {
                background-color:rgb(105, 105, 105);
                color:rgb(255, 255, 255);
            }

            .head2
            {
                background-color:rgb(215, 217, 242);
            }

            .row1, .row2, .head2
            {
                color:rgb(51, 51, 51);
            }
            ";
            Html.WriteLine(@"<!doctype html>
            <html>
            <head>
            <meta charset=""utf-8""/>
            <title>Elements of Harmony: Results</title>
            <style>"+style+@"</style>
            </head>
            <body>
            <table>");
            foreach (string path in Libraries)
            {
                PrintTypes(path);
            }

            Html.WriteLine("</table>");
            /*
            Console.WriteLine("<table>");
            foreach (string path in Libraries)
            {
                DebugTypes(path);
            }
            Console.WriteLine("</table>");
            */
            Html.WriteLine("</body></html>");
            Html.Flush();
            Console.WriteLine($"File written to {Path.GetFullPath("Output.html")}.");
            return 0;
        }

        private static void DebugTypes(string fileName)
        {
            bool gray = false;
            foreach (TypeReference type in TypeReferences)
            {
                string fullName = type.FullName.Replace("<", "&lt;").Replace(">", "&gt;");
                string name = type.Name.Replace("<", "&lt;").Replace(">", "&gt;");

                Html.WriteLine($"<tr class=\"{(gray ? "row2" : "row1")}\"><td>{fullName}</td><td>{name}</td><td>{SanitizeType(type)}</td></tr>");
                gray = !gray;
            }
        }

        public static void PrintTypes(string fileName)
        {
            bool Matches(string name)
            {
                if (TargetClasses.Length <= 0)
                {
                    return true;
                }

                foreach (string target in TargetClasses)
                {
                    if (name.Contains(target, StringComparison.InvariantCultureIgnoreCase))
                    {
                        return true;
                    }
                }

                return false;
            }

            bool gray = false;

            void DisplayMethodOrProperty(MethodDefinition m, TypeDefinition type, PropertyDefinition property)
            {
                bool isExtensionMethod = false;
                foreach (var attribute in m.CustomAttributes)
                {
                    if (attribute.AttributeType.FullName == "System.Runtime.CompilerServices.ExtensionAttribute")
                    {
                        isExtensionMethod = true;
                        break;
                    }
                }

                Html.WriteLine($"<tr class=\"{(gray ? "row2" : "row1")}\"><td>{PrintMethodPatch(m, type, PatchType.Prefix, isExtensionMethod, property)}</td><td>{PrintMethodPatch(m, type, PatchType.Replacement, isExtensionMethod, property)}</td><td>{PrintMethodPatch(m, type, PatchType.Postfix, isExtensionMethod, property)}</td></tr>");
                gray = !gray;
            }

            void ProcessType(TypeDefinition type, int depth)
            {
                if (!TypeReferences.Contains(type))
                {
                    TypeReferences.Add(type);
                }

                if (type.HasGenericParameters)
                    return;

                if (Matches(type.FullName))
                {
                    string cachedHeader = SanitizeType(type);
                    Html.WriteLine($"<tr class=\"head1\"><th>{cachedHeader}</th><th>{cachedHeader}</th><th>{cachedHeader}</th></tr>");
                    Html.WriteLine("<tr class=\"head2\"><th>Before</th><th>Replace</th><th>After</th></tr>");

                    Html.WriteLine();
                    foreach (var method in type.Methods)
                    {
                        if (method.HasGenericParameters ||
                            (method.SemanticsAttributes & MethodSemanticsAttributes.Setter) != 0 ||
                            (method.SemanticsAttributes & MethodSemanticsAttributes.Getter) != 0)
                        {
                            continue;
                        }

                        DisplayMethodOrProperty(method, type, null);
                    }

                    foreach (var property in type.Properties)
                    {
                        /*
                        Console.WriteLine($"<tr class=\"{(gray ? "row2" : "row1")}\"><td>{property.GetMethod}</td><td>{property.SetMethod}</td><td>{property.HasOtherMethods}</td></tr>");
                        gray = !gray;
                        */

                        if (property.GetMethod != null)
                        {
                            DisplayMethodOrProperty(property.GetMethod, type, property);
                        }

                        if (property.SetMethod != null)
                        {
                            DisplayMethodOrProperty(property.SetMethod, type, property);
                        }
                    }
                }

                foreach (var nestedType in type.NestedTypes)
                {
                    ProcessType(nestedType, depth + 1);
                }
            }

            ModuleDefinition module = ModuleDefinition.ReadModule(fileName);
            foreach (TypeDefinition type in module.Types)
            {
                ProcessType(type, 0);
            }
        }

        private static string PrintMethodPatch(MethodDefinition method, TypeDefinition type, PatchType patchType,
            bool isExtensionMethod, PropertyDefinition property)
        {
            StringBuilder sb = new StringBuilder();
            List<string> parameters = new List<string>();
            List<string> parameterTypes = new List<string>();

            string returnType = SanitizeType(method.ReturnType);
            if (patchType == PatchType.Replacement && returnType != "void")
            {
                parameters.Add($"ref {returnType} __result");
            }

            if ((method.Attributes & MethodAttributes.Static) == 0)
            {
                parameters.Add($"{SanitizeType(type)} __instance");
            }

            for (var i = 0; i < method.Parameters.Count; i++)
            {
                var parameter = method.Parameters[i];
                if (patchType == PatchType.Replacement && !(isExtensionMethod && i == 0))
                {
                    parameters.Add($"ref {SanitizeParameter(parameter)}");
                }
                else
                {
                    parameters.Add(SanitizeParameter(parameter));
                }

                parameterTypes.Add($"typeof({SanitizeType(parameter.ParameterType)})");
            }

            string funcName = $"{SanitizeMethod(method)}{patchType}";

            sb.Append($"[HarmonyPatch(typeof({type}))]<br/>");
            if (method.Name == ".ctor")
            {
                sb.Append("[HarmonyPatch(MethodType.Constructor)]<br/>");
            }
            else if (property == null)
            {
                sb.Append($"[HarmonyPatch(\"{SanitizeMethod(method)}\")]<br/>");
            }
            else if (method == property.GetMethod)
            {
                sb.Append($"[HarmonyPatch(\"{SanitizeProperty(property)}\", MethodType.Getter)]<br/>");
                funcName = $"{SanitizeProperty(property)}Getter{patchType}";
            }
            else if (method == property.SetMethod)
            {
                sb.Append($"[HarmonyPatch(\"{SanitizeProperty(property)}\", MethodType.Setter)]<br/>");
                funcName = $"{SanitizeProperty(property)}Setter{patchType}";
            }

            if (parameterTypes.Count > 0)
            {
                sb.Append($"[HarmonyPatch(new Type[] {{ {string.Join(", ", parameterTypes)} }})]<br/>");
            }

            switch (patchType)
            {
                case PatchType.Prefix:
                    sb.Append("[HarmonyPrefix]<br/>");
                    sb.Append($"private static void {funcName}(");
                    break;
                case PatchType.Replacement:
                    sb.Append("[HarmonyPrefix]<br/>");
                    sb.Append($"private static bool {funcName}(");
                    break;
                case PatchType.Postfix:
                    sb.Append("[HarmonyPostfix]<br/>");
                    sb.Append($"private static void {funcName}(");
                    break;
            }

            sb.Append(string.Join(", ", parameters));
            sb.Append(")<br/>");
            sb.Append("{<br/>");
            //sb.Append($"&nbsp;&nbsp;&nbsp;&nbsp;// {PrintMethodFriendly(method)}<br/>");
            sb.Append($"&nbsp;&nbsp;&nbsp;&nbsp;Logger.LogInfo(\"Autogenerated {patchType.ToString().ToLowerInvariant()} stub for {PrintMethodFriendly(method, isExtensionMethod, type)}\");<br/>");
            switch (patchType)
            {
                case PatchType.Prefix:
                case PatchType.Postfix:
                    //sb.Append("&nbsp;&nbsp;&nbsp;&nbsp;return;<br/>");
                    break;
                case PatchType.Replacement:
                    /*
                    sb.Append("&nbsp;&nbsp;&nbsp;&nbsp;/*<br/>");
                    sb.Append("&nbsp;&nbsp;&nbsp;&nbsp;throw new NotImplementedException();<br/>");
                    sb.Append("&nbsp;&nbsp;&nbsp;&nbsp;return false; // Don't run the original method.<br/>");
                    sb.Append("&nbsp;&nbsp;&nbsp;&nbsp;*"+"/<br/>");
                    */
                    sb.Append("&nbsp;&nbsp;&nbsp;&nbsp;return true; // Run the original method.<br/>");
                    break;
            }

            sb.Append("}<br/>");

            return sb.ToString();
        }

        public enum PatchType
        {
            Prefix,
            Replacement,
            Postfix,
        }

        private static string SanitizeParameter(ParameterDefinition parameter)
        {
            StringBuilder sb = new StringBuilder();
            if (parameter.IsOut)
            {
                sb.Append("out ");
            }

            sb.Append(SanitizeType(parameter.ParameterType));
            sb.Append(" ");
            sb.Append(SanitizeVariable(parameter.Name));
            return sb.ToString();
        }

        private static string PrintMethodFriendly(MethodDefinition method, bool isExtensionMethod, TypeReference type = null)
        {
            StringBuilder sb = new StringBuilder();

            string[] parameters = new string[method.Parameters.Count];
            for (int i = 0; i < parameters.Length; i++)
            {
                parameters[i] = SanitizeParameter(method.Parameters[i]);
            }

            if ((method.Attributes & MethodAttributes.Static) != 0)
            {
                sb.Append("static ");
                //sb.Append($"/*{method.Attributes}*/ ");
            }
            if (isExtensionMethod)
            {
                parameters[0] = $"this {parameters[0]}";
            }

            /*if (method.SemanticsAttributes != MethodSemanticsAttributes.None)
            {
                sb.Append($"/*{method.SemanticsAttributes}-/");
            }*/

            sb.Append($"{SanitizeType(method.ReturnType)} ");
            if (type != null)
            {
                sb.Append($"{SanitizeType(type)}.");
            }

            sb.Append($"{SanitizeMethod(method)}({string.Join(", ", parameters)})");

            return sb.ToString();
        }

        private static string SanitizeProperty(PropertyDefinition property) => property.Name;

        private static string SanitizeMethod(MethodDefinition method)
        {
            return method.Name switch
            {
                ".ctor" => "Constructor",
                ".cctor" => "StaticConstructor",
                _ => method.Name
            };
        }

        private static string SanitizeType(TypeReference type)
        {
            if (!TypeReferences.Contains(type))
            {
                TypeReferences.Add(type);
            }

            StringBuilder sb = new StringBuilder();

            string typeFullName = type.IsArray
                ? SanitizeBaseType(type.GetElementType()).Replace('/', '.')
                : SanitizeBaseType(type).Replace('/', '.');

            /*
            if (type.HasGenericParameters)
            {
                sb.Append("/*HasGenericParameters*"+"/");
                foreach (GenericParameter genPar in type.GenericParameters)
                {
                    sb.Append($"/*GenPar: {genPar.FullName}*"+"/");
                }
            }
            if (type.IsByReference)
            {
                sb.Append("/*IsByReference*"+"/");
            }
            if (type.IsPinned)
            {
                sb.Append("/*IsPinned*"+"/");
            }
            if (type.IsPointer)
            {
                sb.Append("/*IsPointer*"+"/");
            }
            */

            {
                typeFullName = typeFullName.Replace("`1", "");
                typeFullName = typeFullName.Replace("`2", "");
                typeFullName = typeFullName.Replace("`3", "");
                typeFullName = typeFullName.Replace("`4", "");
                typeFullName = typeFullName.Replace("`5", "");
                typeFullName = typeFullName.Replace("`6", "");
                typeFullName = typeFullName.Replace("`7", "");
                typeFullName = typeFullName.Replace("`8", "");
                typeFullName = typeFullName.Replace("`9", "");
                typeFullName = typeFullName.Replace("<", "&lt;");
                typeFullName = typeFullName.Replace(">", "&gt;");
            }

            sb.Append(typeFullName);

            if (type.IsArray)
            {
                sb.Append("[]");
            }

            return sb.ToString();
        }

        private static string SanitizeVariable(string variableName) => variableName switch
        {
            "abstract" => "@abstract",
            "as" => "@as",
            "base" => "@base",
            "bool" => "@bool",
            "break" => "@break",
            "byte" => "@byte",
            "case" => "@case",
            "catch" => "@catch",
            "char" => "@char",
            "checked" => "@checked",
            "class" => "@class",
            "const" => "@const",
            "continue" => "@continue",
            "decimal" => "@decimal",
            "default" => "@default",
            "delegate" => "@delegate",
            "do" => "@do",
            "double" => "@double",
            "else" => "@else",
            "enum" => "@enum",
            "event" => "@event",
            "explicit" => "@explicit",
            "extern" => "@extern",
            "false" => "@false",
            "finally" => "@finally",
            "fixed" => "@fixed",
            "float" => "@float",
            "for" => "@for",
            "foreach" => "@foreach",
            "goto" => "@goto",
            "if" => "@if",
            "implicit" => "@implicit",
            "in" => "@in",
            "int" => "@int",
            "interface" => "@interface",
            "internal" => "@internal",
            "is" => "@is",
            "lock" => "@lock",
            "long" => "@long",
            "namespace" => "@namespace",
            "new" => "@new",
            "null" => "@null",
            "object" => "@object",
            "operator" => "@operator",
            "out" => "@out",
            "override" => "@override",
            "params" => "@params",
            "private" => "@private",
            "protected" => "@protected",
            "public" => "@public",
            "readonly" => "@readonly",
            "record" => "@record",
            "ref" => "@ref",
            "return" => "@return",
            "sbyte" => "@sbyte",
            "sealed" => "@sealed",
            "short" => "@short",
            "sizeof" => "@sizeof",
            "stackalloc" => "@stackalloc",
            "static" => "@static",
            "string" => "@string",
            "struct" => "@struct",
            "switch" => "@switch",
            "this" => "@this",
            "throw" => "@throw",
            "true" => "@true",
            "try" => "@try",
            "typeof" => "@typeof",
            "uint" => "@uint",
            "ulong" => "@ulong",
            "unchecked" => "@unchecked",
            "unsafe" => "@unsafe",
            "ushort" => "@ushort",
            "using" => "@using",
            "virtual" => "@virtual",
            "void" => "@void",
            "volatile" => "@volatile",
            "while" => "@while",
            /*
            "add" => "@add",
            "alias" => "@alias",
            "ascending" => "@ascending",
            "async" => "@async",
            "await" => "@await",
            "by" => "@by",
            "descending" => "@descending",
            "dynamic" => "@dynamic",
            "equals" => "@equals",
            "from" => "@from",
            "get" => "@get",
            "global" => "@global",
            "group" => "@group",
            "init" => "@init",
            "into" => "@into",
            "join" => "@join",
            "let" => "@let",
            "nameof" => "@nameof",
            "nint" => "@nint",
            "notnull" => "@notnull",
            "nuint" => "@nuint",
            "on" => "@on",
            "orderby" => "@orderby",
            "partial" => "@partial",
            "remove" => "@remove",
            "select" => "@select",
            "set" => "@set",
            "unmanaged" => "@unmanaged",
            "value" => "@value",
            "var" => "@var",
            "when" => "@when",
            "where" => "@where",
            "with" => "@with",
            "yield" => "@yield",
            */
            _ => variableName
        };

        private static string SanitizeBaseType(TypeReference type)
        {
            if (type.Name == "Void")
            {
                return "void";
            }

            if (type.IsPrimitive)
            {
                return type.Name switch
                {
                    "Boolean" => "bool",
                    "Byte" => "byte",
                    "SByte" => "sbyte",
                    "Char" => "char",
                    "Decimal" => "decimal",
                    "Double" => "double",
                    "Single" => "float",
                    "Int32" => "int",
                    "UInt32" => "uint",
                    "Int64" => "long",
                    "UInt64" => "ulong",
                    "Int16" => "short",
                    "UInt16" => "ushort",
                    "IntPtr" => "IntPtr",
                    _ => type.Name
                };
            }

            return type.Namespace switch
            {
                "System" => type.Name switch
                {
                    "String" => "string",
                    "Object" => "object",
                    _ => type.Name
                },
                "System.Collections" => type.Name switch
                {
                    "IEnumerator" => type.Name,
                    _ => type.FullName
                },
                "UnityEngine" => type.Name switch
                {
                    "Material" => type.Name,
                    "Mesh" => type.Name,
                    "RenderTexture" => type.Name,
                    "Vector3" => type.Name,
                    "Vector4" => type.Name,
                    "Color" => type.Name,
                    "Camera" => type.Name,
                    "Matrix4x4" => type.Name,
                    "Transform" => type.Name,
                    "Object" => "UnityEngine.Object",
                    _ => type.FullName
                },
                _ => type.FullName
            };
        }
    }
}
