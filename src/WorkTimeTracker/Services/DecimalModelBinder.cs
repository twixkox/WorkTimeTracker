using System.Globalization;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace WorkTimeTracker.Services;

/// <summary>
/// Биндер decimal/decimal?, принимающий и точку, и запятую как разделитель —
/// пользователи вводят «6,5» или «6.5».
/// </summary>
public class DecimalModelBinder : IModelBinder
{
    public Task BindModelAsync(ModelBindingContext bindingContext)
    {
        var valueProviderResult = bindingContext.ValueProvider.GetValue(bindingContext.ModelName);
        if (valueProviderResult == ValueProviderResult.None)
        {
            if (Nullable.GetUnderlyingType(bindingContext.ModelType) is not null)
                bindingContext.Result = ModelBindingResult.Success(null);
            return Task.CompletedTask;
        }

        bindingContext.ModelState.SetModelValue(bindingContext.ModelName, valueProviderResult);
        var raw = valueProviderResult.FirstValue;
        if (string.IsNullOrWhiteSpace(raw))
        {
            if (Nullable.GetUnderlyingType(bindingContext.ModelType) is not null)
                bindingContext.Result = ModelBindingResult.Success(null);
            return Task.CompletedTask;
        }

        var normalized = raw.Trim().Replace(',', '.');
        if (decimal.TryParse(normalized, NumberStyles.Number, CultureInfo.InvariantCulture, out var value))
        {
            bindingContext.Result = ModelBindingResult.Success(value);
        }
        else
        {
            bindingContext.ModelState.TryAddModelError(bindingContext.ModelName,
                $"Значение «{raw}» недопустимо. Введите число, например 6,5.");
        }
        return Task.CompletedTask;
    }
}

public class DecimalModelBinderProvider : IModelBinderProvider
{
    public IModelBinder? GetBinder(ModelBinderProviderContext context)
    {
        var type = context.Metadata.UnderlyingOrModelType;
        return type == typeof(decimal) ? new DecimalModelBinder() : null;
    }
}
