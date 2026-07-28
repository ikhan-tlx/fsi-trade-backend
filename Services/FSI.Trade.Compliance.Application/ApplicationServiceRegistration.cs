using System.Reflection;
using FluentValidation;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using FSI.Trade.Compliance.Application.Common.Behaviours;
using FSI.Trade.Compliance.Application.Features.Requests.Common;

namespace FSI.Trade.Compliance.Application;

public static class ApplicationServiceRegistration
{
    public static IServiceCollection AddApplicationServices(this IServiceCollection services)
    {
        var asm = Assembly.GetExecutingAssembly();

        services.AddMediatR(cfg => cfg.RegisterServicesFromAssembly(asm));
        services.AddValidatorsFromAssembly(asm);
        services.AddTransient(typeof(IPipelineBehavior<,>), typeof(ValidationBehaviour<,>));

        // Maker/checker replay registry (RequestType+Action → the command to replay on approve).
        services.AddSingleton<IApprovalReplayRegistry, ApprovalReplayRegistry>();

        return services;
    }
}
