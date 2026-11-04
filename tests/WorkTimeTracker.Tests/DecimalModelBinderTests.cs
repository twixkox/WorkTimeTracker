using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using WorkTimeTracker.Services;
using Xunit;

namespace WorkTimeTracker.Tests;

public class DecimalModelBinderTests
{
    private static (ModelBindingContext Context, ActionContext ActionContext) CreateContext(string? raw)
    {
        var metadata = new EmptyModelMetadataProvider()
            .GetMetadataForType(typeof(decimal?));
        var actionContext = new ActionContext(
            new Microsoft.AspNetCore.Http.DefaultHttpContext(),
            new Microsoft.AspNetCore.Routing.RouteData(),
            new ActionDescriptor());
        var valueProvider = new Microsoft.AspNetCore.Mvc.ModelBinding.FormValueProvider(
            Microsoft.AspNetCore.Mvc.ModelBinding.BindingSource.Form,
            new Microsoft.AspNetCore.Http.FormCollection(
                raw is null ? new Dictionary<string, Microsoft.Extensions.Primitives.StringValues>()
                           : new Dictionary<string, Microsoft.Extensions.Primitives.StringValues> { ["Hours"] = raw }),
            System.Globalization.CultureInfo.GetCultureInfo("ru-RU"));
        var context = new DefaultModelBindingContext
        {
            ActionContext = actionContext,
            ModelMetadata = metadata,
            ModelName = "Hours",
            ModelState = actionContext.ModelState,
            ValueProvider = valueProvider,
        };
        return (context, actionContext);
    }

    [Theory]
    [InlineData("6.5", 6.5)]
    [InlineData("6,5", 6.5)]
    [InlineData("0,25", 0.25)]
    public async Task Binds_BothSeparators(string raw, decimal expected)
    {
        var provider = new DecimalModelBinderProvider();
        var binder = provider.GetBinder(new TestProviderContext(typeof(decimal?)))!;
        var (context, _) = CreateContext(raw);
        await binder.BindModelAsync(context);
        Assert.True(context.Result.IsModelSet);
        Assert.Equal(expected, context.Result.Model);
    }

    [Fact]
    public async Task Empty_Nullable_Binds_Null()
    {
        var provider = new DecimalModelBinderProvider();
        var binder = provider.GetBinder(new TestProviderContext(typeof(decimal?)))!;
        var (context, _) = CreateContext(null);
        await binder.BindModelAsync(context);
        Assert.True(context.Result.IsModelSet);
        Assert.Null(context.Result.Model);
    }

    private class TestProviderContext : ModelBinderProviderContext
    {
        private readonly ModelMetadata _metadata;
        public TestProviderContext(Type modelType)
        {
            _metadata = new EmptyModelMetadataProvider().GetMetadataForType(modelType);
        }
        public override BindingInfo BindingInfo => new() { };
        public override ModelMetadata Metadata => _metadata;
        public override IModelMetadataProvider MetadataProvider => new EmptyModelMetadataProvider();
        public override IModelBinder CreateBinder(ModelMetadata metadata) => new DecimalModelBinder();
    }
}
