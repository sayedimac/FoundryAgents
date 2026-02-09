using System;
using System.Linq;
using System.Reflection;

var asm = Assembly.LoadFrom(@"C:\Users\jomyburg\.nuget\packages\openai\2.8.0\lib\net8.0\OpenAI.dll");

// Find all MCP-related public types
var mcpTypes = asm.GetTypes()
    .Where(t => t.IsPublic && (t.Name.Contains("Mcp") || t.Name.Contains("MCP")))
    .OrderBy(t => t.FullName)
    .ToList();

Console.WriteLine("=== PUBLIC MCP TYPES ===");
foreach (var t in mcpTypes)
{
    Console.WriteLine($"  {t.FullName}");
}

// Find ResponseTool.CreateMcpTool
var responseTool = asm.GetTypes().FirstOrDefault(t => t.Name == "ResponseTool");
if (responseTool != null)
{
    Console.WriteLine($"\n=== ResponseTool: {responseTool.FullName} ===");
    var methods = responseTool.GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.Instance)
        .Where(m => m.Name.Contains("Mcp") || m.Name.Contains("MCP") || m.Name.Contains("Create"))
        .ToList();
    Console.WriteLine($"  Found {methods.Count} matching methods:");
    foreach (var m in methods)
    {
        var parms = string.Join(", ", m.GetParameters().Select(p =>
        {
            var typeName = p.ParameterType.IsGenericType
                ? $"{p.ParameterType.GetGenericTypeDefinition().Name}<{string.Join(",", p.ParameterType.GetGenericArguments().Select(a => a.Name))}>"
                : p.ParameterType.Name;
            var defaultVal = p.HasDefaultValue ? $" = {p.DefaultValue ?? "null"}" : "";
            return $"{typeName} {p.Name}{defaultVal}";
        }));
        Console.WriteLine($"  {(m.IsStatic ? "static " : "")}{m.ReturnType.Name} {m.Name}({parms})");
    }
}

// Inspect McpTool type
var mcpTool = asm.GetTypes().FirstOrDefault(t => t.Name == "McpTool" && t.IsPublic);
if (mcpTool != null)
{
    Console.WriteLine($"\n=== {mcpTool.FullName} Properties ===");
    foreach (var p in mcpTool.GetProperties(BindingFlags.Public | BindingFlags.Instance))
    {
        Console.WriteLine($"  {p.PropertyType.Name} {p.Name} {{ {(p.CanRead ? "get;" : "")} {(p.CanWrite ? "set;" : "")} }}");
    }
    Console.WriteLine($"\n=== {mcpTool.FullName} Constructors ===");
    foreach (var c in mcpTool.GetConstructors(BindingFlags.Public | BindingFlags.Instance))
    {
        var parms = string.Join(", ", c.GetParameters().Select(p => $"{p.ParameterType.Name} {p.Name}"));
        Console.WriteLine($"  McpTool({parms})");
    }
}

// Also check the base type
if (mcpTool?.BaseType != null)
{
    Console.WriteLine($"\n=== Base type: {mcpTool.BaseType.FullName} ===");
    foreach (var p in mcpTool.BaseType.GetProperties(BindingFlags.Public | BindingFlags.Instance))
    {
        Console.WriteLine($"  {p.PropertyType.Name} {p.Name}");
    }
}

// Inspect McpToolDefinition
var mcpToolDef = asm.GetTypes().FirstOrDefault(t => t.Name == "McpToolDefinition" && t.IsPublic);
if (mcpToolDef != null)
{
    Console.WriteLine($"\n=== {mcpToolDef.FullName} Properties ===");
    foreach (var p in mcpToolDef.GetProperties(BindingFlags.Public | BindingFlags.Instance))
    {
        Console.WriteLine($"  {p.PropertyType.Name} {p.Name}");
    }
}

// Inspect McpToolFilter
var mcpToolFilter = asm.GetTypes().FirstOrDefault(t => t.Name == "McpToolFilter" && t.IsPublic);
if (mcpToolFilter != null)
{
    Console.WriteLine($"\n=== {mcpToolFilter.FullName} Properties ===");
    foreach (var p in mcpToolFilter.GetProperties(BindingFlags.Public | BindingFlags.Instance))
    {
        Console.WriteLine($"  {p.PropertyType.Name} {p.Name}");
    }
}

// Look for headers/auth on ALL MCP types
Console.WriteLine($"\n=== Properties containing 'Header'/'Auth'/'Token'/'Secret'/'Key' on any MCP type ===");
foreach (var t in mcpTypes)
{
    foreach (var p in t.GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.NonPublic))
    {
        if (p.Name.Contains("Header", StringComparison.OrdinalIgnoreCase) ||
            p.Name.Contains("Auth", StringComparison.OrdinalIgnoreCase) ||
            p.Name.Contains("Token", StringComparison.OrdinalIgnoreCase) ||
            p.Name.Contains("Secret", StringComparison.OrdinalIgnoreCase) ||
            p.Name.Contains("Key", StringComparison.OrdinalIgnoreCase))
        {
            Console.WriteLine($"  {t.Name}.{p.Name} : {p.PropertyType.Name}");
        }
    }
}

// Inspect McpToolCallApprovalPolicy
var approvalPolicy = asm.GetTypes().FirstOrDefault(t => t.Name == "McpToolCallApprovalPolicy" && t.IsPublic);
if (approvalPolicy != null)
{
    Console.WriteLine($"\n=== {approvalPolicy.FullName} ===");
    foreach (var p in approvalPolicy.GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static))
    {
        Console.WriteLine($"  {p.PropertyType.Name} {p.Name}");
    }
}

// Check all Create* methods on ResponseTool
Console.WriteLine("\n=== ALL static factory methods on ResponseTool ===");
if (responseTool != null)
{
    foreach (var m in responseTool.GetMethods(BindingFlags.Public | BindingFlags.Static).OrderBy(m => m.Name))
    {
        var parms = string.Join(", ", m.GetParameters().Select(p => $"{p.ParameterType.Name} {p.Name}"));
        Console.WriteLine($"  {m.ReturnType.Name} {m.Name}({parms})");
    }
}
