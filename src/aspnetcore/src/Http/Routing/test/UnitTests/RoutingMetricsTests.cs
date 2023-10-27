// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Diagnostics;
using System.Diagnostics.Metrics;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing.Matching;
using Microsoft.AspNetCore.Routing.Patterns;
using Microsoft.AspNetCore.Routing.TestObjects;
using Microsoft.AspNetCore.Testing;
using Microsoft.Extensions.Diagnostics.Metrics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Telemetry.Testing.Metering;
using Moq;

namespace Microsoft.AspNetCore.Routing;

public class RoutingMetricsTests
{
    [Fact]
    public async Task Match_Success()
    {
        // Arrange
        var routeEndpointBuilder = new RouteEndpointBuilder(c => Task.CompletedTask, RoutePatternFactory.Parse("/{hi}"), order: 0);
        var meterFactory = new TestMeterFactory();
        var middleware = CreateMiddleware(
            matcherFactory: new TestMatcherFactory(true, c =>
            {
                c.SetEndpoint(routeEndpointBuilder.Build());
            }),
            meterFactory: meterFactory);
        var httpContext = new DefaultHttpContext();
        var meter = meterFactory.Meters.Single();

        using var routingMatchSuccessCollector = new MetricCollector<long>(meterFactory, RoutingMetrics.MeterName, "routing-match-success");
        using var routingMatchFailureCollector = new MetricCollector<long>(meterFactory, RoutingMetrics.MeterName, "routing-match-failure");

        // Act
        await middleware.Invoke(httpContext);

        // Assert
        Assert.Equal(RoutingMetrics.MeterName, meter.Name);
        Assert.Null(meter.Version);

        Assert.Collection(routingMatchSuccessCollector.GetMeasurementSnapshot(),
            m => AssertSuccess(m, "/{hi}", fallback: false));
        Assert.Empty(routingMatchFailureCollector.GetMeasurementSnapshot());
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Match_SuccessFallback_SetTagIfPresent(bool hasFallbackMetadata)
    {
        // Arrange
        var routeEndpointBuilder = new RouteEndpointBuilder(c => Task.CompletedTask, RoutePatternFactory.Parse("/{hi}"), order: 0);
        if (hasFallbackMetadata)
        {
            routeEndpointBuilder.Metadata.Add(FallbackMetadata.Instance);
        }
        var meterFactory = new TestMeterFactory();
        var middleware = CreateMiddleware(
            matcherFactory: new TestMatcherFactory(true, c =>
            {
                c.SetEndpoint(routeEndpointBuilder.Build());
            }),
            meterFactory: meterFactory);
        var httpContext = new DefaultHttpContext();
        var meter = meterFactory.Meters.Single();

        using var routingMatchSuccessCollector = new MetricCollector<long>(meterFactory, RoutingMetrics.MeterName, "routing-match-success");
        using var routingMatchFailureCollector = new MetricCollector<long>(meterFactory, RoutingMetrics.MeterName, "routing-match-failure");

        // Act
        await middleware.Invoke(httpContext);

        // Assert
        Assert.Equal(RoutingMetrics.MeterName, meter.Name);
        Assert.Null(meter.Version);

        Assert.Collection(routingMatchSuccessCollector.GetMeasurementSnapshot(),
            m => AssertSuccess(m, "/{hi}", fallback: hasFallbackMetadata));
        Assert.Empty(routingMatchFailureCollector.GetMeasurementSnapshot());
    }

    [Fact]
    public async Task Match_Success_MissingRoute()
    {
        // Arrange
        var meterFactory = new TestMeterFactory();
        var middleware = CreateMiddleware(
            matcherFactory: new TestMatcherFactory(true, c =>
            {
                c.SetEndpoint(new Endpoint(c => Task.CompletedTask, EndpointMetadataCollection.Empty, "Test name"));
            }),
            meterFactory: meterFactory);
        var httpContext = new DefaultHttpContext();
        var meter = meterFactory.Meters.Single();

        using var routingMatchSuccessCollector = new MetricCollector<long>(meterFactory, RoutingMetrics.MeterName, "routing-match-success");
        using var routingMatchFailureCollector = new MetricCollector<long>(meterFactory, RoutingMetrics.MeterName, "routing-match-failure");

        // Act
        await middleware.Invoke(httpContext);

        // Assert
        Assert.Equal(RoutingMetrics.MeterName, meter.Name);
        Assert.Null(meter.Version);

        Assert.Collection(routingMatchSuccessCollector.GetMeasurementSnapshot(),
            m => AssertSuccess(m, "(missing)", fallback: false));
        Assert.Empty(routingMatchFailureCollector.GetMeasurementSnapshot());
    }

    [Fact]
    public async Task Match_Failure()
    {
        // Arrange
        var meterFactory = new TestMeterFactory();
        var middleware = CreateMiddleware(
            matcherFactory: new TestMatcherFactory(false),
            meterFactory: meterFactory);
        var httpContext = new DefaultHttpContext();
        var meter = meterFactory.Meters.Single();

        using var routingMatchSuccessCollector = new MetricCollector<long>(meterFactory, RoutingMetrics.MeterName, "routing-match-success");
        using var routingMatchFailureCollector = new MetricCollector<long>(meterFactory, RoutingMetrics.MeterName, "routing-match-failure");

        // Act
        await middleware.Invoke(httpContext);

        // Assert
        Assert.Equal(RoutingMetrics.MeterName, meter.Name);
        Assert.Null(meter.Version);

        Assert.Empty(routingMatchSuccessCollector.GetMeasurementSnapshot());
        Assert.Collection(routingMatchFailureCollector.GetMeasurementSnapshot(),
            m => Assert.Equal(1, m.Value));
    }

    private void AssertSuccess(CollectedMeasurement<long> measurement, string route, bool fallback)
    {
        Assert.Equal(1, measurement.Value);
        Assert.Equal(route, (string)measurement.Tags["route"]);
        Assert.Equal(fallback, (bool)measurement.Tags["fallback"]);
    }

    private EndpointRoutingMiddleware CreateMiddleware(
        ILogger<EndpointRoutingMiddleware> logger = null,
        MatcherFactory matcherFactory = null,
        DiagnosticListener listener = null,
        IMeterFactory meterFactory = null,
        RequestDelegate next = null)
    {
        next ??= c => Task.CompletedTask;
        logger ??= new Logger<EndpointRoutingMiddleware>(NullLoggerFactory.Instance);
        matcherFactory ??= new TestMatcherFactory(true);
        listener ??= new DiagnosticListener("Microsoft.AspNetCore");
        var metrics = new RoutingMetrics(meterFactory ?? new TestMeterFactory());

        var middleware = new EndpointRoutingMiddleware(
            matcherFactory,
            logger,
            new DefaultEndpointRouteBuilder(Mock.Of<IApplicationBuilder>()),
            new DefaultEndpointDataSource(),
            listener,
            Options.Create(new RouteOptions()),
            metrics,
            next);

        return middleware;
    }
}
