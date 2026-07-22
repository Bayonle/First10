using First10.Api.Filters;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NSubstitute;

namespace First10.Tests;

/// <summary>
/// D-006 hard gate: the local chat cockpit must be unreachable outside Development.
/// An exposed fake-injection endpoint is a report-forging vector (R11) — if these
/// tests start failing, stop and fix the gate before anything else.
/// </summary>
public class DevelopmentOnlyAttributeTests
{
    [Theory]
    [InlineData("Production")]
    [InlineData("Staging")]
    [InlineData("Testing")]
    public void Blocks_any_non_development_environment(string environmentName)
    {
        var context = BuildContext(environmentName);

        new DevelopmentOnlyAttribute().OnResourceExecuting(context);

        Assert.IsType<NotFoundResult>(context.Result);
    }

    [Fact]
    public void Allows_development()
    {
        var context = BuildContext(Environments.Development);

        new DevelopmentOnlyAttribute().OnResourceExecuting(context);

        Assert.Null(context.Result);
    }

    private static ResourceExecutingContext BuildContext(string environmentName)
    {
        var env = Substitute.For<IHostEnvironment>();
        env.EnvironmentName.Returns(environmentName);

        var services = new ServiceCollection();
        services.AddSingleton(env);

        var httpContext = new DefaultHttpContext
        {
            RequestServices = services.BuildServiceProvider(),
        };

        var actionContext = new ActionContext(httpContext, new RouteData(), new ActionDescriptor());
        return new ResourceExecutingContext(actionContext, [], []);
    }
}
