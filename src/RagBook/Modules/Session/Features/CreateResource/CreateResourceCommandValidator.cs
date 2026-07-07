using FluentValidation;

namespace RagBook.Modules.Session.Features.CreateResource;

/// <summary>Validates <see cref="CreateResourceCommand"/> in the dispatch pipeline (→ 400 on failure).</summary>
public sealed class CreateResourceCommandValidator : AbstractValidator<CreateResourceCommand>
{
    /// <summary>Configures the rules.</summary>
    public CreateResourceCommandValidator()
    {
        RuleFor(command => command.Name)
            .NotEmpty()
            .MaximumLength(CreateResourceCommand.MaxNameLength);
    }
}
