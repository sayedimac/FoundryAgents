// Copyright (c) Microsoft. All rights reserved.
using System.Diagnostics;
using OpenTelemetry;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace GitHubAgent;

internal static class Program
{
    private static TracerProvider? s_tracerProvider;

    private static async Task Main(string[] args)
    {
        try
        {
            bool containerMode = !(args.Length > 0 && args[0] == "interactive");

            Console.WriteLine("Microsoft Learn Agent - Lab 02 (Develop an AI agent)");
            Console.WriteLine(containerMode ? "Mode: container" : "Mode: interactive");

            // Enable OpenTelemetry tracing for visualization
            ConfigureObservability();

            await WorkflowCore.RunAsync(containerMode).ConfigureAwait(false);
        }
        catch (Exception e)
        {
            Console.WriteLine($"Critical error: {e}");
        }
    }

    private static void ConfigureObservability()
    {
        var otlpEndpoint =
            Environment.GetEnvironmentVariable("OTLP_ENDPOINT") ?? "http://localhost:4319";

        var resourceBuilder = OpenTelemetry
            .Resources.ResourceBuilder.CreateDefault()
            .AddService("MSLearnAgent.Lab02");

        s_tracerProvider = OpenTelemetry
            .Sdk.CreateTracerProviderBuilder()
            .SetResourceBuilder(resourceBuilder)
            .AddSource("Microsoft.Agents.AI.*") // All agent framework sources
            .SetSampler(new AlwaysOnSampler()) // Ensure all traces are sampled
            .AddOtlpExporter(options =>
            {
                options.Endpoint = new Uri(otlpEndpoint);
                options.Protocol = OpenTelemetry.Exporter.OtlpExportProtocol.Grpc;
            })
            .Build();

        Console.WriteLine($"OpenTelemetry configured. OTLP endpoint: {otlpEndpoint}");
    }
}
