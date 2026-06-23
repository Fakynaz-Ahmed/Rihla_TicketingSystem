using Microsoft.OpenApi.Any;
using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace Rihla.Filters;

public class LanguageHeaderOperationFilter : IOperationFilter
{
    public void Apply(OpenApiOperation operation, OperationFilterContext context)
    {
        operation.Parameters ??= new List<OpenApiParameter>();
        operation.Parameters.Add(new OpenApiParameter
        {
            Name        = "X-Language",
            In          = ParameterLocation.Header,
            Required    = false,
            Description = "Response language: en (default) or ar.",
            Schema = new OpenApiSchema
            {
                Type    = "string",
                Enum    = [new OpenApiString("en"), new OpenApiString("ar")],
                Default = new OpenApiString("en")
            }
        });
    }
}
