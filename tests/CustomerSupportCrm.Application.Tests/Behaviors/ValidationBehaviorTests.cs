using CustomerSupportCrm.Application.Behaviors;
using FluentValidation;

namespace CustomerSupportCrm.Application.Tests.Behaviors;

public sealed class ValidationBehaviorTests
{
    public sealed record SampleRequest(string? Name, int Count);

    [Fact]
    public async Task InvokesHandlerWhenRequestIsValid()
    {
        var behavior = new ValidationBehavior<SampleRequest, string>([NameRequired()]);
        var handlerCalled = false;

        var result = await behavior.Handle(new SampleRequest("ok", 1), _ =>
        {
            handlerCalled = true;
            return Task.FromResult("handled");
        }, CancellationToken.None);

        Assert.True(handlerCalled);
        Assert.Equal("handled", result);
    }

    [Fact]
    public async Task InvokesHandlerWhenNoValidatorsAreRegistered()
    {
        var behavior = new ValidationBehavior<SampleRequest, string>([]);

        var result = await behavior.Handle(new SampleRequest(null, 0), _ => Task.FromResult("handled"), CancellationToken.None);

        Assert.Equal("handled", result);
    }

    [Fact]
    public async Task CollectsFailuresFromAllValidatorsAndSkipsHandler()
    {
        var countPositive = new InlineValidator<SampleRequest>();
        countPositive.RuleFor(request => request.Count).GreaterThan(0);
        var behavior = new ValidationBehavior<SampleRequest, string>([NameRequired(), countPositive]);
        var handlerCalled = false;

        var exception = await Assert.ThrowsAsync<ValidationException>(() => behavior.Handle(new SampleRequest(null, 0), _ =>
        {
            handlerCalled = true;
            return Task.FromResult("handled");
        }, CancellationToken.None));

        Assert.False(handlerCalled);
        Assert.Equal(["Name", "Count"], exception.Errors.Select(failure => failure.PropertyName));
    }

    private static InlineValidator<SampleRequest> NameRequired()
    {
        var validator = new InlineValidator<SampleRequest>();
        validator.RuleFor(request => request.Name).NotEmpty();
        return validator;
    }
}
