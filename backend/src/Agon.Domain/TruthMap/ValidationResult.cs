namespace Agon.Domain.TruthMap;

/// <summary>
/// Result of patch validation — either success or a list of errors.
/// </summary>
public class ValidationResult
{
    public bool IsValid { get; }
    public IReadOnlyList<string> Errors { get; }

    private ValidationResult(bool isValid, IReadOnlyList<string> errors)
    {
        IsValid = isValid;
        Errors = errors;
    }

    public static ValidationResult Success() => new(true, Array.Empty<string>());

    public static ValidationResult Failure(IReadOnlyList<string> errors) => new(false, errors);
}
