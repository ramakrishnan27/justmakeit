using Platform.Helpers.Exceptions;

namespace Platform.Helpers.Validation;

/// <summary>
/// Collects per-field problems while a request is validated, then reports them together as one
/// <see cref="BadRequestException"/>. Field names are the JSON names the client sent, for example <c>email</c>.
/// </summary>
public sealed class ValidationErrors
{
    public const string Code = "validation_failed";

    private readonly Dictionary<string, List<string>> _errors = [];

    public bool Any => _errors.Count > 0;

    public ValidationErrors Add(string field, string message)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(field);
        ArgumentException.ThrowIfNullOrWhiteSpace(message);

        if (!_errors.TryGetValue(field, out var messages))
        {
            messages = [];
            _errors[field] = messages;
        }

        messages.Add(message);
        return this;
    }

    public IReadOnlyDictionary<string, string[]> ToDictionary() =>
        _errors.ToDictionary(pair => pair.Key, pair => pair.Value.ToArray());

    /// <summary>Throws a 400 listing every problem found, or does nothing when there were none.</summary>
    public void ThrowIfAny()
    {
        if (Any)
        {
            throw new BadRequestException("One or more fields are invalid.", Code, ToDictionary());
        }
    }
}
